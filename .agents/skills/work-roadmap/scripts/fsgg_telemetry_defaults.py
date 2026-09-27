"""Private host configuration and assignment helpers for FS-GG telemetry adapters."""

from __future__ import annotations

import json
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import urllib.parse
from dataclasses import dataclass


CONFIG_SCHEMA = "fsgg.telemetry.host-config/1"
WORKSPACE_CONFIG_SCHEMA = "fsgg.telemetry.workspace-config/1"
RUNTIME_ASSIGNMENT_SCHEMA = "fsgg.telemetry.codex-assignment/1"
CI_ASSIGNMENT_SCHEMA = "fsgg.telemetry.ci-assignment/1"
IDENTITY_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,199}$")
GITHUB_OWNER_RE = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}[A-Za-z0-9])?$")
GITHUB_REPOSITORY_RE = re.compile(r"^(?!\.{1,2}$)[A-Za-z0-9_.-]{1,100}$")
GITHUB_SCP_RE = re.compile(r"^git@github\.com:([^/]+)/([^/]+)$", re.IGNORECASE)
sys.dont_write_bytecode = True


class ConfigurationError(RuntimeError):
    pass


@dataclass(frozen=True)
class HostConfig:
    path: pathlib.Path
    store_root: pathlib.Path
    engine: str
    repository: str | None = None
    workspace: bool = False
    producer: str | None = None
    binding_digest: str | None = None
    credential_reference: str | None = None


def canonical_github_repository(value: str) -> str:
    """Return OWNER/REPO for one credential-free canonical GitHub remote."""
    if not isinstance(value, str) or not value or value != value.strip() or any(character.isspace() for character in value):
        raise ConfigurationError("git origin is not a canonical GitHub repository URL")
    owner: str
    repository: str
    scp = GITHUB_SCP_RE.fullmatch(value)
    if scp:
        owner, repository = scp.groups()
    else:
        try:
            parsed = urllib.parse.urlsplit(value)
            port = parsed.port
        except ValueError as error:
            raise ConfigurationError("git origin is not a canonical GitHub repository URL") from error
        if parsed.query or parsed.fragment or port is not None or parsed.hostname is None or parsed.hostname.lower() != "github.com":
            raise ConfigurationError("git origin is not a canonical GitHub repository URL")
        if parsed.scheme == "https":
            if parsed.username is not None or parsed.password is not None:
                raise ConfigurationError("git origin is not a canonical GitHub repository URL")
        elif parsed.scheme == "ssh":
            if parsed.username != "git" or parsed.password is not None:
                raise ConfigurationError("git origin is not a canonical GitHub repository URL")
        else:
            raise ConfigurationError("git origin is not a canonical GitHub repository URL")
        parts = parsed.path.removeprefix("/").split("/")
        if len(parts) != 2:
            raise ConfigurationError("git origin is not a canonical GitHub repository URL")
        owner, repository = parts
    if repository.endswith(".git"):
        repository = repository[:-4]
    if not GITHUB_OWNER_RE.fullmatch(owner) or not GITHUB_REPOSITORY_RE.fullmatch(repository):
        raise ConfigurationError("git origin is not a canonical GitHub repository URL")
    return f"{owner}/{repository}"


def discover_checkout_repository(cwd: pathlib.Path | None = None) -> str:
    """Resolve one local origin without invoking a shell or exposing its raw value."""
    try:
        completed = subprocess.run(
            ["git", "config", "--local", "--get-all", "remote.origin.url"],
            cwd=cwd,
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        raise ConfigurationError("telemetry repository discovery unavailable") from error
    if completed.returncode != 0 or len(completed.stdout.encode()) > 4096:
        raise ConfigurationError("telemetry repository discovery requires one local origin")
    origins = completed.stdout.splitlines()
    if len(origins) != 1:
        raise ConfigurationError("telemetry repository discovery requires one local origin")
    return canonical_github_repository(origins[0])


def workspace_repository() -> str:
    for name in ("FSGG_TELEMETRY_REPOSITORY", "GITHUB_REPOSITORY"):
        if name in os.environ:
            value = os.environ[name]
            if not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}[A-Za-z0-9])?/(?!\.{1,2}$)[A-Za-z0-9_.-]{1,100}", value):
                raise ConfigurationError(f"{name} is not a bounded GitHub repository identity")
            return value
    return discover_checkout_repository()


