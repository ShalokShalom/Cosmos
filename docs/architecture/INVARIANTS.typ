== Architecture invariants

These are the properties the architecture must preserve. Each names the test
that currently enforces it, or says `not yet` when the invariant is adopted but
its enforcement test has not landed. The kernel boundary tests live in
`development/scale/tests.fs` under `Kernel boundary`.

#table(
  columns: (auto, 1fr, auto),
  align: (left, left, left),
  table.header([No.], [Invariant], [Enforcement]),
  [1], [The kernel runs without the Prelude loaded], [`the kernel abstraction runs without Prelude state`],
  [2], [The Prelude is swappable data], [`an image swap changes the entry result`; `scale_image imports a replacement image`],
  [3], [The kernel state has no Prelude state], [`the kernel assembly contains no Prelude state, errors, or portable IR`],
  [4], [The kernel source contains no Prelude logic], [`the kernel assembly references no Prelude assembly`],
  [5], [Removing a feature requires no kernel change], [not yet; per-feature test],
  [6], [Adding a feature requires no kernel change], [partial: image units are data; per-feature test not yet],
  [7], [Errors are attributed to the raising layer], [partial: `a kernel failure carries its named reason`; `a primitive failure carries its named reason`],
  [8], [Upcalls are real], [not yet; kernel image execution currently resolves image units, not Prelude policy upcalls],
  [9], [Version equality is enforced at load time], [`the exported ABI version is the header version`; `an unsupported image schema is refused`; the adapter's guard compiles and `just kernel-aot` runs the C conformance harness against the NativeAOT kernel in alpha (D180)],
)

A change that breaks an enforced invariant is a kernel-boundary change and must
update this file, the affected decision record, and the test in the same
change. `not yet` is a visible gap, not a silent pass.

#strong[Design principle.] The best designs are co-designed with their
environment: the theory, compiler, tooling, libraries, tests, documentation,
community, and standards that host a feature decide whether it is feasible,
usable, and maintainable. A feature without that environment is a proposal, not
a technology.
