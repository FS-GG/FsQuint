module Turnstile

open System.IO
open FsQuint
open ByzantineSystems.Automata.Core

type Phase = Locked | Unlocked
type Event = Coin | Push
type Observation = { Phase: Phase; Actions: string list }

let chart broken =
    statechart<Phase,Event,string,string> {
        root "gate"
        classify (fun phase -> stateId(string phase))
        state "Locked" {
            on (fun _ event -> event=Coin) (fun _ _ -> ["thank"],Unlocked)
            internalOn (fun _ event -> event=Push) (fun _ _ -> [if broken then "pass" else "alarm"])
        }
        state "Unlocked" {
            on (fun _ event -> event=Coin) (fun _ _ -> ["thank"],Unlocked)
            on (fun _ event -> event=Push) (fun _ _ -> ["pass"],Locked)
        }
    } |> Approval.unwrap

let check root =
    use manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"fixtures","turnstile.manifest.json")))
    let entries = manifest.RootElement.EnumerateObject() |> Seq.toList
    if (entries |> List.map(fun p -> p.Name) |> List.sort) <> ["fixtures/turnstile.itf.json";"turnstile.qnt";"turnstile_test.qnt"] then
        failwith "Incomplete turnstile provenance"
    for entry in entries do
        if Conformance.hash(File.ReadAllBytes(Path.Combine(root,entry.Name))) <> entry.Value.GetString() then failwith "Turnstile provenance mismatch"
    let raw = File.ReadAllBytes(Path.Combine(root,"fixtures","turnstile.itf.json"))
    let document = Itf.read Itf.defaultLimits raw |> Approval.unwrap
    let states = document.States |> List.map(fun fields ->
        Conformance.state ["state",Conformance.project(Conformance.field "state" fields)])
    let inputs = [Push;Coin;Coin;Push]
    let names = ["push";"coin";"coin";"push"]
    for i,name in List.indexed names do
        if Conformance.field "input" document.States[i+1] <> ItfValue.Text name then failwith "Turnstile input mismatch"
    let hashFile file = Conformance.hash(File.ReadAllBytes(Path.Combine(root,file)))
    let environment = {
        Seed="42"; Bounds=["steps",4L]
        ToolFingerprint="939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
        ProfileFingerprint=Conformance.hash(System.Text.Encoding.UTF8.GetBytes "turnstile/1")
        ContractFingerprint=hashFile "turnstile.qnt"; AdapterFingerprint=hashFile "PureReplay.fs"; ImplementationFingerprint=hashFile "Turnstile.fs" }
    let draft = { SchemaVersion=1; TraceIdentity=""; Environment=environment; Initial=states.Head
                  Steps=List.zip3 names [4;6;7;8] states.Tail |> List.mapi(fun i (name,line,state) ->
                    {Index=i+1;Action=name;Source={Path="turnstile_test.qnt";Line=line;Column=1};Expected=state}) }
    let trace = {draft with TraceIdentity=QuintReplay.traceFingerprint draft |> Approval.unwrap}
    let bindings : PureReplay.BoundInput<Event> list = List.zip names inputs |> List.mapi(fun i (name,event) ->
        {Index=i+1;OperationId=name;Input=event})
    let observe current = Conformance.state ["state",Record ["phase",Text(string current.Phase);"actions",Sequence(List.map Text current.Actions)]] |> Ok
    let run broken =
        let machine = chart broken
        PureReplay.run (fun () -> Ok {Phase=Locked;Actions=[]})
            (fun current input -> Chart.resolve machine current.Phase input |> Result.map(fun r -> {Phase=r.Next;Actions=r.Actions}) |> Result.mapError(sprintf "%A"))
            observe (fun _ -> Ok()) bindings trace
    if (run false).Outcome <> ReplayOutcome.Equivalent then failwith "Turnstile replay differs"
    match (run true).Outcome with
    | ReplayOutcome.Diverged(1,_,_,_,_,_) -> ()
    | other -> failwithf "Turnstile alarm mutation escaped: %A" other
    let mutable initialized = false
    let malformed = {bindings.Head with OperationId="wrong"} :: bindings.Tail
    let refused =
        try
            PureReplay.run (fun () -> initialized <- true; Ok {Phase=Locked;Actions=[]})
                (fun s _ -> Ok s) observe (fun _ -> Ok()) malformed trace |> ignore
            false
        with :? System.ArgumentException -> true
    if initialized || not refused then failwith "Input identity mismatch reached initialization"
    printfn "PASS: second Automata domain uses the shared input-only helper; alarm mutation and mismatched binding detected."
