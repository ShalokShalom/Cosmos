# Scale conformance test

`conformance.c` is the executable definition of a correct host adapter. It
loads a kernel through the C adapter, checks the ABI version and both sides of
version equality, initialises a known image, runs an exported unit that
dispatches a declared primitive, verifies the result, and frees the handle.

- `just conformance-check` compiles the adapter, the harness, the fake kernel,
  and the version probe with `cc`. It runs during alpha.
- `just conformance-version-check` builds a fake kernel whose
  `scale_abi_version()` returns 2 and proves the adapter refuses it before
  resolving any other symbol. It runs during alpha.
- `just conformance <library>` builds and runs the full harness against a native
  kernel shared library. It is release-only, because a native kernel needs a
  NativeAOT publish.

The adapter is documented in `../README.md`; the ABI is
`components/kernel/scale_abi.h`.
