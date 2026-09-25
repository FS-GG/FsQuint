# FSC-08 archive fingerprint source pilot

`archive-fingerprint.fsx` is a read-only F# source primitive. It records each
archive member's exact name, SHA-256 digest, and ZIP Unix mode bits. It refuses
duplicate, unsafe, or symlink member names and archives without a payload. It
permits a feed-added root `.signature.p7s`, matching the current readback
policy's signature exception.

Run the independent fixture with:

```sh
dotnet fsi eng/test-archive-fingerprint.fsx
```

The 17 controls cover wrong members, nested signatures, changed payload and
manifest digests, changed mode, duplicates, empty and signature-only archives,
unsafe paths, symlinks, Unicode member names, and reordered entries. One fixture
embeds the exact bytes of the tracked `FsQuint.fsproj`; it is not a packed
package or a served feed artifact. The mode and path checks are candidate
strengthenings and need qualification against real served archives before
adoption. This comparator does not validate package identity, `.nuspec`
semantics, or publication receipts. The Python `eng/readback.py` remains the
release receiver; the versioned package roster, feeds, publication and retry
behavior are unchanged.
