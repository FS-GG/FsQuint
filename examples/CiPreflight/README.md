# Check a CI plan before running expensive tests

A missing job dependency can become visible only after a long test suite finishes.
This example runs a small Quint model first. A counterexample blocks the expensive
command and shows the job order that violates the requirement.

The pipeline builds once, runs two test shards, then collects their reports. The
requirement is independent of the plan: both shards need the build, and the report
needs both shards. The broken plan omits the report's dependency on shard B.
Quint finds this schedule:

```text
build → shardA → report
                ^ shardB has not completed
```

`pipeline.qnt` contains the scheduler, the valid and broken dependency plans, and
that invariant. `Program.fs` uses the public **FsQuint.Tooling 0.1.0** package to run
100 sampled schedules of at most four transitions with seed 42. Only a successful
sample run and a named completion scenario return exit code 0. A counterexample,
missing tool, timeout or incomplete result returns nonzero. The program starts no
workload itself: shell `&&` makes successful preflight a prerequisite for the next
command.

## Run the gate

From repository root, on Linux x64 with .NET SDK 10.0.401:

```bash
bash eng/provision-quint.sh /tmp/fsquint-tools
export QUINT_BIN=/tmp/fsquint-tools/quint
export QUINT_HOME=/tmp/fsquint-tools/home
dotnet build examples/CiPreflight/CiPreflight.fsproj -c Release

# The valid plan permits the following command.
dotnet examples/CiPreflight/bin/Release/net10.0/CiPreflight.dll valid && echo 'Expensive tests would start here'

# The broken plan exits 1; the following command never runs.
dotnet examples/CiPreflight/bin/Release/net10.0/CiPreflight.dll broken && echo 'Expensive tests would start here'
```

Replace the `echo` with the real test command after adapting the model to that
pipeline. Restore, compilation and first-time tool provisioning are setup costs;
the preflight prints its own elapsed time. Timings depend on the machine and model.
No long-running workload or artificial sleep is needed to demonstrate rejection.

`bash examples/CiPreflight/check.sh` copies the example outside the repository,
restores the public package and checks that the valid plan reaches a workload
marker, while the broken plan and a missing tool never do.

## Scope and maintenance

The model represents a single scheduler with shared completion state. Completion
is atomic; elapsed job time is irrelevant to the dependency invariant. Four jobs
complete at most once, and successful termination is expected after four steps.
Both shard orders are possible. The completion scenario and model witnesses guard
against a vacuous check where no job can run; the separate `broken_test` scenario
reproduces the missing dependency without relying on random schedule selection.

This is an illustrative plan, not an automatic import or validation of GitHub
Actions YAML. Keep a production model's dependencies synchronized with the actual
workflow, preferably by generating both from one reviewed plan; keep the intended
requirements independently stated so the same omission cannot weaken the check.
Update and check the model before changing the corresponding workflow.

A passing sampled run is not exhaustive verification and does not predict failures
inside a test, runner outage, cache corruption or package restore. The value is
catching modeled scheduling mistakes before paying for the real run. Application
correctness still requires the real tests. No measured time or cost saving is
claimed for this illustrative example.

The [bounded queue example](../BoundedQueue) separately demonstrates FsQuint trace
replay against a real F# implementation.
