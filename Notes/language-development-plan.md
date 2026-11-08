# Language development plan

Status: proposal for confirmation. The normative ordering stays in
`docs/process/backlog-graph.typ` until this is accepted; then that page and
`docs/process/decision-index.typ` are updated in the same work item.

This plan is evidence-based: every claim below was checked against the code and
the generated status matrix, not against prose alone. Evidence is `file:line`.

## 0. Fix the docs before building on them

The audits found eight places where the documents contradict the code. Planning
on those facts would order the work wrongly, so this is Phase 0: mechanical,
small, one commit.

| Document claim | Reality |
|---|---|
| `docs/now.typ:31-45` "type-system A/B/C/D landed" | D has no static representation at all (`type-retrieval.fs:15-21` has no token type); `own`/`borrow`/`consume`/`release` are `NotInferred` |
| `docs/types/type-map.typ:19,20,24,34` cite `components/typing/type-inference.fs` | No `components/typing` directory exists; the module is `TypeRetrieval` in `components/verification/typing/type-retrieval.fs` |
| `docs/types/row-and-set-types.typ:82-83` "open effect subtraction is refused" | Implemented: `label-sets.fs:117-128`, `type-retrieval.fs:426` |
| `docs/types/effect-handlers.typ:19-20` "`with-handler` accepts any two code values" | The registry marks `with-handler` `NotInferred` (`signatures.fs:266`); the type rule is a fixed-arity AST special case (`type-retrieval.fs:1161`) |
| `docs/types/effect-polymorphism.typ:59-60` "registry has only closed-effect entries" | Open entries exist at `signatures.fs:190-199,225-250` |
| `Notes/w1-term-contract-plan.md:92-96` "`HistoryScope` is `HistoryExcluded`" | Both scopes and `scopeOf` exist (`lowered-instructions.fs:38-94`), plus `RecordStep` (`:85`) |
| `docs/architecture/irs.typ:88-91` "keep the symbolic form, or retire it from execution" | D200 fixed the answer (one lowered encoding); this text predates it |
| `docs/architecture/term-ir.typ:283-295` "interpreter and compiled path share the term list" | Contradicts D200's `Expression -> Instruction` target |
| `docs/process/backlog-graph.typ:72` "the prose parser still accepts definitions and must stop" | Half stale: named definitions stopped at D184 (`prose-parser.fs:1565-1566`); what survives is anonymous `logic (x y) = ...` (`:1556-1564`) and the `logic`/`cycle`/`define` words (`control.fs:173-234`) |
| `docs/language/action/prose-surface.typ:224` "no ... Lambda ... in the surface yet" | Contradicted by its own `:150` and by `prose-parser.fs:686,689` |
| `callable-contract.typ:52-53` "book words ... join the same resolver in later slices" | Accurate, and larger than it reads: books bypass the kernel entirely today (`kernel-bridge.fs:25-28`) |

Two code defects belong with Phase 0 because they are cheap and they block
nothing later, but they silently disagree with the type system:

- `components/verification/typing/unification-host.fs` has no `Code` case in
  `ofType`/`toType`, so a `Code` reaching `agrees` raises
  `MatchFailureException` instead of refusing.
- `signatures.fs:118` declares `condition: text -> condition`, while the runtime
  pops three texts (`condition.fs:38-72`).

## 1. What the language actually is today

| Layer | Form | Where | State |
|---|---|---|---|
| Surface | prose sentences, `logic`/`cycle` definitions, `.book` packages | `tokenizer.fs`, `parser.fs`, `book-host` | first slices landed (D142-D149, D184-D199) |
| Semantic form | `ExpressionLogic.Expression`, 20 constructors | `components/core/expression-logic.fs:46` | the one semantic form (D200) |
| Lowered encoding | `LoweredInstructions.Instruction`, 41 opcodes | `components/kernel/lowered-instructions.fs:44` | the one execution encoding (D200), step records landed (D201) |
| Interpreter encoding | `SymbolicAbstraction.Term`, 11 constructors | `components/kernel/symbolic-abstraction.fs:10` | **still the interpreter's program**, 22 files |
| Token front end | `EvaluatorProgram.ofTokens` -> `Term list` -> `Instruction list` | `program.fs:55`, `:79` | lossy, `toKernelTokens` throws on shapes it cannot carry |
| Portable documents | expression, pattern, package, term, image opcode JSON | `expression-portable.fs`, `pattern-portable.fs`, `package-portable.fs`, `term-portable.fs`, `prelude-image.fs:980` | five codecs; the term one is also a C ABI contract |

