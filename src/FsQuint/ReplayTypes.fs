namespace FsQuint

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Json

type QuintReplaySourceBinding =
    { Path: string; Line: int; Column: int }

type QuintReplayValue =
    | Null
    | Boolean of bool
    | Integer of string
    | Text of string
    | Sequence of QuintReplayValue list
    | Set of QuintReplayValue list
    | Record of (string * QuintReplayValue) list

type QuintReplayState =
    {
        Identity: string
        Bindings: (string * QuintReplayValue) list
    }

type QuintReplayStep =
    {
        Index: int
        Action: string
        Source: QuintReplaySourceBinding
        Expected: QuintReplayState
    }

type QuintReplayEnvironment =
    {
        Seed: string
        Bounds: (string * int64) list
        ToolFingerprint: string
        ProfileFingerprint: string
        ContractFingerprint: string
        AdapterFingerprint: string
        ImplementationFingerprint: string
    }

type QuintReplayTrace =
    {
        SchemaVersion: int
        TraceIdentity: string
        Environment: QuintReplayEnvironment
        Initial: QuintReplayState
        Steps: QuintReplayStep list
    }

type QuintItfStepBinding =
    {
        Index: int
        Action: string
        Source: QuintReplaySourceBinding
    }

type QuintItfDecodeContext =
    {
        Environment: QuintReplayEnvironment
        Steps: QuintItfStepBinding list
    }

type QuintReplayObservation =
    {
        Index: int
        Action: string
        Source: QuintReplaySourceBinding
        Actual: QuintReplayState
    }

type QuintReplayDiagnostic =
    {
        Code: string
        Path: string
        Message: string
    }

type QuintReplayDivergence =
    {
        Step: int
        Action: string
        Source: QuintReplaySourceBinding
        Expected: QuintReplayState option
        Actual: QuintReplayState option
        Reason: string
    }

[<RequireQualifiedAccess>]
type QuintReplayResult =
    | Equivalent
    | Diverged of QuintReplayDivergence
