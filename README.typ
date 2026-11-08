= Scale
#strong[Property testing that grows the specification.]

Scale is a domain-specific language for design-by-contract property tests.
Instead of writing a complete specification up front, the specification grows
interactively: a property is run, Hedgehog returns a shrunk counterexample, and
the refiner proposes a candidate refinement. The `.scale` document remains the
single editable source of truth. The refined property is re-emitted, and the
SMT obligation is checked by Z3.

This repository is a research prototype for #strong[Scale];. #strong[Lia] is the
mascot, not the product: Scale is the product name, and prose that needs the
product's name uses Scale. Lia appears in this index and in historical notes.

Canonical documentation is Typst under `docs/`; frozen rendered PDFs live under
`docs/pdf/`. The short repository entry point is `README.md`.

Under the specification language sits one idea, stated three ways:

#table(
  columns: (auto, 3fr),
  align: (left, left),
    [#strong[One thing]], [a #emph[tensor] is the only semantic type; even a scalar is a rank-0
tensor],
    [#strong[One structure]], [the #emph[image] --- a single append-only heap of tensors --- holds all
data and all definitions],
    [#strong[One protocol]], [#emph[references] (`EntryIdentity`) resolved through the image, with failures
carried as values (Railway), never as exceptions],
)
Around that core is a verification-oriented compiler from controlled natural
language to F\# and SMT (Z3), with a Forth-flavored runtime at its heart and a
child-facing vocabulary at its surface --- you say #emph[\"Every player that is alive
can jump.\"];, and out comes a property you can test and a modelled obligation
Z3 can discharge. Testing checks points; the obligation checks the space the
model encodes.

Scale is explicit about the boundary: Hedgehog finds counterexamples; Z3
proves or falsifies the #emph[encoded] obligation; `Unknown` and `Unavailable` are
reported, never treated as passes. The prototype does not prove the
implementation or the completeness of the specification; a `Proved` SMT
obligation is the only proof boundary.

== Architecture in brief
At the bottom is one data structure: the #strong[image];, an append-only heap of
tensors. Every value, text, vocabulary entry, model, sort, channel, and history
step is an image entry addressed by an `EntryIdentity`; nothing is ever rewritten or
freed. Host code holds only execution state --- the stack, the program counter,
the root pointers, the scheduler, and the ownership map --- and persistence
serializes the image alone, so a saved world carries no run.

Above that sits a Forth-flavored #strong[evaluator];: a stack machine whose words are
dispatched through built-ins, image-resident word metaobjects, generic
functions, user words, and the vocabulary. Failures are values (`Result` with a
named reason), never exceptions. A cooperative #strong[scheduler] runs actors over
the shared image, with channels and ChannelEvents-style events whose state is also
image-resident; a #strong[history] records a step before every executed token so a
run can roll back, and #strong[linear ownership] checks (own/borrow/consume/release)
are per-run execution state, never serialized.

The #strong[surface] is the Prose sentence grammar: a closed sentence grammar over an
image-resident vocabulary. The #strong[compiler] is one emitter with a domain binding
supplied as data: it renders a framework-free F\# predicate and an SMT obligation
from the same parsed document, so the two artifacts cannot state different
preconditions. For existential `some`, the artifact is a witness predicate and
the obligation is satisfiability (D33). The #strong[refiner] turns a shrunk counterexample into a candidate
guard, accepts it only when `pre ∧ guard ∧ ¬post` is proved unsatisfiable, and
appends it to the `.scale` document; the fixed-spec gate reports a witness as a
versioned repair bundle instead of editing the specification.

The extension points are #strong[metaobjects];: words, sorts, and generic functions
are image data, so a new method needs no F\#, and a predicate sort carries the
quotation that decides membership, answering `Member`, `NotMember`, or
`Undecided` rather than a silent false. Classification is a verdict too:
`class-of` answers Class, Unknown, Ambiguous, or Undecided, tagged metaobjects
report their entry kind, and dispatch uses the same membership verdict. The
trust boundary is the same
everywhere: Hedgehog samples, Z3 decides the encoded theory, SMT models are
differentially checked against the compiled predicate, and `Unknown`,
`Unavailable`, inconclusive runs, and undecided membership are reported as such.

== The working parts
- #strong[Hedgehog] supplies the testing side. Generators are derived from the same
  domain lattice the verifier uses; a failure is shrunk to a minimal
  counterexample with a recheck seed.
- #strong[Z3] supplies the proof side for the encoded theory. `Smt.discharge`
  returns `Proved`, `Falsified (model)`, `Unknown`, or `Unavailable`; only
  `Proved` closes a guarded obligation.
- #strong[Scale] is the bridge. It is a small, readable specification language that
  compiles to a framework-free F\# property and an SMT obligation from one
  parsed document and one domain binding.
- #strong[The refiner] turns a falsified diagnostic into a candidate guard, consults
  the proved closure oracle for `pre ∧ guard ∧ ¬post`, and returns the guard;
  the driver writes it into the `.scale` document.
- #strong[The `.scale` document] is line-oriented: one `specification:` line, then
  one `refinement:` block per refinement, each with an `origin:` and a
  `counterexample:` line. Free text is escaped (`\\`, `\n`, `\r`), so a field
  can never be read back as another directive and the document round-trips
  exactly (D27).

== The pipeline
+ A developer writes the initial specification in `.scale`.
+ The compiler emits a Hedgehog property and an SMT obligation from the same
  parsed document.
