# Shared disposable-worker host loop

Use the worker/subagent mechanism the current host exposes. If it can create an isolated worktree for a
worker, request that isolation; otherwise create a fresh worktree from the fetched default branch
before starting the worker. If the host has no worker mechanism, run the same milestone loop
sequentially in fresh worktrees. Never invent another host's tool name or syntax.

Each worker starts with fresh context and a current default-branch worktree, mints its own
`FSGG_WORKER` identity, owns its own claim and one bounded unit, and is discarded after verified
completion. After its first green implementation/test/evidence loop, the worker starts one fresh
independent critic and follows [critique-contract](critique-contract.md). The critic may write only
the critique artifact, never implementation, tests, lifecycle artifacts, or roadmap state. Reuse the
same critic for confirmation so it can verify dispositions against its original findings.
Route no more than ten numbered repairs. Validate the ordered commit chain and confirm its latest
round is less than ten before routing each one. A failed tenth confirmation must leave the milestone
unchecked and unmerged with a terminal human escalation in its critique artifact; never start round
eleven or accept that artifact as passing.

A host session, account, or parent identity is not a substitute for the worker identity. Check the
worker's PR, merge, tests, release obligations, critique, feedback, and ledger update against
repository state.

When handing a claim forward — including relaying a worker's or critic's claim — preserve a
`Verification:` field for every specific, checkable fact about code, history, or an external source.
The field contains the actual command, `file:line`, API call, or URL used, or exactly `unverified` when
the claim was not checked. Before sending, confirm each such assertion has that field; an assertion
without one is incomplete, not verified. `unverified` is an acceptable and explicit handoff state, not
a reason to invent evidence.

Apply the exact fail-closed critique command in [critique-contract](critique-contract.md) to the
merged artifact before accepting it; worker or critic prose is not verification. For a
`game_functionality: true` milestone that command's `player_journeys` check IS the bot-driven
player journey gate (`.github#2087`): missing, empty (without a fail-closed
`entry_point_not_test_ownable`), bypass-surfaced, or non-boot-entry journey evidence fails the
merged artifact exactly as a missing critique field does — never a warning, never a pass.
Apply the exact fail-closed commands in [feedback-contract](feedback-contract.md) to the merged cycle
paths before accepting it; worker prose is not verification. Re-read the ledger after every completion;
never schedule from the stale copy given to the previous worker.

Invoke skills through the selector supported by the current host: for example, `$work-roadmap` in
Codex or the host's skill picker. A literal `/skill` is documentation only unless that host explicitly
supports slash-based skill selection.

Terminate only after a fresh read proves no required unit remains, every cycle critique and feedback
gate passes, and the final reporting and cross-cycle disposition obligations are landed.
