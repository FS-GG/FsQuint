#!/usr/bin/env python3
"""Check the pinned public package from an isolated, locked consumer restore."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def main():
    example = Path(__file__).resolve().parent
    repo = example.parents[1]
    baseline = json.loads((example / "baseline.json").read_text())
    with tempfile.TemporaryDirectory(prefix="fsquint-automata-") as scratch:
        root = Path(scratch)
        names = ["AutomataReplay.fsproj", "baseline.json", "packages.lock.json"]
        names += [p.name for pattern in ("*.fs", "*.qnt") for p in example.glob(pattern)]
        for name in names:
            shutil.copy2(example / name, root / name)
        shutil.copytree(example / "fixtures", root / "fixtures")
        shutil.copy2(repo / "examples/Shared/PureReplay.fs", root / "PureReplay.fs")
        for name in ("global.json", "NuGet.Config"):
            shutil.copy2(repo / name, root / name)
        env = dict(os.environ, NUGET_PACKAGES=str(root / "packages"),
                   NUGET_HTTP_CACHE_PATH=str(root / "http"))
        restore = ["dotnet", "restore", "AutomataReplay.fsproj",
                   "--source", str(repo / "artifacts/packages"),
                   "--source", "https://api.nuget.org/v3/index.json"]
        # FsQuint is the package just built from this candidate, including its source revision.
        # Refresh only that content identity; all third-party locks must stay byte-for-byte equal.
        locked = json.loads((root / "packages.lock.json").read_text())
        subprocess.run([*restore, "--force-evaluate"], cwd=root, env=env, check=True)
        refreshed = json.loads((root / "packages.lock.json").read_text())
        locked["dependencies"]["net10.0"]["FsQuint"]["contentHash"] = refreshed["dependencies"]["net10.0"]["FsQuint"]["contentHash"]
        if locked != refreshed:
            raise RuntimeError("Dependency lock changed beyond the candidate FsQuint content hash")
        candidate = repo / "artifacts/packages/FsQuint.0.1.1.nupkg"
        restored = root / "packages/fsquint/0.1.1/fsquint.0.1.1.nupkg"
        if restored.read_bytes() != candidate.read_bytes():
            raise RuntimeError("Consumer did not restore the exact candidate FsQuint package")
        subprocess.run([*restore, "--locked-mode"], cwd=root, env=env, check=True)
        package_id = baseline["packageId"].lower()
        version = baseline["packageVersion"]
        archive = root / "packages" / package_id / version / f"{package_id}.{version}.nupkg"
        if hashlib.sha256(archive.read_bytes()).hexdigest() != baseline["packageSha256"]:
            raise RuntimeError("Restored Automata package differs from the recorded archive")
        with zipfile.ZipFile(archive) as package:
            nuspec = next(name for name in package.namelist() if name.endswith(".nuspec"))
            metadata = ET.fromstring(package.read(nuspec)).find("{*}metadata")
            expected = {"id": baseline["packageId"], "version": version,
                        "license": baseline["licenseExpression"]}
            for key, value in expected.items():
                if metadata.find(f"{{*}}{key}").text != value:
                    raise RuntimeError(f"Unexpected package {key}")
            source = metadata.find("{*}repository")
            if (source.get("url") != baseline["repository"] or
                    source.get("commit") != baseline["repositoryCommit"]):
                raise RuntimeError("Unexpected package source provenance")
        execution = subprocess.run(["dotnet", "run", "--project", "AutomataReplay.fsproj",
                                   "-c", "Release", "--no-restore"], cwd=root, env=env,
                                   check=True, capture_output=True, text=True, timeout=120)
        print(execution.stdout, end="")
        failures = [json.loads(line.removeprefix("DIVERGENCE ")) for line in execution.stdout.splitlines()
                    if line.startswith("DIVERGENCE ")]
        if len(failures) != 4:
            raise RuntimeError("Missing approval mutation diagnostics")
        artifact = repo / "artifacts/automata-replay.json"
        artifact.parent.mkdir(exist_ok=True)
        artifact.write_text(json.dumps({"schema": "fsquint.replay-evidence/1", "outcome": "passed",
                                        "mutationDiagnostics": failures}, indent=2) + "\n")
    print("PASS: isolated locked restore and package archive/source/license provenance.")
    quint = os.environ.get("QUINT_BIN")
    if quint:
        if hashlib.sha256(Path(quint).read_bytes()).hexdigest() != baseline["quintSha256"]:
            raise RuntimeError("Quint executable differs from the qualified pin")
        subprocess.run(["python3", "regenerate.py"], cwd=example, check=True, timeout=180)
        for args in (["typecheck", "approval.qnt"],
                     ["typecheck", "turnstile.qnt"],
                     ["test", "turnstile_test.qnt", "--seed", "42"],
                     ["typecheck", "resolver.qnt"],
                     ["test", "resolver_test.qnt", "--seed", "42"],
                     ["run", "resolver.qnt", "--invariant", "safety", "--seed", "42",
                      "--max-samples", "1000", "--max-steps", "10", "--verbosity", "1"],
                     ["test", "approval_test.qnt", "--seed", "42"],
                     ["run", "approval.qnt", "--invariant", "safety", "--seed", "42",
                      "--max-samples", "1000", "--max-steps", "30", "--verbosity", "1"],
                     ["run", "approval.qnt", "--invariant", "transitionSafety", "--seed", "42",
                      "--max-samples", "1000", "--max-steps", "30", "--verbosity", "1"]):
            subprocess.run([quint, *args], cwd=example, check=True, timeout=120)
    else:
        print("Model execution not requested; offline fixture replay qualified above.")


if __name__ == "__main__":
    main()
