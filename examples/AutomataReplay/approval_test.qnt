module approval_test {
  import approval.* from "./approval"
  run approvalTest = init
    .then(take({ op: "submit", actor: "author" }))
    .then(take({ op: "approve", actor: "author" }))
    .expect(state.outcome == "NotAuthorized")
    .then(take({ op: "remind", actor: "author" }))
    .then(take({ op: "approve", actor: "reviewer" }))
    .then(take({ op: "remind", actor: "author" }))
    .then(take({ op: "publish", actor: "author" }))
    .expect(state.phase == "Published")
    .then(take({ op: "cancel", actor: "author" }))
    .expect(state.outcome == "Terminated")
  run cancelTest = init
    .then(take({ op: "publish", actor: "author" }))
    .expect(state.outcome == "Unhandled")
    .then(take({ op: "cancel", actor: "author" }))
    .then(take({ op: "submit", actor: "author" }))
    .expect(state.phase == "Cancelled" and state.outcome == "Terminated")
  run pendingCancelTest = init
    .then(take({ op: "submit", actor: "author" }))
    .then(take({ op: "cancel", actor: "author" }))
    .expect(state.phase == "Cancelled")
  run approvedCancelTest = init
    .then(take({ op: "submit", actor: "author" }))
    .then(take({ op: "approve", actor: "reviewer" }))
    .then(take({ op: "cancel", actor: "author" }))
    .expect(state.phase == "Cancelled" and state.approvedBy == "Reviewer")
}
