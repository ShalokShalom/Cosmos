#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "$0")/.." && pwd)"
repo="$(cd "$here/../.." && pwd)"
bin="${SCALE_BIN:-$repo/bases/scale-cli/bin/Debug/net10.0/scale}"
sock="/tmp/scale-beam-listener-$$.sock"
log="/tmp/scale-beam-listener-$$.log"

rm -f "$sock" "$log"

cd "$here"
mix compile >/dev/null

SCALE_SOCK="$sock" mix run --no-compile -e 'Scale.Listener.serve(System.get_env("SCALE_SOCK"))' >"$log" 2>&1 &
listener=$!

cleanup() {
  kill "$listener" 2>/dev/null || true
  wait "$listener" 2>/dev/null || true
  rm -f "$sock" "$log"
}
trap cleanup EXIT

for _ in $(seq 1 100); do
  if [ -S "$sock" ]; then
    break
  fi
  sleep 0.05
done

if [ ! -S "$sock" ]; then
  echo "listener did not start" >&2
  cat "$log" >&2
  exit 1
fi

output="$("$bin" --wire-connect "$sock" 21)"

for line in "wire: reply 42" "wire: down normal" "wire: monitor 1"; do
  if ! grep -q "$line" <<<"$output"; then
    echo "missing: $line" >&2
    echo "$output" >&2
    exit 1
  fi
done

echo "$output"
