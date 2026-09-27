#!/usr/bin/env python3
"""Regenerate genuine Quint traces, validate inputs/coverage, retain raw bytes on --write."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def meaningful(raw):
    doc = json.loads(raw)
    if doc.get("#meta", {}).get("status") != "ok":
        raise RuntimeError("Quint did not report positive trace evidence")
    return {"vars": doc["vars"], "states": [
        {key: value for key, value in state.items() if key != "#meta"}
        for state in doc["states"]]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Replace fixtures explicitly; default only compares")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    baseline = json.loads((root / "baseline.json").read_text())
    quint = Path(os.environ["QUINT_BIN"]).resolve()
    evaluator = Path(os.environ["QUINT_HOME"]) / "rust-evaluator-v0.6.0/quint_evaluator"
    if digest(quint) != baseline["quintSha256"] or digest(evaluator) != baseline["evaluatorSha256"]:
        raise RuntimeError("Generator/evaluator identity differs from the baseline")
    sources = {p.name: digest(p) for p in sorted(root.glob("*.qnt"))}
    commands = []
    inputs_seen, phases, outcomes = set(), set(), set()
    action_orders = set()
    resolver_paths, rule_markers, handlers = set(), set(), set()

    def invoke(arguments):
        commands.append(arguments)
        result = subprocess.run([str(quint), *arguments], cwd=root, capture_output=True,
                                text=True, timeout=120)
        if result.returncode != 0:
            raise RuntimeError(f"Quint failed ({result.returncode}): {result.stdout}\n{result.stderr}")

    def accept(candidate, relative):
        target = root / "fixtures" / relative
        raw = candidate.read_bytes()
        parsed = meaningful(raw)
        if args.write:
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.exists():
                if parsed != meaningful(target.read_bytes()):
                    raise RuntimeError(f"Existing regression changed: version the profile/fixture before replacing {relative}")
            else:
                target.write_bytes(raw)
        elif parsed != meaningful(target.read_bytes()):
            raise RuntimeError(f"Regeneration differs semantically: {relative}")
        return target, parsed["states"]

    def json_file(path, value):
        if args.write:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(value, indent=2) + "\n")
        elif json.loads(path.read_text()) != value:
            raise RuntimeError(f"Manifest/coverage differs: {path.name}")

    allowed = {("submit", "author"), ("approve", "author"), ("approve", "reviewer"),
               ("publish", "author"), ("cancel", "author"), ("remind", "author")}

    def approval_binding(target, states, entries, source, seed, sampled):
        if len(entries) != len(states) - 1:
            raise RuntimeError("Input count differs")
        for entry, state in zip(entries, states[1:]):
            inp = state["input"]
            if set(inp) != {"op", "actor"} or (inp["op"], inp["actor"]) not in allowed:
                raise RuntimeError("Unknown generated input")
            if inp != {"op": entry["op"], "actor": entry["actor"]}:
                raise RuntimeError("Authored input differs from instrumented input")
            inputs_seen.add((inp["op"], inp["actor"]))
        for item in states:
            state = item["state"]
            phases.add(state["phase"])
            outcomes.add(state["outcome"])
            action_orders.add(tuple((e["kind"], e["name"]) for e in state["actions"]))
        binding = {"schema": "fsquint.approval-binding/2" if sampled else "fsquint.approval-binding/1",
                   "profile": "fsquint.automata-approval/1", "traceSha256": digest(target),
                   "modelSha256": sources["approval.qnt"], "scenarioSha256": sources[source], "steps": entries}
        if sampled:
            binding.update(sourceFile=source, seed=str(seed), maxSteps=30)
        json_file(target.with_suffix("").with_suffix(".binding.json"), binding)

    with tempfile.TemporaryDirectory(prefix="fsquint-generation-") as scratch:
        scratch = Path(scratch)
        for module in ("approval", "resolver", "turnstile", "correction", "protocol"):
            invoke(["typecheck", f"{module}.qnt"])
            invoke(["test", f"{module}_test.qnt", "--seed", "42", "--max-samples", "1",
                    "--out-itf", str(scratch / f"{module}_{{test}}_{{seq}}.itf.json")])
        authored = {}
        name = None
        for line, text in enumerate((root / "approval_test.qnt").read_text().splitlines(), 1):
            match = re.search(r"run (\w+)", text)
            if match:
                name = match[1]
                authored[name] = []
            match = re.search(r'op: "(\w+)", actor: "(\w+)"', text)
            if match:
                authored[name].append(dict(index=len(authored[name])+1, op=match[1], actor=match[2], line=line))
        for name, entries in authored.items():
            matches = list(scratch.glob(f"approval_{name}_*.itf.json"))
            if len(matches) != 1:
                raise RuntimeError("Missing or ambiguous witness output")
            target, states = accept(matches[0], f"{name}.itf.json")
            approval_binding(target, states, entries, "approval_test.qnt", 42, False)
        for case in range(13):
            matches = list(scratch.glob(f"resolver_case{case}Test_*.itf.json"))
            if len(matches) != 1:
                raise RuntimeError("Missing resolver witness")
            _, states = accept(matches[0], f"resolver/case{case}.itf.json")
            result = states[-1]["result"]
            resolver_paths.add((tuple(result["exited"]), tuple(result["entered"])))
            handlers.add(result["handler"])
            rule_markers.update(e["node"] for e in result["actions"] if e["kind"] == "rule")
        accept(next(scratch.glob("turnstile_passageTest_*.itf.json")), "turnstile.itf.json")
        for case in range(10):
            matches = list(scratch.glob(f"correction_case{case}Test_*.itf.json"))
            if len(matches) != 1:
                raise RuntimeError("Missing correction witness")
            accept(matches[0], f"correction/case{case}.itf.json")
        for case in range(7):
            matches = list(scratch.glob(f"protocol_case{case}Test_*.itf.json"))
            if len(matches) != 1:
                raise RuntimeError("Missing protocol witness")
            accept(matches[0], f"protocol/case{case}.itf.json")
        line = next(i for i, s in enumerate((root / "approval.qnt").read_text().splitlines(), 1) if "action step" in s)
        for seed in (1, 7, 42, 99, 123, 1000, 2000, 3000):
            path = scratch / f"sample-{seed}.itf.json"
            invoke(["run", "approval.qnt", "--invariant", "safety", "--seed", str(seed),
                    "--max-samples", "1", "--max-steps", "30", "--out-itf", str(path)])
            target, states = accept(path, f"sampled/seed-{seed}.itf.json")
            # Uses the dedicated choice channel, never adjacent domain observations.
            entries = [dict(index=i, op=s["input"]["op"], actor=s["input"]["actor"], line=line)
                       for i, s in enumerate(states[1:], 1)]
            approval_binding(target, states, entries, "approval.qnt", seed, True)

    resolver_files = ["resolver.qnt", "resolver_test.qnt"] + [f"fixtures/resolver/case{i}.itf.json" for i in range(13)]
    json_file(root / "fixtures/resolver/manifest.json", {name: digest(root / name) for name in resolver_files})
    turnstile_files = ["fixtures/turnstile.itf.json", "turnstile.qnt", "turnstile_test.qnt"]
    json_file(root / "fixtures/turnstile.manifest.json", {name: digest(root / name) for name in turnstile_files})
    correction_files = ["correction.qnt", "correction_test.qnt"] + [f"fixtures/correction/case{i}.itf.json" for i in range(10)]
    json_file(root / "fixtures/correction/manifest.json", {name: digest(root / name) for name in correction_files})
    protocol_files = ["protocol.qnt", "protocol_test.qnt"] + [f"fixtures/protocol/case{i}.itf.json" for i in range(7)]
    json_file(root / "fixtures/protocol/manifest.json", {name: digest(root / name) for name in protocol_files})
    if inputs_seen != allowed or phases != {"Draft", "Pending", "Approved", "Published", "Cancelled"}:
        raise RuntimeError("Required input/phase coverage missing")
    if not {"Initial", "Applied", "NotAuthorized", "Unhandled", "Terminated"}.issubset(outcomes):
        raise RuntimeError("Required outcome coverage missing")
    for order in ((('audit','submitted'),('notify','review')), (('audit','published'),('notify','published'))):
        if order not in action_orders:
            raise RuntimeError("Required ordered action coverage missing")
    coverage = {"schema": "fsquint.coverage/1", "inputs": sorted([list(x) for x in inputs_seen]),
                "phases": sorted(phases), "outcomes": sorted(outcomes), "resolverCases": list(range(13)),
                "correctionCases": list(range(10)),
                "protocolSchedules": list(range(7)),
                "actionOrders": [[list(pair) for pair in order] for order in sorted(action_orders)],
                "resolverPaths": [[list(exits), list(entries)] for exits, entries in sorted(resolver_paths)],
                "resolverHandlers": sorted(handlers), "ruleMarkers": sorted(rule_markers),
                "sampleSeeds": [1,7,42,99,123,1000,2000,3000], "sampleStepBound": 30,
                "claim": "required bins exercised; no exhaustive application/runtime claim"}
    json_file(root / "fixtures/coverage.json", coverage)
    evidence = {"schema": "fsquint.generation-evidence/1", "outcome": "passed", "quintSha256": digest(quint),
                "evaluatorSha256": digest(evaluator), "sourceDigests": sources, "coverage": coverage,
                "commands": commands, "rawAndBindingDigests": {
                    str(p.relative_to(root)): digest(p) for p in sorted((root / "fixtures").rglob("*.json"))}}
    out = root.parents[1] / "artifacts/automata-generation.json"
    out.parent.mkdir(exist_ok=True)
    out.write_text(json.dumps(evidence, indent=2) + "\n")
    print("PASS: pinned witness/sample regeneration, explicit input bindings and all required coverage bins.")


if __name__ == "__main__":
    main()
