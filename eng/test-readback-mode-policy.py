#!/usr/bin/env python3
"""Offline comparison of the Python readback projection and F# candidate."""

import ast
import hashlib
from pathlib import Path
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parents[1]
READBACK = ROOT / "eng/readback.py"
FSX = ROOT / "eng/packed-archive-fingerprint.fsx"


def live_payloads(path):
    # Extract only the real receiver function; importing readback.py runs feed IO.
    tree = ast.parse(READBACK.read_text())
    definition = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "payloads")
    scope = {"hashlib": hashlib, "zipfile": zipfile}
    exec(compile(ast.Module(body=[definition], type_ignores=[]), str(READBACK), "exec"), scope)
    return scope["payloads"](path)


def make_archive(path, members):
    with zipfile.ZipFile(path, "w") as archive:
        for name, body, mode in members:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = mode << 16
            archive.writestr(info, body)
    return path


def compare(source, served, *, accept, reason=""):
    result = subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", "compare", str(source), str(served)],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )
    if accept:
        assert result.returncode == 0 and result.stdout.strip() == "match", result.stderr
    else:
        assert result.returncode == 2 and reason in result.stderr, result.stderr


def main():
    with tempfile.TemporaryDirectory(prefix="fsquint-mode-policy-") as folder:
        temp = Path(folder)
        source = make_archive(temp / "source.nupkg", [("README.md", b"same", 0o100644)])
        mode_only = make_archive(temp / "mode.nupkg", [("README.md", b"same", 0o100755)])
        digest = make_archive(temp / "digest.nupkg", [("README.md", b"changed", 0o100644)])
        name = make_archive(temp / "name.nupkg", [("OTHER.md", b"same", 0o100644)])
        signature = make_archive(temp / "signature.nupkg", [
            ("README.md", b"same", 0o100644), (".signature.p7s", b"feed", 0o100644)
        ])
        symlink = make_archive(temp / "symlink.nupkg", [("README.md", b"same", 0o120777)])

        assert live_payloads(source) == live_payloads(mode_only)
        compare(source, mode_only, accept=True)
        assert live_payloads(source) != live_payloads(digest)
        compare(source, digest, accept=False, reason="digest differs")
        assert live_payloads(source) != live_payloads(name)
        compare(source, name, accept=False, reason="archive members differ")
        assert live_payloads(source) == live_payloads(signature)
        compare(source, signature, accept=True)
        compare(source, symlink, accept=False, reason="symlink archive member")
    print("readback mode policy: 5 offline comparisons passed")


if __name__ == "__main__":
    main()
