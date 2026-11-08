= Scale now

This is the session-start brief. Read this page first. Read the roadmap
archive or the decision records only by section, on demand; they are history,
not a briefing.

== State

- Example suite: #strong[1741 checks] (1683 examples, 58 properties) at the last
  release baseline. The current suite runs more checks; only `just release`
  refreshes the documented counts.
- Granular suite: 36 cases (13 snapshots, 14 integrations, 9 writer checks);
  discovery is 100 tests.
- ABI and image schema: version 4 and 2.
- Polylith: 11 components, 2 bases, 3 projects, 0 development, 1 external.
- Last verified checkpoint: Slices 1--6, R3, R4, R6, R8, R10, R11, R12, and R13
  are landed. The canonical renames and verification move are complete; the
  shell policy enforces allowed programs, write roots, and environment choice;
  the semantic test matrix is recorded.
- R0 durable callable path landed: control primitives, control closures,
  builtin tokens, host words, and non-builtin names resolve once to a
  `Callable` kind and invoke through one path. Book words and closure
  arity/effect checks remain later R0 slices.
- Architecture and the specification status matrix are generated from
  `docs/architecture/architecture.edn`; `just docs-check` runs the generator in check mode.
  `docs/generated/specification.edn` is the generated machine-readable projection;
  `docs/specification.typ` remains normative.
- Language layers: `logic` is the core definition language; prose is an
  execution-only shell-scripting layer that calls `logic` and does not define
  words. `logic` can call F\# and Elixir packages through the host contract.
- Current work: type-system A (row typing) is landed, including open records,
  row lacks constraints, field access, and the host/reference unifier
  integration. B (effect typing) has closed and open subtraction with lacks
  constraints and effect polymorphism; quotations carry `Code(result, effects)`
  and the typed rule covers the shallow one-shot `with-handler` as a
  fixed-arity AST case, while `with-handler` itself is `NotInferred` in the word
  registry, so a general handler expression remains deferred. Host, actor, and
  channel words now carry callable effect signatures; stack words have typed stack
  effects and
  `StackInference.check` covers the simple token fragment; control forms remain
  `NotInferred`. C, the combined kinded model, is landed as a reference model
  with kind separation, scheme quantification, field/effect composition, and a
  host/reference differential; the inference loop still runs its own unifier
  rather than the model. The nominal-constructor (`shape` declarations, construction, and
  positional patterns), real-literal, local-binding, and multiple-dispatch
  slices landed, and the port proofs (a record, a recursive function, a
  higher-order call, and a dispatch method in one book; the pure core of
  `DecisionTable.select`; a whole cons-cell list module; and the whole
  `PatternAnalysis` module) run through `cast`. D (host resource tokens) is
  runtime only: ABI 4, the opaque token type, the host resource table, and
  token-returning transport acquisition. `own`, `borrow`, `consume`, and
  `release` remain `NotInferred`, so D has no static representation yet.
  Declarative IO has the lower layers: `.install`
  parsing, Intent → Plan, confirmation, realpath/hash checks, a trash/unlink
  executor, and a scope property; prose remains deferred. The host pattern
  boundary keeps `PatternLogic` portable and lowers `HostOnly` features through
  an elaborator. Hot swap is host-driven: validated whole-image import, unit
  replacement, unit/export/group patches, dependency pins, previous-image
  rollback, retained execution state, and serialized handle calls. `just release`
  is the single format/test/NativeAOT/count gate.
  The remaining security work is explicitly future: read paths, network policy,
  and package signatures. The differential matrix now checks the token,
  expression, pattern, package, and kernel-term paths; the first
  expression-to-lowered slice is green.
- Step records are per statement and opt-in (D201): `lowerExpression` emits none,
  so artifacts and snapshots are unchanged and `scopeOf` stays
  `HistoryExcluded`; `lowerExpressionWithStepRecords` records one position at
  each statement start of a `Block` or `Quotation` body. A resume is sound only
  with the state that boundary had, so a snapshot remains future work.
