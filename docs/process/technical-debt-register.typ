= Scale --- Technical-Debt Register
Scope: make every #strong[core] image reference nominally `EntryIdentity`, and let the
compiler expose what remains host-typed in core. The edge is explicitly
whitelisted.

This register operates at the #strong[host-encoding] level. It is deliberately
separate from the #emph[commitments ledger] (`docs/process/commitments-ledger.typ`), which
tracks #strong[cell-semantics] commitments. See also the level model in `types.fs`.

== Two axes
#table(
  columns: (auto, auto, 2fr),
  align: (left, left, left),
    table.header(table.cell(fill: luma(240))[Axis], table.cell(fill: luma(240))[Values], table.cell(fill: luma(240))[Applies to]),
    table.hline(),
    [#strong[Scope]], [Core / Edge], [#emph[where] host types may appear],
    [#strong[Level]], [Cell / Host], [#emph[which layer] a type belongs to],
)
A type is legitimate host encoding iff it satisfies both rules:

+ #strong[Projection.] It has a total, injective encoding into tensors and a decoding.
+ #strong[Image-resident state.] It holds no state absent from the image.

A host type that fails either rule has become a #emph[semantic] type and the
\"Tensor is the only semantic type\" invariant is broken.

== Nominal types introduced
- #strong[`EntryIdentity`] (Wave 0) --- a distinct image reference.
  `[<Struct>] type EntryIdentity = EntryIdentity of int`, non-negative, unset = `EntryIdentity option`.
  Projection: rank-0 tensor (an address). Kills the `-1` sentinel.
- #strong[`ValueTag`] (nominal component landed in W3/D14; no host type is needed) ---
  a value\'s declared sort. #strong[Host encoding only if the sort and the tagging
  relation are image-resident.] Projection: the name slot of a model entry
  (a text `EntryIdentity`, or `-1` for anonymous), with declared domains in
  `DeclarationsRoot`.

