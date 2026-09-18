# FsQuint

Quint trace validation and implementation correspondence for F# on .NET 10.
A trace match establishes agreement for the selected trace and explicit projection;
it is not an unbounded proof of correctness.

Packages are available on [nuget.org](https://www.nuget.org/packages/FsQuint).
Use them from any F# project targeting .NET 10; no FS-GG project setup is required.

- **FsQuint**: bounded raw ITF decoding, exact values, stable replay fingerprints,
  validation, comparison and a cooperative async driver lifecycle.
- **FsQuint.Tooling**: optional, explicitly pinned Quint process execution on Linux x64.
  Tools are provisioned by the caller; no download or installation occurs in the library.

The [bounded queue example](examples/BoundedQueue) includes a genuine Quint-generated
trace and an independently implemented F# queue. Its positive control and three negative
controls run without FS-GG services, repository conventions or credentials.

Install the core package in your project:

```sh
dotnet add package FsQuint --version 0.1.0
# Optional process wrapper:
dotnet add package FsQuint.Tooling --version 0.1.0
```

Read a trace produced by Quint:

```fsharp
open System.IO
open FsQuint

match Itf.read Itf.defaultLimits (File.ReadAllBytes "trace.itf.json") with
| Ok trace -> printfn "Read %d states" trace.States.Length
| Error diagnostics -> failwithf "Invalid trace: %A" diagnostics
```

For implementation replay, the [queue example](examples/BoundedQueue) shows state
fingerprints, a driver, exact comparison and deliberate divergence controls. The
[usage guide](docs/usage.md) explains attribution, cancellation and tool outcomes.

Build this repository with SDK **10.0.401**, pinned in `global.json`:

```sh
dotnet run --project tests/FsQuint.Tests
# Include actual Quint tooling checks with the qualified binary:
QUINT_BIN=/absolute/path/to/quint dotnet run --project tests/FsQuint.Tests
```

See the [roadmap](docs/roadmaps/fsquint.md), [extraction decisions](docs/decisions.md)
and [source notices](NOTICE.md). The [compatibility policy](docs/compatibility.md)
defines the supported API, trace dialect and tooling platform.
