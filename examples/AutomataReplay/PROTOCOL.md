# Controlled runtime qualification (FQA-6)

The protocol model and harness have separate implementations. `protocol.qnt` defines a
shared-store transition relation over two entities, two workers and three command slots.
Commands move through ready, leased and terminal states; claim tokens are distinct from
entity epochs. Atomic commit updates history, state and outbox together. Delivery attempts,+destination deduplication and acknowledgment are separate facts. There is no network model;
the workers coordinate through the store, so plain Quint is used.

`ProtocolStore.fs` is original test infrastructure implementing Automata's public store
interfaces. `Protocol.fs` runs the actual `Machine.processor` and `Machine.dispatcher`.
Task barriers pause workers immediately before the store's atomic commit. Every schedule
is run twice with fresh state; all wait operations have finite deadlines, and cleanup
cancels and joins outstanding workers before stopping machines.

| Schedule | Control and observations |
|---|---|
| Stale lease | Pause worker 1, expire and reclaim with worker 2, release the old worker first; only the replacement token may finalize |
| Ambiguous commit | Commit all durable facts, lose its response, retry the same finalize; retain one transition and the original result |
| Lost effect acknowledgment | Apply destination effect, lose acknowledgment, redeliver; two attempts, one deduplicated effect, one acknowledgment |
| Pre-commit crash | Cancel a worker blocked before commit, expire its claim, recover with another worker; one later commit |
| Duplicate and ordering | Same key/different payload returns the original command; an unrelated entity progresses while the second command for the first entity waits |
| Retry exhaustion | Three failed attempts dead-letter the head; the next command for that entity can then commit |
| Domain rejection | The real resolver refuses a negative event; no epoch, transition or outbox action is fabricated |

FsQuint compares each complete schedule's observation: submitted command count, both entity
epochs/amounts, transition and pending-action counts, acknowledgments, fences, terminal
command counts, winning tokens, delivery attempts and destination effects. The model's
internal operation prefixes are separately checked for epoch/log agreement, atomic
commit/outbox accounting, unique command commits and effect/attempt bounds. A schedule is
the explicit observation unit; this does not claim a stepwise correspondence for every
internal runtime instruction or general linearizability.

Three seeded defects are detected at the schedule comparison: disabling command fencing,
duplicating an already-finalized command and disabling destination deduplication. They are
test-store/destination mutations, not changes to or discovered bugs in Automata's source.
Conflicting idempotency payloads use a first-submission-wins contract here; FQA-7 must check
actual provider behavior separately.

## Assumptions and limits

The test store supplies an atomic lock-protected finalize and explicit lease expiry. Its
clock is not PostgreSQL's clock. Barriers cannot expose SQL transaction internals. Its
minimal action queue is sufficient for acknowledgment-loss schedules, not a complete
provider implementation or an action-lease-fencing qualification. Background renewal is
configured beyond the five-second schedule deadline; timer scheduling is uncontrolled and
not part of the qualified observations. Independent entity execution order may vary, but
the compared per-entity results commute for these inputs.

Recovery/polling is driven explicitly; no wake-up notification is required, so dropped
notifications do not prevent these bounded polls. Progress depends on continued polling,
available storage and the configured three-attempt policy. This is bounded recovery
evidence, not an unbounded fairness/liveness proof. Hard process crashes, transaction
rollback, storage recovery, retention and provider clocks are FQA-7 obligations.

Coyote decision: do not add it to this initial profile. Public barriers reproduce the
required faults without assembly rewriting; task/SQL scheduling outside those boundaries
remains explicitly unqualified. Revisit only when a named fault requires finer control.
