// FSC-08 source primitive. This is not wired into release/readback.py.
// Feed qualification must decide whether archive mode bits are part of the
// cross-feed contract before any receiver switch.
module ArchiveFingerprint

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography

type Member = {
    Name: string
    Sha256: string
    UnixMode: int
}

let private signature = ".signature.p7s"
let private invalidData message = raise (InvalidDataException message)

let inspect (path: string) : Member list =
    use archive = ZipFile.OpenRead path
    let names = System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)

    [
        for entry in archive.Entries do
            if not (names.Add entry.FullName) then
                invalidData $"{path}: duplicate archive member '{entry.FullName}'"

            if entry.FullName <> signature then
                use stream = entry.Open()
                let digest = SHA256.HashData stream |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

                {
                    Name = entry.FullName
                    Sha256 = digest
                    UnixMode = (entry.ExternalAttributes >>> 16) &&& 0xffff
                }
    ]
    |> List.sortBy _.Name

let compare (qualified: string) (served: string) : unit =
    let expected = inspect qualified |> List.map (fun member' -> member'.Name, member') |> Map.ofList
    let actual = inspect served |> List.map (fun member' -> member'.Name, member') |> Map.ofList
    let expectedNames = expected |> Map.keys |> Set.ofSeq
    let actualNames = actual |> Map.keys |> Set.ofSeq
    let missing = Set.difference expectedNames actualNames
    let unexpected = Set.difference actualNames expectedNames

    if not missing.IsEmpty || not unexpected.IsEmpty then
        invalidData $"archive members differ: missing={Set.toList missing}; unexpected={Set.toList unexpected}"

    for name in expectedNames do
        let before = expected.[name]
        let after = actual.[name]

        if before.Sha256 <> after.Sha256 then
            invalidData $"archive member '{name}' digest differs"

        if before.UnixMode <> after.UnixMode then
            invalidData $"archive member '{name}' Unix mode differs"
