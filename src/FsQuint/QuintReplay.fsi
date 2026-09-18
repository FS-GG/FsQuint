namespace FsQuint

[<RequireQualifiedAccess>]
module QuintReplay =
    /// Encode one value as deterministic JSON. Record keys and set values are canonicalized.
    val encodeValue: value: QuintReplayValue -> Result<string, QuintReplayDiagnostic list>

    /// Encode one state as deterministic JSON after validating its identity and bindings.
    val encodeState: state: QuintReplayState -> Result<string, QuintReplayDiagnostic list>

    /// Return the lowercase SHA-256 identity of a valid state's canonical JSON bytes.
    val stateFingerprint: state: QuintReplayState -> Result<string, QuintReplayDiagnostic list>

    /// Validate all trace identities, ordered steps, bounds, states, and source bindings.
    val validateTrace: trace: QuintReplayTrace -> QuintReplayDiagnostic list

    /// Return the lowercase SHA-256 identity of canonical trace content, excluding its derived identity field.
    val traceFingerprint: trace: QuintReplayTrace -> Result<string, QuintReplayDiagnostic list>

    /// Strictly decode an ITF state sequence and bind consumer-owned action/source identities.
    val decodeItf:
        context: QuintItfDecodeContext -> text: string -> Result<QuintReplayTrace, QuintReplayDiagnostic list>

    /// Compare ordered observations and return the exact first action/source/state divergence.
    val compare:
        trace: QuintReplayTrace ->
        observations: QuintReplayObservation list ->
            Result<QuintReplayResult, QuintReplayDiagnostic list>
