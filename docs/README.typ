== Scale documentation index

This is the entry point for new readers. The Typst sources under `docs/` are
the central documentation; Markdown copies are retired. Non-canonical design
notes and historical briefs live under `Notes/` and are not normative.

The folders are the semantic groups. Start with `now` and `specification`, then
open the group for the question you are asking.

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Group], [What lives there]),
  [Start], [`docs/start/` — introduction, glossary],
  [Architecture], [`docs/architecture/` — invariants, abstraction index, IRs],
  [Language], [`docs/language/` — prose, books, recursion, surface features],
  [Types], [`docs/types/` — rows, effects, inference, lattice],
  [Runtime], [`docs/runtime/` — memory, cells, roots, IO, performance],
  [Kernel], [`docs/kernel/` — ABI, boundary, ports, actor wire],
  [Process], [`docs/process/` — roadmap, decisions, reviews, tooling],
  [Generated], [`docs/generated/` — projections and counts],
  [Quests], [`docs/quests/` — active focused contracts],
  [Integrations], [`docs/integrations/` — Nu and external systems],
  [Rejected], [`docs/rejected-ideas/` — retired designs],
  [PDFs], [`docs/pdf/` — frozen renderings; the `.typ` sources are canonical],
)

== Start here

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Session start], [`docs/now.typ` — current state and read order],
  [Language specification], [`docs/specification.typ` — normative, with per-feature status],
  [Guided introduction], [`docs/start/introduction.typ` — a whole-language tour],
  [Glossary], [`docs/start/glossary.typ`],
  [Repository entry point], [`README.md` at the repository root],
  [Project overview], [`README.typ` at the repository root],
  [Component map], [`components/README.md`],
  [Base map], [`bases/README.md`],
  [Project map], [`projects/README.md`],
  [Internal notes index], [`Notes/README.md`],
)

== Architecture

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Architecture invariants], [`docs/architecture/INVARIANTS.typ` — the project constitution],
  [Abstraction index], [`docs/architecture/irs.typ` — every abstraction and its dependency graph],
  [Architecture data], [`docs/architecture/architecture.edn` — the EDN source for generated views],
  [General IR], [`docs/architecture/general-ir.typ`],
  [Typed term IR], [`docs/architecture/term-ir.typ`],
  [Language-feature IRs], [`docs/architecture/language-feature-ir.typ`],
  [Semantic cross-walk], [`docs/architecture/semantic-crosswalk.edn`],
  [DSPy-shaped programs], [`docs/architecture/dspy.typ`],
  [Repair model analysis], [`docs/architecture/repair-model-analysis.typ`],
  [LONGHAND claim ledger], [`docs/architecture/longhand.typ` — proposed],
)

== Language

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Action language], [`docs/language/action/README.typ` — functionality, examples, implementation, specification, tests],
  [Prose surface], [`docs/language/action/prose-surface.typ` — design and first slice],
  [Shell layer], [`docs/language/shell-layer.typ` — interaction, completion, and safety],
  [Question language], [`docs/language/question/README.typ` — functionality, examples, implementation, specification, tests],
  [Question surface], [`docs/language/question/question-surface.typ` — symbolic facts, rules, and questions],
  [Question evaluator], [`docs/language/question/question-evaluator.typ` — first-order unification, search, limits],
  [Semiotics of the layers], [`docs/language/semiotics.typ` — Shell, Prose, and Scale semantics/syntax/pragmatics],
  [Semiotic style], [`docs/process/semiotic-style.typ` — Arthur/Lia names and Catppuccin colors],
  [PR material], [`docs/promo/README.typ` — Lia and the training-free strategy],
  [Meet Lia], [`docs/promo/lia.typ` — children's promotional page],
  [Prose Prelude port], [`docs/language/action/prose-prelude-port.typ`],
  [Prose as a shell], [`docs/language/action/prose-as-shell.typ`],
  [Prose grammar], [`docs/language/action/prose-grammar.typ` and `docs/language/action/prose-foundations.typ`],
  [Books and spells], [`docs/language/action/books-and-spells.typ`],
  [Recursion plan], [`docs/language/action/recursion.typ`],
  [Recursion design], [`docs/language/action/recursion-design.typ`],
  [Callable contract], [`docs/language/action/callable-contract.typ`],
  [Function values and currying], [`docs/language/action/function-values.typ`],
  [Local binding], [`docs/language/action/local-binding.typ`],
  [Multiple dispatch], [`docs/language/action/multiple-dispatch.typ`],
  [Nominal constructors], [`docs/language/action/nominal-constructors.typ`],
  [Real numbers], [`docs/language/action/real-numbers.typ`],
  [Field access], [`docs/language/action/field-access.typ`],
  [Host pattern boundary], [`docs/language/action/pattern-host.typ`],
  [Basic English coverage], [`docs/language/action/basic-english-coverage.edn`],
)

