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
        for name in ("AutomataReplay.fsproj", "Program.fs", "packages.lock.json"):
            shutil.copy2(example / name, root / name)
        for name in ("global.json", "NuGet.Config"):
            shutil.copy2(repo / name, root / name)
        env = dict(os.environ, NUGET_PACKAGES=str(root / "packages"),
                   NUGET_HTTP_CACHE_PATH=str(root / "http"))
        subprocess.run(["dotnet", "restore", "AutomataReplay.fsproj", "--locked-mode"],
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


if __name__ == "__main__":
    main()