+ Hedgehog searches for a counterexample and shrinks it to a minimal
  reproducer.
+ Z3 discharges the initial obligation and returns `Proved`,
  `Falsified (model)`, `Unknown`, or `Unavailable` honestly.
+ The refiner consumes the counterexample and the solver result, selects a
  candidate guard whose closure is proved, and the driver appends it to the
  document.
+ The refined `.scale` document is recompiled, so the guard is active in both
  the property and the obligation.
+ When the refined obligation is `Proved`, that bug class is closed within
  the encoded theory. This is not a claim that inputs outside the theory are
  covered.

== What the loop claims
The guarantee is narrower than \"the implementation is correct\":

- The same parsed document drives the F\# property and the SMT obligation, so
  the two artifacts cannot state different preconditions.
- A refinement is accepted only when Z3 proves the guarded closure; a
  counterexample, `Unknown`, or `Unavailable` never counts as a pass.
- The `.scale` document remains the single editable specification; the lattice
  is a projection of it, never a second source of truth.
- Hedgehog can discover missing preconditions, but a green run is evidence,
  not proof that the specification is complete.

The remaining open question, shared by specification-driven systems, is whether
the specification itself is complete. Scale addresses that incrementally
through falsification, not by claiming certainty.

== Community and feedback
This is an experimental approach at the intersection of property-based testing
and SMT-backed verification. We welcome feedback, questions, and contributions
--- especially from the F\# and property-based testing communities. Feel free to
open an issue or start a discussion.

== How it proves it means it

Claims here are not vibes; they are checked, and checkable, from the outside:

- #strong[Examples pin the known] --- the example suite (`tests.fs`) locks in
  behavior the way a photograph locks in a moment, including the Scale Basic
  English sentence grammar and its named refusals, the refinement loop that
  grows a specification when it is falsified, the structural/nominal sort
  surface, the unification chain/occurs-check/typed-term cases, and the
  dispatch surface (inferred and typed exact, subtype specificity, ambiguity,
  and refusal).
- #strong[Properties hunt the unknown] --- 32 Hedgehog properties (`properties.fs`)
  state the architecture itself as falsifiable law: the image never rewrites
  its past, interning is injective, evaluation is deterministic, every
  program returns a value or a diagnostic (never a crash), `nor` is set
  complement over its universe, `and`/`or` agree with ∩/∪, the structural set
  words agree with ∪/∩/, unification resolves a random variable chain and
  refuses cyclic terms, dispatch selects exactly the declared sort members,
  retired words cannot be resurrected by the surface,
  the emitter is a pure function. Each run generates fresh random inputs --- so
  a green run is evidence, not proof, and a failed one prints a recheck seed
  that reproduces the exact counterexample (decision D6).
- #strong[Every check is counted individually] --- the run reports every example,
  property, snapshot, and integration result, and the summary counts them together (1768 checks), so a regression names how many things broke, not just
  that something did.
- #strong[One gate, every kind] --- `just check` runs 1683 examples, 58 properties, 13 snapshots, and 14 integrations (1768 checks), so the summary is by the nature
  of the check rather than by runner. The Verify snapshots cover the emitter
  artifacts (boolean, numeric, transition, possibility, and no-condition), the
  repair bundle (boolean and numeric), the escaped `.scale` document
  (boolean and numeric), and the prose function and construction lowerings, all under `snapshots/`.
  The snapshot path is JIT-only; the suite is a separate `projects/scale-tests`
  deployable and the NativeAOT artifact is the product (D23, D176).
- #strong[A failing suite fails the process] --- exit code `1` on any failure, so
  continuous integration (or a colleague, or you in six months) can trust the
  machine\'s word.
- #strong[Dependencies are pinned] (Hedgehog 1.0.0, Microsoft.Z3 4.12.2), and the
  emitted golden file (`generated/sample-properties.fs`) is compiled into the
  project and invoked by a test --- the pipeline\'s output is never just asserted,
  it is #emph[run];.
- #strong[The boundaries refuse, they do not approximate] (D27) --- an inconclusive
  property run is reported as inconclusive rather than as a pass; a
  `.scale` document cannot inject a directive line; a refinement threshold is
  emitted exactly or refused; a channel\'s queued messages live in the image and
  survive persistence; a corrupt image file, an ill-sorted solver assignment,
  and an extreme term arity are named refusals rather than surprises. Each one
  is pinned by a regression test, and the property suite now generates the
  whole word surface, not only the arithmetic core.
