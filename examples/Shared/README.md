# Input-only replay helper decision (FQA-3)

`PureReplay.fs` is linked into the approval, turnstile and bounded queue examples. It has
no domain switches, Automata reference or new library dependency. It validates complete
index/operation bindings before constructing a runtime. Constructor, reducer, observer
and cleanup callbacks see only their own data; expected states remain with `Replay.run`.

The same positive and negative controls run for each consumer. The turnstile also checks
that an operation-identity mismatch cannot initialize the implementation. Existing core
tests retain authority for replay deadlines, cancellation and cleanup failure behavior.
The helper delegates these mechanics instead of implementing a second replay engine.

Public-surface decision: retain this as example source, not a new package or a core API.
Three local consumers establish useful code reuse, but there is no external consumer
request establishing compatibility and maintenance needs. The current helper assumes
small synchronous reducers and a five-second cooperative deadline. A durable-runtime
adapter requires different asynchronous scheduling/observation boundaries and must not
silently acquire this shape. FsQuint maintainers own the example helper; reconsider a
public surface when actual consumer needs justify its versioning obligation.

For a standalone copied consumer, copy `PureReplay.fs` beside the project. The repository
projects otherwise link this canonical source. `eng/check.sh` verifies isolated package
consumers for both the Automata examples and the queue.
