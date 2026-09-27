module AutomataProviders.Program

open System
open System.Threading
open System.Threading.Tasks
open ByzantineSystems.Automata.Core
open ByzantineSystems.Automata.Storage
open ByzantineSystems.Automata.Resilience
open Microsoft.Extensions.Logging.Abstractions

type Account = class end
type Store = IMachineStore<EntityId<Account>, int, int, string, string>
let ct = CancellationToken.None
let machine = machineId "fsquint-crash"
let entity : EntityId<Account> = entityId "account"
let version = ChartVersion.create 1
let ok = function Ok value -> value | Error error -> failwithf "%A" error
let get (task: Task<Result<'a, 'e>>) = task.GetAwaiter().GetResult() |> ok
let require condition message = if not condition then failwith message

let openStore provider connection : Store * IChartRegistry * IDisposable =
    if provider = "sqlite" then
        let cs = ByzantineSystems.Automata.Storage.Sqlite.DataSource.connectionString connection
        ByzantineSystems.Automata.Storage.Sqlite.Migrator.migrate NullLogger.Instance cs |> ok |> ignore
        let context = ByzantineSystems.Automata.Storage.Sqlite.SqliteContext.create cs TransientPolicy.defaults ignore TimeProvider.System
        let options = ByzantineSystems.Automata.Storage.Sqlite.MachineStoreOptions.forEntityId<Account, int, int, string, string> context
        ByzantineSystems.Automata.Storage.Sqlite.SqliteMachineStore(options),
        ByzantineSystems.Automata.Storage.Sqlite.SqliteChartRegistry({ Context = context }),
        { new IDisposable with member _.Dispose() = Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools() }
    elif provider = "postgres" then
        ByzantineSystems.Automata.Storage.Postgres.Migrator.migrate NullLogger.Instance connection |> ok |> ignore
        let data = ByzantineSystems.Automata.Storage.Postgres.DataSource.create connection
        let context = ByzantineSystems.Automata.Storage.Postgres.PostgresContext.create data TransientPolicy.defaults ignore
        use conn = data.OpenConnection()
        use cmd = new Npgsql.NpgsqlCommand("SELECT fsm.ensure_action_queue('fsquint_crash_actions');", conn)
        cmd.ExecuteNonQuery() |> ignore
        let options = ByzantineSystems.Automata.Storage.Postgres.MachineStoreOptions.forEntityId<Account, int, int, string, string> context "fsquint_crash_actions"
        ByzantineSystems.Automata.Storage.Postgres.PostgresMachineStore(options),
        ByzantineSystems.Automata.Storage.Postgres.PostgresChartRegistry({ Context = context }), data
    else failwith "unknown provider"

let submit (store: Store) key =
    store.Submit({ MachineId = machine; EntityId = entity; IdempotencyKey = key
                   ChartVersion = version; Kind = CommandKind.Event; Event = 1
                   VisibleAt = None; ReceivedAt = None
                   Audit = AuditContext.empty }, ct) |> get |> ignore

let claim (store: Store) =
    (store :> ICommandInbox<_, _>).Claim(machine, 1, TimeSpan.FromSeconds 1.0, ct)
    |> get |> List.exactlyOne

let commit (store: Store) held before =
    let draft = { MachineId = machine; EntityId = entity; Event = 1; Actions = [ "notify" ]
                  FromState = before; ToState = before + 1; HandledBy = stateId "account"
                  Exited = []; Entered = []; Status = Running; EffectiveAt = DateTimeOffset.UtcNow }
    let epoch = if before = 0 then Epoch.initial else Epoch.next Epoch.initial
    let outcome = store.Commit(held.Work.CommandId, held.Token, epoch, draft, ct) |> get
    require (outcome = Finalized(Epoch.next epoch)) "commit was not finalized"

[<EntryPoint>]
let main args =
    let provider, phase, connection = args[0], args[1], args[2]
    let store, registry, lifetime = openStore provider connection
    use _lifetime = lifetime
    registry.Register({ MachineId = machine; Version = version
                        Fingerprint = ChartFingerprint.tryCreate (String.replicate 64 "b") |> ok }, ct)
    |> get |> ignore
    if phase = "seed" then
        submit store "first"
        commit store (claim store) 0
        submit store "second"
        let held = claim store
        // Parent waits for this durable boundary, then SIGKILLs us without disposing resources.
        printfn "READY %d %d" (CommandId.value held.Work.CommandId) (LeaseToken.value held.Token)
        Console.Out.Flush()
        Thread.Sleep Timeout.Infinite
    elif phase = "recover" then
        let snapshot = store.TryGetSnapshot(machine, entity, ct) |> get |> Option.get
        require (snapshot.State = 1 && snapshot.Epoch = Epoch.next Epoch.initial) "first commit lost or duplicated"
        let held = claim store
        require (CommandId.value held.Work.CommandId = int64 args[3]) "wrong recovered command"
        require (LeaseToken.value held.Token > int64 args[4]) "recovery did not fence the dead worker"
        commit store held 1
        let snapshot = store.TryGetSnapshot(machine, entity, ct) |> get |> Option.get
        require (snapshot.State = 2 && snapshot.Epoch = Epoch.next (Epoch.next Epoch.initial)) "recovery state/epoch incorrect"
        let actions = (store :> IActionQueue<_, _>).Claim(machine, 10, TimeSpan.FromSeconds 30.0, ct) |> get
        require (actions.Length = 2) "committed outbox lost or duplicated"
        for action in actions do store.Complete(action, ct) |> get |> ignore
        printfn "RECOVERED state=2 epoch=2 actions=2"
    else failwith "unknown phase"
    0