== Level model, in one breath
Object semantics (Scale) are tensors and only tensors: a reference is a
rank-0 tensor used as an address, a sort is a tensor denoting a type.
Host encoding (F\#) --- `EntryIdentity`, the error channel, edge strings --- is
implementation, not meaning: each core host type must project into tensors
totally and injectively, and hold no state absent from the image. See the
banner of `types.fs`.

== What is a set?
A #emph[set] is a collection of values. In Scale a value is a tensor, so a set is
a collection of tensor values, and its members are image entries (`EntryIdentity`s).
Membership is structural: two tensors with the same data and shape are the same
member, even at different addresses. `[ 1 2 ]` builds an anonymous set; `1` is a
member and `3` is not.

A #emph[named] set is a #emph[sort]. It adds a seal (a text name) to an extension
(the member identities); the code type is `EvaluatorValueSets.ValueSet`. A sort is not
an ordering. It is the type-theory term for a named value set. A sort's declared
domain is the universe `complement` uses; it only grows when `admit`, `seal`, or
`constructor-seal` adds members. A predicate or constructor sort narrows its
extension with an image-resident quotation; membership answers `Member`,
`NotMember`, or `Undecided`, never a silent false.

Sets are image data: adding a member or deriving a set appends an entry and never
rewrites the old one. The set tables are execution-state pointers (`SetsRoot` for
subsets, `DeclarationsRoot` for declared domains), so a reader re-threads them
after persistence and the saved image carries the sets themselves.

== Glossary
Every public module, type, and surface word, with a link to its definition and
its main uses. Private helpers are documented in their module. Entries link to
one another, so a description can be read as a path through the system.

The naming philosophy and the rename tool live in
`tools/rename-names.fsx`; it records the earlier renames and applies the current
mapping.

#include "docs/start/glossary.typ"

== Documentation

The Typst sources under `docs/` are the central documentation. The Markdown
copies are retired.

- `docs/start/introduction.typ` --- a guided whole-language tour for experienced
  programmers new to concatenative programming, images, or set theory.
- `docs/process/commitments-ledger.typ` --- every design commitment, with status
  (Met / Partial / Deferred / Absent) and dependencies.
- `docs/process/technical-debt-register.typ` --- host-encoding debt (closed; waves re-scoped).
- `docs/process/decision-index.typ` --- every decision by number; `docs/process/decision-records.typ` is the full history (E1 and D1--D195).
- `docs/types/type-lattice.typ` --- the verification type lattice (D25): the one
  coordinate system Z3, Hedgehog, and the refiner share, and the planned dual
  property-synthesis program.
- `docs/language/action/prose-grammar.typ` --- the controlled sentence grammar: surface
  forms, canonical tokens, logical form, named refusals, and the staged
  executable subset (D18), including the event-triggered transition shape (D55).
- `docs/process/decision-models.typ` --- decision tables as a specification refinement
  (D13, promoted as D73 and D74): Z3-proved completeness and disjointness, pure
  selection, per-row emission, the Prose sentence table surface, the diagnostic
  sink, and the boundary that keeps ML decision models out of the verified path.
- `docs/rejected-ideas/rejected-designs.typ` --- rejected alternatives, removed code, and the
  conditions for reintroducing it.
- `docs/architecture/dspy.typ` --- DSPy-shaped model-backed programs as an optional, prepared
  feature set (D42): the signature as a projection of the `.scale` contract, one
  model-touching word at the edge, fast search versus the existing proof gate,
  and the tensions (nondeterminism, transcripts, graded decoding). Not
  implemented.
- `docs/runtime/cell-system.typ` --- the full cell system (D43--D50): classes,
  slots, instances, class precedence, nominal dispatch, method combination,
  closures, and metaclasses. The encoding, closures, classes/slots/instances,
  and precedence/dispatch, method combination, metaclasses, and conditions/restarts
  landed as D44--D50. The surface stays the Prose sentence grammar; tensor entries are internal
  encodings.
- `docs/types/set-theoretic-typing.typ` --- how the type system narrows to nominal and
  widens to structural (and beyond), the landed surface that makes the reading
  local (D12, D14).
- `docs/kernel/kernel-inventory.typ` --- the Metaobject Protocol baseline: every hardcoded
  word case and host mechanism, the bootstrap kernel, and the staged acceptance
  tests (D16, D17).
- `docs/architecture/general-ir.typ` --- the portable general IR: expressions, patterns,
  packages, kinds, predicates, sorts, conditions, dispatch, and actor messages,
  with the adapter contract, versioning, and portability boundaries (D97).
- `docs/process/roadmap.typ` --- the current pointers and read budget; `docs/archive/roadmap-archive.typ` is the completed history.
- `docs/process/sagefs.typ` --- the SageFs REPL-first inner loop, OpenCode MCP wiring,
  and the commands used in this repo.
- `AGENTS.md` (repository root) --- the single home for instructions meant for
  AI agents: the MCP gate, the tool matrix, the inner and outer loops, the
  `just` recipes, change discipline, and troubleshooting.
- `docs/kernel/nativeaot.typ` --- the NativeAOT contract: the publish commands, the
  release binaries, and the rules that keep the code AOT-safe.
- `docs/kernel/native-dotnet-port.typ` --- the plan to compile Scale to .NET
  assemblies: the small lowered intermediate representation, the late-bound
  seams, dispatch as the case mechanism without a `match`, and the phases
  before any assembly emitter. Plan only; not implemented.
- `docs/process/dotnet-mcp.typ` --- the .NET MCP server (structured CLI, the final gate),
  its tools, and the three places its README disagrees with its own code.
- `docs/process/vestige.typ` --- the Vestige MCP memory server: exact-handle retrieval,
  why semantic search cannot be enabled in 6.0.0, the `just mcp-check` coupling,
  and the operating rules for saving and recalling.
- `docs/runtime/evaluator-slicing-and-scheduler.typ` --- the proposed split of
  `evaluator.fs` into vertical slices, and the evaluation of `scheduler.fs`.
- `docs/runtime/memory-model.typ` --- the memory vocabulary and target model: image,
  actor epoch, struct handle, byref-like view, promotion, and the Elixir/BEAM
  scope. Stage 0; no runtime change.
- `docs/runtime/memory-and-go-transpiler.typ` --- a potential memory tier (linear epochs
  with affine references, and a separate finalizer token for side effects) and
  what it could enable for a restricted Go transpiler. The Go path is delayed;
  the Elixir-focused model is in `docs/runtime/memory-model.typ`. Analysis only.
- `demo/` --- the refinement loop in practice: a specification that grows when
  it is falsified (see `demo/README.typ` and decision record D7).
- `docs/architecture/repair-model-analysis.typ` --- a root-cause analysis of the three
meanings of “repair” (sentence advice, specification refinement, and
implementation acceptance), the `Patch.SpecEdit` invariant, and the typed
proposal/capability alternative. Analysis only; no implementation.

- `docs/rejected-ideas/beam-compatibility.typ` --- the BEAM/OTP feature matrix: local actors,
  mailboxes, links, monitors, supervision, and system messages; the missing
  distribution, remote code sharing, preemption, per-process GC, and release
  model. Analysis of current limits, not a scope promise.
- `docs/kernel/actor-ipc.typ` --- the implementation-instance boundary: separate
  implementations in their host languages, connected by the actor model's IPC;
  the message envelope, portability, failure, staging, and open decisions.
- `docs/kernel/binary-format.typ` --- the Scale actor wire protocol: an in-repo,
  Cap'n Proto-compatible subset, the C++/Rust/Go references, the Protobuf-style
  schema tooling, the staging, and the conformance plan.
- `docs/architecture/term-ir.typ` --- the typed term list: `Program: Term list`, quotations
  as code values, the kernel verifier and receipt, the Prelude elaboration and
  effects, macros, and the migration stages (D160, planned).
- `docs/rejected-ideas/unified-language.typ` --- the dropped unified Julia/Elixir
  proposal, kept as the record of the rejected direction (D151).
== Build & test

#strong[Alpha phase.] Do not run MSBuild or Fantomas during alpha, and do not publish NativeAOT. SageFs is the compiler, the runner, and the test harness; `just check`, `just fmt`, and the AOT recipes are release-time steps, performed only when the owner calls a major public release. See `AGENTS.md`.

```
just check                         # format check, build, every kind of test
just doctor                        # toolchain, project, SageFs health
dotnet build
dotnet run      # the product showcase
dotnet run --project projects/scale-tests/scale-tests.fsproj -- --all-tests   # every kind once
dotnet run --project projects/scale-tests/scale-tests.fsproj -- --tests       # the example suite
dotnet run --project projects/scale-tests/scale-tests.fsproj -- --granular-tests   # JIT granular suite
dotnet run -- --fixed-spec   # fixed-spec gate: writes the bundle, fails when falsified
just test-all                     # the same unfiltered aggregate, via just
just test-granular                # the granular suite, via just (JIT)
just test-count-check             # run both suites and verify the documented counts
just release                      # release only: format, full suite, NativeAOT, count refresh
just publish linux-x64            # NativeAOT product binary in artifacts/native/linux-x64
just aot-test linux-x64           # publish, then smoke the product
```

`dotnet` is on the normal PATH. `just` also sets the toolchain directories for
its recipes, so both work from any shell. The cold-start guide is the
repository-root `AGENTS.md`.

Every GitHub release publishes native product binaries for `linux-x64`,
`win-x64`, and `osx-x64`; the release job runs the JIT suite before it publishes
and smoke-tests the product binary before it attaches the archive (D176). The compatibility rules (notably the `printf` and Z3
constraints) are in `docs/kernel/nativeaot.typ`.

The binary output of the natural-language → F\# pipeline lives in `generated/`
--- compiled golden modules produced by the emitter, pinned by tests.

== SageFs REPL

SageFs, a warm F\# Interactive daemon with an MCP server, is the inner loop.
Prove a change with an eval, write it into the file, reload the session, and run
the full build and both unfiltered suites once at the end. It is not a
replacement for the gate. The agent rules for using SageFs live in the
repository-root `AGENTS.md`.

```
sagefs check                 # SDK, F# Interactive, project, ports
sagefs --ttl 2h              # start the daemon; leave it running
sagefs status                # process identity, port, sessions
```

The dashboard is `http://localhost:37750/dashboard`; MCP is
`http://localhost:37749/`. OpenCode is wired to it through
`mcp.servers.sagefs` in the global config, and the SageFs skill is installed in
`~/.config/opencode/skills/sagefs/`. See `docs/process/sagefs.typ` for endpoints and
exact config; the session loop and tool names are in `AGENTS.md`.


== .NET MCP (dotnet-mcp)

The `.NET MCP Server` (`Community.Mcp.DotNet`) is the structured CLI, wired
into OpenCode as `mcp.servers.dotnet` and run on demand through `dnx`:

```
opencode mcp list            # ✓ dotnet  connected
```

It exposes 12 consolidated `dotnet_*` tools (build, test, run, packages,
solutions, templates, SDK metadata, tools, workloads, Entity Framework Core,
certificates) plus
read-only resources (`dotnet://sdk-info`, `dotnet://workspace`, …). The skill
is installed in `~/.config/opencode/skills/dotnet-mcp/`.

The gate is `just check` (both suites). `AGENTS.md` carries the tool matrix and
explains why the .NET MCP `Test` action is not this repository's gate. See
`docs/process/dotnet-mcp.typ` for the tools and the corrections to its upstream README.

== Formatting

#strong[Alpha phase.] Formatting is deferred: do not run `just fmt`, `just fmt-check`, or `dotnet fantomas` until the owner calls a major public release.

```
dotnet tool restore
dotnet fantomas check .   # exits 0 when formatted
dotnet fantomas .         # format in place
```

Fantomas is the formatting authority, using its defaults: `x: T` annotations,
multi-line records whose fields are not column-aligned, and a 120-column
budget. `.fantomasignore` excludes `generated/` and `demo/generated/` because
those files are the emitter\'s byte-pinned output: format the emitter, not its
artifacts.

For when to run it, see the repository-root `AGENTS.md` section 6: formatting is
a rework boundary, not a reflex, and it is never mid-work.

No linter is configured, and that is deliberate. FSLint (B2R2) was evaluated
and removed: its return-type rule wants `(x: T): U` where Fantomas always
writes `(x: T) : U`, it has no suppression mechanism, and it flags the
lowercase JSON data-transfer-object fields in `repair.fs` that #emph[are] the serialized wire
format. The two tools cannot both reach zero, so the linter produced noise
rather than signal. Re-adding one means resolving that conflict first.

== Where it stands, honestly
#strong[Met and property-pinned:] the kernel --- image, stack machine, environments,
Vivid `nor`, persistence, Railway errors, vocabulary single-source, the
W1 emitter (compile-verified properties from a child-facing sentence), the
W2.1 Prose sentence grammar (an explicit sentence grammar with named
refusals), and the refinement
loop demonstrated in `demo/` (decision records D7, D8, D9, and D18) --- the loop runs
the emitter\'s #strong[compiled] output, not a mirror of it. W3 adds the sort
registry and its properties: a sort is `(option Name, Extension)`, and the
structural set words are pinned against ∪, ∩, and \\ elementwise.

#strong[Landed (W3, D14):] sorts are a pair, not a label. `[ … ]` builds an
anonymous set (evaluating its body; `[ ]` is empty), `S :Name seal` names one,
`unname` widens it back, and `:Name` references an existing seal. `subset`,
`union`, `intersect`, `except`, `member`, and `same-shape` read the extension;
`same-name` and `sort-of` read the name; `merge` is the seal-preserving union
and refuses a mismatch with `ValueSetMismatch`; `complement` defaults its universe
to a declared sort and refuses an anonymous set with `ValueSetNotClosed`. The
ternary `nor` is unchanged --- a binary default-universe form is ambiguous on a
stack, so D3\'s open line closes with a distinct unary word. Declarations are
image-resident, latest-write-wins, and grow-only. Comprehension syntax
(`[ ?x : ?x > 1 ]`) is #strong[not] landed.

#strong[Landed (W4, D15):] unification is a structural pillar, not a one-way
assignment. Variables are `?name` text entries, atoms are ordinary entries,
compounds are `-2` tagged vectors (`head arg… arity term`), and typed terms are
`-3` tagged wrappers (`term :Name typed`). `unify` walks substitutions, binds
bidirectionally with a canonical lower-`EntryIdentity` direction, rejects direct and
indirect cycles with an occurs-check, unifies compounds head-and-arguments, and
unifies typed terms only under the same sort #strong[name];. A failed match is
transactional, so no partial binding leaks. `lookup` resolves chains, `resolve`
applies the substitution, and the binding remains the image-resident
`BindingsRoot`. The neighbour research is in D15: Kitten\'s substitute-then-match and
occurs-check, Cat\'s deterministic representative, Factor\'s separation of
dispatch from unification, and Joy\'s warning that a concatenative core does not
need a logic engine.

#strong[Landed (W2.1, D18; transitions, D55; disjunction, D58):] the sentence
layer has an explicit grammar, not an order-sensitive guess. The Prose sentence grammar
is `every`/`no` + TypeName subject + optional `that` relative clause +
FunctionName verb + full stop. `every` is a `Capability`; `no` is an
`Invariant`. Conditions are boolean predicate phrases (`is alive` canonicalizes
to `isAlive`), joined by `and`, with `or` between conjunctions (`and` binds
tighter, D58); `has <field> <comparison> <integer>` is a typed numeric condition
that reaches the emitted predicate, the SMT obligation, and the repair gate
(D24). Articles, pronouns, `when` away from a transition's start, `not`/`never`,
and `all` are refused by name. `some` is a `Possibility` (D33): the emitter
renders a witness predicate and the SMT obligation is satisfiability, so it is
never silently read as `every`. A disjunction is one DNF atom, emitted as `||`
over `&&` clauses and encoded as `MkOr` over `MkAnd` (D58). A transition (D55)
is `when <subject> <condition>, every <subject> <verb>.`; its trigger is the
precondition and its verb the effect, so it emits `Transition` as a pinned
guarded effect. See `docs/language/action/prose-grammar.typ`.

#strong[Landed (W13--W15):] properties stop at sampling and become #emph[discharged] ---
the same parsed document drives an SMT encoding checked in-process by Z3, with
`Proved` / `Falsified (model)` / `Unknown` / `Unavailable` reported honestly
and validated differentially against the compiled predicate. The
implementation is one `DomainExpression` body with an interpreter for Hedgehog and an
encoder for Z3, and the `.smt2` obligation is byte-pinned. W14 moved the
refiner\'s guard closure onto the proved query: a candidate is accepted only if
`pre ∧ guard ∧ ¬post` is unsatisfiable and the guarded precondition is
satisfiable, and the chosen refinement records the solver version and the Z3
witness model. W15 makes the same verifier an #strong[acceptance oracle];:
`Repair.Mode` separates exploratory spec growth from a fixed specification; a
witness yields a versioned repair bundle (spec hash, shrunk witness, solver
model, encoded implementation, differential result, source location);
`witnesses/player.json` is the checked-in append-only corpus; and
`Repair.accept` accepts a patch only if the target is `Proved`, no previously
passing witness regresses, the spec hash is unchanged, and the encoder still
agrees with the compiled predicate. `Unknown`, `Unavailable`, and spec edits
are rejected, never accepted. The demo stays exploratory and reports its mode;
the fixed-spec path has its own tests. SMT discharge is the backstop.

#strong[Landed (W8, D29):] the event-stream is the program\'s history. Every executed
token appends an image-resident, append-only step (`StepTag = -10.0`) that
carries the program-counter metadata, the token, the stack before it ran, and
the program it resumes. The history root is a vector of step addresses;
`history` reports the steps before it, `rollback` restores a step, and rolling
back re-points the root at the steps before the target without rewriting the
image. Bookmarks are absorbed as degenerate one-point events: `bookmark`
records a step and names it, and `jump` restores through the same rollback.

#strong[Landed (W9, D30):] ownership is per-run execution state. `own`, `borrow`,
`consume`, and `release` check a value\'s owner and consumed state, with
`NotOwned`, `OwnedByAnother`, and `ValueConsumed` as named refusals. A spawned
actor owns nothing, and the append-only image is never rewritten; the image
carries no ownership, so a resumed run starts owning nothing. The checks are
dynamic and advisory at the ownership words --- the honest limit without a static
type system.

#strong[Promoted as D73:] decision tables (`decision-table.fs`, D13) --- a symbolic
table of guarded rows with Z3-proved completeness and disjointness, a pure
`select`, and three distinct failure modes (`Unsound` with a witness,
`Undecided`, and an encoding `Error`). `DecisionTableEmitter.emit` renders one
property per row with the row's guards as the precondition and `rowN` as the
body the host supplies; `snapshots/decision-table.verified.txt` pins it. There
is no Prose sentence surface, no parser change, and no vocabulary change; a
domain that does not declare a table is unaffected. Machine-learning decision
models stay outside the proof-or-witness trust boundary.

#strong[Prepared, optional:] DSPy-shaped model-backed programs (D42) --- a native
signature, one model-touching `predict` word, a pluggable adapter, and a
host-side optimizer. The signature is a projection of the `.scale` contract and
the verification lattice, not a second source of truth; the model is an edge
effect recorded for replay; the fast metric ranks candidates and only the
existing `Smt.discharge` + `Repair.accept` gate can accept. Nothing is
implemented: no source, dependency, word, or test. See `docs/architecture/dspy.typ`.

#strong[Applied:] the full cell system (D43--D50) --- classes,
slots, instances, class precedence, nominal dispatch, method combination,
closures, and metaclasses. The encoding landed as D44 (tags, codecs, the class
and instance-state roots, persistence), closures/lexical scope as D45 (`let`,
`lambda`, `apply`, frame-first lookup), classes/slots/instances as D46
(`class`, `slot`, `create-instance`, `slot-value`, `set-slot-value`), and C3
precedence plus nominal class dispatch as D47 (`class-precedence`,
`subclass-of`, `instance-of`); conditions and restarts (D50) landed. The surface
is the Prose sentence grammar; the tensor entries are internal image encodings, never
syntax. An instance has stable identity and appended state; class dispatch is
nominal; closures landed before real method combination. See
`docs/runtime/cell-system.typ`.

#strong[Landed in part, still proposed:] the Metaobject Protocol (D16/D17) --- the
plan to make the language\'s extension points image-resident metaobjects with a
fixed meta-level and an explicit bootstrap kernel. Generic functions and methods
are image-resident (`-4`/`-5`) under D19, word metaobjects under D21,
predicate sorts under D31, classification under D32, and the guard runtime
protocol under D35, and the guard-lattice bridge under D41, so a new method,
word, predicate sort, or guard needs no F\# at the evaluation seam. A named
guard is executable and generative but has no SMT encoding; Z3 reports
`Unavailable` and the emitter refuses it by name. The full cell system ---
slots, class precedence, method combination, closures, and metaclasses --- has
its encoding, closures, classes/slots/instances, and precedence/dispatch landed
as D44--D50; conditions and restarts (D50) landed. A reflective evaluator remains
proposed. See #emph[The Metaobject Protocol plan] below for its nature and
benefits.

#strong[Deferred by design, tracked in the ledger:] comprehension syntax (the
filtering word landed as D34; the `[ ?x : … ]` surface still needs a domain
decision), and the Metaobject Protocol\'s full cell system (D16/D17; methods
landed as D19, word metaobjects as D21, predicate sorts as D31, the guard runtime
protocol as D35, the guard-lattice bridge as D41, and the full cell system
with its encoding, closures, classes/slots/instances, precedence/dispatch,
method combination, metaclasses, and conditions/restarts landed as D44--D50; see #emph[The Metaobject
Protocol plan] below).
W15\'s gate is exercised in-process, and its thin follow-ons landed as
D28: `scale --fixed-spec` writes the repair bundle and exits non-zero when the
fixed specification is falsified, and a subject that declares no conditions
emits a compiling marker type.
`model` remains name-as-sort sugar: it declares a sort,
but the nominal/structural pair is read through the new words (D14).

