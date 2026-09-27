module Protocol

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open FsQuint
open ByzantineSystems.Automata.Core
open ByzantineSystems.Automata.Storage
open ByzantineSystems.Automata.Runtime
open ProtocolStore

type Mutation = Correct | Unfenced | DuplicateCommit | UndeduplicatedEffect
let timeout = TimeSpan.FromSeconds 5.0
let awaitTask (t: Task<'a>) = t.WaitAsync(timeout).GetAwaiter().GetResult()
let machineName = machineId "protocol"
let countChart =
    statechart<int,int,string,string> {
        root "counter"
        classify (fun _ -> stateId "count")
        state "count" {
            attempt (fun _ _ -> true) (fun s event -> if event<0 then Error "refused" else Ok(["effect"],s+event))
        }
    } |> Approval.unwrap
let build (testStore: TestStore) =
    machine<string,int,int,string,string> machineName {
        chart countChart
        chartVersion 1
        initialState 0
        store (testStore :> IMachineStore<_,_,_,_,_>)
        processor {ProcessorPolicy.defaults with Batch=4;Concurrency=2;MaxAttempts=3;
                                                  Lease=TimeSpan.FromHours 1.0;RenewAfter=TimeSpan.FromMinutes 30.0}
    } |> Approval.unwrap

let project submitted epochs amounts commits queued ack fenced dead rejected winners attempts effects =
    let n = Correction.number
    Record ["submitted",n submitted;"epochs",Correction.numbers epochs;"amounts",Correction.numbers amounts
            "commits",n commits;"queued",n queued;"acknowledged",n ack;"fenced",n fenced;"dead",n dead
            "rejected",n rejected;"winners",Correction.numbers winners;"deliveryAttempts",n attempts;"effects",n effects]
let initial = project 0 [0;0] [0;0] 0 0 0 0 0 0 [] 0 0

let evaluate scenario mutation =
    let testStore = TestStore((mutation=Unfenced),(mutation=DuplicateCommit))
    let inbox = testStore :> ICommandInbox<string,int>
    let writer = testStore :> ICommandProcessorStore<string,int,int,string,string>
    let first,second = build testStore,build testStore
    use lifetime = new CancellationTokenSource()
    let active = ResizeArray<Task>()
    let mutable attempts,effects = 0,0
    let seen = Collections.Generic.HashSet<int64*int>()
    let submit entity key payload =
        inbox.Submit({MachineId=machineName;EntityId=entity;IdempotencyKey=key;ChartVersion=ChartVersion.create 1;
                      Kind=CommandKind.Event;Event=payload;VisibleAt=None;ReceivedAt=None;Audit=AuditContext.empty},CancellationToken.None)
        |> awaitTask |> Approval.unwrap
    let poll m = (Machine.processor m).PollAsync CancellationToken.None |> awaitTask |> Approval.unwrap
    let barrier () = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    try
        if scenario=0 || scenario=3 then
            submit "one" "a" 1 |> ignore
            let arrived1,arrived2,release1,release2 = barrier(),barrier(),barrier(),barrier()
            testStore.BeforeCommit <- fun token ct -> task {
                let arrived,release = if token=1L then arrived1,release1 else arrived2,release2
                arrived.TrySetResult() |> ignore
                do! release.Task.WaitAsync(ct) }
            use crashed = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)
            let work1 = (Machine.processor first).PollAsync crashed.Token
            active.Add work1
            arrived1.Task |> awaitTask
            if scenario=3 then
                crashed.Cancel()
                try work1 |> awaitTask |> ignore with :? OperationCanceledException -> ()
            testStore.Expire()
            let work2 = (Machine.processor second).PollAsync lifetime.Token
            active.Add work2
            arrived2.Task |> awaitTask
            if scenario=0 then
                release1.SetResult()
                work1 |> awaitTask |> Approval.unwrap |> ignore
            release2.SetResult()
            work2 |> awaitTask |> Approval.unwrap |> ignore
        elif scenario=1 then
            submit "one" "a" 1 |> ignore
            testStore.LoseCommitResponse <- true
            poll first |> ignore
            let id,token,epoch,draft = testStore.LastCommit.Value
            let retry = writer.Commit(id,token,epoch,draft,CancellationToken.None) |> awaitTask |> Approval.unwrap
            if mutation=Correct then
                match retry with AlreadyFinalized _ -> () | other -> failwithf "Ambiguous retry was not idempotent: %A" other
            match writer.TryGetResult(id,CancellationToken.None) |> awaitTask |> Approval.unwrap with
            | Some(CommandResult.Committed _) -> ()
            | other -> failwithf "Committed result lost after response loss: %A" other
        elif scenario=2 then
            submit "one" "a" 1 |> ignore
            poll first |> ignore
            let handler : ActionHandler<string,string,string> = fun action _ ->
                attempts <- attempts+1
                if seen.Add(CommandId.value action.Work.CommandId,action.Work.Ordinal) || mutation=UndeduplicatedEffect then effects <- effects+1
                Task.FromResult(Ok())
            let dispatcher = Machine.dispatcher first handler
            testStore.LoseAck <- true
            dispatcher.PollAsync CancellationToken.None |> awaitTask |> Approval.unwrap |> ignore
            dispatcher.PollAsync CancellationToken.None |> awaitTask |> Approval.unwrap |> ignore
        elif scenario=4 then
            let original = submit "one" "a" 1
            let duplicate = submit "one" "a" 99
            match original,duplicate with Accepted a,AlreadySubmitted b when a=b -> () | _ -> failwith "Idempotency identity changed"
            submit "one" "b" 2 |> ignore
            submit "two" "a" 4 |> ignore
            poll first |> ignore
            poll second |> ignore
        elif scenario=5 then
            submit "one" "a" 1 |> ignore
            submit "one" "b" 2 |> ignore
            testStore.FailCommit <- true
            for _ in 1..3 do poll first |> ignore
            testStore.FailCommit <- false
            poll second |> ignore
        elif scenario=6 then
            submit "one" "a" -1 |> ignore
            poll first |> ignore
        else invalidArg "scenario" "Unknown protocol schedule"
        let submitted,epochs,amounts,commits,queued,ack,fenced,dead,rejected,winners = testStore.Metrics
        project submitted epochs amounts commits queued ack fenced dead rejected winners attempts effects
    finally
        lifetime.Cancel()
        try Task.WhenAll(active).WaitAsync(timeout).GetAwaiter().GetResult()
        with :? OperationCanceledException -> ()
        for m in [first;second] do Machine.stopAsync m CancellationToken.None |> fun task -> task.WaitAsync(timeout).GetAwaiter().GetResult()