def workspace_credential_reference(value: dict[str, object], binding: dict[str, object], repository: str) -> str:
    matches = []
    for association in value.get("associations", []):
        if (isinstance(association, dict) and association.get("producerId") == binding.get("producerId") and
                isinstance(association.get("repositories"), list) and repository in association["repositories"]):
            destination = association.get("destination")
            if isinstance(destination, dict):
                matches.append(destination.get("credentialReference"))
    if len(matches) != 1 or not isinstance(matches[0], str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]{0,63}", matches[0]):
        raise ConfigurationError("telemetry workspace credential association is missing or ambiguous")
    return matches[0]


def credential_environment_name(reference: str) -> str:
    return "FSGG_TELEMETRY_CREDENTIAL_" + reference.replace("-", "_").upper()


def workspace_mutation_command(config: HostConfig, command: list[str]) -> list[str]:
    """Use the owner-controlled credential client when the selected secret is not already loaded."""
    if not config.workspace:
        return command
    reference = config.credential_reference
    if not reference:
        raise ConfigurationError("telemetry workspace credential association is unavailable")
    if os.environ.get(credential_environment_name(reference)):
        return command
    helper_name = "fdev-telemetry"
    helper_value = shutil.which(helper_name)
    if not helper_value:
        raise ConfigurationError("telemetry credential client is unavailable")
    helper = pathlib.Path(helper_value)
    try:
        details = helper.lstat()
    except OSError as error:
        raise ConfigurationError("telemetry credential client is unavailable") from error
    if (helper.is_symlink() or not stat.S_ISREG(details.st_mode) or
            (hasattr(os, "getuid") and details.st_uid != os.getuid()) or details.st_mode & 0o022):
        raise ConfigurationError("telemetry credential client is not an owner-controlled executable")
    return [str(helper.resolve()), "exec", *command]


def candidate_config_paths(explicit: str | None = None) -> list[pathlib.Path]:
    if explicit:
        return [pathlib.Path(explicit)]
    configured = os.environ.get("FSGG_TELEMETRY_CONFIG")
    if configured:
        return [pathlib.Path(configured)]
    xdg = os.environ.get("XDG_CONFIG_HOME")
    base = pathlib.Path(xdg) if xdg else pathlib.Path.home() / ".config"
    return [base / "fs-gg" / "telemetry.json"]


def discover_config(explicit: str | None = None) -> HostConfig | None:
    path = candidate_config_paths(explicit)[0]
    if not path.exists() and explicit is None and not os.environ.get("FSGG_TELEMETRY_CONFIG"):
        return None
    try:
        if not path.is_absolute():
            raise ConfigurationError("telemetry config path must be absolute")
        info = path.lstat()
        if path.is_symlink() or not path.is_file():
            raise ConfigurationError("telemetry config must be a regular non-symlink file")
        if os.name != "nt" and (info.st_mode & 0o777) != 0o600:
            raise ConfigurationError("telemetry config permissions must be 0600")
        if info.st_size > 65536:
            raise ConfigurationError("telemetry config exceeds 64 KiB")
        def reject_duplicates(pairs):
            if len(pairs) != len({name for name, _ in pairs}):
                raise ConfigurationError("telemetry config contains duplicate properties")
            return dict(pairs)
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=reject_duplicates)
        if not isinstance(value, dict):
            raise ConfigurationError("telemetry config has an invalid closed shape")
        if value.get("schema") == WORKSPACE_CONFIG_SCHEMA:
            if set(value) != {"schema", "engine", "associations", "retiredAssociations"}:
                raise ConfigurationError("telemetry workspace config has an invalid closed shape")
            repository = workspace_repository()
            engine = value["engine"]
            if not isinstance(engine, str) or not engine or "/" in engine or "\\" in engine:
                raise ConfigurationError("telemetry engine must be an executable name")
            completed = subprocess.run([engine, "telemetry", "workspace", "binding", "--config", str(path),
                                        "--repository", repository], capture_output=True, text=True, timeout=20, check=False)
            if completed.returncode != 0 or len(completed.stdout.encode()) > 4096:
                raise ConfigurationError(completed.stderr.strip() or "telemetry workspace binding failed")
            binding = json.loads(completed.stdout, object_pairs_hook=reject_duplicates)
            fields = {"schema", "configPath", "repository", "producerId", "bindingDigest", "destination", "privateStateRoot"}
            if (not isinstance(binding, dict) or set(binding) != fields or
                    binding.get("schema") != "fsgg.telemetry.workspace-binding/1" or
                    binding.get("repository") != repository):
                raise ConfigurationError("telemetry workspace binding result is invalid")
            state_root = pathlib.Path(binding["privateStateRoot"])
            credential_reference = workspace_credential_reference(value, binding, repository)
            return HostConfig(path.resolve(), state_root, engine, binding["repository"], True,
                              binding["producerId"], binding["bindingDigest"], credential_reference)
        if list(value) != ["schema", "storeRoot", "engine"]:
            raise ConfigurationError("telemetry config has an invalid closed shape")
        if value.get("schema") != CONFIG_SCHEMA:
            raise ConfigurationError("telemetry config schema is unsupported")
        store_root = pathlib.Path(value["storeRoot"])
        if not store_root.is_absolute():
            raise ConfigurationError("telemetry store root must be absolute")
        engine = value["engine"]
        if not isinstance(engine, str) or not engine or os.path.sep in engine:
            raise ConfigurationError("telemetry engine must be an executable name")
        return HostConfig(path.resolve(), store_root, engine)
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError, subprocess.SubprocessError) as error:
        if isinstance(error, ConfigurationError):
            raise
        raise ConfigurationError(f"telemetry config is unreadable: {error}") from error


