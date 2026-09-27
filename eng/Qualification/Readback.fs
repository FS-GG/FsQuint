namespace FsQuint.Qualification

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.RegularExpressions
open System.Text.Json.Nodes
open System.Threading
open System.Xml
open System.Xml.Linq
open FsQuint.Qualification.Common

module Readback =
    let tagCommit version repository =
        let tag = sprintf "refs/tags/v%s^{commit}" version
        let value = runCommand "git" ["rev-parse"; "--verify"; "--end-of-options"; tag] repository [] 30 |> fun s -> s.Trim()
        ensure (Regex.IsMatch(value, "^[0-9a-f]{40}$")) "source tag commit is unavailable"
        value

    let inspectNuspec path package version commit =
        NuspecIdentity.verify path package version commit |> ignore

    let downloaded (client: HttpClient) (url: string) authorization =
        let origin = Uri url
        let rec get (current: Uri) redirects =
            ensure (redirects <= 5) "too many package redirects"
            use request = new HttpRequestMessage(HttpMethod.Get, current)
            request.Headers.UserAgent.ParseAdd("FsQuint-Qualification/1.0")
            if current.Authority = origin.Authority then
                match authorization with Some value -> request.Headers.Authorization <- AuthenticationHeaderValue("Basic", value) | None -> ()
            use response = client.Send(request)
            if int response.StatusCode >= 300 && int response.StatusCode < 400 then
                let target = response.Headers.Location
                ensure (not (isNull target)) "package redirect has no target"
                let redirected = Uri(current, target)
                ensure (redirected.Scheme = Uri.UriSchemeHttps) "insecure package redirect"
                get redirected (redirects + 1)
            elif response.StatusCode = HttpStatusCode.NotFound then None
            else
                response.EnsureSuccessStatusCode() |> ignore
                Some(response.Content.ReadAsByteArrayAsync().Result)
        get origin 0

    let run (version: string) (suppliedCommit: string option) =
        ensure (Regex.IsMatch(version, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9.-]+)?$")) "Invalid package version"
        let commit = tagCommit version root
        let requested = defaultArg suppliedCommit (Environment.GetEnvironmentVariable "GITHUB_SHA")
        if not (String.IsNullOrWhiteSpace requested) then ensure (requested = commit) "supplied commit differs from source tag"
        let checkTag () = ensure (tagCommit version root = commit) "source tag moved during readback"
        let output = Path.Combine(root, "artifacts/readback")
        Directory.CreateDirectory output |> ignore
        let receipts = JsonObject()
        use handler = new HttpClientHandler(AllowAutoRedirect = false)
        use client = new HttpClient(handler, Timeout = TimeSpan.FromSeconds 30.)
        for package in ["FsQuint"; "FsQuint.Tooling"] do
            let name = package.ToLowerInvariant()
            let local = Path.Combine(root, "artifacts/packages", sprintf "%s.%s.nupkg" package version)
            let mutable expected = if File.Exists local then Some(ArchiveFingerprint.inspect local) else None
            for feed in ["github"; "nuget"] do
                checkTag()
                let url =
                    if feed = "github" then sprintf "https://nuget.pkg.github.com/FS-GG/download/%s/%s/%s.%s.nupkg" name version name version
                    else sprintf "https://api.nuget.org/v3-flatcontainer/%s/%s/%s.%s.nupkg" name version name version
                let authorization =
                    if feed = "github" then
                        let actor = Environment.GetEnvironmentVariable "GITHUB_ACTOR"
                        let token = Environment.GetEnvironmentVariable "GH_TOKEN"
                        ensure (not (String.IsNullOrWhiteSpace actor) && not (String.IsNullOrWhiteSpace token)) "GitHub readback credentials missing"
                        Some(Convert.ToBase64String(Encoding.UTF8.GetBytes(actor + ":" + token)))
                    else None
                let mutable content = None
                let mutable attempt = 0
                while content.IsNone && attempt < 120 do
                    content <- downloaded client url authorization
                    attempt <- attempt + 1
                    if content.IsNone && attempt < 120 then
                        if attempt % 6 = 1 then printfn "%s: %s has not exposed the download yet; retrying" package feed
                        Thread.Sleep 10000
                ensure content.IsSome (sprintf "%s: %s package is unavailable" package feed)
                checkTag()
                let bytes = content.Value
                let target = Path.Combine(output, sprintf "%s.%s.nupkg" name feed)
                File.WriteAllBytes(target, bytes)
                let actual = ArchiveFingerprint.inspect target
                inspectNuspec target package version commit
                match expected with
                | Some qualified ->
                    ensure ((qualified |> List.map (fun m -> m.Name,m.Sha256)) = (actual |> List.map (fun m -> m.Name,m.Sha256))) (sprintf "%s: %s differs from qualified payload" package feed)
                | None -> expected <- Some actual
                let members = actual |> List.map (fun m -> m.Name, jstr m.Sha256)
                receipts.Add(package + ":" + feed, obj ["archiveSha256", jstr (shaBytes bytes); "payloads", obj members])
        checkTag()
        writeJson (Path.Combine(output, "receipt.json")) receipts
        printfn "Both package payloads match both feeds."
