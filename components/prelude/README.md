# Scale Prelude (reference)

The evaluator and its runtime: the stack, history, quotation runner, lowering,
slots, frames, closures, scheduler, sets, classes, conditions, ownership,
dispatch, words, the actor/channel runtime, the kernel bridge, the writer
runtime, and the JIT-only IL writer behind `ENABLE_WRITER`.

`Scale.Prelude.fsproj`; depends on `components/core`, `components/typing`, and
the kernel. The boundary and the reason each mechanism is Prelude-side are in
`docs/kernel/kernel-prelude-boundary.typ`.
