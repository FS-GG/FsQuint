open System
open System.Diagnostics
open System.Threading
open FsQuint.Tooling

// This program is the cheap gate. The caller starts the costly command only on exit 0.
[<EntryPoint>]
let main args =
    if args <> [| "valid" |] && args <> [| "broken" |] then
        eprintfn "Usage: CiPreflight valid|broken"
        2
    else
        let executable = Environment.GetEnvironmentVariable "QUINT_BIN"

        if String.IsNullOrWhiteSpace executable then
            eprintfn "Set QUINT_BIN and provision Quint plus its Rust evaluator (see README)."
            2
        else
            let timer = Stopwatch.StartNew()

            let request =
                {
                    Executable = executable
                    Sha256 = "939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
                    Version = "0.32.0"
                    WorkingDirectory = AppContext.BaseDirectory
                    Source = "pipeline.qnt"
                    Module = args[0]
                    Command = QuintCommand.Run(100, 4, "42", "dependenciesSatisfied")
                    Timeout = TimeSpan.FromSeconds(10.0)
                    MaxOutputBytes = 100000
                }

            let run r =
                Quint.run r CancellationToken.None |> fun t -> t.GetAwaiter().GetResult()

            let report = run request
            printf "%s" report.StandardOutput
            eprintf "%s" report.StandardError

            match report.Outcome with
            | ToolOutcome.SamplesPassed ->
                // A named completion scenario guards against a model whose jobs cannot run.
                let completion =
                    run
                        { request with
                            Source = "pipeline_test.qnt"
                            Module = "pipeline_test"
                            Command = QuintCommand.Test "completePipelineTest"
                        }

                if completion.Outcome = ToolOutcome.TestsPassed 1 then
                    printfn "ALLOW: sampled preflight and completion scenario passed (%d ms)." timer.ElapsedMilliseconds
                    0
                else
                    eprintfn "BLOCK: completion scenario did not pass: %A" completion.Outcome
                    1
            | outcome ->
                eprintfn
                    "BLOCK: preflight returned %A (%d ms); expensive tests were not started."
                    outcome
                    timer.ElapsedMilliseconds

                1