So the language has two front ends and two encodings, and the one that executes
in the interpreter is not the one the kernel executes.

## 2. The five real blockers

These are the things that actually gate language progress. They do not line up
one-to-one with W1-W4.

1. **Two encodings, and execution state is term-typed.** `EvaluationState.Program`
   is a `Term list` (`components/core/runtime-types.fs:534`); history steps persist
   term-program documents (`history.fs:59-110`); a code value is
   `[QuoteTag; documentId]` into term text (`code.fs:27-66`); and `scale_verify`
   in the C ABI verifies a term-program payload (`components/kernel/abi.fs:582`,
   `scale_abi.h:116`). Deleting `Term` today would delete rollback and the
   verification ABI.
2. **Function values have no implementation.** `PartialApplication`,
   `ArgumentHole`, and `ParameterHole` appear nowhere in `components/**`. A
   closure is fixed-arity and carries no runtime signature. D172 is design only
   (`decision-records.typ:7360-7361`), yet the surface doc claims the first
   `logic` slice implements function values.
3. **Handlers are shallow and label-free.** The runtime handler catches every
   diagnostic (`condition.fs:206-224`) and its frame has no label
   (`runtime-types.fs:120-126`); the type rule hard-codes `"failure"`
   (`type-retrieval.fs:1173`); `with-handler` is `NotInferred`
   (`signatures.fs:266`); effects are computed and then discarded at the lowering
   handoff (`lowering.fs:739-746`).
4. **Inference is a thin cover.** 100 of 143 registry words are `NotInferred`
   under one generic reason (`signatures.fs:73-74,95-280`); `PatternMatch` is
   refused (`type-retrieval.fs:1412`); `DeclareKind`/`Condition`/`Restart`/
   `Generic` are silently typed `unit` with no effects (`:1413-1424`).
5. **There is no single call contract, and two of the three candidates cannot
   express a call.** `Callable` is one DU with eight cases
   (`callable.fs:21-29`) but has no F#-primitive and no book-word case; a builtin
   never gets an identity, it re-dispatches a token into a 200-line string match
   (`evaluator.fs:63-268`, `callable.fs:108`). Books declare no primitives and
   route every `CallWord` back into the F# evaluator
   (`book-runner.fs:679,962`, `kernel-bridge.fs:25-28`). The F# package document
   has exactly one caller, a test (`toPackageDocument` used at `tests.fs:16843`,
   no CLI mode in `cli.fs:56-89`). Elixir has no call operation at all: its wire
   carries `send/reply/monitor` and a numeric-or-text body
   (`adapters/elixir/lib/scale/wire.ex:14-28`). So "unify the two host contracts"
   is really "give the Elixir lane a contract, give the F# package an execution
   path, and fold books and builtins into one resolver".

## 3. Proposed order

Four phases. Each phase gate is a testable claim, not a feeling.

### Phase 1: one encoding in fact (W1a, W1b, W1c)

**Measured before starting (2026-11-08).** The code-value surface is wider than
the plan assumed but shallower: `EvaluatorCode.encode`/`decode` are the only
readers and writers, and they are called from twenty files
(`scheduler.fs`, `runtime-guard.fs`, `with-fields.fs`, `channel-event.fs`,
`closure.fs`, `control.fs`, `dispatch.fs`, `history.fs`, `pattern.fs`,
`quotation-run.fs`, `value-sets.fs`, `callable.fs`, `condition.fs`,
`evaluator.fs`, `actor-host.fs`, plus tests). Every one of those passes a
`SymbolicAbstraction.Term list` through two functions and nothing else. So the
document swap is a change behind one codec, and the call sites move when
`Program` changes type, not when the document format changes.

Consequence for the order: W1a and W1b are one slice in practice. Swapping the
document while `Program` is still a term list would leave the codec converting
shapes at every call. Do them together: change `Program` and the codec, keep
`encode`/`decode` as the seam, and let the twenty call sites move by type. That
makes the pair a *medium* slice rather than the two large ones the table sized.

