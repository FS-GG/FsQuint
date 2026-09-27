"""Project completed native child usage from the local Codex host.

Only App Server identity/turn metadata and token_usage_record rollout entries are
retained. Conversation items and other rollout entries are never decoded.
"""

from __future__ import annotations

import json
import os
import pathlib
import re
import select
import subprocess
import time
from collections import defaultdict


UUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\Z")
COUNTS = ("input_tokens", "cached_input_tokens", "output_tokens", "reasoning_output_tokens", "total_tokens")


class HostUnavailable(Exception):
    """The host cannot prove an exact child identity or final usage."""


def counts(value: object) -> dict[str, int]:
    if not isinstance(value, dict):
        raise HostUnavailable("native usage counters are unavailable")
    result = {}
    for key in COUNTS:
        number = value.get(key)
        if not isinstance(number, int) or isinstance(number, bool) or number < 0:
            raise HostUnavailable("native usage counters are malformed")
        result[key] = number
    if result["cached_input_tokens"] > result["input_tokens"] or result["reasoning_output_tokens"] > result["output_tokens"]:
        raise HostUnavailable("native usage subsets exceed their parent counts")
    if result["input_tokens"] + result["output_tokens"] != result["total_tokens"]:
        raise HostUnavailable("native usage total does not equal input plus output")
    return result


class AppServer:
    def __init__(self, command: str = "codex") -> None:
        try:
            self.process = subprocess.Popen(
                [command, "app-server"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL, text=True, bufsize=1,
            )
            self.request(1, "initialize", {
                "clientInfo": {"name": "fsgg_telemetry", "title": "FS-GG Telemetry", "version": "1"},
                "capabilities": {"experimentalApi": True},
            })
            assert self.process.stdin is not None
            self.process.stdin.write('{"method":"initialized","params":{}}\n')
            self.process.stdin.flush()
        except (OSError, ValueError, AssertionError, HostUnavailable) as error:
            self.close()
            raise HostUnavailable("Codex App Server is unavailable") from error

    def request(self, request_id: int, method: str, params: dict[str, object]) -> dict[str, object]:
        if self.process.stdin is None or self.process.stdout is None:
            raise HostUnavailable("Codex App Server stream is unavailable")
        self.process.stdin.write(json.dumps({"id": request_id, "method": method, "params": params}, separators=(",", ":")) + "\n")
        self.process.stdin.flush()
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            if not select.select([self.process.stdout], [], [], max(0, deadline - time.monotonic()))[0]:
                break
            line = self.process.stdout.readline()
            if not line or len(line) > 1024 * 1024:
                break
            try:
                response = json.loads(line)
            except json.JSONDecodeError:
                continue
            if response.get("id") == request_id:
                result = response.get("result")
                if not isinstance(result, dict) or response.get("error") is not None:
                    raise HostUnavailable("Codex App Server refused a read-only usage request")
                return result
        raise HostUnavailable("Codex App Server read timed out")

    def close(self) -> None:
        process = getattr(self, "process", None)
        if process is None:
            return
        process.terminate()
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=2)

    def __enter__(self) -> "AppServer":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


def page(server: AppServer, method: str, request_id: int, params: dict[str, object]) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    cursor: str | None = None
    for offset in range(10):
        request = dict(params, cursor=cursor, limit=100)
        result = server.request(request_id + offset, method, request)
        data = result.get("data")
        if not isinstance(data, list) or any(not isinstance(row, dict) for row in data):
            raise HostUnavailable("Codex App Server returned malformed pagination")
        rows.extend(data)
        if len(rows) > 1000:
            raise HostUnavailable("native child inventory exceeds the bound")
        next_cursor = result.get("nextCursor")
        if next_cursor is None:
            return rows
        if not isinstance(next_cursor, str) or not next_cursor or next_cursor == cursor:
            raise HostUnavailable("Codex App Server pagination is invalid")
        cursor = next_cursor
    raise HostUnavailable("Codex App Server pagination exceeds the bound")


