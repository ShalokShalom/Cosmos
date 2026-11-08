defmodule Scale.Wire do
  @moduledoc """
  The Scale actor wire subset.

  One segment, one root struct, seven data words, one pointer word, and a
  numeric or text body. The operations are send, reply, monitor, monitor
  acknowledgement, and down.
  """

  import Bitwise

  @max_message_bytes 1_048_576

  @operation_codes %{send: 0, reply: 1, monitor: 2, monitor_ack: 3, down: 4}
  @operations Map.new(@operation_codes, fn {name, code} -> {code, name} end)

  defstruct [:message_id, :target, :sender, :reply, :operation, :body]

  @type operation :: :send | :reply | :monitor | :monitor_ack | :down
  @type body :: {:number, float()} | {:text, binary()}
  @type t :: %__MODULE__{
          message_id: non_neg_integer(),
          target: non_neg_integer(),
          sender: non_neg_integer(),
          reply: non_neg_integer(),
          operation: operation(),
          body: body()
        }

  @spec encode(t()) :: {:ok, binary()} | {:error, term()}
  def encode(%__MODULE__{} = envelope) do
    with {:ok, operation} <- operation_code(envelope.operation),
         {:ok, kind, number, text} <- body_parts(envelope.body) do
      text_bytes = text || <<>>
      text_len = if text, do: byte_size(text_bytes) + 1, else: 0
      text_words = div(text_len + 7, 8)
      total_words = 9 + text_words
      total_bytes = 8 + total_words * 8

      if total_bytes > @max_message_bytes do
        {:error, :message_too_large}
      else
        root = (7 <<< 32) ||| (1 <<< 48)
        text_pointer = if text, do: 1 ||| (2 <<< 32) ||| (text_len <<< 35), else: 0

        header = <<0::little-32, total_words::little-32>>

        words =
          <<root::little-64, envelope.message_id::little-64, envelope.target::little-64,
            envelope.sender::little-64, envelope.reply::little-64, operation::little-64,
            kind::little-64, number::little-float-64, text_pointer::little-64>>

        text_part =
          if text do
            padding = rem(8 - rem(text_len, 8), 8)
            text_bytes <> <<0>> <> :binary.copy(<<0>>, padding)
          else
            <<>>
          end

        {:ok, header <> words <> text_part}
      end
    end
  end

  @spec decode(binary()) :: {:ok, t()} | {:error, term()}
  def decode(message) when is_binary(message) do
    case message do
      <<0::little-32, words::little-32, segment::binary>> when byte_size(segment) == words * 8 ->
        decode_segment(words, segment)

      <<count::little-32, _::binary>> when count != 0 ->
        {:error, {:segment_count_unsupported, count + 1}}

      <<_::little-32, words::little-32, segment::binary>> ->
        {:error, {:segment_size_mismatch, words * 8, byte_size(segment)}}

      _ ->
        {:error, :truncated_frame}
    end
  end

  def decode(_), do: {:error, :truncated_frame}

  @spec read(:gen_tcp.socket(), timeout()) :: {:ok, t()} | {:error, term()}
  def read(socket, timeout \\ 5000) do
    case :gen_tcp.recv(socket, 8, timeout) do
      {:ok, <<0::little-32, words::little-32>> = header} ->
        case :gen_tcp.recv(socket, words * 8, timeout) do
          {:ok, segment} -> decode(header <> segment)
          {:error, reason} -> {:error, reason}
        end

      {:ok, <<count::little-32, _::binary>>} ->
        {:error, {:segment_count_unsupported, count + 1}}

      {:error, reason} ->
        {:error, reason}
    end
  end

  @spec write(:gen_tcp.socket(), t()) :: :ok | {:error, term()}
  def write(socket, envelope) do
    with {:ok, bytes} <- encode(envelope) do
      :gen_tcp.send(socket, bytes)
    end
  end

  def describe_error(error) do
    case error do
      :message_too_large -> "wire: message is over the limit"
      :truncated_frame -> "wire: truncated frame"
      :invalid_root_pointer -> "wire: the root pointer is not a struct pointer"
      :struct_out_of_bounds -> "wire: the struct is outside the segment"
      :unexpected_text_pointer -> "wire: a numeric body carries a text pointer"
      :missing_text_pointer -> "wire: a text body has no text pointer"
      :invalid_text_pointer -> "wire: the text pointer is not a byte list"
      :text_out_of_bounds -> "wire: the text is outside the segment"
      :text_missing_terminator -> "wire: the text has no NUL terminator"
      :invalid_body -> "wire: the body is not numeric or text"
      {:segment_count_unsupported, count} -> "wire: #{count} segments are not supported yet"
      {:segment_size_mismatch, expected, actual} -> "wire: expected #{expected} bytes, got #{actual}"
      {:data_section_too_small, words} -> "wire: the data section has #{words} words, expected 7"
      {:pointer_section_too_small, words} -> "wire: the pointer section has #{words} words, expected 1"
      {:invalid_operation, operation} -> "wire: operation #{inspect(operation)} is not defined"
      {:invalid_body_kind, kind} -> "wire: body kind #{kind} is not defined"
      other -> "wire: #{inspect(other)}"
    end
  end

  defp decode_segment(words, segment) when words >= 9 do
    root = read_u64(segment, 0)

    if (root &&& 3) != 0 do
      {:error, :invalid_root_pointer}
    else
      offset = signed_offset(root)
      data_words = (root >>> 32) &&& 0xFFFF
      pointer_words = (root >>> 48) &&& 0xFFFF
      struct_word = 1 + offset

      cond do
        data_words < 7 ->
          {:error, {:data_section_too_small, data_words}}

        pointer_words < 1 ->
          {:error, {:pointer_section_too_small, pointer_words}}

        struct_word < 0 or struct_word + data_words + pointer_words > words ->
          {:error, :struct_out_of_bounds}

        true ->
          struct_byte = struct_word * 8
          message_id = read_u64(segment, struct_byte)
          target = read_u64(segment, struct_byte + 8)
          sender = read_u64(segment, struct_byte + 16)
          reply = read_u64(segment, struct_byte + 24)
          operation_code = read_u64(segment, struct_byte + 32)
          body_kind = read_u64(segment, struct_byte + 40)
          number = read_float(segment, struct_byte + 48)
          text_pointer = read_u64(segment, struct_byte + data_words * 8)

          with {:ok, operation} <- operation_from_code(operation_code),
               {:ok, body} <-
                 decode_body(
                   segment,
                   struct_word,
                   data_words,
                   body_kind,
                   number,
                   text_pointer
                 ) do
            {:ok,
             %__MODULE__{
               message_id: message_id,
               target: target,
               sender: sender,
               reply: reply,
               operation: operation,
               body: body
             }}
          end
      end
    end
  end

  defp decode_segment(_words, _segment), do: {:error, :truncated_frame}

  defp decode_body(_segment, _struct_word, _data_words, 0, number, 0),
    do: {:ok, {:number, number}}

  defp decode_body(_segment, _struct_word, _data_words, 0, _number, _text_pointer),
    do: {:error, :unexpected_text_pointer}

  defp decode_body(_segment, _struct_word, _data_words, 1, _number, 0),
    do: {:error, :missing_text_pointer}

  defp decode_body(segment, struct_word, data_words, 1, _number, text_pointer) do
    if (text_pointer &&& 3) != 1 do
      {:error, :invalid_text_pointer}
    else
      offset = signed_offset(text_pointer)
      element_size = (text_pointer >>> 32) &&& 0x7
      count = (text_pointer >>> 35) &&& 0x1FFFFFFF
      text_word = struct_word + data_words + 1 + offset

      cond do
        element_size != 2 ->
          {:error, :invalid_text_pointer}

        count <= 0 ->
          {:error, :text_missing_terminator}

        text_word < 0 or text_word * 8 + count > byte_size(segment) ->
          {:error, :text_out_of_bounds}

        true ->
          text_byte = text_word * 8
          text = binary_part(segment, text_byte, count)

          if binary_part(text, count - 1, 1) != <<0>> do
            {:error, :text_missing_terminator}
          else
            {:ok, {:text, binary_part(text, 0, count - 1)}}
          end
      end
    end
  end

  defp decode_body(_segment, _struct_word, _data_words, kind, _number, _text_pointer),
    do: {:error, {:invalid_body_kind, kind}}

  defp body_parts({:number, value}) when is_float(value), do: {:ok, 0, value, nil}
  defp body_parts({:text, value}) when is_binary(value), do: {:ok, 1, 0.0, value}
  defp body_parts(_), do: {:error, :invalid_body}

  defp operation_code(operation) when is_atom(operation) do
    case Map.fetch(@operation_codes, operation) do
      {:ok, code} -> {:ok, code}
      :error -> {:error, {:invalid_operation, operation}}
    end
  end

  defp operation_code(other), do: {:error, {:invalid_operation, other}}

  defp operation_from_code(code) do
    case Map.fetch(@operations, code) do
      {:ok, operation} -> {:ok, operation}
      :error -> {:error, {:invalid_operation, code}}
    end
  end

  defp read_u64(binary, offset) do
    :binary.decode_unsigned(binary_part(binary, offset, 8), :little)
  end

  defp read_float(binary, offset) do
    <<value::little-float-64>> = binary_part(binary, offset, 8)
    value
  end

  defp signed_offset(pointer) do
    raw = (pointer >>> 2) &&& 0x3FFFFFFF
    if raw >= 0x20000000, do: raw - 0x40000000, else: raw
  end
end
