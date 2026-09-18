module scenario {
  import queue.* from "./queue"
  // Consumer-owned schedule: enqueue(1), enqueue(2), dequeue, dequeue.
  action scheduledStep = any {
    all { state.inserted.length() == 0, enqueueAction(1) },
    all { state.inserted.length() == 1, enqueueAction(2) },
    all { state.inserted.length() == 2, dequeueAction },
  }
}
