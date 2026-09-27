# FsQuint

FsQuint reads Quint traces and checks F# implementations against them on .NET 10.
It includes integration examples for [Automata](https://github.com/byzantine-systems/automata),
an F# library for hierarchical statecharts and durable command processing.
The packages work in any F# project targeting .NET 10; no FS-GG setup is required.

[Quint](https://quint.sh/) is an open-source specification language for modeling
systems as states and transitions, then checking their properties. A trace records
one sequence of states. FsQuint reads traces in the Informal Trace Format (ITF) and
supports replay against an implementation through a caller-supplied driver. The
caller maps implementation state to model state for comparison. A match establishes
agreement for that trace and mapping; it is not an unbounded proof of correctness.

## What you can check

| Example | What it demonstrates |
|---|---|
| [Bounded queue](examples/BoundedQueue) | Replay an independent F# implementation; detect FIFO and projection defects; reject malformed traces before execution. |
| [Automata charts](examples/AutomataReplay) | Check approval and turnstile charts, hierarchical resolver semantics, ordered effects and correction planning against independent Quint models. |
| [Automata runtime](examples/AutomataReplay/PROTOCOL.md) | Exercise real processors and dispatchers through seven controlled fault schedules; detect broken fencing, duplicate finalization and missing effect deduplication. |
| [Native providers](examples/AutomataProviders) | Qualify SQLite and PostgreSQL contracts, worker-crash recovery and PostgreSQL correction persistence with real stores. |
| [CI workflow design](examples/CiPreflight) | Find a missing job dependency by exploring execution orders before relying on the workflow. |

Committed traces can be replayed without a Quint installation. Generating traces and
checking model properties require the pinned Quint toolchain. Native provider checks
have their own database prerequisites and qualification boundaries.

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

To check an implementation, supply a replay driver that initializes its real state,
applies explicitly bound inputs and projects observations into the model's values.
Keep expected states outside the implementation callbacks so they cannot hide a defect.
FsQuint reports the first divergence. The [bounded queue example](examples/BoundedQueue)
provides a complete driver; the [usage guide](docs/usage.md) covers cancellation,
cleanup and tool outcomes.

## Quint, Automata and FsQuint

The three projects have distinct roles:

- **Quint** specifies allowed behavior and produces model executions as ITF traces.
- **[Automata](https://github.com/byzantine-systems/automata)** executes F# statecharts
  and durable command processing.
- **FsQuint** compares observations from the actual implementation with the selected
  model execution, using explicit input bindings and projections.

The [implemented integration](docs/automata-integration.md) includes independent
application, resolver, correction and protocol models; reproducible fixtures; coverage
checks; and seeded defects that confirm the comparisons detect incorrect behavior.
State, hierarchy paths, refusals and ordered effects are checked where the profile
requires them. Structural chart fingerprints alone do not identify callback behavior.

Run the committed-trace consumer from this checkout:

```sh
dotnet pack src/FsQuint/FsQuint.fsproj -c Release -o artifacts/packages
python3 examples/AutomataReplay/check.py
```

This creates an isolated package consumer and verifies dependency provenance before
running the examples. Restore needs NuGet access; fixture replay needs neither Quint
nor a database. The examples pin Automata **0.5.0** and add no Automata dependency to
FsQuint's public packages. The shared replay helper remains example source, with no
separate public adapter package.

[Native qualification](examples/AutomataProviders/README.md) is a separate tier:
118 SQLite and 152 PostgreSQL upstream tests, public-API worker-kill recovery checks,
and a corrected-history restart witness. PostgreSQL qualification uses pinned
**19beta3**, PGMQ and pg_cron. SQLite does not support temporal correction; its explicit
refusal and entity-release behavior are tested. These results do not establish
arbitrary-history correctness, replication safety or exactly-once external effects.

All nine stages of the [integration roadmap](docs/roadmaps/2026-09-27-090245-quint-automata-integration.md)
are closed with merge evidence. Exporters, finite callback exploration, shared IR and
historical trace validation were [explicitly deferred](docs/roadmaps/2026-09-27-quint-automata-expansion-decisions.md)
until they have a named consumer and maintenance owner.

## Check CI workflow designs

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

## Run repository checks

Use SDK **10.0.401**, pinned in [global.json](global.json):

```sh
dotnet run --project tests/FsQuint.Tests
```

Run the complete package and external-consumer checks:

```sh
bash eng/check.sh
```

To include model regeneration and Quint process checks, follow the
[Linux tooling setup](docs/usage.md#reproducing-the-linux-tooling-checks), which
provisions both Quint and its Rust evaluator with verified checksums, then run the
checks with `QUINT_BIN` and `QUINT_HOME` configured. For isolated databases, use the
[native-provider instructions](examples/AutomataProviders/README.md#reproduce).
CI runs the model/consumer checks and native provider qualification nightly, with
provider checks also triggered by relevant changes.

See the [compatibility policy](docs/compatibility.md) for supported APIs, trace dialect
and tooling platform; the [roadmap](docs/roadmaps/fsquint.md) and
[extraction decisions](docs/decisions.md) for design context; and
[source notices](NOTICE.md) for attribution.
