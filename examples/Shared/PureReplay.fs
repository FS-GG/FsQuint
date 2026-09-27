// Example-local helper: no Automata dependency and no oracle passed to domain callbacks.
module PureReplay

open System
open System.Threading
open System.Threading.Tasks
open FsQuint

type BoundInput<'input> = { Index: int; OperationId: string; Input: 'input }

let run construct reduce observe cleanup (bindings: BoundInput<'input> list) (trace: QuintReplayTrace) =
    if bindings.Length <> trace.Steps.Length then invalidArg "bindings" "Input count differs from trace"
    List.zip bindings trace.Steps |> List.iteri(fun index (binding,step) ->
        if binding.Index <> index+1 || step.Index <> binding.Index || step.Action <> binding.OperationId then
            invalidArg "bindings" "Input index or operation identity differs from trace")
    let inputs = bindings |> List.map(fun binding -> binding.Input) |> List.toArray
    let driver = {
        Initialize = fun _ _ -> Task.FromResult(construct() |> Result.map ref)
        Apply = fun step runtime _ ->
            match reduce runtime.Value inputs[step.Index-1] with
            | Ok next -> runtime.Value <- next; Task.FromResult(Ok())
            | Error error -> Task.FromResult(Error error)
        Observe = fun runtime _ -> Task.FromResult(observe runtime.Value)
        Cleanup = fun runtime _ -> Task.FromResult(cleanup runtime.Value) }
    Replay.run (TimeSpan.FromSeconds 5.0) CancellationToken.None driver trace
    |> fun task -> task.GetAwaiter().GetResult()
