#!/usr/bin/env bash
# Explicit Linux x64 test toolchain provisioning; the library never calls this script.
set -euo pipefail

# Local mode is explicit and never falls back to downloading on refusal.
# Default CI's one-argument body below remains unchanged.
if [[ $# -ne 1 ]]; then
  if [[ $# -ne 4 || ${2:-} != --from-custody ]]; then
    echo 'usage: provision-quint.sh DEST [--from-custody QUINT_PART EVALUATOR_PART]' >&2
    exit 2
  fi
  destination=$1
  quint_input=$3
  evaluator_input=$4

  refuse() { echo "Local Quint custody refused: $1" >&2; exit 3; }
  plain_path() {
    local value=$1 current=$1
    [[ $value == /* && $value != *$'\n'* && $value != *$'\r'* ]] || refuse 'absolute plain paths required'
    while [[ -n $current && $current != / ]]; do
      [[ ! -L $current ]] || refuse 'linked path'
      current=${current%/*}
    done
  }
  regular_bytes() {
    plain_path "$1"
    [[ -f $1 && ! -L $1 ]] || refuse 'regular input required'
    [[ $(stat --format=%s -- "$1") == "$2" ]] || refuse 'input byte count'
  }
  exact_hash() {
    printf '%s  %s\n' "$2" "$1" | sha256sum --check --status || refuse 'input digest'
  }

  # Verify both original payloads before mkdir, tar extraction or mode changes.
  regular_bytes "$quint_input" 125661253
  regular_bytes "$evaluator_input" 1077099
  exact_hash "$quint_input" 939b64095b706017f2f202c6f99c860c40be7c31bddc2b98557316e50f42cd7f
  exact_hash "$evaluator_input" 61755a09d5052d93a4e75e840059edfd0d3674aeda164b9d2464be3d6e21b1c2
  plain_path "$destination"
  evaluator_directory="$destination/home/rust-evaluator-v0.6.0"
  evaluator_output="$evaluator_directory/quint_evaluator"
  plain_path "$evaluator_output"
  [[ ! -e $evaluator_output && ! -L $evaluator_output ]] || refuse 'fresh evaluator output required'
  [[ $(tar -tzf "$evaluator_input") == quint_evaluator ]] || refuse 'sole selected member required'
  member_details=$(tar -tvzf "$evaluator_input")
  [[ ${member_details:0:1} == '-' && $member_details != *$'\n'* ]] || refuse 'regular selected member required'

  umask 077
  mkdir -p "$evaluator_directory"
  tar --no-same-owner --no-same-permissions -xzf "$evaluator_input" -C "$evaluator_directory" quint_evaluator
  regular_bytes "$evaluator_output" 2628304
  exact_hash "$evaluator_output" b2efdeac5713d153e41bf2143b94ed75d888fdd5637f4a5d61a04c695313510a
  # Reuse the original Quint file directly: no second125MB copy or payload move.
  chmod u+x "$quint_input" "$evaluator_output"
  echo 'Verified local Quint0.32.0 and Rust evaluator0.6.0 custody.'
  exit 0
fi

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