**Status of the first half.** The instruction document exists:
`PreludeImage.toInstructionDocumentJson`/`fromInstructionDocumentJson` write and
read `{"form":"instruction-program","schema":1,"instructions":[...]}` using the
image's own opcode writer, and `EvaluatorCode.encodeInstructions`/
`decodeInstructions` carry it behind the unchanged `[QuoteTag; documentId]` value.
The two readers refuse each other's form by name. Nothing executes it yet, so
`Program` and the twenty call sites are the remaining half.

**Coverage of the opcode, stated honestly.**

| Kind | What covers it |
|---|---|
| property | the image round-trip property now generates `MakeCode` (one body level), so the recursive spelling is exercised on every run |
| property | a document round-trip property: any generated instruction list survives `toInstructionDocumentJson`/`fromInstructionDocumentJson` unchanged |
| property | a token-boundary property: `ofTokens` and `ofTokensAsInstructions` agree shape-for-shape on generated token lists |
| example | a kernel `MakeCode` run whose code value `decodeInstructions` returns as the original body |
| example | each reader refusing the other's document form by name |
| example | a spelling pin on `"form": "instruction-program"` and `"op": "make-code"` |
| refusal | the IL writer reports `make-code` as an unsupported opcode rather than miscompiling it |
| integration | **none yet, deliberately**: nothing emits `MakeCode`, so no book or demo path can reach it. The integration case lands with the program-type swap. |
| snapshot | the emitter snapshot is unchanged because lowering never emits `MakeCode`; the document has a spelling pin instead of a full-text snapshot, because the writer indents and whitespace is incidental while the two names are the contract |

**Two compile-order facts now load-bearing.** `prelude-image.fs` must compile
before `lowered-abstraction.fs`, and `code.fs` before `program.fs`: the executor
in each pair needs the document writer. Swapping either back is a build error, not
a silent one, but the reason lives here and not at the call site.

**The second half, and the one decision inside it.** The interpreter's loop
consumes exactly three term shapes (`evaluator.fs:337-377`): `LiteralNumber`
becomes a stack push, `Word` becomes a late-bound word call, and `Quote` becomes
a code value at run time. Two already have instructions. The third does not, so
the migration needs one new opcode, `MakeCode of body: Instruction list`,
spelled `{"op":"make-code","instructions":[...]}`. Its body is nested
instructions, which is the only recursive shape in the union; the alternative, a
side table of code blocks, buys nothing while the document is per-value.

Order that keeps every step green:

1. `MakeCode` in the union, plus its image and document spelling, plus a
   round-trip test and an older-reader refusal test. Additive; nothing uses it.
   **Landed.**
2. The token reader's instruction form and its executor, beside the term ones,
   with a parity test that both boundaries read a token the same way.
   **Landed** as `ofTokensAsInstructions` and `executeInstruction`; `code.fs` now
   precedes `program.fs` in the Prelude project so the executor can store a
   `MakeCode` body as a code value.
3. `EvaluationState.Program` becomes `Instruction list`, and the loop's three
   cases become instruction cases. The twenty `Program = ...` sites move by type.
   This is the remaining atomic change, and it subsumes step 4: the codec and the
   type must move together.
4. `encode`/`decode` become the instruction codec, and the ledger pin for
   `Quote` goes from `UnitOnly` to a real target.

Step 3 is the one that touches everything at once, which is why steps 1 and 2
land first: after them the type change is mechanical.

**Step 3 as an executable checklist (measured 2026-11-08).** Forty-five sites in
twelve files. Grouped by the kind of change each needs, because they are not the
same job:

*Kind A: type flow only (mechanical, no semantic decision).*
`runtime-types.fs:533` (`Program` field), `history.fs:158` and the history step
record, `dispatch.fs` and `condition.fs` `CallerProgram` fields, `scheduler.fs`
(9 sites) and `book-runner.fs:945`, `actor-host.fs:154`, `prose-parser.fs:1925`.
These only need the field, the record, and the empty-list literals to change type.

*Kind B: token-shaped programs built inline (mechanical but easy to get wrong).*
`closure.fs:80`, `condition.fs:122,163,199,220`, `dispatch.fs:886,1198`,
`callable.fs:129`, `pattern.fs:523,531,553`, `channel-event.fs:167,190,417`,
`scheduler.fs:278,499,810,1331-1392`, `kernel-bridge.fs:15,21`,
`bookmark.fs:24`. Each prepends `[Word "token"]` or `ofTokensOrFail body` to
`state.Program`; each becomes an instruction list, and the sentinel words
(`returnToken`, `methodReturnToken`, `handlerReturnToken`) become
`CallWord(token, None)`.

