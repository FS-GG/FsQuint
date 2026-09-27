# Independent milestone critique gate

Run one bounded qualitative review per milestone after the first green implementation/test/evidence
loop and before verify/ship/PR orchestration. The implementation worker starts a fresh critic with the
milestone text, roadmap path, cycle id, base ref, current head, diff, lifecycle artifacts, tests, and
evidence. Do not give it the worker's conclusions or ask it to approve a predetermined result.

The critic reviews only:

- milestone and acceptance-requirement coverage;
- correctness and regression risk in the diff;
- test quality and missing negative/boundary coverage;
- architectural fit and avoidable coupling;
- sufficiency and accuracy of roadmap completion evidence.

The critic must cite concrete repository evidence. Style preferences and hypothetical concerns without
an observable failure mode are not findings. It may write `reviews/roadmap/<cycle-id>.json` and nothing
else. It must not edit implementation, tests, SDD artifacts, feedback, or roadmap state.

## Handoff-assertion provenance

Any handoff assertion about a specific, checkable code, history, or external-source fact carries a
`Verification:` field. Its value is either the basis actually used (for example a command, `file:line`,
API call, or URL) or exactly `unverified`. `unverified` is a valid, non-pejorative result: it tells the
receiver what still needs checking instead of making a guess look established. Do not infer a basis
from surrounding prose.

Before sending or relaying a handoff, use this checklist: for every such assertion, confirm that its
`Verification:` field is present and is either a reproducible basis or `unverified`. A receiver rejects
an assertion with no field as incomplete rather than treating it as checked. This applies equally to a
worker or critic producing the assertion and to a host relaying it onward.

Use `blocker` when the milestone cannot safely ship, `major` when acceptance, correctness, regression
protection, or architecture materially fails, and `minor` for bounded non-acceptance debt. The worker
must resolve every blocker/major finding in the milestone branch. A minor finding may be resolved or
routed to a durable issue or unchecked roadmap item; never bury it in prose.

For work-roadmap milestones, this critique contract owns the review/repair count and supersedes
`$pnext-item`'s normal three-round cap. The exception is limited to that count: all other applicable
`$pnext-item` planning, review-evidence, exact-SHA, merge, release, and escalation discipline remains
in force.

## Game functionality — the bot-driven player journey gate

This gate is **blocking**, not advisory (`.github#2087`). Every milestone declares
`game_functionality: true` or `false`. When `true`, the milestone cannot reach a passing verdict —
initial or confirmation — without at least one recorded `player_journeys` entry, and the critic
reviews the journeys themselves, not only their result: whether the messages used are genuinely
player-emittable and whether the start point is genuinely the product's entry. `false` is for
non-game milestones and every non-game repository; this gate never applies there.

A journey is evidence only when it was driven **through the product's real input surface** — the
same control messages a player emits. Direct `Msg` injection, a test-only API, or any seam that
exists solely for tests is **not evidence** and is a blocker/major finding, never a note. A journey
must **boot at the product's real entry point** and reach its functionality by navigating as a
player would; seeding a mid-game model is a gate failure, including when the seeded run reports the
functionality reached — reachability claimed from an unreachable start is exactly the eleven false
`shipReady` verdicts this gate exists to stop (`2026-08-02-Rogue3.md` §4.3). The milestone also
declares `uncovered_functionality`: game functionality named by the milestone that no journey
reached is reported there, never silently absent — this is what closes the `FS.GG.Game#563`
blind spot rather than inheriting it. Where the product's entry point is not yet test-ownable, the
milestone sets `entry_point_not_test_ownable: true` with a concrete
`entry_point_not_test_ownable_reason` and the gate **fails closed**: `game_functionality: true`
with empty `player_journeys` is otherwise always a validation error, never a silent pass.

One advisory input is explicitly **not** consumed as blocking here: `FS.GG.Game#563`'s
`DegenerateVocabulary` check fires unconditionally on declared-vocabulary cardinality alone, so it
flags a legitimately single-inhabitant slot with zero `Unbound` arms. A `DegenerateVocabulary`-only
finding, with no accompanying `Unbound`-arm evidence, must not by itself block this gate or a
milestone verdict.

