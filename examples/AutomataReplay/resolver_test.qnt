module resolver_test {
  import resolver.* from "./resolver"
  run case0Test = init.then(check(0)).expect(safety)
  run case1Test = init.then(check(1)).expect(safety)
  run case2Test = init.then(check(2)).expect(safety)
  run case3Test = init.then(check(3)).expect(safety)
  run case4Test = init.then(check(4)).expect(safety)
  run case5Test = init.then(check(5)).expect(safety)
  run case6Test = init.then(check(6)).expect(safety)
  run case7Test = init.then(check(7)).expect(safety)
  run case8Test = init.then(check(8)).expect(safety)
  run case9Test = init.then(check(9)).expect(safety)
  run case10Test = init.then(check(10)).expect(safety)
  run case11Test = init.then(check(11)).expect(safety)
  run case12Test = init.then(check(12)).expect(safety)
}
