namespace FsQuint.Qualification

open System
open System.IO
open System.IO.Compression
open System.Diagnostics
open System.Text.RegularExpressions
open System.Text.Json.Nodes
open System.Xml.Linq
open System.Threading
open FsQuint.Qualification.Common

module Providers =
    let private source = "03c3282f25888a32d36e53fa708f0342c328ccfc"
    let private pins =
        ["postgres", "https://github.com/postgres/postgres.git", "3638289fb57bdabec00deda98ee9624a35f5d66a"
         "pgmq", "https://github.com/pgmq/pgmq.git", "51d7655a097d91bf05ad5ae40dd604aa067ed355"
         "pg_cron", "https://github.com/citusdata/pg_cron.git", "5cedfa472ccc83567aa23ec645925ed8489a7797"
         "automata", "https://github.com/byzantine-systems/automata.git", source]
    let provision destination =
        let destination = Path.GetFullPath destination
        ensure (not (Directory.Exists destination)) "destination already exists"
        Directory.CreateDirectory destination |> ignore
        let prefix = Path.Combine(destination, "tools")
        let clone name folder =
            let _, url, commit = pins |> List.find (fun (key, _, _) -> key = name)
            Directory.CreateDirectory folder |> ignore
            runCommand "git" ["init"; "-q"] folder [] 900 |> ignore
            runCommand "git" ["fetch"; "--depth"; "1"; url; commit] folder [] 900 |> ignore
            runCommand "git" ["checkout"; "--detach"; "FETCH_HEAD"] folder [] 900 |> ignore
        use temporary = new Temporary("fsquint-provider-build-")
        let pg = Path.Combine(temporary.Path, "postgres")
        clone "postgres" pg
        runCommand (Path.Combine(pg, "configure")) ["--prefix=" + prefix; "--without-readline"; "--without-icu"; "--without-zlib"] pg [] 900 |> ignore
        for args in [["-j4"];["install"];["-C";"contrib/btree_gist";"install"]] do runCommand "make" args pg [] 900 |> ignore
        for name, subdir in ["pgmq", "pgmq-extension"; "pg_cron", "."] do
            let location = Path.Combine(temporary.Path, name)
            clone name location
            let directory = Path.Combine(location, subdir)
            let option = "PG_CONFIG=" + Path.Combine(prefix, "bin/pg_config")
            runCommand "make" [option] directory [] 900 |> ignore
            runCommand "make" ["install"; option] directory [] 900 |> ignore
        clone "automata" (Path.Combine(destination, "automata"))
        let pinData = pins |> List.map (fun (name,url,commit) -> name, jarray [jstr url; jstr commit])
        writeJson (Path.Combine(destination, "pins.json")) (obj pinData)
    let private suite (sourcePath: string) (output: string) (provider: string) (database: (string * string) option) (selected: string option) =
        let label = if selected.IsSome then provider + "-correction" else provider
        let start = Stopwatch.StartNew()
        let command = ["run"; "--project"; sprintf "tests/ByzantineSystems.Automata.Storage.%s.Tests" provider; "-c"; "Release"; "--"; "--summary"; "--sequenced"]
                      @ (match selected with Some name -> ["--filter-test-case"; name] | None -> [])
        use scratch = new Temporary("fsquint-suite-")
        let environment = ["AUTOMATA_TEST_DB", database |> Option.map fst;
                           "AUTOMATA_TEST_CRON_DB", database |> Option.map snd;
                           "TMPDIR", Some scratch.Path]
        let log =
            try runCommand "dotnet" command sourcePath environment 300
            with error ->
                File.WriteAllText(Path.Combine(output, label + ".log"), error.Message)
                reraise()
        File.WriteAllText(Path.Combine(output, label + ".log"), log)
        let clean = Regex.Replace(log, "\\x1b\\[[0-9;?]*[A-Za-z]", "")
        let counts = Regex.Match(clean, "(\\d+) tests run.*?(\\d+) passed,\\s*(\\d+) ignored,\\s*(\\d+) failed,\\s*(\\d+) errored", RegexOptions.Singleline)
        let expected = if selected.IsSome then 1 elif provider = "Sqlite" then 118 else 152
        ensure (counts.Success && int counts.Groups.[1].Value = expected && counts.Groups.[1].Value = counts.Groups.[2].Value && [3..5] |> List.forall (fun index -> int counts.Groups.[index].Value = 0)) (provider + ": missing, ignored or failing provider contracts; inspect log")
        obj ["tests", jint expected; "passed", jint expected; "ignored", jint 0;
             "elapsedSeconds", JsonValue.Create(Math.Round(start.Elapsed.TotalSeconds, 3)) :> JsonNode;
             "command", jarray (["dotnet"] @ command |> List.map jstr);
             "logSha256", jstr (shaText log)]

    let private crash (provider: string) (connection: string) (restart: unit -> unit) =
        let start = Stopwatch.StartNew()
        let dll = Path.Combine(providers, "bin/Release/net10.0/AutomataProviders.dll")
        use worker = new Process()
        worker.StartInfo.FileName <- "dotnet"
        for arg in [dll; provider; "seed"; connection] do worker.StartInfo.ArgumentList.Add arg
        worker.StartInfo.UseShellExecute <- false
        worker.StartInfo.RedirectStandardOutput <- true
        worker.StartInfo.RedirectStandardError <- true
        ensure (worker.Start()) "could not start provider seed worker"
        let mutable boundary = ""
        try
            let line = worker.StandardOutput.ReadLineAsync()
            ensure (line.Wait(TimeSpan.FromSeconds 30.)) "provider never reached crash boundary"
            boundary <- line.Result
            ensure (Regex.IsMatch(boundary, "^READY (\\d+) (\\d+)$")) ("bad crash boundary: " + boundary)
        finally
            if not worker.HasExited then worker.Kill(true)
            worker.WaitForExit(10000) |> ignore
        let parts = boundary.Split ' '
        restart()
        Thread.Sleep 1100
        let result = runCommand "dotnet" [dll; provider; "recover"; connection; parts.[1]; parts.[2]] root [] 30
        ensure (result.Trim() = "RECOVERED state=2 epoch=2 actions=2") ("recovery mismatch: " + result)
        obj ["boundary", jstr "commit-first-claim-second";
             "workerExit", jint worker.ExitCode;
             "recovered", obj ["state", jint 2; "epoch", jint 2; "actions", jint 2; "newFenceToken", JsonValue.Create(true) :> JsonNode];
             "serverImmediateRestart", (JsonValue.Create((provider = "postgres")) :> JsonNode);
             "elapsedSeconds", JsonValue.Create(Math.Round(start.Elapsed.TotalSeconds, 3)) :> JsonNode]

    let check sourcePath binaries =
        let sourcePath, binaries = Path.GetFullPath sourcePath, Path.GetFullPath binaries
        ensure (runCommand "git" ["rev-parse"; "HEAD"] sourcePath [] 30 |> fun s -> s.Trim() = source) "wrong Automata source"
        ensure (runCommand "git" ["status"; "--porcelain"; "--untracked-files=normal"] sourcePath [] 30 |> String.IsNullOrWhiteSpace) "modified source"
        let output = Path.Combine(root, "artifacts/automata-providers")
        Directory.CreateDirectory output |> ignore
        writeJson (Path.Combine(output, "report.json")) (obj ["status", jstr "incomplete"; "sourceCommit", jstr source])
        let pg name = Path.Combine(binaries, name)
        let version = runCommand (pg "postgres") ["--version"] root [] 30 |> fun s -> s.Trim()
        ensure (version = "postgres (PostgreSQL) 19beta3") "unqualified PostgreSQL version; use provider provisioning"
        runCommand "dotnet" ["restore"; providers; "--locked-mode"] root [] 180 |> ignore
        runCommand "dotnet" ["build"; providers; "-c"; "Release"; "--no-restore"] root [] 180 |> ignore
        let cache = runCommand "dotnet" ["nuget"; "locals"; "global-packages"; "--list"] root [] 30 |> fun s -> s.Trim().Split(": ").[1]
        let packages = ["sqlite";"postgres"] |> List.map (fun provider ->
            let id = "byzantinesystems.automata.storage." + provider
            let archive = Path.Combine(cache, id, "0.5.0", id + ".0.5.0.nupkg")
            use zip = ZipFile.OpenRead archive
            let entry = zip.Entries |> Seq.find (fun e -> e.FullName.EndsWith(".nuspec"))
            use stream = entry.Open()
            let xml = XDocument.Load stream
            let repository = xml.Descendants() |> Seq.find (fun e -> e.Name.LocalName = "repository")
            ensure (repository.Attribute(XName.Get "commit").Value = source) "provider package/source commit mismatch"
            obj ["id", jstr id; "version", jstr "0.5.0"; "sourceCommit", jstr source; "sha256", jstr (shaFile archive)])
        let report = obj ["schemaVersion", jint 1; "sourceCommit", jstr source;
                          "sdk", jstr ((runCommand "dotnet" ["--version"] root [] 30).Trim());
                          "postgres", jstr version; "packages", jarray packages;
                          "timeoutsSeconds", obj ["upstreamSuite", jint 300; "crashBoundary", jint 30; "recovery", jint 30]]
        let sqlite = suite sourcePath output "Sqlite" None None
        use sqliteDir = new Temporary("fsquint-sqlite-")
        sqlite.AsObject().Add("crashRecovery", crash "sqlite" (Path.Combine(sqliteDir.Path, "store.db")) ignore)
        report.AsObject().Add("sqlite", sqlite)
        use scratch = new Temporary("fsquint-pg-")
        let data, socket = Path.Combine(scratch.Path, "db"), Path.Combine(scratch.Path, "socket")
        Directory.CreateDirectory socket |> ignore
        runCommand (pg "initdb") ["-D"; data; "--auth=trust"; "--no-locale"] root [] 180 |> ignore
        File.AppendAllText(Path.Combine(data, "postgresql.conf"), "\nshared_preload_libraries = 'pg_cron'\ncron.database_name = 'postgres'\ncron.use_background_workers = on\n")
        let serverLog = Path.Combine(scratch.Path, "server.log")
        let serverOptions = sprintf "-k %s -h '' -p 55439" socket
        let startServer () = runCommand (pg "pg_ctl") ["-D"; data; "-l"; serverLog; "-o"; serverOptions; "-w"; "start"] root [] 60 |> ignore
        let stopServer () = runCommand (pg "pg_ctl") ["-D"; data; "-m"; "immediate"; "-w"; "stop"] root [] 60 |> ignore
        try
            startServer()
            let sql = "CREATE ROLE automata_app LOGIN; GRANT CREATE ON DATABASE postgres TO automata_app; GRANT CREATE ON SCHEMA public TO automata_app; CREATE EXTENSION btree_gist; CREATE EXTENSION pgmq; CREATE EXTENSION pg_cron; GRANT ALL ON SCHEMA pgmq TO automata_app; GRANT ALL ON ALL TABLES IN SCHEMA pgmq TO automata_app; GRANT ALL ON ALL SEQUENCES IN SCHEMA pgmq TO automata_app;"
            runCommand (pg "psql") ["-h"; socket; "-p"; "55439"; "-d"; "postgres"; "-v"; "ON_ERROR_STOP=1"; "-c"; sql] root [] 60 |> ignore
            let connection role = sprintf "Host=%s;Port=55439;Database=postgres;Username=%s;Pooling=false" socket role
            let database = Some(connection "automata_app", connection Environment.UserName)
            let postgres = suite sourcePath output "Postgres" database None
            let restart () = stopServer(); startServer()
            postgres.AsObject().Add("crashRecovery", crash "postgres" (fst database.Value) restart)
            let correction = suite sourcePath output "Postgres" database (Some "a correction rewrites the timeline and keeps what was believed before it")
            let beliefs () =
                ["instance_state";"instance_state_history";"transition";"command_correction"]
                |> List.map (fun table ->
                    let sql = sprintf "SELECT row_to_json(t)::text FROM fsm.%s t ORDER BY row_to_json(t)::text;" table
                    let rows = runCommand (pg "psql") ["-h"; socket; "-p"; "55439"; "-d"; "postgres"; "-At"; "-c"; sql] root [] 60
                               |> fun output -> output.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Seq.map JsonNode.Parse |> jarray
                    ensure (arr rows |> List.isEmpty |> not) ("empty correction evidence: " + table)
                    table, rows) |> obj
            let before = beliefs()
            restart()
            ensure (JsonNode.DeepEquals(before, beliefs())) "corrected history changed across immediate PostgreSQL restart"
            let counts = before.AsObject() |> Seq.map (fun item -> item.Key, jint (arr item.Value |> List.length)) |> Seq.toList |> obj
            correction.AsObject().Add("persistedRows", counts)
            correction.AsObject().Add("rowsSha256", jstr (shaText (before.ToJsonString())))
            writeJson (Path.Combine(output, "corrected-history.json")) before
            postgres.AsObject().Add("correctionRecovery", correction)
            report.AsObject().Add("postgresql", postgres)
        finally
            if File.Exists(Path.Combine(data, "postmaster.pid")) then stopServer()
            if File.Exists serverLog then File.Copy(serverLog, Path.Combine(output, "postgres-server.log"), true)
        report.AsObject().Add("status", jstr "passed")
        let harnessFiles =
            ["Program.fs", Path.Combine(providers, "Program.fs")
             "packages.lock.json", Path.Combine(providers, "packages.lock.json")
             "Providers.fs", Path.Combine(__SOURCE_DIRECTORY__, "Providers.fs")
             "Common.fs", Path.Combine(__SOURCE_DIRECTORY__, "Common.fs")]
        report.AsObject().Add("harnessSha256", obj (harnessFiles |> List.map (fun (name,file) -> name, jstr (shaFile file))))
        writeJson (Path.Combine(output, "report.json")) report
        printfn "%s" (report.ToJsonString(jsonOptions))
