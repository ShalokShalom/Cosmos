= Quest: step records per statement

#strong[Status: answered by D201, and the statement slice has landed.] Step
records are per statement, emitted only by an emitter that asks for them
(`EvaluatorLowering.lowerExpressionWithStepRecords`), never by the default
emitter (`lowerExpression`, which stays `HistoryExcluded`). The snapshot half of
option C is the documented extension, not part of this slice.

== What the question assumes

Three different things are called a step, and they are not the same granularity.

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Level], [What a step holds], [Where]),
  [interpreter token],
  [The token, the stack *before* it ran, the program to resume, the location,
   baseline and position, and the previous step.],
  [`D29`; `components/prelude/evaluator/history.fs`],
  [kernel record],
  [A position only: `RecordStep of step: int` appends an int to
   `KernelState.History`. No stack, no snapshot.],
  [`D201`; `components/kernel/lowered-instructions.fs`],
  [statement],
  [One expression in a statement sequence. The boundary is real while lowering,
   and an emitter that asks for history writes a `RecordStep` at it, so it
   survives into the artifact.],
  [`components/prelude/evaluator/lowering.fs` §`lowerSequence`]
)

`EvaluatorHistory.rollbackTo` restores the stack, the program and the counter from
a token step (`history.fs:146`), and its own comment calls it "the mechanism a
debugger calls" (`history.fs:214`). `LoweredAbstraction.runFrom` cannot do that: it
takes the state the caller supplies and skips instructions (`lowered-abstraction.fs`
§`runFrom`). A kernel step therefore buys a *resume point*, not a rollback point.

== The decisive fact

Recording is position-only in the lowered artifact, so a step is usable only when
the stack at that position is reconstructible. Inside a statement it is not: a
record between the operands of a pipeline leaves a half-built operand stack, and
nothing in `RecordStep` or in the image carries that stack back. `runFrom` would
then resume onto an empty or foreign stack and produce a wrong value rather than a
refusal.

So the granularity question collapses into one: *does the artifact carry a stack
snapshot per step?*

+ **No snapshot (today).** Per-statement is the only sensible granularity: a
  statement boundary is the only point whose stack the lowering can describe
  without storing one, because it is where the preceding statement's effects are
  complete.