*Kind C: programs that parse surface syntax out of the queue (the real work).*
`control.fs:27,28` (`collectQuotation` and the brace reader), `:87-101`
(branch/quote splices), `:125` (`parseDefinition`), `:180` and `:116`
(`parseCycleGroup`, `containsWord`), `value-sets.fs:408,409` (`collectSet`),
`:437` (actor-word scan), `channel-event.fs:180` (guard-token scan),
`quotation-run.fs:58` (actor-word scan), `pattern.fs:424` (`remaining`),
`quotation-run.fs:70,91`. These match `Word "logic"`, `Word "{"`, `Word "["` and
match/rebuild term lists. Every pattern becomes `CallWord(name, None)`, every
rebuilt body becomes instruction construction, and each needs a test that the
surface meaning is unchanged: `logic`/`cycle`, brace quotation, set literal,
pattern clause, `quotation-run`.

*Kind D: the codec (one change, wide effect).* `code.fs` `encode`/`decode` become
the instruction codec and the term ones are deleted; the twenty `EvaluatorCode`
call sites then move by type. `EvaluatorHistory.record` takes an instruction.

*Kind E: the loop.* `evaluator.fs:318-390`: the `term :: remaining` destructuring,
the `Quote terms` case becoming `MakeCode body`, `executeTerm` becoming
`executeInstruction`, and the decision the critical review flagged:
`executeInstruction`'s `invalidOp` on an off-surface instruction must become a
named refusal or stay an invariant, and the loop makes it reachable.

**Why this is not one sitting.** Nothing compiles until A through E land together.
The honest options are (a) a dedicated window with the fast type-check loop
(`check_project`, which needs the FsiMcp restart) so the cascade is fixed without a
two-minute build per error, or (b) accepting a red tree for the duration. The plan
recommends (a); a red tree defeats the freshness rule the rest of the workflow
depends on.

**W1a: the instruction document.** Give persisted code, history, and verification
an instruction-form document, reusing the image opcode writer
(`prelude-image.fs:980`) rather than a second codec. `scale_verify` and the
receipt then speak instruction documents.
Gate: codec round-trip property, golden vectors, `scale_verify` conformance in
`just kernel-aot`, and one ABI decision (see Q2).

**W1b: the interpreter runs the instruction set.** `EvaluationState.Program`
(`runtime-types.fs:533`, verified: `SymbolicAbstraction.Term list`) becomes
`Instruction list`. The token reader cannot be deleted, and the first draft of
this plan was wrong to offer that: `ofTokens` is the runtime's text-to-program
path, not a test helper. It is called at `kernel-bridge.fs:21` for every book and
word call, `pattern.fs:523,553` for guard bodies, `actor-host.fs:154`, and
`discourse.fs:111`. It becomes a reader *into the final encoding*: tokens a static
form can express go through `ExpressionLogic` and the one lowering; the dynamic
words (`define`, `let`, `word`, `apply`, quotation splices) stay words whose
meaning is a runtime action. No second encoding survives; what survives is a
second surface, which is a surface and not an IR.
Gate: the differential matrix stays green for every row, rollback restores the
same value as today (a test that fails without the change), and a book call and a
guard body still run after the reader changes.

**W1c: delete the term form.** Flip the two `UnitOnly` constructors
(`LiteralAtom`, `Quote`) from the pinned 2 to 0 with an instruction target for
each (`differential.fs:309-347`, `tests.fs:5234-5241`), remove
`symbolic-abstraction.fs`, `term-portable.fs`, `term-verification.fs`, and
`term-vectors/`, and delete the kernel-term row from `general-ir.typ:289`.
Gate: W1's four exit criteria from `Notes/w1-term-contract-plan.md:164-174` all
hold, and `just kernel-aot` passes.

### Phase 2: language core semantics (F, W2a, W2b, S)

**F1: typed function values.** One signature record (parameters, result, effects,
supplied count) carried by a closure in both engines; the registry stops guessing.
Gate: a function value reports its signature; a call checks arity and effects.

**F2: currying.** Left-associative application; fewer arguments return a function
with the supplied prefix evaluated once; complete arity runs the body; extra
arguments apply the result; a multi-result word is a named refusal (D172).
Gate: the four cases as separate tests, plus the multi-result refusal.

