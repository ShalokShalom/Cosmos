#strong[Status: rejected by D151.] This document is kept as the record of the
dropped direction. It is not a design target. The current integration model is
the actor IPC in `docs/kernel/actor-ipc.typ`.


= The unified Julia/Elixir language against Scale

The proposal in `Notes/archive/Elixir-and-Julia.md` is a complete, general-purpose
programming language that hosts every construct of Julia and Elixir behind
distinct tokens. It is not an extension of Scale's current surface. It is the
semantic core that Scale compiles into.

The unified language is the programmable intermediate representation and
semantic core one level below Basic English. Basic English is a restricted,
declarative front-end that compiles into it. A programmer may write the unified
language directly to use its full expressiveness; Basic English may use only a
subset. The unified language owns its types, dispatch, values, modules, macros,
and runtime.

Scale has no independent implementation or semantics separate from the unified
language. Its features are implemented in the unified language. The current
Scale implementation is the starting implementation of the unified language:
parts are reused, parts are extended, and parts are replaced. There is no
separate "Scale runtime" beside the unified runtime.

This document records the collision between the proposal and the current
implementation, recommends the implementation stack, and names the tests that
must exist before the refactor starts. It changes no code.

== What the proposal is

The proposal has four commitments that decide every later question:

+ #strong[Two dispatch constructs.] `method` is Julia's typed, unordered,
  specificity-resolved, open dispatch. `clause` is Elixir's ordered,
  first-match, pattern-and-guard dispatch. They are never the same keyword.
+ #strong[A reified type hierarchy.] The runtime carries `Any`, `Number`,
  `Real`, `Integer`, `Bool <: Integer` and `Bool <: Atom`, `Symbol` distinct
  from `Atom`, `Nothing`, `Missing`, parametric types, unions, `where`
  constraints, abstract types, and primitive types.
+ #strong[A general runtime.] It provides processes and messages, tasks and
  typed channels, OTP behaviours as groups, records as tagged tuples, bit
  syntax, modules, and two macro systems.
+ #strong[Total coverage.] Every source construct is hosted. The only rejected
  programs are structurally invalid mixes of the two dispatch grammars,
  modifiers on bodies, and unmarked macros.

The single test is: if a programmer cannot tell from the keyword or operator
alone which source language's semantics apply, the construct is not clean
enough.

== What Scale is today

The current implementation is a small, verification-oriented language and image
runtime. It is the starting implementation of the unified language, not a
separate Scale runtime.

+ #strong[Surface.] Scale Basic English: controlled sentences such as
  "Every player that is alive can jump." The surface is deliberately restricted
  and refuses unnamed constructs by name.
+ #strong[Values and execution.] Tensors, an append-only image, a stack, and a
  cooperative actor scheduler. Execution state is never serialized; the image
  is the durable authority.
+ #strong[Types.] `DomainExpression.FieldType` has four sorts: `BooleanType`,
  `IntegerType`, `NaturalType`, and `RealType`. The verification lattice adds
  finite or symbolic coordinates and single-field narrowing guards.
+ #strong[Dispatch.] Image-resident generic functions and methods (D19/W10),
  type-first over the stack's sorts, with extension-subset specificity and
  named ambiguity. There is no `clause`, no `where`, no open/closed modifier,
  and no reified parametric type.
+ #strong[Verification.] The same document is emitted as an F\# predicate and as
  an SMT obligation. Z3 reports `Proved`, `Falsified`, `Unknown`, or
  `Unavailable`; `Unknown` and `Unavailable` are never passes.
+ #strong[Build.] One F\# project, NativeAOT, no runtime code generation, no
  reflection-based execution, and a release gate of `just check`.

== The reuse and gap register