#strong[Diagnostics (D57, D61, D62):] a failure is one `Violation` with a layer, rule, span, expected/given, blame (`Input` versus `CoverageGap`), witness, and suggestion. The parser and the semantic stages report first; the SMT stage now attaches its evidence --- the counterexample model for a falsified obligation, or the labelled unsat core for a proved obligation or a refuted possibility. `Unknown` and `Unavailable` stay coverage gaps, never passes. Discourse rollback, the minimum-edit repair search, and the presentation layer are applied as D70, D71, and D72.

#strong[Landed (D75):] the actor model reserves one runtime-handled message
class. Suspend, Resume, Code-Change, Status, and Shutdown are checked before any
user delivery; a suspended actor leaves the ready queue and keeps user messages
queued, status returns a runtime tensor, and shutdown fires the terminated
actors' links and monitors. The extension adds no user word and leaves the
mailbox structure, MOP, persistence, and linear regions unchanged. `just check` runs 1683 examples, 58 properties, 13 snapshots, and 14 integrations (1768 checks); the granular suite has 36 tests. The memory tier and restricted
Go-transpiler path remain a design note in
`docs/runtime/memory-and-go-transpiler.typ`.

#strong[Landed (D76):] a violation can carry the offending source range.
`Violations.parseText` tokenizes and parses the original sentence and fills an
exclusive `Start`/`End` character range from the surface form; the token-only
API stays offset-free, and the presentation renders `characters start-end`. A
repeated surface form uses its first occurrence, and semantic violations
without source text keep no range. This is presentation data, not a verdict.

