namespace FsQuint.Qualification

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open System.Text.Json.Nodes
open FsQuint.Qualification.Common

module Generation =
    let private semantic (raw: byte[]) =
        let document = JsonNode.Parse raw
        ensure (str (prop (prop document "#meta") "status") = "ok") "Quint did not report positive trace evidence"
        let states = arr (prop document "states") |> List.map (fun state ->
            let copy = state.DeepClone()
            copy.AsObject().Remove("#meta") |> ignore
            copy)
        obj ["vars", prop document "vars"; "states", jarray states]

    let run write =
        let baseline = read (Path.Combine(replay, "baseline.json"))
        let quint = Environment.GetEnvironmentVariable "QUINT_BIN"
        let evaluator = Path.Combine(Environment.GetEnvironmentVariable "QUINT_HOME", "rust-evaluator-v0.6.0", "quint_evaluator")
        ensure (shaFile quint = str (prop baseline "quintSha256") && shaFile evaluator = str (prop baseline "evaluatorSha256")) "Generator/evaluator identity differs from baseline"
        let sources = Directory.GetFiles(replay, "*.qnt") |> Array.sort |> Array.map (fun file -> Path.GetFileName file, shaFile file) |> Map.ofArray
        let commands = ResizeArray<JsonNode>()
        let inputs = HashSet<string * string>()
        let phases, outcomes, orders = HashSet<string>(), HashSet<string>(), HashSet<string>()
        let paths, handlers, markers = HashSet<string>(), HashSet<string>(), HashSet<string>()
        let invoke args =
            commands.Add(jarray (args |> List.map jstr))
            runCommand quint args replay [] 120 |> ignore
        let checkJson file value =
            if write then writeJson file value
            else ensure (JsonNode.DeepEquals(read file, value)) ("Manifest/coverage differs: " + file)
        let accept candidate relative =
            let target = Path.Combine(replay, "fixtures", relative)
            let parsed = semantic (File.ReadAllBytes candidate)
            if write then
                Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                if File.Exists target then
                    ensure (JsonNode.DeepEquals(parsed, semantic (File.ReadAllBytes target))) ("Existing regression changed: " + relative)
                else File.Copy(candidate, target)
            else
                ensure (JsonNode.DeepEquals(parsed, semantic (File.ReadAllBytes target))) ("Regeneration differs semantically: " + relative)
            target, arr (prop parsed "states")
        let matchOne folder pattern =
            match Directory.GetFiles(folder, pattern) with
            | [| file |] -> file
            | _ -> failwith ("Missing or ambiguous witness output: " + pattern)
        let allowed = Set.ofList ["submit","author"; "approve","author"; "approve","reviewer"; "publish","author"; "cancel","author"; "remind","author"]
        let binding target states entries source seed sampled =
            ensure (List.length entries = List.length states - 1) "Input count differs"
            for entry, state in List.zip entries (List.tail states) do
                let input = prop state "input"
                let actual = str (prop input "op"), str (prop input "actor")
                ensure (input.AsObject().Count = 2 && allowed.Contains actual) "Unknown generated input"
                ensure (fst actual = str (prop entry "op") && snd actual = str (prop entry "actor")) "Authored input differs from instrumented input"
                inputs.Add actual |> ignore
            for item in states do
                let state = prop item "state"
                phases.Add(str (prop state "phase")) |> ignore
                outcomes.Add(str (prop state "outcome")) |> ignore
                let order = arr (prop state "actions") |> List.map (fun action -> str (prop action "kind") + ":" + str (prop action "name")) |> String.concat "|"
                orders.Add order |> ignore
            let properties =
                ["schema", jstr (if sampled then "fsquint.approval-binding/2" else "fsquint.approval-binding/1")
                 "profile", jstr "fsquint.automata-approval/1"
                 "traceSha256", jstr (shaFile target)
                 "modelSha256", jstr sources.["approval.qnt"]
                 "scenarioSha256", jstr sources.[source]
                 "steps", jarray entries]
                @ if sampled then ["sourceFile", jstr source; "seed", jstr (string seed); "maxSteps", jint 30] else []
            let bindingFile = Path.ChangeExtension(Path.ChangeExtension(target, null), ".binding.json")
            checkJson bindingFile (obj properties)
        use scratch = new Temporary("fsquint-generation-")
        for moduleName in ["approval"; "resolver"; "turnstile"; "correction"; "protocol"] do
            invoke ["typecheck"; moduleName + ".qnt"]
            invoke ["test"; moduleName + "_test.qnt"; "--seed"; "42"; "--max-samples"; "1"; "--out-itf"; Path.Combine(scratch.Path, moduleName + "_{test}_{seq}.itf.json")]
        let authored = Dictionary<string, ResizeArray<JsonNode>>()
        let mutable current = ""
        File.ReadAllLines(Path.Combine(replay, "approval_test.qnt")) |> Array.iteri (fun index line ->
            let action = Regex.Match(line, @"run (\w+)")
            if action.Success then
                current <- action.Groups.[1].Value
                authored.[current] <- ResizeArray()
            let operation = Regex.Match(line, "op: \"(\\w+)\", actor: \"(\\w+)\"")
            if operation.Success then
                let entries = authored.[current]
                entries.Add(obj ["index", jint (entries.Count + 1); "op", jstr operation.Groups.[1].Value; "actor", jstr operation.Groups.[2].Value; "line", jint (index + 1)]))
        for KeyValue(name, entries) in authored do
            let target, states = accept (matchOne scratch.Path (sprintf "approval_%s_*.itf.json" name)) (name + ".itf.json")
            binding target states (List.ofSeq entries) "approval_test.qnt" 42 false
        for index in 0..12 do
            let _, states = accept (matchOne scratch.Path (sprintf "resolver_case%dTest_*.itf.json" index)) (sprintf "resolver/case%d.itf.json" index)
            let result = prop (List.last states) "result"
            let exits = arr (prop result "exited") |> List.map str
            let entered = arr (prop result "entered") |> List.map str
            paths.Add(String.concat "/" exits + "|" + String.concat "/" entered) |> ignore
            handlers.Add(str (prop result "handler")) |> ignore
            for action in arr (prop result "actions") do
                if str (prop action "kind") = "rule" then markers.Add(str (prop action "node")) |> ignore
        accept (matchOne scratch.Path "turnstile_passageTest_*.itf.json") "turnstile.itf.json" |> ignore
        for moduleName, count in ["correction", 10; "protocol", 7] do
            for index in 0..count-1 do
                accept (matchOne scratch.Path (sprintf "%s_case%dTest_*.itf.json" moduleName index)) (sprintf "%s/case%d.itf.json" moduleName index) |> ignore
        let line = File.ReadAllLines(Path.Combine(replay, "approval.qnt")) |> Array.findIndex (fun line -> line.Contains("action step")) |> (+) 1
        for seed in [1;7;42;99;123;1000;2000;3000] do
            let candidate = Path.Combine(scratch.Path, sprintf "sample-%d.itf.json" seed)
            invoke ["run"; "approval.qnt"; "--invariant"; "safety"; "--seed"; string seed; "--max-samples"; "1"; "--max-steps"; "30"; "--out-itf"; candidate]
            let target, states = accept candidate (sprintf "sampled/seed-%d.itf.json" seed)
            let entries = List.tail states |> List.mapi (fun index state ->
                let input = prop state "input"
                obj ["index", jint (index + 1); "op", prop input "op"; "actor", prop input "actor"; "line", jint line])
            binding target states entries "approval.qnt" seed true
        let manifest file names =
            let pairs = names |> List.map (fun name -> name, jstr (shaFile (Path.Combine(replay, name))))
            checkJson (Path.Combine(replay, "fixtures", file)) (obj pairs)
        manifest "resolver/manifest.json" (["resolver.qnt"; "resolver_test.qnt"] @ [for i in 0..12 -> sprintf "fixtures/resolver/case%d.itf.json" i])
        manifest "turnstile.manifest.json" ["fixtures/turnstile.itf.json"; "turnstile.qnt"; "turnstile_test.qnt"]
        manifest "correction/manifest.json" (["correction.qnt"; "correction_test.qnt"] @ [for i in 0..9 -> sprintf "fixtures/correction/case%d.itf.json" i])
        manifest "protocol/manifest.json" (["protocol.qnt"; "protocol_test.qnt"] @ [for i in 0..6 -> sprintf "fixtures/protocol/case%d.itf.json" i])
        ensure (Set.ofSeq inputs = allowed && Set.ofSeq phases = Set.ofList ["Draft";"Pending";"Approved";"Published";"Cancelled"]) "Required input/phase coverage missing"
        ensure (Set.ofList ["Initial";"Applied";"NotAuthorized";"Unhandled";"Terminated"] |> Set.isSubset (Set.ofSeq outcomes)) "Required outcome coverage missing"
        for order in ["audit:submitted|notify:review"; "audit:published|notify:published"] do ensure (orders.Contains order) "Required ordered action coverage missing"
        let stringArray (values: seq<string>) = values |> Seq.sort |> Seq.map jstr |> jarray
        let splitOrder (value: string) = value.Split('|', StringSplitOptions.RemoveEmptyEntries) |> Seq.map (fun (item: string) -> item.Split(':') |> Seq.map jstr |> jarray) |> jarray
        let coverage = obj [
            "schema", jstr "fsquint.coverage/1"
            "inputs", (inputs |> Seq.sort |> Seq.map (fun (a,b) -> jarray [jstr a; jstr b]) |> jarray)
            "phases", stringArray phases
            "outcomes", stringArray outcomes
            "resolverCases", (seq {0..12} |> Seq.map jint |> jarray)
            "correctionCases", (seq {0..9} |> Seq.map jint |> jarray)
            "protocolSchedules", (seq {0..6} |> Seq.map jint |> jarray)
            "actionOrders", (orders |> Seq.sort |> Seq.map splitOrder |> jarray)
            "resolverPaths", (paths |> Seq.sortWith (fun a b ->
                let parts (value: string) =
                    let sides = value.Split('|')
                    sides.[0].Split('/', StringSplitOptions.RemoveEmptyEntries) |> Array.toList,
                    sides.[1].Split('/', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
                compare (parts a) (parts b)) |> Seq.map (fun text ->
                let sides = text.Split('|')
                let elements side = sides.[side].Split('/', StringSplitOptions.RemoveEmptyEntries) |> Seq.map jstr |> jarray
                jarray [elements 0; elements 1]) |> jarray)
            "resolverHandlers", stringArray handlers
            "ruleMarkers", stringArray markers
            "sampleSeeds", ([1;7;42;99;123;1000;2000;3000] |> Seq.map jint |> jarray)
            "sampleStepBound", jint 30
            "claim", jstr "required bins exercised; no exhaustive application/runtime claim"]
        checkJson (Path.Combine(replay, "fixtures/coverage.json")) coverage
        let files = Directory.GetFiles(Path.Combine(replay, "fixtures"), "*.json", SearchOption.AllDirectories) |> Array.sort
        let digests = files |> Array.map (fun file -> Path.GetRelativePath(replay, file).Replace('\\','/'), jstr (shaFile file)) |> Array.toList
        let evidence = obj ["schema", jstr "fsquint.generation-evidence/1"; "outcome", jstr "passed"; "quintSha256", jstr (shaFile quint); "evaluatorSha256", jstr (shaFile evaluator); "sourceDigests", obj (sources |> Map.toList |> List.map (fun (k,v) -> k,jstr v)); "coverage", coverage; "commands", jarray commands; "rawAndBindingDigests", obj digests]
        writeJson (Path.Combine(root, "artifacts/automata-generation.json")) evidence
        printfn "PASS: pinned witness/sample regeneration, explicit input bindings and all required coverage bins."
