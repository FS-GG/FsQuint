---
name: work-roadmap
description: Use when explicitly asked to complete a markdown roadmap milestone by milestone. Routine delivery is the default; use heavyweight ceremony only for human-named scope.
---

# work-roadmap

Burn down a markdown roadmap milestone by milestone. The roadmap—not a project board—is the ledger.

## Routine is the default process

Read `.fsgg/routine-development.json`. Use the routine route for every unified-roadmap milestone unless a
human has recorded an explicit instruction selecting heavyweight process for named scope. Absence,
ambiguity, a strict label, GS2 registration, a protected or sensitive path, a policy change, modeled work,
a protected operation, or inherited strict state does not select heavyweight ceremony. Preserve existing
evidence as history, not as a route selector. Do not manufacture a heavy instruction from risk, size, rank,
labels, artifacts, or tooling defaults.

Technical and operational safeguards are orthogonal. Keep focused and repository-native checks, canonical
model/formal authority, permissions, and release/deploy/credential/destructive/cutover/external-acceptance
controls fail-closed. A missing or invalid authorization blocks only its affected effect and may leave that
operation pending; it does not add an issue/claim/SDD/critic/receipt lifecycle to source delivery.

For routine delivery:

1. Keep one accountable owner in the current implementation session. Create a fresh
   `routine/<outcome-slug>` branch from current default branch. Complete adjacent dependent micro-steps
   as tested commits on that branch, then open one PR for a coherent, reviewable outcome. Do not open a
   draft PR for each checkpoint or test. The relevant roadmap checkbox updates and concise evidence land
   in the same PR, and completion is recorded only after its native merge readback. Under
   `work-unified-roadmap`, the parent integrator admits that PR within its live queue capacity; workers
   keep branches local until admitted, and the integrator uses the canonical `.github`
   `tools/pr-lane-admission.py` helper for new PR creation.
2. Do **not** create or require an issue, claim, SDD artifact family, phase lifecycle ledger,
   independent critic, feedback report, telemetry/receipt cycle, roadmap cycle envelope, receipt-only
   or projection PR except the mandatory Unified section 0 closure follow-up in step 5, or metadata-`Done`
   write. Independent review is optional or sampled and never a
   second authority. Best-effort telemetry is asynchronous and cannot block valid delivery.
3. Run the smallest relevant automated technical checks. Before opening the PR, run the routine
   eligibility and operation-boundary fixtures. Put exactly one
   `<!-- fsgg:routine-development/v1 head=<exact-head-sha> operation=<allowed-operation> -->` marker in
   the PR body. The distinct `routine-eligibility` context is defined by a default-branch
   `pull_request_target` workflow and loads its validator and policy from the exact PR base; it never
   checks out or executes candidate bytes. It validates exact base/head, operation, and changed paths
   without reading board, issue, claim, SDD, review, feedback, or Done state. Candidate-controlled
   `claim-generation` remains only the strict item gate and treats routine branches as not applicable.
   Until branch protection is armed after observation, the pilot owner must still require the reported
   `routine-eligibility` success. A moved head refuses. For an existing-PR repair, review the local repair SHA,
   rebind the PR marker before pushing it, then qualify that exact new head; obsolete-head checks cannot qualify
   or poison it.
   This is a trusted-repository-writer reliability boundary: it catches accidental mistakes and drift but
   does not claim adversarial protection from a writer able to alter Actions workflows or spoof a name-based
   check context. The accountable human explicitly accepted that smaller guarantee for routine work;
   protected/high-assurance operations retain their stronger authority boundaries.
4. Repair on the same PR, normally in no more than two material attempts. After required checks pass
   for the exact head, merge through GitHub's native merge boundary and read back the PR's merged state
   and merge commit. Report code delivery separately from any protected publication still pending.
5. Re-read the roadmap from default branch and continue. Keep projection asynchronous; do not launch a
   model turn or PR merely to copy already-merged facts. The sole exception is the mandatory Unified
   Roadmap section 0 closure projection after authoritative cross-repository readback: land its immediate
   progress-only follow-up under `work-unified-roadmap`. This exception does not apply to CI ticks, waits,
   intermediate checkpoints or any other status copy, and does not create a PR per checkpoint.

Apply the permanent qualification-selection doctrine in
ADR-0084 (`https://github.com/FS-GG/.github/blob/main/docs/adr/0084-semantic-reuse-never-cancels-coherent-validation.md`). Run the cheap
content/semantic classifier first; after acceptance, start the coherent run, and only a validated exact-head
`reused` receipt may start delivery concurrently with it. `current` or missing valid reuse waits for a
pass, while `deferred`/`failed` refuses. Keep post-merge pending distinct from disputed, and block dependent
acceptance or activation on a late failure. Use the shared `tools/routine-delivery.py` behavior rather than
reimplementing these transitions in the skill.

