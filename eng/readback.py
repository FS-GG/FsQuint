"""Compare served package payloads (NuGet may add a repository signature)."""
import base64
import hashlib
import json
import re
import xml.etree.ElementTree as ET
import xml.parsers.expat as expat
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile
from urllib.parse import urlsplit

class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
        if redirected is not None and urlsplit(req.full_url).netloc != urlsplit(newurl).netloc:
            redirected.remove_header("Authorization")
        return redirected

reader = urllib.request.build_opener(SafeRedirect())

version = sys.argv[1]
if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?", version):
    raise ValueError("Invalid package version")

def select_expected_commit(version, supplied_commit, repository=None):
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?", version):
        raise ValueError('Invalid package version')
    tag = f'refs/tags/v{version}^{{commit}}'
    result = subprocess.run(
        ['git', 'rev-parse', '--verify', '--end-of-options', tag],
        cwd=repository, text=True, capture_output=True, check=False,
    )
    source_commit = result.stdout.strip()
    if result.returncode != 0 or not re.fullmatch(r'[0-9a-f]{40}', source_commit):
        raise ValueError(f'{version}: source tag commit is unavailable')
    if supplied_commit is not None and supplied_commit != source_commit:
        raise ValueError(f'{version}: supplied commit differs from source tag')
    return source_commit

def verify_source_tag_stable(version, selected_commit, repository=None):
    current = select_expected_commit(version, None, repository)
    if current != selected_commit:
        raise ValueError(f'{version}: source tag moved during readback')

expected_commit = select_expected_commit(
    version, sys.argv[2] if len(sys.argv) > 2 else os.environ.get('GITHUB_SHA'))
root = Path('artifacts/readback')
root.mkdir(parents=True, exist_ok=True)

def payloads(path):
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)):
            raise ValueError('Duplicate ZIP entry')
        ascii_fold = str.maketrans('ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')
        aliases = set()
        for entry in entries:
            name = entry.filename
            path_part = name[:-1] if name.endswith('/') else name
            if (not path_part.strip() or name.startswith('/')
                    or any(char in name for char in ('\\', ':', '\x00'))
                    or any(segment in ('', '.', '..') for segment in path_part.split('/'))):
                raise ValueError(f'Unsafe ZIP entry: {name!r}')
            folded = name.translate(ascii_fold)
            if folded in aliases:
                raise ValueError(f'Case-alias ZIP entry: {name!r}')
            aliases.add(folded)
            mode = (entry.external_attr >> 16) & 0xffff
            if mode & 0o170000 == 0o120000:
                raise ValueError(f'Symlink ZIP entry: {name!r}')
        if not any(entry.filename != '.signature.p7s' and not entry.is_dir()
                   for entry in entries):
            raise ValueError('Archive has no payload members')
        return {entry.filename: hashlib.sha256(archive.read(entry)).hexdigest()
                for entry in entries if entry.filename != '.signature.p7s'}

def select_nuspec(archive, package):
    expected = f'{package}.nuspec'
    specs = [name for name in archive.namelist() if name.lower().endswith('.nuspec')]
    if specs != [expected]:
        raise ValueError(f'{package}: archive must contain one exact root nuspec')
    return expected

def verify_nuspec_xml(archive, spec, package, version, expected_commit):
    body = archive.read(spec)
    parser = expat.ParserCreate()
    def refuse_doctype(*_):
        raise ValueError(f'{package}: nuspec DTD is prohibited')
    parser.StartDoctypeDeclHandler = refuse_doctype
    try:
        parser.Parse(body, True)
    except expat.ExpatError as error:
        raise ValueError(f'{package}: nuspec XML is invalid') from error
    root = ET.fromstring(body)
    def local_name(node):
        return node.tag.rsplit('}', 1)[-1] if isinstance(node.tag, str) else None
    def one(parent, name):
        matches = [node for node in parent if local_name(node) == name]
        if len(matches) != 1:
            raise ValueError(f'{package}: nuspec must contain one {name}')
        return matches[0]
    if local_name(root) != 'package':
        raise ValueError(f'{package}: nuspec package root is invalid')
    metadata = one(root, 'metadata')
    actual_id = ''.join(one(metadata, 'id').itertext())
    actual_version = ''.join(one(metadata, 'version').itertext())
    repository = one(metadata, 'repository')
    if actual_id != package or actual_version != version:
        raise ValueError(f'{package}: nuspec package identity differs')
    if expected_commit and repository.attrib.get('commit') != expected_commit:
        raise ValueError(f'{package}: source commit differs from release tag')

receipts = {}
for package in ['FsQuint', 'FsQuint.Tooling']:
    name = package.lower()
    local = Path(f'artifacts/packages/{package}.{version}.nupkg')
    expected = payloads(local) if local.exists() else None
    for feed in ['github', 'nuget']:
        verify_source_tag_stable(version, expected_commit)
        url = (f'https://nuget.pkg.github.com/FS-GG/download/{name}/{version}/{name}.{version}.nupkg'
               if feed == 'github' else
               f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg')
        headers = {}
        if feed == 'github':
            credential = (os.environ['GITHUB_ACTOR'] + ':' + os.environ['GH_TOKEN']).encode()
            headers['Authorization'] = 'Basic ' + base64.b64encode(credential).decode()
        for attempt in range(120):
            try:
                with reader.open(urllib.request.Request(url, headers=headers), timeout=30) as response:
                    content = response.read()
                break
            except urllib.error.HTTPError as error:
                if error.code != 404 or attempt == 119:
                    raise
                if attempt % 6 == 0:
                    print(f"{package}: {feed} has not exposed the download yet; retrying", flush=True)
                time.sleep(10)
        verify_source_tag_stable(version, expected_commit)
        target = root / f'{name}.{feed}.nupkg'
        target.write_bytes(content)
        actual = payloads(target)
        with zipfile.ZipFile(target) as archive:
            spec = select_nuspec(archive, package)
            verify_nuspec_xml(archive, spec, package, version, expected_commit)
        if expected is None:
            expected = actual
        if expected != actual:
            raise ValueError(f'{package}: {feed} differs from qualified payload')
        receipts[f'{package}:{feed}'] = {'archiveSha256': hashlib.sha256(content).hexdigest(), 'payloads': actual}
verify_source_tag_stable(version, expected_commit)
(root / 'receipt.json').write_text(json.dumps(receipts, indent=2) + '\n')
print('Both package payloads match both feeds.')
