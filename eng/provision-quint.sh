#!/usr/bin/env bash
# Explicit Linux x64 test toolchain provisioning; the library never calls this script.
set -euo pipefail
root=${1:?toolchain destination required}
mkdir -p "$root/home/rust-evaluator-v0.6.0"
curl --fail --silent --show-error --location --retry 3 --max-time 120 \
  https://github.com/quint-co/quint/releases/download/v0.32.0/quint-linux-amd64 -o "$root/quint"
echo "939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f  $root/quint" | sha256sum --check
curl --fail --silent --show-error --location --retry 3 --max-time 120 \
  https://github.com/quint-co/quint/releases/download/evaluator/v0.6.0/quint_evaluator-x86_64-unknown-linux-gnu.tar.gz -o "$root/evaluator.tar.gz"
echo "61755a09d5052d93a4e75e840059edfd0d3674aeda164b9d2464be3d6e21b1c2  $root/evaluator.tar.gz" | sha256sum --check
tar -xzf "$root/evaluator.tar.gz" -C "$root/home/rust-evaluator-v0.6.0" quint_evaluator
chmod +x "$root/quint" "$root/home/rust-evaluator-v0.6.0/quint_evaluator"
