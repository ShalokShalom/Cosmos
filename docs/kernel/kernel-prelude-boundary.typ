= Kernel and Prelude boundary

#strong[Status: current.] This document is the single explanation of what belongs
in the native kernel, what belongs in the F\# reference Prelude, and why. It
replaces the older kernel inventory as the boundary statement; the inventory
remains useful as a historical snapshot of the dispatcher.

== The one rule

> If it is needed to load, validate, or execute an image, or to cross the host
> ABI, it belongs in the kernel. If it can be expressed as image data or a word
> and is not needed to bootstrap the image, it belongs in the Prelude. Host and
> OS access belongs in an adapter.

The kernel is the fixed point. The Prelude is the evolving language.

== The kernel

The kernel is the NativeAOT shared library built by
`components/kernel/Scale.Kernel.fsproj`. It references only `FSharp.Core`; it
does not reference `components/core`, `components/prelude`, `components/ipc`, or
`components/verification`. It has no Prelude state and no Prelude logic.

=== What is in the kernel

#table(
  columns: (1.2fr, 2.4fr, 3fr),
  align: (left, left, left),
  table.header([Area], [Modules], [Why it is native]),
  [`EntryIdentity`, `Tensor`, `Store`, `Lexicon`, `Stack`],
    [`types.fs`, `tensor.fs`, `lexicon.fs`, `store.fs`, `stack.fs`, `scalar-read.fs`, `index-transformation.fs`],
    [The image value model and the operand stack are the bootstrap data structures. Every host and every image must agree on them.],
  [Opaque host tokens],
    [`host-token-wire.fs`, `types.fs`],
    [The token is an ABI value: the host mints it, the kernel carries it, and language code cannot forge it.],
  [Image schema and loading],
    [`prelude-image.fs`],
    [The kernel validates names, arity, captures, groups, kinds, predicates, jumps, capabilities, dependencies, schema, and form before retaining an image.],
  [Lowered execution],
    [`lowered-instructions.fs`, `kernel-state.fs`, `lowered-abstraction.fs`, `prelude-execution.fs`],
    [The kernel runs the image's instruction stream. It cannot depend on the F\# evaluator it is meant to replace.],
  [Portable term and verification],
    [`symbolic-abstraction.fs`, `term-portable.fs`, `term-verification.fs`],
    [The term boundary and structural verifier are the execution authority and must be available without the Prelude.],
  [Primitive wire],
    [`primitive-wire.fs`],
    [Declared host primitives cross the ABI as tensors with a checked result count; the kernel owns the protocol.],
  [C ABI],
    [`abi.fs`, `scale_abi.h`],
    [The ABI is the host contract: ten exports at version 4, callbacks, status codes, image transfer, host calls, transport send/acquire, and handle lifetime.],
)

=== Why the kernel is small

- **Bootstrap.** The image loader and instruction executor must exist before an
  image-resident language can run.
- **Portability.** The kernel is the piece a non-F\# host can link. It must not
  pull in the F\# evaluator, typing, or host policy.
- **NativeAOT.** The kernel is published as a NativeAOT shared library. It uses
  no reflection, `%A` serialization, `Expression.Compile`, quotation evaluation,
  `Reflection.Emit`, or `Assembly.Load`.
- **Trust boundary.** The kernel validates the image and executes only the
  instruction set it understands. The Prelude may reject more; it cannot
  authorize an image the kernel rejects.
- **One authority per fact.** Tensor shape, store identity, image schema, and
  ABI status live once, in the kernel.

=== What is deliberately not in the kernel

- word dispatch policy;
- classes, slots, instances, precedence, method combination, metaclasses;
- conditions and restarts;
- actor scheduling, supervision, mailboxes, channels, and events;
- set algebra, sorts, predicates, constructors, and dispatch;
- ownership, linearity, history, bookmarks, and rollback;
- pattern matching policy and analysis;
- prose parsing, books, and spells;
- SMT, refinement, and the verification lattice;
- any host adapter, filesystem, network, or Incus behavior.

Those are language or host policy. They change faster than the bootstrap, and
most can become image data or a self-hosted Prelude.

