# Independent bounded queue

Install .NET SDK 10.0.401. Once the pinned package is published, copy this directory,
`../../global.json` and `../../NuGet.Config` into an otherwise empty directory, then:

```sh
dotnet run --project BoundedQueue.fsproj
```

The project restores FsQuint from nuget.org. No Quint installation is needed to replay
the committed trace. `Program.fs` runs a real mutable queue implementation: it never
sets the implementation state to the next expected state. A deliberately broken LIFO
removal diverges at step 3; an observation mutation diverges at step 1. Malformed ITF
is refused before any implementation action.

The model describes one sequential queue with capacity two, atomic enqueue/dequeue,
integer values 1 and 2, no failures or inter-process communication. FIFO conservation
is `inserted == removed ++ items`. `filled` and `drained` are reachability witnesses.
The scheduled trace ends after four operations; the general queue remains serviceable.
This example does not model concurrent clients, persistence, timing or fairness.

Regenerate with caller-provisioned Quint 0.32.0 for Linux x64 (SHA-256
`939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f`):

```sh
quint typecheck queue.qnt
quint run queue.qnt --max-samples 100 --max-steps 20 --invariant fifo --witnesses filled --witnesses drained --seed 42
quint test queue_test.qnt --match fifoTest --seed 42
quint run scenario.qnt --step scheduledStep --max-samples 1 --max-steps 4 --seed 42 --invariant fifo --out-itf queue.itf.json
```

The checked-in fixture was generated from repository root with the same arguments and
`examples/BoundedQueue/` source/output prefixes. Quint embeds generation timestamp and
source path, so regenerated raw bytes differ; compare decoded states for semantic
reproduction. Retain raw bytes whenever they are used as evidence. The action schedule
is explicitly authored in `scenario.qnt` and `Program.fs`, never guessed from states.
All source and generated fixture content in this example is MIT-licensed.
