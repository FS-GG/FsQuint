namespace FsQuint

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Json

module private ReplayInternal =
    let diagnostic code path message : QuintReplayDiagnostic =
        {
            Code = code
            Path = path
            Message = message
        }

    let sortDiagnostics diagnostics =
        diagnostics
        |> List.distinct
        |> List.sortBy (fun item -> item.Path, item.Code, item.Message)

    let isLowerSha256 (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && value.Length = 64
        && value
           |> Seq.forall (fun character ->
               (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))

    let escapeJson (value: string) =
        let builder = StringBuilder(value.Length + 2)
        builder.Append('"') |> ignore

        for character in value do
            match character with
            | '"' -> builder.Append("\\\"") |> ignore
            | '\\' -> builder.Append("\\\\") |> ignore
            | '\b' -> builder.Append("\\b") |> ignore
            | '\f' -> builder.Append("\\f") |> ignore
            | '\n' -> builder.Append("\\n") |> ignore
            | '\r' -> builder.Append("\\r") |> ignore
            | '\t' -> builder.Append("\\t") |> ignore
            | value when int value < 0x20 ->
                builder.Append("\\u").Append((int value).ToString("x4", CultureInfo.InvariantCulture))
                |> ignore
            | value -> builder.Append(value) |> ignore

        builder.Append('"').ToString()

    let canonicalInteger (value: string) =
        let mutable parsed = 0I

        if String.IsNullOrWhiteSpace value then
            None
        elif
            System.Numerics.BigInteger.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                &parsed
            )
        then
            Some(parsed.ToString(CultureInfo.InvariantCulture))
        else
            None

    let rec encodeAt path value =
        match value with
        | Null -> Ok "null"
        | Boolean true -> Ok "true"
        | Boolean false -> Ok "false"
        | Integer value ->
            match canonicalInteger value with
            | Some canonical -> Ok canonical
            | None ->
                Error
                    [
                        diagnostic "QRP-VALUE-INTEGER" path "Integer values must use base-10 integer syntax."
                    ]
        | Text value when Object.ReferenceEquals(value, null) ->
            Error [ diagnostic "QRP-VALUE-TEXT" path "Text values cannot be null." ]
        | Text value -> Ok(escapeJson value)
        | Sequence values ->
            values
            |> List.mapi (fun index item -> encodeAt $"%s{path}[%d{index}]" item)
            |> collect $"%s{path}"
            |> Result.map (String.concat "," >> fun body -> $"[%s{body}]")
        | Set values ->
            values
            |> List.mapi (fun index item -> encodeAt $"%s{path}[%d{index}]" item)
            |> collect path
            |> Result.bind (fun encoded ->
                let canonical =
                    encoded
                    |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))

                if List.distinct canonical |> List.length <> canonical.Length then
                    Error
                        [
                            diagnostic
                                "QRP-VALUE-SET-DUPLICATE"
                                path
                                "Set values must be unique after canonical encoding."
                        ]
                else
                    let body = String.concat "," canonical
                    Ok("{\"#set\":[" + body + "]}"))
        | Record fields ->
            let names = fields |> List.map fst

            let duplicates =
                names
                |> List.countBy id
                |> List.choose (fun (name, count) -> if count > 1 then Some name else None)
                |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))

            let keyDiagnostics =
                fields
                |> List.mapi (fun index (name, _) ->
                    if String.IsNullOrWhiteSpace name then
                        [
                            diagnostic "QRP-VALUE-RECORD-KEY" $"%s{path}[%d{index}]" "Record keys cannot be blank."
                        ]
                    else
                        [])
                |> List.concat

            if not duplicates.IsEmpty || not keyDiagnostics.IsEmpty then
                [
                    for duplicate in duplicates do
                        diagnostic "QRP-VALUE-RECORD-DUPLICATE" path $"Record key '%s{duplicate}' is duplicated."
                    yield! keyDiagnostics
                ]
                |> sortDiagnostics
                |> Error
            else
                fields
                |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
                |> List.map (fun (name, item) ->
                    encodeAt $"%s{path}.%s{name}" item
                    |> Result.map (fun encoded -> $"%s{escapeJson name}:%s{encoded}"))
                |> collect path
                |> Result.map (String.concat "," >> fun body -> "{" + body + "}")

    and collect path results =
        let values =
            results
            |> List.choose (function
                | Ok value -> Some value
                | Error _ -> None)

        let diagnostics =
            results
            |> List.collect (function
                | Ok _ -> []
                | Error findings -> findings)

        if diagnostics.IsEmpty then
            Ok values
        else
            Error(sortDiagnostics diagnostics)

    let validateSource path (source: QuintReplaySourceBinding) =
        [
            if String.IsNullOrWhiteSpace source.Path then
                diagnostic "QRP-SOURCE-PATH" $"%s{path}.path" "Source path is required."
            if source.Line < 1 then
                diagnostic "QRP-SOURCE-LINE" $"%s{path}.line" "Source line must be positive."
            if source.Column < 1 then
                diagnostic "QRP-SOURCE-COLUMN" $"%s{path}.column" "Source column must be positive."
        ]

    let validateStateContent path (state: QuintReplayState) =
        let bindingNames = state.Bindings |> List.map fst

        let structural =
            [
                for index, (name, _) in state.Bindings |> List.indexed do
                    if String.IsNullOrWhiteSpace name then
                        diagnostic
                            "QRP-STATE-BINDING"
                            $"%s{path}.bindings[%d{index}]"
                            "State binding names cannot be blank."

                for name, count in bindingNames |> List.countBy id |> List.sortBy fst do
                    if count > 1 then
                        diagnostic
                            "QRP-STATE-BINDING-DUPLICATE"
                            $"%s{path}.bindings"
                            $"State binding '%s{name}' is duplicated."
            ]

        let valueDiagnostics =
            state.Bindings
            |> List.mapi (fun index (_, value) ->
                match encodeAt $"%s{path}.bindings[%d{index}]" value with
                | Ok _ -> []
                | Error findings -> findings)
            |> List.concat

        sortDiagnostics (structural @ valueDiagnostics)

    let encodeStateUnchecked (state: QuintReplayState) =
        state.Bindings
        |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
        |> List.map (fun (name, value) ->
            match encodeAt "$.bindings" value with
            | Ok encoded -> $"%s{escapeJson name}:%s{encoded}"
            | Error _ -> invalidOp "State was encoded before validation completed.")
        |> String.concat ","
        |> fun bindings -> "{" + bindings + "}"

    let stateFingerprint (state: QuintReplayState) =
        let bytes = Encoding.UTF8.GetBytes(encodeStateUnchecked state)

        SHA256.HashData bytes
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let encodeSource (source: QuintReplaySourceBinding) =
        $"{{\"path\":%s{escapeJson source.Path},\"line\":%d{source.Line},\"column\":%d{source.Column}}}"

    let encodeEnvironment (environment: QuintReplayEnvironment) =
        let bounds =
            environment.Bounds
            |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
            |> List.map (fun (name, value) -> $"%s{escapeJson name}:%d{value}")
            |> String.concat ","

        $"{{\"seed\":%s{escapeJson environment.Seed},\"bounds\":{{%s{bounds}}},\"toolFingerprint\":%s{escapeJson environment.ToolFingerprint},\"profileFingerprint\":%s{escapeJson environment.ProfileFingerprint},\"contractFingerprint\":%s{escapeJson environment.ContractFingerprint},\"adapterFingerprint\":%s{escapeJson environment.AdapterFingerprint},\"implementationFingerprint\":%s{escapeJson environment.ImplementationFingerprint}}}"

    let encodeTraceUnchecked (trace: QuintReplayTrace) =
        let steps =
            trace.Steps
            |> List.map (fun (step: QuintReplayStep) ->
                $"{{\"index\":%d{step.Index},\"action\":%s{escapeJson step.Action},\"source\":%s{encodeSource step.Source},\"expected\":%s{encodeStateUnchecked step.Expected}}}")
            |> String.concat ","

        $"{{\"schemaVersion\":%d{trace.SchemaVersion},\"environment\":%s{encodeEnvironment trace.Environment},\"initial\":%s{encodeStateUnchecked trace.Initial},\"steps\":[%s{steps}]}}"

    let traceFingerprint (trace: QuintReplayTrace) =
        encodeTraceUnchecked trace
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let checkFields path required allowed (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then
            [ diagnostic "QRP-ITF-TYPE" path "Expected an object." ]
        else
            let names = element.EnumerateObject() |> Seq.map _.Name |> Seq.toList

            [
                for name, count in names |> List.countBy id do
                    if count > 1 then
                        diagnostic "QRP-ITF-DUPLICATE-FIELD" (path + "/" + name) "Duplicate ITF field."

                for name in names |> List.distinct do
                    if not (Set.contains name allowed) then
                        diagnostic "QRP-ITF-UNSUPPORTED-FIELD" (path + "/" + name) "Unknown ITF field."

                for name in required do
                    if not (List.contains name names) then
                        diagnostic "QRP-ITF-REQUIRED" (path + "/" + name) "Required ITF field is absent."
            ]

    let property (name: string) (element: JsonElement) =
        match element.TryGetProperty name with
        | true, value -> Some value
        | _ -> None

    let collectDecoded (results: Result<'value, QuintReplayDiagnostic list> list) =
        let values =
            results
            |> List.choose (function
                | Ok value -> Some value
                | Error _ -> None)

        let diagnostics =
            results
            |> List.collect (function
                | Ok _ -> []
                | Error findings -> findings)

        if diagnostics.IsEmpty then
            Ok values
        else
            Error(sortDiagnostics diagnostics)

    let rec decodeItfValue (path: string) (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null -> Ok Null
        | JsonValueKind.True -> Ok(QuintReplayValue.Boolean true)
        | JsonValueKind.False -> Ok(QuintReplayValue.Boolean false)
        | JsonValueKind.String ->
            match element.GetString() with
            | null -> Error [ diagnostic "QRP-ITF-TYPE" path "ITF strings cannot be null." ]
            | value -> Ok(Text value)
        | JsonValueKind.Number ->
            match element.TryGetInt64() with
            | true, value -> Ok(Integer(value.ToString(CultureInfo.InvariantCulture)))
            | _ -> Error [ diagnostic "QRP-ITF-INTEGER" path "ITF numbers must be integers." ]
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.indexed
            |> Seq.map (fun (index, item) -> decodeItfValue $"%s{path}/%d{index}" item)
            |> Seq.toList
            |> collectDecoded
            |> Result.map Sequence
        | JsonValueKind.Object ->
            match property "#bigint" element, property "#set" element with
            | Some bigint, None when
                element.EnumerateObject() |> Seq.length = 1
                && bigint.ValueKind = JsonValueKind.String
                ->
                match bigint.GetString() |> Option.ofObj |> Option.bind canonicalInteger with
                | Some value -> Ok(Integer value)
                | None -> Error [ diagnostic "QRP-ITF-INTEGER" (path + "/#bigint") "Invalid ITF bigint." ]
            | None, Some set when
                element.EnumerateObject() |> Seq.length = 1
                && set.ValueKind = JsonValueKind.Array
                ->
                set.EnumerateArray()
                |> Seq.indexed
                |> Seq.map (fun (index, item) -> decodeItfValue $"%s{path}/#set/%d{index}" item)
                |> Seq.toList
                |> collectDecoded
                |> Result.map Set
            | None, None when element.EnumerateObject() |> Seq.exists (fun f -> f.Name.StartsWith("#", StringComparison.Ordinal)) ->
                Error [ diagnostic "QRP-ITF-UNSUPPORTED" path "Tagged value is outside the schema-v1 replay subset; use Itf.read and an explicit projection." ]
            | None, None ->
                element.EnumerateObject()
                |> Seq.map (fun item ->
                    decodeItfValue (path + "/" + item.Name) item.Value
                    |> Result.map (fun value -> item.Name, value))
                |> Seq.toList
                |> collectDecoded
                |> Result.map Record
            | _ -> Error [ diagnostic "QRP-ITF-SHAPE" path "Unsupported or ambiguous ITF tagged value." ]
        | _ -> Error [ diagnostic "QRP-ITF-TYPE" path "Unsupported ITF value kind." ]

    let validateState path (state: QuintReplayState) =
        let contentDiagnostics = validateStateContent path state

        [
            yield! contentDiagnostics

            if not (isLowerSha256 state.Identity) then
                diagnostic
                    "QRP-STATE-IDENTITY"
                    $"%s{path}.identity"
                    "State identity must be a lowercase SHA-256 digest."
            elif contentDiagnostics.IsEmpty then
                let expected = stateFingerprint state

                if not (String.Equals(state.Identity, expected, StringComparison.Ordinal)) then
                    diagnostic
                        "QRP-STATE-FINGERPRINT"
                        $"%s{path}.identity"
                        $"State identity does not match canonical state fingerprint '%s{expected}'."
        ]
        |> sortDiagnostics

    let validateFingerprint path value =
        if isLowerSha256 value then
            []
        else
            [
                diagnostic "QRP-ENV-FINGERPRINT" path "Environment fingerprints must be lowercase SHA-256 digests."
            ]

[<RequireQualifiedAccess>]
module QuintReplay =
    let encodeValue value = ReplayInternal.encodeAt "$" value

    let encodeState state =
        match ReplayInternal.validateState "$" state with
        | [] -> Ok(ReplayInternal.encodeStateUnchecked state)
        | diagnostics -> Error diagnostics

    let stateFingerprint state =
        match ReplayInternal.validateStateContent "$" state with
        | [] -> Ok(ReplayInternal.stateFingerprint state)
        | diagnostics -> Error diagnostics

    let validateTrace (trace: QuintReplayTrace) =
        let environment = trace.Environment

        let boundsDiagnostics =
            [
                for index, (name, value) in environment.Bounds |> List.indexed do
                    if String.IsNullOrWhiteSpace name then
                        ReplayInternal.diagnostic
                            "QRP-BOUND-NAME"
                            $"$.environment.bounds[%d{index}]"
                            "Bound names cannot be blank."

                    if value < 0L then
                        ReplayInternal.diagnostic
                            "QRP-BOUND-VALUE"
                            $"$.environment.bounds[%d{index}]"
                            "Bound values cannot be negative."

                for name, count in environment.Bounds |> List.map fst |> List.countBy id |> List.sortBy fst do
                    if count > 1 then
                        ReplayInternal.diagnostic
                            "QRP-BOUND-DUPLICATE"
                            "$.environment.bounds"
                            $"Bound '%s{name}' is duplicated."
            ]

        let stepDiagnostics =
            [
                for ordinal, step in trace.Steps |> List.indexed do
                    let expectedIndex = ordinal + 1

                    if step.Index <> expectedIndex then
                        ReplayInternal.diagnostic
                            "QRP-STEP-ORDER"
                            $"$.steps[%d{ordinal}].index"
                            $"Expected step index %d{expectedIndex}."

                    if String.IsNullOrWhiteSpace step.Action then
                        ReplayInternal.diagnostic "QRP-STEP-ACTION" $"$.steps[%d{ordinal}].action" "Action is required."

                    yield! ReplayInternal.validateSource $"$.steps[%d{ordinal}].source" step.Source
                    yield! ReplayInternal.validateState $"$.steps[%d{ordinal}].expected" step.Expected
            ]

        let structural =
            [
                if trace.SchemaVersion <> 1 then
                    ReplayInternal.diagnostic
                        "QRP-SCHEMA-VERSION"
                        "$.schemaVersion"
                        "Only quint-replay-v1 schema version 1 is supported."
                if not (ReplayInternal.isLowerSha256 trace.TraceIdentity) then
                    ReplayInternal.diagnostic
                        "QRP-TRACE-IDENTITY"
                        "$.traceIdentity"
                        "Trace identity must be a lowercase SHA-256 digest."
                if String.IsNullOrWhiteSpace environment.Seed then
                    ReplayInternal.diagnostic "QRP-SEED" "$.environment.seed" "Replay seed is required."

                yield! ReplayInternal.validateFingerprint "$.environment.toolFingerprint" environment.ToolFingerprint
                yield!
                    ReplayInternal.validateFingerprint "$.environment.profileFingerprint" environment.ProfileFingerprint
                yield!
                    ReplayInternal.validateFingerprint
                        "$.environment.contractFingerprint"
                        environment.ContractFingerprint
                yield!
                    ReplayInternal.validateFingerprint "$.environment.adapterFingerprint" environment.AdapterFingerprint
                yield!
                    ReplayInternal.validateFingerprint
                        "$.environment.implementationFingerprint"
                        environment.ImplementationFingerprint
                yield! boundsDiagnostics
                yield! ReplayInternal.validateState "$.initial" trace.Initial
                yield! stepDiagnostics
            ]
            |> ReplayInternal.sortDiagnostics

        if structural.IsEmpty then
            let expected = ReplayInternal.traceFingerprint trace

            if String.Equals(trace.TraceIdentity, expected, StringComparison.Ordinal) then
                []
            else
                [
                    ReplayInternal.diagnostic
                        "QRP-TRACE-FINGERPRINT"
                        "$.traceIdentity"
                        $"Trace identity does not match canonical trace fingerprint '%s{expected}'."
                ]
        else
            structural

    let traceFingerprint (trace: QuintReplayTrace) =
        let placeholder =
            { trace with
                TraceIdentity = String.replicate 64 "0"
            }

        let diagnostics =
            validateTrace placeholder
            |> List.filter (fun finding -> finding.Code <> "QRP-TRACE-FINGERPRINT")

        if diagnostics.IsEmpty then
            Ok(ReplayInternal.traceFingerprint placeholder)
        else
            Error diagnostics

    let private decodeItfUnchecked (context: QuintItfDecodeContext) (text: string) =
        try
            use document = JsonDocument.Parse text
            let root = document.RootElement
            let rootFields = Set.ofList [ "#meta"; "vars"; "states" ]
            let mutable diagnostics = ReplayInternal.checkFields "$" rootFields rootFields root

            let metaFields = Set.ofList [ "format"; "format-description"; "source"; "status" ]

            match ReplayInternal.property "#meta" root with
            | Some meta ->
                diagnostics <- ReplayInternal.checkFields "$/#meta" metaFields metaFields meta @ diagnostics

                for name, expected in [ "format", "ITF"; "status", "ok" ] do
                    match ReplayInternal.property name meta with
                    | Some value when value.ValueKind = JsonValueKind.String && value.GetString() = expected -> ()
                    | _ ->
                        diagnostics <-
                            ReplayInternal.diagnostic
                                "QRP-ITF-META"
                                ($"$/#meta/%s{name}")
                                $"Expected ITF metadata '%s{name}' to be '%s{expected}'."
                            :: diagnostics
            | None -> ()

            let variables =
                match ReplayInternal.property "vars" root with
                | Some values when values.ValueKind = JsonValueKind.Array ->
                    values.EnumerateArray()
                    |> Seq.indexed
                    |> Seq.choose (fun (index, value) ->
                        if value.ValueKind = JsonValueKind.String then
                            value.GetString() |> Option.ofObj
                        else
                            diagnostics <-
                                ReplayInternal.diagnostic
                                    "QRP-ITF-VAR"
                                    ($"$/vars/%d{index}")
                                    "ITF variable names must be strings."
                                :: diagnostics

                            None)
                    |> Seq.toList
                | Some _ ->
                    diagnostics <-
                        ReplayInternal.diagnostic "QRP-ITF-TYPE" "$/vars" "ITF vars must be an array."
                        :: diagnostics

                    []
                | None -> []

            if variables.IsEmpty || List.distinct variables |> List.length <> variables.Length then
                diagnostics <-
                    ReplayInternal.diagnostic "QRP-ITF-VARS" "$/vars" "ITF variables must be non-empty and unique."
                    :: diagnostics

            let states =
                match ReplayInternal.property "states" root with
                | Some values when values.ValueKind = JsonValueKind.Array -> values.EnumerateArray() |> Seq.toList
                | Some _ ->
                    diagnostics <-
                        ReplayInternal.diagnostic "QRP-ITF-TYPE" "$/states" "ITF states must be an array."
                        :: diagnostics

                    []
                | None -> []

            if states.IsEmpty then
                diagnostics <-
                    ReplayInternal.diagnostic "QRP-ITF-STATES" "$/states" "ITF needs an initial state."
                    :: diagnostics

            let decodedStates =
                states
                |> List.mapi (fun index state ->
                    let path = $"$/states/%d{index}"
                    let fields = Set.add "#meta" (Set.ofList variables)
                    diagnostics <- ReplayInternal.checkFields path fields fields state @ diagnostics

                    match ReplayInternal.property "#meta" state with
                    | Some meta ->
                        let required = Set.ofList [ "index" ]

                        diagnostics <-
                            ReplayInternal.checkFields (path + "/#meta") required required meta
                            @ diagnostics

                        match ReplayInternal.property "index" meta with
                        | Some value when value.ValueKind = JsonValueKind.Number ->
                            match value.TryGetInt32() with
                            | true, actual when actual = index -> ()
                            | _ ->
                                diagnostics <-
                                    ReplayInternal.diagnostic
                                        "QRP-ITF-STATE-INDEX"
                                        (path + "/#meta/index")
                                        $"Expected ITF state index %d{index}."
                                    :: diagnostics
                        | _ ->
                            diagnostics <-
                                ReplayInternal.diagnostic
                                    "QRP-ITF-STATE-INDEX"
                                    (path + "/#meta/index")
                                    "ITF state index must be an integer."
                                :: diagnostics
                    | None -> ()

                    let bindings =
                        variables
                        |> List.choose (fun name ->
                            match ReplayInternal.property name state with
                            | Some value ->
                                match ReplayInternal.decodeItfValue (path + "/" + name) value with
                                | Ok decoded -> Some(name, decoded)
                                | Error findings ->
                                    diagnostics <- findings @ diagnostics
                                    None
                            | None -> None)

                    let draft = { Identity = ""; Bindings = bindings }

                    { draft with
                        Identity = ReplayInternal.stateFingerprint draft
                    })

            if context.Steps.Length <> max 0 (decodedStates.Length - 1) then
                diagnostics <-
                    ReplayInternal.diagnostic
                        "QRP-ITF-STEP-BINDINGS"
                        "$.context.steps"
                        "One consumer action/source binding is required per ITF transition."
                    :: diagnostics

            let steps =
                let transitionStates = decodedStates |> List.skip (min 1 decodedStates.Length)

                if transitionStates.Length = context.Steps.Length then
                    List.zip transitionStates context.Steps
                    |> List.map (fun (state, binding) ->
                        {
                            Index = binding.Index
                            Action = binding.Action
                            Source = binding.Source
                            Expected = state
                        })
                else
                    []

            let initial =
                decodedStates
                |> List.tryHead
                |> Option.defaultValue
                    {
                        Identity = String.replicate 64 "0"
                        Bindings = []
                    }

            let draft =
                {
                    SchemaVersion = 1
                    TraceIdentity = String.replicate 64 "0"
                    Environment = context.Environment
                    Initial = initial
                    Steps = steps
                }

            let trace =
                { draft with
                    TraceIdentity = ReplayInternal.traceFingerprint draft
                }

            let all = ReplayInternal.sortDiagnostics (diagnostics @ validateTrace trace)
            if all.IsEmpty then Ok trace else Error all
        with :? JsonException as ex ->
            Error [ ReplayInternal.diagnostic "QRP-ITF-MALFORMED" "$" ex.Message ]

    let decodeItf context (text: string) =
        if isNull text || text.Length > Itf.defaultLimits.MaxBytes then
            Error [ ReplayInternal.diagnostic "QRP-ITF-LIMIT" "$" "Input is null or exceeds the input limit." ]
        else
            try
                match Itf.read Itf.defaultLimits (UTF8Encoding(false, true).GetBytes(text)) with
                | Error errors -> Error errors
                | Ok _ -> decodeItfUnchecked context text
            with :? EncoderFallbackException ->
                Error [ ReplayInternal.diagnostic "QRP-ITF-UTF8" "$" "Input contains an invalid Unicode scalar." ]

    let compare trace observations =
        let traceDiagnostics = validateTrace trace

        let observationDiagnostics =
            [
                for ordinal, observation in observations |> List.indexed do
                    if observation.Index <> ordinal + 1 then
                        ReplayInternal.diagnostic
                            "QRP-OBSERVATION-ORDER"
                            $"$.observations[%d{ordinal}].index"
                            $"Expected observation index %d{ordinal + 1}."

                    if String.IsNullOrWhiteSpace observation.Action then
                        ReplayInternal.diagnostic
                            "QRP-OBSERVATION-ACTION"
                            $"$.observations[%d{ordinal}].action"
                            "Observed action is required."

                    yield! ReplayInternal.validateSource $"$.observations[%d{ordinal}].source" observation.Source
                    yield! ReplayInternal.validateState $"$.observations[%d{ordinal}].actual" observation.Actual
            ]
            |> ReplayInternal.sortDiagnostics

        let diagnostics =
            ReplayInternal.sortDiagnostics (traceDiagnostics @ observationDiagnostics)

        if not diagnostics.IsEmpty then
            Error diagnostics
        else
            let rec first (expected: QuintReplayStep list) (actual: QuintReplayObservation list) =
                match expected, actual with
                | [], [] -> QuintReplayResult.Equivalent
                | step :: _, [] ->
                    QuintReplayResult.Diverged
                        {
                            Step = step.Index
                            Action = step.Action
                            Source = step.Source
                            Expected = Some step.Expected
                            Actual = None
                            Reason = "missing-observation"
                        }
                | [], observation :: _ ->
                    QuintReplayResult.Diverged
                        {
                            Step = observation.Index
                            Action = observation.Action
                            Source = observation.Source
                            Expected = None
                            Actual = Some observation.Actual
                            Reason = "unexpected-observation"
                        }
                | step :: remainingExpected, observation :: remainingActual ->
                    let expectedJson = ReplayInternal.encodeStateUnchecked step.Expected
                    let actualJson = ReplayInternal.encodeStateUnchecked observation.Actual

                    if step.Index <> observation.Index then
                        QuintReplayResult.Diverged
                            {
                                Step = min step.Index observation.Index
                                Action = step.Action
                                Source = step.Source
                                Expected = Some step.Expected
                                Actual = Some observation.Actual
                                Reason = "step-identity"
                            }
                    elif not (String.Equals(step.Action, observation.Action, StringComparison.Ordinal)) then
                        QuintReplayResult.Diverged
                            {
                                Step = step.Index
                                Action = step.Action
                                Source = step.Source
                                Expected = Some step.Expected
                                Actual = Some observation.Actual
                                Reason = "action-identity"
                            }
                    elif step.Source <> observation.Source then
                        QuintReplayResult.Diverged
                            {
                                Step = step.Index
                                Action = step.Action
                                Source = step.Source
                                Expected = Some step.Expected
                                Actual = Some observation.Actual
                                Reason = "source-binding"
                            }
                    elif
                        not (
                            String.Equals(step.Expected.Identity, observation.Actual.Identity, StringComparison.Ordinal)
                        )
                        || not (String.Equals(expectedJson, actualJson, StringComparison.Ordinal))
                    then
                        QuintReplayResult.Diverged
                            {
                                Step = step.Index
                                Action = step.Action
                                Source = step.Source
                                Expected = Some step.Expected
                                Actual = Some observation.Actual
                                Reason = "state"
                            }
                    else
                        first remainingExpected remainingActual

            Ok(first trace.Steps observations)
