open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open FsQuint
open FsQuint.Tooling

let mutable count = 0

let check name condition =
    if not condition then
        failwith name
    else
        count <- count + 1
        printfn "PASS %s" name

let unwrap =
    function
    | Ok x -> x
    | Error e -> failwithf "%A" e

let input value =
    Encoding.UTF8.GetBytes("{\"vars\":[\"x\"],\"states\":[{\"#meta\":{\"index\":0},\"x\":" + value + "}]}")

let read value =
    Itf.read Itf.defaultLimits (input value)

let rejects name value =
    check name (read value |> Result.isError)

for name, value in
    [
        "float", "1.5"
        "unknown tag", "{\"#unknown\":[]}"
        "duplicate key", "{\"x\":1,\"x\":2}"
        "duplicate set", "{\"#set\":[1,{\"#bigint\":\"01\"}]}"
        "duplicate map key", "{\"#map\":[[1,2],[1,3]]}"
        "malformed tuple", "{\"#tup\":false}"
        "malformed map", "{\"#map\":[[1]]}"
        "truncated", "[1"
    ] do
    rejects name value

for name, value in
    [
        "map", "{\"#map\":[[1,2]]}"
        "tuple", "{\"#tup\":[1,true]}"
        "bigint", "{\"#bigint\":\"999999999999999999999999999999999999\"}"
        "variant record", "{\"tag\":\"Some\",\"value\":42}"
        "unicode", "\"λ😀\""
    ] do
    check name (read value |> Result.isOk)

check "invalid UTF8" (Itf.read Itf.defaultLimits [| 255uy |] |> Result.isError)
check "byte limit" (Itf.read { Itf.defaultLimits with MaxBytes = 2 } (input "1") |> Result.isError)

check
    "depth limit"
    (Itf.read { Itf.defaultLimits with MaxDepth = 3 } (input "[[[1]]]")
     |> Result.isError)

check
    "collection limit"
    (Itf.read
        { Itf.defaultLimits with
            MaxCollection = 2
        }
        (input "[1,2,3]")
     |> Result.isError)

let set1 = QuintReplay.encodeValue (Set [ Integer "02"; Integer "1" ])
let set2 = QuintReplay.encodeValue (Set [ Integer "1"; Integer "2" ])
check "legacy unordered set canonicalization" (set1 = set2)
check "legacy duplicate record" (QuintReplay.encodeValue (Record [ "a", Null; "a", Null ]) |> Result.isError)

let state x =
    let draft =
        {
            Identity = ""
            Bindings = [ "x", Integer(string x) ]
        }

    { draft with
        Identity = QuintReplay.stateFingerprint draft |> unwrap
    }

let fingerprint = String.replicate 64 "a"

let source =
    {
        Path = "test.qnt"
        Line = 1
        Column = 1
    }

let environment =
    {
        Seed = "42"
        Bounds = [ "steps", 2L ]
        ToolFingerprint = fingerprint
        ProfileFingerprint = fingerprint
        ContractFingerprint = fingerprint
        AdapterFingerprint = fingerprint
        ImplementationFingerprint = fingerprint
    }

let draft =
    {
        SchemaVersion = 1
        TraceIdentity = ""
        Environment = environment
        Initial = state 0
        Steps =
            [
                for i in 1..2 ->
                    {
                        Index = i
                        Action = "inc"
                        Source = source
                        Expected = state i
                    }
            ]
    }

let trace =
    { draft with
        TraceIdentity = QuintReplay.traceFingerprint draft |> unwrap
    }

let mutable cleaned = 0

let driver =
    {
        Initialize = fun _ _ -> Task.FromResult(Ok(ref 0))
        Apply =
            fun _ runtime _ ->
                runtime.Value <- runtime.Value + 1
                Task.FromResult(Ok())
        Observe = fun runtime _ -> Task.FromResult(Ok(state runtime.Value))
        Cleanup =
            fun _ _ ->
                cleaned <- cleaned + 1
                Task.FromResult(Ok())
    }