- Next: continue W1 with the canonical semantic term contract and the lowering
  handoff, then W2 general typed handlers.
  W3 unifies the F\#/Elixir host contract; W4 is self-hosting and self-typing.
  Run `just release` only when explicitly called.
== Read order

+ This page.
+ `AGENTS.md` for the workflow and the tool policy.
+ `docs/specification.typ` by section for the feature under change.
+ The feature's `.typ` (for example `docs/language/action/prose-as-shell.typ`).
+ `docs/architecture/irs.typ` when touching parsing, lowering, the image, or execution.
+ `docs/process/decision-index.typ` to find the active decision, then its section.
+ `docs/runtime/performance.typ` before optimizing.

`docs/process/roadmap.typ` is the current pointer page; `docs/archive/roadmap-archive.typ` is
completed history. Do not read the archive or `docs/process/decision-records.typ` end
to end. Use `doc_get_structure` and `doc_get_section`, or a narrow search.

== Active decisions

- D183: shell `run`/`capture` are host words; `components/shell-host` owns the
  process effects.
- D177 is superseded for prose: logic definitions belong in books, not in the
  prose language. D184 lands books/spells, `--book`, `--cast`, and the prose
  `cast` host word.
- D182: all work uses the full `Scale` checkout. `Scale-code` is outdated and
  retired. The F\# language server loads Scale only when the client excludes the
  vendored Nu workspace, because the workspace peek otherwise picks
  `components/nu-engine/Nu.slnx` first; the client configuration that works
  (`AutomaticWorkspaceInit`, peek depth, and `ExcludeProjectDirectories`) is
  recorded in `Notes/lsp-workspace-plan.md`. With it, the symbol tools work on
  `.fs` files and the direct-read fallback is no longer the daily path.
- D185: canonical vocabulary. An IR is an #strong[abstraction]; a `-Ast` is a
  `-Logic`; `TermIr` is `SymbolicAbstraction`; `TypeInference` is
  `TypeRetrieval`. The docs use the canonical names; the code keeps its current
  names until a rename is scheduled. The `verification` directory is the
  canonical target for tests, formal verification, and type checking.
- D186: Basic English is the historical name for the Prose sentence grammar.
  The docs are `docs/language/action/prose-grammar.typ` and `docs/language/action/prose-foundations.typ`; the
  sentence parser remains `tokenizer.fs`/`parser.fs` and is not merged into
  `prose-parser.fs`.
- D190: the lowered instruction set is `LoweredInstructions`;
  `LoweredAbstraction` is the execution abstraction. The old module name is
  retired. No behavior changed.
- D191: `RootKind`/`rootKinds` is the one execution-root registry;
  `getRoot`/`setRoot` reattach a root. Roots remain execution state and are
  not serialized.
- D192: image and package schema 2 carry `Unit`/`UnitGroup`, arity, captures,
  initialization order, and classification; ABI 2.
- D193: group calls resolve through the unit's group, frame arguments and
  captures, and use a separate closure callback; short calls and missing
  captures are named refusals. Value-unit initialization is Slice 3.
- D194: a book is one recursive group; self, forward, and mutual calls run.
  `logic` and `cycle` are checked against the book calls; `and` groups cycle
  definitions.
- D195: core `logic` and `cycle` definitions use the plain name for recursion;
  `logic` refuses a self-call, and `cycle ... and ...` stores a mutual group.
  There is no `recurse` word.
- D196: the F\# abstraction, manifest tiers, FCS read-off, supported body
  translation, diagnostics, metadata, and package lowering are implemented.
- D197: `ClassificationWire` carries `(name, kind, version)` only; a body,
  duplicate name, unknown kind, and version skew are refused.
- D198: a book package is lazy; units carry per-definition hashes and dependencies,
  calls check arity, `main` takes no arguments, and the book cache is keyed by
  resolved path plus source hash.
- D199: `open math` imports `math.book` beside the opening book; its exports
  are callable, its capabilities are unioned, and cycles/conflicts are refused.

- A SageFs session keeps serving the last successful build. Check
  `sourceState` before trusting an eval or a test; see `docs/process/sagefs.typ`.
- `--prose-typecheck` does not infer host words yet; run a shell script with
  `--prose`.