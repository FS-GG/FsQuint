# FSC-08 packed archive characterization

> Historical note: the Python readback experiments described here have been superseded by the F# qualification tool in [Qualification](Qualification/README.md). This document preserves the original findings.

This source-only fixture is stacked on the F# archive fingerprint draft #15. It
compares locally packed `.nupkg` member names, SHA-256 payload digests, and ZIP
Unix mode bits against an independent Python `zipfile` enumeration. It mirrors
the local `payloads()` projection in `eng/readback.py` without importing that
script, whose top level fetches served packages. It then mutates copies of both
packages to prove refusals for wrong names, payload and `.nuspec` bytes, and
nested signature members; a mode-only difference retains payload equality. A
feed-added root `.signature.p7s` stays excluded. Repacking the same payload
with different ZIP container bytes passes.

The live offline readback controls use the F# qualification project:

```sh
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- selftest
```

The local archives carry 13 and 10 payload members respectively at this
checkpoint, all with mode `100644`. The Python and F# cross-feed comparators
now compare names and payload digests; F# inspection retains Unix mode facts.
This fixture does not fetch a served package, validate feed receipts, verify a
release commit, publish a package, or install a receiver. Real served archives
and the live `.nuspec` commit check remain prerequisites for parity.
