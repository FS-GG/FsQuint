#!/usr/bin/env python3
"""Offline expected-commit authority controls over readback source selection."""

import ast
import hashlib
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import xml.etree.ElementTree as ET
import xml.parsers.expat as expat
import zipfile


ROOT = Path(__file__).resolve().parents[1]
READBACK = ROOT / "eng/readback.py"
FSX = ROOT / "eng/nuspec-root-inspect.fsx"
PACKAGE = "FsQuint"
VERSION = "0.1.0"


def readback_source():
    # Extract only pure helpers and the actual expected_commit assignment.
    # Importing the module would fetch feeds and write a receipt.
    tree = ast.parse(READBACK.read_text())
    helpers = [node for node in tree.body if isinstance(node, ast.FunctionDef)
               and node.name in ("payloads", "select_nuspec", "verify_nuspec_xml", "select_expected_commit")]
    assignments = [node for node in tree.body if isinstance(node, ast.Assign)
                   and any(isinstance(target, ast.Name) and target.id == "expected_commit" for target in node.targets)]
    assert len(assignments) == 1, "readback must select one expected commit"
    scope = {"hashlib": hashlib, "zipfile": zipfile, "ET": ET, "expat": expat,
             "subprocess": subprocess, "re": re}
    exec(compile(ast.Module(body=helpers, type_ignores=[]), str(READBACK), "exec"), scope)
    expression = compile(ast.Expression(assignments[0].value), str(READBACK), "eval")

    def select(version, supplied, github_sha, repository):
        args = [str(READBACK), version] + ([supplied] if supplied is not None else [])
        context = dict(scope, version=version, sys=SimpleNamespace(argv=args),
                       os=SimpleNamespace(environ={"GITHUB_SHA": github_sha} if github_sha is not None else {}))
        original = Path.cwd()
        try:
            os.chdir(repository)
            return eval(expression, context)
        finally:
            os.chdir(original)

    return scope, select


def git(repository, *args):
    result = subprocess.run(["git", *args], cwd=repository, text=True,
                            capture_output=True, check=True)
    return result.stdout.strip()


def archive(path, commit):
    xml = (f'<package><metadata><id>{PACKAGE}</id><version>{VERSION}</version>'
           f'<repository type="git" commit="{commit}"/></metadata></package>').encode()
    with zipfile.ZipFile(path, "w") as packed:
        for name, body in [("FsQuint.nuspec", xml), ("lib/a.dll", b"payload")]:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            packed.writestr(info, body)
    return path


def fsharp_refuses(path, expected):
    result = subprocess.run(
        ["dotnet", "fsi", str(FSX), "--", str(path), PACKAGE, VERSION, expected],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )
    assert result.returncode == 2 and "nuspec" in result.stderr, result.stderr


def main():
    helpers, select = readback_source()
    with tempfile.TemporaryDirectory(prefix="fsquint-commit-source-") as folder:
        temp = Path(folder)
        repo = temp / "source"
        repo.mkdir()
        git(repo, "init", "-q", "-b", "main")
        git(repo, "config", "user.name", "Fixture")
        git(repo, "config", "user.email", "fixture@example.invalid")
        marker = repo / "marker.txt"
        marker.write_text("release source\n")
        git(repo, "add", "marker.txt")
        git(repo, "commit", "-qm", "release source")
        source_commit = git(repo, "rev-parse", "HEAD")
        git(repo, "tag", "v0.1.0")
        marker.write_text("foreign source\n")
        git(repo, "commit", "-qam", "foreign source")
        foreign_commit = git(repo, "rev-parse", "HEAD")
        assert source_commit != foreign_commit

        wrong_one = archive(temp / "github.nupkg", foreign_commit)
        wrong_two = archive(temp / "nuget.nupkg", foreign_commit)
        assert helpers["payloads"](wrong_one) == helpers["payloads"](wrong_two)
        with zipfile.ZipFile(wrong_one) as packed:
            spec = helpers["select_nuspec"](packed, PACKAGE)
            helpers["verify_nuspec_xml"](packed, spec, PACKAGE, VERSION, foreign_commit)
        fsharp_refuses(wrong_one, source_commit)

        missed = []
        try:
            chosen = select(VERSION, foreign_commit, foreign_commit, repo)
        except ValueError as error:
            assert "source tag" in str(error), str(error)
        else:
            missed.append(f"caller-selected foreign commit {chosen}")

        chosen = select(VERSION, source_commit, foreign_commit, repo)
        assert chosen == source_commit, "recovery CLI commit must match source tag"
        chosen = select(VERSION, None, source_commit, repo)
        assert chosen == source_commit, "tag-push event commit must match source tag"
        chosen = select(VERSION, None, None, repo)
        if chosen != source_commit:
            missed.append("missing caller commit disabled source identity check")
        with zipfile.ZipFile(wrong_one) as packed:
            spec = helpers["select_nuspec"](packed, PACKAGE)
            try:
                helpers["verify_nuspec_xml"](packed, spec, PACKAGE, VERSION, chosen)
            except ValueError as error:
                assert "source commit differs" in str(error), str(error)
            else:
                missed.append("foreign archive passed tag-selected commit")
        correct = archive(temp / "correct.nupkg", source_commit)
        with zipfile.ZipFile(correct) as packed:
            spec = helpers["select_nuspec"](packed, PACKAGE)
            helpers["verify_nuspec_xml"](packed, spec, PACKAGE, VERSION, chosen)

        git(repo, "tag", "-d", "v0.1.0")
        try:
            chosen = select(VERSION, source_commit, None, repo)
        except ValueError as error:
            assert "source tag" in str(error), str(error)
        else:
            missed.append(f"missing source tag accepted {chosen}")
        assert not missed, "\n".join(missed)

    print("readback commit source: 7 offline controls passed")


if __name__ == "__main__":
    main()
