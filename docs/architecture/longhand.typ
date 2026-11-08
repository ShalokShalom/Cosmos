= LONGHAND: one conversation, one claim ledger

#strong[Status: proposed, not a decision.] At the time of writing the decision
register ends at D199; no record has been appended for this design. No LONGHAND
source, word, `.claims` parser, MCP server, or actor exists yet. The mechanisms
named below exist in some form; their status is in the table. Their composition
is not. This document is the contract to critique before a slice is scheduled.

#block(
  fill: luma(240),
  inset: 12pt,
  radius: 4pt,
  width: 100%,
)[
  *One conversation is enough if every durable claim is written down, checked,
  and delivered at the moment it is needed.* The conversation is the work, the
  ledger is the state, and compaction may forget the transcript because the
  ledger cannot be forgotten.
]

== The problem

A long conversation fails in a specific way, and the failure is not a lack of
context. It is a loss of *standing*.

+ The context window is finite. Compaction summarizes the transcript; a summary
  of a summary drifts, and the drift is invisible to the reader.
+ The agent's account of what it learned is self-reported. A journal written by
  the agent that made the mistake is a rationalization log unless something
  outside the agent checks it.
+ A fleet of agents does not repair this. It adds coordination, leases, and
  consensus, and every agent has the same self-reporting problem. The original
  fleet design was generation-side architecture for a verification-bound
  problem; this document keeps the useful half and deletes the fleet.
+ The useful unit is not a message, a task, or a file. It is a *claim*: a
  statement that something is true, anchored to something in the repository,
  with the evidence that settles it.

LONGHAND is the harness that makes the claim the unit. It is not a new agent
runtime, not a memory database, and not a chat protocol. It is a ledger plus a
small set of words over it.

== What LONGHAND is

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Piece], [Responsibility]),
  [Ledger], [An append-only `.claims` document, versioned with the code, that
  holds claims, verdicts, and explicit coverage gaps.],
  [Claim], [An open typed term with anchors, a grade, support links, and an
  optional check quotation.],
  [Verdict], [`Confirmed`, `Refuted`, or `Undecided`, with a witness, a
  residual, or a named reason. Never a silent false.],
  [Gate], [The only role that may append a `Confirmed` verdict. It does not run
  checks; it ratifies evidence.],
  [Brief], [A bounded projection of the ledger, recomputed from the ledger, not
  from the conversation.],
  [Delivery], [Pull via an MCP tool; push via an instruction entry where the API
  permits it. The pull path is the guarantee.],
)

The design has one continuing conversation, one ledger per surface, and four
narrow roles. Coordination between agents is not simplified; it is absent.

== Why Scale is the host

The fit is not metaphorical. The properties LONGHAND needs are the properties
Scale already treats as load-bearing.

#table(
  columns: (auto, 1fr, auto),
  align: (left, left, left),
  table.header([Need], [Scale mechanism], [Status]),
  [Never rewrite, never erase], [The image: an append-only heap of tensors],
  [landed],
  [Resume mid-thought], [`StepTag` history, `rollback`, bookmarks (D29, D178)],
  [landed],
  [Failures are values], [`Result` with a named reason; no exceptions in the
  language], [landed],
  [Honest verdicts], [`Member`/`NotMember`/`Undecided`,
  `Proved`/`Falsified`/`Unknown`/`Unavailable`, `Selected`/`NoMatch`/`Ambiguous`],
  [landed],
  [Structural matching], [`unify` with occurs-check (D15); Miller-pattern
  higher-order unification (D59)], [landed],
  [Dispatch on claim shape], [Image-resident generic functions and methods
  (D19)], [landed],
  [A total, budgeted selector], [Decision tables with proved completeness and
  disjointness (D13, D73)], [landed],
  [Portable, escaped line discipline], [The `.scale` document format and its
  escaping/round-trip rule (D27)], [landed],
  [A refinement loop], [The refiner plus the `.scale` document (D7)],
  [landed],
  [A portable, verifiable term], [The typed term IR (D160–D167; canonical names
  in D185)], [landed],
  [Extend without editing the host], [Metaobjects, the MOP plan (D16, D17)],
  [partial],
)

The image is the runtime store and the `.claims` document is its portable
projection. The image supplies the append-only substrate and the history; the
document supplies the reviewable, diffable, versioned source of truth. Neither
is sufficient alone, and the two must round-trip.

== The ledger is a sibling document