== Core rows
#table(
  columns: (auto, 2fr, 2fr, 2fr, 2fr, auto, 4fr),
  align: (left, left, left, left, left, left, left),
    table.header(table.cell(fill: luma(240))[\#], table.cell(fill: luma(240))[Site], table.cell(fill: luma(240))[Today], table.cell(fill: luma(240))[Target], table.cell(fill: luma(240))[Scope/Level], table.cell(fill: luma(240))[Wave], table.cell(fill: luma(240))[Done]),
    table.hline(),
    [C1], [`Lexicon`], [`Map<int,Tensor>`], [`Map<EntryIdentity,Tensor>`], [Core/Host], [0], [✅ #strong[done] --- compiler rejects `int` key],
    [C2], [`Image.Root` / `VocabularyRoot`], [`int`, `-1`], [`EntryIdentity option`], [Core/Host], [0], [✅ #strong[done] --- no sentinel; empty round-trips],
    [C3], [`Image.addTensor` / `addText`], [returns `int`], [returns `EntryIdentity`], [Core/Host], [0], [✅ #strong[done] --- signature],
    [C4], [`Stack`], [`Tensor` of doubles-as-indices], [`EntryIdentity list`], [Core/Host (control state)], [0.5], [✅ #strong[done] --- push/pop round-trip; no doubles-as-indices],
    [C5], [`Lace.asIndex` / `asValue`], [double decode / first element], [`EntryIdentity` / rank-0 projection], [Core/Host], [0.5], [✅ #strong[done] --- `asIndex` wraps `EntryIdentity`],
    [C6], [`TextCache`], [`Map<string,int>`], [`Map<string,EntryIdentity>`], [Core/Host], [0/1], [✅ #strong[done] --- values are `EntryIdentity`],
    [C7], [`EvaluationState.UserWords`], [`Map<string,string>`], [image-resident, keyed by `EntryIdentity`], [Core/Host], [1], [✅ #strong[done] --- `UserWordsRoot`; survives persistence],
    [C8], [`EvaluationState.Bindings`], [`Map<string,Tensor>`], [image-resident], [Core/Host], [1], [✅ #strong[done] --- `BindingsRoot`; survives persistence],
    [C9], [`EvaluationState.Models`], [`Map<string,Tensor list>`], [image-resident + sort tag], [Core/Host], [1 (after `nor`), W3], [✅ #strong[done] --- `SetsRoot` holds model entries
`[nameValue; members…]` (`-1` = anonymous); `DeclarationsRoot` holds declared
domains; latest-write-wins, domains grow only. No separate `ValueTag`
host type is needed (D14)],
    [C10], [`EvaluationState.Program`], [`string list`], [#emph[re-scoped: control state]], [Core/Host], [2], [✅ #strong[closed (re-scoped)] --- see D4],
    [C11], [`Tensor.Data`], [`double[]`], [leaf host storage], [Core/Host (leaf)], [out], [accepted leaf leak],
    [C12], [counters (`Location`/`Position`/`Baseline`/`Reductions`)], [`int`], [out of scope (structural)], [---], [---], [---],
)
`EntryIdentity` is `[<Struct>] type EntryIdentity = EntryIdentity of int`, non-negative, with unset
represented as `EntryIdentity option`. `NextIndex` stays `int` (a counter, not a
reference). `IndexTransformation` and `Shape` stay `int` (structural, out of
scope); `EntryIdentity.value` is the boundary unwrap.

`C4` is reclassified as a #strong[category error] (control state encoded in the data
representation), not a host-type leak. `Stack` is allowed host types, the same
latitude as the edge. `C10` is likewise #strong[re-scoped as control state] (decision
D4): every persistent form of program text (user-word bodies, quotations,
bookmark programs) is already image-resident; the live `Program` list is only
the interpreter\'s pending-token work queue and is never persisted.

#strong[Wave 2 is closed] (re-scoped, no conversion) and #strong[Wave 3 is closed];: the
edge track (`Tokenizer`/`Parser`/`Emitter`/`Bag`) derives its vocabulary from
the image (`VocabularyInImage.readVocabulary`) and respects `IsActive`, so
edge and core agree on word availability --- the live divergence where `canFly`
tokenized and parsed but then failed at evaluation is fixed. The register is now
closed; what remains open lives in the commitments ledger as deferred work.

== Edge rows (allowed; adapters)
`Tokenizer` string input; `Parser`/`Emitter`/`Bag` string fields and
`WordCategory`; `Persistence` `BinaryReader`/`Writer`. Edge rows must #strong[derive
from the image copy];, not constitute a second authority (two-sources-of-truth fix).

== Named decisions (must precede waves)
+ #strong[Ownership lifetime.] Is linear-ownership state #emph[data] (serialized with the
  image) or #emph[execution state] (like `Location`, not serialized)? This determines
  whether C7--C9 need a new serialization section at all. #strong[Answered (D1):]
  execution state; landed as W9/D30, with no serialization section.
+ #strong[Dictionary encoding.] How an association is represented as image entries ---
  reuse the vocabulary-vector precedent (a vector of `EntryIdentity` pairs), with name
  resolution via the text index.
+ #strong[Value typing / sorts --- landed (W3/D14).] A sort is the image-resident
  pair `(option Name, Extension)`: the name is a text entry in a model\'s first
  slot, the extension is the remaining member identities, and a declared domain lives
  in `DeclarationsRoot`. `union`/`intersect`/`except` are structural; `merge` is
  seal-preserving; `complement` reads a declared domain rather than the whole
  lexicon. The ternary `nor` is unchanged: a binary default-universe form is
  ambiguous on a stack, so the default landed as a distinct unary word.
  Substitution-based unification landed in W4/D15 without a new host type or
  root: terms are image entries (a variable is text, a compound is a `-2`
  tagged vector, a typed term is a `-3` tagged vector) and the substitution is
  the existing `BindingsRoot` association vector. `ValueSet.decode` accepts only
  `-1` as the anonymous marker, so a term cannot be misread as a sort.

== Guardrail

If a nominal type ever needs information not projectable into a tensor, that is an
alarm --- either move the information into the image or revise the invariant.
Linear/ownership state is the most likely first violator; its lifetime (data vs.
execution state) is therefore also a level decision.

#line(length: 100%)

== Post-D126 port review

The nine-commit slice (D119--D126) is landed; these are the debts it leaves
visible. D127 records the 1288-example / 21-granular verification and the one
follow-up code change (`EnableWriter` is Debug-only by default).

- #strong[Writer harness ABI.] Landed for value instructions, stack access,
  dispatch, and failure (D128): the emitted method takes and returns the real
  `KernelState<EvaluationState>` and calls the AOT-safe `WriterRuntime`. Units as
  methods and direct lambda calls are landed (D134); what remains is first-class
  closures plus the tensor and pattern opcodes.
- #strong[Pattern roles and guards.] Open subjects, `Pin`, `Repeated`,
  `Shadowing`, and guards remain interpreter-only by D130. They need
  image-resident bindings and pinned predicate units.
- #strong[Lambda capture.] Capture is conservative (every enclosing name), not a
  free-variable analysis. A later analysis can shrink captures without changing
  semantics.
- #strong[Declaration nodes.] `DeclareKind`, `DeclarePredicate`, `Condition`,
  `Restart`, `Generic`, and `Message` are still named refusals, interpreter-only
  by D130. They need image-resident registries and a dispatch contract.
- #strong[First adapter.] The first host adapter is C (D130); the conformance
  harness and version equality land with it. Until then the SageFs suite is the
  acceptance gate.
- #strong[Sequences and recursive types.] Target (D132), not implemented.
  Sequences need an ordered projector and words; recursive types need a
  recursive reference in the portable type document and a fixpoint rule.
  Multisets and co-products wait for a consumer.
- #strong[Distribution.] Target (D133), not implemented. The
  `scale_transport_send` ABI seam exists, but node identity, remote
  send/receive, and partition/failure semantics are undesigned.
- #strong[Capture.] Free-variable capture is implemented (D133); the conservative
  version is retired.
- #strong[Differential coverage.] The corpus covers the token subset; it does not
  drive every `lowerExpression` path. Extend it as the lowering grows.
- #strong[Process.] The alpha reload uses SageFs `rebuild=true`, which invokes
  MSBuild; the owner should confirm this is the intended exception. Typst phrase
  edits still need `edit` plus `doc_compile`.