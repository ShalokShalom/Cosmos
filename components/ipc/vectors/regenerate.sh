#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
cd "$here"

if ! command -v capnp >/dev/null 2>&1; then
  echo "capnp is required; run: nix-shell -p capnproto --run './components/ipc/vectors/regenerate.sh'" >&2
  exit 2
fi

for name in number text monitor monitor-ack reply defaults; do
  capnp encode scale_actor.capnp ActorEnvelope <"$name.txt" >"$name.bin"
done

head -c 4 number.bin >truncated.bin

elixir -e '
  bytes = File.read!("number.bin")
  <<prefix::binary-size(8), rest::binary>> = bytes
  tail = binary_part(rest, 1, byte_size(rest) - 1)
  File.write!("bad-root.bin", prefix <> <<3>> <> tail)
'

elixir -e '
  import Bitwise

  number = File.read!("number.bin")
  <<before_op::binary-size(48), _op::binary-size(8), after_op::binary>> = number
  File.write!("bad-operation.bin", before_op <> <<9, 0, 0, 0, 0, 0, 0, 0>> <> after_op)

  <<before_kind::binary-size(56), _kind::binary-size(8), after_kind::binary>> = number
  File.write!("bad-body-kind.bin", before_kind <> <<9, 0, 0, 0, 0, 0, 0, 0>> <> after_kind)

  text = File.read!("text.bin")
  <<before_pointer::binary-size(72), _pointer::binary-size(8), after_pointer::binary>> = text
  File.write!("missing-text.bin", before_pointer <> <<0::64>> <> after_pointer)

  <<before_terminator::binary-size(91), _terminator::binary-size(1), after_terminator::binary>> = text
  File.write!("text-no-nul.bin", before_terminator <> <<65>> <> after_terminator)

  pointer = :binary.decode_unsigned(binary_part(text, 72, 8), :little)
  limited = pointer &&& 0x7FFFFFFFF
  new_pointer = limited ||| (1000 <<< 35)
  File.write!("text-out-of-bounds.bin", binary_part(text, 0, 72) <> <<new_pointer::little-64>> <> binary_part(text, 80, byte_size(text) - 80))

  File.write!("multi-segment.bin", <<1::little-32, 0::little-32>>)
'
