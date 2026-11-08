# W1 plan: the semantic term contract and the lowering handoff

Status: decided as D200. One semantic form, one lowered encoding; the term form
is retired by migration and history moves to a step-record instruction. The
first slice (the term ledger) has landed; this note stays the working plan.

## What the code contains

| Form | Where | Shape |
|---|---|---|
| `Expression` | `components/core/expression-logic.fs:46` | 20 constructors: literals, word, call, partial, hole, quotation, block, pipeline, let, lambda, if, pattern match, kind and predicate declarations, condition, restart, generic, message, fail |
| `Term` | `components/kernel/symbolic-abstraction.fs:10` | 11 constructors: number, text, atom, word, quote, branch, slot get, slot set, make closure, call closure, fail |
| `TermProgram` | `components/kernel/symbolic-abstraction.fs:24` | the portable projection of `Term`, schema 1 |
| `Instruction` | `components/kernel/lowered-instructions.fs:44` | ~35 opcodes: literals, slots, word call with guard, stack shuffle, roots, closures, tensor and text, arithmetic, jumps, labels, fail |
| `LoweredProgram` | `components/prelude/evaluator/lowering.fs:150` | `Units: LoweredUnit list` plus `Instructions: Instruction list` |

Two handoffs exist, and they do not meet:

- `lowerExpression : Expression -> Result<LoweredProgram, LoweringRefusal>`
  (`lowering.fs:159`) goes from the semantic form to the kernel instruction set.
- `EvaluatorProgram.ofTokens : string list -> Result<Term list, Error>`
  (`components/prelude/evaluator/program.fs:55`) goes from tokens to `Term`, and
  `executeTerm` (`program.fs:92`) runs it. The interpreter's program is
  `Program: Term list`.

So the language has **two front ends** (tokens, expressions) and **two
encodings** (`Term`, `Instruction`), with no function that relates an
`Expression` to a `Term`.

## Execution status of each form

A correction that matters for the decision: `Term` does not execute. The pinned
shape says so in its own comment (`symbolic-abstraction.fs:8`), and
`EvaluatorProgram.executeTerm` (`program.fs:92`) handles only `LiteralNumber` and
`Word`; the other nine constructors raise. `Evaluator.run` therefore runs a term
program whose only executable shapes are a number and a late-bound word.

What is real:

- `Expression` lowers fully to `Instruction` through `lowerExpression`, with
  named refusals for the nodes it declines.
- `Term` holds the interpreter's `Program`, a portable document (schema 1), and
  golden verification vectors; it is a carrier and a wire shape, not an engine.
- There is no `Expression -> Term` converter at all. The only bridge from a term
  to the lowered form is `EvaluatorProgram.toKernelTokens` plus
  `LoweredInstructions.lowerTokens`, which is lossy, executes only the two
  shapes, and throws on any other term shape (`program.fs:264`).

## The finding

`Term` is not a semantic term family. Five of its eleven constructors are
lowered concepts: `SlotGet`, `SlotSet`, `MakeClosure`, `CallClosure`, `Branch`.
An expression has lambdas, lets, patterns and pipelines; a term has slots,
closures and jumps. `Term` is therefore a **second lowered encoding**, the one
the interpreter's program is written in, and `Instruction` is the one the VM
executes. `TermProgram` is its portable projection, not a peer IR.

The five code IRs are one semantic form (`Expression`), one embedded pattern
language (`PatternLogic`), one container (`PackageLogic`), and **two lowered
encodings** (`Term`, `Instruction`). The duplication is not
`Expression`/`Term` as the case study says; it is `Term`/`Instruction` — and
today the duplication is structural, because only one side executes.

`LoweredNode` (`lowering.fs:15`) is not a fourth IR. It is a per-constructor
label used to refuse a node by name; it holds no data.

## Differential coverage today

The harness is real but thin. `Differential.cases` has 18 token programs and
`Differential.expressionCases` has 9 expression programs; both compare the token
interpreter against the lowered abstraction and both are green. What the 9
expression cases actually exercise is `Call` (six cases) and one `Let` (the
narrowed form built directly in `tests.fs:5110`). Sixteen of the twenty
`Expression` constructors have no expression-level differential case at all:
`Literal`, `Word`, `Partial`, `Hole` and `Block` are only reachable through the
token oracle, and `Lambda`, `If`, `PatternMatch`, `DeclareKind`,
`DeclarePredicate`, `Condition`, `Restart`, `Generic`, `Message` and `Fail` have
no oracle in the corpus.

That is the actual W1 gap: the corpus proves two paths agree on a sliver of the
language, not that the semantic form has one lowering.

## Options

