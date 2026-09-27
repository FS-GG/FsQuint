namespace FsQuint.Qualification

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open FsQuint.Qualification.Common

module SelfTest =
    type private MockHandler(answer: HttpRequestMessage -> HttpResponseMessage) =
        inherit HttpMessageHandler()
        override _.Send(request, _) = answer request
        override _.SendAsync(request, _) = Task.FromResult(answer request)
    let private archive file entries =
        use zip = ZipFile.Open(file, ZipArchiveMode.Create)
        for name, bytes, mode in entries do
            let item = zip.CreateEntry(name)
            item.ExternalAttributes <- mode <<< 16
            use stream = item.Open()
            stream.Write(bytes, 0, bytes.Length)
        file
    let private member' (name: string) = name, System.Text.Encoding.UTF8.GetBytes name, 0o100644
    let private rejected label thunk =
        let mutable refused = false
        try thunk() with _ -> refused <- true
        ensure refused ("accepted invalid " + label)
    let private goodXml commit = sprintf "<package><metadata><id>FsQuint</id><version>0.1.0</version><repository type=\"git\" commit=\"%s\"/></metadata></package>" commit
    let run () =
        use temporary = new Temporary("fsquint-readback-selftest-")
        let file name entries = archive (Path.Combine(temporary.Path, name + ".nupkg")) entries
        let valid = file "valid" [member' "README.md"; member' "LICENSE.txt"]
        ensure (ArchiveFingerprint.inspect valid |> List.length = 2) "valid package rejected"
        let duplicate = file "duplicate" [member' "README.md"; member' "README.md"]
        rejected "duplicate ZIP member" (fun () -> ArchiveFingerprint.inspect duplicate |> ignore)
        let badMembers =
            ["root-case", ["README.md"; "readme.md"]
             "nested-case", ["README.md"; "Docs/Guide.md"; "docs/guide.md"]
             "directory-case", ["README.md"; "Folder/"; "folder/"]
             "signature-case", ["README.md"; ".signature.p7s"; ".SIGNATURE.P7S"]
             "parent", ["README.md"; "../outside"]
             "absolute", ["README.md"; "/outside"]
             "dot", ["README.md"; "docs/./file"]
             "backslash", ["README.md"; "docs\\file"]
             "colon", ["README.md"; "C:drive"]
             "empty-segment", ["README.md"; "docs//file"]
             "blank", ["README.md"; " "]]
        for label, names in badMembers do
            let path = file label (names |> List.map member')
            rejected label (fun () -> ArchiveFingerprint.inspect path |> ignore)
        for label, entries in
            ["empty", []
             "signature-only", [member' ".signature.p7s"]
             "directory-only", ["docs/", [||], 0o040755]
             "symlink-payload", ["README.md", [|1uy|], 0o120777]
             "symlink-signature", [member' "README.md"; ".signature.p7s", [|1uy|], 0o120777]] do
            rejected label (fun () -> ArchiveFingerprint.inspect (file label entries) |> ignore)
        let signed = file "signed" [member' "README.md"; member' ".signature.p7s"]
        ensure ((ArchiveFingerprint.inspect signed |> List.map _.Name) = ["README.md"]) "signature altered payload set"
        let executable = file "executable" ["README.md", System.Text.Encoding.UTF8.GetBytes "README.md", 0o100755; member' "LICENSE.txt"]
        ArchiveFingerprint.compare valid executable
        let nestedSignature = file "nested-signature" [member' "README.md"; member' "LICENSE.txt"; member' "nested/.signature.p7s"]
        rejected "nested signature" (fun () -> ArchiveFingerprint.compare valid nestedSignature)
        let changed = file "changed" ["README.md", [|1uy|], 0o100644; member' "LICENSE.txt"]
        rejected "changed payload" (fun () -> ArchiveFingerprint.compare valid changed)
        let commit = String.replicate 40 "a"
        let xmlBytes (value: string) = System.Text.Encoding.UTF8.GetBytes value
        let makeSpec label xml names = file label (("FsQuint.nuspec", xmlBytes xml, 0o100644) :: (names |> List.map member'))
        let spec = makeSpec "spec-valid" (goodXml commit) ["lib/a.dll"]
        Readback.inspectNuspec spec "FsQuint" "0.1.0" commit
        let namespaceXml = (goodXml commit).Replace("<package>", "<package xmlns=\"urn:nuget:test\">")
        Readback.inspectNuspec (makeSpec "spec-namespace" namespaceXml ["lib/a.dll"]) "FsQuint" "0.1.0" commit
        for label, xml in
            ["wrong-id", (goodXml commit).Replace("<id>FsQuint</id>", "<id>Foreign</id>")
             "wrong-version", (goodXml commit).Replace("<version>0.1.0</version>", "<version>0.2.0</version>")
             "wrong-commit", (goodXml commit).Replace(commit, String.replicate 40 "b")
             "wrong-root", (goodXml commit).Replace("<package>", "<foreign>").Replace("</package>", "</foreign>")
             "two-id", (goodXml commit).Replace("<id>FsQuint</id>", "<id>FsQuint</id><id>FsQuint</id>")
             "two-metadata", (goodXml commit).Replace("</metadata>", "</metadata><metadata><id>FsQuint</id></metadata>")
             "two-repository", (goodXml commit).Replace("</metadata>", "<repository commit=\"" + commit + "\"/></metadata>")
             "dtd", "<!DOCTYPE package [<!ENTITY x \"FsQuint\">]>" + (goodXml commit).Replace("FsQuint</id>", "&x;</id>")
             "malformed", "<package>"] do
            rejected label (fun () -> Readback.inspectNuspec (makeSpec label xml ["lib/a.dll"]) "FsQuint" "0.1.0" commit)
        let extraSpec = file "two-specs" ["FsQuint.nuspec", xmlBytes (goodXml commit), 0o100644; member' "foreign.nuspec"; member' "lib/a.dll"]
        rejected "two nuspecs" (fun () -> Readback.inspectNuspec extraSpec "FsQuint" "0.1.0" commit)
        let nestedSpec = file "nested-spec" ["metadata/FsQuint.nuspec", xmlBytes (goodXml commit), 0o100644; member' "lib/a.dll"]
        rejected "nested nuspec" (fun () -> Readback.inspectNuspec nestedSpec "FsQuint" "0.1.0" commit)
        let repo = Path.Combine(temporary.Path, "source")
        Directory.CreateDirectory repo |> ignore
        let git args = runCommand "git" args repo [] 30 |> ignore
        git ["init"; "-q"; "-b"; "main"]
        git ["config"; "user.name"; "Fixture"]
        git ["config"; "user.email"; "fixture@example.invalid"]
        File.WriteAllText(Path.Combine(repo, "marker"), "source")
        git ["add"; "marker"]
        git ["commit"; "-qm"; "source"]
        let tagged = (runCommand "git" ["rev-parse"; "HEAD"] repo [] 30).Trim()
        git ["tag"; "v0.1.0"]
        File.WriteAllText(Path.Combine(repo, "marker"), "foreign")
        git ["commit"; "-qam"; "foreign"]
        ensure (Readback.tagCommit "0.1.0" repo = tagged) "release readback selected caller head over tag"
        git ["tag"; "-d"; "v0.1.0"]
        rejected "missing tag" (fun () -> Readback.tagCommit "0.1.0" repo |> ignore)
        let mutable firstAuthenticated, secondAuthenticated = false, true
        use handler = new MockHandler(fun request ->
            if request.RequestUri.Host = "origin.example" then
                firstAuthenticated <- not (isNull request.Headers.Authorization)
                let redirect = new HttpResponseMessage(HttpStatusCode.Redirect)
                redirect.Headers.Location <- Uri("https://other.example/archive.nupkg")
                redirect
            else
                secondAuthenticated <- not (isNull request.Headers.Authorization)
                new HttpResponseMessage(HttpStatusCode.OK, Content = new ByteArrayContent([|1uy; 2uy|])))
        use client = new HttpClient(handler)
        let bytes = Readback.downloaded client "https://origin.example/archive.nupkg" (Some "fixture-token")
        ensure (firstAuthenticated && not secondAuthenticated && bytes.Value = [|1uy; 2uy|]) "redirect leaked package credentials"
        printfn "PASS: F# readback archive, nuspec and release-tag controls."
