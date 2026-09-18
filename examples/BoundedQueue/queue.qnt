// Single sequential queue, capacity two; no concurrency, failures or time.
module queue {
  type State = { items: List[int], inserted: List[int], removed: List[int] }
  var state: State
  pure def canEnqueue(s: State): bool = s.items.length() < 2
  pure def enqueue(s: State, value: int): State =
    { ...s, items: s.items.append(value), inserted: s.inserted.append(value) }
  action init = state' = { items: [], inserted: [], removed: [] }
  action enqueueAction(value: int): bool = all { canEnqueue(state), state' = enqueue(state, value) }
  pure def canDequeue(s: State): bool = s.items.length() > 0
  pure def dequeue(s: State): State =
    { ...s, items: s.items.tail(), removed: s.removed.append(s.items.head()) }
  action dequeueAction = all { canDequeue(state), state' = dequeue(state) }
  val drained = state.removed.length() > 0 and state.items.length() == 0
  action step = { nondet value = Set(1, 2).oneOf(); any { enqueueAction(value), dequeueAction } }
  val filled = state.items.length() == 2
  val fifo = state.inserted == state.removed.concat(state.items)
}
