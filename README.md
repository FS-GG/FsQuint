# FsQuint

FsQuint reads Quint traces and checks F# implementations against them on .NET 10.
The packages work in any F# project targeting .NET 10; no FS-GG setup is required.

[Quint](https://quint.sh/) is an open-source specification language for modeling
systems as states and transitions, then checking their properties. A trace records
one sequence of states. FsQuint reads traces in the Informal Trace Format (ITF) and
supports replay against an implementation through a caller-supplied driver. The
caller maps implementation state to model state for comparison. A match establishes
agreement for that trace and mapping; it is not an unbounded proof of correctness.

## Harden CI workflows before relying on them

Quint can check a CI design when the workflow is created or materially changed.
Model the relevant job state and required outcomes, explore possible execution
orders, then harden the real workflow when Quint finds a counterexample. Keep the
model tied to the workflow so later changes cannot silently invalidate the result.

The [CI workflow example](examples/CiPreflight) demonstrates a report job missing
one test-shard dependency. Quint finds an order where the report runs too early,
and the corrected design passes the same property. Its command-gating demonstration
shows how a changing plan could block dependent work. For a fixed workflow, use it
as a change-time robustness check. It targets mistakes represented in the model;
a passing sampled check does not replace the actual test suite.

Related Quint tools:

- [Quint CLI](https://quint.sh/docs/quint): generate ITF traces, including with its Rust evaluator.
- [Quint Connect](https://github.com/quint-co/quint-connect): model-based testing
  that replays Quint traces against Rust implementations.
- [Quint Trace Explorer](https://github.com/quint-co/quint-trace-explorer): a terminal
  interface for inspecting ITF traces and state changes.

## Install

- [FsQuint](https://www.nuget.org/packages/FsQuint): trace decoding with explicit
  resource limits, exact values, stable fingerprints, validation and asynchronous replay.
- [FsQuint.Tooling](https://www.nuget.org/packages/FsQuint.Tooling): optional execution
  of explicitly pinned Quint tools on Linux x64. The caller provisions the tools;
  the library does not download or install them.

```sh
dotnet add package FsQuint --version 0.1.0
# Optional process wrapper:
dotnet add package FsQuint.Tooling --version 0.1.0
```

## Read and replay traces

Read a trace produced by Quint:

```fsharp
open System.IO
open FsQuint

match Itf.read Itf.defaultLimits (File.ReadAllBytes "trace.itf.json") with
| Ok trace -> printfn "Read %d states" trace.States.Length
| Error diagnostics -> failwithf "Invalid trace: %A" diagnostics
```

The [bounded queue example](examples/BoundedQueue) replays a Quint-generated trace
against an independently implemented F# queue. It demonstrates matching behavior,
an implementation bug, a state-mapping error and rejection of a malformed trace.
It runs without Quint installed or FS-GG services and credentials. The
[usage guide](docs/usage.md) covers replay drivers, cancellation and tool outcomes.

## Run repository checks

Use SDK **10.0.401**, pinned in [global.json](global.json):

```sh
dotnet run --project tests/FsQuint.Tests
```

To include Quint process checks, follow the
[Linux tooling setup](docs/usage.md#reproducing-the-linux-tooling-checks), which
provisions both Quint and its Rust evaluator with verified checksums.

See the [compatibility policy](docs/compatibility.md) for supported APIs, trace dialect
and tooling platform; the [roadmap](docs/roadmaps/fsquint.md) and
[extraction decisions](docs/decisions.md) for design context; and
[source notices](NOTICE.md) for attribution.
