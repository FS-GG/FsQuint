"""Compare served package payloads (NuGet may add a repository signature)."""
import base64
import hashlib
import json
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
        for attempt in range(40):
            try:
                with reader.open(urllib.request.Request(url, headers=headers), timeout=30) as response:
                    content = response.read()
                break
            except urllib.error.HTTPError as error:
                if error.code != 404 or attempt == 39:
                    raise
                time.sleep(10)
        target = root / f'{name}.{feed}.nupkg'
        target.write_bytes(content)
        actual = payloads(target)
        if expected is None:
            expected = actual
        if expected != actual:
            raise ValueError(f'{package}: {feed} differs from qualified payload')
        receipts[f'{package}:{feed}'] = {'archiveSha256': hashlib.sha256(content).hexdigest(), 'payloads': actual}
(root / 'receipt.json').write_text(json.dumps(receipts, indent=2) + '\n')
print('Both package payloads match both feeds.')
