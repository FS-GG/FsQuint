#load "NuspecIdentity.fsx"

open System
open System.IO
open NuspecIdentity

match fsi.CommandLineArgs |> Array.skip 1 with
| [| archive; package; version; commit |] ->
    try
        verify archive package version commit |> ignore
        printfn "match"
    with :? InvalidDataException as error ->
        eprintfn "refusal: %s" error.Message
        Environment.Exit 2
| _ ->
    eprintfn "usage: nuspec-root-inspect ARCHIVE PACKAGE VERSION COMMIT"
    Environment.Exit 2
