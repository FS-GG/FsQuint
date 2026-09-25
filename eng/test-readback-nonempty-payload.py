#!/usr/bin/env python3
"""Offline no-payload archive controls for Python and F# readers."""

import ast
import hashlib
from pathlib import Path
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parents[1]
READBACK = ROOT / "eng/readback.py"
FSX = ROOT / "eng/packed-archive-fingerprint.fsx"


def python_payloads(path):
    # Importing readback.py would fetch feeds and write a receipt.
    tree = ast.parse(READBACK.read_text())
    definition = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "payloads")
    scope = {"hashlib": hashlib, "zipfile": zipfile}
    exec(compile(ast.Module(body=[definition], type_ignores=[]), str(READBACK), "exec"), scope)
    return scope["payloads"](path)


def make_archive(path, members):
    with zipfile.ZipFile(path, "w") as packed:
        for name, body in members:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = (0o040755 if name.endswith("/") else 0o100644) << 16
            packed.writestr(info, body)
    return path


def fsharp(path, accept):
    result = subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", "inspect", str(path)],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )
    if accept:
        assert result.returncode == 0 and "README.md" in result.stdout, result.stderr
    else:
        assert result.returncode == 2 and "no payload members" in result.stderr, result.stderr


def main():
    with tempfile.TemporaryDirectory(prefix="fsquint-nonempty-payload-") as folder:
        temp = Path(folder)
        good = [
            ("file", [("README.md", b"payload")]),
            ("file-signature", [("README.md", b"payload"), (".signature.p7s", b"feed")]),
            ("directory-file", [("docs/", b""), ("README.md", b"payload")]),
        ]
        for label, members in good:
            path = make_archive(temp / f"{label}.nupkg", members)
            assert "README.md" in python_payloads(path)
            fsharp(path, True)

        bad = [
            ("empty", []),
            ("signature", [(".signature.p7s", b"feed")]),
            ("directory", [("docs/", b"")]),
            ("directory-signature", [("docs/", b""), (".signature.p7s", b"feed")]),
        ]
        missed = []
        for label, members in bad:
            path = make_archive(temp / f"{label}.nupkg", members)
            fsharp(path, False)
            try:
                python_payloads(path)
            except ValueError as error:
                assert "no payload members" in str(error), str(error)
            else:
                missed.append(f"Python accepted {label}.nupkg")
        assert not missed, "\n".join(missed)

    print("readback nonempty payload: 7 offline controls passed")


if __name__ == "__main__":
    main()
