= Backlog dependency graph

#strong[This page is current.] It orders the remaining project work. The current
state is `docs/now.typ`; completed history is `docs/archive/roadmap-archive.typ`; the
previous R0--R13 and W0--W8 orderings are archived there.

== Ground rule

Every item follows the same workflow: Critical Planning, documentation, tests
derived from the contract, implementation to green, specification, then commit.
A change that adds execution state also adds a root-registry entry and a
persistence test. A change that adds a product mode also adds an AOT smoke.

#strong[No feature is deferred for convenience.] Phase gates are explicit: for
example, self-typing follows a working self-hosted compiler. The ordering exists
so shared substrate and decisions land once, at the root, and leaves do not get
rewritten.

== Current workstreams

```text
P0  Docs and defects
      stale claims in now, type-map, row-and-set-types, effect-handlers,
      effect-polymorphism, irs, term-ir, and backlog-graph
      the missing `Code` case in the host/reference bridge
      the `condition` registry signature

W1  IR contract and differential matrix
      W1a instruction document for code values, history, and verification
      W1b the interpreter program becomes instructions; the token reader
          becomes a reader into that encoding, not a second encoding
      W1c delete the term form and its document
      one semantic term family + one lowered IR as the target
      start with cross-IR differential tests, not a big-bang rewrite
      pattern and package remain dialects/containers; portable docs are projections

F   Function values and currying (D172)
      F1 signature record carried by a closure in both engines
      F2 automatic currying, left-associative, multi-result refused
      F3 explicit holes: `PartialApplication`, `ArgumentHole`
      the calling convention handlers and host calls both consume

W2  General typed handlers
      W2a effect labels become identity, on the frame and in the type rule
      W2b a general handler expression, one shot
      consumes the W1 code contract and the F calling convention

S   Sequences and recursive types (D132)
      ordered projector and words; recursive reference and a fixpoint rule
      a self-hosting prerequisite, because a compiler works over sequences

W3  Logic/host unification
      W3a finish the resolver: F-sharp primitive and book word cases, builtins
          through Invoke instead of a token re-dispatch
      W3b books through the kernel instead of back through the F-sharp evaluator
      W3c the F-sharp package executes; recursion classification tiers A and B
      W3d an Elixir call contract, which does not exist yet
      W3e prose executes only; definitions come from books
      logic definitions provide prose implementations

P4  Inference coverage
      the `NotInferred` words, `PatternMatch`, and both directions of the
      host/reference bridge including `Code`

L   Lowering completeness
      pattern guards, and the declaration/message registry contract

W4  Self-hosting and self-typing
      W4a working self-hosted compiler/port path
      W4b self-typing of Scale quotations and type representations

W5  Hot-swap hardening
      C conformance for pins/state, non-state ABI gating, D Phase 2 token value kind

W6  Declarative IO and prose execution
      vision adapter, Incus/mount/watch, `.install` discovery, prose execution surface

W7  Actor / IPC
      mailbox and channel protocol types, supervision contract, full S1/S2,
      remote send

W8  Verification bridges and ownership
      D25 <-> P4 static types, D12 <-> P4 runtime sorts, typed term/verification,
      OwnershipTypes probe

W9  Security, Nu, and performance
      read paths, network policy, package signatures, Nu node integration,
      benchmark/product performance

W10 Product and release
      NativeAOT product modes, CLI/demo smoke, docs/release archive

W0  Release gate
      explicit `just release`: Fantomas, full suites, NativeAOT, count refresh
```

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Id], [Item], [Blocked by / why]),
  table.hline(),
  [P0], [Docs and defects: eight stale claims, the missing `Code` bridge case, the `condition` signature], [Cheap and first. Planning on a false fact orders the work wrongly.],
  [W1], [IR contract: instruction document (W1a), interpreter program becomes instructions (W1b), delete the term form (W1c)], [Root. Execution state is term-typed: `EvaluationState.Program`, history steps, code values, and `scale_verify`. W1a must land before anything touches `Code`.],
  [F], [Function values and currying (D172): signature record, automatic currying, explicit holes], [W1c, so the calling convention is built once on the final encoding. Consumed by W2 and W3.],
  [W2], [General typed handlers: effect labels as identity (W2a), a general handler expression (W2b), one shot], [W1a for a `Code` that can carry effects; F for the calling convention. No resumption.],
  [S], [Sequences and recursive types (D132): ordered projector, words, fixpoint rule], [W1c and F. A compiler works over sequences, so W4a needs it.],
  [W3], [Logic/host unification: resolver completion (W3a), books through the kernel (W3b), F\# package execution (W3c), an Elixir call contract (W3d), prose executes only (W3e)], [W2 for effect and code values; F for signatures. Elixir has no call operation yet, so W3d is new work, not unification.],
  [P4], [Inference coverage: the `NotInferred` words, `PatternMatch`, both directions of the host/reference bridge], [Runs beside W2. W4b needs it; a handler rule cannot be general while most words have no signature.],
  [L], [Lowering completeness: pattern guards, declaration/message registry], [W2a for the registry contract that declarations share with effect labels.],
  [W4a], [Self-hosting: working self-hosted compiler/port path], [W1, F, S, and W3b. Books must run in the kernel before a compiler can be self-hosted.],
  [W4b], [Self-typing of Scale quotations and type representations], [W4a and P4. Do not fake it with a `Sort "type"` label.],
  [W5], [Hot-swap hardening: C conformance for pins/state, non-state ABI gating, D Phase 2 token value kind], [W7 for protocol types; core hot swap is already landed.],
  [W6], [Declarative IO: vision adapter, Incus/mount/watch, `.install` discovery, prose execution surface], [W3 for the host-call contract; lower layers are landed.],
  [W7], [Actor/IPC: mailbox and channel protocols, supervision contract, full S1/S2, remote send], [W2/W3 for effect and protocol types; W5 for hot-swap state.],
  [W8], [Verification bridges: D25/P4, D12/P4, typed term/verification, OwnershipTypes probe], [W4b for a self-typed term representation. Ownership is a runtime/ownership probe, not a D25 bridge.],
  [W9], [Security, Nu, and performance: read paths, network policy, package signatures, Nu node, benchmarks], [W3/W5/W7 for the host, hot-swap, and actor surfaces.],
  [W10], [Product/release: NativeAOT product modes, CLI/demo smoke, docs archive], [W1--W9 must settle first.],
  [W0], [Release gate: `just release`], [Explicit user call only. Documented counts stay frozen until then.],
)

