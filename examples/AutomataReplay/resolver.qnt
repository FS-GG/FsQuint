// Bounded resolver contract: fixed tree r -> p -> {a,b,t}, r -> c.
// Pure relation over ordered rules and ancestor paths, independent of Automata source.
module resolver {
  type Rule = { owner: str, event: str, guard: bool, reject: bool, mode: str, target: str, leaf: str, label: str }
  type Effect = { kind: str, node: str, data: int }
  type Result = { outcome: str, leaf: str, handler: str, exited: List[str], entered: List[str], actions: List[Effect] }
  var result: Result
  var scenario: int
  pure val paths = Map("r" -> List("r"), "p" -> List("r","p"),
    "a" -> List("r","p","a"), "b" -> List("r","p","b"),
    "t" -> List("r","p","t"), "c" -> List("r","c"))
  pure def reverse(xs: List[str]): List[str] = xs.foldl(List(), (acc,x) => List(x).concat(acc))
  pure def fx(k: str, n: str, d: int): Effect = { kind:k, node:n, data:d }
  pure def rule(owner: str, mode: str, target: str, leaf: str, label: str): Rule =
    { owner:owner, event:"go", guard:true, reject:false, mode:mode, target:target, leaf:leaf, label:label }
  pure def empty(outcome: str, leaf: str): Result =
    { outcome:outcome, leaf:leaf, handler:"", exited:List(), entered:List(), actions:List() }
  pure def source(c: int): str = if (c == 6 or c == 7) "c" else if (c == 8) "b" else if (c == 10) "t" else "a"
  pure def rules(c: int): List[Rule] = {
    val first = rule("a","next","b","b","first")
    val parent = rule("p","next","a","a","parent")
    if (c == 0) List(first, rule("a","next","c","c","second"), parent)
    else if (c == 1) List({...first, event:"other", guard:false}, parent)
    else if (c == 2) List({...first, reject:true}, parent)
    else if (c == 3) List(rule("a","internal","a","a","internal"))
    else if (c == 4) List(rule("a","next","a","a","self"))
    else if (c == 5 or c == 10) List(parent)
    else if (c == 6) List(rule("c","goto","p","a","compound"))
    else if (c == 7) List(rule("c","goto","p","b","mismatch"))
    else if (c == 8) List(rule("b","goto","p","a","ancestor"))
    else if (c == 9) List(first)
    else List()
  }
  pure def resolve(c: int): Result = {
    val from = source(c)
    val fromPath = paths.get(from)
    // Lexicographic priority: deepest owner first, then declaration position.
    val candidates = reverse(fromPath).foldl(List(), (acc,node) =>
      acc.concat(rules(c).select(r => r.owner == node and (not(r.guard) or r.event == "go"))))
    if (c == 11) empty("InvalidTerminal",from)
    else if (candidates.length() == 0) empty("Unhandled",from)
    else {
      val r = candidates.head()
      val leaf = if (r.mode == "internal") from else r.leaf
      val base = empty("Handled",leaf)
      if (not(r.guard)) empty("GuardFailed",from)
      else if (r.reject) empty("Rejected",from)
      else if (r.mode == "goto" and r.target == "p" and leaf != "a") empty("TargetMismatch",from)
      else if (r.mode == "internal") { ...base, handler:r.owner, actions:List(fx("rule",r.label,1)) }
      else {
        val toPath = paths.get(leaf)
        val common = fromPath.indices().filter(i => i < toPath.length() and fromPath.nth(i) == toPath.nth(i)).size()
        val boundary = if (r.mode == "goto" and fromPath.select(n => n == r.target).length() > 0) paths.get(r.target).length()-1
          else if (leaf == from) paths.get(r.owner).length()-1 else common
        val exits = reverse(fromPath.slice(boundary,fromPath.length()))
        val entries = toPath.slice(boundary,toPath.length())
        val effects = exits.foldl(List(), (acc,n) => acc.append(fx("exit",n,0)))
          .concat(List(fx("rule",r.label,1)))
          .concat(entries.foldl(List(), (acc,n) => acc.append(fx("entry",n,1))))
        { ...base, handler:r.owner, exited:exits, entered:entries, actions:effects }
      }
    }
  }
  action init = all { scenario' = -1, result' = empty("Initial","") }
  action check(c: int): bool = all { c >= 0, c <= 12, scenario' = c, result' = resolve(c) }
  action step = { nondet c = 0.to(12).oneOf(); check(c) }
  val safety = and {
    result.outcome != "Handled" implies result.actions.length() == 0,
    result.actions.select(e => e.kind == "exit").foldl(true, (ok,e) => ok and e.data == 0),
    result.actions.select(e => e.kind == "entry").foldl(true, (ok,e) => ok and e.data == 1),
    scenario == 3 implies result.exited == List() and result.entered == List() and result.leaf == "a",
    scenario == 0 implies result.handler == "a" and result.leaf == "b",
    scenario == 1 implies result.outcome == "GuardFailed",
    scenario == 2 implies result.outcome == "Rejected",
    scenario == 8 implies result.exited == List("b","p") and result.entered == List("p","a"),
  }
}
