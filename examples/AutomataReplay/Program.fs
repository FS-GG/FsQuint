// Package-boundary characterization for FQA-0. This is not Quint conformance evidence.
open ByzantineSystems.Automata.Core

let require message condition =
    if not condition then failwith message

let unwrap = function
    | Ok value -> value
    | Error error -> failwithf "Unexpected error: %A" error

type State = Off | On
type Event = Toggle | Remind | Unknown

let chart entry =
    statechart<State, Event, string, string> {
        root "switch"
        classify (function Off -> stateId "off" | On -> stateId "on")
        state "off" {
            on (fun _ e -> e = Toggle) (fun _ _ -> [ "toggle" ], On)
        }
        state "on" {
            onEntry (fun _ _ -> [ entry ])
            onExit (fun _ _ -> [ "exit" ])
            on (fun _ e -> e = Toggle) (fun _ _ -> [ "toggle" ], Off)
            internalOn (fun _ e -> e = Remind) (fun _ _ -> [ "reminder" ])
        }
    } |> unwrap

let baseline = chart "entry"
let entered = Chart.resolve baseline Off Toggle |> unwrap
require "Next state differs" (entered.Next = On)
require "Rule/entry actions must remain ordered" (entered.Actions = [ "toggle"; "entry" ])
require "Wrong handler" (entered.HandledBy = stateId "off")
require "Wrong exit path" (entered.Exited = [ stateId "off" ])
require "Wrong entry path" (entered.Entered = [ stateId "on" ])

let reminded = Chart.resolve baseline On Remind |> unwrap
require "Internal transition changes state" (reminded.Next = On)
require "Internal transition runs exit/entry callbacks" (reminded.Exited.IsEmpty && reminded.Entered.IsEmpty)
require "Internal action missing" (reminded.Actions = [ "reminder" ])

let exited = Chart.resolve baseline On Toggle |> unwrap
require "Exit/rule actions must remain ordered" (exited.Actions = [ "exit"; "toggle" ])
match Chart.resolve baseline On Unknown with
| Error(TransitionError.Unhandled(id, _)) -> require "Wrong unhandled leaf" (id = stateId "on")
| other -> failwithf "Unhandled event was not retained: %A" other

let changedClosure = chart "different-entry"
require "Closure-only change unexpectedly changes structural fingerprint"
    (Chart.fingerprint baseline = Chart.fingerprint changedClosure)
require "Closure-only behavior change was not observable"
    ((Chart.resolve changedClosure Off Toggle |> unwrap).Actions <> entered.Actions)

let guarded: Rule<State, Event, string, string> =
    Rule.transition (fun (_: State) e -> e = Toggle) (fun _ _ -> [], On)
    |> Rule.guarded (fun _ _ -> false) "not-authorized"

match Rule.invoke guarded Off Unknown with
| RuleVerdict.GuardFailed "not-authorized" -> ()
| other -> failwithf "Guard-before-event-predicate characterization changed: %A" other

printfn "PASS: Automata.Core 0.5.0 package API, paths, ordered effects, internal/unhandled outcomes, opaque closures and guard precedence."
Conformance.check System.AppContext.BaseDirectory
Resolver.check System.AppContext.BaseDirectory
