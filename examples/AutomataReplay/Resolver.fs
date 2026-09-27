module Resolver

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open FsQuint
open ByzantineSystems.Automata.Core

type Data = { Leaf: string; Payload: int }
type Effect = { Kind: string; Node: string; Data: int }
type Observation = { Outcome: string; Leaf: string; Handler: string; Exited: string list; Entered: string list; Actions: Effect list }
let empty outcome leaf = { Outcome=outcome; Leaf=leaf; Handler=""; Exited=[]; Entered=[]; Actions=[] }
let effect kind node data = { Kind=kind; Node=node; Data=data }
let source = function 6 | 7 -> "c" | 8 -> "b" | 10 -> "t" | _ -> "a"
let transition label target = Rule.transition (fun _ e -> e = "go") (fun _ _ -> [effect "rule" label 1], {Leaf=target;Payload=1})
let jump label target leaf = Rule.goto (fun _ e -> e = "go") (stateId target) (fun _ _ -> [effect "rule" label 1], {Leaf=leaf;Payload=1})
let internalRule label = Rule.internalOn (fun _ e -> e = "go") (fun _ _ -> [effect "rule" label 1])

let evaluate scenario mutant =
    let first = transition "first" "b"
    let parent = transition "parent" "a"
    let rules : (string * Rule<Data,string,Effect,string> list) list =
        match scenario with
        | 0 -> ["a", (if mutant then [transition "second" "c";first] else [first;transition "second" "c"]); "p",[parent]]
        | 1 ->
            let wrapped = Rule.transition (fun _ e -> e = "other") (fun _ _ -> [], {Leaf="b";Payload=1})
            ["a",[if mutant then wrapped else Rule.guarded (fun _ _ -> false) "blocked" wrapped]; "p",[parent]]
        | 2 -> ["a",[if mutant then first else Rule.attempt (fun _ e -> e="go") (fun _ _ -> Error "refused")]; "p",[parent]]
        | 3 -> ["a",[if mutant then transition "internal" "a" else internalRule "internal"]]
        | 4 -> ["a",[if mutant then internalRule "self" else transition "self" "a"]]
        | 5 -> [(if mutant then "a" else "p"),[parent]]
        | 6 -> ["c",[jump "compound" "p" (if mutant then "b" else "a")]]
        | 7 -> ["c",[jump "mismatch" "p" (if mutant then "a" else "b")]]
        | 8 -> ["b",[if mutant then transition "ancestor" "a" else jump "ancestor" "p" "a"]]
        | 9 -> ["a",[first]]
        | 10 -> ["p",[parent]]
        | 11 -> ["t",[first]]
        | 12 -> if mutant then ["a",[first]] else []
        | _ -> invalidArg "scenario" "Unknown resolver case"
    let node name parent initial terminal : Node<Data,string,Effect,string> = {
        Id=stateId name; Parent=Option.map stateId parent; InitialChild=Option.map stateId initial
        Terminal=terminal
        OnExit=fun s _ -> [effect "exit" name (if mutant && scenario=9 then 1 else s.Payload)]
        OnEntry=fun s _ -> [effect "entry" name s.Payload]
        Rules=rules |> List.tryFind(fst >> (=) name) |> Option.map snd |> Option.defaultValue [] }
    let nodes = [node "r" None None false; node "p" (Some "r") (Some "a") false;
                 node "a" (Some "p") None false; node "b" (Some "p") None false;
                 node "t" (Some "p") None (not(mutant && scenario=11)); node "c" (Some "r") None false]
    let from = { Leaf=source scenario; Payload=0 }
    match Chart.create (stateId "r") (fun (s: Data) -> stateId s.Leaf) nodes with
    | Error errors when errors |> List.contains(ChartError.InvalidTerminal(stateId "t")) -> empty "InvalidTerminal" from.Leaf
    | Error errors -> failwithf "Unexpected chart errors %A" errors
    | Ok chart when mutant && scenario=10 -> empty "Terminated" from.Leaf
    | Ok chart ->
        match Chart.resolve chart from "go" with
        | Ok r -> { Outcome="Handled"; Leaf=r.Next.Leaf; Handler=StateId.value r.HandledBy;
                    Exited=List.map StateId.value r.Exited; Entered=List.map StateId.value r.Entered; Actions=r.Actions }
        | Error(TransitionError.GuardFailed _) -> empty "GuardFailed" from.Leaf
        | Error(TransitionError.Rejected _) -> empty "Rejected" from.Leaf
        | Error(TransitionError.TargetMismatch _) -> empty "TargetMismatch" from.Leaf
        | Error(TransitionError.Unhandled _) -> empty "Unhandled" from.Leaf
        | Error error -> failwithf "Unexpected resolver error %A" error

let observe result =
    let strings xs = Sequence(List.map Text xs)
    Conformance.state ["result",Record [
        "outcome",Text result.Outcome; "leaf",Text result.Leaf; "handler",Text result.Handler;
        "exited",strings result.Exited; "entered",strings result.Entered;
        "actions",Sequence(result.Actions |> List.map(fun e ->
            Record["kind",Text e.Kind; "node",Text e.Node; "data",Integer(string e.Data)]))]]

