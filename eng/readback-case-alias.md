# FSC-08 ASCII case-alias archive guard

This draft rejects an archive containing two ZIP member names that differ only
by ASCII letter case. Such names can refer to the same extracted path on a
case-insensitive filesystem, even though both source readers previously saw
distinct ZIP names. The guard applies before hashing, including directory
entries and the excluded root `.signature.p7s`. It leaves exact ZIP names and
SHA-256 payload digests as the cross-feed equality keys. Regular-file Unix
mode remains inspection evidence and a symlink guard, not an equality key.

The independent `eng/test-readback-case-alias.py` extracts the real Python
`payloads()` function without running its feed-fetching top level and invokes
the F# archive inspector separately. Before the repair, both accepted four
cases: root file, nested file, directory, and root-signature case aliases.
Both now refuse each. The two existing local 0.1.0 packs have 13 and 10
members respectively, with no exact or ASCII case-alias duplicates.

The fold maps only `A`–`Z` to `a`–`z`; it is not a Unicode normalization or
filesystem-specific equivalence proof. These source tests do not fetch served
archives, install a receiver, or establish package parity. Review and
acceptance of #15 through #20, access to GitHub Packages served bytes, and
installed readback evidence remain separate gates. No feed request,
publication, receipt write, merge, or receiver flip occurs in this draft.
