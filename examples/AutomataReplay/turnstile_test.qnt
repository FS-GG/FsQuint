module turnstile_test {
  import turnstile.* from "./turnstile"
  run passageTest = init
    .then(take("push"))
    .expect(state.actions == List("alarm"))
    .then(take("coin"))
    .then(take("coin"))
    .then(take("push"))
    .expect(state.phase == "Locked" and state.actions == List("pass"))
}
