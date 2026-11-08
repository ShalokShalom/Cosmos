defmodule Mix.Tasks.Scale.Listen do
  use Mix.Task

  @shortdoc "Serve one Scale supervised actor from a BEAM listener"

  @moduledoc """
  Accepts one Scale supervised connection and serves a BEAM worker at
  capability 1:

      mix scale.listen /tmp/scale-ipc.sock

  The worker doubles one number and reports its normal exit. This is the BEAM
  side of the first cross-language actor exchange.
  """

  @impl Mix.Task
  def run([path]) do
    Mix.Task.run("app.start")

    case Scale.Listener.serve(path) do
      {:ok, result} ->
        IO.puts("listener=ok reply=#{format_number(result)}")

      {:error, reason} ->
        IO.puts(:stderr, "listener: #{inspect(reason)}")
        System.halt(1)
    end
  end

  def run(_) do
    Mix.raise("usage: mix scale.listen PATH")
  end

  defp format_number(value) do
    if value == trunc(value) do
      Integer.to_string(trunc(value))
    else
      Float.to_string(value)
    end
  end
end