The ledger is a `.claims` document, not an extension of the `.scale`
specification format and not an extension of the Prose sentence grammar
(formerly Basic English, D186). The Prose grammar is deliberately closed; that
closure is what makes it trustworthy. Widening it to arbitrary claims would
spend the property that makes it useful.

What `.claims` shares with `.scale` is the *line discipline*, not the grammar:

+ one directive per line;
+ free text escapes `\\`, `\n`, and `\r`, so a field can never be read back as
  another directive;
+ an unknown directive is a named refusal;
+ a duplicate singleton directive is a named refusal;
+ a block ends at the next block kind or at end of file;
+ canonical emission orders the directives and round-trips the document
  byte-for-byte.

A minimal document:

```text
ledger: cache
version: 1

claim: c-0001
surface: cache
grade: A
statement: ( ?metric dominates ?cost :EvictionCost )
anchor: src/cache/evict.fs#selectVictim
evidence: check cache-eviction
check: ( p99 dominates ttlFanout :EvictionCost )

verdict: c-0001 confirmed
evidence: check cache-eviction
substitution: metric=p99 cost=ttlFanout
date: 2026-11-07
```

The document header is `ledger:` plus `version:`; both are required. The block
kinds are deliberately few:

#table(
  columns: (auto, auto, 1fr),
  align: (left, left, left),
  table.header([Block], [Cardinality], [Meaning]),
  [`claim:`], [one per claim], [Begins a claim. Carries the statement, surface,
  grade, anchors, support links, evidence reference, optional formal projection,
  optional `check` quotation, and escaped `notes` text.],
  [`verdict:`], [one per verdict], [Begins a verdict event. The current status
  is the latest verdict; earlier verdicts stay in the document.],
  [`contest:`], [one per contest], [A refutation or objection that has not been
  ratified. It is evidence, not a verdict.],
  [`surface:`], [one per surface], [Declares the anchor set and whether the
  surface is a partition (O3/O4).],
  [`unclaimed:`], [one per gap], [An anchor that is deliberately not claimed,
  with a reason. A gap is data, not an omission.],
)

The parser refuses a `verdict:` block whose claim id is unknown, a `because:`
link to an unknown claim, and a `surface:` that does not cover the anchor.
Those are the three failures that would make the ledger lie about its own
structure. A support link is local (`c-0001`) or qualified
(`cache#c-0001`); an unqualified link never crosses a document.

The thread checkpoint is not a ledger block. It is session state: the Watcher
writes it to the session store, and the brief reads it from there. Execution
state is never serialized into a versioned document.

The document is the source of truth. A verdict is written to the document before
it is projected into the image; if the image is lost, the document rebuilds it.
This is the write-ahead rule, moved from "flush on eviction" to "checkpoint on
every settled step" because an external client cannot observe the pre-compaction
boundary reliably.

== The claim is an open term

A claim is a record, and the record is an open row:

```text
Claim = { id: ClaimId, surface: SurfaceId, grade: Grade,
          evidence: Evidence, statement: Pattern,
          formal: Option Formula, anchors: PathSet,
          because: ClaimId list, check: Option Quotation,
          notes: Text, ... | ρ }

Grade = A | B | C

Evidence = Check | Measurement | Proof | Judgment of Text | Missing

Witness = Substitution | Measurement | Proof

Residual = (TermPath * Pattern * Pattern) list

Verdict = Confirmed of Witness
        | Refuted of Residual
        | Undecided of Reason
```

`Claim` is an open row because different surfaces add different fields. A cache
surface adds `evictionPolicy`; an API surface adds `versioningScheme`. Row
polymorphism lets the generic words (`claims`, `brief`, `ratify`) work over the
tail `ρ` while the domain sees its own fields.

Three variable namespaces meet here and must not be confused:

+ a *row variable* `ρ` extends the claim record;
+ a *term variable* `?x` is a hole in the statement;
+ an *effect set variable* is the open remainder of a word's effect set.

Rows are for data; sets are for effects. They have separate unifiers and no
cross-unification. A design that unifies a row variable with an effect set
variable is wrong, even if the error message looks plausible.

The statement is the shared `PatternLogic.Pattern` term model, extended by the
lambda term D59 added. `Formula` is the existing SMT encoding, not a second
logic. The ledger does not introduce a third term representation. If the pattern
model and the term IR are not yet the same type, the implementation must
reconcile them; it must not grow a ledger-specific term type.

== Verification is unification

A statement is an open term. Evidence is a closed one.

```text
statement: ( ?metric dominates ?cost :EvictionCost )
evidence:  ( p99 dominates ttlFanout :EvictionCost )
```

