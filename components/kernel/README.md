# Scale kernel

The native kernel: core types, tensors, store, lowered instructions and VM, term
IR and structural verifier, image loading, primitive wire, and the C ABI. The
interface is `scale_abi.h` at ABI version 4; the image schema is version 2. The kernel has
no Prelude logic and does not know about any host adapter.

`Scale.Kernel.fsproj` builds the NativeAOT shared library. The boundary and the
reason each mechanism is native are in `docs/kernel/kernel-prelude-boundary.typ`.
