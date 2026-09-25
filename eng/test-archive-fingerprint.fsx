#load "archive-fingerprint.fsx"

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open ArchiveFingerprint

let root = Path.Combine(Path.GetTempPath(), "fsquint-fsc08-" + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory root |> ignore

let archiveBytes (name: string) (members: (string * byte[] * int) list) =
    let path = Path.Combine(root, name)
    use file = File.Create path
    use zip = new ZipArchive(file, ZipArchiveMode.Create)

    for (memberName, bytes, mode) in members do
        let entry = zip.CreateEntry memberName
        entry.ExternalAttributes <- mode <<< 16
        use stream = entry.Open()
        stream.Write(bytes, 0, bytes.Length)

    path

let archive (name: string) (members: (string * string * int) list) =
    members
    |> List.map (fun (memberName, body, mode) -> memberName, Encoding.UTF8.GetBytes body, mode)
    |> archiveBytes name

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

    let empty = archive "empty.nupkg" []
    expectFailure "empty archive has no payload evidence" "no payload" (fun () -> compare empty empty)
    let onlySignature = archive "only-signature.nupkg" [ (".signature.p7s", "signature", 0) ]
    expectFailure "signature alone has no payload evidence" "no payload" (fun () -> compare onlySignature onlySignature)

    for name in [ "../outside"; "/absolute"; "lib/./same.dll"; "lib\\same.dll" ] do
        let unsafeArchive = archive ("unsafe-" + Guid.NewGuid().ToString("N") + ".nupkg") [ (name, "v1", 0o100644) ]
        expectFailure ("unsafe member " + name) "unsafe archive member" (fun () -> compare unsafeArchive unsafeArchive)

    let symlink = archive "symlink.nupkg" [ ("lib/escape", "../../outside", 0o120777) ]
    expectFailure "Unix symlink cannot be a package payload" "symlink archive member" (fun () -> compare symlink symlink)

    let unicode = archive "unicode.nupkg" [ ("docs/résumé-😃.md", "unicode", 0o100644); ("lib/net10.0/FsQuint.dll", "v1", 0o100644) ]
    let reordered = archive "unicode-reordered.nupkg" [ ("lib/net10.0/FsQuint.dll", "v1", 0o100644); ("docs/résumé-😃.md", "unicode", 0o100644) ]
    compare unicode reordered
    if inspect unicode |> List.exists (fun member' -> member'.Name = "docs/résumé-😃.md") |> not then
        failwith "Unicode member name was not preserved"

    let manifest = archive "manifest.nupkg" [ ("FsQuint.nuspec", "version=1", 0o100644); ("lib/net10.0/FsQuint.dll", "v1", 0o100644) ]
    let changedManifest = archive "changed-manifest.nupkg" [ ("FsQuint.nuspec", "version=2", 0o100644); ("lib/net10.0/FsQuint.dll", "v1", 0o100644) ]
    expectFailure "manifest metadata changes the package fingerprint" "digest differs" (fun () -> compare manifest changedManifest)

    // A tracked product input supplies real bytes without packing or publishing a package.
    let projectBytes = File.ReadAllBytes(Path.Combine(__SOURCE_DIRECTORY__, "../src/FsQuint/FsQuint.fsproj"))
    let sourceCopy = archiveBytes "source-copy.nupkg" [ ("content/FsQuint.fsproj", projectBytes, 0o100644) ]
    let servedCopy = archiveBytes "served-copy.nupkg" [ ("content/FsQuint.fsproj", Array.copy projectBytes, 0o100644) ]
    compare sourceCopy servedCopy
    let projectDigest = SHA256.HashData(projectBytes) |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()
    if (inspect sourceCopy |> List.head).Sha256 <> projectDigest then failwith "tracked project bytes were not fingerprinted exactly"
    let altered = Array.copy projectBytes
    altered.[0] <- altered.[0] ^^^ 1uy
    let alteredCopy = archiveBytes "altered-source.nupkg" [ ("content/FsQuint.fsproj", altered, 0o100644) ]
    expectFailure "tracked product byte change" "digest differs" (fun () -> compare sourceCopy alteredCopy)

    printfn "archive fingerprint fixture: 17 controls passed"
finally
    Directory.Delete(root, true)
