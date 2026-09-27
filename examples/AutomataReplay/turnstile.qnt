// FQA-3 second consumer: one turnstile, sequential coin/push inputs, no physical timing.
module turnstile {
  type State = { phase: str, actions: List[str] }
  var state: State
  var input: str
  pure def evolve(s: State, event: str): State =
    if (event == "coin") {phase:"Unlocked", actions:List("thank")}
    else if (s.phase == "Locked") {phase:"Locked", actions:List("alarm")}
    else {phase:"Locked", actions:List("pass")}
  action init = all { state' = {phase:"Locked",actions:List()}, input' = "init" }
  action take(event: str): bool = all {
    Set("coin","push").contains(event), state' = evolve(state,event), input' = event }
  action step = { nondet event = Set("coin","push").oneOf(); take(event) }
  val safety = Set("Locked","Unlocked").contains(state.phase) and state.actions.length() <= 1
}
