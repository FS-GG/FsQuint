# FSC-08 source-only `.nuspec` root selection

The Python release readback previously chose the first ZIP member whose name
ended in `.nuspec`. A nested manifest, a foreign root manifest, or the first
of two root manifests could supply the repository commit and pass readback
when qualified and served payload maps matched. The F# identity candidate
already requires exactly one manifest at `<package>.nuspec`.

`eng/test-readback-nuspec-root.py` extracts the actual Python `spec` selection
expression and `payloads()` function from the source AST, without running
its feed-fetching or receipt-writing top level. It separately invokes the F#
identity verifier over independently authored ZIP archives. Before repair,
Python selected all three malformed manifest sets while F# refused them.
The Python selector now requires exactly one case-insensitive `.nuspec`
candidate and an exact root name for the selected package. The test also
covers a valid root manifest with feed-added signature and a missing manifest.
Both local 0.1.0 packages have the exact expected root names.

This bounded change does not validate the XML package ID or version, reject
duplicate XML metadata/repository elements or DTDs, or prove the caller's
expected commit is authoritative. The F# candidate has separate checks for
several of those cases; full Python/F# parity remains open. Exact ZIP member
names and SHA-256 payload digests remain cross-feed equality keys, while Unix
mode remains inspection evidence and a symlink guard. No feed request,
readback output, package publication, merge, or receiver switch occurs in
this draft. Acceptance of #15 through #21, GitHub Packages served-byte
access, and installed receiver parity remain prerequisites.
