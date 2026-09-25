# FSC-08 source-only tag stability checks

The expected commit is selected once from `refs/tags/v<version>^{commit}` in
the preceding source draft. Before this draft, a tag could move after that
selection while the readback continued to validate archives against the stale
commit. An A archive still passed its XML check after a disposable repository's
tag moved from A to B.

The proposed readback source re-resolves the tag and compares it with the
original immutable commit object at feed entry, after each download, and
immediately before writing the receipt. A persistent move or deleted tag
refuses a verdict. `eng/test-readback-tag-stability.py` uses a disposable Git
repository and packed archive, compiles only pure helpers from the actual
Python source, and checks that those three call sites are wired. It does not
run the feed loop or write readback artifacts. The fixture also restores the
tag to A, showing that the check can then pass again.

This is a bounded consistency check, not an atomic snapshot of the tag and
served archives. A move and restoration between checks (ABA), or a move after
the final check, can evade detection. A local tag ref is not by itself proof
of protection, signature, stable publication, or an independent receipt.
Archive equality remains exact ZIP names and SHA-256 payload digests; Unix
mode remains inspection evidence and a symlink guard. Acceptance of #15
through #24, served-byte access to both feeds, and installed receiver parity
remain separate gates. No feed run, readback output, package publication,
merge, or receiver flip occurs in this draft.
