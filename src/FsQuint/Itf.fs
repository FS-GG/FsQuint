namespace FsQuint

open System
open System.Text
open System.Text.Json
open System.Numerics
open System.Globalization

/// Exact raw ITF values. Variant encodings remain records with their tag/value fields.
[<RequireQualifiedAccess>]
type ItfValue =
    | Null
    | Boolean of bool
    | Integer of BigInteger
    | Text of string
    | Sequence of ItfValue list
    | Tuple of ItfValue list
    | Set of ItfValue list
    | Map of (ItfValue * ItfValue) list
    | Record of (string * ItfValue) list

type ItfLimits =
    {
        MaxBytes: int
        MaxDepth: int
        MaxStates: int
        MaxCollection: int
    }

type ItfDocument =
    {
        Variables: string list
        States: (string * ItfValue) list list
    }

[<RequireQualifiedAccess>]
module Itf =
    let defaultLimits =
        {
            MaxBytes = 16 * 1024 * 1024
            MaxDepth = 64
            MaxStates = 10000
            MaxCollection = 100000
        }

    let private quote (s: string) = JsonSerializer.Serialize(s)

    /// Canonical raw ITF JSON, distinct from the legacy replay canonicalization.
    let rec canonical value =
        let array values = "[" + String.concat "," values + "]"

        let tagged tag values =
            "{" + quote tag + ":" + array values + "}"

        let ordered values =
            values |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

        match value with
        | ItfValue.Null -> "null"
        | ItfValue.Boolean b -> if b then "true" else "false"
        | ItfValue.Integer n -> "{\"#bigint\":" + quote (n.ToString(CultureInfo.InvariantCulture)) + "}"
        | ItfValue.Text s -> quote s
        | ItfValue.Sequence xs -> xs |> List.map canonical |> array
        | ItfValue.Tuple xs -> xs |> List.map canonical |> tagged "#tup"
        | ItfValue.Set xs -> xs |> List.map canonical |> ordered |> tagged "#set"
        | ItfValue.Map xs ->
            xs
            |> List.map (fun (k, v) -> array [ canonical k; canonical v ])
            |> ordered
            |> tagged "#map"
        | ItfValue.Record xs ->
            xs
            |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
            |> List.map (fun (k, v) -> quote k + ":" + canonical v)
            |> String.concat ","
            |> fun s -> "{" + s + "}"

    /// Parse raw bytes without opening files, fetching resources, or inferring actions.
    let read (limits: ItfLimits) (bytes: byte array) : Result<ItfDocument, QuintReplayDiagnostic list> =
        let fail code path message =
            raise (ArgumentException(code + "\n" + path + "\n" + message))

        try
            if
                limits.MaxBytes <= 0
                || limits.MaxDepth <= 0
                || limits.MaxStates <= 0
                || limits.MaxCollection <= 0
            then
                fail "ITF-LIMITS" "$" "All limits must be positive."

            if isNull bytes || bytes.Length > limits.MaxBytes then
                fail "ITF-BYTES" "$" "Input exceeds byte limit or is null."

            let json = UTF8Encoding(false, true).GetString(bytes)
            use doc = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = limits.MaxDepth))

            let rec check path (el: JsonElement) =
                match el.ValueKind with
                | JsonValueKind.Object ->
                    let fields = el.EnumerateObject() |> Seq.toList

                    if fields.Length > limits.MaxCollection then
                        fail "ITF-COLLECTION" path "Object exceeds collection limit."

                    let names = Collections.Generic.HashSet<string>(StringComparer.Ordinal)

                    for f in fields do
                        if not (names.Add f.Name) then
                            fail "ITF-DUPLICATE" (path + "/" + f.Name) "Duplicate object key."

                        check (path + "/" + f.Name) f.Value
                | JsonValueKind.Array ->
                    if el.GetArrayLength() > limits.MaxCollection then
                        fail "ITF-COLLECTION" path "Array exceeds collection limit."

                    el.EnumerateArray() |> Seq.iteri (fun i v -> check (path + "/" + string i) v)
                | _ -> ()

            check "$" doc.RootElement

            let rec value path (el: JsonElement) =
                let values (a: JsonElement) =
                    if a.ValueKind <> JsonValueKind.Array then
                        fail "ITF-TAG" path "Tagged collection must be an array."

                    a.EnumerateArray()
                    |> Seq.mapi (fun i x -> value (path + "/" + string i) x)
                    |> Seq.toList

                let unique xs =
                    if xs |> List.map canonical |> List.distinct |> List.length <> xs.Length then
                        fail "ITF-DUPLICATE" path "Duplicate canonical collection member."

                    xs

                let integer (s: string) =
                    let mutable n = BigInteger.Zero

                    if
                        not (BigInteger.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, &n))
                    then
                        fail "ITF-INTEGER" path "Expected an exact decimal integer."

                    ItfValue.Integer n

                match el.ValueKind with
                | JsonValueKind.Null -> ItfValue.Null
                | JsonValueKind.True -> ItfValue.Boolean true
                | JsonValueKind.False -> ItfValue.Boolean false
                | JsonValueKind.String -> ItfValue.Text(el.GetString())
                | JsonValueKind.Number -> integer (el.GetRawText())
                | JsonValueKind.Array -> ItfValue.Sequence(values el)
                | JsonValueKind.Object ->
                    let fields = el.EnumerateObject() |> Seq.toList

                    match fields with
                    | [ f ] when f.Name = "#bigint" && f.Value.ValueKind = JsonValueKind.String ->
                        integer (f.Value.GetString())
                    | [ f ] when f.Name = "#set" -> ItfValue.Set(values f.Value |> unique)
                    | [ f ] when f.Name = "#tup" -> ItfValue.Tuple(values f.Value)
                    | [ f ] when f.Name = "#map" ->
                        let pairs =
                            values f.Value
                            |> List.map (function
                                | ItfValue.Sequence [ k; v ] -> k, v
                                | _ -> fail "ITF-MAP" path "Map entries must contain exactly a key and a value.")

                        pairs |> List.map fst |> unique |> ignore
                        ItfValue.Map pairs
                    | _ when
                        fields
                        |> List.exists (fun f -> f.Name.StartsWith("#", StringComparison.Ordinal))
                        ->
                        fail "ITF-UNSUPPORTED" path "Unsupported or malformed tagged value."
                    | _ -> ItfValue.Record(fields |> List.map (fun f -> f.Name, value (path + "/" + f.Name) f.Value))
                | _ -> fail "ITF-VALUE" path "Unsupported JSON value."

            let root = doc.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                fail "ITF-ROOT" "$" "Expected an object."

            let property (name: string) (el: JsonElement) =
                let mutable result = Unchecked.defaultof<JsonElement>

                if not (el.TryGetProperty(name, &result)) then
                    fail "ITF-FIELD" ("$/" + name) "Missing field."

                result

            for field in root.EnumerateObject() do
                if not ([ "#meta"; "vars"; "states" ] |> List.contains field.Name) then
                    fail "ITF-FIELD" ("$/" + field.Name) "Unsupported root field."

            let mutable metadata = Unchecked.defaultof<JsonElement>

            if root.TryGetProperty("#meta", &metadata) then
                if metadata.ValueKind <> JsonValueKind.Object then
                    fail "ITF-META" "$/#meta" "Metadata must be an object."

                let format = property "format" metadata

                if format.ValueKind <> JsonValueKind.String || format.GetString() <> "ITF" then
                    fail "ITF-META" "$/#meta/format" "Expected ITF format."

            let vars = property "vars" root

            if vars.ValueKind <> JsonValueKind.Array then
                fail "ITF-VARS" "$/vars" "Expected variable names."

            let names =
                vars.EnumerateArray()
                |> Seq.map (fun x ->
                    if x.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(x.GetString()) then
                        fail "ITF-VARS" "$/vars" "Invalid variable name."

                    x.GetString())
                |> Seq.toList

            if
                names.IsEmpty
                || names
                   |> List.exists (fun name -> name.StartsWith("#", StringComparison.Ordinal))
                || names.Length <> (List.distinct names).Length
            then
                fail "ITF-VARS" "$/vars" "Empty or duplicate variables."

            let states = property "states" root

            if
                states.ValueKind <> JsonValueKind.Array
                || states.GetArrayLength() = 0
                || states.GetArrayLength() > limits.MaxStates
            then
                fail "ITF-STATES" "$/states" "Expected a nonempty bounded state array."

            let result =
                states.EnumerateArray()
                |> Seq.mapi (fun i s ->
                    let path = "$/states/" + string i

                    if s.ValueKind <> JsonValueKind.Object then
                        fail "ITF-STATE" path "Expected state object."

                    let fields =
                        s.EnumerateObject()
                        |> Seq.map (fun f -> f.Name)
                        |> Seq.filter ((<>) "#meta")
                        |> Set.ofSeq

                    if fields <> Set.ofList names then
                        fail "ITF-BINDINGS" path "State bindings differ from declared variables."

                    let mutable meta = Unchecked.defaultof<JsonElement>

                    if s.TryGetProperty("#meta", &meta) then
                        let idx = property "index" meta
                        let mutable index = 0

                        if not (idx.TryGetInt32(&index)) || index <> i then
                            fail "ITF-INDEX" path "State index differs from ordinal."

                    names
                    |> List.map (fun name -> name, value (path + "/" + name) (property name s)))
                |> Seq.toList

            Ok { Variables = names; States = result }
        with
        | :? ArgumentException as e ->
            let parts = e.Message.Split('\n', 3)

            if parts.Length = 3 then
                Error
                    [
                        {
                            Code = parts[0]
                            Path = parts[1]
                            Message = parts[2]
                        }
                    ]
            else
                Error
                    [
                        {
                            Code = "ITF-INPUT"
                            Path = "$"
                            Message = "Invalid input or UTF-8."
                        }
                    ]
        | :? JsonException ->
            Error
                [
                    {
                        Code = "ITF-JSON"
                        Path = "$"
                        Message = "Invalid JSON or nesting limit exceeded."
                    }
                ]
        | :? InvalidOperationException ->
            Error
                [
                    {
                        Code = "ITF-SHAPE"
                        Path = "$"
                        Message = "Invalid field shape."
                    }
                ]
