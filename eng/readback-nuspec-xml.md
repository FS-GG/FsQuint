# FSC-08 source-only `.nuspec` XML identity guard

The Python release readback previously parsed the served root `.nuspec` XML,
selected the first descendant `repository` element, and checked only its
`commit` when a caller supplied one. It could accept a wrong package ID or
version, duplicate metadata fields, a foreign XML root, and a DTD entity
whose expanded value looked valid. The F# identity candidate refused each.

`eng/test-readback-nuspec-xml.py` compiles the actual served-archive identity
block and pure helpers from Python source AST without running feed requests or
receipt writes. It invokes the F# identity verifier separately over eleven
independently authored packed archives. Seven cases were red before repair:
wrong ID, wrong version, duplicate metadata, duplicate ID, duplicate
repository, foreign XML root, and DTD entity. The two valid forms, malformed
XML and wrong commit, provide positive and existing-refusal controls.

The proposed Python helper refuses DTDs through the XML parser's doctype
callback, requires one `package` root and one direct `metadata`, `id`,
`version`, and `repository` element by local name, and matches ID and version
to the selected package. It retains the existing commit comparison when an
expected commit is supplied. Both locally packed 0.1.0 packages passed the
source-only XML identity check, without making those packs a release baseline.

The stacked commit-source draft checks a caller value against the local
version tag's commit object; tag protection and publication authority remain
unproved. This draft does not bound decompression or prove complete
Python/F# parser parity. Archive equality remains exact ZIP names and payload
SHA-256 digests; Unix mode remains inspection evidence and a symlink guard.
Acceptance of #15 through #22, access to both served feeds, and installed
receiver parity remain separate gates. No feed request, readback output,
publication, merge, or receiver flip occurs here.
