# Deferred directions

Status: decision, not implementation. These are the chosen target designs for
the items left out of the kinded-unification slice. The current suite remains
green; each direction is staged and opens only when its tests and contract are
written.

Decision rule (revised): choose the most expressive and flexible option that can
reuse an existing engine **and pass a feasibility gate**. When no option
satisfies expressiveness, flexibility, and sharing together, choose the most
shared feasible option and record the expressive option as a gated target. The
first version of this document over-indexed on expressiveness; the critical
review below corrects that.

| Area | Chosen direction | Reused code |
|---|---|---|
| Open records | Payload-aware record row (row-carried fields) | `RowTypes` tail/label mechanics, `unifyType` |
| Handlers | Target: deep multi-shot with second-class blocks; next slice: shallow one-shot with explicit handler values | `LabelSets`, `Function` |
| Second-class functions | `Block` alongside `Function`, gated on a mandatory checked path | Existing parameters, result, effects, application |
| Word effects | `WordSignature.Effects` becomes `EffectSet` plus `QuantifiedEffects` | `Scheme`, `generalize`, `instantiate` |
| Self-typing | Typed unifier plus bootstrap, gated on a signature surface; the fixture remains the oracle | Host checker, book language, differential fixture |
| Rows | Lacks constraints when needed; AC is implicit | `RowTypes` set-based label equality |
| Performance | Shared `Map`/`Set`; profile-driven | Existing host and self-hosted representations |

## Critical review

The first decision pass had three real errors and two overclaims.

+ **Row-carried fields do not work with plain `RowTypes.unify`.** The row engine
  unifies labels and tails; it never unifies payloads. With name-only label
  equality, two fields with the same name and different types look equal, so the
  payload difference is silently ignored. The correct design is a payload-aware
  `RecordRow` unifier: use `RowTypes` for label/tail mechanics, unify common
  payloads with `unifyType`, and carry absorbed fields. This still reuses the
  row engine, but not as a drop-in call.
+ **Deep multi-shot handlers are the most expressive, but the least shared.**
  The current evaluator is a stack machine with no continuation capture.
  Multi-shot resume needs snapshot/resume of execution state and new runtime
  machinery; `LabelSets` shares only the subtraction. The original claim that
  this is “shared” was false. The next implementable slice is shallow one-shot
  with explicit handler values; deep multi-shot stays a gated target.
+ **Second-class blocks cannot be enforced by an optional type checker.** The
  runtime can store a closure, so “second-class” is only a convention unless
  effectful code requires a checked path or the runtime marks blocks. `Block`
  must be gated on one of those enforcement mechanisms.
+ **`QuantifiedEffects` alone does not give effect polymorphism in word
  signatures.** `WordSignature.Effects` is still `Set<string>`; an effect
  variable cannot appear there. `Effects` must become `EffectSet`
  (`LabelSet<string>`) as well. The change is still the right one, but it is not
  just a new field.
+ **Self-typing is a bootstrap, not a vertical slice.** The language has no
  user-facing signature syntax, and the unifier would have to check the checker
  that checks it. The differential fixture is the practical oracle; the typed
  bootstrap is gated on a signature surface.

The AC claim survives: `RowTypes` compares labels as sets, so permutation
insensitivity is already the semantics. Lacks constraints are only needed for
negative label information. Performance is not a semantic choice; a second
representation would violate the reuse rule.

## 1. Open records: row-carried fields

Status: implemented. `RecordField`, open record rows, payload absorption,
common-field payload unification, row-variable quantification, the host
`TypeRetrieval.Type` migration, and `field("name" record)` access are landed.
The field surface uses a text literal and a book host word.

A record becomes a row of fields, where a field carries both its name and its
type: `RecordField = { Name: string; Type: TypeTerm }`. The row tail is the
same nominal `RowVariable`; a record variable absorbs labels together with
their payloads.

To reuse `RowTypes`, the label wrapper compares **names only**. F# structural
comparison would compare the payload too, so the field wrapper needs custom
equality/comparison, or `RowTypes` needs a label-comparison parameter. The
payload unifier remains `unifyType`.

`RowTypes.unify` alone is not enough: it unifies labels and tails, not payloads.
The record layer must be a payload-aware `RecordRow` unifier that uses
`RowTypes` for the label/tail mechanics, unifies common payloads with
`unifyType`, and carries absorbed fields. This is a refinement of the chosen
direction, not the original drop-in claim.

Why this is the most expressive, flexible, and shared option:

+ Expressive: true open records, including field addition, field projection,
  and row-polymorphic functions.
+ Flexible: the same row engine serves records and variants; payloads travel
  with their labels, so no parallel payload map can drift.
+ Shared: one row engine and one type unifier; no second record representation.

Cost: a custom label comparer, a payload-conflict rule for shared tails, and a
field-access consumer (projection or record pattern). Without that consumer,
open records are mostly pass-through and the feature has little surface value.

## 2. Handlers: deep, multi-shot, second-class blocks

Status: effect subtraction is landed and the evaluator already has a shallow
one-shot runtime handler. The typed handler surface is next; deep multi-shot
and second-class blocks remain the gated target.
Effect subtraction uses the existing set engine: a handler consumes a set of
labels and the result carries the remaining set. The `Block` type carries
parameters, result, and effects; the handler stack and resume capture the
block's execution.

This is the expressive target, not the next slice. The current evaluator has no
continuation capture, so deep multi-shot is the least shared option. The next
implementable slice is **shallow one-shot handlers with explicit handler
values**: it reuses first-class functions and `LabelSets`, pins the semantics,
and leaves deep multi-shot behind a feasibility gate.

