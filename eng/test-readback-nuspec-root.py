#!/usr/bin/env python3
"""Offline root .nuspec selection against the real readback expression."""

import ast
import hashlib
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]
READBACK = ROOT / "eng/readback.py"
FSX = ROOT / "eng/nuspec-root-inspect.fsx"
PACKAGE = "FsQuint"
VERSION = "0.1.0"
COMMIT = "a" * 40
SPEC = (
    f'<package><metadata><id>{PACKAGE}</id><version>{VERSION}</version>'
    f'<repository type="git" commit="{COMMIT}"/></metadata></package>'
).encode()


def readback_functions():
    # Extract only source expressions/functions; the module top level fetches
    # feeds and writes archives and receipts.
    tree = ast.parse(READBACK.read_text())
    functions = [node for node in tree.body if isinstance(node, ast.FunctionDef)
                 and node.name in ("payloads", "select_nuspec")]
    scope = {"hashlib": hashlib, "zipfile": zipfile}
    exec(compile(ast.Module(body=functions, type_ignores=[]), str(READBACK), "exec"), scope)
    assignments = [node for node in ast.walk(tree) if isinstance(node, ast.Assign)
                   and any(isinstance(target, ast.Name) and target.id == "spec" for target in node.targets)]
    assert len(assignments) == 1, "readback must select one spec expression"
    expression = compile(ast.Expression(assignments[0].value), str(READBACK), "eval")

    def select(archive):
        return eval(expression, scope, {"archive": archive, "package": PACKAGE})

    return scope["payloads"], select


def make_archive(path, members):
    with zipfile.ZipFile(path, "w") as archive:
        for name, body in members:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, body)
    return path


def fsharp(path, expected):
    result = subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", str(path), PACKAGE, VERSION, COMMIT],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )
    if expected:
        assert result.returncode == 0 and result.stdout.strip() == "match", result.stderr
    else:
        assert result.returncode == 2 and "nuspec" in result.stderr, result.stderr


def main():
    payloads, select = readback_functions()
    with tempfile.TemporaryDirectory(prefix="fsquint-nuspec-root-") as folder:
        temp = Path(folder)
        valid = make_archive(temp / "valid.nupkg", [
            ("FsQuint.nuspec", SPEC), ("lib/a.dll", b"payload"),
            (".signature.p7s", b"feed")
        ])
        with zipfile.ZipFile(valid) as archive:
            assert select(archive) == "FsQuint.nuspec"
            assert ET.fromstring(archive.read(select(archive))).find("metadata/repository").attrib["commit"] == COMMIT
        assert len(payloads(valid)) == 2
        fsharp(valid, True)

        cases = [
            ("nested", [("nested/FsQuint.nuspec", SPEC), ("lib/a.dll", b"payload")]),
            ("wrong-root", [("Foreign.nuspec", SPEC), ("lib/a.dll", b"payload")]),
            ("two-roots", [("FsQuint.nuspec", SPEC), ("Foreign.nuspec", SPEC), ("lib/a.dll", b"payload")]),
            ("missing", [("lib/a.dll", b"payload")]),
        ]
        missed = []
        for label, members in cases:
            path = make_archive(temp / f"{label}.nupkg", members)
            assert payloads(path), label
            fsharp(path, False)
            with zipfile.ZipFile(path) as archive:
                try:
                    select(archive)
                except ValueError as error:
                    assert "exact root nuspec" in str(error), str(error)
                else:
                    missed.append(f"Python selected {label}.nupkg")
        assert not missed, "\n".join(missed)

    print("readback nuspec root: 5 offline controls passed")


if __name__ == "__main__":
    main()
