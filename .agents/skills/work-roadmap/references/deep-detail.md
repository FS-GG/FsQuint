
# work-roadmap

One command's worth of intent: **"take this roadmap and burn it down, milestone by milestone, and
don't make me drive."** The roadmap is the plan and the ledger both — the parent reads it to find
the next milestone, and the milestone's own worker writes back to it when the work lands. Every
milestone gets a **fresh subagent with fresh context**; when it has shipped and merged, that
subagent **dies**, and the parent spawns the next one against updated `main`. The loop ends when the
roadmap has no unchecked milestone left, and then the parent writes the report and lands it.

This skill is the **loop and its contract**. It inserts one bounded independent critique after the
first green implementation/evidence loop. It does not re-teach the SDD lifecycle — that is
[fs-gg-sdd-lifecycle](../../fs-gg-sdd-lifecycle/SKILL.md)'s, and each subagent runs it. It does not
re-teach PR-to-merge discipline — that is `pnext-item` §5–6's, and each
subagent follows it. It owns the thing neither of those does: **the sequencing of many milestones
across many disposable workers, driven off one markdown file.**

## Where this runs

Classify the tree by which of `fs-gg-sdd-*` (the lifecycle skills) and `fs-gg-feedback-report` are
materialized. There are three states, not two — a tree can satisfy one half without the other
(`.github#2366`), and the two halves get different remedies:

- **Kit source (neither half present)** — e.g. `FS-GG/.github` itself, which is the source of these
  skills, not a scaffold of them. This skill has nothing to drive. Stop and say so rather than
  degrading into a plain edit loop — the whole value here is the SDD-per-milestone discipline.
- **Full product tree (both halves present)** — proceed normally. Feedback checkpoints are
  agent-invoked JSONL events owned by `fs-gg-feedback-report`; they are not Spec Kit hooks and work
  on every lifecycle lane.
- **Partial product materialization (`fs-gg-sdd-*` present, `fs-gg-feedback-report` absent)** — this
  is a genuine, working product tree with an incomplete skill delivery, **not** the wrong tree; the
  registry (`.github`'s `registry/skills.yml`) declares `fs-gg-feedback-report` unconditionally
  materialized (`materializes-when: always`) for every tree that receives this driver, so the gap is
  a delivery defect, not evidence you are somewhere this skill does not belong. Do **not** stop, and
  do **not** fabricate a substitute out-of-workspace tool path (a workaround that then has to be
  remembered and re-applied every cycle, `.github#2366`'s "Recorded cost" table — eight of ten
  independent cycles rediscovered exactly this). Instead: proceed with the loop; when a feedback
  checkpoint step is reached, record it per the feedback contract's zero-event envelope with the
  reason `fs-gg-feedback-report not materialized in this tree (see .github#2366)`; and raise or
  dedupe **one** finding against the tree's own scaffold provenance (not against `work-roadmap`
  itself) so the gap is fixed at its source rather than rediscovered by the next milestone's worker.
  See `feedback-contract.md`'s note for the exact commands this affects.

Preconditions, checked once before the loop:

- The working tree is **clean** and `main` is up to date (`git fetch && git status`).
- The roadmap doc exists (see below) and has at least one unchecked milestone.
- You are NOT going to push to `main` directly — the merge guard blocks it, and every change lands
  as a PR. Agent PR *merges* are allowed; direct pushes to `main` are not.

## The roadmap contract

**Locating the roadmap.** If `$work-roadmap <path>` was given an argument, that is the roadmap. With
no argument, auto-locate in this order and take the first hit: `docs/roadmap.md`, then any
`docs/**/*roadmap*.md`, then `ROADMAP.md` at the repo root. If two or more match and it is not
obvious which is live, **ask** rather than guess.

**What a milestone is.** The **top-level checklist items** are the milestones:

```markdown
## Milestones
- [x] M1 — transport wire contract      # done
- [ ] M2 — inbox widening               # <- the next milestone
  - [ ] narrow the Paths: touch-set     # a SUB-task of M2, not its own milestone
  - [ ] wire the derived-close predicate
- [ ] M3 — derived close
```

- **"Next" is the first unchecked (`- [ ]`) top-level item in document order.** The parent
  re-derives this from the file each round — the file, not memory, is the source of truth, because
  the previous subagent just edited it.
