module Conformance

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FsQuint
open Approval

let hash (bytes: byte[]) = SHA256.HashData bytes |> Convert.ToHexStringLower
let state bindings =
    let value = { Identity = ""; Bindings = bindings }
    { value with Identity = QuintReplay.stateFingerprint value |> unwrap }

let rec project = function
    | ItfValue.Integer n -> Integer(string n)
    | ItfValue.Text s -> Text s
    | ItfValue.Sequence xs -> Sequence(List.map project xs)
    | ItfValue.Record xs -> Record(List.map (fun (k,v) -> k, project v) xs)
    | other -> failwithf "Unsupported approval value: %A" other

let observe mutation current =
    let phase = if mutation = Projection && current.Domain.Phase = Pending then "Draft" else string current.Domain.Phase
    state ["state", Record ["phase", Text phase; "approvedBy", Text current.Domain.ApprovedBy;
                            "outcome", Text current.Outcome; "actions", Sequence(current.Actions |> List.map(fun e ->
                                Record ["kind", Text e.Kind; "name", Text e.Name]))]]

let fields expected (element: JsonElement) =
    let actual = element.EnumerateObject() |> Seq.map(fun p -> p.Name) |> Seq.toList
    if List.sort actual <> List.sort expected then failwithf "Unknown, duplicate or missing fields: %A" actual
let str (key: string) (e: JsonElement) = e.GetProperty(key).GetString()

let decode op actor =
    match op, actor with
    | "submit", "author" -> Submit
    | "approve", "author" -> Approve Author
    | "approve", "reviewer" -> Approve Reviewer
    | "publish", "author" -> Publish
    | "cancel", "author" -> Cancel
    | "remind", "author" -> Remind
    | _ -> failwithf "Unknown input %s/%s" op actor

let field key bindings = bindings |> List.find(fst >> (=) key) |> snd

let validateObservation value =
    match value with
    | ItfValue.Record fields ->
        if (fields |> List.map fst |> List.sort) <> ["actions"; "approvedBy"; "outcome"; "phase"] then
            failwith "Unexpected observation fields"
        let memberOf key allowed =
            match field key fields with
            | ItfValue.Text text when List.contains text allowed -> ()
            | _ -> failwith ("Unknown observation case: " + key)
        memberOf "phase" ["Draft";"Pending";"Approved";"Published";"Cancelled"]
        memberOf "approvedBy" ["None";"Reviewer"]
        memberOf "outcome" ["Initial";"Applied";"NotAuthorized";"Unhandled";"Terminated"]
        match field "actions" fields with
        | ItfValue.Sequence actions ->
            for action in actions do
                match action with
                | ItfValue.Record ["kind", ItfValue.Text kind; "name", ItfValue.Text name]
                    when List.contains (kind,name) ["audit","submitted"; "audit","approved"; "audit","published";
                                                    "audit","cancelled"; "notify","review"; "notify","published"; "notify","reminder"] -> ()
                | _ -> failwith "Unknown effect record"
        | _ -> failwith "Actions must be an ordered list"
    | _ -> failwith "Observation must be a record"

