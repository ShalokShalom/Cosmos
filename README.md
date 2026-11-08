# Scale

Scale is a research prototype for a design-by-contract property-testing language:
a `.scale` document drives a Hedgehog property and an SMT obligation from one
parsed source, and the specification grows from shrunk counterexamples.

This file is the repository entry point. The canonical project overview is
`README.typ`; the canonical documentation index is `docs/README.typ`.

## Start here

| Need | Read |
|---|---|
| Current state and read order | `docs/now.typ` |
| Guided language tour | `docs/start/introduction.typ` |
| Normative language contract | `docs/specification.typ` |
| Architecture invariants | `docs/architecture/INVARIANTS.typ` |
| Every abstraction and dependency | `docs/architecture/irs.typ` |
| Full documentation index | `docs/README.typ` |
| Working agreement for agents and maintainers | `AGENTS.md` |

## Repository map

| Path | What lives there |
|---|---|
| `components/` | Polylith components: kernel, core, Prelude, IPC, verification, hosts, adapters |
| `bases/` | Executable bases: `scale-cli`, `nu-game` |
| `projects/` | Build and test projects: `scale`, `game`, `scale-tests` |
| `docs/` | Canonical Typst documentation and EDN data |
| `docs/pdf/` | Frozen rendered PDFs; sources in `docs/` are authoritative |
| `Notes/` | Internal design notes, history, and decision records for D |
| `tools/` | Repository checks and generators |
| `adapters/` | External adapter sources |

`components/nu-engine` is the external Nu node, not a Scale component. Its source
skeleton is tracked; prebuilt dependencies and binary assets are fetched
separately and are ignored by Git.

## Feature matrix

Status is deliberately coarse; the generated `docs/generated/specification-status.typ` is
the authoritative per-feature status. “Partial” means the named slice works and
the remaining surface is explicit in the linked document.

| Area | Feature | Status | Canonical detail |
|---|---|---|---|
| Language | Tokenizer, literals, source positions | Implemented | `docs/specification.typ` §2 |
| Language | Concatenative evaluator, stack, arithmetic | Implemented | §3.1 |
| Language | Quotations, `if`, `apply`, `define`, `control` | Implemented | §3.2–3.5 |
| Language | Frames, `lookup`, closures | Implemented | §6 |
| Language | `let`, lexical bare names, `lambda` | Implemented | §3.6, §6 |
| Language | Ground pattern matching and open subjects | Implemented | §7 |
| Language | Pattern `Pin`/`Repeated`/guards | Partial | §7, `docs/generated/specification-status.typ` |
| Language | Sets, sorts, set operations | Implemented | §8 |
| Language | Subtyping | Value-level only | §8.4 |
| Language | Sequences, recursive types, multisets, co-products | Planned / out until a consumer | §8.7–8.8 |
| Types | Unification, occurs check, Miller patterns | Implemented | `docs/architecture/term-ir.typ` |
| Types | P4 type-inference core and expression inference | Partial | `docs/types/type-inference.typ` |
| Types | Record rows, row lacks, field access | Implemented | `docs/types/row-and-set-types.typ`, `docs/language/action/field-access.typ` |
| Types | Effect sets and effect polymorphism | Implemented for the inferred fragment | `docs/types/effect-polymorphism.typ` |
| Types | Typed effect handlers, open effect subtraction | Deferred / refused | `docs/types/effect-handlers.typ` |
| Types | Verification lattice, SMT, refiner, repair | Implemented; deferred extensions | `docs/types/type-lattice.typ` |
| Types | Decision tables | Implemented | `docs/process/decision-models.typ` |
| Runtime | Classes, slots, instances, method combination | Implemented | `docs/runtime/cell-system.typ` |
| Runtime | Conditions and restarts | Implemented | §10 |
| Runtime | Actors, mailboxes, links, monitors, supervision | Implemented | §11 |
| Runtime | Channels and ChannelEvents | Implemented | §11.5 |
| Runtime | Ownership, linearity, epochs, reclaim | Implemented as a trace discipline | §12 |
| Runtime | Persistence, image validation, image swap | Implemented | §15, §19 |
| Runtime | Hot reload / host-driven image replacement | Implemented as image import | §15.3, §22 |
| Kernel | C ABI, ten exports, version equality | Implemented at ABI 3 | `docs/kernel/abi.typ` |
| Kernel | C adapter and conformance harness | Implemented for the frozen subset | `components/adapter-c/` |
| Compilation | Expression lowering and lambda units | Implemented | §16 |
| Compilation | IL writer, statically referenced AOT artifact | Partial / planned | §16 |
| Surface | Prose sentence grammar and prose surface | Implemented, first slice | `docs/language/action/prose-surface.typ` |
| Surface | Books, spells, casts | Implemented, Stage A | `docs/language/action/books-and-spells.typ` |
| Surface | Prose shell effects and policy | Implemented, Stage 1a/1b | `docs/language/action/prose-as-shell.typ` |
| Surface | Full natural-language coverage | Deferred | §17 |
| IPC | Actor wire protocol, golden vectors | Partial; frozen subset | `docs/kernel/binary-format.typ` |
| IPC | Language-level remote send | Partial; one-way | `docs/kernel/actor-ipc.typ` |
| Verification | Typed term IR, structural verifier, receipts | Partial; P3d | `docs/architecture/term-ir.typ` |
| Tooling | Polylith workspace and dependency checks | Implemented | `workspace.edn` |
| Tooling | SageFs inner loop, docs-check, line-count checks | Implemented | `docs/process/sagefs.typ`, `AGENTS.md` |
| Integration | Nu external node and host skeleton | Partial | `docs/integrations/nu-gaia-imsim.typ` |
| Integration | Multi-platform NativeAOT deployment | Partial | `docs/kernel/nativeaot.typ` |

## Working in the repository

The alpha inner loop is SageFs, not MSBuild:

```sh
just sage-ensure
just docs-check
```

Use `just --list` for the full tool surface. `just check` and `just test` are
release gates, not the inner loop; the alpha build policy is in `AGENTS.md`.

Documentation is Typst under `docs/`; do not convert existing `.typ` files.
PDFs are frozen under `docs/pdf/` and are not regenerated during normal work.
Markdown under `Notes/` is internal and non-normative.

## License

See `LICENSE` and `LICENSE-docs`.
