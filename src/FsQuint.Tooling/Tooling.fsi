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
    /// Run a caller-provisioned, digest-checked Quint 0.32.0 on Linux x64.
    /// Test/run outcomes require positive evidence; no verification claim is made.
    val run: request: QuintRequest -> cancellation: CancellationToken -> Task<ToolReport>
