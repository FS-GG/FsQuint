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
    /// Replay once, stopping at the first mismatch. Cancellation and timeout are cooperative.
    /// Cleanup is attempted after successful initialization; its failure is retained separately.
    val run:
        timeout: TimeSpan ->
        cancellation: CancellationToken ->
        driver: ReplayDriver<'runtime> ->
        trace: QuintReplayTrace ->
            Task<ReplayReport>
