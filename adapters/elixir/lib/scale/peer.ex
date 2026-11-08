defmodule Scale.Peer do
  @moduledoc """
  The first BEAM actor peer.

  It connects to a Scale supervised listener, monitors capability 1, sends one
  number, reads the reply, and reads the worker's down message.
  """

  alias Scale.Wire

  @spec run_s2(String.t(), float(), timeout()) ::
          {:ok, float(), String.t(), non_neg_integer()} | {:error, term()}
  def run_s2(path, value, timeout \\ 5000) do
    case connect(path, timeout) do
      {:ok, socket} ->
        try do
          with :ok <-
                 Wire.write(socket, %Wire{
                   message_id: 1,
                   target: 1,
                   sender: 0,
                   reply: 0,
                   operation: :monitor,
                   body: {:number, 0.0}
                 }),
               {:ok, acknowledgement} <- Wire.read(socket, timeout),
               {:ok, reference} <- monitor_reference(acknowledgement),
               :ok <-
                 Wire.write(socket, %Wire{
                   message_id: 2,
                   target: 1,
                   sender: 0,
                   reply: 0,
                   operation: :send,
                   body: {:number, value}
                 }),
               {:ok, reply} <- Wire.read(socket, timeout),
               {:ok, result} <- reply_value(reply),
               {:ok, down} <- Wire.read(socket, timeout),
               {:ok, reason} <- down_reason(down) do
            {:ok, result, reason, reference}
          end
        after
          :gen_tcp.close(socket)
        end

      {:error, reason} ->
        {:error, reason}
    end
  end

  @spec run_cli(String.t(), float()) :: :ok
  def run_cli(path, value) do
    case run_s2(path, value) do
      {:ok, result, reason, reference} ->
        IO.puts("reply=#{format_number(result)} down=#{reason} monitor=#{reference}")

      {:error, reason} ->
        IO.puts(:stderr, "beam: #{inspect(reason)}")
        System.halt(1)
    end
  end

  @spec connect(String.t(), timeout()) :: {:ok, :gen_tcp.socket()} | {:error, term()}
  def connect(path, timeout \\ 5000) do
    :gen_tcp.connect(
      {:local, String.to_charlist(path)},
      0,
      [:binary, {:active, false}, {:packet, :raw}],
      timeout
    )
  end

  defp monitor_reference(%Wire{operation: :monitor_ack, body: {:number, reference}}) do
    {:ok, trunc(reference)}
  end

  defp monitor_reference(other), do: {:error, {:unexpected_monitor_ack, other}}

  defp reply_value(%Wire{operation: :reply, body: {:number, value}}), do: {:ok, value}
  defp reply_value(other), do: {:error, {:unexpected_reply, other}}

  defp down_reason(%Wire{operation: :down, body: {:text, reason}}), do: {:ok, reason}
  defp down_reason(other), do: {:error, {:unexpected_down, other}}

  defp format_number(value) do
    if value == trunc(value) do
      Integer.to_string(trunc(value))
    else
      Float.to_string(value)
    end
  end
end
