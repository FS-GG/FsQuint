# FSC-08 source-only expected commit selection

The Python readback previously trusted its optional command-line expected
commit, or `GITHUB_SHA` when no argument was supplied. A caller could provide
the commit recorded by the wrong served package, and two feeds with matching
wrong bytes would pass. If neither value existed, the `.nuspec` commit check
was skipped entirely. A missing version tag did not stop either path.

The proposed source selector resolves `refs/tags/v<version>^{commit}` from the
checked-out Git repository to a full 40-character lowercase commit object ID.
That object ID is the expected `.nuspec` commit. A supplied CLI or event commit
must match it; a missing tag or malformed result fails closed before readback
output is created. The release workflow's tag-push path supplies `GITHUB_SHA`,
while read-only recovery already resolves the version tag and supplies its
commit. The selector independently checks both against the local tag.

`eng/test-readback-commit-source.py` extracts the actual expected-commit
assignment and pure helpers from Python source AST without running its feed
or receipt top level. Its disposable Git repository has a tag on source A and
a later foreign source B. Before repair, a caller-supplied B accepted two
matching B packages, no supplied commit disabled identity checking, and a
missing tag still allowed a caller value. The selected tag commit now rejects
those cases; matching CLI and event commits pass. The real repository's
`v0.1.0` tag resolved read-only to
`34eeca981c136144ced73a3f98b5ce04218e89c7` at this checkpoint.

Git commit object IDs are immutable once selected, but a tag reference can be
retargeted. This source-only check does not prove protected tags, a signed
tag, an independently sealed publication receipt, or the stability of the tag
reference after selection. It does not fetch served archives or establish
installed receiver parity. Exact ZIP names and SHA-256 payload digests remain
cross-feed equality keys; Unix mode remains inspection evidence and a symlink
guard. Acceptance of #15 through #23, both-feed served-byte access, and
installed readback remain separate gates. No feed run, readback output,
publication, merge, or receiver flip occurs in this draft.
