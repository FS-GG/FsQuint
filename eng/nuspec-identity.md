# Provisional `.nuspec` identity source seam

`NuspecIdentity.fsx` checks one exact root `.nuspec` member, package ID,
version, and repository commit in a local archive. It uses the archive member
guard from draft #15 and stacks on packed characterization draft #16. The
expected commit is supplied by the caller; this source does not prove its
release authority, fetch a feed archive, write a receipt, publish, or change a
receiver. It rejects duplicate, nested, missing, malformed, or DTD-backed
manifests. Run `dotnet fsi eng/test-nuspec-identity.fsx` for local controls.

The current Python `eng/readback.py` checks the served `.nuspec` repository
commit when one is supplied. A stacked source draft requires its archive to
contain exactly one root `<package>.nuspec`; a subsequent draft adds bounded
XML ID, version, structure, and DTD guards. Full parser and expected-commit
authority parity remain open.
Python compares names and payload digests but ignores ZIP mode. The stacked
source-only mode policy draft aligns F# cross-feed
equality with that behavior while preserving mode inspection and symlink
refusal. Draft #16 recorded the earlier mismatch on locally packed archives;
the mode policy decision does not establish served-feed or installed parity.