== The F\# reference Prelude

The Prelude is `components/prelude/Scale.Prelude.fsproj`. It references
`components/core`, `components/verification/typing`, and the kernel. It is the
reference evaluator, not the NativeAOT product.

=== What is in the Prelude

#table(
  columns: (1.2fr, 2.4fr, 3fr),
  align: (left, left, left),
  table.header([Area], [Modules], [Why it is here]),
  [Dispatch and words],
    [`evaluator/evaluator.fs`, `word.fs`, `callable.fs`, `signatures.fs`, `dispatch.fs`],
    [Word resolution, built-in cases, user words, generic functions, and the callable contract are language policy.],
  [Control and closures],
    [`evaluator/control.fs`, `closure.fs`, `program.fs`, `code.fs`, `quotation-run.fs`],
    [`quotation`, `if`, `apply`, `define`, `let`, `lambda`, code values, and frames are language semantics.],
  [Frames and environments],
    [`evaluator/environment.fs`, `with-fields.fs`, `cell-system.fs`, `class-system.fs`],
    [Lexical frames, bindings, closures, classes, slots, instances, and precedence are image-resident or portable language behavior.],
  [Pattern matching],
    [`evaluator/pattern.fs`],
    [The runtime matcher is language behavior; the portable pattern IR is in `core`.],
  [Sets, sorts, and conditions],
    [`evaluator/value-sets.fs`, `set-algebra.fs`, `condition.fs`, `runtime-guard.fs`, `channel-event.fs`],
    [Set algebra, predicates, constructors, conditions, restarts, guards, and event selection are language policy.],
  [Ownership and history],
    [`evaluator/ownership.fs`, `bookmark.fs`, `history.fs`],
    [Ownership checks, bookmarks, rollback, and the event stream are interpreter features; compiled artifacts exclude them.],
  [Actors and channels],
    [`scheduler.fs`, `evaluator/scheduler.fs`, `channel.fs`],
    [Scheduling, supervision, mailboxes, links, monitors, and channels are language/runtime policy.],
  [Lowering and writer],
    [`evaluator/lowering.fs`, `writer-runtime.fs`, `writer/il-writer.fs`],
    [The expression-to-lowered compiler and the JIT-only IL writer are compiler/tooling, not the kernel.],
  [Kernel bridge],
    [`evaluator/kernel-bridge.fs`],
    [The adapter between the F\# evaluator and kernel callbacks; it is not part of the kernel or a portable language module.],
)

=== Why the Prelude is F\#

- It is the reference implementation and the differential oracle while the
  image-resident language is ported.
- Its behavior changes as the language grows; the kernel should not change for
  every new word, class rule, or actor policy.
- It may use host F\# features that are unsuitable for the NativeAOT kernel.
- It is the place to prototype a word before deciding whether the mechanism is
  kernel material.

The image-resident Prelude is the target, not a second runtime. The F\# Prelude
stays until a slice is ported and differentially proven.

== `core` and `typing` are neither

- `components/core` is host-side shared data and IR: runtime types, patterns,
  prose parser, package/portable documents, persistence, vocabulary, ownership
  types, the scale file, and declarative IO. It is not in the kernel ABI and not
  in the language Prelude.
- `components/verification/typing` is the host verification/type layer: rows,
  sets, records, kinded unification, type retrieval, and the lattice. It is not
  the kernel and not the F\# evaluator.

Keeping these separate prevents the kernel from acquiring host policy and
prevents the Prelude from becoming a dependency of every host.

== Boundary tests

- The kernel assembly must not reference the Prelude, `core`, `ipc`, or
  `verification`; the kernel boundary tests enforce the reference separation.
- The image loader rejects invalid images before the Prelude sees them.
- The Prelude and kernel agree on the shared closed fragment through differential
  tests.
- A kernel change requires `just kernel-aot` after the change; a Prelude change
  requires the F\# suite and the image differential.

== Change rule

A new explicit kernel case, a new image field, or a new ABI export requires an
inventory update and a decision record in the same change. A new language word,
class rule, condition, actor policy, or pattern behavior goes to the Prelude or
the image first. No new F\# kernel case is added silently.
