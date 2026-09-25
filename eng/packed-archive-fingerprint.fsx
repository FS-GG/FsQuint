#load "archive-fingerprint.fsx"

open System
open System.IO
open System.Text.Json
open ArchiveFingerprint

let args = fsi.CommandLineArgs |> Array.skip 1

try
    match args with
    | [| "inspect"; path |] ->
        inspect path |> JsonSerializer.Serialize |> printfn "%s"
    | [| "compare"; qualified; served |] ->
        compare qualified served
        printfn "match"
    | _ ->
        eprintfn "usage: inspect PACKAGE | compare QUALIFIED SERVED"
        Environment.Exit 2
with :? InvalidDataException as error ->
    eprintfn "refusal: %s" error.Message
    Environment.Exit 2
