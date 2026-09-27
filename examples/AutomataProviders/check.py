#!/usr/bin/env python3
"""Run pinned upstream provider contracts against owned native resources; never a supplied DB."""
import argparse
import hashlib
import json
import os
import pwd
from pathlib import Path
import re
import selectors
import signal
import subprocess
import tempfile
import time
import zipfile
import xml.etree.ElementTree as ET

SOURCE = "03c3282f25888a32d36e53fa708f0342c328ccfc"
ROOT = Path(__file__).resolve().parents[2]


def run(args, *, cwd=None, env=None, timeout=300):
    with subprocess.Popen([str(x) for x in args], cwd=cwd, env=env, start_new_session=True,
                          text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT) as process:
        try:
            log, _ = process.communicate(timeout=timeout)
            if process.returncode:
                raise subprocess.CalledProcessError(process.returncode, args, output=log)
            return log
        finally:
            # Also clean up test workers if their parent timed out or exited early.
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass


def suite(source, provider, output, env, selected=None):
    start = time.monotonic()
    command = ["dotnet", "run", "--project", f"tests/ByzantineSystems.Automata.Storage.{provider}.Tests",
               "-c", "Release", "--", "--summary", "--sequenced"]
    label = provider
    if selected:
        command += ["--filter-test-case", selected]
        label += "-correction"
    try:
        with tempfile.TemporaryDirectory(prefix="fsquint-suite-") as temporary:
            log = run(command, cwd=source, env={**env, "TMPDIR": temporary})
    except subprocess.CalledProcessError as error:
        (output / f"{label}.log").write_text(error.stdout)
        raise
    (output / f"{label}.log").write_text(log)
    plain = re.sub(r"\x1b\[[0-9;?]*[A-Za-z]", "", log)
    counts = re.search(r"(\d+) tests run.*?(\d+) passed,\s*(\d+) ignored,\s*(\d+) failed,\s*(\d+) errored", plain)
    expected = 1 if selected else {"Sqlite": 118, "Postgres": 152}[provider]
    if not counts or int(counts[1]) != expected or counts[1] != counts[2] or any(int(counts[i]) for i in (3, 4, 5)):
        raise RuntimeError(f"{provider}: missing, ignored or failing provider contracts; inspect log")
    return {"tests": int(counts[1]), "passed": int(counts[2]), "ignored": 0,
            "elapsedSeconds": round(time.monotonic() - start, 3), "command": command,
            "logSha256": hashlib.sha256(log.encode()).hexdigest()}


