# FSC-08 source-only archive mode policy

Select exact archive member names and SHA-256 payload digests as the cross-feed
equality contract. This is the contract documented in `docs/releases.md` and
implemented by the current Python `eng/readback.py` receiver. ZIP Unix mode is
inspection evidence, not an equality key. A mode-only regular-file change is
accepted by both source comparators. F# still refuses symlink archive members
and unsafe names before comparison; those are separate candidate guards and
are not claims of full Python receiver parity.

The red-before `eng/test-readback-mode-policy.py` extracted only the real Python
`payloads()` function from its AST, without running its feed-fetching top level.
It demonstrated Python equality for a `100644` to `100755` regular-file mode
change while F# returned `Unix mode differs`. The repaired comparator and
independent test now agree on the mode-only case and retain controls for a
changed digest, changed name, feed-added root signature, and symlink refusal.
The existing local packed archive fixture checks both packages. These are
offline source tests, not served archive or installed receiver tests.

The choice preserves the live release contract and avoids requiring equality
of ZIP metadata that a feed could change. It does not establish that the
GitHub Packages served archives preserve or alter mode; access to those bytes
was unavailable in draft #18. Real served archive comparison across both
feeds, acceptance of #15 through #18, and installed-package parity remain
prerequisites to F# receiver adoption. No feed publication, readback output,
receiver switch, or merge is performed by this draft.
