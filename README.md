# FsQuint

Quint trace validation and implementation correspondence for F# on .NET 10.
A trace match establishes agreement for the selected trace and explicit projection;
it is not an unbounded proof of correctness.

Preview packages are available on [nuget.org](https://www.nuget.org/packages/FsQuint).
Stable qualification and consumer migration are in progress.

- **FsQuint**: bounded raw ITF decoding, exact values, stable replay fingerprints,
  validation, comparison and a cooperative async driver lifecycle.
- **FsQuint.Tooling**: optional, explicitly pinned Quint process execution on Linux x64.
  Tools are provisioned by the caller; no download or installation occurs in the library.

The [bounded queue example](examples/BoundedQueue) includes a genuine Quint-generated
trace and an independently implemented F# queue. Its positive control and three negative
controls run without FS-GG services, repository conventions or credentials.

Build with SDK **10.0.401**, pinned in `global.json`:

```sh
dotnet run --project tests/FsQuint.Tests
# Include actual Quint tooling checks with the qualified binary:
QUINT_BIN=/absolute/path/to/quint dotnet run --project tests/FsQuint.Tests
```

See the [roadmap](docs/roadmaps/fsquint.md), [extraction decisions](docs/decisions.md)
and [source notices](NOTICE.md). Public packages and consumer migrations remain release gates.
