module queue_test {
  import queue.* from "./queue"
  run fifoTest = init.then(enqueueAction(1)).then(enqueueAction(2))
    .then(dequeueAction).expect(state.items == [2] and state.removed == [1])
}
