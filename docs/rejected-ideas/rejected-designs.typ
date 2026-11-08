= Rejected and reserved designs

#strong[Status: reference.] This page records alternatives that were considered and rejected, and
code that was removed with the condition for reintroducing it. It is not a roadmap; the roadmap
records scheduled work.

== Rejected alternatives

#table(
  columns: (3fr, 6fr, 3fr),
  align: (left, left, left),
    table.header(table.cell(fill: luma(240))[Alternative], table.cell(fill: luma(240))[Why it was rejected], table.cell(fill: luma(240))[Record]),
    table.hline(),
    [Single-dispatch receiver methods], [Multiple dispatch is the only polymorphism: a generic function dispatches on all arguments, so receiver-first lookup would be a second, weaker mechanism. The cell system still gets nominal class dispatch through class specializers inside that one mechanism.], [D19, D46, D47],
    [A typeclass or word registry], [The dispatcher is a match; a typeclass or registry would add an abstraction over the same table. The image-resident word and generic protocols are the extension points instead.], [D19, D21; `docs/runtime/evaluator-slicing-and-scheduler.typ`],
    [Ad-hoc overloading or a silent `method_missing` fallback], [A missing slot, an unbound slot, a wrong value and an undecidable dispatch are named refusals. A silent fallback would make an unknown case look like a valid one.], [D46, D47, D49],
    [A conditional verb body (`Expression.Ite`)], [Guarded rows plus a completeness proof keep the implementation fragment small and make the gap/no-overlap claim provable instead of asserted.], [D13],
    [A machine-learning decision model inside the verified path], [A probability is neither a proof nor a witness, so it cannot enter the proof-or-witness trust boundary. It could only advise outside the verified path.], [D13],
    [Lean as the proof backstop], [Superseded by D10: Z3 is in-process; Lean remains only a possible optional export.], [D10],
    [A unified Julia/Elixir source language], [Julia and Elixir have incompatible dispatch, type, runtime, module, and macro semantics; one source language would either lose one semantics or become a third. Each implementation keeps its host language and integrates through actor IPC instead.], [D93 revoked by D151; `docs/kernel/actor-ipc.typ`],
    [A reflective tower or an open meta-level], [The meta-level is fixed and the class/metaclass closure is finite; the goal is kernel reduction, not a reflective tower.], [D16, D17, D49],
    [Physical deletion of image entries], [The image is append-only. Opt-in compaction preserves stable addresses; physical deletion remains a non-goal.], [D52, D53],
    [`all` as a quantifier], [Use `every`; the subject stays singular. `all` is refused with `UnsupportedQuantifier`.], [D18],
    [Fable as a browser compile target], [Out of scope for now, not rejected on technical grounds: it becomes useful only if the browser deployment is a goal.], [Q6],
)

== Removed code and reserved names

- `Violations.Witness.ShrunkSentence` was removed because no stage produced it. Reintroduce it when a
  stage can shrink a failing sentence (for example a parser or discourse witness) and
  `Presentation` can render it: add the producer, the presentation line, and a regression test.
  D62 and D72 record the original intent.
- `Presentation.Rendered` was removed because `render` and `renderTechnical` return strings and no
  caller needed a structured value. Reintroduce it when the CLI or a UI needs structured output
  (headline plus details, or JSON), not pre-joined text: add the constructor, the renderer, and the
  consumer together.
- `Presentation.withNearest` is implemented but only the suite calls it. TODO: wire it into the
  CLI/demo.
- `Violations.Layer.Discourse` is reserved for a host-provided contradiction. TODO: add a
  contradiction detector that produces this layer; `Discourse.utter` currently implements only the
  rollback policy.
- `DecisionTable.explain` is the adapter from a `NoMatch` or `Ambiguous` selection to one
  `Violation`; `Diagnostics.addSelection` feeds the common sink (D74). The CLI mode is
  `--table <path>`.
- The Basic English decision-table surface is D74 (`table`, optional `policy:`, transition rows,
  `end`). It is a document format, not a new sentence grammar. The CLI mode is `--table <path>`.
- Character offsets are implemented as D76: `Violations.parseText` fills `Violation.Span.Start`
  and `End` from the first surface occurrence; the token-only API stays offset-free.
- The mixed `Repair`/`RepairSearch` model was rejected and replaced as D79:
  `FixedSpecificationAcceptance` consumes a frozen specification and an implementation proposal,
  while `NearestSentence` produces advice. The old optional `Patch.SpecEdit` and the string
  `Violation.Repair` do not exist.

== Non-goals, not rejected designs

Bounded mailboxes, actor priorities, scheduling across instances, and the optional
DSPy-shaped model layer (D42) are explicit boundaries rather than rejected designs. The
implementation, not a hidden feature, is the record. See `docs/process/commitments-ledger.typ` and
`docs/process/roadmap.typ`.
