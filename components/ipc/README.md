# ipc

The actor IPC boundary.

- `wire.fs` implements the first Scale actor wire subset: a single-segment
  Cap'n Proto message with a root struct, seven data words, one pointer word,
  numeric or text bodies, and send/reply/monitor/monitor-ack/down operations.
- `actor-host.fs` binds a Unix socket, delivers a numeric envelope to a root or
  worker actor mailbox, runs the actor, and returns the reply. It also carries
  a capability table and a remote monitor down.

The component depends on `core` and `prelude`. It does not change the kernel
ABI or the local actor words. The first slice is one connection and one worker;
discovery, authentication, far pointers, and reference conformance are planned.
See `docs/kernel/actor-ipc.typ` and `docs/kernel/binary-format.typ`.
