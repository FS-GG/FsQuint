#!/usr/bin/env python3
"""Offline guard controls over the real Python readback payload function."""

import ast
import hashlib
from pathlib import Path
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parents[1]
READBACK = ROOT / "eng/readback.py"
FSX = ROOT / "eng/packed-archive-fingerprint.fsx"


def receiver_payloads(path):
    # Importing readback.py would fetch feeds and write a receipt.
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


def fsharp_refuses(path, reason):
    result = subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", "inspect", str(path)],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )
    assert result.returncode == 2 and reason in result.stderr, result.stderr


def receiver_refuses(path, reason):
    try:
        receiver_payloads(path)
    except ValueError as error:
        assert reason in str(error), str(error)
    else:
        raise AssertionError(f"Python receiver accepted {path.name}")


def main():
    with tempfile.TemporaryDirectory(prefix="fsquint-member-guards-") as folder:
        temp = Path(folder)
        regular = make_archive(temp / "regular.nupkg", [("README.md", b"same", 0o100644)])
        executable = make_archive(temp / "executable.nupkg", [("README.md", b"same", 0o100755)])
        assert receiver_payloads(regular) == receiver_payloads(executable)
        signature = make_archive(temp / "signature.nupkg", [
            ("README.md", b"same", 0o100644), (".signature.p7s", b"feed", 0o100644)
        ])
        assert receiver_payloads(regular) == receiver_payloads(signature)

        cases = [
            ("symlink-payload", "README.md", 0o120777, "Symlink ZIP entry", "symlink archive member"),
            ("symlink-signature", ".signature.p7s", 0o120777, "Symlink ZIP entry", "symlink archive member"),
            ("parent", "../outside", 0o100644, "Unsafe ZIP entry", "unsafe archive member"),
            ("absolute", "/outside", 0o100644, "Unsafe ZIP entry", "unsafe archive member"),
            ("dot", "docs/./file", 0o100644, "Unsafe ZIP entry", "unsafe archive member"),
            ("backslash", "docs\\file", 0o100644, "Unsafe ZIP entry", "unsafe archive member"),
            ("colon", "C:drive", 0o100644, "Unsafe ZIP entry", "unsafe archive member"),
            ("empty-segment", "docs//file", 0o100644, "Unsafe ZIP entry", "unsafe archive member"),
            ("blank", " ", 0o100644, "Unsafe ZIP entry", "unsafe archive member"),
        ]
        missed = []
        for label, name, mode, python_reason, fsharp_reason in cases:
            members = [("README.md", b"same", 0o100644)]
            if label == "symlink-payload":
                members = [(name, b"same", mode)]
            else:
                members.append((name, b"same", mode))
            archive = make_archive(temp / f"{label}.nupkg", members)
            fsharp_refuses(archive, fsharp_reason)
            try:
                receiver_refuses(archive, python_reason)
            except AssertionError as error:
                missed.append(str(error))

        assert not missed, "\n".join(missed)

    print("readback member guards: 11 offline controls passed")


if __name__ == "__main__":
    main()
