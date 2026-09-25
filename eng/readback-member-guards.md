# FSC-08 source-only Python archive member guards

The live Python `eng/readback.py` computes a name-to-SHA-256 payload map for
each qualified and served archive. Before this draft, its `payloads()` function
accepted ZIP symlink members and unsafe names. A symlinked root
`.signature.p7s` was particularly easy to miss because that entry is omitted
from the payload map. The F# candidate already refused these entries.

`eng/test-readback-member-guards.py` extracts only the real Python `payloads()`
function from its AST, avoiding the module's feed requests and receipt output.
Its independently authored ZIP archives showed nine red-before false greens:
symlink payload and signature entries, plus parent, absolute, dot-segment,
backslash, colon, empty-segment, and blank member names. F# refused each.
The Python function now checks every entry before hashing, including a root
signature, and refuses those cases. A stacked draft separately rejects ASCII
case-alias names in both readers. It still returns the same name-to-digest
map for valid archives; a regular-file mode-only change and feed-added root
signature remain accepted. The selected cross-feed equality contract is still
exact member names and payload digests, with mode used only to detect symlinks.

This is a proposed source repair in a draft branch, not an installed receiver
change. The test performs no feed request or readback write. Real GitHub
Packages served archives remain inaccessible as reported in #18, so both-feed
qualification, acceptance of #15 through #19, and installed receiver parity
remain prerequisites. These guards do not establish full Python/F# parity or
publication authority.
