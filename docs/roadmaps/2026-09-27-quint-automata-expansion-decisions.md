# Optional expansion decisions (FQA-8)

Decision date: 2026-09-27. This closes the optional research gate in the
[Quint–Automata integration roadmap](2026-09-27-090245-quint-automata-integration.md).
The user authorized all implementation decisions. The delivered examples establish
independent conformance and provider qualification; they do not create a request to
maintain every possible exporter, interpreter or validator.

No optional spike is selected for implementation in this roadmap. There is no named
consumer requesting these additional interfaces and no accepted maintenance owner for
another public tool. Existing FsQuint maintainers own the committed example harnesses;
this does not assign work to Quint or Automata maintainers. No spike tests ran, and no
export, exploration, code-generation or historical-validation capability is advertised.

| Candidate | Decision and evidence | Gate to reopen |
|---|---|---|
| Structural chart export | Defer. FQA-0 confirms structural fingerprints omit callback behavior. Exporting a diagram cannot establish the guard, action-order or resolver properties checked by FQA-1/FQA-2. The present consumer already has authored models and original F# charts. | A named diagram/documentation consumer and owner; a versioned structural schema; closure fields explicitly opaque; independent checks for hierarchy, rule order and initial-child paths. |
| Finite executable exploration | Defer. FQA-4 already generates genuine Quint traces with bound inputs and eight fixed seeds. Exploring F# callbacks would require separate finite-domain, equality and termination contracts and would weaken independence if substituted for the model. | A consumer with explicitly finite state/event domains; owner for the explorer; visited-state and transition budgets; explicit incomplete outcome for truncation, exceptions and nontermination. |
| Shared chart IR/code generation | Defer. Approval, turnstile and queue reuse only an input-only replay helper. Three local examples do not establish demand for a new language or compiler; sharing generated logic would introduce common-error risk. | Two independently maintained consumers with shared representable semantics; owner for schema/compiler compatibility; independent hand-written reference checks; unsupported callbacks refused explicitly. |
| Historical trace validation | Defer. FQA-7 proves one correction witness persists across a PostgreSQL restart. SQLite intentionally lacks temporal correction. Neither observation supplies a complete input/decision history, global event order or mapping for arbitrary production histories. | A concrete recorded-history consumer and data contract covering inputs, versions, epochs, refusals, corrections and effect identities; privacy/access plan; owner for a three-outcome validator: valid, invalid, inconclusive. |

A future selected spike must pass one independently checked positive case and detect a
seeded semantic defect. It must also demonstrate an explicit incompleteness result,
not merely document one. Examples of required incompleteness cases are an opaque guard
for export, a deliberately exhausted exploration budget, an unsupported callback for
an IR, and a missing command/version interval for history validation. Those are future
acceptance criteria, not tests claimed as delivered here.

## Decisions retained from the implemented stages

Keep independent Quint application/resolver/correction/protocol models and FsQuint ITF
replay as the primary integration. Keep scenario instrumentation and sidecar manifests
in the consumer. Keep the shared pure replay helper as example source; it has no Automata
dependency and delegates cancellation, deadlines and cleanup to FsQuint's existing engine.
Do not publish a new adapter package or expand the core/tooling API without an external
consumer's compatibility requirements.

Use Automata's public store interfaces for controlled scheduling. FQA-6 expressed the
required races with task barriers, so no Coyote dependency or upstream test seam was
needed. Retain separate real-provider qualification: a passing deterministic test-store
schedule does not establish SQL behavior, and passing SQL contracts does not prove the
independent Quint model complete.

Qualify only the pinned versions. A callback-only change can alter behavior without
changing a structural chart fingerprint; corpus replay, mutation controls and package
identity checks therefore remain necessary. Treat future upstream semantic changes as
profile migrations rather than silently regenerating expected observations.

The implemented scope is complete once its delivery PRs are merged. The four deferred
options are explicit stop decisions under the roadmap's consumer/ownership gate, not
unfinished required implementation or proven capabilities. Reopening one creates a new
bounded work item with its own consumer, owner and acceptance evidence.
