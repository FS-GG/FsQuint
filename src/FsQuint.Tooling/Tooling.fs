namespace FsQuint.Tooling

open System
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open FS.GG.Execution.Contracts
open FS.GG.Execution.Processes

[<RequireQualifiedAccess>]
type QuintCommand =
    | Typecheck
    | Test of pattern: string
    | Run of samples: int * steps: int * seed: string * invariant: string

type QuintRequest =
    {
        Executable: string
        Sha256: string
        Version: string
        WorkingDirectory: string
        Source: string
        Module: string
        Command: QuintCommand
        Timeout: TimeSpan
        MaxOutputBytes: int
    }

[<RequireQualifiedAccess>]
type ToolOutcome =
    | Typechecked
    | TestsPassed of count: int
    | SamplesPassed
    | Counterexample
    | Failed of exitCode: int
    | Incomplete
    | IdentityMismatch
    | Unsupported
    | LaunchFailure of message: string
    | TimedOut
    | Cancelled
    | OutputLimit

type ToolReport =
    {
        Outcome: ToolOutcome
        StandardOutput: string
        StandardError: string
    }

[<RequireQualifiedAccess>]
module Quint =
    type private SessionSlot =
        | Vacant
        | Claimed
        | Retained of ITextProcessSession

    // Root-selected admission bound. No detached reaper or renewed budget.
    let private custodyGate = obj()
    let private custody = Array.create 16 Vacant
    let private factory =
        ProcessSessions.createTextFactoryWithRetirement RetirementPolicy.BestEffortProcessTree

    let private releaseSlot slot (session: ITextProcessSession) =
        lock custodyGate (fun () ->
            let stillOwnsSlot () =
                match custody[slot] with
                | Retained current -> Object.ReferenceEquals(current, session)
                | _ -> false

            try
                match session.TryRelease() with
                | ResourcesReleased ->
                    if stillOwnsSlot () then custody[slot] <- Vacant
                    true
                | ReleaseRefused _ ->
                    if stillOwnsSlot () then custody[slot] <- Retained session
                    false
            with _ ->
                if stillOwnsSlot () then custody[slot] <- Retained session
                false)

    let private claimSlot () =
        lock custodyGate (fun () ->
            for index in 0 .. custody.Length - 1 do
                match custody[index] with
                | Retained session -> releaseSlot index session |> ignore
                | _ -> ()

            match custody |> Array.tryFindIndex (function Vacant -> true | _ -> false) with
            | Some index ->
                custody[index] <- Claimed
                Some index
            | None -> None)

    let private createRequestClock () =
        let domain: ClockDomain = { Id = Guid.NewGuid(); Frequency = Stopwatch.Frequency }
        { new IMonotonicClock with
            member _.Now() = { Domain = domain; Ticks = Stopwatch.GetTimestamp() } }

    let private executeText
        path
        workingDirectory
        arguments
        sourceDigest
        ordinal
        maxBytes
        (clock: IMonotonicClock)
        (originalBudget: ExecutionBudget)
        (cancellation: CancellationToken)
        =
        task {
            let remaining = Budget.remaining (clock.Now()) originalBudget

            match remaining with
            | Ok view when cancellation.IsCancellationRequested ->
                return ToolOutcome.Cancelled, "", ""
            | Ok view when view.WorkExpired ->
                return ToolOutcome.TimedOut, "", ""
            | Error _ ->
                return ToolOutcome.LaunchFailure "Original process budget observation refused.", "", ""
            | _ ->
                match claimSlot () with
                | None ->
                    return
                        ToolOutcome.LaunchFailure "Process custody capacity is occupied by unresolved invocations.", "", ""
                | Some slot ->
                    let mutable retained: ITextProcessSession option = None
                    let mutable ownsSlot = true
                    let mutable firstOutcome: ToolOutcome option = None

                    try
                        try
                            let identity =
                                { InvocationId = Guid.NewGuid()
                                  Execution = None
                                  LaunchOrdinal = ordinal
                                  SourceDigest = sourceDigest
                                  ProfileId = "fsquint-text-v1"
                                  ProfileDigest =
                                    SHA256.HashData(Encoding.UTF8.GetBytes "FsQuint.Tooling/text-v1/best-effort-tree")
                                    |> Convert.ToHexStringLower }

                            let owner =
                                { OwnerId = "fsquint-" + identity.InvocationId.ToString("N")
                                  Evidence =
                                    { Kind = "local-session"
                                      Reference = "fsquint:" + identity.InvocationId.ToString("D")
                                      Digest = None } }

                            // ProcessStartInfo previously accepted relative/default cwd. Normalize
                            // that same choice instead of silently imposing a new absolute-path rule.
                            let cwd =
                                if String.IsNullOrEmpty workingDirectory then Directory.GetCurrentDirectory()
                                else Path.GetFullPath workingDirectory

                            let command: TextCommandSpec =
                                { Executable = path
                                  Arguments = arguments
                                  WorkingDirectory = cwd
                                  Environment = Inherit(Map.ofList [ "NO_COLOR", Some "1" ])
                                  Input = Closed
                                  TextCapture =
                                    { MaximumDecodedUtf8Bytes = int64 maxBytes
                                      TerminalReserveBytes = Validation.limits.FirstCauseReserveBytes }
                                  Capabilities =
                                    [ { Capability = BoundedReturnWithRetainedCustody; Required = true } ]
                                  Resources =
                                    { MemoryBytes = None
                                      CpuQuota = None
                                      CpuAffinity = None
                                      MaxKernelTasks = None
                                      MaxOwnedProcesses = None
                                      MaxClrProcesses = None } }

                            match factory.Prepare(command, identity, originalBudget, clock, owner) with
                            | Error errors when errors |> List.exists (fun error -> error.Field = "budget") ->
                                return ToolOutcome.TimedOut, "", ""
                            | Error _ ->
                                return ToolOutcome.LaunchFailure "Process session preparation refused.", "", ""
                            | Ok session ->
                                // Establish bounded in-process ownership before the sole Run call.
                                retained <- Some session
                                lock custodyGate (fun () -> custody[slot] <- Retained session)
                                let! initial = session.RunAsync cancellation
                                let rememberStop (result: TextExecutionResult) =
                                    if firstOutcome.IsNone then
                                        firstOutcome <-
                                            match result.Process.Stop with
                                            | Requested(OutputLimit, _) -> Some ToolOutcome.OutputLimit
                                            | Requested(CallerCancellation, _) -> Some ToolOutcome.Cancelled
                                            | Requested(WorkDeadline, _) -> Some ToolOutcome.TimedOut
                                            | Requested _ | StopRefused _ -> Some ToolOutcome.Incomplete
                                            | _ -> None

                                rememberStop initial
                                let mutable latest = initial
                                let mutable observing = true

                                let settled (result: TextExecutionResult) =
                                    match result.Process.Cleanup.LocalTasks with
                                    | Known(Settled, _) -> true
                                    | _ -> false

                                while observing do
                                    let! observed = session.ObserveAsync cancellation
                                    latest <- observed
                                    rememberStop observed

                                    if settled latest then
                                        observing <- false
                                    else
                                        match Budget.clip (clock.Now()) Cleanup (TimeSpan.FromMilliseconds 5.) originalBudget with
                                        | Ok delay when delay > TimeSpan.Zero -> do! Task.Delay delay
                                        | _ -> observing <- false

                                // Read the atomic process/text snapshot once more before release;
                                // never use stale Run output to classify final EOF/limit/exit races.
                                let! final = session.ObserveAsync cancellation
                                rememberStop final
                                let released = releaseSlot slot session
                                if released then ownsSlot <- false
                                let output = String.Concat(final.Text.Stdout.Chunks :> seq<string>)
                                let errors = String.Concat(final.Text.Stderr.Chunks :> seq<string>)

                                let stop =
                                    match initial.Process.Stop with
                                    | Requested _ as original -> original
                                    | _ -> final.Process.Stop

                                let eof (capture: TextStreamCapture) =
                                    match capture.Reader with
                                    | Known(Eof, _) -> true
                                    | _ -> false

                                let completeText =
                                    final.Text.SnapshotComplete
                                    && final.Text.FirstFailure.IsNone
                                    && final.Text.Stdout.OmittedDecodedUtf8Bytes = 0L
                                    && final.Text.Stderr.OmittedDecodedUtf8Bytes = 0L
                                    && eof final.Text.Stdout
                                    && eof final.Text.Stderr
                                    && final.Process.FirstFailure.IsNone
                                    && final.Process.Violations.IsEmpty

                                let outcome =
                                    match stop with
                                    | Requested(OutputLimit, _) -> ToolOutcome.OutputLimit
                                    | Requested(CallerCancellation, _) -> ToolOutcome.Cancelled
                                    | Requested(WorkDeadline, _) -> ToolOutcome.TimedOut
                                    | Requested _ | StopRefused _ -> ToolOutcome.Incomplete
                                    | NotRequested ->
                                        match final.Process.Launch with
                                        | Refused diagnostic | Ambiguous diagnostic ->
                                            ToolOutcome.LaunchFailure diagnostic.Message
                                        | _ ->
                                            match final.Process.Exit with
                                            | Known(Exited code, _) when completeText && released && settled final ->
                                                ToolOutcome.Failed code
                                            | _ -> ToolOutcome.Incomplete

                                return outcome, output, errors
                        with _ ->
                            return
                                Option.defaultValue (ToolOutcome.LaunchFailure "Process execution observation failed.") firstOutcome, "", ""
                    finally
                        if ownsSlot then
                            match retained with
                            | Some session -> releaseSlot slot session |> ignore
                            | None -> lock custodyGate (fun () -> custody[slot] <- Vacant)
        }

    /// Linux x64, caller-provisioned Quint 0.32.0, Rust backend. Never downloads tools.
    let private runInternal
        (request: QuintRequest)
        (hashCancellation: CancellationToken)
        (callerCancellation: CancellationToken)
        (clock: IMonotonicClock)
        (originalBudget: ExecutionBudget)
        =
        task {
            let report status output errors =
                {
                    Outcome = status
                    StandardOutput = output
                    StandardError = errors
                }

            if
                not (OperatingSystem.IsLinux())
                || Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                   <> Runtime.InteropServices.Architecture.X64
                || request.Version <> "0.32.0"
            then
                return report ToolOutcome.Unsupported "" ""
            elif
                request.Timeout <= TimeSpan.Zero
                || request.MaxOutputBytes <= 0
                || not (Path.IsPathFullyQualified request.Executable)
            then
                return report (ToolOutcome.LaunchFailure "Invalid path or child limits.") "" ""
            else
                try
                    use stream = File.OpenRead request.Executable
                    let! digest = SHA256.HashDataAsync(stream, hashCancellation)

                    if Convert.ToHexStringLower(digest) <> request.Sha256 then
                        return report ToolOutcome.IdentityMismatch "" ""
                    else
                        let! version, stdout, stderr =
                            executeText
                                request.Executable
                                request.WorkingDirectory
                                [ "--version" ]
                                request.Sha256
                                0UL
                                request.MaxOutputBytes
                                clock
                                originalBudget
                                callerCancellation

                        if version <> ToolOutcome.Failed 0 then
                            return report version stdout stderr
                        elif stdout.Trim() <> request.Version then
                            return report ToolOutcome.IdentityMismatch stdout stderr
                        else
                            let args =
                                match request.Command with
                                | QuintCommand.Typecheck -> [ "typecheck"; request.Source ]
                                | QuintCommand.Test pattern ->
                                    [
                                        "test"
                                        request.Source
                                        "--main"
                                        request.Module
                                        "--match"
                                        pattern
                                        "--backend"
                                        "rust"
                                    ]
                                | QuintCommand.Run(samples, steps, seed, invariant) ->
                                    [
                                        "run"
                                        request.Source
                                        "--main"
                                        request.Module
                                        "--max-samples"
                                        string samples
                                        "--max-steps"
                                        string steps
                                        "--seed"
                                        seed
                                        "--invariant"
                                        invariant
                                        "--backend"
                                        "rust"
                                    ]

                            let! result, output, errors =
                                executeText
                                    request.Executable
                                    request.WorkingDirectory
                                    args
                                    request.Sha256
                                    1UL
                                    request.MaxOutputBytes
                                    clock
                                    originalBudget
                                    callerCancellation

                            let status =
                                match result with
                                | ToolOutcome.Failed 0 ->
                                    match request.Command with
                                    | QuintCommand.Typecheck -> ToolOutcome.Typechecked
                                    | QuintCommand.Test _ ->
                                        let matched = Regex.Match(output, @"(?m)^\s*([1-9][0-9]*) passing(?:\s|$)")

                                        if matched.Success then
                                            ToolOutcome.TestsPassed(Int32.Parse(matched.Groups[1].Value))
                                        else
                                            ToolOutcome.Incomplete
                                    | QuintCommand.Run(samples, steps, _, _) ->
                                        if
                                            samples > 0
                                            && steps > 0
                                            && output.Contains("[ok] No violation found")
                                            && output.Contains("Trace length statistics:")
                                        then
                                            ToolOutcome.SamplesPassed
                                        else
                                            ToolOutcome.Incomplete
                                | ToolOutcome.Failed code when
                                    code <> 0
                                    && output.Contains("[violation]")
                                    && errors.Contains("Invariant violated")
                                    ->
                                    ToolOutcome.Counterexample
                                | other -> other

                            return report status output errors
                with
                | :? OperationCanceledException -> return report ToolOutcome.Cancelled "" ""
                | ex -> return report (ToolOutcome.LaunchFailure ex.Message) "" ""
        }

    let run (request: QuintRequest) (cancellation: CancellationToken) =
        task {
            if
                request.Timeout <= TimeSpan.Zero
                || request.Timeout.TotalMilliseconds > float UInt32.MaxValue - 1.0
            then
                return
                    {
                        Outcome = ToolOutcome.LaunchFailure "Invalid total timeout."
                        StandardOutput = ""
                        StandardError = ""
                    }
            else
                let clock = createRequestClock ()
                let startedAt = clock.Now()
                let cleanupGrace = TimeSpan.FromSeconds 1.
                let totalTicks = bigint request.Timeout.Ticks + bigint cleanupGrace.Ticks

                if totalTicks > bigint Int64.MaxValue then
                    return
                        { Outcome = ToolOutcome.LaunchFailure "Invalid process budget arithmetic."
                          StandardOutput = ""
                          StandardError = "" }
                else
                    let total = TimeSpan.FromTicks(int64 totalTicks)

                    match Budget.create startedAt total cleanupGrace with
                    | Error _ ->
                        return
                            { Outcome = ToolOutcome.LaunchFailure "Original process budget preparation refused."
                              StandardOutput = ""
                              StandardError = "" }
                    | Ok originalBudget ->
                        // Hash cancellation uses the already-started work window. Session
                        // calls receive only caller cancellation; the producer owns work expiry
                        // and must preserve an on-time completion observed after that expiry.
                        use workTimeout = new CancellationTokenSource()

                        match Budget.remaining (clock.Now()) originalBudget with
                        | Ok view when not view.WorkExpired -> workTimeout.CancelAfter view.WorkRemaining
                        | _ -> workTimeout.Cancel()

                        use linked =
                            CancellationTokenSource.CreateLinkedTokenSource(cancellation, workTimeout.Token)

                        let! report = runInternal request linked.Token cancellation clock originalBudget

                        if
                            report.Outcome = ToolOutcome.Cancelled
                            && not cancellation.IsCancellationRequested
                            && workTimeout.IsCancellationRequested
                        then
                            return { report with Outcome = ToolOutcome.TimedOut }
                        else
                            return report
        }
