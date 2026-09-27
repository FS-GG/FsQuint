module protocol_test {
  import protocol.* from "./protocol"
  run prefixesTest = init.expect(allSchedulePrefixesSafe)
  run case0Test = init.then(check(0)).expect(safety)
  run case1Test = init.then(check(1)).expect(safety)
  run case2Test = init.then(check(2)).expect(safety)
  run case3Test = init.then(check(3)).expect(safety)
  run case4Test = init.then(check(4)).expect(safety)
  run case5Test = init.then(check(5)).expect(safety)
  run case6Test = init.then(check(6)).expect(safety)
}
