// FQA-1: one document, synchronous atomic inputs, two roles; no runtime/store claims.
// Closed strings are wire identities; finite inputs and invariants constrain their domain.
module approval {
  type Input = { op: str, actor: str }
  type Effect = { kind: str, name: str }
  type State = { phase: str, approvedBy: str, outcome: str, actions: List[Effect] }
  var state: State
  // Instrumented input is separate from the domain observation, never inferred from it.
  var input: Input
  pure val inputs = Set(
    { op: "submit", actor: "author" }, { op: "approve", actor: "author" },
    { op: "approve", actor: "reviewer" }, { op: "publish", actor: "author" },
    { op: "cancel", actor: "author" }, { op: "remind", actor: "author" })
  pure def effect(k: str, n: str): Effect = { kind: k, name: n }
  pure val initial: State = { phase: "Draft", approvedBy: "None", outcome: "Initial", actions: List() }
  pure def terminal(s: State): bool = Set("Published", "Cancelled").contains(s.phase)
  pure def evolve(s: State, i: Input): State = {
    val cleared = { ...s, actions: List(), outcome: "Applied" }
    if (terminal(s)) { ...cleared, outcome: "Terminated" }
    else if (i.op == "submit" and s.phase == "Draft")
      { ...cleared, phase: "Pending", actions: List(effect("audit", "submitted"), effect("notify", "review")) }
    else if (i.op == "approve" and s.phase == "Pending")
      if (i.actor == "author") { ...cleared, outcome: "NotAuthorized" }
      else { ...cleared, phase: "Approved", approvedBy: "Reviewer", actions: List(effect("audit", "approved")) }
    else if (i.op == "publish" and s.phase == "Approved")
      { ...cleared, phase: "Published", actions: List(effect("audit", "published"), effect("notify", "published")) }
    else if (i.op == "remind" and Set("Pending", "Approved").contains(s.phase))
      { ...cleared, actions: List(effect("notify", "reminder")) }
    else if (i.op == "cancel")
      { ...cleared, phase: "Cancelled", actions: List(effect("audit", "cancelled")) }
    else { ...cleared, outcome: "Unhandled" }
  }
  action init = all { state' = initial, input' = { op: "init", actor: "system" } }
  action take(i: Input): bool = all { inputs.contains(i), state' = evolve(state, i), input' = i }
  action step = { nondet i = inputs.oneOf(); take(i) }
  val safety = and {
    Set("Draft", "Pending", "Approved", "Published", "Cancelled").contains(state.phase),
    Set("Approved", "Published").contains(state.phase) implies state.approvedBy == "Reviewer",
    Set("NotAuthorized", "Unhandled", "Terminated").contains(state.outcome) implies state.actions == List(),
  }
  val transitionSafety = inputs.forall(i => and {
    terminal(state) implies evolve(state, i).phase == state.phase,
    (i.op == "approve" and i.actor == "author") implies evolve(state, i).approvedBy == state.approvedBy,
  })
}
