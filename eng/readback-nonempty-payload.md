# FSC-08 nonempty archive payload guard

The Python `payloads()` projection previously returned an empty or
directory-only map for archives without a regular payload member. The F#
archive inspector refused those archives. An independent offline fixture
showed four Python projection false greens: empty ZIP, root-signature-only,
directory-only, and directory-plus-signature archives. Three positive
controls cover a file, a file with feed-added root signature, and a directory
with a file.

The Python projection now requires at least one member that is neither a
directory nor the excluded root `.signature.p7s`, matching the F# source
guard. Exact ZIP names and SHA-256 payload digests remain cross-feed equality
keys; Unix mode remains inspection evidence and a symlink guard. The stacked
root `.nuspec` selector also refuses these malformed archives in the complete
served-archive path, so this is a narrower projection parity repair. The test
extracts only the real Python function from source AST and invokes F#
separately. It performs no feed request or readback write.

This source-only control does not prove protected release authority, served
archives from either feed, or installed receiver parity. Acceptance of #15
through #25 and retesting after upstream movement remain separate. No feed
run, publication, merge, or receiver flip occurs in this draft.
