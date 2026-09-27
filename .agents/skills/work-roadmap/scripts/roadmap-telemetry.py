#!/usr/bin/env python3
"""Record repository-owned roadmap dispatches without claiming native tool interception."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import uuid
from datetime import datetime, timezone

sys.dont_write_bytecode = True

from fsgg_telemetry_defaults import (
    CI_ASSIGNMENT_SCHEMA,
    ConfigurationError,
    HostConfig,
    create_assignment,
    discover_config,
    validate_identity,
    validate_workspace,
    workspace_mutation_command,
    write_private_json,
)
from native_collaboration_usage import HostUnavailable, collect as collect_native_usage


BATCH_SCHEMA = "fsgg.telemetry.ingest/1"
STATE_SCHEMA = "fsgg.telemetry.roadmap-dispatch-state/1"
RUNTIME = "collaboration-spawn-agent"
REVIEW_SCHEMA = "fsgg.telemetry.process-review-input/1"
ACTIVITY_SCHEMA = "fsgg.telemetry.activity-span-input/1"
ATTRIBUTION_SCHEMA = "fsgg.telemetry.activity-usage-attribution-input/1"
COMPLICATION_SCHEMA = "fsgg.telemetry.complication-input/1"
DASHBOARD_HEALTH_SCHEMA = "fsgg.telemetry.dashboard-event-health/1"
ORIGINAL_ASSIGNMENTS = "docs/coordination/telemetry-original-item-assignments.json"
ORIGINAL_ASSIGNMENTS_SCHEMA = "fsgg.telemetry.original-item-assignments/1"
ORIGINAL_BINDING_STATE_SCHEMA = "fsgg.telemetry.original-binding-state/1"


def now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def event(kind: str, identity: str, item: str | None, **values: object) -> dict[str, object]:
    return {"kind": kind, "identity": identity, "itemId": item, "revision": 0, **values}


def digest(prefix: str, *values: str) -> str:
    value = "\x1f".join(values).encode()
    return prefix + hashlib.sha256(value).hexdigest()[:32]


def authorized_original(feature: str, item: str, original: str) -> str:
    """Bind a non-self original to an immutable read of protected .github/main."""
    def github(endpoint: str) -> dict[str, object]:
        try:
            result = subprocess.run(
                ["gh", "api", endpoint], capture_output=True, text=True, timeout=10, check=False,
            )
        except (OSError, subprocess.SubprocessError) as error:
            raise ConfigurationError("protected original-item assignment is unavailable") from error
        if result.returncode != 0 or len(result.stdout.encode("utf-8")) > 131072:
            raise ConfigurationError("protected original-item assignment is unavailable")
        try:
            value = json.loads(result.stdout)
        except json.JSONDecodeError as error:
            raise ConfigurationError("protected original-item assignment is malformed") from error
        if not isinstance(value, dict):
            raise ConfigurationError("protected original-item assignment is malformed")
        return value

    ref = github("repos/FS-GG/.github/git/ref/heads/main")
    commit = ref.get("object")
    if (ref.get("ref") != "refs/heads/main" or not isinstance(commit, dict) or
            commit.get("type") != "commit" or not isinstance(commit.get("sha"), str) or
            not re.fullmatch(r"[0-9a-f]{40}", commit["sha"])):
        raise ConfigurationError("protected original-item revision is malformed")
    revision = commit["sha"]
    content = github(f"repos/FS-GG/.github/contents/{ORIGINAL_ASSIGNMENTS}?ref={revision}")
    if (content.get("type") != "file" or content.get("path") != ORIGINAL_ASSIGNMENTS or
            content.get("encoding") != "base64" or not isinstance(content.get("content"), str)):
        raise ConfigurationError("protected original-item assignment is malformed")
    try:
        raw = base64.b64decode(content["content"].replace("\n", ""), validate=True)
        if len(raw) > 65536:
            raise ValueError("oversize")
        document = json.loads(raw)
    except (ValueError, json.JSONDecodeError) as error:
        raise ConfigurationError("protected original-item assignment is malformed") from error
    if (not isinstance(document, dict) or set(document) != {"schema", "assignments"} or
            document["schema"] != ORIGINAL_ASSIGNMENTS_SCHEMA or
            not isinstance(document["assignments"], list) or len(document["assignments"]) > 200):
        raise ConfigurationError("protected original-item assignment is malformed")
    matches = []
    for row in document["assignments"]:
        if (not isinstance(row, dict) or set(row) != {"featureId", "itemId", "originalItemId"} or
                any(not isinstance(row[key], str) for key in row)):
            raise ConfigurationError("protected original-item assignment is malformed")
        if row["featureId"] == feature and row["itemId"] == item:
            matches.append(row["originalItemId"])
    if matches != [original]:
        raise ConfigurationError("original item is not authorized by the protected assignment")
    return revision + ":" + hashlib.sha256(raw).hexdigest()


def prepare_publication(
    config: HostConfig,
    state: dict[str, object],
    operation: str,
    next_phase: str,
    events: list[dict[str, object]],
) -> None:
    pending = state.get("pendingPublication")
    if pending is not None:
        if (not isinstance(pending, dict) or pending.get("operation") != operation or
                pending.get("nextPhase") != next_phase or
                not isinstance(pending.get("batch"), dict) or pending["batch"].get("events") != events):
            raise ConfigurationError("a different telemetry publication is already pending")
        return
    sequence = int(state["sequence"]) + 1
    state["sequence"] = sequence
    invocation = str(state["invocationId"])
    batch = {
        "schema": BATCH_SCHEMA,
        "ingestId": f"{invocation}-{sequence:06d}",
        "sourceIdentity": str(state["producerStream"]),
        "generation": invocation,
        "cursor": str(sequence),
        "eventCount": len(events),
        "events": events,
    }
    state["pendingPublication"] = {"operation": operation, "nextPhase": next_phase, "batch": batch}
    save_state(config, state)


def publish_pending(config: HostConfig, state: dict[str, object]) -> bool:
    validate_workspace(config)
    pending = state.get("pendingPublication")
    if not isinstance(pending, dict) or set(pending) != {"operation", "nextPhase", "batch"}:
        raise ConfigurationError("telemetry publication intent is unavailable")
    batch = pending["batch"]
    if (not isinstance(batch, dict) or batch.get("schema") != BATCH_SCHEMA or
            batch.get("generation") != state.get("invocationId") or
            batch.get("cursor") != str(state.get("sequence"))):
        raise ConfigurationError("telemetry publication intent is malformed")
    invocation = str(state["invocationId"])
    sequence = int(state["sequence"])
    batch_path = write_private_json(config.store_root / "orchestrator-publish", f"batch-{invocation}-{sequence}", batch)
    try:
        command = ([config.engine, "telemetry", "workspace", "submit", "--config", str(config.path),
                    "--repository", str(config.repository), "--producer", str(state.get("associationProducer")),
                    "--binding-digest", str(state.get("associationDigest")), "--input", str(batch_path)] if config.workspace else
                   [config.engine, "telemetry", "store", "publish", "--store-root", str(config.store_root), "--input", str(batch_path)])
        completed = subprocess.run(
            workspace_mutation_command(config, command),
            text=True,
            capture_output=True,
            timeout=20,
            check=False,
        )
    finally:
        batch_path.unlink(missing_ok=True)
    if completed.returncode != 0:
        message = completed.stderr.strip() or "telemetry batch publication failed"
        if "invalid-request" in message:
            # The engine has definitively rejected these bytes, so they cannot
            # have been applied. Retaining that intent would permanently fence
            # every corrected observation behind an unreplayable batch. Roll
            # back only the unpublished cursor; unknown delivery outcomes keep
            # their exact durable batch and continue to use normal replay.
            state["sequence"] = sequence - 1
            del state["pendingPublication"]
            if str(pending["operation"]).startswith("native-usage:"):
                state.pop("usageIntent", None)
            save_state(config, state)
        raise ConfigurationError(message)
    if pending["operation"] == "population-only":
        if not config.workspace:
            raise ConfigurationError("original binding requires a receipt-scoped workspace")
        response = completed.stdout.strip()
        if response.startswith("{"):
            try:
                receipt = json.loads(response)
            except json.JSONDecodeError as error:
                raise ConfigurationError("original binding receipt is malformed") from error
            if (not isinstance(receipt, dict) or receipt.get("schema") != "fsgg.telemetry.receipt/1" or
                    receipt.get("batchId") != batch.get("ingestId")):
                raise ConfigurationError("original binding receipt is malformed")
            response = receipt.get("status")
        if response != "applied":
            if response == "durably-received":
                raise ConfigurationError("original binding receipt is not applied; retry the exact binding")
            raise ConfigurationError("original binding receipt did not apply")
    state["phase"] = pending["nextPhase"]
    del state["pendingPublication"]
    save_state(config, state)
    return True


def publish(
    config: HostConfig,
    state: dict[str, object],
    events: list[dict[str, object]],
    *,
    operation: str = "observation",
    next_phase: str | None = None,
) -> bool:
    prepare_publication(config, state, operation, next_phase or str(state["phase"]), events)
    return publish_pending(config, state)


def state_path(config: HostConfig, token: str) -> pathlib.Path:
    if not isinstance(token, str) or not re.fullmatch(r"[0-9a-f]{32}", token):
        raise ConfigurationError("token must be the opaque 32-hex dispatch token")
    return config.store_root / "orchestrator-dispatches" / f"{token}.json"


def drain_command(config: HostConfig) -> list[str]:
    if config.workspace:
        return workspace_mutation_command(config, [
            config.engine, "telemetry", "workspace", "drain", "--config", str(config.path),
            "--repository", str(config.repository), "--binding-digest", str(config.binding_digest),
        ])
    return [config.engine, "telemetry", "store", "drain", "--store-root", str(config.store_root)]


def read_state(config: HostConfig, token: str) -> dict[str, object]:
    path = state_path(config, token)
    try:
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 262144:
            raise ConfigurationError("dispatch state is unavailable")
        if os.name != "nt" and (path.stat().st_mode & 0o777) != 0o600:
            raise ConfigurationError("dispatch state permissions must be 0600")
        value = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(value, dict) or value.get("schema") != STATE_SCHEMA or value.get("token") != token:
            raise ConfigurationError("dispatch state is malformed")
        if config.workspace and (value.get("associationProducer") != config.producer or value.get("associationDigest") != config.binding_digest):
            raise ConfigurationError("dispatch state belongs to a retired workspace association")
        return value
    except (OSError, json.JSONDecodeError) as error:
        raise ConfigurationError(f"dispatch state is unreadable: {error}") from error


def save_state(config: HostConfig, state: dict[str, object]) -> None:
    directory = ("orchestrator-original-bindings" if state.get("schema") == ORIGINAL_BINDING_STATE_SCHEMA
                 else "orchestrator-dispatches")
    write_private_json(config.store_root / directory, str(state["token"]), state)


def population_only(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    """Bind an observed codex-exec member without creating a second expected root."""
    feature = validate_identity("feature", args.feature)
    item = validate_identity("item", args.item)
    original = validate_identity("original item", args.original_item)
    producer = validate_identity("producer", args.producer)
    if not config.workspace:
        raise ConfigurationError("population-only requires a receipt-scoped workspace")
    if item == original:
        raise ConfigurationError("population-only requires a distinct protected original item")
    token = hashlib.sha256(f"population-only\x1f{feature}\x1f{item}".encode()).hexdigest()[:32]
    path = config.store_root / "orchestrator-original-bindings" / f"{token}.json"
    expected = {
        "schema": ORIGINAL_BINDING_STATE_SCHEMA,
        "token": token,
        "featureId": feature,
        "itemId": item,
        "originalItemId": original,
        "producerStream": producer,
        "associationProducer": config.producer,
        "associationDigest": config.binding_digest,
    }
    if path.exists() or path.is_symlink():
        try:
            if path.is_symlink() or not path.is_file() or path.stat().st_size > 262144:
                raise ConfigurationError("original binding state is unavailable")
            if os.name != "nt" and (path.stat().st_mode & 0o777) != 0o600:
                raise ConfigurationError("original binding state permissions must be 0600")
            state = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise ConfigurationError("original binding state is unreadable") from error
        if not isinstance(state, dict) or any(state.get(key) != value for key, value in expected.items()):
            raise ConfigurationError("original binding retry differs from its protected identity")
        if (state.get("invocationId") != "original-binding-" + token or
                not isinstance(state.get("originalAssignmentDigest"), str) or
                not re.fullmatch(r"[0-9a-f]{40}:[0-9a-f]{64}", state["originalAssignmentDigest"]) or
                state.get("sequence") != 1):
            raise ConfigurationError("original binding state is malformed")
        if state.get("phase") == "applied" and state.get("pendingPublication") is None:
            authorized_original(feature, item, original)
            return {"schema": "fsgg.telemetry.original-binding-result/1", "status": "applied"}
        if state.get("phase") != "pending":
            raise ConfigurationError("original binding state is malformed")
        publish_pending(config, state)
    else:
        assignment_digest = authorized_original(feature, item, original)
        state = {**expected, "originalAssignmentDigest": assignment_digest,
                 "phase": "pending", "sequence": 0,
                 "invocationId": "original-binding-" + token}
        key = digest("", item, original)
        events = [
            event("feature", feature, None, name=feature),
            event("item", item, item, featureId=feature),
            event("budget-population", "budget-population-" + key, item,
                  originalItemId=original, state="open", sourceKind="native-item",
                  sourceRef="roadmap-dispatch:" + key),
        ]
        publish(config, state, events, operation="population-only", next_phase="applied")
    return {"schema": "fsgg.telemetry.original-binding-result/1", "status": "applied"}


def matching_dispatch(config: HostConfig, expected: dict[str, object]) -> dict[str, object] | None:
    directory = config.store_root / "orchestrator-dispatches"
    if not directory.exists():
        return None
    paths = list(directory.glob("*.json"))
    if len(paths) > 4096:
        raise ConfigurationError("dispatch state inventory exceeds the recovery bound")
    identity = {name: expected[name] for name in ("featureId", "itemId", "attemptId")}
    matches = []
    for path in paths:
        if not re.fullmatch(r"[0-9a-f]{32}\.json", path.name):
            continue
        state = read_state(config, path.stem)
        if all(state.get(name) == value for name, value in identity.items()):
            matches.append(state)
    if len(matches) > 1:
        raise ConfigurationError("dispatch identity is ambiguous in private state")
    if not matches:
        return None
    if not all((matches[0].get(name, matches[0].get("itemId")) if name == "originalItemId"
                else matches[0].get(name)) == value for name, value in expected.items()):
        raise ConfigurationError("dispatch attempt retry differs from its durable identity")
    return matches[0]


def refresh_dashboard(config: HostConfig) -> dict[str, object]:
    dashboard = pathlib.Path(__file__).resolve().parents[4] / "tools" / "telemetry-dashboard.py"
    try:
        completed = subprocess.run(
            [sys.executable, str(dashboard), "publisher-event", "--config", str(config.path)],
            text=True, capture_output=True, timeout=60, check=False,
        )
        if completed.returncode != 0 or len(completed.stdout.encode("utf-8")) > 8192:
            return {"status": "advisory-failure", "reason": "publisher-event-subprocess-failed"}
        value = json.loads(completed.stdout)
        fields={"schema","status","reason","observedAt","publicRevision","commit"}
        if not isinstance(value,dict) or set(value)!=fields or value.get("schema")!=DASHBOARD_HEALTH_SCHEMA:
            return {"status": "advisory-failure", "reason": "publisher-event-result-invalid"}
        return {"status":"observed","health":value}
    except (OSError,subprocess.SubprocessError,UnicodeError,json.JSONDecodeError):
        return {"status": "advisory-failure", "reason": "publisher-event-subprocess-failed"}


def prospective_coverage(state: dict[str, object]) -> str:
    return ("native-collaboration-usage-unknown" if state.get("hostParentThreadId")
            else "native-collaboration-usage-unsupported")


def begin(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    feature = validate_identity("feature", args.feature)
    item = validate_identity("item", args.item)
    original_item = validate_identity("original item", args.original_item or item)
    attempt = validate_identity("attempt", args.attempt)
    parent_attempt = validate_identity("parent attempt", args.parent_attempt, optional=True)
    producer = validate_identity("producer", args.producer)
    model = validate_identity("model", args.model)
    effort = validate_identity("effort", args.effort)
    if args.late_after_seconds < 0:
        raise ConfigurationError("late-after-seconds must be non-negative")
    parent_dispatch = parent_invocation = None
    relation = "root"
    assignment_digest = None
    if args.parent_token:
        parent = read_state(config, args.parent_token)
        if args.original_item is None:
            original_item = str(parent.get("originalItemId", item))
        if parent.get("phase") not in {"started", "terminal"}:
            raise ConfigurationError("parent dispatch must be started before a child is expected")
        if parent.get("itemId") != item:
            raise ConfigurationError("parent and child dispatches must share the item identity")
        if parent.get("originalItemId", item) != original_item:
            raise ConfigurationError("parent and child dispatches must share the original item identity")
        parent_dispatch = str(parent["dispatchId"])
        parent_invocation = str(parent["invocationId"])
        assignment_digest = parent.get("originalAssignmentDigest")
        relation = args.relation
        if relation == "follow-up" and not isinstance(parent.get("usageLedger", {}), dict):
            raise ConfigurationError("follow-up usage baseline is malformed")
    elif args.relation != "root":
        raise ConfigurationError("child and follow-up dispatches require --parent-token")
    elif original_item != item:
        assignment_digest = authorized_original(feature, item, original_item)
    expected = {
        "featureId": feature, "itemId": item, "originalItemId": original_item,
        "originalAssignmentDigest": assignment_digest,
        "attemptId": attempt, "parentAttemptId": parent_attempt,
        "producerStream": producer, "model": model, "effort": effort, "relation": relation,
        "parentDispatchId": parent_dispatch, "parentInvocationId": parent_invocation,
        "lateAfterSeconds": args.late_after_seconds,
    }
    host_parent = os.environ.get("CODEX_THREAD_ID") if relation != "root" else None
    if host_parent and not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", host_parent):
        host_parent = None
    existing = matching_dispatch(config, expected)
    if existing is not None:
        if existing.get("phase") == "begin-pending":
            publish_pending(config, existing)
        elif existing.get("phase") != "expected":
            raise ConfigurationError("dispatch attempt already progressed beyond expectation")
        return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "expected",
                "token": existing["token"], "coverage": prospective_coverage(existing)}
    if (args.parent_token and parent.get("phase") != "started" and
            not (relation == "follow-up" and parent.get("phase") == "terminal")):
        raise ConfigurationError("parent dispatch must be started before a child is expected")
    baseline_ids: list[str] = []
    baseline_known = relation != "follow-up"
    if relation == "follow-up" and host_parent and parent.get("hostParentThreadId") == host_parent:
        try:
            prior = collect_native_usage(host_parent, str(parent["nativeId"]))
            baseline_ids = list(prior["allTurnIds"])
            baseline_known = True
        except (HostUnavailable, OSError, subprocess.SubprocessError):
            pass
    token, activation, dispatch, invocation = (uuid.uuid4().hex for _ in range(4))
    root_invocation = invocation
    if args.parent_token:
        activation = str(parent["activationId"])
        root_invocation = str(parent["rootInvocationId"])
    timestamp = now()
    state: dict[str, object] = {
        "schema": STATE_SCHEMA,
        "token": token,
        "phase": "begin-pending",
        "sequence": 0,
        "featureId": feature,
        "itemId": item,
        "originalItemId": original_item,
        "originalAssignmentDigest": assignment_digest,
        "attemptId": attempt,
        "parentAttemptId": parent_attempt,
        "producerStream": producer,
        "model": model,
        "effort": effort,
        "activationId": activation,
        "dispatchId": dispatch,
        "invocationId": invocation,
        "rootInvocationId": root_invocation,
        "parentDispatchId": parent_dispatch,
        "parentInvocationId": parent_invocation,
        "relation": relation,
        "lateAfterSeconds": args.late_after_seconds,
        "nativeId": None,
        "hostParentThreadId": host_parent,
        "baselineTurnIds": baseline_ids,
        "usageBaselineKnown": baseline_known,
        "associationProducer": config.producer,
        "associationDigest": config.binding_digest,
    }
    events = []
    if relation == "root":
        events.extend([
            event("feature", feature, None, name=feature),
            event("item", item, item, featureId=feature),
            event("budget-population", digest("budget-population-", item, original_item), item,
                  originalItemId=original_item, state="open", sourceKind="native-item",
                  sourceRef=digest("roadmap-dispatch:", item, original_item)),
            event("operational-activation", f"operational-activation-{activation}", item,
                  activationId=activation, scope="explicit-future-dispatches", runtime=RUNTIME,
                  activatedAt=timestamp, clockProvenance="host-wall", lateAfterSeconds=args.late_after_seconds),
        ])
    if parent_attempt:
        events.append(event("parent-child", digest("parent-child-", parent_attempt, attempt), item,
                            parentId=parent_attempt, childId=attempt))
    events.append(event("expected-dispatch", f"expected-dispatch-{dispatch}", item,
                        dispatchId=dispatch, activationId=activation, relation=relation,
                        parentDispatchId=parent_dispatch, runtime=RUNTIME,
                        expectedAt=timestamp, clockProvenance="host-wall"))
    publish(config, state, events, operation="begin", next_phase="expected")
    return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "expected", "token": token,
            "coverage": prospective_coverage(state)}


def started(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    native_id = validate_identity("native id", args.native_id)
    if state.get("phase") == "started":
        if state.get("nativeId") != native_id:
            raise ConfigurationError("started dispatch belongs to a different native identity")
        return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "started", "token": args.token,
                "coverage": prospective_coverage(state)}
    if state.get("phase") == "start-pending":
        if state.get("nativeId") != native_id:
            raise ConfigurationError("pending start belongs to a different native identity")
        publish_pending(config, state)
        return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "started", "token": args.token,
                "coverage": prospective_coverage(state)}
    if state.get("phase") != "expected":
        raise ConfigurationError("dispatch must be expected before start")
    item, invocation = str(state["itemId"]), str(state["invocationId"])
    timestamp = now()
    events = [
        event("invocation-lineage", f"invocation-lineage-{invocation}", item,
              dispatchId=state["dispatchId"], invocationId=invocation, relation=state["relation"],
              parentInvocationId=state["parentInvocationId"], rootInvocationId=state["rootInvocationId"], runtime=RUNTIME),
        event("runtime-admission", f"runtime-admission-{invocation}", item,
              invocationId=invocation, featureId=state["featureId"], attemptId=state["attemptId"],
              parentAttemptId=state["parentAttemptId"], producerStream=state["producerStream"],
              requestedModel=state["model"], requestedEffort=state["effort"], backend="codex-collaboration"),
        event("runtime-start", f"runtime-process-{invocation}", item, invocationId=invocation,
              threadId=native_id, turnId=None, turnSequence=None, processId=0, phase="process"),
        event("event-time", f"event-time-{invocation}-admission", item, invocationId=invocation,
              event="admission", occurredAt=timestamp, occurredClockProvenance="host-wall",
              observedAt=timestamp, observedClockProvenance="host-wall"),
        event("event-time", f"event-time-{invocation}-start", item, invocationId=invocation,
              event="start", occurredAt=timestamp, occurredClockProvenance="host-wall",
              observedAt=timestamp, observedClockProvenance="host-wall"),
        event("runtime-gap", f"runtime-gap-{invocation}-native-process", item, invocationId=invocation,
              code="native-process-id-unavailable"),
    ]
    if not state.get("hostParentThreadId"):
        events.append(event("runtime-gap", f"runtime-gap-{invocation}-native-usage", item,
                            invocationId=invocation, code="native-collaboration-usage-unsupported"))
    state["phase"], state["nativeId"] = "start-pending", native_id
    publish(config, state, events, operation="started", next_phase="started")
    return {"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "started", "token": args.token,
            "coverage": prospective_coverage(state)}


def reconcile_usage(config: HostConfig, state: dict[str, object]) -> str:
    """Publish only verified native turns; retain missing coverage as unknown."""
    if state.get("phase") != "terminal" or not state.get("hostParentThreadId"):
        return "native-collaboration-usage-unsupported"
    if not state.get("usageBaselineKnown", state.get("relation") != "follow-up"):
        return "native-collaboration-usage-unknown"
    pending = state.get("pendingPublication")
    if isinstance(pending, dict) and pending.get("operation") == "native-thread":
        publish_pending(config, state)
        state["nativeThreadPublished"] = True
        save_state(config, state)
    intent = state.get("usageIntent")
    if isinstance(intent, dict):
        if state.get("pendingPublication"):
            try:
                publish_pending(config, state)
            except ConfigurationError:
                if not state.get("pendingPublication"):
                    state.pop("usageIntent", None)
                    save_state(config, state)
                raise
        ledger = state.setdefault("usageLedger", {})
        ledger[intent["turnId"]] = {"revision": intent["revision"], "hash": intent["hash"]}
        del state["usageIntent"]
        save_state(config, state)
    try:
        native = collect_native_usage(str(state["hostParentThreadId"]), str(state["nativeId"]))
    except (HostUnavailable, OSError, subprocess.SubprocessError):
        return "native-collaboration-usage-unknown"
    ledger = state.setdefault("usageLedger", {})
    if not isinstance(ledger, dict):
        raise ConfigurationError("native usage ledger is malformed")
    if state.get("nativeThreadId") and state["nativeThreadId"] != native["threadId"]:
        raise ConfigurationError("native child thread changed for a dispatch")
    if not state.get("nativeThreadPublished"):
        if not state.get("nativeThreadId"):
            state["nativeThreadId"] = native["threadId"]
            save_state(config, state)
        publish(config, state, [event("runtime-start", f"runtime-thread-{state['invocationId']}",
                                      str(state["itemId"]), invocationId=state["invocationId"],
                                      threadId=native["threadId"], turnId=None, turnSequence=None,
                                      processId=0, phase="thread")], operation="native-thread")
        state["nativeThreadPublished"] = True
        save_state(config, state)
    eligible = [turn for turn in native["turns"] if turn["turnId"] not in state.get("baselineTurnIds", [])]
    for turn in eligible:
        usage = turn["usage"]
        turn_id = str(turn["turnId"])
        fingerprint = hashlib.sha256(json.dumps([turn["turnSequence"], usage], sort_keys=True).encode()).hexdigest()
        previous = ledger.get(turn_id)
        if previous and previous.get("hash") == fingerprint:
            continue
        revision = 0 if previous is None else int(previous["revision"]) + 1
        observation = event("runtime-turn-usage", digest("runtime-turn-usage-", str(state["invocationId"]), turn_id),
                            str(state["itemId"]), invocationId=state["invocationId"],
                            threadId=native["threadId"], turnId=turn_id, turnSequence=turn["turnSequence"],
                            provider="openai", requestedModel=state["model"], observedModel=native.get("model"),
                            requestedEffort=state["effort"], observedEffort=native.get("effort"),
                            backend="codex-collaboration", scope="turn", provenance="codex-native-token-usage-record",
                            input=usage["input_tokens"], cachedInput=usage["cached_input_tokens"],
                            output=usage["output_tokens"], reasoning=usage["reasoning_output_tokens"],
                            total=usage["total_tokens"])
        observation["revision"] = revision
        state["usageIntent"] = {"turnId": turn_id, "revision": revision, "hash": fingerprint}
        save_state(config, state)
        publish(config, state, [observation], operation=f"native-usage:{turn_id}:{revision}")
        ledger[turn_id] = {"revision": revision, "hash": fingerprint}
        del state["usageIntent"]
        save_state(config, state)
    return ("native-collaboration-usage-complete" if native["complete"] and eligible
            else "native-collaboration-usage-unknown")


def finish(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    exit_code = args.exit_code if args.exit_code is not None else (0 if args.outcome == "completed" else 1)
    if exit_code < 0:
        raise ConfigurationError("exit-code must be non-negative")
    if state.get("phase") in {"terminal-pending", "terminal"}:
        if state.get("phase") == "terminal" and "exitCode" not in state:
            if args.exit_code is not None:
                raise ConfigurationError("legacy terminal state cannot verify an explicit exit-code retry")
            state["exitCode"] = exit_code
            save_state(config, state)
        if state.get("outcome") != args.outcome or state.get("exitCode") != exit_code:
            raise ConfigurationError("terminal retry differs from the durable terminal intent")
        if state.get("phase") == "terminal-pending":
            publish_pending(config, state)
    elif state.get("phase") == "started":
        item, invocation = str(state["itemId"]), str(state["invocationId"])
        timestamp = now()
        events = [
            event("runtime-terminal", f"runtime-terminal-{invocation}", item, invocationId=invocation,
                  threadId=state["nativeId"], outcome=args.outcome, exitCode=exit_code),
            event("event-time", f"event-time-{invocation}-terminal", item, invocationId=invocation,
                  event="terminal", occurredAt=timestamp, occurredClockProvenance="host-wall",
                  observedAt=timestamp, observedClockProvenance="host-wall"),
        ]
        state["phase"], state["outcome"], state["exitCode"] = "terminal-pending", args.outcome, exit_code
        publish(config, state, events, operation="finish", next_phase="terminal")
    else:
        raise ConfigurationError("dispatch must be started before terminal")
    try:
        coverage = reconcile_usage(config, state)
    except ConfigurationError:
        coverage = "native-collaboration-usage-unknown"
    drain = subprocess.run(
        drain_command(config),
        text=True, capture_output=True, timeout=30, check=False,
    )
    result={"schema": "fsgg.telemetry.roadmap-dispatch/1", "status": "terminal", "token": args.token,
            "outcome": args.outcome, "coverage": coverage,
            "drain": "complete" if drain.returncode == 0 else "pending"}
    if drain.returncode==0 and args.outcome=="completed" and state.get("relation")=="root":
        result["dashboardPublication"]=refresh_dashboard(config)
    return result


def usage_reconcile(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    coverage = reconcile_usage(config, state)
    drain = subprocess.run(drain_command(config), text=True, capture_output=True, timeout=30, check=False)
    return {"schema": "fsgg.telemetry.roadmap-usage/1", "status": "reconciled", "token": args.token,
            "coverage": coverage, "drain": "complete" if drain.returncode == 0 else "pending"}


def read_contract(path: str, schema: str, fields: set[str]) -> dict[str, object]:
    source = pathlib.Path(path)
    if source.is_symlink() or not source.is_file() or source.stat().st_size > 32768:
        raise ConfigurationError("private observation input must be a regular file of at most 32768 bytes")
    try:
        value = json.loads(source.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ConfigurationError(f"private observation input is unreadable: {error}") from error
    if not isinstance(value, dict) or value.get("schema") != schema or set(value) != fields | {"schema"}:
        raise ConfigurationError(f"private observation input must have the exact {schema} shape")
    return value


def record_event(config: HostConfig, state: dict[str, object], value: dict[str, object]) -> dict[str, object]:
    operation = f"observation:{value.get('kind')}:{value.get('identity')}"
    publish(config, state, [value], operation=operation)
    drain = subprocess.run(drain_command(config),
                           text=True, capture_output=True, timeout=30, check=False)
    if drain.returncode != 0:
        raise ConfigurationError(drain.stderr.strip() or "telemetry observation drain failed")
    result={"schema": "fsgg.telemetry.roadmap-observation/1", "status": "recorded", "kind": value["kind"]}
    if state.get("phase")=="terminal" and state.get("relation")=="root":
        result["dashboardPublication"]=refresh_dashboard(config)
    return result


def review(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") != "terminal":
        raise ConfigurationError("process review requires a terminal attempt")
    if args.scope == "item" and state.get("relation") != "root":
        raise ConfigurationError("item process review requires the root dispatch token")
    fields = {"revision", "outcomeSynopsis", "wentWell", "problems", "avoidableDelayOrRework",
              "processObservations", "remainingRisks", "concreteImprovements", "evidence",
              "evidenceCoverage", "populationCoverage", "confidence", "reviewerModel", "reviewerEffort",
              "reviewedAt", "durationSeconds"}
    value = read_contract(args.input, REVIEW_SCHEMA, fields)
    subject = str(state["attemptId"]) if args.scope == "attempt" else str(state["itemId"])
    observation = {"kind": "process-review", "identity": digest("process-review-", args.scope, str(state["itemId"]), subject),
                   "itemId": state["itemId"], "scope": args.scope,
                   "attemptId": state["attemptId"] if args.scope == "attempt" else None,
                   **{name: value[name] for name in fields}}
    return record_event(config, state, observation)


def activity(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") not in {"started", "terminal"}:
        raise ConfigurationError("activity span requires a started attempt")
    fields = {"revision", "activityId", "category", "startedAt", "endedAt", "clockProvenance", "evidence", "summary"}
    value = read_contract(args.input, ACTIVITY_SCHEMA, fields)
    activity_id = validate_identity("activity", value["activityId"])
    observation = {"kind": "activity-span", "identity": digest("activity-span-", str(state["itemId"]), activity_id),
                   "itemId": state["itemId"], "invocationId": state["invocationId"], "attemptId": state["attemptId"],
                   **{name: value[name] for name in fields}}
    return record_event(config, state, observation)


def attribution(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") != "terminal":
        raise ConfigurationError("usage attribution requires a terminal attempt")
    fields = {"revision", "usageIdentity", "activityId", "classification", "input", "cachedInput", "output", "reasoning", "total"}
    value = read_contract(args.input, ATTRIBUTION_SCHEMA, fields)
    usage = validate_identity("usage identity", value["usageIdentity"])
    observation = {"kind": "activity-usage-attribution", "identity": digest("activity-usage-", str(state["itemId"]), usage),
                   "itemId": state["itemId"], **{name: value[name] for name in fields}}
    return record_event(config, state, observation)


def complication(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    state = read_state(config, args.token)
    if state.get("phase") not in {"started", "terminal"}:
        raise ConfigurationError("complication requires a started attempt")
    fields = {"revision", "complicationId", "activityId", "trigger", "cause", "occurredAt", "synopsis", "evidence"}
    value = read_contract(args.input, COMPLICATION_SCHEMA, fields)
    complication_id = validate_identity("complication", value["complicationId"])
    observation = {"kind": "complication", "identity": digest("complication-", str(state["itemId"]), complication_id),
                   "itemId": state["itemId"], "attemptId": state["attemptId"],
                   **{name: value[name] for name in fields if name != "complicationId"}}
    return record_event(config, state, observation)


def create_ci(config: HostConfig, args: argparse.Namespace) -> dict[str, object]:
    path = create_assignment(config, CI_ASSIGNMENT_SCHEMA, feature=args.feature, item=args.item,
                             attempt=args.attempt, parent_attempt=args.parent_attempt, producer=args.producer)
    return {"schema": "fsgg.telemetry.assignment-result/1", "status": "ready", "assignment": str(path)}


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--config", help="explicit private host telemetry configuration")
    commands = result.add_subparsers(dest="command", required=True)
    begin_parser = commands.add_parser("begin")
    for name in ("feature", "item", "attempt", "model", "effort"):
        begin_parser.add_argument(f"--{name}", required=True)
    begin_parser.add_argument("--parent-attempt")
    begin_parser.add_argument("--original-item", help="stable original item shared by distinct member items")
    begin_parser.add_argument("--parent-token")
    begin_parser.add_argument("--relation", choices=("root", "child", "follow-up"), default="root")
    begin_parser.add_argument("--producer", default="roadmap-orchestrator")
    begin_parser.add_argument("--late-after-seconds", type=int, default=60)
    population_parser = commands.add_parser("population-only")
    for name in ("feature", "item", "original-item"):
        population_parser.add_argument(f"--{name}", required=True)
    population_parser.add_argument("--producer", default="roadmap-orchestrator")
    started_parser = commands.add_parser("started")
    started_parser.add_argument("--token", required=True)
    started_parser.add_argument("--native-id", required=True)
    finish_parser = commands.add_parser("finish")
    finish_parser.add_argument("--token", required=True)
    finish_parser.add_argument("--outcome", choices=("completed", "failed", "cancelled", "blocked"), required=True)
    finish_parser.add_argument("--exit-code", type=int)
    usage_parser = commands.add_parser("usage-reconcile")
    usage_parser.add_argument("--token", required=True)
    ci_parser = commands.add_parser("ci-assignment")
    for name in ("feature", "item", "attempt"):
        ci_parser.add_argument(f"--{name}", required=True)
    ci_parser.add_argument("--parent-attempt")
    ci_parser.add_argument("--producer", default="routine-delivery")
    review_parser = commands.add_parser("review")
    review_parser.add_argument("--token", required=True)
    review_parser.add_argument("--scope", choices=("attempt", "item"), required=True)
    review_parser.add_argument("--input", required=True)
    for command in ("activity", "usage-attribution", "complication"):
        observation_parser = commands.add_parser(command)
        observation_parser.add_argument("--token", required=True)
        observation_parser.add_argument("--input", required=True)
    commands.add_parser("status")
    return result


def main(argv: list[str]) -> int:
    args = parser().parse_args(argv)
    try:
        config = discover_config(args.config)
        if config is None:
            print(json.dumps({"schema": "fsgg.telemetry.host-status/1", "status": "not-configured"}, separators=(",", ":")))
            return 2
        if args.command == "status":
            command = ([config.engine, "telemetry", "workspace", "status", "--config", str(config.path),
                        "--repository", str(config.repository)] if config.workspace else
                       [config.engine, "telemetry", "store", "status", "--store-root", str(config.store_root)])
            completed = subprocess.run(command,
                                       text=True, capture_output=True, timeout=20, check=False)
            print(json.dumps({"schema": "fsgg.telemetry.host-status/1",
                              "status": "ready" if completed.returncode == 0 else "unavailable"}, separators=(",", ":")))
            return 0 if completed.returncode == 0 else 1
        handlers = {"begin": begin, "population-only": population_only,
                    "started": started, "finish": finish, "usage-reconcile": usage_reconcile,
                    "ci-assignment": create_ci,
                    "review": review, "activity": activity, "usage-attribution": attribution, "complication": complication}
        value = handlers[args.command](config, args)
        print(json.dumps(value, separators=(",", ":")))
        return 0
    except (ConfigurationError, OSError, subprocess.SubprocessError) as error:
        print(f"fsgg roadmap telemetry: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