`unify` returns one of three things:

+ a substitution, which is the witness: `{ ?metric := p99, ?cost := ttlFanout }`;
+ a residual, which is the first disagreeing subterm with both sides, not a
  narrated explanation;
+ `Undecided` with a named reason when the terms leave the Miller-pattern
  fragment (D59).

A witness is a substitution for a term check, a measurement reference for a
measured claim, or a proof receipt for an entailment. The substitution is the
primary case; the other two use the same verdict shape.

The residual is the important repair. In the original design, the failure
explanation was written by the agent that had just failed. Here it is computed
by the unifier, so "why it failed" is evidence rather than narrative.

The occurs-check is the anti-dogma mechanism at the term level. A claim cannot
justify itself through a cyclic binding. The same property must hold one level
up, over claim support, and that is obligation O2.

A claim pattern may abstract over its surface. Higher-order matching binds the
flex variable to a lambda over the spine, so a claim about "any surface whose
oracle is a benchmark" is found from the concrete surface without a separate
similarity index. Retrieval is exact-anchor intersection plus unification. There
are no embeddings, no cosine scores, and no keyword ranking.

== The vocabulary

The spelling is tentative; a naming decision belongs in a decision record. The
stack effects and the effect sets are the contract.

```text
propose   ( Claim -- ClaimId )                  {image, ledger.propose}
contest   ( ClaimId Residual -- ClaimId )       {image, ledger.propose}
check     ( ClaimId -- Evidence )               {exec, smt, image, ledger.evidence}
ratify    ( ClaimId Evidence -- Verdict )       {image, ledger.confirm}
claims    ( Context -- ClaimSet )               {image}
brief     ( Surface -- Brief )                  {image}
inject    ( Context -- Brief )                  {image}
sweep     ( Surface -- StaleSet )               {image}
mechanize ( ClaimId -- Result ClaimId Reason )  {exec, image, ledger.evidence}
promote   ( ClaimId -- Result ClaimId Reason )  {smt, image, ledger.confirm}
dig       ( Surface -- ClaimSet )               {net, exec, image, ledger.propose}
```

`ratify` is the only general word that appends the verdict it is given.
`promote` also carries `ledger.confirm` because promotion computes and appends
a verdict; the Gate is the only role that holds that capability. `check`
appends evidence but cannot change a status. `inject` and `brief` have no
`net`, `exec`, or `model`; that is the type-level statement that delivery
cannot smuggle in an unverified claim or reach outside the ledger.

There is no single `settle` word. A `settle` with both `check` and `ratify` in
its effect set would give the checker the power to confirm, or the confirmer the
power to run arbitrary checks. Splitting the word is the boundary. The Gate
composes the two: it accepts `Evidence` from the Verifier and calls `ratify`.
No role holds both `exec`/`smt` and `ledger.confirm`.

`check` dispatches on `(Grade, Evidence)` through an image-resident generic
function (D19). The method set is image data, not a host `match`:

#table(
  columns: (auto, auto, 1fr, 1fr),
  align: (left, left, left, left),
  table.header([Grade], [Evidence], [Method], [Verdict]),
  [A], [Check], [Run the quotation; `Confirmed` or `Refuted`],
  [`Confirmed(subst)` / `Refuted(residual)`],
  [B], [Measurement], [Re-measure, or mark `Stale` by anchor hash],
  [`Confirmed(measurement)` / `Undecided(Stale)`],
  [C], [Judgment], [No machine check], [`Undecided(JudicialKnowledge)`],
  [A/B/C], [Missing], [No evidence attached], [`Undecided(NoEvidence)`],
)

If more than one method matches, the verdict is `Undecided(AmbiguousMethod)`,
never "whichever method ran first". Ambiguity is a named reason inside the
three-way verdict algebra, not a fourth verdict.

`ratify` appends the verdict it was given. An `Undecided` verdict is recorded
as a coverage gap; it never becomes a pass. A `Refuted` verdict is kept and
counted as learning, not erased.

== Z3 obligations

Six obligations are discharged through the existing `Smt.discharge`, with the
same honest outcome algebra and byte-pinned `.smt2` artifacts. Each obligation
is over the *encoded* fragment; a claim without a `formal` projection does not
enter O1 or O6. A Grade A claim has an executable check; it enters the SMT
theory only when it also has a `formal` projection. Execution and entailment are
different evidence.

