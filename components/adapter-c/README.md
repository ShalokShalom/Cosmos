# Scale C adapter

Targets Scale ABI version 4 (`SCALE_EXPECTED_ABI_VERSION` in
`scale_adapter.h`). A compile-time assertion keeps it equal to
`SCALE_ABI_VERSION` in `kernel/scale_abi.h`.

## What it does

`scale_adapter_open(library_path, &status)` loads the kernel shared library,
resolves the ten exports, and calls `scale_abi_version()` before any other
call. A kernel whose version differs is refused with
`SCALE_ADAPTER_VERSION_MISMATCH`; no handle is possible. The adapter owns the
library, not the kernel handle.

This is the marshalling contract, not a Scale feature description. The header,
not this file, is the ABI.

## Using it

1. Fill a `scale_callbacks` table (`alloc`, `free`, `host_dispatch`; `log` and
   `abort` may be null) and keep it alive for the handle's lifetime.
2. `scale_adapter_open`; check the status.
3. `kernel->init(&callbacks, image, image_len, &handle)`. The kernel refuses an
   image whose schema is not 2 with `SCALE_INVALID`.
4. `kernel->run(handle, entry, entry_len, &result, &len)`. `host_dispatch`
   receives the primitive request JSON and returns the response JSON; both are
   allocated through the callback table. Free `result` with your `free`.
5. `kernel->verify(handle, program, program_len, &receipt, &len)` verifies a
   term-program document and returns its canonical receipt. Free `receipt` with
   your `free`.
6. `kernel->free(handle)` and then `scale_adapter_close(adapter)`.

The adapter never calls `scale_free`; the host owns the handle. Transport and
host calls are the raw ABI exports in `scale_kernel`; `transport_send` is the
ambient send path and `transport_acquire` is the token-returning path. The
adapter does not marshal payloads beyond the header's byte buffers.

## Conformance

`conformance/conformance.c` is the executable definition of a correct
adapter. It checks the ABI version, both sides of version equality (the adapter
refuses a different ABI; the kernel refuses a different image schema), the
prelude dispatch, a declared primitive dispatch, the result, and handle
teardown.

    just conformance-check                  # compile only; runs in alpha
    just conformance-version-check          # fake kernel with a mismatched ABI version
    just conformance path/to/libScale.Kernel.so   # release only

The full run needs a native kernel shared library, which a NativeAOT publish
produces at release. Passing the harness at version 4 is the release criterion
for this adapter.