def crash(provider, connection, restart=lambda: None):
    """Kill a provider consumer after commit 1 / claim 2, recover in a different process."""
    dll = Path(__file__).parent / "bin/Release/net10.0/AutomataProviders.dll"
    start = time.monotonic()
    command = ["dotnet", str(dll), provider, "seed", str(connection)]
    with subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, start_new_session=True) as worker:
        try:
            with selectors.DefaultSelector() as selector:
                selector.register(worker.stdout, selectors.EVENT_READ)
                if not selector.select(timeout=30):
                    raise RuntimeError("provider never reached the crash boundary")
                line = worker.stdout.readline().strip()
            match = re.fullmatch(r"READY (\d+) (\d+)", line)
            if not match:
                raise RuntimeError(f"bad crash boundary: {line}")
        finally:
            try:
                os.killpg(worker.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            worker.wait(timeout=10)
    restart()
    # Actual provider clocks expire the one-second lease; no clock injection here.
    time.sleep(1.1)
    result = run(["dotnet", dll, provider, "recover", connection, match[1], match[2]], timeout=30).strip()
    if result != "RECOVERED state=2 epoch=2 actions=2":
        raise RuntimeError(result)
    return {"boundary": "commit-first-claim-second", "workerExit": worker.returncode,
            "recovered": {"state": 2, "epoch": 2, "actions": 2, "newFenceToken": True},
            "serverImmediateRestart": provider == "postgres",
            "elapsedSeconds": round(time.monotonic() - start, 3)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--automata-source", type=Path, required=True)
    parser.add_argument("--postgres-bin", type=Path, required=True)
    args = parser.parse_args()
    source = args.automata_source.resolve()
    pg = args.postgres_bin.resolve()
    if run(["git", "rev-parse", "HEAD"], cwd=source).strip() != SOURCE:
        raise RuntimeError("wrong Automata source")
    if run(["git", "status", "--porcelain", "--untracked-files=normal"], cwd=source).strip():
        raise RuntimeError("modified source")
    output = ROOT / "artifacts" / "automata-providers"
    output.mkdir(parents=True, exist_ok=True)
    (output / "report.json").write_text(json.dumps({"status": "incomplete", "sourceCommit": SOURCE}) + "\n")
    env = os.environ.copy()
    env.pop("AUTOMATA_TEST_DB", None)
    env.pop("AUTOMATA_TEST_CRON_DB", None)
    report = {"schemaVersion": 1, "sourceCommit": SOURCE,
              "sdk": run(["dotnet", "--version"]).strip(),
              "postgres": run([pg / "postgres", "--version"]).strip()}
    if report["postgres"] != "postgres (PostgreSQL) 19beta3":
        raise RuntimeError("unqualified PostgreSQL version; use provision.py")
    run(["dotnet", "restore", Path(__file__).parent, "--locked-mode"])
    run(["dotnet", "build", Path(__file__).parent, "-c", "Release", "--no-restore"])
    cache = Path(run(["dotnet", "nuget", "locals", "global-packages", "--list"]).strip().split(": ", 1)[1])
    packages = []
    for provider in ("sqlite", "postgres"):
        name = f"byzantinesystems.automata.storage.{provider}"
        archive = cache / name / "0.5.0" / f"{name}.0.5.0.nupkg"
        with zipfile.ZipFile(archive) as package:
            metadata = ET.fromstring(package.read(next(n for n in package.namelist() if n.endswith(".nuspec"))))
            repo = next(node for node in metadata.iter() if node.tag.endswith("}repository"))
            if repo.attrib.get("commit") != SOURCE:
                raise RuntimeError("provider package/source commit mismatch")
        packages.append({"id": name, "version": "0.5.0", "sourceCommit": SOURCE,
                         "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()})
    report["packages"] = packages
    report["timeoutsSeconds"] = {"upstreamSuite": 300, "crashBoundary": 30, "recovery": 30}
    report["sqlite"] = suite(source, "Sqlite", output, env)
    with tempfile.TemporaryDirectory(prefix="fsquint-sqlite-") as scratch:
        report["sqlite"]["crashRecovery"] = crash("sqlite", Path(scratch) / "store.db")
    with tempfile.TemporaryDirectory(prefix="fsquint-pg-") as scratch:
        scratch = Path(scratch)
        data = scratch / "db"
        socket = scratch / "socket"
        socket.mkdir(mode=0o700)
        run([pg / "initdb", "-D", data, "--auth=trust", "--no-locale"])
        with (data / "postgresql.conf").open("a") as config:
            config.write("\nshared_preload_libraries = 'pg_cron'\ncron.database_name = 'postgres'\ncron.use_background_workers = on\n")
        try:
            run([pg / "pg_ctl", "-D", data, "-l", scratch / "server.log", "-o",
                 f"-k {socket} -h '' -p 55439", "-w", "start"])
            admin = pwd.getpwuid(os.getuid()).pw_name
            run([pg / "psql", "-h", socket, "-p", "55439", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c",
                 "CREATE ROLE automata_app LOGIN; GRANT CREATE ON DATABASE postgres TO automata_app; "
                 "GRANT CREATE ON SCHEMA public TO automata_app; "
                 "CREATE EXTENSION btree_gist; CREATE EXTENSION pgmq; CREATE EXTENSION pg_cron; "
                 "GRANT ALL ON SCHEMA pgmq TO automata_app; "
                 "GRANT ALL ON ALL TABLES IN SCHEMA pgmq TO automata_app; "
                 "GRANT ALL ON ALL SEQUENCES IN SCHEMA pgmq TO automata_app;"])
            env["AUTOMATA_TEST_DB"] = f"Host={socket};Port=55439;Database=postgres;Username=automata_app;Pooling=false"
            env["AUTOMATA_TEST_CRON_DB"] = f"Host={socket};Port=55439;Database=postgres;Username={admin};Pooling=false"
            report["postgresql"] = suite(source, "Postgres", output, env)
            def restart():
                run([pg / "pg_ctl", "-D", data, "-m", "immediate", "-w", "stop"])
                run([pg / "pg_ctl", "-D", data, "-l", scratch / "server.log", "-o",
                     f"-k {socket} -h '' -p 55439", "-w", "start"])
            report["postgresql"]["crashRecovery"] = crash("postgres", env["AUTOMATA_TEST_DB"], restart)
            correction = suite(source, "Postgres", output, env,
                               "a correction rewrites the timeline and keeps what was believed before it")
            def beliefs():
                facts = {}
                for table in ("instance_state", "instance_state_history", "transition", "command_correction"):
                    rows = run([pg / "psql", "-h", socket, "-p", "55439", "-d", "postgres", "-At", "-c",
                                f"SELECT row_to_json(t)::text FROM fsm.{table} t ORDER BY row_to_json(t)::text;"])
                    facts[table] = [json.loads(row) for row in rows.splitlines()]
                    if not facts[table]:
                        raise RuntimeError(f"empty correction evidence: {table}")
                return facts
            before = beliefs()
            restart()
            if beliefs() != before:
                raise RuntimeError("corrected history changed across immediate PostgreSQL restart")
            correction["persistedRows"] = {table: len(rows) for table, rows in before.items()}
            correction["rowsSha256"] = hashlib.sha256(json.dumps(before, sort_keys=True).encode()).hexdigest()
            (output / "corrected-history.json").write_text(json.dumps(before, indent=2) + "\n")
            report["postgresql"]["correctionRecovery"] = correction
        finally:
            if (data / "postmaster.pid").exists():
                run([pg / "pg_ctl", "-D", data, "-m", "immediate", "-w", "stop"])
            (output / "postgres-server.log").write_text((scratch / "server.log").read_text())
    report["status"] = "passed"
    report["harnessSha256"] = {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest()
                               for name in ("Program.fs", "check.py", "provision.py", "packages.lock.json")}
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        print(error.stdout)
        raise