#table(
  columns: (auto, 1fr, 1fr, 1fr),
  align: (left, left, left, left),
  table.header([No.], [Obligation], [Encoding], [Verdict]),
  [O1], [Consistency], [`SAT(∧ confirmed formal claims)`],
  [`Witnessed(model)` / `Falsified(unsat core)`],
  [O2], [Justification acyclicity], [Transitive closure of `because`],
  [`Proved` / `Falsified(cycle)`],
  [O3], [Anchor coverage], [Finite set cover over a surface],
  [`Proved` / `Falsified(orphan)`],
  [O4], [Surface disjointness], [Pairwise anchor intersection],
  [`Proved` / `Falsified(shared path)`],
  [O5], [Selector totality and budget], [Decision table plus
  `count(selected) ≤ 3 ∧ tokens ≤ 360`],
  [`Proved` / `Falsified(gap/overlap/overbudget)`],
  [O6], [Promotion entailment], [`A ∧ B ∧ ¬C` is unsatisfiable],
  [`Proved` / `Falsified(model)` / `Unknown` / `Unavailable`],
)

An obligation verdict maps to a claim verdict: `Proved` or `Witnessed` becomes
`Confirmed`; `Falsified` becomes `Refuted`; `Unknown` and `Unavailable` become
`Undecided` with the solver's reason. The obligation algebra is the project's,
not a new one.

#strong[O1 is satisfiability, not truth.] A model of `∧ confirmed` is a witness
that the claims can hold together; it is not a proof that they are true. A set
of false claims can be consistent. The obligation catches contradiction, and
the unsat core names the contradictory set by claim id. The SAT answer is
`Witnessed(model)`, not `Proved`; `Proved` remains the unsat answer for the
other obligations. This is `Discourse.contradicts` (D77) promoted from
syntactic to semantic.

#strong[O2 is a graph property.] The support graph over claim ids must be a DAG.
Term-level occurs-check does not cover claim-to-claim support. The total cycle
test is cheap; when a certificate is wanted, the same relation is encoded as a
transitive closure and discharged through `Smt.discharge`. A cycle witness is
the path, not a bool.

#strong[O3 covers what is enumerated.] The surface's anchors are enumerated at
the host boundary, so the obligation is finite. Every anchor is claimed or
explicitly `unclaimed` with a reason. `Proved` means no orphan in the
enumeration; it does not mean the surface was enumerated correctly.

#strong[O4 is opt-in.] Not every surface is a partition. Where it is, the anchor
sets are pairwise disjoint. Where it is not, the obligation is not asserted and
the overlap is data.

#strong[O5 protects the attention budget.] The injection selector is a decision
table (D13, D73): prove completeness (every context yields `Selected`,
`NoMatch`, or `Ambiguous`), disjointness (no two rows fire), and the bound
`count(selected) ≤ 3 ∧ tokens ≤ 360`. The bound is structural: at most three claims,
at most 120 tokens each. A claim that cannot fit is truncated by the selector,
not silently allowed to exceed the budget.

#strong[O6 makes promotion mechanical.] If the formal fragment of a Grade C
claim is entailed by confirmed A/B claims, promote it with a proof receipt:
`A ∧ B ∧ ¬C` is unsatisfiable. This is the same shape as the refiner's closure,
`pre ∧ guard ∧ ¬post`. Promotion is per fragment: the formal fragment becomes a
Grade A claim with `Evidence = Proof`; the natural-language residue, if it is
recorded, remains a Grade C claim linked to it.

What Z3 is *not* asked to prove: that the claims are true, that the anchors are
the right anchors, that the formal projection matches the English reading, that
the selector is relevant, or that the Gate is honest. Those are review
obligations, and pretending otherwise would be the exact overclaim the project
forbids.

#pagebreak()

== Actors and the trust boundary

The deployed system has one external agent and three Scale-side roles. For the
agent, the MCP tool list is its effect set; for the Scale actors, the effect set
is the type. The two must agree.

#table(
  columns: (auto, auto, 1fr, 1fr),
  align: (left, left, left, left),
  table.header([Role], [Where], [Effect set], [Power]),
  [Extractor], [the agent, outside Scale],
  [`{model, ledger.propose, image}`],
  [The only model-touching role. It may append `claim:` and `contest:` blocks.
  It cannot append a verdict.],
  [Verifier], [Scale actor], [`{exec, smt, ledger.evidence, image}`],
  [Runs checks and measurements; appends evidence. Cannot confirm.],
  [Gate], [Scale actor], [`{ledger.confirm, image}`],
  [The only role that appends `verdict: confirmed`. It does not run checks.],
  [Watcher], [Scale actor], [`{net, image}`],
  [Reads session events and writes a thread checkpoint to the session store.
  Cannot touch claims.],
)