+ **Snapshot (D29's data model).** Then per-instruction becomes meaningful, and
  matches the interpreter's rollback exactly. That is a new data shape in the
  image, not a flag.

== Options

=== A. Per instruction, position only

Every lowered instruction emits a record. Cheap to implement (one wrap in
`lower`/`lowerAll`), wrong to use: resume points exist where the stack cannot be
rebuilt, so the extra records are unusable and the artifact grows by the
instruction count.

Rejected. It offers the appearance of debugger-grade granularity with none of the
data that makes it work.

=== B. Per statement, position only

The lowering inserts one record where each expression in a `lowerAll` sequence
finishes, and one at the start of each unit body so a resume can jump to a
definition. A statement's stack effect is complete there, so `runFrom` has a state
it can be given.

- Smallest honest claim: "you can resume at the start of any statement".
- History size is proportional to statements, not instructions.
- Preserves the differential: the lowered path never claims rollback parity it
  does not have, and the interpreter keeps its own token-level history.
- Cost: intra-statement undo is impossible in compiled code. A debugger that
  expects it gets a named refusal, not a wrong answer.

=== C. Per statement now, snapshots later

B, plus a documented extension: a `StepSnapshot` record that carries the stack
identity list, at which point per-instruction records become legal and the
interpreter's rollback contract can be met by compiled code.

- Complete without doing the expensive part twice.
- Needs a decision of its own: what a snapshot costs in image size, and whether
  it is recorded always or on request.

Recommendation: #text[C]. Emit per statement, and treat the snapshot as the
documented next step rather than pretending per-instruction records are useful
without it.

== The landed slice

Option B is implemented as an emitter choice, because recording is a property of
the program and not of the lowering:

- `EvaluatorLowering.lowerExpression` emits no record. Every existing caller,
  every emitted artifact, and every snapshot is unchanged, and `scopeOf` still
  reports `HistoryExcluded`.
- `EvaluatorLowering.lowerExpressionWithStepRecords` is the same lowering with
  `recordStatements` set. It writes one `RecordStep` at the start of each
  statement of a sequence that has more than one, with the position being the
  instruction index inside that sequence's own output.
- A single-statement sequence records nothing: there is no later statement to
  resume to, so a record there would cost artifact size for no capability.
- Only `Block` and `Quotation` bodies are statement sequences. Argument lists,
  message fields, and pattern clause bodies lower through `lowerAll` and never
  record, because they are operand sequences, not statements.
- Resuming stays the caller's contract: `runFrom` takes the state it is given,
  so a resume at a statement boundary is sound only if the caller supplies the
  stack that boundary had. No snapshot is implied.

== Open questions

1. B's claim, in one sentence: compiled code can resume at a statement boundary,
   not inside one, and that is a documented limit rather than a defect. This is
   what the landed slice asserts.
2. Whether bookmarks in compiled code are statement-granular (B) or refused with
   a named refusal until C. Still open: the lowering records positions, but no
   bookmark surface reads them yet.
3. Whether C is scheduled now or recorded as an extension inventory item. It is
   in the inventory below; nothing waits on it.

== Where a statement boundary actually is

Not every instruction seam is a statement, and four shapes need an explicit rule:

+ **`lowerSequence` sequences** (a `Block` or `Quotation` body). Each element
  starts at a known instruction count: that is the boundary, and it is where
  `lowerSequence` writes the record when the emitter asks for one. A unit body
  and the entry program reach the same rule whenever their shape is a `Block`.
+ **`If`.** Choosing a branch is a decision, not a statement. The record belongs
  after the branch merge, not at `Label endLabel`, otherwise a resume lands with
  an unbuilt result.
+ **`PatternMatch`.** The clauses are separate code paths; a record must follow
  the merge label, for the same reason. `lower` emits `Fail`/`Label endLabel` for
  the no-match case, which is a boundary only when it is the end of the statement.
+ **`Let`.** The value is evaluated, the binding is written, then the body runs.
  Recording between them is a useful resume point (the binding exists) but only if
  the frame is part of the state the caller supplies. For position-only records,
  the safe boundary is after the whole `Let`.

== Critical review

+ The phrase "step per statement" hides a decision about *source position*. A
  statement record should carry the position the `ExpressionSourceMap` already
  knows (`lowerExpressionWithInference` takes one), otherwise a resume cannot say
  where it is. That is an extension of `RecordStep`'s payload, not of its
  granularity, and it is cheap.
+ Resuming inside a `pattern` or a guard is unsound in the same way as inside a
  statement: no snapshot. The refusal must be explicit rather than an empty-stack
  failure.
+ `bookmark` is defined as a named step. Under B the bookmark granularity becomes
  statement-level in compiled code and token-level in the interpreter. That
  difference has to be named in the specification instead of being found by a
  user.
+ The interpreter's recording is opt-in and off by default. An artifact should
  follow the same rule: `scopeOf` already reports what a program does, so an
  emitter that never inserts records keeps `HistoryExcluded` and pays nothing.
+ D120's tiering promise ("a program that uses history gets the interpreter") is
  the fallback that makes B safe: a program that wants token-level rollback is
  still routed to the interpreter until C lands.

== Confirmation

The three questions in the previous section were the gate on this slice. The
landed form of the answer is: B, with recording opt-in, and C in the inventory.

== Extension inventory

+ `StepSnapshot`: a record that carries the stack identities, making
  per-instruction resume sound and giving compiled code the interpreter's rollback
  contract.
+ A position payload on `RecordStep` sourced from `ExpressionSourceMap`, so a
  resumed run reports where it is.
+ A statement-boundary table in the artifact so a resume can map a position to an
  instruction index without re-lowering.
+ Differential rows that resume at every statement boundary of the existing corpus
  and assert the same value as a fresh run.
+ The same boundary rule for book semantics, so a book definition is a resume
  point like any other statement.
