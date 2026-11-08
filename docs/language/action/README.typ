= #text(fill: rgb("#f5c2e7"))[Arthur] --- Action language

#strong[Status: implemented for the current imperative subset.] Action is the
imperative part of Scale. It executes sentences, calls, local bindings, choices,
closures, host words, and book casts. It does not define modules; definitions
belong to Logic books.

== At a glance

```text
source -> ProseParser -> ExpressionLogic -> EvaluatorLowering
       -> LoweredAbstraction -> final value
```

Action needs the shared IR, lowering, VM, builtins, and host capabilities. It
does not need Logic definitions or Question; books and questions are optional
adjacent layers.

== Functionality

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Form], [Current syntax]),
  [Numbers], [`42`, `-1`, `3.5`],
  [Text], [`"hello"`],
  [Atoms], [`\:red`],
  [Calls], [`add(2 3)`],
  [Sequences], [`5. multiply(2 3).`],
  [Local binding], [`let x = add(2 3) in multiply(x x)`],
  [Choice], [`match 5, when 5 then 42, otherwise 0`],
  [Pipeline], [`5 |> add(2)`],
  [Partial/hole], [`subtract(_ 10)`],
  [Anonymous function], [`logic (x) = add(x 1)`, or `... close` for a multi-line body],
  [Book cast], [`cast("math.book" "double" 21)`],
  [Shell word], [`run "echo" with "hello"`],
)

Examples, run through the current implementation:

```text
let x = add(2 3) in multiply(x x).   → 25
match 5, when 5 then 42, otherwise 0 → 42
5. logic (x) = add(x 1). apply.      → 6
```

== Implementation (#text(fill: rgb("#f9e2af"))[yellow])

- `components/core/prose-parser.fs` — Action parser and refusals.
- `components/core/expression-logic.fs` — shared expression IR and profiles.
- `components/prelude/evaluator/lowering.fs` — expression-to-instruction lowering.
- `components/kernel/lowered-instructions.fs` — instruction set.
- `components/kernel/lowered-abstraction.fs` — execution.
- `components/shell-host/prose-shell.fs` — script runner and host words.
- `components/book-host/book-runner.fs` — book loading and `cast`.

== Specification and feature documents

- `docs/specification.typ` — normative language contract.
- `docs/language/action/prose-surface.typ` — grammar, sentences, calls, match, pipelines.
- `docs/language/action/prose-as-shell.typ` — shell words and process effects.
- `docs/language/action/books-and-spells.typ` — definitions and casts.
- `docs/language/action/function-values.typ` — named-parameter closures and terminology.
- `docs/language/action/local-binding.typ` — `let ... in`.
- `docs/language/action/real-numbers.typ` — numeric literals.
- `docs/language/action/nominal-constructors.typ` — shapes, construction, and patterns.
- `docs/language/action/multiple-dispatch.typ` — generics and methods.
- `docs/language/action/recursion.typ` and `recursion-design.typ` — recursion groups.
- `docs/language/action/callable-contract.typ` — call versus reference.
- `docs/language/action/field-access.typ` — named field projection.
- `docs/language/action/pattern-host.typ` — shared pattern boundary.
- `docs/language/action/prose-grammar.typ` and `prose-foundations.typ` — historical foundations.
- `docs/language/action/prose-prelude-port.typ` — Prelude port feature contract.
- `docs/language/action/basic-english-coverage.edn` — controlled vocabulary coverage.

== Tests

- `components/verification/tests/tests.fs` — parser, lowering, execution, host words, books, functions, and examples.
- `components/verification/tests/granular-tests.fs` — snapshots and integrations.
- `components/verification/tests/properties.fs` — expression/function properties.
- `components/verification/tests/differential.fs` — token and expression lowering agreement.

== Limits and current gaps

- No module or definition system; books own definitions.
- No Question call yet; an `ask` bridge is designed but not implemented.
- No currying, `let rec`, conditions/restarts, mutable locals, classes, or collection libraries yet.
- No shell interaction; Shell is the separate top layer.
- Action source can be self-contained; its runtime still depends on the shared core.

== Related

- Question: `docs/language/question/README.typ`.
- Shell: `docs/language/shell-layer.typ`.
- Semiotics: `docs/language/semiotics.typ`.
