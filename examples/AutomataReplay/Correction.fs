module Correction

open System
open System.IO
open FsQuint
open ByzantineSystems.Automata.Core
open ByzantineSystems.Automata.Storage
open ByzantineSystems.Automata.Runtime

type Account = { Amount: int; Closed: bool }
type Event = Credit | Double | Check | Close
type Mutation = Correct | Version | TieOrder | Effects | Policy
let at n = DateTimeOffset.UnixEpoch.AddSeconds(float n)
let number n = Integer(string n)
let textList xs = Sequence(List.map Text xs)
let numbers xs = Sequence(List.map number xs)
let initial = {Amount=0;Closed=false}
let chart factor =
    statechart<Account,Event,string,string> {
        root "account"
        classify (fun s -> stateId(if s.Closed then "closed" else "open"))
        state "open" {
            on (fun _ e -> e=Credit) (fun s _ -> ["credit"],{s with Amount=s.Amount+factor})
            on (fun _ e -> e=Double) (fun s _ -> ["double"],{s with Amount=s.Amount*2})
            attempt (fun _ e -> e=Check) (fun s _ -> if s.Amount>=20 then Error "limit" else Ok(["check"],s))
            on (fun _ e -> e=Close) (fun s _ -> ["close"],{s with Closed=true})
        }
        state "closed" { terminal }
    } |> Approval.unwrap

let original epoch event time version : CommittedTransition<string,Account,Event,string> = {
    Draft={MachineId=machineId "account"; EntityId="account-1"; Event=event; Actions=[];
           FromState=initial;ToState=initial;HandledBy=stateId "open";Exited=[];Entered=[];
           Status=InstanceStatus.Running;EffectiveAt=at time}
    Epoch=Epoch.ofUInt64(uint64 epoch);CommandId=CommandId.ofInt64(int64 epoch)
    ChartVersion=ChartVersion.create version;CommittedAt=at 10 }
let suffix = function
    | 7 | 9 -> []
    | 1 -> [original 2 Double 3 1;original 1 Credit 3 1]
    | 2 | 3 -> [original 1 Double 3 1;original 2 Check 4 1]
    | 4 -> [original 1 Credit 3 3]
    | 8 -> [original 1 Credit 3 1]
    | _ -> [original 1 Credit 3 1;original 2 Double 4 1]

let empty outcome epoch = Record [
    "outcome",Text outcome;"errorEpoch",number epoch;"amount",number -1;"closed",Boolean false
    "replayed",Sequence [];"versions",Sequence [];"beliefs",Sequence [];"hypothetical",Sequence []
    "committed",Sequence [];"truncated",number 0;"fromAmount",number 99]

let evaluate scenario mutation =
    let truncate = (scenario=3) <> (mutation=Policy)
    let input : ReplayInput<string,Account,Event> = {
        MachineId=machineId "account";EntityId="account-1";Start=(if scenario=7 then {initial with Amount=30} else initial)
        Live={initial with Amount=99};Missed=(if scenario=7 then Check elif scenario=8 then Close else Credit)
        At=at 2;Correction=CommandId.ofInt64 99L;Current=ChartVersion.create(if scenario=6 then 3 else 2)
        NextEpoch=Epoch.ofUInt64 9UL;Policy={OnDivergence=(if truncate then Divergence.Truncate else Divergence.Fail);ReplayLimit=(if scenario=5 then 1 else 5)} }
    let history = suffix scenario |> List.map(fun t ->
        if mutation=TieOrder then {t with Epoch=Epoch.ofUInt64(3UL-Epoch.value t.Epoch)} else t)
    let catalog = ChartCatalog.ofList [1,chart (if mutation=Version then 10 else 1);2,chart 10]
    match ByzantineSystems.Automata.Runtime.Replay.plan catalog input history with
    | Error(ReplayError.UnknownChartVersion _) -> empty "UnknownChartVersion" 0
    | Error(ReplayError.BudgetExceeded _) -> empty "BudgetExceeded" 0
    | Error(ReplayError.Diverged(epoch,_)) -> empty "Diverged" (int(Epoch.value epoch))
    | Error other -> failwithf "Unexpected planner failure %A" other
    | Ok plan ->
        Record [
            "outcome",Text "Planned";"errorEpoch",number 0;"amount",number plan.Commit.Transition.ToState.Amount
            "closed",Boolean plan.Commit.Transition.ToState.Closed
            "replayed",numbers(plan.Replayed |> List.map(fun (t,_) -> int(Epoch.value t.Epoch)))
            "versions",numbers(plan.Replayed |> List.map(fun (t,_) -> ChartVersion.value t.ChartVersion))
            "beliefs",Sequence(plan.Commit.Beliefs |> List.map(fun b -> Record [
                "at",number(int ((b.ValidFrom-DateTimeOffset.UnixEpoch).TotalSeconds));"amount",number b.Asserted.State.Amount
                "epoch",number(Epoch.value b.Asserted.Epoch);"version",number(ChartVersion.value b.ChartVersion)
                "command",number(CommandId.value b.CommandId)]))
            "hypothetical",textList(plan.Inserted.Actions @ (plan.Replayed |> List.collect(fun (_,d) -> d.Actions)))
            "committed",textList(if mutation=Effects then plan.Inserted.Actions else plan.Commit.Transition.Actions)
            "truncated",number(plan.Truncated |> Option.map(Epoch.value >> int) |> Option.defaultValue 0)
            "fromAmount",number plan.Commit.Transition.FromState.Amount]