let fragments () =
    let fragment prefix : NodeDraft<string,string,string,string> =
        state "p" {
            initial "a"
            state "a" {
                goto (fun _ e -> e="self") (stateId "p") (fun _ _ -> [], prefix+".a")
                goto (fun _ e -> e="outside") (stateId "outside") (fun _ _ -> [], "outside")
            }
        }
    let build stale =
        statechart<string,string,string,string> {
            root "r"
            classify (fun s -> stateId (if stale && s="left.a" then "a" else s))
            Fragment.prefix "left" (fragment "left")
            Fragment.prefix "right" (fragment "right")
            state "outside" { terminal }
        } |> Approval.unwrap
    let chart = build false
    for prefix in ["left";"right"] do
        let r = Chart.resolve chart (prefix+".a") "self" |> Approval.unwrap
        if r.Entered <> [stateId(prefix+".p");stateId(prefix+".a")] then failwith "Fragment internal target not rewritten"
        let external = Chart.resolve chart (prefix+".a") "outside" |> Approval.unwrap
        if external.Next <> "outside" then failwith "Fragment external target was rewritten"
    match Chart.resolve (build true) "left.a" "self" with
    | Error(TransitionError.UnknownState id) when id=stateId "a" -> ()
    | result -> failwithf "Stale fragment classifier escaped: %A" result
    if Chart.fingerprint chart <> Chart.fingerprint (build true) then failwith "Unexpected classifier fingerprint change"

let check root =
    use manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"fixtures","resolver","manifest.json")))
    let identities = manifest.RootElement.EnumerateObject() |> Seq.toList
    let required = ["resolver.qnt"; "resolver_test.qnt"] @ [for i in 0..12 -> sprintf "fixtures/resolver/case%d.itf.json" i]
    if (identities |> List.map(fun p -> p.Name) |> List.sort) <> List.sort required then failwith "Incomplete resolver manifest"
    for identity in identities do
        if Conformance.hash(File.ReadAllBytes(Path.Combine(root,identity.Name))) <> identity.Value.GetString() then
            failwith ("Resolver provenance mismatch: " + identity.Name)
    for scenario in 0..12 do
        let path = Path.Combine(root,"fixtures","resolver",sprintf "case%d.itf.json" scenario)
        let document = File.ReadAllBytes path |> Itf.read Itf.defaultLimits |> Approval.unwrap
        if Conformance.field "scenario" document.States[1] <> ItfValue.Integer(bigint scenario) then
            failwith "Resolver witness input mismatch"
        let states = document.States |> List.map(fun fields ->
            Conformance.state ["result",Conformance.project(Conformance.field "result" fields)])
        if states.Length <> 2 then failwith "Resolver witness must contain exactly one input"
        let environment = {
            Seed="42"; Bounds=["cases",13L;"steps",1L]
            ToolFingerprint="939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
            ProfileFingerprint=Conformance.hash(Text.Encoding.UTF8.GetBytes "fsquint.resolver/1")
            ContractFingerprint=Conformance.hash(File.ReadAllBytes(Path.Combine(root,"resolver.qnt")))
            AdapterFingerprint=Conformance.hash(File.ReadAllBytes(Path.Combine(root,"Resolver.fs")))
            ImplementationFingerprint=Conformance.hash(File.ReadAllBytes(Path.Combine(root,"baseline.json"))) }
        let draft = { SchemaVersion=1; TraceIdentity=""; Environment=environment; Initial=states.Head;
                      Steps=[{Index=1;Action=sprintf "case:%d" scenario;Source={Path="resolver_test.qnt";Line=scenario+3;Column=1};Expected=states[1]}] }
        let trace = {draft with TraceIdentity=QuintReplay.traceFingerprint draft |> Approval.unwrap}
        let run mutant =
            let driver = {
                Initialize=fun _ _ -> Task.FromResult(Ok(ref(empty "Initial" "")))
                Apply=fun _ (runtime: Observation ref) _ -> runtime.Value <- evaluate scenario mutant; Task.FromResult(Ok())
                Observe=fun runtime _ -> Task.FromResult(Ok(observe runtime.Value))
                Cleanup=fun _ _ -> Task.FromResult(Ok()) }
            Replay.run (TimeSpan.FromSeconds 5.0) CancellationToken.None driver trace |> fun t -> t.GetAwaiter().GetResult()
        if (run false).Outcome <> ReplayOutcome.Equivalent then failwithf "Resolver case %d differs: %A" scenario (run false)
        match (run true).Outcome with
        | ReplayOutcome.Diverged(1,_,_,_,_,_) -> ()
        | other -> failwithf "Resolver negative control %d escaped: %A" scenario other
    fragments()
    printfn "PASS: 13 Quint resolver witnesses and semantic mutations; prefixed fragment reuse, external targets and stale classifier."
