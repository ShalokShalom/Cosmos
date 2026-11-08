defmodule Scale.PeerTest do
  use ExUnit.Case, async: true

  test "a missing socket is an error" do
    path = "/tmp/scale-missing-#{System.unique_integer([:positive])}.sock"
    assert {:error, _reason} = Scale.Peer.run_s2(path, 1.0)
  end
end
