#load "NuspecIdentity.fsx"

open System
open System.IO
open System.IO.Compression
open System.Text
open NuspecIdentity

let commit = String.replicate 40 "a"
let folder = Path.Combine(Path.GetTempPath(), "fsquint-nuspec-" + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory folder |> ignore

let archive (name: string) (members: (string * string) list) =
    let path = Path.Combine(folder, name)
    use stream = File.Create path
    use zip = new ZipArchive(stream, ZipArchiveMode.Create)
    for (memberName, content) in members do
        let entry = zip.CreateEntry memberName
        entry.ExternalAttributes <- 0o100644 <<< 16
        use output = entry.Open()
        let raw = Encoding.UTF8.GetBytes content
        output.Write(raw, 0, raw.Length)
    path

let nuspec id version actualCommit =
    $"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{version}</version><repository type=\"git\" commit=\"{actualCommit}\"/></metadata></package>"

let payload = "payload"
let validSpec = nuspec "FsQuint" "0.1.0" commit
let good = archive "valid.nupkg" [ "FsQuint.nuspec", validSpec; "lib/net10.0/FsQuint.dll", payload ]
let mutable passed = 0
let accepts label path =
    let identity = verify path "FsQuint" "0.1.0" commit
    if identity.Id <> "FsQuint" || identity.Version <> "0.1.0" || identity.RepositoryCommit <> commit then
        failwithf "%s: wrong identity %A" label identity
    passed <- passed + 1
    printfn "PASS %s" label
let refusesExpected label path expectedCommit =
    try
        verify path "FsQuint" "0.1.0" expectedCommit |> ignore
        failwithf "%s: expected refusal" label
    with :? InvalidDataException ->
        passed <- passed + 1
        printfn "PASS %s" label
let refuses label path = refusesExpected label path commit

try
    accepts "exact nuspec package and commit" good
    refuses "wrong package id" (archive "wrong-id.nupkg" [ "FsQuint.nuspec", nuspec "Foreign" "0.1.0" commit; "lib/a.dll", payload ])
    refuses "wrong version" (archive "wrong-version.nupkg" [ "FsQuint.nuspec", nuspec "FsQuint" "0.2.0" commit; "lib/a.dll", payload ])
    refuses "wrong commit" (archive "wrong-commit.nupkg" [ "FsQuint.nuspec", nuspec "FsQuint" "0.1.0" (String.replicate 40 "b"); "lib/a.dll", payload ])
    refusesExpected "newline after expected commit"
        (archive "newline-commit.nupkg" [ "FsQuint.nuspec", nuspec "FsQuint" "0.1.0" (commit + "&#10;"); "lib/a.dll", payload ])
        (commit + "\n")
    refuses "duplicate nuspec" (archive "duplicate.nupkg" [ "FsQuint.nuspec", validSpec; "Foreign.nuspec", validSpec; "lib/a.dll", payload ])
    refuses "duplicate exact ZIP member" (archive "duplicate-member.nupkg" [ "FsQuint.nuspec", validSpec; "FsQuint.nuspec", validSpec; "lib/a.dll", payload ])
    refuses "duplicate XML id" (archive "duplicate-id.nupkg" [ "FsQuint.nuspec", validSpec.Replace("<id>FsQuint</id>", "<id>FsQuint</id><id>FsQuint</id>"); "lib/a.dll", payload ])
    refuses "duplicate XML repository" (archive "duplicate-repository.nupkg" [ "FsQuint.nuspec", validSpec.Replace("</metadata>", $"<repository type=\"git\" commit=\"{commit}\"/></metadata>"); "lib/a.dll", payload ])
    refuses "nested nuspec" (archive "nested.nupkg" [ "nested/FsQuint.nuspec", validSpec; "lib/a.dll", payload ])
    refuses "missing nuspec" (archive "missing.nupkg" [ ("lib/a.dll", payload) ])
    refuses "malformed XML" (archive "malformed.nupkg" [ "FsQuint.nuspec", "<package>"; "lib/a.dll", payload ])
    refuses "DTD entity" (archive "dtd.nupkg" [ "FsQuint.nuspec", "<!DOCTYPE package [<!ENTITY x 'bad'>]><package><metadata><id>&x;</id></metadata></package>"; "lib/a.dll", payload ])
    printfn "nuspec identity: %d controls passed" passed
finally
    Directory.Delete(folder, true)
