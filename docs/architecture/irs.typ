= Abstractions and logic

This is the canonical index of code representations. The file name `irs.typ` is
historical; the canonical term is #strong[abstraction]. The current code names
are shown beside each canonical name so the documents and the source can be read
together.

== Canonical names

Generated from `docs/architecture/architecture.edn` by `tools/architecture-check.fsx`.

#include "architecture-table.typ"
== Everything is an implementation of some logic

An abstraction is an implementation of a logic. This is the teaching frame:

- the tests implement #strong[test logic];
- expression trees implement #strong[expression logic];
- patterns implement #strong[pattern logic];
- a decision table implements #strong[decision logic];
- an SMT obligation implements #strong[formal logic];
- the kernel implements the #strong[lowered abstraction];
- the F\# Prelude implements the #strong[symbolic abstraction].

The project does not need to rename every implementation to "logic". The
canonical names above are the ones used in the documents; the code names remain
the implementation detail until a rename is scheduled. This is the framing that
lets a child see a function, a test, a table, and a proof as the same kind of
object: a piece of logic with an implementation.

== Dependency graph

Generated from `docs/architecture/architecture.edn`.

#include "architecture-graph.typ"
== Decision tables: where the source comes from

A decision table is not synthesized from the prose specification in the current
slice. Its source is a #strong[table document]:

```text
table jump-or-run
policy: unique
when player has health at least 2, every player can jump.
end
```

`DecisionTableSurface.parse` reads that document, tokenizes each row as a Basic
English transition, and turns each row's conditions into guards and its verb
into the outcome. The host supplies the domain's field getters and the outcome
body (`binding`); `DecisionTable` then selects rows and checks coverage and
disjointness. The demo's table text in `bases/scale-cli/entry.fs` is a
hand-written example; the product path is the parsed table document, not a
hard-coded F\# table.

So the table is an abstraction of decision logic: its source is the table text,
its domain comes from the host, and its two properties are checked by the
solver. The table is authored or generated; it is not inferred from the prose
sentences.

== The `verification` directory

The canonical target is one unified `verification` node:

```text
verification/
  formal/   Z3, the refiner, diagnostics, and decision tables
  tests/    the example, property, granular, and differential suites
  typing/   type checking (a later layer)
```

The existing code is not moved yet: the formal layer is
`components/verification`, the tests are `development/scale`, and type retrieval
is `components/typing`. The target is documented now so the move is a mechanical
step, not a new design. Type retrieval stays separate from type checking.

== Recommendations

- #strong[One canonical abstraction per layer.] The surface produces
  `ExpressionLogic`; execution consumes `LoweredAbstraction`; portability uses
  the canonical JSON document for that layer. Do not add a second format for the
  same layer.
- #strong[Use `PackageLogic` for books/spells.] A book is a package with
  exports; a spell is a package with `main`. Do not invent a second module
  format.
- #strong[Keep `PatternLogic` stable.] It is shared by expression matches,
  pattern analysis, and verification.
- #strong[Resolve `SymbolicAbstraction` versus `LoweredAbstraction`.] The first is
  the interpreter's current form; the second is the execution encoding. D200 and
  D202 resolved this: the symbolic form and its document are retired, and the
  instruction set becomes the one execution encoding with an instruction document
  for the payloads that used to be term documents (W1a--W1c). The split is
  explicit until that lands, and it is not a place to build new work.
- #strong[Pin the schemas.] Every portable document carries schema 1 and a
  round-trip/property test.
- #strong[Label stability.] In every abstraction document, state whether the
  type is a surface, a portable contract, or an internal optimization.

== Shared code boundaries

The whole-graph review (`Notes/code-sharing-review.md`) fixed these boundaries:

- **Host policies stay separate.** Shell programs/roots/environment, declarative
  file capabilities, and IPC resource tokens are different authority models. Only
  path containment and resource-release shape are shared.
- **Kernel/core schemas are duplicated intentionally.** `PreludeImage` and
  `PackageLogic` cannot share a project reference without breaking the
  kernel/Prelude boundary. Generate both from one schema or share conformance
  vectors.
- **Build-graph cost is real.** Adding one `core` file forces a full graph
  rebuild. Batch low-risk moves and use the hard-reset overlap.
- **JSON validation is shared in `WireCore`.** Protocol fields and named errors
  remain in their owning wire modules.
- **TypeCore, ProgramPipeline, and HostBoundary are contracts first.** Extract
  implementation only after the semantic contract and tests exist.