let check root =
    use manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"fixtures/protocol/manifest.json")))
    let entries = manifest.RootElement.EnumerateObject() |> Seq.toList
    let required=["protocol.qnt";"protocol_test.qnt"] @ [for i in 0..6 -> sprintf "fixtures/protocol/case%d.itf.json" i]
    if (entries |> List.map(fun e -> e.Name) |> List.sort) <> List.sort required then failwith "Incomplete protocol manifest"
    for entry in entries do
        if Conformance.hash(File.ReadAllBytes(Path.Combine(root,entry.Name)))<>entry.Value.GetString() then failwith "Protocol provenance mismatch"
    for scenario in 0..6 do
        let raw=File.ReadAllBytes(Path.Combine(root,sprintf "fixtures/protocol/case%d.itf.json" scenario))
        let doc=Itf.read Itf.defaultLimits raw |> Approval.unwrap
        if doc.States.Length<>2 || Conformance.field "scenario" doc.States[1]<>ItfValue.Integer(bigint scenario) then failwith "Protocol input mismatch"
        let states=doc.States |> List.map(fun s -> Conformance.state["result",Conformance.project(Conformance.field "result" s)])
        let hashFile file=Conformance.hash(File.ReadAllBytes(Path.Combine(root,file)))
        let env={Seed="42";Bounds=["workers",2L;"entities",2L;"commands",3L]
                 ToolFingerprint="939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
                 ProfileFingerprint=Conformance.hash(Text.Encoding.UTF8.GetBytes "protocol/1")
                 ContractFingerprint=hashFile "protocol.qnt";AdapterFingerprint=hashFile "Protocol.fs";ImplementationFingerprint=hashFile "ProtocolStore.fs"}
        let draft={SchemaVersion=1;TraceIdentity="";Environment=env;Initial=states.Head
                   Steps=[{Index=1;Action=sprintf "schedule:%d" scenario;Source={Path="protocol_test.qnt";Line=scenario+4;Column=1};Expected=states[1]}]}
        let trace={draft with TraceIdentity=QuintReplay.traceFingerprint draft |> Approval.unwrap}
        let binding : PureReplay.BoundInput<int> list=[{Index=1;OperationId=trace.Steps.Head.Action;Input=scenario}]
        let run mutation=PureReplay.run (fun () -> Ok initial) (fun _ input -> Ok(evaluate input mutation))
                             (fun actual -> Ok(Conformance.state["result",actual])) (fun _ -> Ok()) binding trace
        let good=run Correct
        if good.Outcome<>ReplayOutcome.Equivalent then failwithf "Protocol schedule %d: %A" scenario good
        if (run Correct).Outcome<>ReplayOutcome.Equivalent then failwithf "Protocol schedule %d was not repeatable" scenario
        let defect=match scenario with 0 -> Some Unfenced | 1 -> Some DuplicateCommit | 2 -> Some UndeduplicatedEffect | _ -> None
        match defect with
        | Some mutation ->
            match (run mutation).Outcome with
            | ReplayOutcome.Diverged(1,_,_,_,_,_) -> ()
            | other -> failwithf "Protocol mutation %A escaped: %A" mutation other
        | None -> ()
    printfn "PASS: seven real processor/dispatcher fault schedules match Quint; fence, finalize and effect-deduplication mutations detected."
