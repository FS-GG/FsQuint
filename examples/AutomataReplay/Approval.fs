module Approval

open ByzantineSystems.Automata.Core

type Phase = Draft | Pending | Approved | Published | Cancelled
type Actor = Author | Reviewer
type Input = Submit | Approve of Actor | Publish | Cancel | Remind
type Effect = { Kind: string; Name: string }
type Domain = { Phase: Phase; ApprovedBy: string }
type Observation = { Domain: Domain; Outcome: string; Actions: Effect list }
type Mutation = Correct | Guard | ActionOrder | WrongTarget | Projection

let initialObservation = { Domain = { Phase = Draft; ApprovedBy = "None" }; Outcome = "Initial"; Actions = [] }
let effect kind name = { Kind = kind; Name = name }
let unwrap = function Ok x -> x | Error e -> failwithf "%A" e

let chart mutation =
    let change phase (s: Domain) = { s with Phase = phase }
    statechart<Domain, Input, Effect, string> {
        root "document"
        classify (fun s -> stateId (string s.Phase))
        state "active" {
            initial "Draft"
            on (fun _ e -> e = Cancel) (fun s _ -> [effect "audit" "cancelled"], change Cancelled s)
            state "Draft" {
                on (fun _ e -> e = Submit) (fun s _ ->
                    let actions = [effect "audit" "submitted"; effect "notify" "review"]
                    (if mutation = ActionOrder then List.rev actions else actions),
                    change (if mutation = WrongTarget then Approved else Pending) s)
            }
            state "review" {
                initial "Pending"
                internalOn (fun _ e -> e = Remind) (fun _ _ -> [effect "notify" "reminder"])
                state "Pending" {
                    attempt (fun _ e -> match e with Approve _ -> true | _ -> false) (fun s e ->
                        if e = Approve Author && mutation <> Guard then Error "NotAuthorized"
                        else Ok([effect "audit" "approved"], { s with Phase = Approved; ApprovedBy = "Reviewer" }))
                }
                state "Approved" {
                    on (fun _ e -> e = Publish) (fun s _ ->
                        [effect "audit" "published"; effect "notify" "published"], change Published s)
                }
            }
        }
        state "Published" { terminal }
        state "Cancelled" { terminal }
    } |> unwrap

let apply chart current input =
    if current.Domain.Phase = Published || current.Domain.Phase = Cancelled then
        { current with Outcome = "Terminated"; Actions = [] }
    else
        match Chart.resolve chart current.Domain input with
        | Ok resolution -> { Domain = resolution.Next; Outcome = "Applied"; Actions = resolution.Actions }
        | Error(TransitionError.Rejected error) -> { current with Outcome = error; Actions = [] }
        | Error(TransitionError.Unhandled _) -> { current with Outcome = "Unhandled"; Actions = [] }
        | Error error -> failwithf "Unexpected resolution error: %A" error
