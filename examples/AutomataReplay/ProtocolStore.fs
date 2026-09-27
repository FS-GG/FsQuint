// A deterministic contract store for scheduling the real Automata processor/dispatcher.
// This is test infrastructure, not evidence of a production provider's transaction semantics.
module ProtocolStore

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open ByzantineSystems.Automata.Core
open ByzantineSystems.Automata.Storage

type Row = { mutable Record: CommandRecord<string,int>; mutable Token: int64; mutable Deliveries: int }
type TestStore(ignoreFence: bool, duplicateFinalize: bool) =
    let gate = obj()
    let commands = ResizeArray<Row>()
    let history = ResizeArray<CommittedTransition<string,int,int,string>>()
    let snapshots = Dictionary<string,Snapshot<int>>()
    let pending = ResizeArray<LeasedAction<string,string>>()
    let activeActions = HashSet<int64*int>()
    let mutable token = 0L
    let mutable fenced = 0
    let mutable acknowledged = 0
    let row id = commands |> Seq.find(fun row -> row.Record.CommandId=id)
    let final status = status=CommandStatus.Succeeded || status=CommandStatus.Rejected || status=CommandStatus.DeadLettered
    let snapshot entity = match snapshots.TryGetValue entity with true,s -> s | _ -> {State=0;Epoch=Epoch.initial;Status=InstanceStatus.Running}
    let owns (r: Row) lease = (ignoreFence || r.Token=LeaseToken.value lease) && r.Record.Status=CommandStatus.Leased
    let result value = Task.FromResult(Ok value)
    member val BeforeCommit: int64 -> CancellationToken -> Task = (fun _ _ -> Task.CompletedTask) with get,set
    member val LoseCommitResponse = false with get,set
    member val LoseAck = false with get,set
    member val FailCommit = false with get,set
    member val LastCommit: (CommandId * LeaseToken<CommandWork> * Epoch * TransitionDraft<string,int,int,string>) option = None with get,set
    member _.Expire() = lock gate (fun () ->
        for r in commands do
            if r.Record.Status=CommandStatus.Leased then r.Record <- {r.Record with Status=CommandStatus.Ready}
        activeActions.Clear())
    member _.Metrics = lock gate (fun () ->
        commands.Count,
        [for entity in ["one";"two"] -> int(Epoch.value (snapshot entity).Epoch)],
        [for entity in ["one";"two"] -> (snapshot entity).State],
        history.Count,pending.Count,acknowledged,fenced,
        (commands |> Seq.filter(fun r -> r.Record.Status=CommandStatus.DeadLettered) |> Seq.length),
        (commands |> Seq.filter(fun r -> r.Record.Status=CommandStatus.Rejected) |> Seq.length),
        (commands |> Seq.filter(fun r -> r.Record.Status=CommandStatus.Succeeded) |> Seq.map(fun r -> int r.Token) |> Seq.toList))
    member _.History = List.ofSeq history
    interface ICommandInbox<string,int> with
        member _.Submit(s,_) = lock gate (fun () ->
            match commands |> Seq.tryFind(fun r -> r.Record.EntityId=s.EntityId && r.Record.IdempotencyKey=s.IdempotencyKey) with
            | Some r -> result(AlreadySubmitted r.Record.CommandId)
            | None ->
                let id = CommandId.ofInt64(int64 commands.Count+1L)
                let sequence = 1L + (commands |> Seq.filter(fun r -> r.Record.EntityId=s.EntityId) |> Seq.length |> int64)
                commands.Add {Record={CommandId=id;MachineId=s.MachineId;EntityId=s.EntityId;Sequence=sequence;IdempotencyKey=s.IdempotencyKey;
                                     ChartVersion=s.ChartVersion;Kind=s.Kind;Event=s.Event;Status=CommandStatus.Ready;Blocked=false;
                                     VisibleAt=DateTimeOffset.UnixEpoch;Attempts=0;ReceivedAt=DateTimeOffset.UnixEpoch;Audit=s.Audit}
                              Token=0L;Deliveries=0}
                result(Accepted id))
        member _.Claim(machine,batch,lease,_) = lock gate (fun () ->
            let eligible =
                commands
                |> Seq.filter(fun r ->
                    r.Record.MachineId=machine && r.Record.Status=CommandStatus.Ready &&
                    not(commands |> Seq.exists(fun earlier -> earlier.Record.EntityId=r.Record.EntityId && earlier.Record.Sequence<r.Record.Sequence && not(final earlier.Record.Status))))
                |> Seq.truncate batch |> Seq.toList
            result [for r in eligible do
                        token <- token+1L
                        r.Token <- token; r.Deliveries <- r.Deliveries+1
                        r.Record <- {r.Record with Status=CommandStatus.Leased}
                        yield {Work=r.Record;Token=LeaseToken.ofInt64 token;DeliveryCount=r.Deliveries;ExpiresAt=DateTimeOffset.UtcNow+lease}])
        member _.Reschedule(id,lease,_,_) = lock gate (fun () ->
            let r=row id
            if owns r lease then
                r.Record <- {r.Record with Status=CommandStatus.Ready;Attempts=r.Record.Attempts+1}
                result Updated
            else result LeaseUpdateOutcome.LeaseLost)
        member _.ExtendLease(id,lease,_,_) = lock gate (fun () -> result(if owns (row id) lease then Updated else LeaseUpdateOutcome.LeaseLost))
        member _.TryGet(id,_) = lock gate (fun () -> result(Some (row id).Record))
        member _.TryFind(_,entity,key,_) = lock gate (fun () -> result(commands |> Seq.tryFind(fun r -> r.Record.EntityId=entity && r.Record.IdempotencyKey=key) |> Option.map _.Record))
    interface IStateReader<string,int,int,string> with
        member _.TryGetSnapshot(_,entity,_) = lock gate (fun () -> result(Some(snapshot entity)))
        member _.History(_,entity,_,_) = lock gate (fun () -> result(history |> Seq.filter(fun t -> t.Draft.EntityId=entity) |> Seq.toList))
    interface ICommandProcessorStore<string,int,int,string,string> with
        member this.Commit(id,lease,expected,draft,ct) = task {
            this.LastCommit <- Some(id,lease,expected,draft)
            do! this.BeforeCommit (LeaseToken.value lease) ct
            ct.ThrowIfCancellationRequested()
            return lock gate (fun () ->
                let r=row id
                let previous=snapshot draft.EntityId
                if r.Record.Status=CommandStatus.Succeeded && r.Token=LeaseToken.value lease && not duplicateFinalize then
                    Ok(AlreadyFinalized previous.Epoch)
                elif not(owns r lease) && not(duplicateFinalize && r.Record.Status=CommandStatus.Succeeded) then
                    fenced <- fenced+1; Ok FinalizeOutcome.LeaseLost
                elif expected<>previous.Epoch && not duplicateFinalize then Ok(Conflict(expected,previous.Epoch))
                elif this.FailCommit then Error(StoreError.Unavailable(InvalidOperationException "injected before commit"))
                else
                    let epoch=Epoch.ofUInt64(Epoch.value previous.Epoch+1UL)
                    let committed={Draft=draft;Epoch=epoch;CommandId=id;ChartVersion=r.Record.ChartVersion;CommittedAt=DateTimeOffset.UnixEpoch}
                    history.Add committed
                    snapshots[draft.EntityId] <- {State=draft.ToState;Epoch=epoch;Status=draft.Status}
                    r.Record <- {r.Record with Status=CommandStatus.Succeeded}
                    r.Token <- LeaseToken.value lease
                    for ordinal,action in List.indexed draft.Actions do
                        pending.Add {Work={MachineId=draft.MachineId;EntityId=draft.EntityId;CommandId=id;Epoch=epoch;Ordinal=ordinal;Action=action}
                                     Token=LeaseToken.none();DeliveryCount=0;ExpiresAt=DateTimeOffset.UnixEpoch}
                    if this.LoseCommitResponse then
                        this.LoseCommitResponse <- false
                        Error(StoreError.Unavailable(InvalidOperationException "injected after atomic commit"))
                    else Ok(Finalized epoch)) }
        member _.Reject(id,lease,_,_) = lock gate (fun () ->
            let r=row id
            if owns r lease then r.Record <- {r.Record with Status=CommandStatus.Rejected}; result(Finalized ((snapshot r.Record.EntityId).Epoch))
            else result FinalizeOutcome.LeaseLost)
        member _.DeadLetter(id,lease,_,_) = lock gate (fun () ->
            let r=row id
            if owns r lease then r.Record <- {r.Record with Status=CommandStatus.DeadLettered}; result(Finalized ((snapshot r.Record.EntityId).Epoch))
            else result FinalizeOutcome.LeaseLost)
        member _.TryGetResult(id,_) = lock gate (fun () ->
            match history |> Seq.tryFind(fun t -> t.CommandId=id) with
            | Some transition -> result(Some(CommandResult.Committed transition))
            | None -> result(Some CommandResult.Pending))
    interface IActionQueue<string,string> with
        member _.Claim(_,batch,lease,_) = lock gate (fun () ->
            let available = pending |> Seq.filter(fun a -> not(activeActions.Contains(CommandId.value a.Work.CommandId,a.Work.Ordinal))) |> Seq.truncate batch |> Seq.toList
            result [for a in available do
                        token <- token+1L
                        activeActions.Add(CommandId.value a.Work.CommandId,a.Work.Ordinal) |> ignore
                        yield {a with Token=LeaseToken.ofInt64 token;DeliveryCount=a.DeliveryCount+1;ExpiresAt=DateTimeOffset.UtcNow+lease}])
        member this.Complete(action,_) = lock gate (fun () ->
            let key=CommandId.value action.Work.CommandId,action.Work.Ordinal
            activeActions.Remove key |> ignore
            if this.LoseAck then
                this.LoseAck <- false
                Task.FromResult(Error(StoreError.Unavailable(InvalidOperationException "injected acknowledgment loss")))
            else
                pending.RemoveAll(fun a -> a.Work.CommandId=action.Work.CommandId && a.Work.Ordinal=action.Work.Ordinal) |> ignore
                acknowledged <- acknowledged+1
                result Updated)
        member _.Reschedule(action,_,_) = lock gate (fun () -> activeActions.Remove(CommandId.value action.Work.CommandId,action.Work.Ordinal) |> ignore; result Updated)
        member _.Abandon(action,_,_) = lock gate (fun () -> pending.RemoveAll(fun a -> a.Work.CommandId=action.Work.CommandId && a.Work.Ordinal=action.Work.Ordinal) |> ignore; result Updated)
    interface IMachineStore<string,int,int,string,string>