let runWith timeout ct d t =
    Replay.run timeout ct d t |> fun task -> task.GetAwaiter().GetResult()

let run d t =
    runWith (TimeSpan.FromSeconds(2.0)) CancellationToken.None d t

check "replay agreement" ((run driver trace).Outcome = ReplayOutcome.Equivalent)
check "cleanup on success" (cleaned = 1)

let divergence =
    run
        { driver with
            Observe = fun _ _ -> Task.FromResult(Ok(state 99))
        }
        trace

check
    "initial divergence"
    (match divergence.Outcome with
     | ReplayOutcome.Diverged(0, _, _, _, _, _) -> true
     | _ -> false)

check "no effects after initial mismatch" (divergence.AppliedSteps = 0)

let failed =
    run
        { driver with
            Apply = (fun _ _ _ -> Task.FromResult(Error "uncertain effect"))
            Cleanup = (fun _ _ -> Task.FromResult(Error "cleanup failed"))
        }
        trace

check "apply failure never retried" (failed.AppliedSteps = 1)
check "cleanup failure retained" (failed.CleanupFailure = Some "cleanup failed")

check
    "original failure retained"
    (match failed.Outcome with
     | ReplayOutcome.DriverFailure("apply", 1, "uncertain effect") -> true
     | _ -> false)

let invalid =
    run
        driver
        { trace with
            TraceIdentity = fingerprint
        }

check
    "invalid trace refused"
    (match invalid.Outcome with
     | ReplayOutcome.InvalidTrace _ -> true
     | _ -> false)

let cancelled = new CancellationTokenSource()
cancelled.Cancel()

check
    "cancellation"
    ((runWith (TimeSpan.FromSeconds(2.0)) cancelled.Token driver trace).Outcome = ReplayOutcome.Cancelled)

let slow =
    { driver with
        Apply =
            fun _ _ ct ->
                task {
                    do! Task.Delay(10000, ct)
                    return Ok()
                }
    }

check
    "cooperative deadline"
    ((runWith (TimeSpan.FromMilliseconds(50.0)) CancellationToken.None slow trace).Outcome = ReplayOutcome.TimedOut)

let observations =
    trace.Steps
    |> List.map (fun s ->
        {
            Index = s.Index
            Action = s.Action
            Source = s.Source
            Actual = s.Expected
        })

check
    "missing terminal observation"
    (match QuintReplay.compare trace observations[..0] |> unwrap with
     | QuintReplayResult.Diverged d -> d.Step = 2
     | _ -> false)

check
    "extra observation"
    (match
        QuintReplay.compare trace (observations @ [ { observations.Head with Index = 3 } ])
        |> unwrap
     with
     | QuintReplayResult.Diverged d -> d.Reason = "unexpected-observation"
     | _ -> false)

let quint = Environment.GetEnvironmentVariable "QUINT_BIN"

if not (String.IsNullOrWhiteSpace quint) then
    let request =
        {
            Executable = quint
            Sha256 = "939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
            Version = "0.32.0"
            WorkingDirectory = Directory.GetCurrentDirectory()
            Source = "examples/BoundedQueue/queue_test.qnt"
            Module = "queue_test"
            Command = QuintCommand.Test "fifoTest"
            Timeout = TimeSpan.FromSeconds(10.0)
            MaxOutputBytes = 100000
        }

    let tool r =
        Quint.run r CancellationToken.None |> fun t -> t.GetAwaiter().GetResult()

    let real = tool request
    check (sprintf "real named test: %A" real) (real.Outcome = ToolOutcome.TestsPassed 1)

    check
        "zero named tests rejected"
        ((tool
            { request with
                Command = QuintCommand.Test "does_not_exist"
            })
            .Outcome
            =
            ToolOutcome.Incomplete)

    check "tool digest mismatch" ((tool { request with Sha256 = fingerprint }).Outcome = ToolOutcome.IdentityMismatch)
    check "tool version unsupported" ((tool { request with Version = "0.0.0" }).Outcome = ToolOutcome.Unsupported)

    let simulation =
        { request with
            Source = "examples/BoundedQueue/queue.qnt"
            Module = "queue"
            Command = QuintCommand.Run(10, 4, "42", "fifo")
        }

    check "real sampled run" ((tool simulation).Outcome = ToolOutcome.SamplesPassed)

    check
        "real counterexample preserved"
        ((tool
            { simulation with
                Command = QuintCommand.Run(1, 4, "42", "state.items.length() == 0")
            })
            .Outcome
            =
            ToolOutcome.Counterexample)

    check
        "real typecheck"
        ((tool
            { simulation with
                Command = QuintCommand.Typecheck
            })
            .Outcome
            =
            ToolOutcome.Typechecked)

