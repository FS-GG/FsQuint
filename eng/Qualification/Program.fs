namespace FsQuint.Qualification

open System
open System.Xml.Linq

module Program =
    [<EntryPoint>]
    let main argv =
        try
            match argv with
            | [| "replay" |] -> Replay.run()
            | [| "generate" |] -> Generation.run false
            | [| "generate"; "--write" |] -> Generation.run true
            | [| "providers-provision"; destination |] -> Providers.provision destination
            | [| "providers-check"; source; binaries |] -> Providers.check source binaries
            | [| "readback"; version |] -> Readback.run version None
            | [| "readback"; version; commit |] -> Readback.run version (Some commit)
            | [| "selftest" |] -> SelfTest.run()
            | [| "set-package-version"; project; version |] ->
                let document = XDocument.Load project
                let reference = document.Descendants() |> Seq.find (fun e -> e.Name.LocalName = "PackageReference" && e.Attribute(XName.Get "Include").Value = "FsQuint")
                reference.SetAttributeValue(XName.Get "Version", version)
                document.Save project
            | _ -> failwith "usage: Qualification replay|generate [--write]|providers-provision DEST|providers-check SOURCE POSTGRES_BIN|readback VERSION [COMMIT]"
            0
        with error ->
            eprintfn "%s" error.Message
            1