For source work associated with a protected operation, use the routine steps for the source PR and leave the
operation pending until its independent authority and safeguards permit it. Invalid or unknown technical or
operation authorization fails the affected effect; it does not reclassify delivery as heavyweight.

## Opt in one item to observed normal-root execution

Native collaboration remains the default roadmap route. For one assigned item selected prospectively, the
driver may instead launch its normal root through the packaged `fsgg-coord-engine telemetry runtime codex-exec`
entrypoint. This changes the execution route for that item; ordinary delivery authority and all existing safety,
permission and operation controls still apply. Do not activate a workspace, change generated defaults or Host data,
or use this route to claim historical usage.

Create one absolute, regular, non-symlink private root assignment with mode `0600`:

```json
{"schema":"fsgg.telemetry.codex-assignment/1","featureId":"<feature>","itemId":"<item>","attemptId":"<root-attempt>","parentAttemptId":null,"producerStream":"<producer>"}
```

For a member whose original item differs from its item ID, first merge that exact mapping into the protected
`.github` assignment registry. Before starting the observed root, run the copied adapter's
`population-only --feature <feature> --item <item> --original-item <original>` command with the selected
receipt-scoped workspace config. It reads the protected mapping at an immutable revision and emits only an open
population fact. A successful result requires an `applied` receiver receipt; a merely durable receipt or lost
response retains the exact private batch for retry. Use the same member item ID in this command, the root
assignment and routine delivery. Do not call `begin`, `started` or `finish` for this observed root: the launcher
already owns its sole expected dispatch, lineage, time and native turn usage. Missing usage remains a gap.

Select the existing private host configuration and repository explicitly, and preserve Codex's machine-readable,
non-persistent launch flags:

```console
fsgg-coord-engine telemetry runtime codex-exec \
  --assignment /private/root-assignment.json \
  --config /private/telemetry.json --repository OWNER/REPO -- \
  --json --ephemeral -m MODEL -c model_reasoning_effort=EFFORT "assigned item"
```

For a remote workspace, invoke the launcher through that host's approved credential-loading wrapper. An explicit
`--config` selects the association but does not supply the receiver credential. On Main's development container,
use `fdev-telemetry exec` before `fsgg-coord-engine`; keep the credential out of prompts, logs and arguments.

Any descendant must call the same launcher from the inherited invocation context, without copying the assignment;
use the default child relation or explicit `--relation follow-up` as applicable. The inherited context preserves
the selected store, repository association and lineage. If any root or descendant invokes unobserved native
collaboration, the whole-item token total remains unknown.

Call the item total proven only after every expected invocation is present, every invocation has a terminal
outcome and final completed-turn counters, and every submitted batch has an applied Host receipt. Missing,
pending or conflicting evidence remains an explicit coverage gap; never infer a total from partial counters.

## Record repository-owned dispatches by default

This native collaboration route applies when the opt-in `codex-exec` route above was not selected. The launcher
owns expected dispatches for its root and descendants; do not also call this section's `begin`, `started` or
`finish` commands for those invocations.

At driver entry, start one root observation with this skill's `scripts/roadmap-telemetry.py begin` adapter
(also exposed as `tools/roadmap-telemetry.py` in `.github`), supplying the roadmap
feature/item/attempt and selected model/effort. For distinct member items under one canonical original, pass
the same `--original-item` at each root; child and follow-up observations inherit it. For non-self originals,
first merge the exact member/original mapping into the protected
`docs/coordination/telemetry-original-item-assignments.json` registry. The adapter reads its protected GitHub
revision. Immediately
after every `spawn_agent`, bind the returned native agent id with `started`. Before each child or `followup_task`, start another observation with its stable attempt,
the parent's token and the `child` or `follow-up` relation. When an agent becomes terminal, call `finish` with its
real outcome before accepting its handoff; finish the root observation before returning. Pass the same stable
identities as `--telemetry-feature`, `--telemetry-item` and `--telemetry-attempt` to routine delivery so it
discovers the private host configuration and creates its CI assignment automatically.

Observation is asynchronous and never adds delivery ceremony. Missing configuration, publication, start or
terminal evidence is reported once as a coverage gap. With a Codex parent thread identity, the adapter joins
native child thread and turn records to the dispatch and verifies their final usage from private host records.
After late usage or a follow-up, call `usage-reconcile --token <private-token>` for the affected terminal attempt.
Until all turns are verified, report native usage as unknown; hosts without a joinable parent retain
`native-collaboration-usage-unsupported`. Expected population, lineage, requested model/effort and terminal
outcome alone do not establish complete usage.

