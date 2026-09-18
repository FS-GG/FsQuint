# Compatibility and ownership

| Axis | Initial contract |
|---|---|
| Package API | 0.1.0-preview.2; prerelease, not stable |
| Target | net10.0; SDK 10.0.401; runtime 10.0.12 |
| FSharp.Core floor | 10.1.302, tested with the current SDK |
| Replay envelope / canonicalization | schema 1; existing SDD valid-value identities preserved |
| Raw ITF canonicalization | 1; explicit maps, tuples and sets |
| Raw dialect | Quint 0.32.0 ITF; Rust and TypeScript generated fixtures |
| Process wrapper | Linux x64; Quint 0.32.0; Rust test/simulation backend |
| Projection | Always consumer-owned and separately fingerprinted |

Keep package and tool pins separate. Any change to decoding, comparison, raw input,
projection, driver or tool must be assessed against consumer evidence reuse; a package
version alone neither proves equivalence nor licenses reuse. Store immutable raw
fixtures and provenance; do not refresh expected observations to make an update pass.
On failure, retain the prior immutable pin and its matching evidence. No runtime auto-update.

The SDD namespace and CLR types stay in its assembly during migration. Its compatibility
facade translates only; FsQuint owns generic algorithms. Coordination owns hosted-writer
policies and Choreo parsing/projection. FsQuint contains no FS.GG package reference.
The linked generic replay harness is removed when Coordination adopts the public package.

FsQuint maintainers own issue triage, generic regressions, API/schema versioning and
release integrity. Consumers own model validity, effect isolation and adoption. Report
bugs with package/tool versions, smallest redistributable fixture, projection and exact
outcome. Do not include credentials or private trace data. Fixes target the newest release. During the 0.1 line, patch releases preserve public
API and valid canonical identities; breaking changes require a new minor line and explicit
migration notes. Deprecated SDD CLR types remain owned by SDD until its own major release.
Tool-matrix extensions need genuine generated fixtures and process qualification. No
service SLA or older-line backport commitment is implied. Consumer update PRs must pass
their own gates; maintainers do not bypass them or automatically refresh observations.
