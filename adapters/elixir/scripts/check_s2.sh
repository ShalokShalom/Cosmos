#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "$0")/.." && pwd)"
repo="$(cd "$here/../.." && pwd)"
bin="${SCALE_BIN:-$repo/bases/scale-cli/bin/Debug/net10.0/scale}"
sock="/tmp/scale-beam-s2-$$.sock"
log="/tmp/scale-beam-s2-$$.log"

rm -f "$sock" "$log"

"$bin" --wire-listen "$sock" >"$log" 2>&1 &
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

cd "$here"
mix compile >/dev/null

output="$(SCALE_SOCK="$sock" mix run --no-compile -e 'Scale.Peer.run_cli(System.get_env("SCALE_SOCK"), 21.0)')"
expected="reply=42 down=down normal monitor=1"

if [ "$output" != "$expected" ]; then
  echo "unexpected peer output: $output" >&2
  exit 1
fi

echo "$output"