The structural answer to the self-serving-claims critique is here: the Extractor
literally lacks the capability to ratify its own claim. It writes proposals; the
Verifier writes evidence; the Gate writes the verdict. A journal written by the
agent that made the mistake is still a rationalization log unless the Gate is
outside the agent and the capability is enforced.

The immediate honest qualification: the effect system can carry arbitrary
labels, but the builtin registry contains only closed-effect entries and there
is no user-facing effect annotation syntax yet. Labels express *that* a word
requires an effect, not *who may grant it*. If the host cannot enforce
per-role closed sets, the boundary degrades to a single-writer actor plus a
runtime check. That is weaker than a type-level guarantee. The first
implementation slice must prove which one is real.

The MCP surface is the second fence. The agent sees `propose`, `contest`,
`check`, `claims`, `brief`, and `inject`; it does not see `ratify` or `promote`.
The Gate is an internal actor, not an agent-callable tool. If the agent needs a
verdict, it asks for `check`; the Gate ratifies the resulting evidence and the
next `claims` call sees it.

The `image` label is broad storage access, not a claim capability. The
Extractor's `image` is for reading through `claims`; the host must not expose a
raw image write to it. The MCP surface is the second fence, and it is not
optional.

== Compaction-immune delivery

The conversation is allowed to be summarized. The ledger is not. The brief is a
projection of the ledger, recomputed at each delivery moment:

```text
Brief = {
  thread:   last StepTag chain (session store, not the ledger),
  surfaces: (open paths ∩ claim anchors) sorted by grade,
  claims:   take 3, ≤ 120 tokens each,
  ratio:    A-ratio per surface,
  gaps:     Unknown and Unavailable obligations,
}
```

There are three delivery moments:

+ #strong[Open.] When a task starts, inject the brief for the surface. The
  agent begins from the ledger, not from a summary of the previous attempt.
+ #strong[Decide.] Before an irreversible or cross-cutting choice, inject the
  top claims whose anchors intersect the decision context. The substitution is
  the relevance witness; there is no similarity score.
+ #strong[Repair.] After a failed check or a contest, inject the refuted claim
  and its residual. The next attempt starts from the computed failure, not from
  the agent's account of it.

The brief is delivered two ways, in order of reliability:

+ #strong[Pull.] The agent calls the `brief` MCP tool at the start of a task and
  at the three moments. This is the guarantee. It works without any OpenCode
  API assumption.
+ #strong[Push.] The Watcher writes the brief into an instruction entry through
  the OpenCode HTTP API. Instruction entries are not messages, so the compactor
  summarizes the transcript without summarizing the entry. This is the
  optimization, not the guarantee.

There is no synchronous pre-compaction hook and no JavaScript plugin in this
design. An external HTTP client cannot reliably intercept the pre-compaction
boundary.
Continuous checkpointing replaces the hook: the Watcher appends a thread
checkpoint to the session store every *N* settled events, not at a boundary it
cannot observe. The existing Vestige brief plugin is a separate, narrower
mechanism; this design does not extend it and does not add another plugin.

The push path depends on one unverified property: that instruction entries
survive compaction in the installed OpenCode version. Verify it before relying
on it. If it is false, the pull path still works, because the ledger is the
state and the brief is recomputed.

== What is deleted

The original concept was a fleet of self-driven experts. The critique that
killed it was not "the idea is too small"; it was "the architecture is
generation-side for a verification-bound problem". The following table keeps
the useful half.

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Idea], [Fate]),
  [Fleet, leases, ladders, 24/7 cadence], [Deleted. Coordination complexity
  buys nothing the ledger does not buy.],
  [The model's self-report as evidence], [Deleted. Evidence is a check, a
  measurement, or a proof; text is Grade C.],
  [The summary as state], [Deleted. The summary is a lossy view; the ledger is
  the state.],
  [Autonomy as the goal], [Deleted. The goal is a settled claim at the moment
  of decision.],
  [Embeddings and similarity search], [Deleted. Retrieval is exact anchors and
  unification.],
  [The claim ledger], [Survives, as a `.claims` document.],
  [Grade A/B/C and the A-ratio], [Survives, as the trust map of a surface.],
  [Delivery at decision moments], [Survives, as the brief and the three
  moments.],
  [Extraction instead of autonomy], [Survives; the effect boundary is the
  intended enforcement.],
  [The legibility budget], [Survives, as the O5 selector bound.],
  [The oracle sets the cadence], [Survives, as the grading rule: Grade A is an
  oracle, and the A-ratio is the map of what can be trusted.],
)