let check root =
    use manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"fixtures/correction/manifest.json")))
    let identities = manifest.RootElement.EnumerateObject() |> Seq.toList
    let required = ["correction.qnt";"correction_test.qnt"] @ [for i in 0..9 -> sprintf "fixtures/correction/case%d.itf.json" i]
    if (identities |> List.map(fun p -> p.Name) |> List.sort) <> List.sort required then failwith "Incomplete correction manifest"
    for identity in identities do
        if Conformance.hash(File.ReadAllBytes(Path.Combine(root,identity.Name))) <> identity.Value.GetString() then failwith "Correction provenance mismatch"
    for scenario in 0..9 do
        let raw = File.ReadAllBytes(Path.Combine(root,sprintf "fixtures/correction/case%d.itf.json" scenario))
        let doc = Itf.read Itf.defaultLimits raw |> Approval.unwrap
        if doc.States.Length <> 2 || Conformance.field "scenario" doc.States[1] <> ItfValue.Integer(bigint scenario) then failwith "Invalid planner input binding"
        let states = doc.States |> List.map(fun bindings -> Conformance.state["result",Conformance.project(Conformance.field "result" bindings)])
        let hashFile name = Conformance.hash(File.ReadAllBytes(Path.Combine(root,name)))
        let env = {Seed="42";Bounds=["scenarios",10L;"suffix",2L];ToolFingerprint="939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
                   ProfileFingerprint=Conformance.hash(System.Text.Encoding.UTF8.GetBytes "correction/1")
                   ContractFingerprint=hashFile "correction.qnt";AdapterFingerprint=hashFile "Correction.fs";ImplementationFingerprint=hashFile "packages.lock.json"}
        let draft = {SchemaVersion=1;TraceIdentity="";Environment=env;Initial=states.Head
                     Steps=[{Index=1;Action=sprintf "plan:%d" scenario;Source={Path="correction_test.qnt";Line=scenario+3;Column=1};Expected=states[1]}]}
        let trace = {draft with TraceIdentity=QuintReplay.traceFingerprint draft |> Approval.unwrap}
        let binding : PureReplay.BoundInput<int> list = [{Index=1;OperationId=trace.Steps.Head.Action;Input=scenario}]
        let run mutation = PureReplay.run (fun () -> Ok(empty "Initial" 0))
                               (fun _ input -> Ok(evaluate input mutation))
                               (fun actual -> Ok(Conformance.state["result",actual])) (fun _ -> Ok()) binding trace
        if (run Correct).Outcome <> ReplayOutcome.Equivalent then failwithf "Planner case %d mismatch: %A" scenario (run Correct)
        let mutations = match scenario with 0 -> [Version;Effects] | 1 -> [TieOrder] | 2 | 3 -> [Policy] | _ -> []
        for mutation in mutations do
            match (run mutation).Outcome with
            | ReplayOutcome.Diverged(1,_,_,_,_,_) -> ()
            | other -> failwithf "Planner mutation %A escaped: %A" mutation other
    printfn "PASS: ten independent correction witnesses; version, tie-order, effects and fail/truncate mutations detected."