let values =
    Itf.read Itf.defaultLimits (File.ReadAllBytes "tests/values.itf.json") |> unwrap

check "actual Quint rich values fixture" (values.States.Length = 2)
let rich = values.States.Head |> List.exactlyOne |> snd

match rich with
| ItfValue.Record fields ->
    check
        "actual tuple encoding"
        (match (List.find (fst >> (=) "pair") fields |> snd) with
         | ItfValue.Tuple [ _; _ ] -> true
         | _ -> false)

    check
        "actual map encoding"
        (match (List.find (fst >> (=) "mapping") fields |> snd) with
         | ItfValue.Map pairs -> pairs.Length = 2
         | _ -> false)

    check
        "actual bigint exactness"
        (match (List.find (fst >> (=) "huge") fields |> snd) with
         | ItfValue.Integer n -> string n = "999999999999999999999999999999999999"
         | _ -> false)

    check
        "actual variant is tag/value record"
        (match (List.find (fst >> (=) "choice") fields |> snd) with
         | ItfValue.Record fields -> (List.find (fst >> (=) "tag") fields |> snd) = ItfValue.Text "Full"
         | _ -> false)
| _ -> failwith "Expected record fixture"

let reordered =
    ItfValue.Record
        [
            "z", ItfValue.Set [ ItfValue.Integer 2I; ItfValue.Integer 1I ]
            "a", ItfValue.Text "λ"
        ]

let originalCulture = Globalization.CultureInfo.CurrentCulture

try
    let baseline = Itf.canonical reordered

    for culture in [ "tr-TR"; "de-AT"; "en-US" ] do
        Globalization.CultureInfo.CurrentCulture <- Globalization.CultureInfo(culture)
        check ("culture " + culture) (Itf.canonical reordered = baseline)
finally
    Globalization.CultureInfo.CurrentCulture <- originalCulture

