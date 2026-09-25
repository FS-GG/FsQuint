#!/usr/bin/env python3
"""Offline version-tag movement controls without running feed readback."""

import ast
import hashlib
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import xml.parsers.expat as expat
import zipfile


ROOT = Path(__file__).resolve().parents[1]
READBACK = ROOT / "eng/readback.py"
VERSION = "0.1.0"
PACKAGE = "FsQuint"


def source_functions():
    tree = ast.parse(READBACK.read_text())
    names = {"payloads", "select_nuspec", "verify_nuspec_xml",
             "select_expected_commit", "verify_source_tag_stable"}
    functions = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in names]
    scope = {"hashlib": hashlib, "zipfile": zipfile, "ET": ET, "expat": expat,
             "re": re, "subprocess": subprocess}
    exec(compile(ast.Module(body=functions, type_ignores=[]), str(READBACK), "exec"), scope)
    return tree, scope


def git(repository, *args):
    return subprocess.run(["git", *args], cwd=repository, text=True,
                          capture_output=True, check=True).stdout.strip()


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


def guard_call(node):
    return (isinstance(node, ast.Expr) and isinstance(node.value, ast.Call)
            and isinstance(node.value.func, ast.Name)
            and node.value.func.id == "verify_source_tag_stable")


def wired_checkpoints(tree):
    loops = [node for node in ast.walk(tree) if isinstance(node, ast.For)
             and isinstance(node.target, ast.Name) and node.target.id == "feed"]
    if len(loops) != 1:
        return False
    body = loops[0].body
    if not body or not guard_call(body[0]):
        return False
    retries = [index for index, node in enumerate(body) if isinstance(node, ast.For)
               and isinstance(node.target, ast.Name) and node.target.id == "attempt"]
    if len(retries) != 1 or retries[0] + 1 >= len(body) or not guard_call(body[retries[0] + 1]):
        return False
    writes = [index for index, node in enumerate(tree.body) if isinstance(node, ast.Expr)
              and isinstance(node.value, ast.Call)
              and isinstance(node.value.func, ast.Attribute)
              and node.value.func.attr == "write_text"]
    return len(writes) == 1 and writes[0] > 0 and guard_call(tree.body[writes[0] - 1])


def main():
    tree, source = source_functions()
    with tempfile.TemporaryDirectory(prefix="fsquint-tag-stability-") as folder:
        temp = Path(folder)
        repo = temp / "source"
        repo.mkdir()
        git(repo, "init", "-q", "-b", "main")
        git(repo, "config", "user.name", "Fixture")
        git(repo, "config", "user.email", "fixture@example.invalid")
        marker = repo / "marker.txt"
        marker.write_text("source A\n")
        git(repo, "add", "marker.txt")
        git(repo, "commit", "-qm", "source A")
        commit_a = git(repo, "rev-parse", "HEAD")
        git(repo, "tag", "v0.1.0")
        marker.write_text("source B\n")
        git(repo, "commit", "-qam", "source B")
        commit_b = git(repo, "rev-parse", "HEAD")
        assert commit_a != commit_b

        selected = source["select_expected_commit"](VERSION, commit_a, repo)
        package = archive(temp / "served-a.nupkg", commit_a)
        with zipfile.ZipFile(package) as packed:
            spec = source["select_nuspec"](packed, PACKAGE)
            source["verify_nuspec_xml"](packed, spec, PACKAGE, VERSION, selected)

        git(repo, "tag", "-f", "v0.1.0", commit_b)
        # The old flow still accepts A's archive using the stale selected A.
        with zipfile.ZipFile(package) as packed:
            source["verify_nuspec_xml"](packed, spec, PACKAGE, VERSION, selected)

        missed = []
        guard = source.get("verify_source_tag_stable")
        if guard is None:
            missed.append("tag moved from A to B but readback has no tag-stability guard")
        else:
            try:
                guard(VERSION, selected, repo)
            except ValueError as error:
                assert "source tag" in str(error), str(error)
            else:
                missed.append("moved source tag accepted stale A")
            git(repo, "tag", "-d", "v0.1.0")
            try:
                guard(VERSION, selected, repo)
            except ValueError as error:
                assert "source tag" in str(error), str(error)
            else:
                missed.append("missing source tag accepted stale A")
            git(repo, "tag", "v0.1.0", commit_a)
            guard(VERSION, selected, repo)

        if not wired_checkpoints(tree):
            missed.append("tag guard is not wired at feed entry, after download, and before receipt")
        assert not missed, "\n".join(missed)

    print("readback tag stability: 5 offline controls passed")


if __name__ == "__main__":
    main()
