namespace FsQuint.Qualification

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Text.Json.Nodes

module Common =
    let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
    let replay = Path.Combine(root, "examples", "AutomataReplay")
    let providers = Path.Combine(root, "examples", "AutomataProviders")
    let path (parts: string list) = Path.Combine(Array.ofList parts)
    let shaBytes (bytes: byte[]) = SHA256.HashData bytes |> Convert.ToHexString |> fun s -> s.ToLowerInvariant()
    let shaFile file = File.ReadAllBytes file |> shaBytes
    let shaText (value: string) = System.Text.Encoding.UTF8.GetBytes value |> shaBytes
    let read file = JsonNode.Parse(File.ReadAllText file)
    let prop (node: JsonNode) (key: string) = node.[key]
    let str (node: JsonNode) = node.GetValue<string>()
    let num (node: JsonNode) = node.GetValue<int>()
    let arr (node: JsonNode) = node.AsArray() |> Seq.toList
    let obj (pairs: (string * JsonNode) list) =
        let value = JsonObject()
        for key, item in pairs do value.Add(key, if isNull item then null else item.DeepClone())
        value :> JsonNode
    let jstr (s: string) = JsonValue.Create(s) :> JsonNode
    let jint (n: int) = JsonValue.Create(n) :> JsonNode
    let jarray (items: JsonNode seq) = JsonArray(items |> Seq.map (fun n -> if isNull n then null else n.DeepClone()) |> Seq.toArray) :> JsonNode
    let jsonOptions = JsonSerializerOptions(WriteIndented = true)
    let writeJson (file: string) (value: JsonNode) =
        Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
        File.WriteAllText(file, value.ToJsonString(jsonOptions) + "\n")
    let ensure condition message = if not condition then failwith message
    let runCommand (exe: string) (args: string list) (cwd: string) (environment: (string * string option) list) (timeoutSeconds: int) =
        use p = new Process()
        p.StartInfo.FileName <- exe
        p.StartInfo.WorkingDirectory <- cwd
        p.StartInfo.UseShellExecute <- false
        p.StartInfo.RedirectStandardOutput <- true
        p.StartInfo.RedirectStandardError <- true
        for item in args do p.StartInfo.ArgumentList.Add item
        for key, value in environment do
            match value with Some text -> p.StartInfo.Environment.[key] <- text | None -> p.StartInfo.Environment.Remove(key) |> ignore
        ensure (p.Start()) ("could not start " + exe)
        let output = p.StandardOutput.ReadToEndAsync()
        let errors = p.StandardError.ReadToEndAsync()
        if not (p.WaitForExit(timeoutSeconds * 1000)) then
            p.Kill(true)
            failwithf "%s timed out after %ds" exe timeoutSeconds
        let result = output.Result + errors.Result
        ensure (p.ExitCode = 0) (sprintf "%s %A failed (%d):\n%s" exe args p.ExitCode result)
        result
    type Temporary(prefix: string) =
        let directory = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"))
        do Directory.CreateDirectory directory |> ignore
        member _.Path = directory
        interface IDisposable with member _.Dispose() = Directory.Delete(directory, true)
