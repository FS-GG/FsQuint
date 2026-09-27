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

type QueueRuntime = { Items: int list; Inserted: int list; Removed: int list }
type QueueInput = Enqueue of int | Dequeue

let run broken mutate =
    let bindings : PureReplay.BoundInput<QueueInput> list = actions |> List.mapi(fun i action ->
        let input = match action with
                    | "enqueue:1" -> Enqueue 1 | "enqueue:2" -> Enqueue 2 | "dequeue" -> Dequeue
                    | other -> failwith ("Unknown queue input: " + other)
        {Index=i+1;OperationId=action;Input=input})
    let reduce current input =
        match input, current.Items with
        | Enqueue _, xs when xs.Length = 2 -> Error "Queue full"
        | Enqueue value, xs -> Ok {current with Items=xs@[value];Inserted=current.Inserted@[value]}
        | Dequeue, [] -> Error "Queue empty"
        | Dequeue, xs ->
            let value, rest = if broken then List.last xs, List.take (xs.Length-1) xs else xs.Head,xs.Tail
            Ok {current with Items=rest;Removed=current.Removed@[value]}
    let observe current =
        let values xs = xs |> List.map(string >> Integer) |> Sequence
        state ["state", Record ["items", (if mutate then Sequence [] else values current.Items)
                                "inserted", values current.Inserted; "removed", values current.Removed]] |> Ok
    PureReplay.run (fun () -> Ok {Items=[];Inserted=[];Removed=[]}) reduce observe
        (fun _ -> Ok()) bindings trace

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
