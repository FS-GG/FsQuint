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
        for name in ("AutomataReplay.fsproj", "Program.fs", "Approval.fs", "Conformance.fs",
                     "approval.qnt", "approval_test.qnt", "baseline.json", "packages.lock.json"):
            shutil.copy2(example / name, root / name)
        shutil.copytree(example / "fixtures", root / "fixtures")
        for name in ("global.json", "NuGet.Config"):
            shutil.copy2(repo / name, root / name)
        env = dict(os.environ, NUGET_PACKAGES=str(root / "packages"),
                   NUGET_HTTP_CACHE_PATH=str(root / "http"))
        subprocess.run(["dotnet", "restore", "AutomataReplay.fsproj", "--locked-mode",
                        "--source", str(repo / "artifacts/packages"),
                        "--source", "https://api.nuget.org/v3/index.json"],
                       cwd=root, env=env, check=True)
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
        subprocess.run(["dotnet", "run", "--project", "AutomataReplay.fsproj",
                        "-c", "Release", "--no-restore"], cwd=root, env=env, check=True)
    print("PASS: isolated locked restore and package archive/source/license provenance.")
    quint = os.environ.get("QUINT_BIN")
    if quint:
        if hashlib.sha256(Path(quint).read_bytes()).hexdigest() != baseline["quintSha256"]:
            raise RuntimeError("Quint executable differs from the qualified pin")
        for args in (["typecheck", "approval.qnt"],
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
