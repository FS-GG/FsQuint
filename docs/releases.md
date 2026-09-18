# Release qualification

Package versions and Quint executable versions are independent. FsQuint and
FsQuint.Tooling publish together from an immutable tag. Release readback compares
payloads from both feeds, excluding NuGet's repository signature, and checks source
commit identity. An anonymous external directory restores and runs the queue example.

## 0.1.0 candidate

The stable candidate includes the preview 2 Unicode fix and the same public API,
canonical schema and Quint 0.32.0 tool matrix. Promotion requires completion of both
consumer migrations, actual package update PRs and rejected-update rollback evidence.
See the [canonical roadmap](roadmaps/fsquint.md) for live completion status.

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