#strong[Landed (D77):] the Discourse layer has a direct contradiction detector.
`Discourse.contradicts` recognises Every/No and Some/No over the same subject,
ordered conditions, and verb; `Discourse.contradiction` returns the first
committed statement a candidate contradicts as a `Contradiction` violation. The
detector is syntactic and does not use SMT, paraphrase matching, condition
reordering, or transitions.

#strong[Landed (D78):] the showcase puts an SMT counterexample through the same
diagnostic sink as the parser, table, and discourse examples. It encodes the
demo property, discharges an empty-condition `Capability`, converts the falsified
result with `Violations.ofSmtDischarge`, and adds it with `Diagnostics.addOption`.
This is showcase wiring, not a CLI SMT mode or a change to the acceptance gate.

#strong[Landed (D79):] the verification model now separates typed proposals from
advice. `FixedSpecificationAcceptance.accept` takes a `FrozenSpecification` and
an `ImplementationProposal`; an implementation proposal cannot carry a
specification edit. `SpecificationProposal` requires an exploratory
`SpecChangeCapability`, and `NearestSentence` produces `Advice` rendered as a
suggestion. The old `Patch.SpecEdit`, `Repair`, and `RepairSearch` names are gone;
the bundle JSON wire format is unchanged.

== The Metaobject Protocol plan
A #emph[sort] in this repository is a named value set; the code type is
`EvaluatorValueSets.ValueSet`. It does not order anything.

