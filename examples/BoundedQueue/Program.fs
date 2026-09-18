open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open FsQuint

let unwrap =
    function
    | Ok value -> value
    | Error error -> failwithf "%A" error

let hash (s: string) =
    SHA256.HashData(Encoding.UTF8.GetBytes(s)) |> Convert.ToHexStringLower

let state bindings =
    let draft = { Identity = ""; Bindings = bindings }

    { draft with
        Identity = QuintReplay.stateFingerprint draft |> unwrap
    }

let rec legacy =
    function
    | ItfValue.Integer n -> Integer(string n)
    | ItfValue.Sequence xs -> Sequence(List.map legacy xs)
    | ItfValue.Record xs -> Record(List.map (fun (k, v) -> k, legacy v) xs)
    | other -> failwithf "Queue projection does not support %A" other

let raw =
    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "queue.itf.json"))

let document = Itf.read Itf.defaultLimits raw |> unwrap

let states =
    document.States |> List.map (List.map (fun (k, v) -> k, legacy v) >> state)
// Bound to the explicit scenario source, never inferred from differences between states.
let actions = [ "enqueue:1"; "enqueue:2"; "dequeue"; "dequeue" ]

let source =
    {
        Path = "scenario.qnt"
        Line = 4
        Column = 3
    }

let environment =
    {
        Seed = "42"
        Bounds = [ "capacity", 2L; "steps", 4L ]
        ToolFingerprint = "939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f"
        ProfileFingerprint = hash "queue-scenario-v1"
        ContractFingerprint = hash "bounded-fifo-capacity-2"
        AdapterFingerprint = hash "queue-projection-v1"
        ImplementationFingerprint = hash "queue-implementation-v1"
    }

let draft =
    {
        SchemaVersion = 1
        TraceIdentity = ""
        Environment = environment
        Initial = states.Head
        Steps =
            List.zip actions states.Tail
            |> List.mapi (fun i (a, s) ->
                {
                    Index = i + 1
                    Action = a
                    Source = source
                    Expected = s
                })
    }

let trace =
    { draft with
        TraceIdentity = QuintReplay.traceFingerprint draft |> unwrap
    }

type QueueRuntime =
    {
        Items: Collections.Generic.List<int>
        Inserted: Collections.Generic.List<int>
        Removed: Collections.Generic.List<int>
    }

let driver broken mutate =
    {
        Initialize =
            fun _ _ ->
                Task.FromResult(
                    Ok
                        {
                            Items = new Collections.Generic.List<int>()
                            Inserted = new Collections.Generic.List<int>()
                            Removed = new Collections.Generic.List<int>()
                        }
                )
        Apply =
            fun step runtime _ ->
                task {
                    match step.Action with
                    | "enqueue:1"
                    | "enqueue:2" ->
                        if runtime.Items.Count = 2 then
                            return Error "Queue full"
                        else
                            let value = int (step.Action.Split(':')[1])
                            runtime.Items.Add(value)
                            runtime.Inserted.Add(value)
                            return Ok()
                    | "dequeue" ->
                        if runtime.Items.Count = 0 then
                            return Error "Queue empty"
                        else
                            let index = if broken then runtime.Items.Count - 1 else 0
                            runtime.Removed.Add(runtime.Items[index])
                            runtime.Items.RemoveAt(index)
                            return Ok()
                    | action -> return Error("Unknown action: " + action)
                }
        Observe =
            fun runtime _ ->
                let values xs =
                    xs |> Seq.map (string >> Integer) |> Seq.toList |> Sequence

                let items = if mutate then Sequence [] else values runtime.Items

                state
                    [
                        "state",
                        Record
                            [
                                "items", items
                                "inserted", values runtime.Inserted
                                "removed", values runtime.Removed
                            ]
                    ]
                |> Ok
                |> Task.FromResult
        Cleanup = fun _ _ -> Task.FromResult(Ok())
    }

let run broken mutate =
    Replay.run (TimeSpan.FromSeconds(5.0)) CancellationToken.None (driver broken mutate) trace
    |> fun t -> t.GetAwaiter().GetResult()

let good = run false false

if good.Outcome <> ReplayOutcome.Equivalent || good.CleanupFailure.IsSome then
    failwithf "Positive control failed: %A" good

match (run true false).Outcome with
| ReplayOutcome.Diverged(3, _, _, _, _, _) -> ()
| other -> failwithf "Wrong FIFO defect result: %A" other

match (run false true).Outcome with
| ReplayOutcome.Diverged(1, _, _, _, _, _) -> ()
| other -> failwithf "Projection mutation escaped: %A" other

match Itf.read Itf.defaultLimits (Encoding.UTF8.GetBytes("{\"states\":")) with
| Error _ -> ()
| Ok _ -> failwith "Malformed trace accepted"

printfn "PASS: queue agreement, FIFO defect at step 3, projection defect at step 1, malformed trace rejected."
