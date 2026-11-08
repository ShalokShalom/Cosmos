defmodule Scale.ListenerTest do
  use ExUnit.Case, async: false

  test "a BEAM listener doubles a number and reports the down" do
    path = "/tmp/scale-beam-listener-#{System.unique_integer([:positive])}.sock"
    on_exit(fn -> File.rm(path) end)

    task = Task.async(fn -> Scale.Listener.serve(path) end)
    wait_for_socket(path, 100)

    assert {:ok, 42.0, "down normal", 1} = Scale.Peer.run_s2(path, 21.0)
    assert {:ok, 42.0} = Task.await(task)
  end

  defp wait_for_socket(_path, 0), do: flunk("the listener socket did not appear")

  defp wait_for_socket(path, attempts) do
    case :file.read_link_info(String.to_charlist(path)) do
      {:ok, _info} ->
        :ok

      {:error, :enoent} ->
        Process.sleep(20)
        wait_for_socket(path, attempts - 1)

      {:error, reason} ->
        flunk("socket check failed: #{inspect(reason)}")
    end
  end
end
