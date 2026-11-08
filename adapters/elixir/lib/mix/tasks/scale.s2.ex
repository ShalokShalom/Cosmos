defmodule Mix.Tasks.Scale.S2 do
  use Mix.Task

  @shortdoc "Monitor and message a Scale actor IPC worker"

  @moduledoc """
  Connects to a Scale supervised listener and exercises the S2 path:

      mix scale.s2 /tmp/scale-ipc.sock 21

  The listener creates a worker at capability 1. The peer monitors it, sends
  the number, and prints the reply, the down reason, and the monitor reference.
  """

  @impl Mix.Task
  def run([path, value]) do
    Mix.Task.run("app.start")

    case Float.parse(value) do
      {number, ""} ->
        Scale.Peer.run_cli(path, number)

      _ ->
        Mix.raise("expected a number, got #{inspect(value)}")
    end
  end

  def run(_) do
    Mix.raise("usage: mix scale.s2 PATH NUMBER")
  end
end
