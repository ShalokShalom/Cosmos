# Elixir / BEAM actor peer

The first BEAM instance in the actor IPC. It is not yet a full Scale language
port; it is the wire and actor boundary that the port starts from.

- `lib/scale/wire.ex` encodes and decodes the Scale actor wire subset:
  one segment, a root struct, seven data words, one pointer word, numeric and
  text bodies, and the send/reply/monitor/monitor-ack/down operations.
- `lib/scale/peer.ex` connects to a Scale supervised listener, monitors
  capability 1, sends a number, reads the reply, and reads the down reason.
- `lib/scale/listener.ex` is the reverse direction: a BEAM listener creates a
  BEAM worker process, acknowledges the monitor, doubles a number, and reports
  its normal exit. `mix scale.listen PATH` exposes it.
- `mix scale.s2 PATH NUMBER` exposes the peer from the command line.

## Run

From this directory:

```sh
mix test
mix scale.s2 /tmp/scale-ipc.sock 21
```

The listener must already be running:

```sh
../../bases/scale-cli/bin/Debug/net10.0/scale --wire-listen /tmp/scale-ipc.sock
```

The peer prints `reply=42 down=down normal monitor=1`.

## Cross-language check

```sh
./scripts/check_s2.sh
```

The first script starts the F# supervised listener, runs the Elixir peer, and
checks the exact reply, down reason, and monitor reference. The reverse script
starts the BEAM listener and runs the F# supervised client:

```sh
./scripts/check_s2.sh
./scripts/check_fsharp_client.sh
```

## Status

The BEAM wire harness. It proves the wire and the actor boundary across hosts.
The old architecture stays: the kernel, the F\# Prelude, and the host adapters
remain the shipped path. A full BEAM language port and a Rustler C ABI adapter
are deferred until the Prelude is in Scale and a native kernel can be tested.
See D156 in `docs/process/decision-records.typ`.
