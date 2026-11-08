== Kernel ABI

The authoritative kernel contract is [`components/kernel/scale_abi.h`](../components/kernel/scale_abi.h).
The header declares the exports, the callback table, the status codes, and the
image-transfer directions. This file is prose about the header, not a second
contract.

== Current exports

The ABI has ten exports: `scale_abi_version`, `scale_init`,
`scale_set_callbacks`, `scale_run`, `scale_verify`, `scale_transport_send`,
`scale_transport_acquire`, `scale_host_call`, `scale_image`, and `scale_free`.

`scale_init` installs the callback table, validates the prelude image, and
retains it. `scale_set_callbacks` replaces that table for a live handle.
`scale_run` treats its bytes as an exported unit name and executes the unit in
the kernel abstraction. Image-resident words execute recursively; declared primitives
dispatch to `host_dispatch` with tensor operands and results. `scale_verify`
verifies a term-program document and writes its `term-receipt` over the
canonical bytes.
`scale_image` exports or imports the retained image, replaces one unit, or applies
a unit/export/group patch. `scale_host_call` forwards
a named package/function call to `host_dispatch`. `scale_transport_send` frames
the target and payload as JSON and forwards them as the `transport` function;
`scale_transport_acquire` uses the `transport-acquire` function and returns the
host's opaque token result.
== Version

The installed single version is `SCALE_ABI_VERSION` (currently 4; the image
schema moved to 2 for unit groups, D192). `SCALE_EXPECTED_ABI_VERSION` is
present in the C adapter, which checks it before resolving any other export; the
kernel image schema is version 2. An ABI-version mismatch is a deployment error,
not a compatibility negotiation. Each `scale_init` creates an independent
handle with its own callback table and image; the header states the lifetime and
concurrency rules.
== Conformance

The C conformance harness in `components/adapter-c/conformance/` is present and
runs against the NativeAOT kernel shared library in alpha:
`just kernel-aot linux-x64` publishes the kernel with NativeAOT and runs the
harness. That gate proves version equality, image validation, multi-handle
lifetime, primitive dispatch, unit replacement, unit/export/group patches, and
the freed-handle refusal against the real
native artifact (D180). The SageFs suite remains the language acceptance gate;
the kernel-only abstraction, image-loader, image-swap, and ABI lifecycle tests
are the JIT boundary proof.