**F3: explicit holes.** `f(1 _)` fills a declared position and preserves the
remaining signature (`PartialApplication`/`ArgumentHole`); `ParameterHole` stays
unimplemented by design.
Gate: a hole in a non-leading position and a disagreement between the signature
table and a method, which is a diagnostic.

**W2a: effect labels become identity.** Decide nominal labels (image ids) versus
elaboration-time strings; put the label on the runtime handler frame; add
`with-handler` to the registry. This is the decision that makes runtime matching
possible at all.
Gate: a handler removes exactly the label it names; a different label passes
through.

**W2b: a general handler expression.** A handler node in `Expression`, lowered and
typed, with the label in the type rule instead of the literal `"failure"`. One
shot only.
Gate: a handler over a non-`failure` effect; the refusal when the handled label is
absent.

**S: sequences and recursive types (D132).** Ordered projector and words; a
recursive reference plus a fixpoint rule in the portable type document. This is a
prerequisite for W4a, because a compiler works over sequences.
Gate: a list program runs on both engines; a recursive type unifies with its own
unfolding under a checked rule.

### Phase 3: host unification and coverage (W3, P4, L)

**W3a: finish the resolver.** Add the F#-primitive and book-word cases to
`Callable`, and route builtins through `Invoke` instead of re-dispatching a token
into the string match at `evaluator.fs:63-268`. This is the cheap half and it is
where one refusal vocabulary starts.
Gate: every name kind reaches `Invoke`; a failed host call reports one named
failure instead of today's three (`PrimitiveFailure` at `callable.fs:43`,
`Diagnostic` at `applyBook`, `{:error, :unsupported}` at `wire.ex:28`).