== Why this order

+ #strong[W1 is the IR root.] Execution state is term-typed, so the retiral starts
  with an instruction document, not with a deletion.
+ #strong[F is the calling convention.] A handler is a function value with
  effects, and both host lanes need one signature to compare. Building it after
  W2 means writing that convention twice.
+ #strong[W2 is the language root.] The `Code` type is landed, but effect labels
  are bare strings, the runtime frame carries none, and `with-handler` is not in
  the word registry.
+ #strong[W3 unifies implementations.] `logic` is the definition language; prose
  executes it. F\# and Elixir packages must expose the same callable contract.
+ #strong[W4a precedes self-typing.] Self-typing is Phase 3 after a working
  self-hosted compiler; IR consolidation alone does not satisfy it.
+ #strong[W5 is hardening.] Core hot swap is landed; protocol types and C
  conformance remain.
+ #strong[W6 and W7 are host surfaces.] They need the host-call and protocol
  contracts, not another evaluator.
+ #strong[W8 follows self-typing.] A verification bridge without a self-typed
  term representation would be another parallel model.
+ #strong[W9 and W10 are last.] Security, Nu, performance, product, and release
  verify settled work.

== Completed baseline

- Core language, `logic`/`cycle`, books, prose first slice, shell effects.
- Type-system A/B/C, open subtraction with lacks, code values, closed subtyping.
- Hot swap: image import, unit replacement, unit/export/group patches, pins,
  previous-image rollback, retained state, serialized handle calls.
- Declarative IO lower layers; host pattern boundary; ABI 4; C conformance.
- Generated specification projection; IR and case-study notes.

== Read budget

Read `docs/now.typ` first. Then read the active W item, its specification
section, and its feature document. Use `doc_get_section` for one decision or
archive section. Do not read `docs/archive/roadmap-archive.typ`,
`docs/process/decision-records.typ`, or the full `.typ` set end to end.