Scale\'s language mechanisms are mostly host code. `executeWord` is a large F\#
`match`; adding a word means adding a case. `EvaluatorValueSets` owns the set-operation
policy, but a named sort constructor is now image-resident and needs no F\#
(D36); the set-operation methods remain host defaults. Guard evaluation has a
runtime protocol (D35) and a verification-lattice bridge (D41): a document
refinement can name an image-resident guard, and an unencodable guard is
refused rather than dropped. Method dispatch is no longer a match: D19 (W10) built multiple
dispatch as image-resident generic functions and methods, so a new method is
data, not a case. The kernel inventory counts #strong[80 explicit word cases] at
D36. Each is a place where behavior is fixed in the implementation rather than in
the image; the plan moves the extension points and keeps a named primitive
kernel.

The plan reverses that direction. Think of it as moving the language\'s wiring
diagram out of the F\# source and into the image: the same heap that holds data
also holds the definitions of words, sorts, and methods. Concretely, the
extension points --- words, sorts, generic functions and methods, guard
evaluators, and protocol failures --- become #strong[image-resident metaobjects];:
ordinary tensors that describe themselves and their behavior. A word metaobject says which token it answers to, whether it
is active, and which implementation it runs. A sort metaobject says how
membership is decided. A generic function metaobject lists its methods and its
ambiguity policy. A small protocol --- operations with default methods --- reads
those tensors and dispatches. Adding a new word family, sort constructor, or
method becomes adding a metaobject to the image, not another case in the F\#
dispatcher.

