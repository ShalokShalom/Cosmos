== Host adapter documentation

The first host adapter is C, in `components/adapter-c/`. It targets ABI version 4, loads
the kernel shared library, checks `scale_abi_version()` before any other call,
and exposes the ten exports. Its README documents loading, callbacks, and
marshalling; the ABI itself is `components/kernel/scale_abi.h`.

The conformance test is `components/adapter-c/conformance/conformance.c`: it loads a kernel
through the adapter, checks both sides of version equality, initialises a known
image, runs an exported unit that dispatches a declared primitive, verifies the
result, and frees the handle. It compiles in alpha and runs at release against a
native kernel shared library.

The BEAM wire harness is `adapters/elixir` (D156). It is not a C ABI adapter and
not a language port: it implements the actor wire subset, a supervised peer, and
a BEAM listener, and both cross-language directions pass. A Rustler NIF over the
C ABI is deferred until the Prelude is in Scale and a native kernel artifact can
be tested.

Future adapters go in `adapters/<host>/` with the same README, the ABI version
they target, and a conformance run. Adapter docs describe marshalling, never
Scale feature semantics; feature documentation belongs in `docs/prelude/`.