Why this is the expressive target:

+ Expressive: resumable, multi-shot algebraic effects, not only abortive
  exceptions or one-shot state.
+ Flexible: the same handler model covers state, exceptions, choice, and
  user-defined effects; effect subtraction is a set operation.
+ Shared only in part: `LabelSets` models subtraction and `Block` reuses the
  function representation, but continuation capture is new runtime machinery.

Cost: this is the largest change. Multi-shot resume needs continuation capture,
a handler stack in the runtime, and careful interaction with execution state and
NativeAOT. Stage it: closed effect subtraction first, then shallow one-shot
handlers, then deep multi-shot. Do not open it together with the `Block` type
unless the `Block` contract is already stable.

## 3. Second-class functions: `Block` alongside `Function`

Keep first-class `Function` for ordinary closures and callbacks. Add a distinct
`Block` type for effectful blocks that may be passed to handlers but cannot be
stored or returned as an ordinary value. `Block` shares the parameter list,
result type, and effect set representation with `Function`, and uses the same
application path.

Why this is the most expressive, flexible, and shared option:

+ Expressive: Effekt-style block typing gives handlers precise effect
  subtraction without weakening first-class closures.
+ Flexible: both callable forms exist; ordinary code keeps first-class
  functions, while effectful code gets second-class blocks.
+ Shared: the parameter/result/effect representation and application machinery
  are shared; no duplicate function model.

Cost: two callable forms, conversion rules, and a surface distinction. Making
all functions second-class would be more uniform but less flexible and a larger
break; keeping only first-class functions is less expressive for handlers.

Enforcement gate: the current type checker is optional elaboration. A `Block`
value can still be stored as a closure at runtime, so second-class is only a
convention unless effectful code requires a checked path or the runtime marks
blocks. Choose `Block` only together with one of those enforcement mechanisms;
otherwise use explicit handler values.

## 4. Word effects: `QuantifiedEffects` in `WordSignature`

Status: implemented. `WordSignature.Effects` is an `EffectSet` and
`WordSignature.QuantifiedEffects` is quantified by `wordScheme` and freshened
by `instantiate`.

Add `QuantifiedEffects` to `WordSignature`, mirroring `Scheme`, and freshen it
in `wordScheme`/`instantiate`. `WordSignature.Effects` must also become
`EffectSet` (`LabelSet<string>`); a `Set<string>` cannot contain a set variable.
A builtin such as a higher-order list word can then be effect-polymorphic.

Why this is the most expressive, flexible, and shared option:

+ Expressive: builtins and host words participate in the same effect
  polymorphism as user definitions.
+ Flexible: one signature registry covers closed and open effects.
+ Shared: `Scheme` already quantifies type and effect variables; this removes
  the last closed-only special case.

Cost: every signature constructor gains a field, `Effects` changes type, and
the registry tests grow. This is still the first direction to open, but it is
not just a field addition.

## 5. Self-typing: typed unifier plus bootstrap

Write the unifier as a typed Scale program, check it with the host checker, and
use the existing differential fixture as the oracle. The bootstrap is the
fixpoint: the typed unifier checks the checker that checks it.

Why this is the most expressive, flexible, and shared option:

+ Expressive: the unifier becomes a checked Scale artifact, not only a test
  fixture.
+ Flexible: the same typed representation can be reused by other tools.
+ Shared: it uses the host checker, the book language, and the existing
  differential corpus.

Cost: type annotations, a bootstrap plan, and a much larger test surface. Keep
the fixture until the typed unifier passes the same corpus. The bootstrap also
needs a user-facing signature surface; without one, this remains a research
direction rather than a vertical slice.

## 6. Rows: lacks constraints, AC is implicit

`RowTypes` already compares labels as sets, so permutation-insensitive (AC)
row equality is the current semantics; a separate AC unifier would duplicate
that behavior. Add lacks constraints only for negative information: a row
variable that must not contain a label.

Why this is the most expressive, flexible, and shared option:

+ Expressive: open rows can state both positive and negative label facts.
+ Flexible: records and variants share the same row engine.
+ Shared: no second AC engine; set-based equality already provides the
  permutation behavior.

Cost: lacks constraints add solver state and occur/apply rules. Add them when a
feature needs negative labels, not before.

## 7. Performance: shared representation, profile-driven

Performance is not a semantic expressiveness choice. Keep the shared `Map`/`Set`
representation behind the existing interfaces, and replace a hot structure only
when a measurement shows it matters. If a faster representation is chosen, it
must be shared by the host and self-hosted paths; a second representation would
violate the reuse rule.

## Staging

1. Word signature effects: done. `Effects` is `EffectSet`, `QuantifiedEffects`
   is preserved and freshened, and an open word effect is tested.
2. Open records: done. The kinded model and `TypeRetrieval.Type` have
   payload-aware record rows, common-field payload unification, row-variable
   quantification, and `field("name" record)` access through a book host word.
3. Effect subtraction is done. The shallow one-shot runtime handler exists; the
   typed handler surface comes next.
4. `Block` alongside `Function`, gated on a mandatory checked path or a runtime
   block restriction; then deep multi-shot, gated on continuation feasibility.
5. Typed unifier and bootstrap, gated on a user-facing signature surface; keep
   the fixture as the oracle until then.
6. Performance only after profiling.

Each stage keeps the suite green, updates the specification and counts, and
commits locally.