== Types

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Set-theoretic typing], [`docs/types/set-theoretic-typing.typ`],
  [Verification type lattice], [`docs/types/type-lattice.typ` — D25],
  [Type inference], [`docs/types/type-inference.typ` — P4],
  [Types in Scale], [`docs/types/type-map.typ`],
  [Row and set types], [`docs/types/row-and-set-types.typ`],
  [Effect polymorphism], [`docs/types/effect-polymorphism.typ`],
  [Effect handlers], [`docs/types/effect-handlers.typ`],
  [Row/effect A/B/C plan], [`docs/types/row-typing-plan.edn`],
  [Type-system architecture], [`docs/types/type-system.edn`],
)

== Runtime

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Cell system], [`docs/runtime/cell-system.typ`],
  [Memory model], [`docs/runtime/memory-model.typ`],
  [Memory and Go transpiler], [`docs/runtime/memory-and-go-transpiler.typ`],
  [Root registry], [`docs/runtime/root-registry.typ`],
  [Evaluator slicing and scheduler], [`docs/runtime/evaluator-slicing-and-scheduler.typ`],
  [Declarative IO], [`docs/runtime/declarative-io.typ`],
  [Performance plan], [`docs/runtime/performance.typ`],
  [Benchmark baseline], [`docs/runtime/performance-baseline.typ`],
)

== Kernel

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Kernel inventory], [`docs/kernel/kernel-inventory.typ`],
  [Kernel/Prelude boundary], [`docs/kernel/kernel-prelude-boundary.typ`],
  [Kernel ABI], [`docs/kernel/abi.typ` and `components/kernel/scale_abi.h`],
  [Prelude port map], [`docs/kernel/prelude-port.typ` and `docs/prelude/README.typ`],
  [Native .NET port], [`docs/kernel/native-dotnet-port.typ`],
  [NativeAOT], [`docs/kernel/nativeaot.typ`],
  [Actor wire protocol], [`docs/kernel/binary-format.typ`],
  [Actor IPC], [`docs/kernel/actor-ipc.typ`],
  [Host adapters], [`docs/adapters/README.typ` and `adapters/elixir`],
)

== Process

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Roadmap], [`docs/process/roadmap.typ`],
  [Backlog dependency graph], [`docs/process/backlog-graph.typ`],
  [Roadmap archive], [`docs/archive/roadmap-archive.typ`],
  [Decision index], [`docs/process/decision-index.typ`],
  [Decisions], [`docs/process/decision-records.typ`],
  [Decision models], [`docs/process/decision-models.typ`],
  [Critical review], [`docs/process/critical-review.typ`],
  [Commitments and status], [`docs/process/commitments-ledger.typ`],
  [Test strategy], [`docs/process/test-strategy.typ`],
  [Technical-debt register], [`docs/process/technical-debt-register.typ`],
  [F\# engineering rules], [`docs/process/rules-for-fsharp.typ`],
  [Agent setup], [`docs/process/agent-setup.typ`],
  [SageFs], [`docs/process/sagefs.typ`],
  [Incus development environment], [`docs/process/incus-dev.typ`],
  [.NET MCP], [`docs/process/dotnet-mcp.typ`],
  [Jujutsu workspaces], [`docs/process/jj-workspaces.typ`],
  [Vestige], [`docs/process/vestige.typ`],
)

== Generated and quests

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Generated specification projection], [`docs/generated/specification.edn`],
  [Generated specification status], [`docs/generated/specification-status.typ`],
  [Generated architecture table], [`docs/generated/architecture-table.typ`],
  [Generated architecture graph], [`docs/generated/architecture-graph.typ`],
  [Line counts], [`docs/generated/line-counts.typ` — generated by `just line-counts`],
  [Quest: path policy], [`docs/quests/quest-path-policy.typ`],
  [Quest: host policy], [`docs/quests/quest-host-policy.typ`],
  [Quest: shell layer], [`docs/quests/quest-shell-layer.typ`],
  [Quest: shell tools], [`docs/quests/quest-shell-tools.typ`],
  [Quest: question engine], [`docs/quests/quest-question-engine.typ`],
  [Quest: code reduction], [`docs/quests/quest-code-reduction.typ`],
  [Quest: KaOS Noctalia kiosk], [`docs/quests/quest-kiosk.typ`],
  [Quest: step records], [`docs/quests/quest-step-records.typ`],
)

== Integrations and rejected ideas

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Kind], [Where]),
  [Nu integration and Gaia-on-ImSim], [`docs/integrations/nu-gaia-imsim.typ`],
  [Unified Julia/Elixir language], [`docs/rejected-ideas/unified-language.typ`],
  [BEAM compatibility], [`docs/rejected-ideas/beam-compatibility.typ`],
  [Rejected and reserved designs], [`docs/rejected-ideas/rejected-designs.typ`],
)

Non-canonical notes and historical briefs are kept under `Notes/` (see
`Notes/README.md`); they are not normative and are not part of this index. If a
note becomes normative, convert it to Typst under `docs/` and add it here.

If a new kind of documentation is introduced, add it here and to the file-layout
section of `AGENTS.md` in the same change.