// Entire binding is validated before a driver can initialize. Domain callbacks receive only Input.
let load root name (manifestBytes: byte[]) =
    use json = JsonDocument.Parse manifestBytes
    let m = json.RootElement
    let sampled = str "schema" m = "fsquint.approval-binding/2"
    fields (["schema";"profile";"traceSha256";"modelSha256";"scenarioSha256";"steps"] @
            (if sampled then ["sourceFile";"seed";"maxSteps"] else [])) m
    if (not sampled && str "schema" m <> "fsquint.approval-binding/1") || str "profile" m <> "fsquint.automata-approval/1" then failwith "Unknown binding profile"
    let sourceFile = if sampled then str "sourceFile" m else "approval_test.qnt"
    if sampled && sourceFile <> "approval.qnt" then failwith "Unknown sampled source"
    let raw = File.ReadAllBytes(Path.Combine(root, "fixtures", name + ".itf.json"))
    for key, bytes in ["traceSha256", raw; "modelSha256", File.ReadAllBytes(Path.Combine(root,"approval.qnt"));
                       "scenarioSha256", File.ReadAllBytes(Path.Combine(root,sourceFile))] do
        if str key m <> hash bytes then failwith ("Provenance mismatch: " + key)
    let document = Itf.read Itf.defaultLimits raw |> unwrap
    for bindings in document.States do
        if (bindings |> List.map fst |> List.sort) <> ["input";"state"] then failwith "Unexpected trace bindings"
        validateObservation (field "state" bindings)
    let entries = m.GetProperty("steps").EnumerateArray() |> Seq.toList
    if entries.Length <> document.States.Length - 1 then failwith "Binding length mismatch"
    if sampled && (m.GetProperty("maxSteps").GetInt32() <> 30 || entries.Length <> 30 || System.String.IsNullOrWhiteSpace(str "seed" m)) then
        failwith "Unexpected sample bound or missing seed"
    let inputs, bindings = entries |> List.mapi(fun index entry ->
        fields ["index";"op";"actor";"line"] entry
        if entry.GetProperty("index").GetInt32() <> index + 1 then failwith "Non-contiguous binding"
        let op, actor = str "op" entry, str "actor" entry
        let input = decode op actor
        let expectedInput = ItfValue.Record ["actor", ItfValue.Text actor; "op", ItfValue.Text op]
        let recorded = document.States[index+1] |> List.find(fst >> (=) "input") |> snd
        if recorded <> expectedInput then failwith "Input instrumentation differs from binding"
        let line = entry.GetProperty("line").GetInt32()
        let sourceLines = File.ReadAllLines(Path.Combine(root,sourceFile))
        let needle = if sampled then "action step" else sprintf "op: \"%s\", actor: \"%s\"" op actor
        if line < 1 || line > sourceLines.Length || not(sourceLines[line-1].Contains needle) then
            failwith "Source binding does not identify the input"
        input, (op + ":" + actor, { Path = sourceFile; Line = line; Column = 1 })) |> List.unzip
    let observations = document.States |> List.map(fun bindings ->
        bindings |> List.filter(fst >> (=) "state") |> List.map(fun (k,v) -> k,project v) |> state)
    let environment = {
        Seed = (if sampled then str "seed" m else "42"); Bounds = ["steps", int64 inputs.Length]
        ToolFingerprint = "939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
        ProfileFingerprint = hash (Text.Encoding.UTF8.GetBytes "fsquint.automata-approval/1")
        ContractFingerprint = str "modelSha256" m
        AdapterFingerprint = hash (File.ReadAllBytes(Path.Combine(root,"Conformance.fs")))
        ImplementationFingerprint = hash (File.ReadAllBytes(Path.Combine(root,"Approval.fs"))) }
    let draft = { SchemaVersion = 1; TraceIdentity = ""; Environment = environment; Initial = observations.Head
                  Steps = List.zip bindings observations.Tail |> List.mapi(fun i ((action,source),s) ->
                    { Index = i+1; Action = action; Source = source; Expected = s }) }
    inputs, { draft with TraceIdentity = QuintReplay.traceFingerprint draft |> unwrap }

let run mutation (inputs: Input list) (trace: QuintReplayTrace) onInit =
    let chart = chart mutation
    let bindings : PureReplay.BoundInput<Input> list = List.zip inputs trace.Steps |> List.map(fun (input,step) ->
        {Index=step.Index;OperationId=step.Action;Input=input})
    PureReplay.run (fun () -> onInit(); Ok initialObservation)
        (fun current input -> Ok(apply chart current input))
        (observe mutation >> Ok) (fun _ -> Ok()) bindings trace

let check root =
    let names = ["approvalTest";"cancelTest";"pendingCancelTest";"approvedCancelTest"] @
                [for seed in [1;7;42;99;123;1000;2000;3000] -> sprintf "sampled/seed-%d" seed]
    for name in names do
        let bytes = File.ReadAllBytes(Path.Combine(root,"fixtures",name+".binding.json"))
        let inputs, trace = load root name bytes
        let good = run Correct inputs trace ignore
        if good.Outcome <> ReplayOutcome.Equivalent || good.CleanupFailure.IsSome then failwithf "Conformance %s: %A" name good
        if name = "approvalTest" then
            for mutation, expectedStep in [Guard,2; ActionOrder,1; WrongTarget,1; Projection,1] do
                match (run mutation inputs trace ignore).Outcome with
                | ReplayOutcome.Diverged(step,action,source,path,expected,actual) when step = expectedStep ->
                    let diagnostic = JsonSerializer.Serialize {|
                        mutation=string mutation; step=step; input=action; source=source; path=path
                        traceIdentity=trace.TraceIdentity; modelDigest=trace.Environment.ContractFingerprint
                        expected=QuintReplay.encodeState expected |> unwrap
                        actual=QuintReplay.encodeState actual |> unwrap
                        reproduce="dotnet run --project examples/AutomataReplay/AutomataReplay.fsproj -c Release --no-restore" |}
                    printfn "DIVERGENCE %s" diagnostic
                | other -> failwithf "Mutation %A escaped or diverged at wrong step: %A" mutation other
            for bad in [Text.Encoding.UTF8.GetString(bytes).Replace("\"submit\"","\"unknown\"");
                        Text.Encoding.UTF8.GetString(bytes).Replace("\"index\": 1", "\"index\": 99"); "{}"] do
                let mutable initialized = false
                let rejected =
                    try
                        let ins, tr = load root name (Text.Encoding.UTF8.GetBytes bad)
                        run Correct ins tr (fun () -> initialized <- true) |> ignore
                        false
                    with _ -> true
                if not rejected || initialized then failwith "Malformed binding reached initialization"
    printfn "PASS: four fixed and eight sampled offline approval traces; four mutations at declared steps; malformed binding rejected before initialization."
