#!/usr/bin/env python3
"""Offline XML identity parity over the actual Python readback block."""

import ast
import hashlib
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import xml.parsers.expat as expat
import zipfile


ROOT = Path(__file__).resolve().parents[1]
READBACK = ROOT / "eng/readback.py"
FSX = ROOT / "eng/nuspec-root-inspect.fsx"
PACKAGE = "FsQuint"
VERSION = "0.1.0"
COMMIT = "a" * 40


def readback_identity():
    # Compile the served-archive `with ZipFile(target)` block itself, together
    # with its pure helpers. Never execute readback.py's feed/receipt top level.
    tree = ast.parse(READBACK.read_text())
    helpers = [node for node in tree.body if isinstance(node, ast.FunctionDef)
               and node.name in ("payloads", "select_nuspec", "verify_nuspec_xml")]
    blocks = [node for node in ast.walk(tree) if isinstance(node, ast.With)
              and len(node.items) == 1
              and isinstance(node.items[0].context_expr, ast.Call)
              and isinstance(node.items[0].context_expr.func, ast.Attribute)
              and node.items[0].context_expr.func.attr == "ZipFile"
              and node.items[0].context_expr.args
              and isinstance(node.items[0].context_expr.args[0], ast.Name)
              and node.items[0].context_expr.args[0].id == "target"]
    assert len(blocks) == 1, "readback must have one served-archive identity block"
    function = ast.parse("def identity(target, package, version, expected_commit):\n    pass\n").body[0]
    function.body = [blocks[0], ast.Return(value=ast.Constant(True))]
    scope = {"ET": ET, "zipfile": zipfile, "hashlib": hashlib, "expat": expat}
    module = ast.fix_missing_locations(ast.Module(body=[*helpers, function], type_ignores=[]))
    exec(compile(module, str(READBACK), "exec"), scope)
    return scope["payloads"], scope["identity"]


def make_archive(path, xml):
    with zipfile.ZipFile(path, "w") as archive:
        for name, body in [("FsQuint.nuspec", xml.encode()), ("lib/a.dll", b"payload")]:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, body)
    return path


def fsharp(path, accept):
    result = subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", str(path), PACKAGE, VERSION, COMMIT],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )
    if accept:
        assert result.returncode == 0 and result.stdout.strip() == "match", result.stderr
    else:
        assert result.returncode == 2 and "nuspec" in result.stderr, result.stderr


def main():
    payloads, identity = readback_identity()
    metadata = f'<metadata><id>{PACKAGE}</id><version>{VERSION}</version><repository type="git" commit="{COMMIT}"/></metadata>'
    valid = f'<package>{metadata}</package>'
    namespace = f'<package xmlns="urn:nuget:test">{metadata}</package>'
    cases = [
        ("valid", valid, True),
        ("namespace", namespace, True),
        ("wrong-id", valid.replace(f'<id>{PACKAGE}</id>', '<id>Foreign</id>'), False),
        ("wrong-version", valid.replace(f'<version>{VERSION}</version>', '<version>0.2.0</version>'), False),
        ("two-metadata", f'<package>{metadata}{metadata}</package>', False),
        ("two-id", valid.replace(f'<id>{PACKAGE}</id>', f'<id>{PACKAGE}</id><id>{PACKAGE}</id>'), False),
        ("two-repository", valid.replace('</metadata>', f'<repository type="git" commit="{COMMIT}"/></metadata>'), False),
        ("wrong-root", f'<foreign>{metadata}</foreign>', False),
        ("dtd-entity", f'<!DOCTYPE package [<!ENTITY id "{PACKAGE}">]><package>{metadata.replace(PACKAGE, "&id;", 1)}</package>', False),
        ("malformed", '<package>', False),
        ("wrong-commit", valid.replace(COMMIT, "b" * 40), False),
    ]
    missed = []
    with tempfile.TemporaryDirectory(prefix="fsquint-nuspec-xml-") as folder:
        for label, xml, accept in cases:
            path = make_archive(Path(folder) / f"{label}.nupkg", xml)
            assert len(payloads(path)) == 2
            fsharp(path, accept)
            try:
                result = identity(path, PACKAGE, VERSION, COMMIT)
            except (ValueError, ET.ParseError):
                if accept:
                    raise AssertionError(f"Python refused valid {label}")
            else:
                if not accept:
                    missed.append(f"Python accepted {label}.nupkg")
                else:
                    assert result is True
        assert not missed, "\n".join(missed)
    print(f"readback nuspec XML: {len(cases)} offline controls passed")


if __name__ == "__main__":
    main()
