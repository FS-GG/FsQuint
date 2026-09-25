#load "archive-fingerprint.fsx"

open System
open System.IO
open System.IO.Compression
open System.Text
open ArchiveFingerprint

let root = Path.Combine(Path.GetTempPath(), "fsquint-fsc08-" + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory root |> ignore

let archive (name: string) (members: (string * string * int) list) =
    let path = Path.Combine(root, name)
    use file = File.Create path
    use zip = new ZipArchive(file, ZipArchiveMode.Create)

    for (memberName, body, mode) in members do
        let entry = zip.CreateEntry memberName
        entry.ExternalAttributes <- mode <<< 16
        use stream = entry.Open()
        let bytes = Encoding.UTF8.GetBytes body
        stream.Write(bytes, 0, bytes.Length)

    path

let expectFailure (label: string) (expected: string) (action: unit -> unit) =
    try
        action ()
        failwith $"{label}: unexpectedly passed"
    with :? InvalidDataException as ex ->
        if not (ex.Message.Contains(expected, StringComparison.Ordinal)) then
            failwith $"{label}: wrong refusal: {ex.Message}"
        printfn "PASS %s" label

try
    let source = archive "qualified.nupkg" [ ("lib/net10.0/FsQuint.dll", "v1", 0o100644) ]
    let signatureOnly =
        archive "signed.nupkg" [ ("lib/net10.0/FsQuint.dll", "v1", 0o100644); (".signature.p7s", "feed-signature", 0) ]
    let unsignedFeed = archive "unsigned.nupkg" [ ("lib/net10.0/FsQuint.dll", "v1", 0o100644) ]

    compare source signatureOnly
    compare source unsignedFeed
    printfn "PASS both served copies match, with a feed-added root signature excluded"

    let nestedSignature =
        archive "nested-signature.nupkg" [ ("lib/net10.0/FsQuint.dll", "v1", 0o100644); ("nested/.signature.p7s", "unexpected", 0) ]
    expectFailure "nested signature is an archive member" "archive members differ" (fun () -> compare source nestedSignature)

    let wrongMember = archive "member.nupkg" [ ("lib/net10.0/Other.dll", "v1", 0o100644) ]
    expectFailure "wrong archive member" "archive members differ" (fun () -> compare source wrongMember)

    let wrongDigest = archive "digest.nupkg" [ ("lib/net10.0/FsQuint.dll", "v2", 0o100644) ]
    expectFailure "wrong archive digest" "digest differs" (fun () -> compare source wrongDigest)

    let wrongMode = archive "mode.nupkg" [ ("lib/net10.0/FsQuint.dll", "v1", 0o100755) ]
    expectFailure "wrong archive mode" "Unix mode differs" (fun () -> compare source wrongMode)

    let duplicate =
        archive "duplicate.nupkg" [ ("lib/net10.0/FsQuint.dll", "v1", 0o100644); ("lib/net10.0/FsQuint.dll", "v1", 0o100644) ]
    expectFailure "duplicate archive member" "duplicate archive member" (fun () -> inspect duplicate |> ignore)

    printfn "archive fingerprint fixture: 6 passed"
finally
    Directory.Delete(root, true)
