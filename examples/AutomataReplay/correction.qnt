// FQA-5 pure planning: bounded business instants, distinct knowledge epoch, no store.
module correction {
  type Event = { op: str, at: int, epoch: int, version: int }
  type Belief = { at: int, amount: int, epoch: int, version: int, command: int }
  type Plan = { outcome: str, errorEpoch: int, amount: int, closed: bool,
    replayed: List[int], versions: List[int], beliefs: List[Belief],
    hypothetical: List[str], committed: List[str], truncated: int, fromAmount: int }
  var result: Plan
  var scenario: int
  pure def empty(outcome: str, epoch: int): Plan = {outcome:outcome,errorEpoch:epoch,
    amount:-1,closed:false,replayed:List(),versions:List(),beliefs:List(),
    hypothetical:List(),committed:List(),truncated:0,fromAmount:99}
  pure def event(op: str, at: int, epoch: int, version: int): Event = {op:op,at:at,epoch:epoch,version:version}
  pure def suffix(c: int): List[Event] =
    if (c == 9 or c == 7) List()
    else if (c == 1) List(event("double",3,2,1), event("credit",3,1,1))
    else if (c == 2 or c == 3) List(event("double",3,1,1), event("check",4,2,1))
    else if (c == 4) List(event("credit",3,1,3))
    else if (c == 8) List(event("credit",3,1,1))
    else List(event("credit",3,1,1), event("double",4,2,1))
  pure def before(a: Event, b: Event): bool = a.at < b.at or (a.at == b.at and a.epoch < b.epoch)
  pure def ordered(events: List[Event]): List[Event] = events.foldl(List(), (sorted,e) =>
    sorted.select(x => before(x,e)).concat(List(e)).concat(sorted.select(x => not(before(x,e)))))
  pure def applyEvent(amount: int, op: str, version: int): int =
    if (op == "credit") amount + (if (version == 2) 10 else 1)
    else if (op == "double") amount*2 else amount
  pure def belief(at: int, amount: int, epoch: int, version: int, command: int): Belief =
    {at:at,amount:amount,epoch:epoch,version:version,command:command}
  pure def appendBelief(bs: List[Belief], b: Belief): List[Belief] =
    if (bs.length() > 0 and bs.nth(bs.length()-1).at == b.at)
      bs.slice(0,bs.length()-1).append(b) else bs.append(b)
  pure def plan(c: int): Plan = {
    val events = ordered(suffix(c))
    if (c == 5) empty("BudgetExceeded",0)
    else if (c == 6) empty("UnknownChartVersion",0)
    else if (c == 7) empty("Diverged",9)
    else {
      val start = if (c == 8) 0 else 10
      val inserted = { ...empty("Planned",0), amount:start, closed:c == 8,
        hypothetical:List(if (c == 8) "close" else "credit"), beliefs:List(belief(2,start,9,2,99)) }
      val folded = events.foldl(inserted, (p,e) =>
        if (p.outcome != "Planned" or p.truncated != 0) p
        else if (e.version == 3) empty("UnknownChartVersion",0)
        else if (p.closed or (e.op == "check" and p.amount >= 20))
          if (c == 3) {...p,truncated:e.epoch} else empty("Diverged",e.epoch)
        else {
          val amount = applyEvent(p.amount,e.op,e.version)
          {...p, amount:amount, replayed:p.replayed.append(e.epoch), versions:p.versions.append(e.version),
            hypothetical:p.hypothetical.append(e.op), beliefs:appendBelief(p.beliefs,belief(e.at,amount,e.epoch,e.version,e.epoch))}
        })
      if (folded.outcome != "Planned") folded else {
        val last = folded.beliefs.nth(folded.beliefs.length()-1)
        {...folded, beliefs:folded.beliefs.replaceAt(folded.beliefs.length()-1,{...last,epoch:9,version:2,command:99})}
      }
    }
  }
  action init = all {scenario' = -1,result' = empty("Initial",0)}
  action check(c: int): bool = all {c >= 0,c <= 9,scenario' = c,result' = plan(c)}
  action step = {nondet c = 0.to(9).oneOf(); check(c)}
  val safety = and {
    result.committed == List(),
    result.outcome != "Planned" implies result.beliefs == List(),
    result.beliefs.indices().forall(i => i == 0 or result.beliefs.nth(i-1).at < result.beliefs.nth(i).at),
    result.outcome == "Planned" implies result.beliefs.nth(result.beliefs.length()-1).epoch == 9,
  }
}
