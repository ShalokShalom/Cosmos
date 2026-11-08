#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
cd "$here"

if ! command -v capnp >/dev/null 2>&1; then
  echo "capnp is required; run: nix-shell -p capnproto --run './components/ipc/vectors/check.sh'" >&2
  exit 2
fi

tmp="$(mktemp)"
trap 'rm -f "$tmp" "$tmp.txt"' EXIT

for name in number text monitor monitor-ack reply defaults; do
  capnp encode scale_actor.capnp ActorEnvelope <"$name.txt" >"$tmp"

  if ! cmp -s "$tmp" "$name.bin"; then
    echo "$name: capnp encode does not match the checked-in bytes" >&2
    exit 1
  fi
done

(
  cd "$repo/adapters/elixir"
  mix compile >/dev/null
  OUT="$tmp" mix run --no-compile -e '
    envelope = %Scale.Wire{
      message_id: 1,
      target: 0,
      sender: 0,
      reply: 0,
      operation: :send,
      body: {:number, 4.5}
    }

    {:ok, bytes} = Scale.Wire.encode(envelope)
    File.write!(System.get_env("OUT"), bytes)
  '
)

capnp decode scale_actor.capnp ActorEnvelope <"$tmp" >"$tmp.txt"

for field in "messageId = 1" "target = 0" "operation = 0" "bodyKind = 0" "bodyNumber = 4.5"; do
  if ! grep -q "$field" "$tmp.txt"; then
    echo "capnp decode is missing: $field" >&2
    cat "$tmp.txt" >&2
    exit 1
  fi
done

echo "wire vectors: ok"
