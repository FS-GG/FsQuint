namespace FsQuint.Qualification

open System
open System.IO
open System.IO.Compression
open System.Text.Json.Nodes
open System.Xml.Linq
open FsQuint.Qualification.Common

module Replay =
    let private copyFile source destination = File.Copy(source, destination, true)
    let private copyTree source destination =
        Directory.CreateDirectory destination |> ignore
        for file in Directory.GetFiles(source, "*", SearchOption.AllDirectories) do
            let target = Path.Combine(destination, Path.GetRelativePath(source, file))
            Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
            copyFile file target

    let run () =
        let baseline = read (Path.Combine(replay, "baseline.json"))
        use scratch = new Temporary("fsquint-automata-")
        let target = scratch.Path
        for name in ["AutomataReplay.fsproj"; "baseline.json"; "packages.lock.json"] do
            copyFile (Path.Combine(replay, name)) (Path.Combine(target, name))
        for pattern in ["*.fs"; "*.qnt"] do
            for file in Directory.GetFiles(replay, pattern) do
                copyFile file (Path.Combine(target, Path.GetFileName file))
        copyTree (Path.Combine(replay, "fixtures")) (Path.Combine(target, "fixtures"))
        copyFile (Path.Combine(root, "examples/Shared/PureReplay.fs")) (Path.Combine(target, "PureReplay.fs"))
        for name in ["global.json"; "NuGet.Config"] do copyFile (Path.Combine(root, name)) (Path.Combine(target, name))
        let environment = ["NUGET_PACKAGES", Some(Path.Combine(target, "packages")); "NUGET_HTTP_CACHE_PATH", Some(Path.Combine(target, "http"))]
        let restore = ["restore"; "AutomataReplay.fsproj"; "--source"; Path.Combine(root, "artifacts/packages"); "--source"; "https://api.nuget.org/v3/index.json"]
        let oldLock = read (Path.Combine(target, "packages.lock.json"))
        runCommand "dotnet" (restore @ ["--force-evaluate"]) target environment 180 |> ignore
        let refreshed = read (Path.Combine(target, "packages.lock.json"))
        prop (prop (prop oldLock "dependencies") "net10.0") "FsQuint" |> fun p -> p.AsObject().["contentHash"] <- jstr (str (prop (prop (prop (prop refreshed "dependencies") "net10.0") "FsQuint") "contentHash"))
        ensure (JsonNode.DeepEquals(oldLock, refreshed)) "Dependency lock changed beyond the candidate FsQuint content hash"
        let candidate = Path.Combine(root, "artifacts/packages/FsQuint.0.1.1.nupkg")
        let restored = Path.Combine(target, "packages/fsquint/0.1.1/fsquint.0.1.1.nupkg")
        ensure (shaFile restored = shaFile candidate) "Consumer did not restore the exact candidate FsQuint package"
        runCommand "dotnet" (restore @ ["--locked-mode"]) target environment 180 |> ignore
        let checkPin (pin: JsonNode) =
            let id = str (prop pin "id")
            let version = str (prop pin "version")
            let name = id.ToLowerInvariant()
            let archive = Path.Combine(target, "packages", name, version, sprintf "%s.%s.nupkg" name version)
            ensure (shaFile archive = str (prop pin "sha256")) ("Package provenance mismatch: " + id)
            archive
        let core = obj ["id", prop baseline "packageId"; "version", prop baseline "packageVersion"; "sha256", prop baseline "packageSha256"]
        let archive = checkPin core
        for pin in arr (prop baseline "additionalPackages") do checkPin pin |> ignore
        use zip = ZipFile.OpenRead archive
        let nuspec = zip.Entries |> Seq.find (fun e -> e.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
        use stream = nuspec.Open()
        let xml = XDocument.Load stream
        let metadata = xml.Root.Elements() |> Seq.find (fun e -> e.Name.LocalName = "metadata")
        let element name = metadata.Elements() |> Seq.find (fun e -> e.Name.LocalName = name)
        for name, expected in ["id", str (prop baseline "packageId"); "version", str (prop baseline "packageVersion"); "license", str (prop baseline "licenseExpression")] do
            ensure ((element name).Value = expected) ("Unexpected package " + name)
        let repository = element "repository"
        ensure (repository.Attribute(XName.Get "url").Value = str (prop baseline "repository") && repository.Attribute(XName.Get "commit").Value = str (prop baseline "repositoryCommit")) "Unexpected package source provenance"
        let execution = runCommand "dotnet" ["run"; "--project"; "AutomataReplay.fsproj"; "-c"; "Release"; "--no-restore"] target environment 120
        Console.Write execution
        let failures = execution.Split('\n') |> Array.choose (fun line -> if line.StartsWith("DIVERGENCE ") then Some(JsonNode.Parse(line.Substring(11))) else None)
        ensure (failures.Length = 4) "Missing approval mutation diagnostics"
        writeJson (Path.Combine(root, "artifacts/automata-replay.json")) (obj ["schema", jstr "fsquint.replay-evidence/1"; "outcome", jstr "passed"; "mutationDiagnostics", JsonArray(failures) :> JsonNode])
        printfn "PASS: isolated locked restore and package archive/source/license provenance."
        match Environment.GetEnvironmentVariable "QUINT_BIN" with
        | null | "" -> printfn "Model execution not requested; offline fixture replay qualified above."
        | quint ->
            ensure (shaFile quint = str (prop baseline "quintSha256")) "Quint executable differs from the qualified pin"
            Generation.run false
            let cases =
                [ ["typecheck"; "approval.qnt"]; ["typecheck"; "turnstile.qnt"]
                  ["run"; "correction.qnt"; "--invariant"; "safety"; "--seed"; "42"; "--max-samples"; "1000"; "--max-steps"; "10"; "--verbosity"; "1"]
                  ["run"; "protocol.qnt"; "--invariant"; "safety"; "--seed"; "42"; "--max-samples"; "1000"; "--max-steps"; "10"; "--verbosity"; "1"]
                  ["test"; "turnstile_test.qnt"; "--seed"; "42"]; ["typecheck"; "resolver.qnt"]; ["test"; "resolver_test.qnt"; "--seed"; "42"]
                  ["run"; "resolver.qnt"; "--invariant"; "safety"; "--seed"; "42"; "--max-samples"; "1000"; "--max-steps"; "10"; "--verbosity"; "1"]
                  ["test"; "approval_test.qnt"; "--seed"; "42"]
                  ["run"; "approval.qnt"; "--invariant"; "safety"; "--seed"; "42"; "--max-samples"; "1000"; "--max-steps"; "30"; "--verbosity"; "1"]
                  ["run"; "approval.qnt"; "--invariant"; "transitionSafety"; "--seed"; "42"; "--max-samples"; "1000"; "--max-steps"; "30"; "--verbosity"; "1"] ]
            for args in cases do runCommand quint args replay [] 120 |> ignore
