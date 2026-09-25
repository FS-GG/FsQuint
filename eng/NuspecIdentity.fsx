#load "archive-fingerprint.fsx"

open System
open System.IO
open System.IO.Compression
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq
open ArchiveFingerprint

type Identity = {
    Id: string
    Version: string
    RepositoryCommit: string
    NuspecSha256: string
}

let private invalid message = raise (InvalidDataException message)
let private sha = Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)

let private one name (elements: seq<XElement>) =
    match elements |> Seq.toList with
    | [ value ] -> value
    | _ -> invalid $"nuspec must contain one {name}"

/// Read-only identity check for a caller-supplied expected release commit.
/// The caller must qualify the expected commit and archive provenance separately.
let verify (path: string) (expectedId: string) (expectedVersion: string)
           (expectedCommit: string) : Identity =
    if String.IsNullOrWhiteSpace expectedId || expectedId.Contains('/')
       || expectedId.Contains('\\') || String.IsNullOrWhiteSpace expectedVersion
       || String.IsNullOrWhiteSpace expectedCommit || not (sha.IsMatch expectedCommit) then
        invalid "expected nuspec identity is invalid"

    let members = ArchiveFingerprint.inspect path
    let expectedName = expectedId + ".nuspec"
    let manifests =
        members
        |> List.filter (fun member' -> member'.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
    let manifest =
        match manifests with
        | [ only ] when only.Name = expectedName -> only
        | _ -> invalid "archive must contain one exact root nuspec"

    use archive = ZipFile.OpenRead path
    let entry = archive.GetEntry expectedName
    if isNull entry then invalid "nuspec archive entry is missing"
    use stream = entry.Open()
    let settings = XmlReaderSettings(
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 1_048_576L
    )
    use reader = XmlReader.Create(stream, settings)
    let document =
        try XDocument.Load reader
        with :? XmlException -> invalid "nuspec XML is invalid"
    let root = document.Root
    if isNull root || root.Name.LocalName <> "package" then invalid "nuspec package root is invalid"
    let metadata = one "metadata" (root.Elements() |> Seq.filter (fun item -> item.Name.LocalName = "metadata"))
    let value name =
        one name (metadata.Elements() |> Seq.filter (fun item -> item.Name.LocalName = name))
        |> fun item -> item.Value
    let id = value "id"
    let version = value "version"
    let repository =
        one "repository" (metadata.Elements() |> Seq.filter (fun item -> item.Name.LocalName = "repository"))
    let commit = repository.Attribute(XName.Get "commit")
    let observedCommit = if isNull commit then "" else commit.Value
    if id <> expectedId || version <> expectedVersion || observedCommit <> expectedCommit then
        invalid "nuspec identity differs from expected package or release commit"
    { Id = id; Version = version; RepositoryCommit = observedCommit; NuspecSha256 = manifest.Sha256 }
