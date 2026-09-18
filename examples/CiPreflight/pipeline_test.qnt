module pipeline_test {
  import plans.* from "./pipeline"
  import pipeline(dependencies = validDependencies).* from "./pipeline"
  run completePipelineTest = init.then(complete("build")).then(complete("shardA"))
    .then(complete("shardB")).then(complete("report"))
    .expect(reportReached and dependenciesSatisfied)
}
module broken_test {
  import plans.* from "./pipeline"
  import pipeline(dependencies = brokenDependencies).* from "./pipeline"
  // Deterministic regression witness, independent of random scheduling coverage.
  run missingDependencyTest = init.then(complete("build")).then(complete("shardA"))
    .then(complete("report")).expect(not(dependenciesSatisfied))
}