def rollout_usage(path: str, thread_id: str, turn_ids: set[str], codex_home: pathlib.Path) -> dict[str, list[dict[str, object]]]:
    source = pathlib.Path(path)
    root = (codex_home / "sessions").resolve()
    if source.is_symlink() or not source.is_file() or not source.resolve().is_relative_to(root) or source.stat().st_size > 128 * 1024 * 1024:
        raise HostUnavailable("native usage rollout is unavailable or outside the private host")
    records: dict[str, list[dict[str, object]]] = defaultdict(list)
    with source.open("rb") as stream:
        for line in stream:
            if len(line) > 1024 * 1024 or not re.search(rb'"type"\s*:\s*"token_usage_record"', line[:256]):
                continue
            try:
                row = json.loads(line)
            except (UnicodeError, json.JSONDecodeError):
                raise HostUnavailable("native usage record is malformed") from None
            if row.get("type") != "token_usage_record" or not isinstance(row.get("payload"), dict):
                raise HostUnavailable("native usage record is malformed")
            value = row["payload"]
            turn = value.get("turn_id")
            response = value.get("response_id")
            if value.get("thread_id") != thread_id or turn not in turn_ids or not isinstance(response, str) or not response:
                raise HostUnavailable("native usage record belongs to another thread or turn")
            records[turn].append({"response": response, "usage": counts(value.get("usage")),
                                  "turnTotal": counts(value.get("turn_token_usage"))})
    return records


def collect(parent_thread_id: str, native_id: str, *, command: str = "codex",
            codex_home: pathlib.Path | None = None) -> dict[str, object]:
    if not UUID.fullmatch(parent_thread_id) or not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", native_id):
        raise HostUnavailable("native parent or child identity is unavailable")
    home = codex_home or pathlib.Path(os.environ.get("CODEX_HOME", pathlib.Path.home() / ".codex"))
    with AppServer(command) as server:
        children = page(server, "thread/list", 100, {"parentThreadId": parent_thread_id,
                                                     "sourceKinds": ["subAgent", "subAgentThreadSpawn"]})
        matches = []
        for child in children:
            child_id = child.get("id")
            if not isinstance(child_id, str) or not UUID.fullmatch(child_id):
                continue
            thread = server.request(200, "thread/read", {"threadId": child_id, "includeTurns": False}).get("thread")
            if not isinstance(thread, dict):
                raise HostUnavailable("native child thread metadata is unavailable")
            spawn = ((thread.get("source") or {}).get("subAgent") or {}).get("thread_spawn")
            if (thread.get("parentThreadId") == parent_thread_id and isinstance(spawn, dict) and
                    spawn.get("parent_thread_id") == parent_thread_id and
                    isinstance(spawn.get("agent_path"), str) and
                    spawn["agent_path"].endswith("/" + native_id)):
                matches.append(thread)
        if len(matches) != 1:
            raise HostUnavailable("native child identity is missing or ambiguous")
        thread = matches[0]
        thread_id = thread["id"]
        turns = page(server, "thread/turns/list", 300, {"threadId": thread_id,
                                                       "sortDirection": "asc", "itemsView": "notLoaded"})
    if not turns:
        return {"threadId": thread_id, "turns": [], "allTurnIds": [], "complete": False, "model": thread.get("model"),
                "effort": thread.get("reasoningEffort")}
    ids = [turn.get("id") for turn in turns]
    if len(set(ids)) != len(ids) or any(not isinstance(value, str) or not UUID.fullmatch(value) for value in ids):
        raise HostUnavailable("native turn identities are malformed")
    records = rollout_usage(str(thread.get("path")), thread_id, set(ids), home)
    observations = []
    complete = True
    for sequence, turn in enumerate(turns, 1):
        if turn.get("status") not in {"completed", "failed", "interrupted"}:
            complete = False
            continue
        rows = records.get(turn["id"], [])
        if not rows:
            complete = False
            continue
        responses = {}
        for row in rows:
            # A later native record may correct the same response. Its final
            # counters replace the earlier observation, never add to it.
            responses[row["response"]] = row["usage"]
        total = {key: sum(value[key] for value in responses.values()) for key in COUNTS}
        if total != rows[-1]["turnTotal"]:
            complete = False
            continue
        observations.append({"turnId": turn["id"], "turnSequence": sequence, "usage": total})
    return {"threadId": thread_id, "turns": observations, "allTurnIds": ids, "complete": complete,
            "model": thread.get("model"), "effort": thread.get("reasoningEffort")}
