= Row and set types

#strong[Status: foundation implemented; the kinded model, the self-hosted unifier, the host effect migration, the host open-record migration, and field access are landed.]

Rows are for structural data: records and variants. Sets are for effects.
They are separate kinds with separate unifiers and no cross-unification.

== Contract

+ A #strong[row] is an ordered sequence of labels with an optional nominal row
  tail. Labels are compared as a set; order is presentation only.
+ A #strong[set] is a finite set of labels with an optional nominal set
  variable. There is no order and no duplicate label.
+ #strong[Row variables and set variables are nominal and distinct.] A row
  variable cannot appear in an effect position, and a set variable cannot
  appear in a data row.
+ #strong[Separate unifiers.] `RowTypes.unify` handles data rows;
  `LabelSets.unify` handles set expressions. Neither calls the other.
+ #strong[The boundary is the field type.] A record field may carry a function
  type with an effect set. That is a field type, not a row label; the row and
  the effect set meet only at the field.
+ #strong[Closed first.] Closed rows and closed sets are the default. Open rows
  or open sets are added only when a concrete consumer needs them; no AC
  unification by default.

== Implementation

+ `components/verification/typing/row-types.fs` — the data-row engine:
  `Row<'label>`, `RowVariable`, `RowSubstitution`, `ofList`, `apply`, `occurs`,
  `unify`, `describe`.
+ `components/verification/typing/label-sets.fs` — the set engine:
  `LabelSet<'label>`, `SetVariable`, `SetSubstitution`, `closed`, `openSet`,
  `apply`, `occurs`, `unify`, `describe`.
+ `components/verification/typing/unification.fs` — the kinded model:
  `TypeTerm`, `KindedTerm`, one `State` with type, row, record-row, and set
  substitutions, `unifyType`, `unifyRow`, `unifySet`, `unifyKinded`,
  `generalize`, `instantiate`.
+ `RecordField` carries a name and a payload; `Record` is a row of fields with
  an optional nominal tail. `recordOfList`, `unifyRecord`, and
  `Scheme.QuantifiedRows` provide payload absorption and row polymorphism.
+ `components/verification/typing/unification-host.fs` — the closed-fragment
  adapter between `TypeRetrieval.Type` and the kinded model.
+ The engines are independent. A record integration unifies field payloads
  separately from the row labels; an effect integration keeps using sets.
+ The host type checker infers a `Message` of a declared shape as a closed
  record and now uses the same open record row representation as the kinded
  model.
+ A field-access surface for open records is still pending; records are useful
  for pass-through and row-polymorphic signatures until then.

== Kinded unification

The model has one dispatcher and three kinds. A type variable never binds a row
or set term; a row never unifies with a set. `unifyType` handles variables,
primitives, sorts, open records, and functions. A record is a row of
`RecordField` values; a common field's payload unifies with `unifyType`, and an
open tail absorbs a field with its payload. A function's effect set is a
`LabelSet<string>`, so an open effect set can bind to concrete effects. A
`Scheme` quantifies type variables, effect variables, and record row variables;
`instantiate` freshens all three. `unifyRow` and `unifySet` are the engines
above; the dispatcher refuses a term of the wrong kind with `KindMismatch`.

The self-hosted unifier is a book fixture,
`components/verification/tests/fixtures/unification.book`. It represents the
same terms as shapes, carries a substitution as a list of pairs, and returns
`ok(value, next)` or `error(reason)`. It covers type variables, primitives,
functions with effect sets, closed records, rows, sets, and kind refusal. Its
fourteen checks run through `BookRunner.cast`; the host adapter agrees with the
host unifier on the closed record check.

== Limits

+ The host `TypeRetrieval` path uses the same open effect set and open record
  row model; the kinded model is the reference for the set rules and the
  self-hosted unifier.
+ Open records with payload absorption and field access are implemented in both
  models; field names must be text literals and field access is a book host
  word.
+ The self-hosted unifier uses numeric labels in its test corpus; the host uses
  text labels. The shape of the algorithm is the same.
+ No row/effect interaction; effects remain sets.
+ No typed handlers. Closed and open effect subtraction are both implemented:
  open subtraction records a lacks constraint, and a later binding that contains
  the removed label is refused.

== Why separate

Effects are commutative, idempotent sets. Data rows are ordered and may need a
tail for extensible records. Using rows for effects would import row order,
tail variables, and lacks constraints into the effect system; using sets for
data would lose the tail that extensible records need. The separation keeps one
set-theoretic account for effects and one row account for data, with a clear
boundary at the field type. It avoids a single hybrid mechanism that is wrong
for at least one of the two.

== Tests

The suite's `Row and set types` block covers:

+ closed rows with the same labels unify;
+ closed rows with different labels are refused;
+ an open row absorbs the other side's labels;
+ two open rows allocate one fresh tail and meet there;
+ a row variable cannot occur in itself;
+ closed sets with the same labels unify;
+ closed sets with different labels are refused;
+ an open set absorbs the other side's labels;
+ two open sets allocate one fresh unknown and meet there.

The kinded model block covers:

+ a closed record unifies with itself;
+ a record payload mismatch is refused;
+ an open effect set binds to concrete effects;
+ two open effect sets meet at a fresh unknown;
+ a row and a set are refused by kind;
+ a scheme quantifies a free set variable;
+ instantiation freshens the set variable;
+ the host adapter agrees with the host unifier on a closed record;
+ an open record absorbs a field with its payload;
+ an open record unifies a common field's payload;
+ a scheme quantifies a free record row variable;
+ instantiation freshens the record row variable.

The host record block covers the same absorption and common-payload rules for
`TypeRetrieval.Type`.

The self-hosted block covers primitive equality, type-variable binding, occurs
check, function effects, record payloads, row absorption, set absorption, two
open sets, the fresh-set counter, and kind refusal.

== Long-term value

The row engine is the foundation for extensible records and variants; the set
engine is the foundation for effect sets and set-theoretic data. They can be
integrated independently: rows into the record/variant layer, sets into the
effect layer. Neither forces the other, and neither becomes a second effect
system.
