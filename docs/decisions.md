# Extraction decisions — 2026-09-18

FSQUINT-01 implementation is authorized by the user's subsequent instruction to complete
the roadmap and make all decisions. This supersedes the design's planning-only authority.

FsQuint maintainers (FS-GG) own the MIT core, tooling, releases and generic fixes. The
migration window is this extraction programme, ending before the first stable release.
SDD and Coordination keep domain models, policy, qualification and adoption ownership.

The user explicitly selected the latest .NET 10. All projects target net10.0, with SDK
10.0.401 (runtime 10.0.12) and FSharp.Core 10.1.302. FSharp.Core retains the consumers’ 10.1.302 minimum to avoid an unrelated organization-wide
package baseline upgrade; the SDK/compiler and runtime remain the latest .NET 10.
The SDK archive was checked against
Microsoft's release-metadata SHA-512. No net8.0 or Fable support is claimed.

The repository is public: https://github.com/FS-GG/FsQuint. Package IDs FsQuint and
FsQuint.Tooling returned 404 from nuget.org before implementation; publication, not this
check, establishes ownership. Preview versions are intentional under the approved roadmap.

SDD's MIT generic values, validation, fingerprints and comparison are the licensed
extraction baseline at 7013aa1915a37c341107fcf9a66f87f3f73cc8c8. SDD will retain its CLR
records/unions and namespace, mapping losslessly to the package. Source aliases alone
would not preserve binary identity. Retire the facade only through a separately reviewed
major SDD API change after consumers migrate.

No Coordination implementation is copied. Its public Initialize/Apply/Observe concept
informs a newly implemented engine with cancellation and cleanup. No Choreo source or
fixture is redistributed here. The queue model and F# example are original MIT assets;
raw fixtures are outputs of that model using the pinned upstream tool.

Raw ITF and schema-v1 replay are separate APIs. Raw ITF preserves tuples, maps and arbitrary
integers; the existing replay union remains unchanged. A domain projection must explicitly
choose what it can represent. Quint variant tag/value objects stay records; the library
does not guess that every such record is a discriminated union.

Initial tooling support: Linux x64, Quint 0.32.0, Rust simulation/test backend. No bundled
tools, downloads or verifier are implied. Bounded model checking remains caller-owned;
the first tooling API exposes typecheck, named tests and sampled runs only. This explicitly
narrows §6's proposed verifier support: no verification outcome can be fabricated by this
API. Existing Coordination formal gates continue using their qualified toolchain.

Replay cancellation/deadlines are cooperative. Cleanup always runs after successful
initialization, with a fresh token. Cleanup failures are retained separately from the
original outcome. Consumers must require both Equivalent and no cleanup failure.
Initialize owns disposal if it fails before returning a runtime. Hard containment requires
an isolated worker process; the engine cannot kill arbitrary in-process user code.
