# Automata integration baseline

This is the FQA-0 package qualification for the
[Quint–Automata integration roadmap](../../docs/roadmaps/2026-09-27-090245-quint-automata-integration.md).
It exercises the public **Automata.Core 0.5.0** NuGet artifact. It does not yet replay Quint
traces or qualify Automata's runtime or persistence providers.

Run the package characterization:

```sh
dotnet restore examples/AutomataReplay/AutomataReplay.fsproj --locked-mode
dotnet run --project examples/AutomataReplay/AutomataReplay.fsproj -c Release --no-restore
```

For the CI qualification, run `python3 examples/AutomataReplay/check.py`. It creates an
isolated consumer and package cache, restores the lock file, verifies the package archive's
SHA-256 and source/license metadata against the baseline, and executes the characterization.
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
contract from roadmap §5.1 and §6; executable Quint conformance is the next milestone.

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