This is the Metaobject Protocol in the #strong[open implementation] sense: the
language exposes the implementation of its own extension points, so users can
extend it without the host language. It is not a reflective tower: the evaluator
does not become a metaobject, and `eval` is not a protocol. It is also not a
wholesale CLOS clone: slots, class precedence,
`:before`/`:after`/`:around`, and `call-next-method` wait until slots and
lexical closures exist. And it is not exposed to the child-facing sentence
layer: a child still writes \"Every player that is alive can jump.\"; the protocol
sits underneath D9\'s domain binding.

=== Why it is worth doing
- #strong[One extension mechanism instead of a growing match.] Today each new word,
  sort operation, or guard is another F\# case. The protocol replaces the
  per-word case with data and a default method, so extensions open without the
  kernel growing with each one.
- #strong[Definitions become data.] A metaobject is a tensor in the image, so it
  persists, round-trips, and can be inspected by the same tools as any other
  value. The image stays the single source of truth instead of splitting
  definitions between the image and the binary.
- #strong[The language can describe its own extensions.] The vocabulary that parses
  sentences already lives in the image; the protocol moves its behavior there
  too. The image becomes the description of the language\'s extension points, not
  only of its data.
- #strong[The proof pipeline gets a seam.] A predicate sort can carry its own
  membership decision and its own SMT encoding. If the solver cannot decide, the
  protocol returns `Undecided`; it never turns an unknown into a silent false.
  That is the same proof-or-witness boundary the rest of the project uses.
