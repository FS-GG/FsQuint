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
    let private execute path workingDirectory arguments timeout maxBytes (cancellation: CancellationToken) =
        task {
            use deadline = new CancellationTokenSource(timeout: TimeSpan)

            use linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token)

            use child = new Process()

            let info =
                ProcessStartInfo(
                    path,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDirectory
                )

            for arg in arguments do
                info.ArgumentList.Add(arg)

            info.Environment["NO_COLOR"] <- "1"
            child.StartInfo <- info
            let mutable count = 0
            let mutable overflow = false
            let output = StringBuilder()
            let errors = StringBuilder()

            let read (reader: StreamReader) (builder: StringBuilder) =
                task {
                    let buffer = Array.zeroCreate<char> 1024
                    let mutable more = true

                    while more do
                        let! size = reader.ReadAsync(buffer.AsMemory(), linked.Token)

                        if size = 0 then
                            more <- false
                        else
                            let bytes = Encoding.UTF8.GetByteCount(buffer, 0, size)

                            if Interlocked.Add(&count, bytes) > maxBytes then
                                overflow <- true
                                linked.Cancel()
                            else
                                builder.Append(buffer, 0, size) |> ignore
                }

            let mutable started = false

            try
                started <- child.Start()

                if not started then
                    return ToolOutcome.LaunchFailure "Process did not start.", "", ""
                else
                    let stdout = read child.StandardOutput output
                    let stderr = read child.StandardError errors

                    try
                        do! Task.WhenAll(stdout, stderr, child.WaitForExitAsync(linked.Token))
                        return ToolOutcome.Failed child.ExitCode, output.ToString(), errors.ToString()
                    with :? OperationCanceledException ->
                        if not child.HasExited then
                            child.Kill(true)

                        do! child.WaitForExitAsync()

                        try
                            do! Task.WhenAll([| stdout :> Task; stderr :> Task |])
                        with _ ->
                            ()

                        let status =
                            if overflow then
                                ToolOutcome.OutputLimit
                            elif cancellation.IsCancellationRequested then
                                ToolOutcome.Cancelled
                            else
                                ToolOutcome.TimedOut

                        return status, output.ToString(), errors.ToString()
            with ex ->
                if started && not child.HasExited then
                    child.Kill(true)
                    do! child.WaitForExitAsync()

                return ToolOutcome.LaunchFailure ex.Message, output.ToString(), errors.ToString()
        }

    /// Linux x64, caller-provisioned Quint 0.32.0, Rust backend. Never downloads tools.
    let private runInternal (request: QuintRequest) (cancellation: CancellationToken) =
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
                    let! digest = SHA256.HashDataAsync(stream, cancellation)

                    if Convert.ToHexStringLower(digest) <> request.Sha256 then
                        return report ToolOutcome.IdentityMismatch "" ""
                    else
                        let! version, stdout, stderr =
                            execute
                                request.Executable
                                request.WorkingDirectory
                                [ "--version" ]
                                request.Timeout
                                request.MaxOutputBytes
                                cancellation

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
                                execute
                                    request.Executable
                                    request.WorkingDirectory
                                    args
                                    request.Timeout
                                    request.MaxOutputBytes
                                    cancellation

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
                use budget = new CancellationTokenSource(request.Timeout)

                use linked =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellation, budget.Token)

                let! report = runInternal request linked.Token

                if
                    report.Outcome = ToolOutcome.Cancelled
                    && not cancellation.IsCancellationRequested
                    && budget.IsCancellationRequested
                then
                    return
                        { report with
                            Outcome = ToolOutcome.TimedOut
                        }
                else
                    return report
        }
