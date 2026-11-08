defmodule Scale.Listener do
  @moduledoc """
  The first BEAM listener.

  It accepts one Scale supervised connection, creates one BEAM worker process,
  acknowledges a monitor, doubles one number, and reports the worker's normal
  exit. The worker is a real BEAM process with a mailbox; no Scale evaluator is
  involved yet.
  """

  alias Scale.Wire

  @spec serve(String.t()) :: {:ok, float()} | {:error, term()}
  def serve(path) do
    socket_path = String.to_charlist(path)
    File.rm(path)

    with {:ok, listener} <-
           :gen_tcp.listen(0, [
             :binary,
             {:active, false},
             {:packet, :raw},
             {:ifaddr, {:local, socket_path}}
           ]) do
      try do
        case :gen_tcp.accept(listener) do
          {:ok, socket} ->
            try do
              handle(socket)
            after
              :gen_tcp.close(socket)
            end

          {:error, reason} ->
            {:error, reason}
        end
      after
        :gen_tcp.close(listener)
        File.rm(path)
      end
    end
  end

  defp handle(socket) do
    with {:ok, monitor} <- Wire.read(socket),
         :ok <- expect(monitor, :monitor),
         {:ok, worker} <- start_worker(),
         :ok <-
           Wire.write(socket, %Wire{
             message_id: monitor.message_id + 1,
             target: monitor.sender,
             sender: monitor.target,
             reply: 0,
             operation: :monitor_ack,
             body: {:number, 1.0}
           }),
         {:ok, send} <- Wire.read(socket),
         :ok <- expect(send, :send),
         {:ok, value} <- number_body(send),
         {:ok, result} <- call_worker(worker, value),
         :ok <-
           Wire.write(socket, %Wire{
             message_id: send.message_id + 1,
             target: send.sender,
             sender: send.target,
             reply: 0,
             operation: :reply,
             body: {:number, result}
           }),
         {:ok, reason} <- await_down(worker),
         :ok <-
           Wire.write(socket, %Wire{
             message_id: send.message_id + 2,
             target: send.sender,
             sender: send.target,
             reply: 0,
             operation: :down,
             body: {:text, "down " <> reason_name(reason)}
           }) do
      {:ok, result}
    end
  end

  defp expect(%Wire{operation: operation}, operation), do: :ok
  defp expect(_envelope, operation), do: {:error, {:expected, operation}}

  defp number_body(%Wire{body: {:number, value}}), do: {:ok, value}
  defp number_body(_envelope), do: {:error, :expected_number}

  defp start_worker do
    pid = spawn(fn -> worker_loop() end)
    reference = Process.monitor(pid)
    {:ok, {pid, reference}}
  end

  defp worker_loop do
    receive do
      {:message, from, value} ->
        send(from, {:reply, value * 2})
    end
  end

  defp call_worker({pid, _reference}, value) do
    send(pid, {:message, self(), value})

    receive do
      {:reply, result} -> {:ok, result}
    after
      5000 -> {:error, :worker_timeout}
    end
  end

  defp await_down({pid, reference}) do
    receive do
      {:DOWN, ^reference, :process, ^pid, reason} -> {:ok, reason}
    after
      5000 -> {:error, :down_timeout}
    end
  end

  defp reason_name(:normal), do: "normal"
  defp reason_name(:killed), do: "killed"
  defp reason_name(reason), do: inspect(reason)
end