Allow at most ten numbered worker repair rounds, each followed by confirmation of the exact repaired
head by the same critic. Before routing a repair, validate the ordered commit chain and permit the
repair only when the latest round is less than ten. This count-before-routing gate prevents a failed
tenth confirmation from racing into an eleventh repair. If the tenth confirmation still reports a
blocker/major finding, record the human escalation described below, stop the milestone, and do not
check its roadmap box, merge it, or start round eleven. A human must decide the required action or
change the acceptance boundary before work resumes. Self-approval by the implementation worker is
invalid. A no-finding review is valid when the critic records the reviewed scope and both verdicts.

The JSON artifact uses schema version 3:

```json
{
  "schema_version": 3,
  "cycle_id": "roadmap-example-m1-example",
  "milestone": "M1 — example",
  "critic": "fresh critic identity",
  "initial_reviewed_commit": "0000000000000000000000000000000000000000",
  "scope": ["requirements", "diff", "tests", "architecture", "roadmap-evidence"],
  "initial_verdict": "pass",
  "repair_rounds": 0,
  "reviewed_commits": ["0000000000000000000000000000000000000000"],
  "findings": [],
  "confirmation": {
    "reviewed_commit": "0000000000000000000000000000000000000000",
    "verdict": "pass",
    "unresolved_blocker_major": []
  },
  "human_escalation": null,
  "game_functionality": false,
  "player_journeys": [],
  "uncovered_functionality": [],
  "entry_point_not_test_ownable": false,
  "entry_point_not_test_ownable_reason": null
}
```

A game milestone (`game_functionality: true`) with a test-ownable entry point instead carries at
least one journey:

```json
{
  "game_functionality": true,
  "player_journeys": [
    {
      "functionality": "rogue3 starting-room descent",
      "entry_point": "product-boot",
      "input_surface": "player-control-messages",
      "reached": true,
      "evidence": ["headless run 2026-08-02T09:12Z: reached starting room, descended one level"]
    }
  ],
  "uncovered_functionality": [],
  "entry_point_not_test_ownable": false,
  "entry_point_not_test_ownable_reason": null
}
```

`entry_point` accepts only `"product-boot"` — anything else (a seeded mid-game model, a screen the
player did not navigate to) is rejected. `input_surface` accepts only `"player-control-messages"` —
`"direct-msg-injection"`, `"test-only-api"`, and every other value are rejected; this is AC2's
falsifiable leg. A journey that claims `"reached": true` from a non-`"product-boot"` entry point is
rejected even though the functionality was technically exercised, because the start point was not
the player's — this is AC6's Rogue3-shape falsifiable leg.

Each finding has a unique `id`, `severity`, `summary`, non-empty `evidence` array, `disposition`, and
non-empty `resolution_evidence` array. `disposition` is normally `resolved` or `follow-up`.
Blocker/major findings must be `resolved`; minor follow-ups must cite the durable issue or roadmap item
in `resolution_evidence`. The only exception is `unresolved` for a blocker/major named by both matching
tenth-round confirmation and human-escalation ID lists. Those lists must exactly equal the set of
`unresolved` finding IDs. `reviewed_commits` is the unique ordered exact-SHA chain: the initial reviewed
commit followed by exactly one new commit per repair round. The confirmation SHA must equal its final
entry.

After a failed tenth confirmation, set `human_escalation` to an object containing the final
`reviewed_commit`, a non-empty `unresolved_blocker_major` list, and a concrete non-empty
`action_required` string. Leave the confirmation verdict and unresolved list truthful. The validator
then fails closed with `human escalation is terminal and cannot satisfy milestone acceptance`; this is
the durable stop signal, not an acceptance artifact. No automated worker may clear it or reset the
round count. Only an explicit human decision may start separately scoped renewed work.

Before handoff, the worker validates the artifact:

```sh
scripts/fsgg-coord telemetry critique validate \
  --cycle <cycle-id> --artifact reviews/roadmap/<cycle-id>.json
```

The host repeats that exact command against the merged artifact. Missing, unreadable, malformed,
wrong-cycle, incomplete-scope, invalid-severity, unevidenced, unresolved blocker/major, excessive
repair-round, or non-passing confirmation state fails closed. The roadmap evidence must point to the
validated artifact. The final completion report names every milestone's critique artifact and rolls up
resolved blocker/major counts plus outstanding minor follow-ups.
