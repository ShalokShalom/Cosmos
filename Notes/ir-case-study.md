# IR case study

Status: exploratory, non-normative.

## Language layers

- `logic` is the core definition language. It is for experienced language
  designers who write implementations by hand.
- Prose is an execution-only, shell-scripting-simple layer. It calls `logic`
  words; it does not define them or expose implementation details.
- `logic` sits on the supporting IRs and is the place where implementations can
  cross-call F# and Elixir packages through one host contract.

## Inventory

`docs/architecture/architecture.edn` has 10 canonical abstractions. Five are executable or
structural code IRs:

| IR | Purpose |
|---|---|
| `ExpressionLogic.Expression` | General expression and quotation IR |
| `PatternLogic.Pattern` | Shared pattern and match IR |
| `PackageLogic.PackageDocument` | Package, unit, export, and capability structure |
| `TermIr.Term` / `SymbolicAbstraction` | Interpreter terms, quotations, slots, closures |
| `LoweredInstructions.Instruction` / `LoweredAbstraction` | VM and kernel execution form |

Three are auxiliary representations, not peer executable IRs:

| Representation | Purpose |
|---|---|
| `PortableDocuments` | Canonical JSON projections at boundaries |
| `TypeRetrieval.Type` | Static type and effect language |
| `DomainExpression` / SMT | Verification and lattice representation |

Two are runtime data, not code:

- `ImageValues`: tensors, identities, store, lexicon.
- `ExecutionState`: store, stack, roots, frames, scheduler.

Surface ASTs outside the canonical graph include `ProseParser` book/sentence
trees and `PatternHost` portable/host pattern forms.

So the honest count is **5 code IRs + 3 auxiliary representations + 2 runtime
data models**, not 10 independent languages.

## What is duplicated

- `ExpressionLogic` and `TermIr` both describe expressions, quotations, and
  application. One is the general source IR; the other is the interpreter's
  symbolic form. The split is useful only if the lowering contract is explicit.
- `SymbolicAbstraction` and `LoweredAbstraction` are two execution forms. That
  is a legitimate source/target split, not a duplicate.
- `PatternLogic` is embedded in `ExpressionLogic.PatternMatch`; it is not a
  second expression language.
- `PackageLogic` is a container, not a third evaluator.
- Portable documents are projections. Treating them as IRs creates a second
  authority and drift.

## Options

### A. One semantic term IR plus one lowered IR

Make `ExpressionLogic`/`TermIr` one semantic term family with dialects for
patterns and packages. Lower that to `LoweredInstructions`. Generate portable
documents from the semantic nodes.

- Best semantic clarity.
- Largest migration.
- Pattern and package remain nodes/dialects, not peers.

### B. Keep the five IRs, add a conformance matrix

Keep the current split and add differential tests from expression to term to
lowered to execution.

- Lowest risk.
- Does not remove parallel authority.
- Fastest to adopt.

### C. External IR (MLIR, LLVM, Erlang Core)

- Rejected. It would import host semantics, weaken NativeAOT/portability, and
  lose the set-theoretic/actor contract.

### D. Direct AST execution, no IR

- Rejected. It loses portability, hot swap, and the kernel boundary.

## Recommendation

Adopt **A as the target, B as the migration path**:

1. Declare `ExpressionLogic` the canonical semantic term family.
2. Treat `TermIr` as a normalized interpreter view, not a second source.
3. Keep `LoweredInstructions` as the single VM target.
4. Generate all portable documents from the canonical nodes.
5. Keep `TypeRetrieval.Type` and `DomainExpression` separate; they are not
   execution IRs.
6. Add one host-call contract so F# and Elixir packages expose the same callable
   contract and schemes.

## Critical review

- The five-IR count is not the problem by itself. The risk is parallel authority
  and hand-maintained codecs.
- `ExpressionLogic`/`TermIr` overlap needs an explicit lowering contract or it
  will drift.
- `PortableDocuments` should not be counted as an execution IR.
- Pattern and package IRs should remain embedded/container forms, not independent
  evaluators.
- The migration is cross-cutting and should be split into differential-test
  slices, not a big-bang rewrite.
- The case study is structural; it does not measure runtime cost, codec cost, or
  hot-swap behavior.
