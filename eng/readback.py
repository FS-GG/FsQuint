"""Compare served package payloads (NuGet may add a repository signature)."""
import base64
import hashlib
import json
import re
import xml.etree.ElementTree as ET
import os
from pathlib import Path
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
expected_commit = sys.argv[2] if len(sys.argv) > 2 else os.environ.get('GITHUB_SHA')
root = Path('artifacts/readback')
root.mkdir(parents=True, exist_ok=True)

def payloads(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate ZIP entry')
        return {name: hashlib.sha256(archive.read(name)).hexdigest()
                for name in names if name != '.signature.p7s'}

receipts = {}
for package in ['FsQuint', 'FsQuint.Tooling']:
    name = package.lower()
    local = Path(f'artifacts/packages/{package}.{version}.nupkg')
    expected = payloads(local) if local.exists() else None
    for feed in ['github', 'nuget']:
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
        target = root / f'{name}.{feed}.nupkg'
        target.write_bytes(content)
        actual = payloads(target)
        with zipfile.ZipFile(target) as archive:
            spec = next(name for name in archive.namelist() if name.endswith('.nuspec'))
            metadata = ET.fromstring(archive.read(spec))
            repository = next(node for node in metadata.iter() if node.tag.endswith('}repository') or node.tag == 'repository')
            if expected_commit and repository.attrib.get('commit') != expected_commit:
                raise ValueError(f'{package}: source commit differs from release tag')
        if expected is None:
            expected = actual
        if expected != actual:
            raise ValueError(f'{package}: {feed} differs from qualified payload')
        receipts[f'{package}:{feed}'] = {'archiveSha256': hashlib.sha256(content).hexdigest(), 'payloads': actual}
(root / 'receipt.json').write_text(json.dumps(receipts, indent=2) + '\n')
print('Both package payloads match both feeds.')