The adapter associates an ordinary local invocation with `FSGG_TELEMETRY_REPOSITORY` first,
`GITHUB_REPOSITORY` second, then one credential-free canonical GitHub `origin` from the current checkout.
Malformed, non-GitHub, missing or ambiguous origins refuse; never guess an association or print remote credentials.
For workspace mutations, it loads a missing selected credential only through the owner-controlled telemetry client.
It persists each exact batch and next phase before submission; a retry replays those bytes and never regenerates
timestamps or identities after an unacknowledged or later-applied delivery.

Record typed activity spans for planning, implementation, review, validation, delivery, repair and operations;
use other/unclassified when necessary, and record complications with evidence as they occur. Attribute usage only
to an exact native usage observation—never by elapsed time—and keep mixed/unclassified explicit. After each
attempt terminal, author one bounded private process review through `review`; author the item review only after
the expected population is terminal. Reviews are advisory, evidence-linked observations: missing evidence is
unknown, and they never change delivery authority.

When the private host has an explicit event-publication activation receipt, the canonical adapter requests one bounded
dashboard refresh after a completed root attempt drains successfully and after each successful post-terminal root
observation drain. Publication and its returned health are advisory: failure, missing credentials, contention or an
unchanged snapshot never blocks delivery or changes the recorded terminal state. Do not assume or require a daemon.

## Human-selected heavyweight route

Use the following route only when a recorded explicit human instruction selects it for named scope. A prior
strict route remains historical evidence unless that human instruction also says to continue it.

1. Read the complete roadmap and select the next unchecked, dependency-ready milestone.
2. Spawn one fresh disposable worker from current default branch and give it only that milestone.
   During worker setup, interactive/game work must explicitly invoke the `pnext-item` performance-first
   planning gate before implementation begins. A milestone that ships or claims reachable game
   functionality must record `game_functionality: true` and a passing bot-driven headless player
   journey (`.github#2087`) — a bot driving the product through the same control messages a real
   player emits, booted at the product's real entry point, not seeded into a mid-game state. No such
   milestone may reach a `shipReady`-equivalent verdict without it.
3. Give the worker a stable feedback cycle id. It follows the repository's SDD lifecycle and item/PR
   merge discipline and invokes `fs-gg-feedback-report` at every required checkpoint boundary.
4. After the first green implementation/test/evidence loop, have the worker start one fresh independent
   critic. The critic reviews requirements, diff, tests, architecture, and roadmap evidence without
   editing the implementation. The worker repairs blocker/major findings, and the same critic confirms
   them before the worker updates the roadmap and lands the milestone. For a work-roadmap milestone,
   this milestone critique loop owns the review/repair count and supersedes `$pnext-item`'s normal
   three-round cap; all other applicable `$pnext-item` planning, review-evidence, exact-SHA, merge,
   release, and escalation discipline remains in force. Permit at most ten numbered
   repair/confirmation rounds. If round ten remains red, record the terminal human escalation, stop
   the milestone, and never start round eleven or merge.
5. Verify the merge, tests, roadmap checkbox/evidence, release obligations, critique artifact,
   checkpoint state, and schema-v2 report externally. Missing, invalid, or unreadable critique or
   feedback state fails closed.
6. Discard the worker and critic, refresh default branch, re-read the roadmap, and select again.
   Persist a typed cycle envelope beside the roadmap: fresh-read Markdown into its source revision
   and units, run `fsgg-coord cycle inspect`, then `register` (or exact-id resume) for the milestone.
   Bind each unit's stable roadmap feedback/critique identity as `providerCycleId`. Pass the actual
   generated SDD verification, validated schema-v3 critique, and validated schema-v2 feedback artifacts
   to `advance`: each provider input names `rootPath` and `artifactPath`, while feedback also names its
   `auditPath` and ordered `phases`. The engine reruns `fsgg-sdd verify` and the canonical critique and
   feedback validators itself; normalized or minimally shaped caller-authored envelopes are not
   provider evidence, and journey applicability comes from the critique artifact. Persist the
   `updateReceipt` emitted and durably journaled by the guarded merged-head/checkpoint `update`;
   `complete` consumes that exact receipt and revalidates its bound
   evidence, followed by another fresh inspection.
   Parallel milestones additionally require recorded disjoint touch-sets and explicit operator
   authorization. Missing, stale, or wrong-cycle evidence fails closed.
7. After no unchecked milestone remains, validate every completed cycle and land the final report with
   cross-cycle critique and feedback roll-ups; a report that omits a cycle, critique disposition, or
   checkpoint disposition cannot finish.

## Lifecycle log

