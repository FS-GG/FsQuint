# Provisional `.nuspec` identity source seam

`NuspecIdentity.fsx` checks one exact root `.nuspec` member, package ID,
version, and repository commit in a local archive. It uses the archive member
guard from draft #15 and stacks on packed characterization draft #16. The
expected commit is supplied by the caller; this source does not prove its
release authority, fetch a feed archive, write a receipt, publish, or change a
receiver. It rejects duplicate, nested, missing, malformed, or DTD-backed
manifests. Run `dotnet fsi eng/test-nuspec-identity.fsx` for local controls.

The current Python `eng/readback.py` checks the served `.nuspec` repository
commit when one is supplied. It compares names and payload digests but ignores
ZIP mode. The F# archive fingerprint compares Unix modes. Draft #16 proves
mode-only mutations are accepted by the Python projection and refused by the
F# candidate on locally packed archives, whose members all carried `100644`.
The package owner must inspect real GitHub and nuget.org served archives and
decide whether mode is contractual before any receiver adoption; local packs
alone do not resolve that policy.