if OperatingSystem.IsLinux() then
    let scratch =
        Path.Combine(Path.GetTempPath(), "fsquint-process-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory scratch |> ignore

    try
        let fake = Path.Combine(scratch, "fake quint")

        let prepareScript body timeout outputBytes =
            File.WriteAllText(fake, "#!/bin/sh\nif [ \"$1\" = --version ]; then echo 0.32.0; exit 0; fi\n" + body)
            File.SetUnixFileMode(fake, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

            let digest =
                Security.Cryptography.SHA256.HashData(File.ReadAllBytes fake)
                |> Convert.ToHexStringLower

            let request =
                {
                    Executable = fake
                    Sha256 = digest
                    Version = "0.32.0"
                    WorkingDirectory = scratch
                    Source = "test.qnt"
                    Module = "test"
                    Command = QuintCommand.Test "test"
                    Timeout = timeout
                    MaxOutputBytes = outputBytes
                }

            request

        let runScript body timeout outputBytes =
            Quint.run (prepareScript body timeout outputBytes) CancellationToken.None
            |> fun t -> t.GetAwaiter().GetResult()

        // Output is a public compatibility contract: the cap is shared by both
        // streams per invocation, and any positive caller-selected cap is valid.
        // These finite shell-built-in fixtures need no Quint installation.
        let emitAscii count channel =
            let chunk = String.replicate 1000 "x"
            sprintf "i=0; while [ $i -lt %d ]; do printf '%%s' '%s'%s; i=$((i + 1)); done\n" count chunk channel

        let requireOutput label expectedOutput expectedError maxBytes body =
            let observed = runScript body (TimeSpan.FromSeconds(5.0)) maxBytes
            check (label + " outcome") (observed.Outcome = ToolOutcome.TestsPassed 1)
            check (label + " complete stdout") (observed.StandardOutput = expectedOutput)
            check (label + " complete stderr") (observed.StandardError = expectedError)

        let marker = "\n1 passing\n"
        requireOutput
            "100000-byte stdout profile"
            (String.replicate 99000 "x" + marker)
            ""
            100000
            (emitAscii 99 "" + "printf '\\n1 passing\\n'\n")
        requireOutput
            "100000-byte stderr profile"
            marker
            (String.replicate 99000 "x")
            100000
            (emitAscii 99 " >&2" + "printf '\\n1 passing\\n'\n")
        requireOutput
            "100000-byte combined profile"
            (String.replicate 49000 "x" + marker)
            (String.replicate 49000 "x")
            100000
            (emitAscii 49 "" + emitAscii 49 " >&2" + "printf '\\n1 passing\\n'\n")
        requireOutput
            "100000-byte UTF8 profile"
            (String.replicate 1000 "é🙂" + marker)
            ""
            100000
            ("i=0; while [ $i -lt 1000 ]; do printf 'é🙂'; i=$((i + 1)); done\n"
             + "printf '\\n1 passing\\n'\n")
        requireOutput
            "public cap above fixed capture allocation"
            (String.replicate 300000 "x" + marker)
            ""
            400000
            (emitAscii 300 "" + "printf '\\n1 passing\\n'\n")
        requireOutput
            "public stderr above fixed stream allocation"
            marker
            (String.replicate 300000 "x")
            400000
            (emitAscii 300 " >&2" + "printf '\\n1 passing\\n'\n")
        check
            "combined streams share one output limit"
            ((runScript
                (emitAscii 60 "" + emitAscii 60 " >&2" + "printf '\\n1 passing\\n'\n")
                (TimeSpan.FromSeconds(5.0))
                100000).Outcome = ToolOutcome.OutputLimit)

        requireOutput
            "UTF8 BOM excluded from decoded output charge"
            marker
            ""
            11
            "printf '\\357\\273\\277\n1 passing\n'\n"
        requireOutput
            "malformed UTF8 replacement retained"
            ("�" + marker)
            ""
            14
            "printf '\\377\n1 passing\n'\n"
        requireOutput
            "surrogate pairs across reader chunks retained"
            (String.replicate 2049 "🙂" + marker)
            ""
            20000
            ("i=0; while [ $i -lt 2049 ]; do printf '🙂'; i=$((i + 1)); done\n"
             + "printf '\\n1 passing\\n'\n")

        // The 0.2 batch profile intentionally narrows request admission. Record
        // version and workload effects separately: a version probe can precede
        // workload argument refusal, but neither path/cwd refusal launches it.
        let profileEffects = Path.Combine(scratch, "profile.effects")
        let quoteShell (value: string) = "'" + value.Replace("'", "'\\''") + "'"
        let prepareProfile executable cwd =
            let body =
                "#!/bin/sh\nif [ \"$1\" = --version ]; then printf 'V\\n' >> "
                + quoteShell profileEffects
                + "; echo 0.32.0; exit 0; fi\nprintf 'C\\n' >> "
                + quoteShell profileEffects
                + "\nprintf '\\n1 passing\\n'\n"
            File.WriteAllText(executable, body)
            File.SetUnixFileMode(executable, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            { Executable = executable
              Sha256 = Security.Cryptography.SHA256.HashData(File.ReadAllBytes executable) |> Convert.ToHexStringLower
              Version = "0.32.0"
              WorkingDirectory = cwd
              Source = "test.qnt"
              Module = "test"
              Command = QuintCommand.Test "test"
              Timeout = TimeSpan.FromSeconds 5.
              MaxOutputBytes = 100000 }

        let runProfile request =
            File.Delete profileEffects
            let result = Quint.run request CancellationToken.None |> fun t -> t.GetAwaiter().GetResult()
            let effects = if File.Exists profileEffects then File.ReadAllText profileEffects else ""
            result, effects

        let directoryBytes target =
            let mutable path = scratch
            let mutable remaining = target - Text.Encoding.UTF8.GetByteCount path
            while remaining > 201 do
                // Leave at least two bytes for the final slash/component pair.
                let componentBytes = min 200 (remaining - 3)
                path <- Path.Combine(path, String.replicate componentBytes "d")
                Directory.CreateDirectory path |> ignore
                remaining <- target - Text.Encoding.UTF8.GetByteCount path
            if remaining = 1 then invalidArg "target" "Directory byte target needs a slash and nonempty component."
            if remaining > 0 then
                path <- Path.Combine(path, String.replicate (remaining - 1) "d")
                Directory.CreateDirectory path |> ignore
            path

        let withinPath = Path.Combine(directoryBytes 2043, "tool")
        let outsidePath = Path.Combine(directoryBytes 2044, "tool")
        check "fixture executable boundary is encoded bytes" (Text.Encoding.UTF8.GetByteCount withinPath = 2048 && Text.Encoding.UTF8.GetByteCount outsidePath = 2049)
        let withinPathResult, withinPathEffects = runProfile (prepareProfile withinPath scratch)
        check "2048-byte executable admitted" (withinPathResult.Outcome = ToolOutcome.TestsPassed 1 && withinPathEffects = "V\nC\n")
        let outsidePathResult, outsidePathEffects = runProfile (prepareProfile outsidePath scratch)
        check "2049-byte executable refused without probe" (match outsidePathResult.Outcome with ToolOutcome.LaunchFailure _ -> outsidePathEffects = "" | _ -> false)
        let withinCwd = directoryBytes 2048
        let outsideCwd = directoryBytes 2049
        let withinCwdResult, withinCwdEffects = runProfile (prepareProfile fake withinCwd)
        check "2048-byte cwd admitted" (withinCwdResult.Outcome = ToolOutcome.TestsPassed 1 && withinCwdEffects = "V\nC\n")
        let outsideCwdResult, outsideCwdEffects = runProfile (prepareProfile fake outsideCwd)
        check "2049-byte cwd refused without probe" (match outsideCwdResult.Outcome with ToolOutcome.LaunchFailure _ -> outsideCwdEffects = "" | _ -> false)

        let unicodeCwd = Path.Combine(directoryBytes 2046, "é")
        Directory.CreateDirectory unicodeCwd |> ignore
        check "cwd boundary counts UTF8 rather than characters" (unicodeCwd.Length = 2048 && Text.Encoding.UTF8.GetByteCount unicodeCwd = 2049)
        let unicodeCwdResult, unicodeCwdEffects = runProfile (prepareProfile fake unicodeCwd)
        check "multibyte cwd beyond encoded limit refused" (match unicodeCwdResult.Outcome with ToolOutcome.LaunchFailure _ -> unicodeCwdEffects = "" | _ -> false)

        let combinedBase = prepareProfile fake scratch
        // Partition into three OS-valid arguments instead of exceeding Linux's
        // per-argument bound while testing the producer's aggregate allowance.
        let combinedBase = { combinedBase with Source = String.replicate 90000 "s"; Module = String.replicate 90000 "m" }
        let requestBytes request pattern =
            Text.Encoding.UTF8.GetByteCount request.Executable
            + Text.Encoding.UTF8.GetByteCount request.WorkingDirectory
            + ([ "test"; request.Source; "--main"; request.Module; "--match"; pattern; "--backend"; "rust" ]
               |> List.sumBy (fun value -> Text.Encoding.UTF8.GetByteCount value + 1))
            + Text.Encoding.UTF8.GetByteCount "NO_COLOR" + Text.Encoding.UTF8.GetByteCount "1" + 2
        let pattern = String.replicate (256 * 1024 - requestBytes combinedBase "") "p"
        check "fixture combined request is exact allowance" (requestBytes combinedBase pattern = 256 * 1024)
        let withinRequestResult, withinRequestEffects = runProfile { combinedBase with Command = QuintCommand.Test pattern }
        check "256-KiB combined request admitted" (withinRequestResult.Outcome = ToolOutcome.TestsPassed 1 && withinRequestEffects = "V\nC\n")
        let outsideRequestResult, outsideRequestEffects = runProfile { combinedBase with Command = QuintCommand.Test(pattern + "p") }
        check "combined request overflow refuses workload after probe" (match outsideRequestResult.Outcome with ToolOutcome.LaunchFailure _ -> outsideRequestEffects = "V\n" | _ -> false)

        check
            "batch stdin is closed"
            ((runScript
                "if IFS= read -r input; then echo 'unexpected input' >&2; exit 9; fi\nprintf '\\n1 passing\\n'\n"
                (TimeSpan.FromSeconds 2.)
                10000).Outcome = ToolOutcome.TestsPassed 1)

        check
            "exit zero without evidence rejected"
            ((runScript "exit 0\n" (TimeSpan.FromSeconds(2.0)) 10000).Outcome = ToolOutcome.Incomplete)

        check
            "zero count refused"
            ((runScript "echo '0 passing'\n" (TimeSpan.FromSeconds(2.0)) 10000).Outcome = ToolOutcome.Incomplete)

        check
            "output flood bounded"
            ((runScript
                "while :; do echo 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx'; done\n"
                (TimeSpan.FromSeconds(5.0))
                1000)
                .Outcome
                =
                ToolOutcome.OutputLimit)

        // These lifecycle fixtures use the public Quint.run token and the same
        // finite shell helper. Only recorded PID/starttime generations authorize
        // fixture cleanup; a current process/group census is not ownership.
        let processGeneration (stat: string) =
            let closing = stat.LastIndexOf(')')
            let pid = stat.Substring(0, stat.IndexOf(' ')) |> int
            let fields = stat.Substring(closing + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            pid, fields[19], fields[0]

        let executingGeneration (pid, birth) =
            try
                let actualPid, actualBirth, state =
                    File.ReadAllText(sprintf "/proc/%d/stat" pid) |> processGeneration
                actualPid = pid && actualBirth = birth && state <> "Z" && state <> "X"
            with
            | :? FileNotFoundException
            | :? DirectoryNotFoundException -> false

        let waitUntil milliseconds predicate =
            let elapsed = Diagnostics.Stopwatch.StartNew()
            while not (predicate ()) && elapsed.ElapsedMilliseconds < int64 milliseconds do
                Thread.Sleep 5
            predicate ()

        let retireRecordedChild statPath =
            // No fresh enrollment or recursive kill. Transient/unrecorded or
            // detached descendants remain outside this fixture's cleanup proof.
            // Cleanup diagnostics cannot replace the first failed assertion.
            try
                if File.Exists statPath then
                    let pid, birth, _ = File.ReadAllText statPath |> processGeneration
                    if executingGeneration (pid, birth) then
                        use child = Diagnostics.Process.GetProcessById pid
                        if executingGeneration (pid, birth) then child.Kill()
                    let retired = waitUntil 500 (fun () -> not (executingGeneration (pid, birth)))
                    printfn "INFO fixture child PID %d birth %s retired: %b" pid birth retired
                    retired
                else
                    printfn "INFO fixture child generation not recorded; cleanup unknown"
                    false
            with ex ->
                printfn "INFO recorded fixture child cleanup unresolved: %s" (ex.GetType().Name)
                false

        let observeFixtureTask label (running: Task<ToolReport> option) =
            // Observation shares the same suite window. Do not detach an await
            // or replace the original assertion with a cleanup/task fault.
            match running with
            | None -> printfn "INFO %s task not started" label
            | Some running ->
                try
                    running.Wait(500) |> ignore
                with ex ->
                    printfn "INFO %s task observation: %s" label (ex.GetType().Name)
                printfn "INFO %s task terminal: %b status: %A" label running.IsCompleted running.Status

        let lifecycleBody prefix tail =
            "sleep 30 &\nchild=$!\nIFS= read -r child_stat < /proc/$child/stat\n"
            + "printf '%s\\n' \"$child_stat\" > " + prefix + ".child.stat\n"
            + "IFS= read -r leader_stat < /proc/$$/stat\n"
            + "printf '%s\\n' \"$leader_stat\" > " + prefix + ".leader.stat\n"
            + "printf '\\n1 passing\\n'\n: > " + prefix + ".ready\n" + tail

        let cancellationStat = Path.Combine(scratch, "caller-cancel.child.stat")
        let mutable cancellationRetired = false
        let mutable cancellationTask: Task<ToolReport> option = None
        let caller = new CancellationTokenSource()
        try
            let request =
                prepareScript (lifecycleBody "caller-cancel" "wait\n") (TimeSpan.FromSeconds 5.) 10000
            let running = Quint.run request caller.Token
            cancellationTask <- Some running
            check "Quint cancellation reaches actual workload"
                (waitUntil 1000 (fun () -> File.Exists(Path.Combine(scratch, "caller-cancel.ready"))))
            let childPid, childBirth, _ = File.ReadAllText cancellationStat |> processGeneration
            check "Quint cancellation fixture child is executing" (executingGeneration (childPid, childBirth))
            caller.Cancel()
            check "Quint caller cancellation returns within observation allowance" (running.Wait(2000))
            check "Quint caller token retains cancellation first cause" (running.Result.Outcome = ToolOutcome.Cancelled)
        finally
            // Dispose alone does not request cancellation on an early failure.
            try caller.Cancel()
            with ex -> printfn "INFO caller cleanup cancellation: %s" (ex.GetType().Name)
            cancellationRetired <- retireRecordedChild cancellationStat
            observeFixtureTask "caller-cancel" cancellationTask
            try caller.Dispose()
            with ex -> printfn "INFO caller cleanup disposal: %s" (ex.GetType().Name)
        check "Quint cancellation known child retired" cancellationRetired

        let heldPipeStat = Path.Combine(scratch, "held-pipe.child.stat")
        let mutable heldPipeRetired = false
        let mutable heldPipeTask: Task<ToolReport> option = None
        try
            let request =
                prepareScript (lifecycleBody "held-pipe" "exit 0\n") (TimeSpan.FromSeconds 5.) 10000
            let running = Quint.run request CancellationToken.None
            heldPipeTask <- Some running
            check "held pipe fixture reaches workload"
                (waitUntil 1000 (fun () -> File.Exists(Path.Combine(scratch, "held-pipe.ready"))))
            let leaderPid, leaderBirth, _ =
                File.ReadAllText(Path.Combine(scratch, "held-pipe.leader.stat")) |> processGeneration
            check "held pipe leader exits before work deadline"
                (waitUntil 1000 (fun () -> not (executingGeneration (leaderPid, leaderBirth))))
            let childPid, childBirth, _ = File.ReadAllText heldPipeStat |> processGeneration
            check "held pipe recorded child remains executing" (executingGeneration (childPid, childBirth))
            check "passing marker and leader exit cannot complete held pipe" (not running.IsCompleted)
            check "held pipe observation returns within fixture allowance" (running.Wait(7000))
            check "held pipe work deadline retains timeout first cause" (running.Result.Outcome = ToolOutcome.TimedOut)
        finally
            // Keep CancellationToken.None: cleanup closes the known held pipe;
            // a late task may remain pending, which is retained as a diagnostic.
            heldPipeRetired <- retireRecordedChild heldPipeStat
            observeFixtureTask "held-pipe" heldPipeTask
        check "held pipe known child retired by fixture" heldPipeRetired

        let timed =
            runScript "sleep 30 &\necho $! > child.pid\nIFS= read -r child_stat < /proc/$!/stat\nprintf '%s\\n' \"$child_stat\" > child.stat\nwait\n" (TimeSpan.FromMilliseconds(200.0)) 10000

        check "process deadline" (timed.Outcome = ToolOutcome.TimedOut)
        let pid = File.ReadAllText(Path.Combine(scratch, "child.pid")).Trim() |> int

        let live =
            try
                let status = File.ReadAllText(sprintf "/proc/%d/stat" pid)
                // A terminated zombie awaiting init's reap is not an executing process.
                not (status.Contains(") Z "))
            with
            | :? FileNotFoundException
            | :? DirectoryNotFoundException -> false

        printfn "INFO owned timeout child original generation: %s" (File.ReadAllText(Path.Combine(scratch, "child.stat")).Trim())
        printfn "INFO owned timeout child still executing: %b" live
        check "owned child no longer executing" (not live)
    finally
        Directory.Delete(scratch, true)

let legacyValues =
    [
        Null
        QuintReplayValue.Boolean true
        Integer "+000123"
        Text "λ😀\n\r\t"
        Sequence [ Integer "2"; Integer "1" ]
        Set [ Integer "2"; Integer "1" ]
        Record [ "z", Text "last"; "a", Set [ Null; QuintReplayValue.Boolean false ] ]
    ]

let vectors =
    System.Text.Json.JsonDocument.Parse(File.ReadAllText "tests/legacy-vectors.json")

for i, value in List.indexed legacyValues do
    let expected = vectors.RootElement[i]

    check
        (sprintf "original SDD canonical bytes %d" i)
        (QuintReplay.encodeValue value |> unwrap = expected.GetProperty("canonical").GetString())

    let fingerprint =
        QuintReplay.stateFingerprint
            {
                Identity = ""
                Bindings = [ "value", value ]
            }
        |> unwrap

    check (sprintf "original SDD fingerprint %d" i) (fingerprint = expected.GetProperty("fingerprint").GetString())

vectors.Dispose()
// Regression reproduced against the served 0.1.0-preview.1 package: malformed
// UTF-16 and U+FFFD collided after Encoding.UTF8's replacement fallback.
let invalidUnicode = String(char 0xD800, 1)

let invalidState =
    {
        Identity = ""
        Bindings = [ "x", Text invalidUnicode ]
    }

check "invalid UTF16 text cannot acquire an identity" (QuintReplay.stateFingerprint invalidState |> Result.isError)
check "invalid UTF16 record key rejected" (QuintReplay.encodeValue (Record [ invalidUnicode, Null ]) |> Result.isError)

check
    "invalid UTF16 binding rejected"
    (QuintReplay.stateFingerprint
        { invalidState with
            Bindings = [ invalidUnicode, Null ]
        }
     |> Result.isError)

check "valid replacement character remains supported" (QuintReplay.encodeValue (Text "�") |> Result.isOk)

check
    "invalid UTF16 seed rejected"
    (QuintReplay.traceFingerprint
        { trace with
            Environment =
                { trace.Environment with
                    Seed = invalidUnicode
                }
        }
     |> Result.isError)

check
    "invalid UTF16 source rejected"
    (QuintReplay.traceFingerprint
        { trace with
            Steps =
                [
                    { trace.Steps.Head with
                        Source = { source with Path = invalidUnicode }
                    }
                ]
        }
     |> Result.isError)

check
    "invalid UTF16 action rejected"
    (QuintReplay.traceFingerprint
        { trace with
            Steps =
                [
                    { trace.Steps.Head with
                        Action = invalidUnicode
                    }
                ]
        }
     |> Result.isError)

check
    "invalid UTF16 bound name rejected"
    (QuintReplay.traceFingerprint
        { trace with
            Environment =
                { trace.Environment with
                    Bounds = [ invalidUnicode, 1L ]
                }
        }
     |> Result.isError)

printfn "%d checks passed" count