Create the item's externally durable, digest-chained lifecycle log on its canonical GitHub issue before its first phase transition and append every
phase start, completion, block, and resume. Include numbered critique/repair rounds, guarded landing,
protected-main verification, receipt/projection, and cleanup as distinct phases. Validate the log at each
worker/critic handoff, before host acceptance, before cycle completion, and in the final cross-cycle
roll-up. A missing phase, sequence gap, invalid transition, or unresolved active phase fails closed.

The candidate branch must not be the live authority. Later review, merge, protected-main, projection, and
cleanup events would change the very head they attest and create an infinite review loop. A tracked
`logs/roadmap/` file is only an immutable export made after the covered interval; it never gates its own
candidate head. Raw runtime reports remain private and untracked, while public events retain aggregate
counts plus stable report digests and runtime identifiers.

Record the exact provider/model variant and effort plus runtime, coordination, SDD CLI/contracts, and
ledger-schema versions for each phase, alongside authoritative input, cached
input, cache-write input, output, reasoning, and total token usage. Token accounting is reconciled after
the response finishes from the runtime session record or stable provider response; a terminal phase may
not turn a temporarily unavailable in-turn counter into permanent `unavailable`. When a runtime truly
provides no authoritative record, retain the concrete host/source reason; never infer or estimate. Freeze
one immutable private usage receipt per phase when cited so later appends cannot invalidate prior events.
Collection archives frozen receipts in the canonical per-user content-addressed private store; `/tmp` and
repository-only copies are not retention. Seal, validation, and roll-up resolve by digest. An already-missing
receipt needs a separately reviewed non-counting legacy proof and is excluded, never reconstructed.
Durations and historical averages are derived from recorded UTC timestamps and comparable prior duration
evidence with the same tooling fingerprint, using only whole discrete minutes. Read [lifecycle-log](references/lifecycle-log.md) for the canonical path,
schema, transition rules, token accounting, and exact validation command.

The roadmap driver is the supervising parent for every dispatched worker, critic, confirmation,
recovery, or host phase. A child returns an unposted terminal draft with `pending final usage` plus its
exact session/turn or transcript identity. Only after that child is terminal does the driver harvest the
completed local record, seal and post its measured terminal event, and accept the handoff. The driver
must not interpret “the final count is written after my response” as terminal `unavailable`; host
acceptance, cycle completion, cross-cycle roll-up, and roadmap Done all fail closed while a completed
child lacks post-response reconciliation. A genuine unavailable result requires a documented
post-completion uniqueness lookup or strict-schema failure. Repair an already-posted legacy timing reason
with a distinct telemetry-reconciliation phase rather than editing the immutable event.

For an extraordinary immutable history that neither its creating nor current toolkit can repair, the
accountable human may authorize the first-class synthetic checkpoint described by the lifecycle reference.
The driver binds the proof to the exact item/run/unit and frontier, declares missing provenance is not
required and no data is reconstructed, carries passing functional verification, and appends exactly one
checkpoint phase. Its completion is the new trusted anchor; ordinary strict processing resumes immediately.
Never infer this authority from broad decision latitude or use it without the immutable human authorization.

## Status position line

Every intermediate roadmap-driver, worker, or critic-handoff status reply must start with:

`Roadmap item: **<unit-id> — <name>** · GitHub: <linked owner/repo#number>`

Make the issue label a Markdown hyperlink to its canonical GitHub issue URL.

Follow it with one current, compact, ordered process-position line using:

- `✅` completed
- `🟢` active (exactly one while work is progressing)
- `⚪` pending
- `🔴` blocked (and no `🟢` while blocked)

Name active repair rounds. Show each completed step's actual duration and historical average for that
canonical step; show active elapsed time. Use only whole minutes: floor active elapsed; round completed
actuals and arithmetic averages to nearest. Average prior completed occurrences in this roadmap run,
excluding the current one; use `avg n/a` without a prior observation. Use recorded lifecycle timestamps,
never invented timing evidence. Give each concurrently reported item its own header and position line.

Example:

`✅ Intake (actual 2 min · avg 3 min) → ✅ Claim (actual 1 min · avg n/a) → 🟢 Repair round 3 (active · elapsed 4 min) → ⚪ Host acceptance → ⚪ Guarded merge`

The header and line complement, not replace, prose and evidence.

Milestones are sequential unless the roadmap explicitly establishes disjoint parallel milestones and
the user authorized parallel execution. Load [host-loop](references/host-loop.md) for shared
fresh-worker and verification rules and [roadmap-ledger](references/roadmap-ledger.md) for markdown
state transitions.
Load [feedback-contract](references/feedback-contract.md) for the worker activation, exact validation
commands, zero-event representation, host acceptance gate, and final roll-up contract.
Load [critique-contract](references/critique-contract.md) for critic isolation, severity and repair
rules, the artifact schema, exact validation command, and host acceptance gate.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
