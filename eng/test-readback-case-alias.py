#!/usr/bin/env python3
"""Offline ASCII case-alias controls for both archive readers."""

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
    # Run the real function without the module's feed and receipt side effects.
    tree = ast.parse(READBACK.read_text())
    definition = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "payloads")
    scope = {"hashlib": hashlib, "zipfile": zipfile}
    exec(compile(ast.Module(body=[definition], type_ignores=[]), str(READBACK), "exec"), scope)
    return scope["payloads"](path)


def make_archive(path, names):
    with zipfile.ZipFile(path, "w") as archive:
        for name in names:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = (0o040755 if name.endswith("/") else 0o100644) << 16
            archive.writestr(info, b"" if name.endswith("/") else name.encode())
    return path


def fsharp(path):
    return subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", "inspect", str(path)],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )


def expect_alias_refusal(path):
    errors = []
    try:
        python_payloads(path)
    except ValueError as error:
        if "Case-alias ZIP entry" not in str(error):
            errors.append(f"Python wrong refusal: {error}")
    else:
        errors.append(f"Python accepted {path.name}")
    result = fsharp(path)
    if result.returncode != 2 or "case-alias archive member" not in result.stderr:
        errors.append(f"F# accepted or misclassified {path.name}: {result.stderr}")
    return errors


def main():
    with tempfile.TemporaryDirectory(prefix="fsquint-case-alias-") as folder:
        temp = Path(folder)
        valid = make_archive(temp / "valid.nupkg", ["README.md", "LICENSE.txt", "docs/a.md", "docs/b.md"])
        assert len(python_payloads(valid)) == 4
        assert fsharp(valid).returncode == 0

        cases = [
            ("root", ["README.md", "readme.md"]),
            ("nested", ["README.md", "Docs/Guide.md", "docs/guide.md"]),
            ("directory", ["README.md", "Folder/", "folder/"]),
            ("signature", ["README.md", ".signature.p7s", ".SIGNATURE.P7S"]),
        ]
        missed = []
        for label, names in cases:
            archive = make_archive(temp / f"{label}.nupkg", names)
            missed.extend(expect_alias_refusal(archive))
        assert not missed, "\n".join(missed)

    print("readback case aliases: 5 offline controls passed")


if __name__ == "__main__":
    main()
