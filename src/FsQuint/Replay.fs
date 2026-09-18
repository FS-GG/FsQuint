namespace FsQuint

open System
open System.Threading
open System.Threading.Tasks

/// Cooperative lifecycle. Apply is called once; cleanup receives a fresh token.
type ReplayDriver<'runtime> =
    {
        Initialize: QuintReplayState -> CancellationToken -> Task<Result<'runtime, string>>
        Apply: QuintReplayStep -> 'runtime -> CancellationToken -> Task<Result<unit, string>>
        Observe: 'runtime -> CancellationToken -> Task<Result<QuintReplayState, string>>
        Cleanup: 'runtime -> CancellationToken -> Task<Result<unit, string>>
    }

[<RequireQualifiedAccess>]
type ReplayOutcome =
    | Equivalent
    | Diverged of
        step: int *
        action: string option *
        source: QuintReplaySourceBinding option *
        path: string *
        expected: QuintReplayState *
        actual: QuintReplayState
    | InvalidTrace of QuintReplayDiagnostic list
    | DriverFailure of phase: string * step: int * message: string
    | Cancelled
    | TimedOut

type ReplayReport =
    {
        Outcome: ReplayOutcome
        CleanupFailure: string option
        AppliedSteps: int
    }

[<RequireQualifiedAccess>]
module Replay =
    /// Deadline is cooperative: use process isolation for untrusted/non-cooperative drivers.
    let run
        (timeout: TimeSpan)
        (cancellation: CancellationToken)
        (driver: ReplayDriver<'runtime>)
        (trace: QuintReplayTrace)
        =
        task {
            if
                timeout <= TimeSpan.Zero
                || timeout.TotalMilliseconds > float UInt32.MaxValue - 1.0
            then
                invalidArg "timeout" "Supply a positive finite timeout supported by CancellationTokenSource."

            use deadline = new CancellationTokenSource(timeout)

            use linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token)

            let token = linked.Token
            let mutable runtime = None
            let mutable phase = "validate"
            let mutable index = 0
            let mutable applied = 0
            let mutable outcome = ReplayOutcome.Equivalent
            let mutable cleanupFailure = None

            let fail message =
                outcome <- ReplayOutcome.DriverFailure(phase, index, message)

            let isEquivalent () = outcome = ReplayOutcome.Equivalent

            let observe expected action source =
                task {
                    phase <- "observe"
                    token.ThrowIfCancellationRequested()
                    let! observation = driver.Observe runtime.Value token
                    token.ThrowIfCancellationRequested()

                    match observation with
                    | Error message -> fail message
                    | Ok actual ->
                        match QuintReplay.encodeState expected, QuintReplay.encodeState actual with
                        | _, Error diagnostics -> fail (sprintf "Invalid observed state: %A" diagnostics)
                        | Ok a, Ok b when a = b -> ()
                        | Ok _, Ok _ ->
                            let names =
                                (expected.Bindings @ actual.Bindings)
                                |> List.map fst
                                |> List.distinct
                                |> List.sort

                            let differing =
                                names
                                |> List.tryFind (fun name ->
                                    let encoded bindings =
                                        bindings
                                        |> List.tryFind (fst >> (=) name)
                                        |> Option.map (snd >> QuintReplay.encodeValue)

                                    encoded expected.Bindings <> encoded actual.Bindings)

                            let path =
                                differing
                                |> Option.map (fun n -> "$/bindings/" + n.Replace("~", "~0").Replace("/", "~1"))
                                |> Option.defaultValue "$"

                            outcome <- ReplayOutcome.Diverged(index, action, source, path, expected, actual)
                        | Error diagnostics, _ -> outcome <- ReplayOutcome.InvalidTrace diagnostics
                }

            try
                match QuintReplay.validateTrace trace with
                | errors when not errors.IsEmpty -> outcome <- ReplayOutcome.InvalidTrace errors
                | _ ->
                    token.ThrowIfCancellationRequested()
                    phase <- "initialize"
                    let! initialized = driver.Initialize trace.Initial token

                    match initialized with
                    | Error message -> fail message
                    | Ok value ->
                        runtime <- Some value
                        do! observe trace.Initial None None

                        for step in trace.Steps do
                            if isEquivalent () then
                                token.ThrowIfCancellationRequested()
                                index <- step.Index
                                phase <- "apply"
                                applied <- applied + 1
                                let! result = driver.Apply step value token
                                token.ThrowIfCancellationRequested()

                                match result with
                                | Error message -> fail message
                                | Ok() -> do! observe step.Expected (Some step.Action) (Some step.Source)
            with
            | :? OperationCanceledException when cancellation.IsCancellationRequested ->
                outcome <- ReplayOutcome.Cancelled
            | :? OperationCanceledException when deadline.IsCancellationRequested -> outcome <- ReplayOutcome.TimedOut
            | ex -> fail ex.Message

            match runtime with
            | None -> ()
            | Some value ->
                try
                    let! cleaned = driver.Cleanup value CancellationToken.None

                    match cleaned with
                    | Ok() -> ()
                    | Error message -> cleanupFailure <- Some message
                with ex ->
                    cleanupFailure <- Some ex.Message

            return
                {
                    Outcome = outcome
                    CleanupFailure = cleanupFailure
                    AppliedSteps = applied
                }
        }
