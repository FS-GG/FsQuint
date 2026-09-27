// FQA-6: bounded shared-store protocol. Two entities/workers; three command slots.
// Claims and atomic finalize are separate operations. A response may be lost after finalize.
module protocol {
  type Cmd = {entity:int, payload:int, status:str, token:int, failures:int}
  type World = {commands:List[Cmd],epochs:List[int],amounts:List[int],history:List[int],
    outbox:List[int],effects:List[int],acked:int,attempts:int,fenced:int,dead:int,rejected:int,serial:int}
  type Kind = Submit | Claim | Expire | Commit | Fail | Reject | Deliver | Ack
  type Op = {kind:Kind,id:int,entity:int,value:int}
  type Observation = {submitted:int,epochs:List[int],amounts:List[int],commits:int,queued:int,
    acknowledged:int,fenced:int,dead:int,rejected:int,winners:List[int],deliveryAttempts:int,effects:int}
  var result: Observation
  var scenario:int
  pure val initial: World = {commands:List(),epochs:List(0,0),amounts:List(0,0),history:List(),outbox:List(),
    effects:List(),acked:0,attempts:0,fenced:0,dead:0,rejected:0,serial:0}
  pure def op(kind:Kind,id:int,entity:int,value:int): Op = {kind:kind,id:id,entity:entity,value:value}
  pure def finalized(c:Cmd): bool = Set("done","dead","rejected").contains(c.status)
  pure def applyOp(w:World,o:Op): World = {
    if (o.kind == Submit)
      if (o.id < w.commands.length()) w
      else {...w,commands:w.commands.append({entity:o.entity,payload:o.value,status:"ready",token:0,failures:0})}
    else if (o.kind == Deliver) {
      val seen = w.effects.select(id => id == o.id).length() > 0
      {...w,attempts:w.attempts+1,effects:if (seen) w.effects else w.effects.append(o.id)}
    }
    else if (o.kind == Ack) {...w,acked:w.acked+1,outbox:w.outbox.select(id => id != o.id)}
    else {
      val c=w.commands.nth(o.id)
      if (o.kind == Claim) {
        val eligible = c.status == "ready" and w.commands.indices().forall(i =>
          i >= o.id or w.commands.nth(i).entity != c.entity or finalized(w.commands.nth(i)))
        if (eligible) {...w,serial:w.serial+1,commands:w.commands.replaceAt(o.id,{...c,status:"leased",token:w.serial+1})} else w
      }
      else if (o.kind == Expire) {...w,commands:w.commands.replaceAt(o.id,{...c,status:"ready"})}
      else if (o.kind == Commit)
        if (c.status == "done" and c.token == o.value) w
        else if (c.status != "leased" or c.token != o.value) {...w,fenced:w.fenced+1}
        else {...w,commands:w.commands.replaceAt(o.id,{...c,status:"done"}),history:w.history.append(o.id),outbox:w.outbox.append(o.id),
          epochs:w.epochs.replaceAt(c.entity,w.epochs.nth(c.entity)+1),amounts:w.amounts.replaceAt(c.entity,w.amounts.nth(c.entity)+c.payload)}
      else if (o.kind == Fail) {
        val dead = c.failures+1 >= 3
        {...w,commands:w.commands.replaceAt(o.id,{...c,status:if (dead) "dead" else "ready",failures:c.failures+1}),dead:w.dead+(if(dead) 1 else 0)}
      }
      else {...w,rejected:w.rejected+1,commands:w.commands.replaceAt(o.id,{...c,status:"rejected"})} // Remaining constructor: Reject.
    }
  }
  pure def schedule(c:int): List[Op] = {
    val submit=op(Submit,0,0,1)
    val claim=op(Claim,0,0,0)
    val commit=op(Commit,0,0,1)
    if (c == 0) List(submit,claim,op(Expire,0,0,0),claim,commit,op(Commit,0,0,2))
    else if (c == 1) List(submit,claim,commit,commit)
    else if (c == 2) List(submit,claim,commit,op(Deliver,0,0,0),op(Deliver,0,0,0),op(Ack,0,0,0))
    else if (c == 3) List(submit,claim,op(Expire,0,0,0),claim,op(Commit,0,0,2))
    else if (c == 4) List(submit,op(Submit,0,0,99),op(Submit,1,0,2),op(Submit,2,1,4),
      claim,op(Claim,2,0,0),commit,op(Commit,2,0,2),op(Claim,1,0,0),op(Commit,1,0,3))
    else if (c == 5) List(submit,op(Submit,1,0,2),claim,op(Fail,0,0,0),claim,op(Fail,0,0,0),claim,op(Fail,0,0,0),op(Claim,1,0,0),op(Commit,1,0,4))
    else List(op(Submit,0,0,-1),claim,op(Reject,0,0,0))
  }
  pure def observe(w:World): Observation = {submitted:w.commands.length(),epochs:w.epochs,amounts:w.amounts,commits:w.history.length(),queued:w.outbox.length(),
    acknowledged:w.acked,fenced:w.fenced,dead:w.dead,rejected:w.rejected,winners:w.commands.select(c => c.status == "done").foldl(List(),(xs,c) => xs.append(c.token)),
    deliveryAttempts:w.attempts,effects:w.effects.length()}
  pure def safe(w:World): bool = and {
    w.history.length() == w.epochs.foldl(0,(sum,e) => sum+e),
    w.history.length() == w.outbox.length()+w.acked,
    w.history.indices().forall(i => w.history.indices().forall(j => i == j or w.history.nth(i) != w.history.nth(j))),
    w.effects.length() <= w.attempts,
  }
  pure val allSchedulePrefixesSafe = 0.to(6).forall(c => schedule(c).indices().forall(i =>
    safe(schedule(c).slice(0,i+1).foldl(initial,applyOp))))
  action init = all {scenario' = -1,result' = observe(initial)}
  action check(c:int): bool = all {c >= 0,c <= 6,scenario' = c,result' = observe(schedule(c).foldl(initial,applyOp))}
  action step = {nondet c=0.to(6).oneOf(); check(c)}
  val safety = and {
    result.commits == result.epochs.foldl(0,(sum,e) => sum+e),
    result.commits == result.queued+result.acknowledged,
    result.effects <= result.deliveryAttempts,
    scenario == 0 implies result.winners == List(2) and result.fenced == 1,
    scenario == 1 implies result.commits == 1,
    scenario == 2 implies result.deliveryAttempts == 2 and result.effects == 1,
  }
}
