# Roadmap lifecycle log

Each roadmap item has one externally durable append-only ledger on its canonical GitHub issue. Every
event is a `fsgg:item-lifecycle/v1` structured comment posted through the verified `fsgg-coord comment`
boundary by the live claim holder. That claim worker is the single append authority; critics and other
actors return timestamped phase/usage receipts to it, and it records their `actor` unchanged. The command
refuses lifecycle writes by any other worker or to any target other than the canonical item. An optional
immutable export uses:

```text
logs/roadmap/<roadmap-slug>/<run-id>/<unit-id>.jsonl
```

Create it when the item begins. Never edit, delete, reorder, or renumber accepted comments. Append an
event immediately when a phase starts, completes, blocks, or resumes. A status reply is a projection of
this ledger; prose is not a substitute for it.

The candidate branch cannot be the live authority: critique, merge, protected-main, projection, and
cleanup happen after its exact head is fixed, and appending those future facts would invalidate that
head forever. Tracked JSONL is a later immutable projection under a distinct source-bound item and never
gates the PR that carries it. Raw request-level usage and machine-local paths remain private/untracked.
Each phase freezes its own immutable usage receipt when cited; later phases use new receipts, so new
telemetry never changes an earlier event's provenance digest.

Immutability includes durable private retention. Every completed collector report is archived outside
git, the repository, and system temporary storage at the canonical per-user host-state root
(`$FSGG_USAGE_RECEIPT_STORE`, else `$XDG_STATE_HOME/fsgg/telemetry/usage`, else platform local
application data), addressed as `sha256/<first-two-hex>/<64-hex>.csv`. Owner-only modes, atomic
no-overwrite writes, byte-identical collision checks, and digest verification on every resolution are
mandatory. Lifecycle seal, validation, and final roll-up locate omitted explicit receipts here.

An already-missing historical receipt is never rebuilt from public aggregate counts. It requires a
distinct reviewer's accepted `fsgg.telemetry.legacy-receipt-proof/v1`, bound to the original event and
receipt digests, canonical issue/comment, exhaustive lookup, and immutable review evidence, plus a later
lifecycle event citing `legacy-receipt-proof:sha256:<proof-digest>`. The sole decision
`irrecoverable-exclude-usage` excludes those counts from roll-up; it is not a measured receipt or waiver.

## Event contract

Every line is one JSON object with these fields:

- `schema_version`: integer `1`.
- `revision`, `previous_digest`, `digest`: contiguous digest chain over canonical event JSON.
- `authority`: `github_issue_comment`, canonical item subject, and exact claim generation.
- `run_id`, `unit_id`: lowercase identifiers matching the validator arguments.
- `item`: `repo`, positive `number`, and exact HTTPS `url` for that issue.
- `sequence`: positive contiguous integer, starting at `1`.
- `phase_order`: positive contiguous integer in first-seen phase order.
- `phase`: lowercase identifier; use a distinct identifier for every numbered repair.
- `event`: `started`, `completed`, `blocked`, or `resumed`.
- `at`: canonical UTC `YYYY-MM-DDTHH:MM:SSZ`, nondecreasing within each phase. Comment/revision order
  remains authoritative when independently measured actor phases overlap.
- `actor`: minted worker, critic, or accountable host identity.
- `model`: either `{"status":"recorded","provider":"...","name":"...","effort":"...","source":"..."}`
  from an authoritative host/runtime observation, or
  `{"status":"unavailable","reason":"...","source":"..."}`.
  Never infer a model from an agent label. One phase binds one model; a model change starts a distinct
  continuation/recovery phase so its duration and tokens remain attributable.
- `tooling`: `ledger_schema` plus versioned `runtime`, `coordination`, `sdd`, and `contracts`
  components. Record exact versions and sources or an explicit unavailable/not-applicable reason.
  Historical duration/token comparisons use only rows with the same canonical tooling fingerprint.
- `source`: object containing `repository` and either a 40-hex `revision` or an
  `unavailable_reason` explaining why no source revision exists yet.
- `evidence`: non-empty array of reproducible commands, paths, API calls, or URLs.
- `actual_minutes`: `null` on `started`/`resumed`; on `blocked`/`completed`, elapsed wall time from the
  phase's first `started` event, rounded to the nearest whole minute.
- `historical_durations_minutes`: empty on non-`completed` events; on `completed`, the prior completed
  durations supplied by a validated history report for the same canonical phase and tooling fingerprint.
- `historical_average_minutes`: `null` when the basis is empty; otherwise the nearest whole-minute
  arithmetic mean of `historical_durations_minutes`.
- `token_usage`: `{"status":"pending"}` on `started`/`resumed`. Draft a terminal event locally at the
  boundary, wait for the closing response to finish, then post it once with reconciled usage; never post
  a pending terminal comment or edit an accepted comment. Reconcile it from the runtime record to
  `{"status":"measured","input":N,"cached_input":N,"cache_write_input":N,"output":N,"reasoning":N,
  "total":N,"source":"...","session_ids":[...],"turn_ids":[...]}` with non-negative integers and
  `total = input + output`, or
  `{"status":"unavailable","reason":"...","source":"..."}`. The validator joins measured rows to
  the private usage report and verifies counts, model, runtime, coordination, SDD, and contracts versions.
  Counts are phase-local deltas from an
  authoritative host/provider receipt, never estimates or context-window sizes.

