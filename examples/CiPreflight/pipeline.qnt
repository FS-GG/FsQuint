// One scheduler with shared completion state. Jobs finish atomically; durations,
// runner failures, caches and application test behavior are outside this model.
module pipeline {
  const dependencies: str -> Set[str]
  pure val jobs = Set("build", "shardA", "shardB", "report")
  var completed: Set[str]
  action init = completed' = Set()
  pure def ready(done: Set[str], job: str): bool =
    not(done.contains(job)) and dependencies.get(job).subseteq(done)
  pure def finish(done: Set[str], job: str): Set[str] = done.union(Set(job))
  action complete(job: str): bool = all {
    ready(completed, job), completed' = finish(completed, job)
  }
  action step = { nondet job = jobs.oneOf(); complete(job) }
  val buildReached = completed.contains("build")
  val shardAReached = completed.contains("shardA")
  val shardBReached = completed.contains("shardB")
  val reportReached = completed.contains("report")
  // Independent requirement: keep this unchanged when the scheduling plan changes.
  val dependenciesSatisfied =
    (completed.contains("shardA") implies completed.contains("build")) and
    (completed.contains("shardB") implies completed.contains("build")) and
    (completed.contains("report") implies Set("shardA", "shardB").subseteq(completed))
}
module plans {
  pure val validDependencies = Map(
    "build" -> Set(), "shardA" -> Set("build"), "shardB" -> Set("build"),
    "report" -> Set("shardA", "shardB")
  )
  pure val brokenDependencies = validDependencies.set("report", Set("shardA"))
}
module valid {
  import plans.*
  import pipeline(dependencies = validDependencies).*
}
module broken {
  import plans.*
  import pipeline(dependencies = brokenDependencies).*
}
