#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tests/FsQuint.Tests -c Release
dotnet pack src/FsQuint/FsQuint.fsproj -c Release -o artifacts/packages
dotnet pack src/FsQuint.Tooling/FsQuint.Tooling.fsproj -c Release -o artifacts/packages
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
cp examples/BoundedQueue/{BoundedQueue.fsproj,Program.fs,queue.itf.json} "$scratch/"
cp global.json NuGet.Config "$scratch/"
export NUGET_PACKAGES="$scratch/packages"
export NUGET_HTTP_CACHE_PATH="$scratch/http"
dotnet restore "$scratch/BoundedQueue.fsproj" --source "$PWD/artifacts/packages" --source https://api.nuget.org/v3/index.json
dotnet run --project "$scratch/BoundedQueue.fsproj" --no-restore
