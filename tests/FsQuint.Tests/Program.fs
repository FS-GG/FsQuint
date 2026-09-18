open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open FsQuint
open FsQuint.Tooling

let mutable count = 0
let check name condition = if not condition then failwith name else count <- count + 1; printfn "PASS %s" name
let unwrap = function Ok x -> x | Error e -> failwithf "%A" e
let input value = Encoding.UTF8.GetBytes("{\"vars\":[\"x\"],\"states\":[{\"#meta\":{\"index\":0},\"x\":" + value + "}]}")
let read value = Itf.read Itf.defaultLimits (input value)
let rejects name value = check name (read value |> Result.isError)
for name,value in ["float","1.5"; "unknown tag","{\"#unknown\":[]}"; "duplicate key","{\"x\":1,\"x\":2}"; "duplicate set","{\"#set\":[1,{\"#bigint\":\"01\"}]}"; "duplicate map key","{\"#map\":[[1,2],[1,3]]}"; "malformed tuple","{\"#tup\":false}"; "malformed map","{\"#map\":[[1]]}"; "truncated","[1" ] do rejects name value
for name,value in ["map","{\"#map\":[[1,2]]}"; "tuple","{\"#tup\":[1,true]}"; "bigint","{\"#bigint\":\"999999999999999999999999999999999999\"}"; "variant record","{\"tag\":\"Some\",\"value\":42}"; "unicode","\"λ😀\"" ] do check name (read value |> Result.isOk)
check "invalid UTF8" (Itf.read Itf.defaultLimits [|255uy|] |> Result.isError)
check "byte limit" (Itf.read { Itf.defaultLimits with MaxBytes=2 } (input "1") |> Result.isError)
check "depth limit" (Itf.read { Itf.defaultLimits with MaxDepth=3 } (input "[[[1]]]") |> Result.isError)
check "collection limit" (Itf.read { Itf.defaultLimits with MaxCollection=2 } (input "[1,2,3]") |> Result.isError)
let set1 = QuintReplay.encodeValue (Set [Integer "02";Integer "1"])
let set2 = QuintReplay.encodeValue (Set [Integer "1";Integer "2"])
check "legacy unordered set canonicalization" (set1 = set2)
check "legacy duplicate record" (QuintReplay.encodeValue (Record ["a",Null;"a",Null]) |> Result.isError)
let state x =
    let draft = { Identity=""; Bindings=["x",Integer(string x)] }
    { draft with Identity=QuintReplay.stateFingerprint draft |> unwrap }
let fingerprint = String.replicate 64 "a"
let source = { Path="test.qnt";Line=1;Column=1 }
let environment = {Seed="42";Bounds=["steps",2L];ToolFingerprint=fingerprint;ProfileFingerprint=fingerprint;ContractFingerprint=fingerprint;AdapterFingerprint=fingerprint;ImplementationFingerprint=fingerprint}
let draft = {SchemaVersion=1;TraceIdentity="";Environment=environment;Initial=state 0;Steps=[for i in 1..2 -> {Index=i;Action="inc";Source=source;Expected=state i}]}
let trace = {draft with TraceIdentity=QuintReplay.traceFingerprint draft |> unwrap}
let mutable cleaned = 0
let driver = {
    Initialize=fun _ _ -> Task.FromResult(Ok(ref 0))
    Apply=fun _ runtime _ -> runtime.Value <- runtime.Value+1;Task.FromResult(Ok ())
    Observe=fun runtime _ -> Task.FromResult(Ok(state runtime.Value))
    Cleanup=fun _ _ -> cleaned <- cleaned+1;Task.FromResult(Ok ())
}
let runWith timeout ct d t = Replay.run timeout ct d t |> fun task -> task.GetAwaiter().GetResult()
let run d t = runWith (TimeSpan.FromSeconds(2.0)) CancellationToken.None d t
check "replay agreement" ((run driver trace).Outcome = ReplayOutcome.Equivalent)
check "cleanup on success" (cleaned = 1)
let divergence = run {driver with Observe=fun _ _ -> Task.FromResult(Ok(state 99))} trace
check "initial divergence" (match divergence.Outcome with ReplayOutcome.Diverged(0,_,_,_,_,_) -> true | _ -> false)
check "no effects after initial mismatch" (divergence.AppliedSteps=0)
let failed = run {driver with Apply=(fun _ _ _ -> Task.FromResult(Error "uncertain effect"));Cleanup=(fun _ _ -> Task.FromResult(Error "cleanup failed"))} trace
check "apply failure never retried" (failed.AppliedSteps=1)
check "cleanup failure retained" (failed.CleanupFailure=Some "cleanup failed")
check "original failure retained" (match failed.Outcome with ReplayOutcome.DriverFailure("apply",1,"uncertain effect") -> true | _ -> false)
let invalid = run driver {trace with TraceIdentity=fingerprint}
check "invalid trace refused" (match invalid.Outcome with ReplayOutcome.InvalidTrace _ -> true | _ -> false)
let cancelled = new CancellationTokenSource()
cancelled.Cancel()
check "cancellation" ((runWith (TimeSpan.FromSeconds(2.0)) cancelled.Token driver trace).Outcome=ReplayOutcome.Cancelled)
let slow =
    { driver with
        Apply = fun _ _ ct -> task {
            do! Task.Delay(10000, ct)
            return Ok ()
        }
    }
check "cooperative deadline" ((runWith (TimeSpan.FromMilliseconds(50.0)) CancellationToken.None slow trace).Outcome=ReplayOutcome.TimedOut)
let observations = trace.Steps |> List.map (fun s -> {Index=s.Index;Action=s.Action;Source=s.Source;Actual=s.Expected})
check "missing terminal observation" (match QuintReplay.compare trace observations[..0] |> unwrap with QuintReplayResult.Diverged d -> d.Step=2 | _ -> false)
check "extra observation" (match QuintReplay.compare trace (observations @ [{observations.Head with Index=3}]) |> unwrap with QuintReplayResult.Diverged d -> d.Reason="unexpected-observation" | _ -> false)
let quint = Environment.GetEnvironmentVariable "QUINT_BIN"
if not (String.IsNullOrWhiteSpace quint) then
    let request = {Executable=quint;Sha256="939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f";Version="0.32.0";WorkingDirectory=Directory.GetCurrentDirectory();Source="examples/BoundedQueue/queue_test.qnt";Module="queue_test";Command=QuintCommand.Test "fifoTest";Timeout=TimeSpan.FromSeconds(10.0);MaxOutputBytes=100000}
    let tool r = Quint.run r CancellationToken.None |> fun t -> t.GetAwaiter().GetResult()
    let real = tool request
    check (sprintf "real named test: %A" real) (real.Outcome=ToolOutcome.TestsPassed 1)
    check "zero named tests rejected" ((tool {request with Command=QuintCommand.Test "does_not_exist"}).Outcome=ToolOutcome.Incomplete)
    check "tool digest mismatch" ((tool {request with Sha256=fingerprint}).Outcome=ToolOutcome.IdentityMismatch)
    check "tool version unsupported" ((tool {request with Version="0.0.0"}).Outcome=ToolOutcome.Unsupported)
printfn "%d checks passed" count