**W3b: books through the same resolver.** Book calls become `Invoke` with arity
and effect checks (D189's later slice), and the kernel bridge stops being the only
route.
Gate: a book call and a host call fail the same way; no book word re-enters the F#
evaluator to decide its own arity.

**W3c: the F# package actually executes.** A CLI mode publishes and loads an F#
package into an image whose units declare their primitives, plus the recursion
classification record for tiers A and B (D172).
Gate: an F# package runs from the CLI with no test-only helper; an override
without a declared reason is a build error; the cross-tier regression suite runs.

**W3d: an Elixir call contract.** Add the call operation and its term encoding to
the wire, then reuse W3a's signature and refusal vocabulary. This is the largest
piece in Phase 3 and it is new work, not unification.
Gate: the Elixir peer passes the same conformance set as the F# lane, and a wire
failure maps to the same refusal name.

**W3e: prose executes only.** Remove anonymous `logic (x y) = ...` from non-book
parsing and retire the `logic`/`cycle`/`define` definition words, since
declarations come from books (D185).
Gate: prose refuses a definition by name, and the existing definition tests move
to the book lane rather than being deleted.

**P4 completeness:** the 100 `NotInferred` words fall to a small remainder with
named reasons; `PatternMatch` and the declaration/restart/generic forms produce
real types or refusals instead of silent `unit`; the `Code` case exists in both
directions of the host/reference bridge.
Gate: registry coverage is a pinned number that can only shrink, like the term
ledger.

**L: lowering completeness:** `Pin`, `Repeated`, `Shadowing`, and guards in the
lowered path (image-resident bindings plus pinned predicate units), and the
declaration/message registry contract.
Gate: each shape either lowers or refuses by name, and the interpreter is no longer
the only engine for it.

### Phase 4: self-hosting, then self-typing (W4a, W4b)

W4a needs Phase 1, F, S, and W3. W4b follows a working self-hosted compiler and
Phase 3's inference coverage; it cannot be faked with a `Sort "type"` label.

The risk that matters here: a book program does not run in the kernel today. Book
units are passed to the callback host (`book-runner.fs:679,962`), which in the CLI
is the F# evaluator (`kernel-bridge.fs:25-28`), so "compiled" book execution still
depends on the interpreter being present. A self-hosted compiler has to run
without it, which means W3b (books through the kernel) is a prerequisite for W4a,
not an optional cleanup. Confirm this as Q6.

### Coordination and relative size

Two work items touch the same record: W1b changes `EvaluationState.Program` and
D179 stages 2 and 3 move `SchedulerState` out of `EvaluationState`
(`decision-records.typ:7548-7573`). They must not run concurrently. Sequence them,
and prefer landing W1b's field change with D179 stage 2 so `runtime-types.fs` is
opened once.

| Slice | Relative size | Why |
|---|---|---|
| W1a instruction document | M | one codec, one ABI gate, golden vectors |
| W1b program becomes instructions | L | the token reader, history, bookmarks, and guard bodies all move |
| W1c delete the term form | S | mechanical once W1a and W1b are green |
| F1 signature record | M | touches both engines and the registry |
| F2 currying | M | application rules are small, the tests are not |
| F3 holes | S | one construct, one refusal |
| W2a effect labels | M | image or registry identity, plus schema impact |
| W2b general handler | L | new expression node, lowering, type rule |
| S sequences, recursive types | L | new projector, new words, fixpoint rule |
| W3a resolver completion | M | perf-sensitive builtin path |
| W3b books through the kernel | L | removes the F# evaluator round trip |
| W3c F# package execution | L | CLI mode, declared primitives, classification |
| W3d Elixir call contract | L | new wire operation and term encoding |
| W3e prose executes only | M | parser split, test migration |
| P4 completeness | L | 100 words, each with a rule |
| L lowering completeness | L | patterns and declaration registry |
| W4a, W4b | L each | gated on everything above |

## 4. Deliberately not scheduled

Multisets and co-products (no consumer, D132); the full natural prose grammar
(D131); the Scale Prelude port (D133); handler resumption beyond one shot (Q4);
`StepSnapshot` (kept as the inventory item it already is, unless Q4 says
otherwise).

## 5. Decisions that need confirmation

**Q1. Shape of the W1 retiral.** Two defensible endpoints:
(a) *full retiral*: the term form and its document are deleted, and the
instruction document replaces them everywhere including `scale_verify`;
(b) *execution retiral*: `Term` leaves execution but the term document survives as
the portable quotation and verification projection.
Recommendation: (a), because D200 says one lowered encoding and two portable
documents for one layer is the duplication this work exists to remove. (b) is
cheaper now and pays a second codec forever.

**Q2. The ABI consequence.** (a) changes the `scale_verify` payload, so the ABI
moves to 5 with a documented break and the C conformance harness updated in the
same slice. This is the one irreversible, externally visible choice in the plan.
Confirm: bump, or keep ABI 4 and accept both payloads for a deprecation window.

**Q3. Where function values sit.** Recommendation: F before W2, because a handler
is a function value with effects, and W3's one contract needs a signature to
compare. The alternative (W2 before F) means writing the handler calling
convention twice.

**Q4. Handler resumption.** Recommendation: one shot now, resumable handlers as a
numbered inventory item, because resumption forces a continuation type, a new
frame schema, and reclaim roots (`reclaim.fs:132`). Confirm that W2b may ship
one-shot only.

**Q5. Sequences before self-hosting.** Recommendation: yes, S lands before W4a and
carries its own decision record for the ordered projector. Confirm the placement,
since D132 left the consumer unspecified.

**Q6. Books through the kernel before self-hosting.** Settled as a gate rather
than a question: W3b is a W4a prerequisite because a book unit's call is answered
by the F# evaluator today (`kernel-bridge.fs:25-28`), and the phase order already
puts W3b before W4a. The critical review removed this as an ordering decision; it
is now the W3b gate.

**Q7. Which artifact is the call contract.** Three shapes carry a call today: an
in-process `Callable` DU for books (`callable.fs:21-29`), a C ABI call
(`scale_call`), and an actor-wire operation for Elixir
(`adapters/elixir/lib/scale/wire.ex:14-28`), with three different silent-failure
outcomes. Recommendation: one signature and one refusal vocabulary, with each
transport reduced to a mapping function behind it, so the contract cannot drift
three ways again. Confirm that the wire stays in the inventory of the same decision
rather than becoming a second source of truth.

## 6. Ratchets this plan adds

- Registry coverage: a pinned count of `NotInferred` words that can only shrink.
- Term ledger: `UnitOnly` pinned at 2 now, 0 at W1c.
- Differential: one row per surviving source form, each naming its test.
- Every phase gate names the test that fails without the change; a filtered or
  stale run is not evidence.

## 7. Critical review of this plan

Run after the first draft, against the evidence. Six findings, one of them fatal
to the draft.

1. **Fatal, fixed.** The draft offered deleting the token reader in W1b. It is the
   runtime's text-to-program path (`kernel-bridge.fs:21`, `pattern.fs:523,553`,
   `actor-host.fs:154`, `discourse.fs:111`), so deleting it would delete book calls
   and pattern guards. W1b now turns it into a reader into the final encoding.
