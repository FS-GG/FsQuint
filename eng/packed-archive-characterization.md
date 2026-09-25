# FSC-08 packed archive characterization

This source-only fixture is stacked on the F# archive fingerprint draft #15. It
compares locally packed `.nupkg` member names, SHA-256 payload digests, and ZIP
Unix mode bits against an independent Python `zipfile` enumeration. It mirrors
the local `payloads()` projection in `eng/readback.py` without importing that
script, whose top level fetches served packages. It then mutates copies of both
packages to prove refusals for wrong names, payload and `.nuspec` bytes, Unix
mode, and nested signature members. A feed-added root `.signature.p7s` stays
excluded. Repacking the same payload with different ZIP container bytes passes.

Run after locked restore, using only local files:

```sh
dotnet restore src/FsQuint/FsQuint.fsproj --locked-mode
dotnet restore src/FsQuint.Tooling/FsQuint.Tooling.fsproj --locked-mode
dotnet pack src/FsQuint/FsQuint.fsproj -c Release --no-restore -o /tmp/fsquint-fsc08-local-packs
dotnet pack src/FsQuint.Tooling/FsQuint.Tooling.fsproj -c Release --no-restore -o /tmp/fsquint-fsc08-local-packs
python3 eng/test-packed-archive-fingerprint.py /tmp/fsquint-fsc08-local-packs/FsQuint.0.1.0.nupkg /tmp/fsquint-fsc08-local-packs/FsQuint.Tooling.0.1.0.nupkg
```

The local archives carry 13 and 10 payload members respectively at this
checkpoint, all with mode `100644`. The Python readback compares names and
payload digests; Unix mode comparison is a candidate strengthening. This
fixture does not fetch a served package, validate feed receipts, verify a
release commit, publish a package, or install a receiver. Real served archives
and the live `.nuspec` commit check remain prerequisites for parity.
