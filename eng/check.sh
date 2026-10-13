#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tests/FsQuint.Tests -c Release
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- selftest
dotnet pack src/FsQuint/FsQuint.fsproj -c Release -o artifacts/packages
dotnet pack src/FsQuint.Tooling/FsQuint.Tooling.fsproj -c Release -o artifacts/packages
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- replay
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
cp examples/BoundedQueue/{BoundedQueue.fsproj,Program.fs,queue.itf.json} "$scratch/"
cp global.json NuGet.Config examples/Shared/PureReplay.fs "$scratch/"
version=$(dotnet msbuild src/FsQuint/FsQuint.fsproj -getProperty:Version)
dotnet run --project eng/Qualification/Qualification.fsproj -c Release -- set-package-version "$scratch/BoundedQueue.fsproj" "[$version]"
export NUGET_PACKAGES="$scratch/packages"
export NUGET_HTTP_CACHE_PATH="$scratch/http"
dotnet restore "$scratch/BoundedQueue.fsproj" --source "$PWD/artifacts/packages" --source https://api.nuget.org/v3/index.json
cmp --silent "$PWD/artifacts/packages/FsQuint.$version.nupkg" "$NUGET_PACKAGES/fsquint/$version/fsquint.$version.nupkg"
dotnet run --project "$scratch/BoundedQueue.fsproj" --no-restore