**A. One lowered encoding, `Term` retired.** The interpreter's `Program` becomes
`Instruction list` and runs on `LoweredAbstraction`; the token reader lowers
directly; `TermProgram` is retired with it or kept as a read-only projection.

- Removes the second authority and the second portable codec.
- Largest migration: `Term` is referenced from 19 prelude files (history,
  bookmarks, quotations, channels, dispatch, scheduler, pattern, control).
- `Instruction` gained a step record and a resumable position (D201), so a
  program's history scope is now a property of the program (`HistoryRecorded` or
  `HistoryExcluded`, via `scopeOf`) instead of a global exclusion. Retiring the
  interpreter therefore no longer loses history outright, but a kernel step
  records a position only, with no token snapshot, so rollback parity is still
  open. This is a semantic gap, not a refactor.

**B. Two lowered encodings, one semantic form, proved equal.** Keep `Term` for
the interpreter and `Instruction` for the VM, and make the relationship a
checked contract: `Term` is the interpreter's private encoding, `TermProgram` is
not a public compatibility promise, and no `Term` constructor may be added
without an `Instruction` counterpart plus a differential case.

- Cheapest correct step, and it keeps the interpreter's history engine alive.
- Keeps two execution engines forever, which is what W4a (self-hosting) has to
  carry.

**C. Big-bang single form.** Rejected: the graph and the case study both forbid
a rewrite before a differential proves the split.

**D. Make `Expression` the interpreter's source and keep `Instruction` only.**
This is A with a migration order: add `Expression -> Term` first to prove
expressions and tokens agree, then replace `Term` with `Instruction`.

## Recommendation

A as the target, B as the migration path, and D as the order — the same shape the
case study recommends. Concretely, in this order:

1. **Two coverage ledgers (first slice, no semantic change).** One enumerates
the eleven `Term` constructors and classifies each against the instruction set
with an exhaustive match, so a new constructor cannot compile until it is
classified; two constructors are `UnitOnly` and that count is pinned so the gap
can shrink but not grow. The mirror ledger enumerates the twenty `Expression`
constructors and records, for each, whether a differential case exercises it;
this is the number that has to climb.
2. **Close the gap case by case** with `Differential` rows, not with rewrites.
3. **Decide the history question** (see compromises) before any retiral.
4. Only then retire `Term`, with `TermProgram` moved to read-only or removed.

## Resolved by D200

+ **A is the endpoint.** The term form is retired; the instruction set is the
  one lowered encoding.
+ **History is kept.** A step-record instruction family with a resumable program
  counter is added (D120), so the single encoding records history, bookmarks and
  rollback. This is a new opcode family inside W1's scope, not a later
  hardening item.
+ **`TermProgram` is not a public promise.** Nothing in the repository treats
  the term document as a compatibility surface; it is retired with the form.
+ **The token reader still has to go.** The migration order is the differential,
  then the retirement; P3d's removal of the token path from execution is what
  makes the retirement small.

## Coverage ledger, closed

All twenty `Expression` constructors are accounted for, and the incomplete tail
is pinned at zero:

+ seven token-oracle rows (`Literal`, `Word`, `Call`, `Let`, `Pipeline`, `Block`,
  `Quotation`) — the interpreter and the lowered abstraction must agree;
+ seven expression-only rows (`Lambda`, `If`, `Partial`, `ReversePipeline`,
  `PatternMatch`, `Fail`, `Message`) — the lowering must produce the documented
  value, and `Message` needed a text expectation because a scalar harness cannot
  compare it;
+ six refusals (`Hole`, `DeclareKind`, `DeclarePredicate`, `Condition`, `Restart`,
  `Generic`) — each pins both the constructor and the refusal name.

Two facts the ledger produced that prose did not: `Call(Partial …)` is refused by
`CallTargetNotWord`, so a partial application needs a `Let` binding to be
callable, and `Quotation` lowers exactly like `Block`, so it needed no new
machinery.

## Exit criteria

W1 is done when all of these hold:

- one semantic form lowers through one function to one lowered encoding;
- every `Term` constructor is either mapped with a green differential case or
  retired, with the ledger showing zero unmapped entries;
- the history decision is recorded as a decision record with a number, not as a
  note;
- `docs/architecture/general-ir.typ` §10.1 lists one row per surviving source
  form, and each row names its test.

## Extension inventory

+ A generated coverage table in `docs/generated/` so the ledger is readable
  without running tests.
+ Differential cases for the portable projections (`Expression` document,
  `TermProgram`, package document) that round-trip through the encoder and
  re-lower, catching codec drift rather than semantics drift.
+ The same ledger shape for `PatternLogic` and `PackageLogic`, so a constructor
  added to any dialect is visible in one place.
+ A step-record instruction (D120), which is the prerequisite for retiring the
  interpreter without losing history.
