module values {
  type Choice = Empty | Full(int)
  var value: { mapping: int -> str, pair: (int, bool), members: Set[int], huge: int, choice: Choice }
  action init = value' = {
    mapping: Map(1 -> "one", 2 -> "two"),
    pair: (1, true),
    members: Set(1, 2),
    huge: 999999999999999999999999999999999999,
    choice: Full(7),
  }
  action step = value' = value
}