== Honest limits

+ #strong[Consistency is not truth.] O1 catches contradiction, not falsehood. A
  consistent ledger can be consistently wrong.
+ #strong[Only the encoded fragment is proved.] A natural-language residue stays
  Grade C. `Unknown` and `Unavailable` are coverage gaps, never passes.
+ #strong[The formal projection is the weakest link.] A wrong projection makes a
  wrong claim look proved. The projection is reviewed like a specification, not
  trusted because Z3 accepted it.
+ #strong[Anchors go stale.] A hash detects drift at an anchor; a semantic
  change at the same path is invisible until a check reruns. `sweep` reports,
  it does not understand.
+ #strong[The A-ratio is a map, not a score.] If it becomes a target, the
  cheapest move is to relabel claims. Grade changes require evidence, and a
  refuted claim is counted as learning, not hidden.
+ #strong[The Gate is a policy, not a proof.] If the Gate's implementation is
  wrong, the boundary fails. The effect set is only as good as the host
  enforcement.
+ #strong[The selector bound is a budget, not relevance.] O5 prevents overload;
  it does not prove the selected claims are the right ones.
+ #strong[The agent can still lie.] It can propose a false claim. The ledger
  catches it only if the claim is checkable or contradicted. Grade C claims are
  explicitly untrusted.
+ #strong[The ledger can be gamed.] Weak evidence can inflate the A-ratio. The
  defense is the evidence taxonomy, the audit of grade changes, and the
  refutation count, not a proof.
+ #strong[The strongest critique still applies if the Gate is a prompt
  convention.] The structural answer only holds when the Gate is outside the
  agent and the capability is enforced. If the Gate is "the agent promises not
  to ratify itself", the original critique stands unchanged.

== First slices

+ #strong[Slice 0: the document.] The `.claims` grammar, parser, canonical
  emitter, escaping, and a round-trip property. No word, no actor. The
  acceptance test is byte-identical re-emission and a refused directive
  injection.
+ #strong[Slice 1: the claim.] The open record, `propose`, `contest`, `claims`,
  the file lock, and the MCP server skeleton. The acceptance test is a claim
  that survives a process restart and a verdict that cannot be appended by the
  proposal path.
+ #strong[Slice 2: the check.] `check`, the Verifier, `ratify`, the Gate, O1,
  and O2. The acceptance test is a contradiction found by the unsat core and a
  support cycle refused with its path.
+ #strong[Slice 3: the brief.] The selector decision table, O5, the pull path,
  and the Watcher's thread checkpoint. The acceptance test is the budget bound
  and a brief that is identical before and after a forced compaction.
+ #strong[Slice 4: the ratchet.] `sweep`, `mechanize`, O3, O4, O6, and the first
  twenty claims by hand. The acceptance test is a stale anchor reported, an
  orphan reported, and one Grade C claim promoted by entailment.
+ #strong[Kill criterion.] If no claim is cited at a decision point in a month,
  the organization does not write down what it learns. Stop the project; do not
  add another surface.

Every slice follows the repository order: documentation, tests, implementation.
A slice is incomplete if its tests were written from the implementation, or if
the specification and the document disagree.

== Decisions needed

None of these is taken here. At the time of writing the register ends at D199;
the next free record is D200.

+ #strong[Document kind.] `.claims` as a sibling document, or an extension of
  `.scale`. Recommendation: sibling. The Prose grammar stays closed, and the
  line discipline is shared without sharing the grammar.
+ #strong[Surface spelling.] The words in *The vocabulary* are operands-then-word,
  with effect sets in braces. Recommendation: keep that shape and make the
  naming a decision record, not a comment in the implementation.
+ #strong[Server location.] A new `bases/scale-mcp`, or a mode of
  `bases/scale-cli`. Recommendation: a new base. A CLI is not a long-running
  server, and the MCP boundary is the security boundary.
+ #strong[Capability enforcement.] Effect labels plus host checks, or opaque
  resource tokens for `ledger.confirm`. Recommendation: tokens for the confirm
  capability. Labels express requirements; tokens express unforgeable
  authority.
+ #strong[Delivery.] Pull-first (`brief`), push-second (instruction entry).
  Recommendation: pull-first. It does not depend on an OpenCode behavior that
  must be verified, and it still works after compaction.

Until the user confirms, this document remains a proposal and an index row. No
decision record is appended, and no source is written.
