#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
cp examples/BoundedQueue/{BoundedQueue.fsproj,Program.fs,queue.itf.json} "$scratch/"
cp global.json NuGet.Config "$scratch/"
if [[ -n "${1:-}" ]]; then
  python3 - "$scratch/BoundedQueue.fsproj" "$1" <<'PYTHON'
import sys
import xml.etree.ElementTree as ET
path, version = sys.argv[1:]
tree = ET.parse(path)
for package in tree.iter('PackageReference'):
    if package.attrib['Include'] == 'FsQuint':
        package.set('Version', version)
tree.write(path)
PYTHON
fi
export NUGET_PACKAGES="$scratch/packages"
export NUGET_HTTP_CACHE_PATH="$scratch/http"
unset GH_TOKEN GITHUB_TOKEN NUGET_AUTH_TOKEN
dotnet restore "$scratch/BoundedQueue.fsproj" --configfile "$scratch/NuGet.Config" --no-http-cache
dotnet run --project "$scratch/BoundedQueue.fsproj" --no-restore