2. **Removed as a question.** Q6 was an ordering decision that the phase order
   already honours; it is now a gate. A plan that asks the owner to confirm what
   its own structure already does is noise.
3. **Merged.** Q1 and Q2 were separate but are one versioning decision: full
   retiral forces the ABI move, and W2a's effect labels may force an image schema
   move. They are decided together so the project bumps versions once.
4. **Added.** The draft ignored D179 stages 2 and 3, which edit the same
   `EvaluationState` record as W1b. Two slices editing one record concurrently is
   how records rot; the coordination note above sequences them.
5. **Softened.** Some evidence comes from the audits and was not re-verified line
   by line by the author (the `Callable` case list, the `QuoteTag` document, the
   `NotInferred` count of 100 of 143). The *conclusions* do not rest on those
   exact numbers, but the numbers are audit-reported, and one line number was
   already off by one (`Program` is `runtime-types.fs:533`, not `:534`). Line
   numbers here are a snapshot and will drift.
6. **Accepted compromises**, all now decided rather than pending: one-shot
   handlers (no resumption), no headline performance work in W1-W3, and a second
   surface (tokens) surviving as a reader. Each is a deliberate trade, and each
   can be revisited from the Complete projection below.

What the review could not settle: whether W1a's instruction document is a small
change or a large one depends on how many stored code values exist outside the
interpreter, which nobody has counted. W1a starts with that count, and the slice
is re-sized if it is large.

## 8. Definition of done

The language program is done when one sentence holds and a test proves each clause:
a program written in any of the three surfaces (`logic`, prose, `.book`) parses
into one semantic form, lowers through one function into one execution encoding,
runs in the kernel without the F# evaluator, carries its signatures and effects to
the point of call, and can be resumed, verified, and typed through that same
encoding with nothing kept alive only by the interpreter.

## 9. Complete projection

Everything this plan deliberately does not build now, with the use case that
would justify building it later. The current slices stay compact and cheap; this
list is what keeps them complete.

| Extension | Use case it would unlock |
|---|---|
| `StepSnapshot` (stack identities per step) | debugger-grade undo in compiled code; rollback parity with the interpreter |
| Resumable and multi-shot handlers | generators, replayable retries, algebraic effects over ordinary functions |
| Handler polymorphism | a handler whose effect is a parameter, so libraries can be written once |
| Parameter holes | point-free definitions without an inline lambda |
| Curried multi-result words | consecutive stack effects used as function values |
| F# package manifest tier C (strict explicit) | an audited package where a missing declaration fails the build |
| Elixir package loader on the same contract | one call surface for two hosts, which is what W3 exists for |
| Sequences with an ordered projector | lists, folds, and the self-hosted compiler's own token stream |
| Recursive types with a fixpoint rule | trees and grammars typed without a nominal escape |
| Multisets and co-products | set-theoretic types and sums, once a consumer exists |
| Declaration and message registry | `kind`/`predicate` nodes executing in the kernel instead of the interpreter |
| Lowered pattern guards | guard-bearing matches in compiled code |
| Value-unit initialization (D192) | a unit that computes its own value at load |
| First-class closures in the writer | closures that survive the IL path instead of refusing |
| Statically referenced AOT artifact | a product whose units never need the JIT |
| Source position on `RecordStep` | a resumed run that can say where it is |
| Statement-boundary table in the artifact | mapping a source position to an instruction index without re-lowering |
| Differential rows that resume at every boundary | proving resume parity across the whole corpus |
| Book-level resume | a book definition as an ordinary resume point |
| Ownership types on the term representation (W8) | a static answer to the linearity that is a runtime trace today |
| Full natural prose grammar (D131) | prose that reads as English beyond the sentence subset |
| Scale Prelude port (D133) | the Prelude authored in Scale rather than F# |
| Effect rows supplied by a package's own signature | host calls whose effects are checked, not declared by hand |
| Hot reload of a live handler frame (W5) | swapping a handler while an actor is suspended in one |
| Typed diagnostics for currying mistakes | an error that names the missing argument type, not an arity mismatch |
