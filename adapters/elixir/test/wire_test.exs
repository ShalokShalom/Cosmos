defmodule Scale.WireTest do
  use ExUnit.Case, async: true

  alias Scale.Wire

  defp envelope(overrides \\ %{}) do
    Map.merge(
      %Wire{
        message_id: 7,
        target: 1,
        sender: 0,
        reply: 0,
        operation: :send,
        body: {:number, 4.5}
      },
      overrides
    )
  end

  test "a numeric envelope round-trips" do
    original = envelope()

    assert {:ok, bytes} = Wire.encode(original)
    assert {:ok, decoded} = Wire.decode(bytes)
    assert decoded == original
  end

  test "a text envelope round-trips" do
    original = envelope(%{operation: :down, body: {:text, "down normal"}})

    assert {:ok, bytes} = Wire.encode(original)
    assert {:ok, decoded} = Wire.decode(bytes)
    assert decoded == original
  end

  test "every operation round-trips" do
    for operation <- [:send, :reply, :monitor, :monitor_ack, :down] do
      original = envelope(%{operation: operation})
      assert {:ok, bytes} = Wire.encode(original)
      assert {:ok, decoded} = Wire.decode(bytes)
      assert decoded.operation == operation
    end
  end

  test "a truncated frame is refused" do
    assert {:ok, bytes} = Wire.encode(envelope())
    assert {:error, :truncated_frame} = Wire.decode(binary_part(bytes, 0, 4))
  end

  test "a non-struct root pointer is refused" do
    assert {:ok, bytes} = Wire.encode(envelope())
    <<prefix::binary-size(8), rest::binary>> = bytes
    bad = prefix <> <<3>> <> binary_part(rest, 1, byte_size(rest) - 1)
    assert {:error, :invalid_root_pointer} = Wire.decode(bad)
  end

  test "an invalid body is refused" do
    assert {:error, :invalid_body} = Wire.encode(envelope(%{body: :bad}))
  end
end
