# Automata integration baseline

This contains the FQA-0 package qualification and FQA-1 offline conformance example for the
[Quint–Automata integration roadmap](../../docs/roadmaps/2026-09-27-090245-quint-automata-integration.md).
It exercises the public **Automata.Core 0.5.0** NuGet artifact. It replays independently generated Quint traces against an approval chart. It does not qualify
Automata's runtime or persistence providers.

Run the package characterization:

```sh
dotnet pack src/FsQuint/FsQuint.fsproj -c Release -o artifacts/packages
python3 examples/AutomataReplay/check.py
```

For the CI qualification, run `python3 examples/AutomataReplay/check.py`. It creates an
isolated consumer and package cache, restores the lock file, verifies the package archive's
SHA-256 and source/license metadata against the baseline, and executes the characterization.
The candidate FsQuint package includes its source revision: only its content hash is refreshed
in the temporary lock, and its archive must exactly equal the locally built package. All
third-party lock entries remain unchanged; a subsequent locked restore must pass.
`eng/check.sh` includes this check. Temporary files are removed even when qualification fails.

The first restore needs NuGet access, or an existing package cache. Running the executable
needs neither Quint nor a database. The package is an example-only dependency; FsQuint's
core and tooling packages acquire no Automata dependency. This project is not packable.

[baseline.json](baseline.json) pins the upstream archive, source commit and intended tool
profile. [packages.lock.json](packages.lock.json) pins restored package contents. The NuGet
archive's repository metadata names the same source commit that the design inspected.
The characterization checks ordered actions, handler and hierarchy paths, internal
transitions, unhandled input, guard precedence and a closure change invisible to the
structural fingerprint. These checks bind our understanding to the published API; they
are not an independent formal model of that API.

## Approval profile for FQA-1

Profile identity: `fsquint.automata-approval/1`. This section fixes the first example's
contract from roadmap §5.1 and §6, implemented by `approval.qnt` and `Approval.fs`.

One document is addressed by the fixed identity `document-1`. Two distinct callers are
`author` and `reviewer`. Inputs are synchronous, one at a time. There are no messages,
clocks, random callbacks or concurrent effects in this profile. Plain Quint is sufficient;
Choreo, database leases and runtime scheduling belong to later, separate models.

The model state is one record with these fields:

| Field | Type / values | Purpose |
|---|---|---|
| `phase` | Draft, Pending, Approved, Published, Cancelled | Domain lifecycle; Pending and Approved are children of review |
| `approvedBy` | None, Reviewer | Evidence of authorization, independent from the phase |
| `outcome` | Initial, Applied, NotAuthorized, Unhandled, Terminated | Latest observable outcome, including state-preserving refusal |
| `actions` | Ordered list of tagged audit/notification records | Latest intended effects, cleared before every input |

The independent input channel carries an operation and actor: submit(author),
approve(author/reviewer), publish(author), cancel(author), remind(author). Other
operation/actor combinations fail input decoding before initialization. An input is
not inferred from expected states. A complete, immutable manifest pairs each typed input
with its trace index, source binding and raw-trace digest.

- Draft + submit enters Pending and emits submission audit then review notification.
- Pending + approve(author) refuses with NotAuthorized, unchanged phase/approval and no actions.
- Pending + approve(reviewer) enters Approved, records Reviewer and emits approval audit.
- Approved + publish enters Published and emits publication audit then publication notification.
- Pending/Approved + remind emits a reminder without exit/entry or domain state change.
- Draft/Pending/Approved + cancel enters Cancelled and emits cancellation audit.
- Published/Cancelled + any decoded input produces Terminated with no domain change/effects.
- Other decoded phase/input combinations produce Unhandled with no domain change/effects.

The terminal refusal is an **application boundary** around the pure resolver, not a claim
that `Chart.resolve` itself checks runtime lifecycle. The wrapper checks its actual chart
state, never expected model state. Initialization constructs Draft/None/Initial/empty actions;
it does not synthesize entry effects or copy the expected observation.

The high-priority properties are authorization before approval, approval before publication,
terminal-state preservation, and absence of success effects on refusal. An initial witness
submits, refuses self-approval, reminds, approves as reviewer and publishes. Separate witnesses
cover cancellation, unhandled events and inputs after termination. FQA-1 must catch independent
guard, action-order, wrong-target and projection mutations at declared steps.

The domain observation includes all four fields. The resolver profile additionally observes
actual handler and ordered exit/entry paths; it is a separate FQA-2 suite. Tagged records have
fixed fields and reject unknown cases. Integers are range-checked; strings and lists retain
their exact identity/order. This first profile needs no maps or tuple coercion into schema 1.
Expected observations are held by the replay engine, outside the input-only reducer callbacks.

## Remaining boundaries

Automata's guard wrapper evaluates its guard before the wrapped event predicate. FQA-1
must place domain authorization inside an event-matching rule so an unrelated input is not
accidentally rejected as unauthorized. FQA-2 will separately characterize the wrapper itself.
Whether Automata maintainers adopt that behavior as a permanent contract remains their decision.

The input/output spelling above is our consumer-owned profile. No upstream acceptance,
stable rule identifiers, provider capability or cross-provider equivalence is implied.
Changing this contract requires a profile version and new evidence, not rewritten fixtures.

## Dependency notices

Automata.Core is authored by ByzantineSystems and declares **LGPL-3.0-or-later** in its
[NuGet metadata and upstream source](https://github.com/byzantine-systems/automata/tree/03c3282f25888a32d36e53fa708f0342c328ccfc).
Its source and license texts remain available in that repository. This example uses public
APIs and references the upstream NuGet package; no Automata implementation source or binaries
are vendored into FsQuint. FsQuint's packages remain independent of this dependency. Any later
redistribution of a combined executable or adapter package must separately retain applicable
notices and satisfy its distribution obligations. No adapter package is distributed here.

## FQA-1 evidence and limits

`approval.qnt` models the contract independently as a record transition function. String
identities are a deliberate wire-format choice: a finite input set, domain invariant and
strict adapter decoder close their domains. `approval_test.qnt` supplies four explicit
witnesses; their raw Quint 0.32.0 ITF output is retained in `fixtures/` alongside manifests
binding trace/model/scenario digests, ordered inputs and source lines.

`Conformance.fs` validates all manifests and observations before initializing the chart.
The domain implementation receives only a typed input and its own previous observation.
The replay engine compares the initial observation and every step, including refusals.
The instrumented Quint input channel is checked against the manifest, independently of
expected domain states. Effect records have exactly `kind` and `name`, preserving order.

The positive corpus covers publication, self-approval refusal, both review reminders,
unhandled publication, cancellation in all three active phases and post-terminal inputs.
The guard defect diverges at step 2; swapped submission effects, wrong target and projection
defects diverge at step 1. Unknown inputs, noncontiguous bindings and malformed manifests
are rejected before initialization. Existing FsQuint tests separately qualify malformed ITF.

`check.py` runs offline replay from an isolated package consumer. With `QUINT_BIN` set,
it additionally checks the pinned executable digest, typechecks the model, executes four
witnesses and samples both safety properties (1,000 traces × 30 steps, seed 42). These
sampled results are not exhaustive verification or a runtime/provider correctness claim.
Fixed fixture replay itself requires neither Quint nor a database. Regeneration and sampled
input coverage are FQA-4; resolver semantics are FQA-2.
