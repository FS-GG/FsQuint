namespace FsQuint.Tooling

open System
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

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
    /// <summary>Run caller-provisioned, digest-checked Quint 0.32.0 under the 0.2 bounded Linux x64 batch profile.</summary>
    /// <param name="request">Executable and normalized working paths must each fit 2048 UTF8 bytes;
    /// combined executable, directory, arguments and explicit environment metadata must fit 256 KiB.
    /// Child stdin is closed. Any positive MaxOutputBytes bounds shared decoded stdout/stderr
    /// separately per invocation. Timeout covers hashing, version selection and command work.</param>
    /// <param name="cancellation">Caller cancellation; the original work deadline is never renewed.</param>
    /// <returns>Positive test/run outcomes require complete evidence and accepted local resource release.</returns>
    /// <remarks>Cleanup observation has a one-second budget beyond the full work deadline, without
    /// a hard wall-clock return guarantee. Tree termination is best effort; up to sixteen unresolved
    /// sessions retain local custody. Exhausted custody or request admission returns launch failure.
    /// A version probe may precede workload refusal. Reports do not prove descendant emptiness,
    /// reaping or verification. These request and stdin rules narrow the 0.1 wrapper's behavior.</remarks>
    val run: request: QuintRequest -> cancellation: CancellationToken -> Task<ToolReport>