Independent actor phases may overlap and their actual UTC intervals must be retained; events remain
nondecreasing within each phase. `blocked` pauses only that phase; only the same phase may `resume`, block
again, or complete. A completed phase is terminal. The status line still selects exactly one primary
active process position and calls out concurrent work in prose. The final item ledger has no active or
blocked phase.

GitHub comment ids provide the server-assigned total order. If a legacy or pre-upgrade race left multiple
children of one predecessor, export deterministically accepts the lowest comment id and reports every later
sibling on stderr as preserved rejected-fork evidence. Never delete or edit those comments. Comment
creation then re-reads the complete live authority and elects the lowest GitHub comment id among the
same run/unit/revision/predecessor key; it succeeds only for that winner. Thus even concurrent calls by
the same claim worker have one accepted successor, while every loser remains explicit rejected evidence.

## Required phases and ownership

Use the actual lifecycle, without collapsing work that has distinct authority or evidence. At minimum,
represent intake/route/claim, SDD or planning, implementation and tests, initial critique, every numbered
repair/confirmation, host acceptance, guarded merge, protected-main verification, receipt/projection, and
cleanup when applicable. A critic returns its frozen receipt to the claim holder, which appends the critic's
phase events under the critic's minted identity; the implementer must not attribute critic token usage to
itself. Cross-repository projection phases remain in the same item ledger and bind their repository/revision
in `source`.

The roadmap driver, as supervising parent, owns every post-child seal. A child returns an unposted
terminal draft with `pending final usage` and its exact session/turn identity (or Claude prompt and
`SubagentStop` transcript path). It does not post its own terminal lifecycle event, because the final
usage row does not exist until after that response ends. Once the child is terminal, the driver locates
the completed record, runs the strict collector into a phase-scoped immutable receipt, seals the terminal
event as `measured`, and only then accepts the handoff. A statement that final usage will be written after
the response means pending, never unavailable. Terminal `unavailable` requires a documented
post-completion lookup that found no unique attributable record or a strict collector schema failure.
If a legacy immutable event already used the timing excuse, append a separate
`telemetry-reconciliation-<phase>` recovery phase before host acceptance, cycle completion, or Done.

Codex's completed `token_usage_record` rows provide request, turn, and thread totals; matching
`turn_context` rows provide the exact model variant and effort. Claude Code's status-line input provides
the exact model ID and latest-response input/cache/output usage, while `Stop` and `SubagentStop` hooks
provide the post-response lifecycle point and separate transcript paths. The `pnext-item` lifecycle-ledger
contract carries the equivalent normal-item collection rules. If turn-level usage spans multiple phases,
do not allocate or estimate shares.

## Validation

Run at every handoff and gate named by the skill:

````sh
set -euo pipefail
gh api repos/<owner>/<repo>/issues/<number>/comments --paginate --slurp > <comments.json>
scripts/fsgg-coord telemetry lifecycle export-comments \
  --run <run-id> --unit <unit-id> --comments <comments.json> --output <exported-lifecycle.jsonl>
scripts/fsgg-coord telemetry lifecycle seal-successor \
  --run <run-id> --unit <unit-id> --existing <exported-lifecycle.jsonl> \
  --draft <one-unposted-event-without-chain-fields.json> \
  --usage <every-cited-private-phase-receipt.csv> --output <one-sealed-successor.json>
event="$(<one-sealed-successor.json>)"
test -n "$event"
printf '<!-- fsgg:item-lifecycle/v1 -->\n```json\n%s\n```\n' "$event" > <owned-comment-file>
FSGG_WORKER=<worker> scripts/fsgg-coord comment create <item> <item> <owned-comment-file> --json
scripts/fsgg-coord telemetry lifecycle validate \
  --run <run-id> --unit <unit-id> \
  --log <exported-lifecycle.jsonl> --usage <private-phase-1.csv> --usage <private-phase-2.csv> \
  [--history-report <validated-history.csv>]
````

Use `--require-terminal --require-reconciled` before cycle completion and final roll-up. The host exports
the issue-comment chain, joins it to the private usage report, and checks the command's exit status directly;
never mask it through a pipeline. If a legacy terminal `unavailable` says usage was pending because the
child response had not finished, append a completed measured `telemetry-reconciliation-<original-phase>`
phase whose evidence contains exactly
`supersedes-lifecycle-digest:<64-hex-digest-of-the-original-terminal-event>`. The reconciliation validator
requires that exact target to exist, match the original phase, be unavailable, and be superseded only once.
Prose or a later receipt without the exact digest cannot clear it. Genuine post-completion lookup or strict
collector failures remain unavailable only with the exact failure recorded. Validate implementation changes through the CLI test suite and the
frozen black-box parity corpus.

An extraordinary immutable history that cannot be repaired under its creating or current toolkit requires
an explicit human-authorized `fsgg.telemetry.synthetic-checkpoint/v1` proof. Bind the exact roadmap
item/run/unit and frontier revision/digest, immutable human issue-comment URL, closed reason, literal
`missing_provenance_required:false` and `reconstruct_missing_data:false`, and non-empty all-passed
functional verification. Supply it through `--synthetic-checkpoint` while sealing and validating one
adjacent `synthetic-evidence-checkpoint` phase whose started event immediately extends the frontier and
solely cites the proof digest. Its completion is the new trusted anchor and strict normal lifecycle rules
resume after it. Absent authority, wrong scope/frontier, reuse, ambiguity, tampering, or failing
verification refuses.
