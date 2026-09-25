#!/usr/bin/env python3
"""Offline, independent characterization over locally packed FsQuint .nupkg bytes."""

import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FSX = ROOT / "eng/packed-archive-fingerprint.fsx"
passed = 0


def fsharp(*args):
    return subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", *map(str, args)],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )


def python_payloads(path):
    # The exact payload projection in eng/readback.py; that script's top level
    # performs feed requests, so it cannot be imported into an offline fixture.
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate ZIP entry")
        return {
            name: hashlib.sha256(archive.read(name)).hexdigest()
            for name in names
            if name != ".signature.p7s"
        }


def rewrite(source, target, *, rename=None, change=None, mode=None, add=None):
    with zipfile.ZipFile(source) as original, zipfile.ZipFile(target, "w") as output:
        for info in original.infolist():
            item = copy.copy(info)
            if info.filename == rename:
                item.filename = "renamed/" + info.filename
            if info.filename == mode:
                item.external_attr = 0o100755 << 16
            body = original.read(info)
            if info.filename == change:
                body += b"changed"
            output.writestr(item, body)
        if add:
            output.writestr(add, b"feed-added-signature")


def check(package):
    global passed
    with zipfile.ZipFile(package) as archive:
        records = [
            {
                "Name": info.filename,
                "Sha256": hashlib.sha256(archive.read(info)).hexdigest(),
                "UnixMode": (info.external_attr >> 16) & 0xFFFF,
            }
            for info in archive.infolist()
            if info.filename != ".signature.p7s"
        ]
        records.sort(key=lambda item: item["Name"])
        specs = [info for info in archive.infolist() if info.filename.endswith(".nuspec")]
        assert len(specs) == 1, (package, "expected one package identity manifest")
        assert package.name.startswith(specs[0].filename.removesuffix(".nuspec") + "."), (
            package,
            specs[0].filename,
        )
        ET.fromstring(archive.read(specs[0]))
        target_member = next(info.filename for info in archive.infolist() if info.filename == "README.md")
        spec_member = specs[0].filename

    result = fsharp("inspect", package)
    assert result.returncode == 0, (package, result.stderr)
    assert json.loads(result.stdout) == records, (package, result.stdout)
    passed += 1

    with tempfile.TemporaryDirectory(prefix="fsquint-packed-characterization-") as folder:
        temporary = Path(folder)

        def expect(label, expected, **changes):
            global passed
            served = temporary / (label + ".nupkg")
            rewrite(package, served, **changes)
            actual = fsharp("compare", package, served)
            if expected is None:
                assert actual.returncode == 0 and actual.stdout.strip() == "match", (label, actual.stderr)
                assert python_payloads(package) == python_payloads(served), label
            else:
                assert actual.returncode == 2 and expected in actual.stderr, (label, actual.stderr)
            passed += 1
            return served

        expect("repacked-copy", None)
        expect("root-signature", None, add=".signature.p7s")
        expect("nested-signature", "archive members differ", add="nested/.signature.p7s")
        expect("wrong-member", "archive members differ", rename=target_member)
        expect("wrong-digest", "digest differs", change=target_member)
        expect("wrong-nuspec", "digest differs", change=spec_member)
        changed_mode = expect("wrong-mode", None, mode=target_member)
        # The receiver contract compares names and payload digests. Mode is
        # retained in inspection evidence, but not cross-feed equality.
        assert python_payloads(package) == python_payloads(changed_mode)
        passed += 1

    print(f"{package.name}: {len(records)} exact payload members characterized")


def main():
    packages = [Path(value).resolve() for value in sys.argv[1:]]
    if len(packages) != 2 or any(not path.is_file() or path.suffix != ".nupkg" for path in packages):
        raise SystemExit("usage: test-packed-archive-fingerprint.py FsQuint.nupkg FsQuint.Tooling.nupkg")
    for package in packages:
        check(package)
    print(f"packed archive fingerprint: {passed} offline controls passed")


if __name__ == "__main__":
    main()
