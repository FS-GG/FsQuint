#!/usr/bin/env bash
# Exercise the actual shell gate without launching a costly test workload.
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
cp "$root"/examples/CiPreflight/{CiPreflight.fsproj,Program.fs,pipeline.qnt,pipeline_test.qnt,packages.lock.json} "$scratch/"
cp "$root"/{global.json,NuGet.Config} "$scratch/"
dotnet restore "$scratch/CiPreflight.fsproj" --locked-mode
dotnet build "$scratch/CiPreflight.fsproj" --no-restore -c Release >/dev/null
app="$scratch/bin/Release/net10.0/CiPreflight.dll"
# Marker creation stands in for the expensive command; && is the actual gate.
dotnet "$app" valid >"$scratch/valid.log" 2>&1 && touch "$scratch/valid-started"
test -f "$scratch/valid-started"
grep -q '^ALLOW:' "$scratch/valid.log"
status=0
dotnet "$app" broken >"$scratch/broken.log" 2>&1 && touch "$scratch/broken-started" || status=$?
test "$status" -eq 1
test ! -f "$scratch/broken-started"
grep -q '^BLOCK: preflight returned Counterexample' "$scratch/broken.log"
status=0
QUINT_BIN=/missing/quint dotnet "$app" valid >"$scratch/missing.log" 2>&1 && touch "$scratch/missing-started" || status=$?
test "$status" -eq 1
test ! -f "$scratch/missing-started"
"$QUINT_BIN" test "$scratch/pipeline_test.qnt" --main broken_test --match missingDependencyTest >"$scratch/regression.log" 2>&1
grep -Eq '^[[:space:]]*1 passing' "$scratch/regression.log"
cat "$scratch/valid.log" "$scratch/broken.log"
printf '%s\n' 'PASS: valid plan allowed; missing dependency and unavailable tool blocked before workload launch.'