- #strong[Ambiguity is explicit.] Generic-function dispatch reuses the decision
  table\'s `Selected`/`NoMatch`/`Ambiguous` and its
  `Unique`/`First`/`Priority` policies. A tie is a reported verdict, not
  whichever method happened to run first.
- #strong[The kernel stays honest.] The protocol needs a fixed point, and the plan
  names it: tensors, image append and lookup, text interning, `EntryIdentity`, root
  threading, stack operations, literals, the protocol dispatcher, and `run`.
  Everything else is a candidate, and the inventory is the counterweight to
  kernel growth.

=== What it looks like in practice
A metaobject is a rank-1 tagged tensor `[tag; field…]`. Identity is the
`EntryIdentity` (address), not structural equality, so `class-of` can be an address
lookup while `same-shape` stays structural. Levels are explicit: base values
(0), sorts and words (1), constructors and metaclasses (2), protocol (3). Tags
are negative sentinels from a single append-only registry below `-1`, following
the precedent of D15\'s `-2` term and `-3` typed term. The image is the only
store; a change is a new entry, not a host mutation.

The work is staged so each step has a falsifiable test:

#table(
  columns: (auto, 2fr, 3fr),
  align: (left, left, left),
    table.header(table.cell(fill: luma(240))[Stage], table.cell(fill: luma(240))[Work], table.cell(fill: luma(240))[Test]),
    table.hline(),
    [0], [Inventory the host surface], [Every explicit word case and host mechanism is listed.],
    [1], [Encode metaobjects (D16)], [Round-trip; tag discrimination; address identity; no host store.],
    [2], [Words], [A new word family needs no F\#.],
    [3], [Sorts], [A named constructor needs no F\# (D36); membership returns a verdict. The
set-operation methods remain host defaults.],
    [4], [Generic functions, methods, guards], [A new method or guard needs no F\#; ambiguity is `Ambiguous` or
`NoMatch`. #strong[The method half landed as D19 (W10), the runtime guard half as
D35, and the guard-lattice bridge as D41: a named guard is evaluated by the
image, and an unencodable one is refused rather than dropped.]],
    [5--7], [Control, conditions and restarts, full protocol], [Landed as D44--D50; control refinement (D17 stage 5) remains.],
)
The near-term scope is stages 0--4: inventory, encoding, words, sorts, and the
generic-function, method, and guard protocol. It comes after W4 (D15) and W10
(multiple dispatch, landed as D19), and the predicate-sort, runtime-guard, and
sort-constructor decisions (D31, D35, D36).

The first test for every stage is the same: #strong[a user adds a word family, sort
constructor, method, or guard without editing F\#];. If it still needs an F\#
case, it is not a protocol yet. The second test is that a protocol decision
returns a verdict, never a silent false.

#strong[Status: recorded and partly applied.] Generic functions and methods are
applied as D19 (W10), word metaobjects as D21, predicate sorts as D31, runtime
guards as D35, sort constructors as D36, the set-operation protocol methods as
D39, the guard-lattice bridge as D41, the cell-system encoding as D44,
closures and lexical scope as D45, classes/slots/instances as D46, and class
precedence/nominal dispatch as D47, method combination as D48, metaclasses as
D49, and conditions/restarts as D50. The decisions are D16
(encoding), D17 (scope), D19 (multiple dispatch), D31, D35, and D36 in
`docs/process/decision-records.typ`; the baseline is `docs/kernel/kernel-inventory.typ`.

== What it could be for
These are honest use #emph[cases];, not promises --- each depends on work that the
ledger lists as open:

- #strong[Teaching computing as sets and stacks.] A child writes plain sentences
  about players and jumping; underneath, they are touching rank-0 tensors,
  set complement, and a heap --- the same math the grown-ups use.
- #strong[Executable specifications from natural language.] Domain rules captured
  as controlled natural language become compiling property-based tests --- the
  spec #emph[is]
  the test suite, and the pipeline\'s output is verified to compile before
  anything else is claimed.
- #strong[A laboratory for language design.] One axiom, one structure, one
  protocol: a minimal place to study what a heap-only, tensor-only language
  feels like --- where it stops being elegant and starts being work.
- #strong[A testbed for verification tooling.] The emitted predicates are
  framework-free by construction (D5), so a Hedgehog harness, an FsCheck
  harness, or an in-process SMT discharge (Z3, D10) can adopt them unchanged.
- #strong[Research into self-describing systems.] Everything, including the
  vocabulary that parses sentences, lives in the image it describes ---
  a language that carries its own genome. The Metaobject Protocol plan is the
  concrete route: the vocabulary and sorts become metaobjects, not host cases.

#line(length: 100%)
#emph[Scale is small, honest about its gaps, and keeps its promises pinned to
tests. That last sentence is checked: see the ledger.]

= License

Code is licensed under the BSD-2-Clause-Patent license (`LICENSE`).
Documentation and the Typst sources are dedicated to the public domain under
CC0-1.0 (`LICENSE-docs`).
