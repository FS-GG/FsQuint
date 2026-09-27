# Native Automata provider qualification

This is an opt-in, non-packable qualification consumer. It complements the independent
Quint models and controlled runtime schedules in [AutomataReplay](../AutomataReplay/README.md).
It does not add a database dependency to FsQuint, and it does not claim that passing an
upstream implementation's own tests is an independent formal proof.

## Reproduce

Linux prerequisites: the repository's .NET SDK, Git, a C compiler, GNU Make,
Bison, Flex, Perl and pkg-config. Run as a non-root user. The destination must not exist:

```sh
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- providers-provision /tmp/fsquint-providers
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- \
  providers-check /tmp/fsquint-providers/automata /tmp/fsquint-providers/tools/bin
```

Provisioning builds PostgreSQL 19beta3, btree_gist, PGMQ 1.10.0 and pg_cron 1.6.8 from
immutable commits recorded in [Providers.fs](../../eng/Qualification/Providers.fs). PostgreSQL 18 is insufficient for Automata's
`UPDATE FOR PORTION OF` statements. This is a pinned beta qualification, not a recommendation
to deploy a beta database. A stable database or dependency upgrade requires fresh qualification.
The runner verifies the unmodified Automata source commit and the NuGet providers' repository
commit metadata; `packages.lock.json` binds their complete dependency content hashes.

The runner owns a fresh PostgreSQL cluster with a private Unix socket and no TCP listener.
Its application role has schema/queue rights; a separate administrator connection exercises
cron installation and scheduling. No connection supplied through the caller's environment is
used. SQLite uses temporary files. Tests and workers have process-tree deadlines; cleanup
kills owned workers, stops the owned server and removes temporary resources on failure.
There are no changes to a shared database or installed system service.

## Qualified boundaries

| Capability | SQLite 0.5.0 | PostgreSQL 0.5.0 |
|---|---|---|
| Command FIFO, deduplication, leases and fencing | Upstream contract suite | Upstream contract suite |
| Atomic state/log/outbox finalization, repeat commit, rollback | Upstream contract suite | Upstream contract suite |
| Cross-process contention | Two independent upstream worker processes | Database sessions/transactions in upstream suite |
| Worker death after one commit and a second claim | Original public-API consumer, SIGKILL and fresh process | Same consumer plus immediate database shutdown/restart |
| Recovery state, fence and outbox | State 2, epoch 2, exactly two actions | State 2, epoch 2, exactly two actions |
| Temporal correction and old beliefs | Unsupported; suite requires explicit dead-letter and entity release | Replay/correction suites; corrected current/history/transition/command rows survive immediate restart |
| Maintenance and notifications | No provider capability claimed | Retention, lease reaping, listeners, index plans and cron permission/scheduling suites |

`Program.fs` uses the public NuGet stores directly to finalize simple integer transitions.
Its first process commits transition 1, claims command 2, and announces the durable boundary.
The parent kills it without disposal. A fresh process waits for real-clock lease expiry,
requires a larger fence token for the same command, commits transition 2 and checks the
snapshot and outbox. Runtime processor behavior is separately exercised by the upstream
provider suites and FQA-6; this small harness isolates the storage recovery boundary.

For PostgreSQL correction recovery, the runner executes one named upstream replay witness
again. That witness checks the corrected state and pre-correction belief through public APIs.
The runner requires nonempty current beliefs, historical beliefs, transitions and correction
rows, immediately restarts PostgreSQL, and compares every captured row exactly. Captured row
JSON and its digest accompany the report. This is one specific persistence witness, not a
general historical trace validator.

The runner refuses missing, ignored or failing tests and checks the expected pinned test
counts. SQLite's unsupported temporal capability is explicitly tested as unsupported; it is
not counted as equivalent to PostgreSQL. SQL schema-negative tests intentionally generate
database errors, so a server log containing errors is not itself evidence of suite failure.

## Evidence and budgets

Each run writes `artifacts/automata-providers/report.json`, raw suite/server logs and corrected
history rows. The committed `evidence.json` is one historical local observation; CI publishes
fresh artifacts through [automata-providers.yml](../../.github/workflows/automata-providers.yml).
The job runs nightly and for provider example/workflow/toolchain changes, and can be dispatched for release
qualification. The ordinary `qualify` job continues to run the independent models and consumer.

Budgets are five minutes per upstream suite, thirty seconds for each crash boundary/recovery,
and twenty minutes for the complete CI job including a clean native compiler build. Recorded
elapsed times include `dotnet run` build overhead; they are qualification costs, not application
throughput benchmarks. A timeout fails qualification. No latency or throughput SLO is claimed.

This does not model disk corruption, power loss, replicas, failover, arbitrary concurrent
histories, every crash instruction, or production delivery destinations. At-least-once effects
still require destination idempotency. The server restart checks acknowledged durable writes;
they do not establish a global exactly-once guarantee or unbounded liveness.

## Sources and licensing

The [pinned Automata source](https://github.com/byzantine-systems/automata/tree/03c3282f25888a32d36e53fa708f0342c328ccfc)
and its tests remain in a separately fetched checkout under upstream's LGPL-3.0-or-later license.
This repository includes original harness code, package references and observations, not a copy
of upstream implementation/test sources. Redistributors must retain the dependency's notices
and comply with its license independently of this repository's MIT license.

Native prerequisites are pinned to [PostgreSQL](https://github.com/postgres/postgres/tree/3638289fb57bdabec00deda98ee9624a35f5d66a),
[PGMQ](https://github.com/pgmq/pgmq/tree/51d7655a097d91bf05ad5ae40dd604aa067ed355),
and [pg_cron](https://github.com/citusdata/pg_cron/tree/5cedfa472ccc83567aa23ec645925ed8489a7797).
Their own licenses apply to the separately built tools.