#table(
  columns: (auto, 1fr, 1fr, 1fr),
  align: (left, left, left, left),
  table.header(
    table.cell(fill: luma(240))[Area],
    table.cell(fill: luma(240))[Scale today],
    table.cell(fill: luma(240))[Proposal],
    table.cell(fill: luma(240))[Reuse and resolution],
  ),
  table.hline(),
  [Surface],
  [Scale Basic English: controlled, declarative specifications.],
  [A general-purpose Julia/Elixir surface.],
  [Basic English is a restricted front-end, not the whole language. It compiles
   into the unified language; direct programmers use the unified language for
   the full capacity.],
  [Dispatch],
  [`generic`/`method` as image-resident words; type-first over sorts.],
  [`method` and `clause` as declaration keywords, with reified parametric
   types, `where`, open/closed heads, and guards.],
  [The word `method` means two different things. The unified language owns both
   dispatch constructs and its tables. The current generic-function machinery
   is an implementation seed to assess, not a semantic definition.],
  [Types],
  [Four `FieldType` sorts; no `Any`, unions, parametric types, or subtyping.],
  [Julia's type DAG, including `Bool <: Integer` and `Bool <: Atom`.],
  [Names collide (`Real`, `Integer`, `Bool`) but meanings differ. The unified
   language owns its type lattice. `FieldType` remains a projection used by the
   verification feature; `Bool`'s two parents cannot be represented in it.],
  [Values],
  [Tensors, image entries, stack; append-only image.],
  [General values, structs, mutable structs, records, closures, bit strings.],
  [The current image and evaluator are the starting implementation, not a
   separate runtime. Assess which values fit the image and which need a
   general-heap extension; do not assume a second runtime.],
  [Runtime],
  [Cooperative local actors, channels, links, monitors, supervision.],
  [Processes and messages, tasks and typed channels, OTP groups, modules,
   macros, bit syntax.],
  [The current scheduler is the seed. It must be extended for tasks, OTP
   groups, modules, macros, and bit syntax; Scale has no separate runtime to
   preserve.],
  [Errors],
  [`EvaluationError` values with named reasons; blocking is a distinct
   outcome.],
  [`raise`/`rescue`, `throw`/`catch`, and `exit` as three channels.],
  [The unified language has its own outcome model. Current
   `EvaluationError`/blocking values may be reused where they match; `exit`
   must not lower to `raise`.],
  [Modules and macros],
  [A flat vocabulary and image-resident words.],
  [Two module systems and two macro systems.],
  [Scale's vocabulary is not a module system. Modules and macros are unified
   language features; macro expansion must be compile-time for AOT.],
  [Verification],
  [SMT over declarative field predicates; `Proved`/`Falsified`/`Unknown`/
   `Unavailable`.],
  [Arbitrary dynamic programs with macros, mutable state, and multiple
   dispatch.],
  [Verification is a unified-language feature, implemented initially with the
   current lattice/SMT/emitter code. Arbitrary programs are still not proved;
   the declarative subset is the contract surface.],
  [Build and AOT],
  [One F\# project; no runtime code generation.],
  [A compiler and runtime with reified types, dispatch tables, and macro
   expansion.],
  [One implementation, not two. Table dispatch and compile-time macros are
   AOT-compatible; any `eval` or runtime code path is a separate JIT-only
   backend, outside the AOT entry point.],
  [Plan],
  [W0--W16, D1--D93, 1118 example checks, 78 granular tests.],
  [New front-ends, IR, runtime, and a much larger test corpus.],
  [Keep current tests as characterization. Add unified conformance and Basic
   English compilation tests separately; move checks only when a feature moves
   into the unified core.],
  [Philosophy],
  [Few concepts, explicit refusals, append-only image, failures as values.],
  [Total coverage, no construct rejected, two constructs for differing
   semantics.],
  [Basic English stays minimal; the unified language is complete. Because
   Basic English compiles into one unified core, both surfaces share one
   semantics and one dispatch model.],
  [Transpiler],
  [An F\# emitter and an SMT emitter, not a general transpiler.],
  [Julia and Elixir front-ends into the unified language.],
  [The delayed Go-to-Scale path is a third, different direction. It is not the
   Julia/Elixir transpiler and must not be conflated with it.],
)

== The recommended split

The durable design is one implementation stack, not a Scale runtime beside a
unified runtime.

```text
Basic English source ──compile──┐
                                 ├──> unified IR and semantics ──> current implementation
direct unified source ──────────┘                                  (image, evaluator,
                                                                    scheduler, SMT,
                                                                    emitters)
```

The unified language is the programmable IR and the semantic core. Basic
English compiles into it. A direct programmer writes it. There is one set of
semantics and one implementation to extend.

The current implementation is the starting implementation of the unified
language. Reuse is expected where the current semantics already match; extension
is expected where the unified language is broader; replacement is expected where
a current design contradicts the unified semantics. The decision is made per
component, not by declaring a second runtime.

=== Why one implementation

Two implementations would duplicate dispatch, values, errors, and execution.
The two would drift, and a Basic English program would not exercise the same
machinery as a direct unified program. One semantic core removes that class of
disagreement by construction.

Basic English still keeps its value: it is a small, controlled, declarative
front-end that is easy to review and to verify. The unified language is larger
because direct programmers need the full capacity. The difference is in the
surface, not in the implementation.

=== What reuse means

+ The image, evaluator, scheduler, actor/channel machinery, lattice, SMT
  encoding, and emitters are candidates for reuse.
+ A component is reused only when its current behavior is the behavior the
  unified language specifies. Otherwise it is extended or replaced.
+ The current test suite becomes the characterization suite for the reused
  behavior. New unified tests state the target behavior.
+ The image stays append-only where it remains the value authority. A general
+ heap or mutable identity is added only where the unified semantics require it.

=== What must not be assumed

+ Do not assume a separate "Scale runtime" exists to preserve. Scale has no
+ independent implementation or semantics.
+ Do not assume the current image is already a general heap.
+ Do not assume the current `method` is the unified `method`, or that the
+ current vocabulary is a module system.
+ Do not assume verification covers arbitrary unified programs. Verification is
+ a unified-language feature for the declarative contract subset.

== Invariants that must survive

+ Basic English remains a restricted, declarative surface. It compiles into the
  unified language; it is not executed by a separate runtime.
+ The unified language is the single programmable IR and semantic core. A
  direct unified program and a compiled Basic English program share one
  semantics and one dispatch model.
+ Scale has no independent implementation or semantics. The current
  implementation is the starting implementation of the unified language.
+ Reuse is per component and conditional: a component is reused only when its
+ current behavior matches the unified specification; otherwise it is extended
+ or replaced.
+ The image remains append-only where it is the value authority. A general heap
+ or mutable identity is added only where the unified semantics require it.
+ The unified language owns its type hierarchy, `method`/`clause` dispatch,
+ values, modules, macros, and concurrency. The current image-resident `method`
+ is an implementation seed, not the semantic definition.
+ Verification is a unified-language feature. It proves the declarative
+ contract subset only; `Unknown` and `Unavailable` are not passes.
+ `FieldType` remains the verification projection; it is not the unified
+ language's type system.
+ The release gate and NativeAOT contract apply to the one implementation.
+ Table dispatch and compile-time macros are AOT-compatible; `eval` and runtime
+ code generation are a separate JIT-only backend.

== Test seams for the refactor

The first work item is tests, not implementation. The tests should pin the
current implementation and state the unified target before the refactor moves
either.

+ #strong[Current-behavior characterization.] Tests for the image, evaluator,
+  scheduler, actors/channels, lattice, SMT outcomes, and emitters. These pass
+  today and become the baseline for the reuse decision.
+ #strong[Basic English compilation.] For every Basic English construct, a
+  golden test that it compiles to the expected unified IR and preserves the
+  verification contract. These pin the current surface while the compiler is
+  introduced.
+ #strong[Direct-versus-compiled equivalence.] A direct unified program and the
+  compiled equivalent Basic English document produce the same observable
+  result. This is the test that keeps one semantics.
+ #strong[Reuse and gap tests.] For each current component, a test that states
+  whether its behavior matches the unified specification. A mismatch is a named
+  extension or replacement, not an unstated second implementation.
+ #strong[Type projection.] A table test for the verification projection:
+  `Bool` to `BooleanType`; `Integer` to `IntegerType` or `NaturalType` only
+  with a proof; `Float64`/`AbstractFloat` to `RealType`; `Rational`,
+  `Complex`, `BigFloat`, `Symbol`, `Atom`, `Nothing`, and `Missing` to
+  `Unavailable`. The test must assert `Unavailable`, not a fallback.
+ #strong[Dispatch target.] A test that the unified `method`/`clause` semantics
+  are the target, and that the current image-resident `method` is either
+  extended to match or replaced. The test names which one happened.
+ #strong[Value and runtime target.] A test that a unified value is represented
+  by the current image where it fits, and a named gap where it does not. Do not
+  assume a second runtime.
+ #strong[AOT boundary.] A structural test that the AOT entry point does not
+  reference a JIT-only backend, and that macro expansion happens before the
+  runtime.
+ #strong[Source-to-unified differential tests.] For each construct in the
+  proposal's worked example, a Julia or Elixir input, the expected unified
+  construct, and a semantic assertion. The first batch covers `method`,
+  `clause`, `struct`, `|>`, `do`, keyword arguments, `with`, `case`,
+  `try`/`rescue`/`catch`, and the numeric tower.
+ #strong[Structural-error tests.] `clause` with a type annotation, `method`
+  with a pattern or guard, modifiers on bodies, and unmarked macros are
+  compile errors, not runtime errors.

== Staged plan

+ #strong[Phase 0 --- record the corrected model.] This document and D93. No
+  code change.
+ #strong[Phase 1 --- tests.] Current-behavior characterization, Basic English
+  compilation goldens, direct-versus-compiled equivalence, reuse/gap tests,
+  type projection, dispatch target, value/runtime target, and the AOT
+  boundary.
+ #strong[Phase 2 --- unified core on the current implementation.] Type
+  hierarchy, `method`/`clause` dispatch, values, and the worked example. Each
+  current component is reused, extended, or replaced with a test that says
+  which.
+ #strong[Phase 3 --- compile Basic English.] Basic English compiles to unified
+  IR. Its declarative contracts use the verification feature; there is no
+  separate Scale runtime.
+ #strong[Phase 4 --- front-ends.] Elixir first, because the current actor and
+  BEAM analysis is closer to it. Julia second, because the numeric tower and
+  parametric dispatch are larger.
+ #strong[Phase 5 --- close runtime gaps.] Tasks, OTP groups, modules, macros,
+  bit syntax, and the AOT backend, each implemented in the unified core.
+ #strong[Phase 6 --- coverage.] Add constructs in source order, each with a
+  distinct token and a differential test, until both ecosystems are covered.

== Open owner decisions

+ #strong[Reuse scope.] Which current components are reused as-is, extended, or
+  replaced? The recommendation is a per-component decision, starting with the
+  image, evaluator, scheduler, lattice, SMT encoding, and emitters.
+ #strong[Runtime host.] Is the unified runtime the current .NET/NativeAOT
+  implementation, or a separate host? The recommendation is to extend the
+  current implementation; if .NET, table dispatch and compile-time macros are
+  mandatory.
+ #strong[Value representation.] Does the current image carry the unified values
+  directly, or does it need a general-heap extension? The recommendation is to
+  extend the image where it fits and add a general heap only for values it
+  cannot represent.
+ #strong[OTP distribution.] Does "100% coverage" include BEAM distribution and
+  the code server, or only local OTP semantics? The proposal specifies local
+  groups and processes; distribution is not named.
+ #strong[First import target.] Elixir or Julia? The recommendation is Elixir
+  first.
+ #strong[Verification scope.] Declarative contracts only, or arbitrary
+  programs? The recommendation is declarative contracts only; arbitrary
+  program verification would break the trust boundary.

== Relationship to existing plans

The native .NET port (`docs/kernel/native-dotnet-port.typ`) is the plan for the one
implementation, now understood as the unified-language implementation: executable
IR, then an SRM writer, then self-typing. It is not a separate Scale Spec
runtime. The Go-transpiler note (`docs/runtime/memory-and-go-transpiler.typ`) is a third
direction and stays delayed. The BEAM compatibility matrix
(`docs/rejected-ideas/beam-compatibility.typ`) describes the current local actor subset; it is
input to the unified runtime's Elixir side, not a claim that the current
implementation already is BEAM.

The unified language is the centerpiece: the programmable IR and semantic core
that Basic English compiles into and that direct programmers use for the full
capacity. The current implementation is its starting implementation.
