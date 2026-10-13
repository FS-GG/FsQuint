# Release qualification

Package versions and Quint executable versions are independent. FsQuint and
FsQuint.Tooling publish together from an immutable tag. Release readback compares
payloads from both feeds, excluding NuGet's repository signature, and checks source
commit identity. An anonymous external directory restores and runs the queue example.

## 0.2.0 bounded batch migration (planned)

FsQuint and FsQuint.Tooling move together to the 0.2 line. This is a prospective
release: package identity collision checks, immutable producer dependencies and
installed consumer qualification remain pending. The pure core, public F# signatures,
schema-v1 fingerprints and replay identities remain unchanged.

Tooling adopts a bounded Linux x64 batch profile. Executable and normalized working
paths admit at most 2048 UTF8 bytes each; the combined executable, directory, argument
and explicit environment request admits at most 256 KiB. Larger requests return launch
failure. Child stdin is closed. These limits change accepted requests from the 0.1
wrapper, even though source signatures stay the same. A successful version probe may
precede a refused workload request.

Migration requires paths within the encoded limits, smaller argument/module/test
patterns or partitioned batches, and no dependency on inherited or interactive stdin.
Callers that need the old behavior retain their qualified immutable 0.1 pin and its
matching evidence until they choose an upgrade or a reviewed capability extension.
No hidden path aliases, environment transports or fallback runner restore old behavior.

Any positive output cap remains supported, shared across stdout/stderr separately for
each version or workload invocation after BOM-aware decoding. The full work deadline
still starts before hashing and covers both invocations. Cleanup observation has a
one-second budget beyond that work deadline; cooperative scheduling and report
materialization do not establish a hard return-time guarantee. Tree termination is best
effort. Unresolved sessions retain local custody, limited to sixteen slots; exhaustion
returns launch failure. A report does not prove descendant emptiness or reaping.

Acceptance requires existing in-profile outcomes and focused request, input, output,
encoding, cancellation, deadline and cleanup controls using genuine immutable producer
packages. Upgrade and rollback retain their own package pins and evidence. Publication,
installed qualification and downstream adoption are separate claims.

## 0.1.0

The stable release includes the preview 2 Unicode fix and the same public API,
canonical schema and Quint 0.32.0 tool matrix. Release qualification requires both consumer migrations, actual package update PRs
and rejected-update rollback evidence. CI and publication explicitly provision the
checksum-pinned Rust evaluator 0.6.0 as well as Quint 0.32.0; no implicit evaluator
download is needed during qualification.
[Stable release verification](https://github.com/FS-GG/FsQuint/actions/runs/35330871321)
passed for both feeds and anonymous external consumption. SDD and Coordination
merged stable adoption; SDD Artifacts/CLI 2.0.2 are publicly qualified, including
previous-client binary compatibility and 58 installed-package checks.
The [canonical roadmap](roadmaps/fsquint.md) records completed FQ0–FQ7 evidence.

## 0.1.0-preview.2

[PR4](https://github.com/FS-GG/FsQuint/pull/4) rejects malformed UTF-16 before
fingerprinting. Previously, an unpaired surrogate collided with U+FFFD through UTF-8
replacement fallback. Regression controls cover values, keys, bindings and provenance;
valid Unicode and original SDD canonical identities are unchanged. 73 checks pass.
[Readback](https://github.com/FS-GG/FsQuint/actions/runs/35321460892) verifies both feeds
and anonymous external use. The first release check timed out on NuGet indexing;
recovery was read-only and did not replace archives or tags.

## 0.1.0-preview.1

Initial bounded ITF reader, schema-v1 replay, asynchronous driver and optional pinned
process wrapper; independent queue and real tool fixtures. 65 checks passed.
[Readback](https://github.com/FS-GG/FsQuint/actions/runs/35320082755) completed after
repairing cross-host redirect credentials. The malformed UTF-16 issue above is a known
limitation of this historical version.
