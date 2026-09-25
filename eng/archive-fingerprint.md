# FSC-08 archive fingerprint source pilot

`archive-fingerprint.fsx` is a read-only F# source primitive. It records each
archive member's exact name, SHA-256 digest, and ZIP Unix mode bits. It refuses
duplicate names and permits a feed-added root `.signature.p7s`, matching the
current readback policy's signature exception.

Run the independent fixture with:

```sh
dotnet fsi eng/test-archive-fingerprint.fsx
```

The fixture proves wrong member, nested signature, changed payload digest,
changed mode, and duplicate member refusals, plus two matching served copies.
The mode comparison is a candidate strengthening;
it must be checked against real served archives before adoption. The Python
`eng/readback.py` remains the release receiver, and the versioned package
roster, feeds, publication and retry behavior are unchanged.
