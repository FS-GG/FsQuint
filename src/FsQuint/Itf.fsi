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
    /// Finite defaults: 16 MiB, depth 64, 10,000 states, 100,000 collection elements.
    val defaultLimits: ItfLimits
    /// Encode a decoded raw value canonically. Distinct from schema-v1 replay JSON.
    val canonical: value: ItfValue -> string
    /// Strict bounded raw ITF decoding; no action inference or I/O.
    val read: limits: ItfLimits -> bytes: byte array -> Result<ItfDocument, QuintReplayDiagnostic list>