- **Nested checkboxes are that milestone's internal tasks**, not milestones of their own. The
  subagent works them all as part of finishing its one milestone; the parent never splits them out.
- A milestone with no checkbox syntax (a bare `## M2 …` heading with a status line) is fine too —
  treat the first heading whose status is not "done"/"shipped" as next, and record completion the
  same shape the doc already uses. Match the doc's existing convention; do not impose this one.

**Recording progress.** When a milestone lands, its subagent flips `- [ ]` → `- [x]` on that top-level
item and appends a compact progress note on the same line or just beneath it:

```markdown
- [x] M2 — inbox widening — PR #219, merged 2026-07-19; critique: roadmap-x-m2-inbox; feedback: 2 findings filed (#221, #222)
```

Keep it one line and deterministic: **PR number, merge date, one-clause outcome, critique pointer,
feedback pointer.**
The tick is what the parent reads next round; the note is the audit trail the final report rolls up.

## The loop (what the PARENT does)

The parent is the agent that invoked `$work-roadmap`. It does **not** implement milestones itself — it
schedules them. The loop is **strictly sequential**: milestone N+1's subagent does not start until
milestone N's PR is merged, because N+1 must branch from a `main` that already contains N, and because
each subagent deserves a fresh, uncluttered context.

Repeat until the roadmap has no unchecked milestone:

1. **Re-read the roadmap file** and pick the next unchecked top-level milestone. If there is none,
   break to the report step.
2. **`git fetch` and confirm `main` is current** (the previous PR merged into it). Each milestone
   branches from up-to-date `main`.
3. **Spawn a fresh implementation worker** using the host's available worker/subagent mechanism and
   hand it the per-milestone brief below, filled in with this milestone's heading text and the roadmap
   path. One implementation worker, one milestone.
4. **Wait for it to return.** On success it reports the merged PR number and the roadmap edit. The
   subagent is now dead; its context does not carry forward — that is deliberate.
5. **Verify the world matches the report** before trusting it: the PR is merged, `main` moved, and
   the roadmap file on `main` shows the milestone ticked. Do not take the subagent's word for a merge
   you can check — pull and look. (A subagent that reports "merged" on an unmerged PR is the failure
   mode most worth catching.)
6. **Loop.** Fresh subagent, next milestone.

Never run two milestones in parallel — the sequencing above is the point. Never let the parent "just
finish this one quickly" itself; the discipline is that every milestone goes through a subagent that
runs the full SDD lifecycle.

## The per-milestone subagent brief

The parent hands each subagent essentially this, with `<MILESTONE>` and `<ROADMAP>` substituted:

> You are working exactly one roadmap milestone to done, then you exit. Milestone: **`<MILESTONE>`**.
> Roadmap file: **`<ROADMAP>`**. You have the full repo and may not push to `main` (every change is a
> PR; agent PR merges are allowed).
>
> 1. **Branch** from up-to-date `main`: `git fetch && git switch -c <slug-of-milestone> origin/main`.
> 2. **Work the milestone to completion via the SDD lifecycle.** Follow
>    [fs-gg-sdd-lifecycle](../../fs-gg-sdd-lifecycle/SKILL.md) end to end — charter/specify → clarify →
>    plan → tasks → implement → verify/validate → **fs-gg-sdd-ship**. The milestone's nested
>    checkboxes are your task list; all of them are in scope. Do not stop at "specified" or
>    "planned" — the milestone is done when it is shipped and green.
> 3. **Checkpoint development feedback.** Use `fs-gg-feedback-report` checkpoint mode with one
>    stable cycle id. Capture only material observations at four bounded transitions:
>    scaffold/onboarding + first build; lifecycle authoring before implementation; first
>    implementation/test/evidence loop; and verify/ship/PR orchestration. Also checkpoint any
>    misleading guidance, avoidable retry, workaround, capability gap, or unexpectedly effective
>    composition when it occurs. Commit `feedback/checkpoints/<cycle-id>.jsonl`.
> 4. **Run the independent critique gate.** After the first green implementation/test/evidence loop,
>    start one fresh critic and follow
>    `.agents/skills/work-roadmap/references/critique-contract.md`. Repair blocker/major findings,
>    route minor follow-ups, obtain confirmation from the same critic, and validate the committed
>    `reviews/roadmap/<cycle-id>.json` artifact. Permit at most ten repair/confirmation rounds; a
>    failed tenth round records human escalation and stops without roadmap completion, merge, or an
>    eleventh round. The critic never edits implementation.
> 5. **Update the roadmap.** In `<ROADMAP>`, flip this milestone's top-level `- [ ]` → `- [x]` and
>    append the one-line progress note (PR number filled in at step 7, merge date, one-clause
>    outcome, critique pointer, feedback pointer). Commit it on your branch as part of the milestone.
> 6. **Finalize and validate `fs-gg-feedback-report`.** Synthesize the checkpoints and repository
>    evidence into one schema-v2 cycle report. Search prior reports and open/closed issues before
>    filing; add recurrence evidence to an existing issue instead of duplicating it. Run the bundled
>    report validator and fix every error. File new actionable findings at their root owner and put
>    issue numbers in the roadmap progress note.
> 7. **Open a PR and merge on green** — the `pnext-item` §5–6 way:
>    open the PR, cite the independent critique artifact, satisfy required PR review and checks, and
>    merge once green. A problem you find on the way you FIX in this PR when that keeps it reviewable,
>    or file at its root cause when it does not belong here. Backfill the PR number into the roadmap
>    note before you merge (or in an immediate follow-up commit if the number only exists after open).
> 8. **Report back** to the parent: the milestone, the merged PR number, the roadmap line you wrote,
>    critique artifact, and any findings you filed. Then you are done — exit.
>
> If the milestone genuinely cannot land from here — it needs a human decision, or it is blocked on
> another repo — do NOT tick it and do NOT fake a merge. Report the blocker to the parent and stop.

## Termination and the final report

When step 1 of the loop finds no unchecked milestone, the parent — **itself, not a subagent** — writes
a detailed report and lands it:

1. **Write** `docs/reports/<YYYY-MM-DD>-<roadmap-slug>-completion.md`, timestamped for today. It
   should be a real report, not a changelog line — cover, per milestone: what shipped, the merged PR,
   the merge timestamp, what the SDD lifecycle produced (spec/plan/evidence pointers), the feedback
   findings and where they were filed, the critique artifact and resolved/outstanding critique
   counts, and any deviations from the original roadmap. Close with a
   roll-up: total milestones, total PRs, open follow-ups, recurring root causes by owner, aggregate
   avoidable cost, positive patterns worth promoting, development-surface coverage gaps, and
   anything a human should look at. Aggregate the schema-v2 reports; do not concatenate them.
   Follow the house report style already in `docs/reports/`.
2. **Land it as its own PR** — feature branch, open, review, merge on green — the same discipline every
   milestone used. The report is the last thing to merge.
3. Report completion to the operator: roadmap fully checked, report PR number, follow-ups outstanding.

## Failure handling

- **A milestone that will not land** stops the loop. The parent surfaces the subagent's blocker and
  does not advance — skipping a milestone silently would corrupt the "roadmap is the ledger" contract
  and strand everything sequenced behind it. A human decides whether to unblock, re-scope, or reorder.
- **A subagent that reports a merge that did not happen** is caught by loop step 5 (the parent
  verifies against `main`, not against the report). Treat a mismatch as a failed milestone, not a
  passed one.
- **Never bypass the merge guard.** No direct push to `main`, no local `git merge` into `main` — the
  guard exists because an agent once merged its own PRs unasked. Everything here is still a PR; the
  allowance is only that the agent may *merge* a green, reviewed one.

## See also

- **ADR-0053** — the org record that canonizes this loop, its cross-repo forces, and the roads not
  taken. This file is the protocol; that record is the *why*.
- [fs-gg-sdd-lifecycle](../../fs-gg-sdd-lifecycle/SKILL.md) — the SDD workflow each subagent runs; the
  authority on stage order, not this file.
- `pnext-item` — the open-PR → review → merge-on-green loop each subagent
  reuses, and the "fix the cause, then take it" discipline for problems found mid-milestone.
- `fs-gg-feedback-report` — the lifecycle-independent checkpoint and schema-v2 synthesis skill the
  driver invokes during each milestone and validates before handoff.
- ADR-0018 (transient/durable SDD artifact taxonomy) — what the lifecycle leaves behind per milestone.