def validate_identity(name: str, value: str | None, *, optional: bool = False) -> str | None:
    if value is None and optional:
        return None
    if not isinstance(value, str) or not IDENTITY_RE.fullmatch(value):
        raise ConfigurationError(f"{name} must be a bounded stable identity")
    return value


def validate_workspace(config: HostConfig) -> None:
    if not config.workspace:
        return
    try:
        completed = subprocess.run(
            [config.engine, "telemetry", "workspace", "status", "--config", str(config.path),
             "--repository", str(config.repository)], capture_output=True, text=True, timeout=20, check=False,
        )
    except (OSError, subprocess.SubprocessError) as error:
        raise ConfigurationError(f"telemetry workspace validation unavailable: {error}") from error
    if completed.returncode != 0:
        raise ConfigurationError(completed.stderr.strip() or "telemetry workspace validation failed")


def assignment_payload(
    schema: str,
    *,
    feature: str,
    item: str,
    attempt: str,
    parent_attempt: str | None,
    producer: str,
) -> dict[str, object]:
    if schema not in {RUNTIME_ASSIGNMENT_SCHEMA, CI_ASSIGNMENT_SCHEMA}:
        raise ConfigurationError("assignment schema is unsupported")
    return {
        "schema": schema,
        "featureId": validate_identity("feature", feature),
        "itemId": validate_identity("item", item),
        "attemptId": validate_identity("attempt", attempt),
        "parentAttemptId": validate_identity("parent attempt", parent_attempt, optional=True),
        "producerStream": validate_identity("producer", producer),
    }


def write_private_json(directory: pathlib.Path, prefix: str, payload: dict[str, object]) -> pathlib.Path:
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    if os.name != "nt" and (directory.stat().st_mode & 0o077):
        raise ConfigurationError("private telemetry directory permissions must exclude group and other")
    descriptor, temporary = tempfile.mkstemp(prefix=prefix + ".", suffix=".tmp", dir=directory)
    target = directory / f"{prefix}.json"
    try:
        os.fchmod(descriptor, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, separators=(",", ":"), ensure_ascii=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, target)
        directory_fd = os.open(directory, os.O_RDONLY)
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
        return target
    except Exception:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass
        raise


def create_assignment(
    config: HostConfig,
    schema: str,
    *,
    feature: str,
    item: str,
    attempt: str,
    parent_attempt: str | None,
    producer: str,
) -> pathlib.Path:
    validate_workspace(config)
    payload = assignment_payload(
        schema,
        feature=feature,
        item=item,
        attempt=attempt,
        parent_attempt=parent_attempt,
        producer=producer,
    )
    safe_name = re.sub(r"[^A-Za-z0-9._-]", "_", f"{producer}-{item}-{attempt}")[:180]
    return write_private_json(config.store_root / "assignments", safe_name, payload)
