= Nominal constructors with named fields

#strong[Status: implemented (first slice).] This is
the feature contract for the Prose constructor slice of the Prelude port. The
three syntax decisions were confirmed by the user: the declaration marker is
`shape`, construction resolves automatically from a declaration, and field
names live in the declaration only (construction and patterns stay
positional).

== Goal and boundary

The Prelude is built from records and discriminated unions. This feature adds
one declaration form that names a constructor and its ordered fields, and one
construction expression that builds the value. The pattern form already
exists: prose `point(a b)` parses to a positional constructor pattern and
matches the compound value.

Port targets covered: records, discriminated unions, options, results,
tuples, and cons cells. Each constructor is declared on its own; a family of
constructors is several `shape` declarations, not a type-level form.

The value representation already exists and does not change: a constructor
value is the unification compound `[-2; head; argument; ...]` built by the
`term` word. This slice adds surface, declaration metadata, and the lowering of
the existing `Message` expression node: fields are evaluated in declared order
into scratch slots, then pushed in reverse so the compound's argument order is
the declared order, and a zero-field shape lowers to a bare atom. It is not a
type system, not a class system, and not mutable state.

== Decided

+ #strong[Declaration marker is `shape`, book-only.] `shape point with x y.`
  declares the constructor `point` with fields `x` then `y`. A zero-field
  constructor is `shape none.`. `shape` in a `.prose` file is
  `ShapeOutsideBook`; prose has no declarations (books are the module layer).
+ #strong[Construction resolves automatically.] In a book body, `point(1 2)`
  where `point` is a declared shape constructs the compound value. Where the
  name is not a declared shape, the expression stays an ordinary word call,
  unchanged. No `construct` marker word is added.
+ #strong[Names in the declaration only.] Field names fix the order. Values
  are positional in that order: construction is `point(1 2)` and the matching
  pattern is `point(a b)`. Named-pair sugar such as `point(x 1 y 2)` is
  deferred until the representation proves it is needed.
+ #strong[Field names are single lowercase words.] A field name is one
  lowercase word; spaced phrases are not field names in this slice.
+ #strong[Visibility is the declaring book.] A shape declared in a book is
  constructible in that book's definition bodies. An `open` does not yet
  import shapes: cross-book shape visibility waits for the image-resident
  registry (D130), the same path as field access. Prose constructs later, when
  the registry reaches the image; prose already matches compounds and calls
  book words through `cast`.
+ #strong[Field reads are destructuring in this slice.] A record field is read
  by matching: `match p, when point(a b) then a close.` A field-access word waits
  for the image-resident registry contract (D130).

== The declaration form

```text
shape point with x y.
shape segment with start end.
shape none.
shape some with value.
logic origin = point(0 0).
logic translate p dx dy =
  match p, when point(a b) then point(add(a dx) add(b dy)), otherwise p close.
```

A declaration is a book-level sentence: the marker `shape`, the constructor
name, then either a period (zero fields) or `with` followed by the ordered
field names and a period. `with` is the existing keyword used by `run` and
`capture`.

Declarations are collected for the whole book: a constructor may be
constructed earlier in the file than its declaration, matching the book's
existing forward-call rule. A name declared twice as a shape is
`DuplicateShape`. A name that is both a shape and a `logic`/`cycle` definition
is `ShapeNameConflict`: one name, one meaning.

== Construction

In a book body, a call `name(args)` whose `name` is a declared shape
constructs the compound whose head is `name` and whose arguments are the
evaluated field values, in declared order. The arity must equal the field
count; a mismatch is `ShapeArityMismatch` at parse time, never a runtime
guess.

Construction lowers to the existing compound representation, so the resulting
value unifies with the same constructor pattern and round-trips through
`term`. Where the name is not a declared shape, nothing changes: the call
resolves as a word exactly as it does today.

== Patterns

The pattern form is unchanged and already implemented:

```text
match origin, when point(a b) then add(a b), otherwise 0 close.
```

`point(a b)` is a positional constructor pattern over the declared field
order. Exhaustiveness analysis over declared families, repeated/pin/shadowing
roles, and guards keep their existing meanings; this slice adds no pattern
syntax.

== Refusals

#table(
  columns: (auto, 2fr),
  align: (left, left),
  table.header(table.cell(fill: luma(240))[Case], table.cell(fill: luma(240))[Meaning]),
  table.hline(),
  [`ShapeOutsideBook`], [`shape` in a `.prose` file; declarations belong in a `.book` file],
  [`ExpectedShapeName`], [`shape` with no constructor name],
  [`ExpectedShapeFields`], [`shape name with` with no field names before the period],
  [`DuplicateShape`], [the same constructor name declared twice in one book],
  [`ShapeNameConflict`], [a name declared both as a shape and as a `logic`/`cycle` definition],
  [`ShapeArityMismatch`], [a construction whose argument count differs from the declared field count],
  [`ExpectedPeriod`], [a declaration that does not end with a full stop],
)

Every refusal is a named `ProseError` case with a test; none is a silent
success or a guessed arity.

== Limits and non-goals

+ No named-pair construction or pattern sugar (`point(x 1 y 2)`); deferred
  until measured need, consistent with the representation-before-sugar rule.
+ No field-access word; destructure with `match` until the image-resident
  registry exists.
+ No type-level declaration grouping constructors into one named family; a
  family is several `shape` declarations. Closed-kind exhaustiveness grouping
  is unchanged and out of scope.
+ No separate record, tuple, option, result, or list declaration forms; this
  one form covers them all (prose-prelude-port overlap decision).
+ No mutation, no inheritance, no identity; constructors are immutable
  structural values. Classes remain the identity/mutation feature.
+ No prose-side construction in this slice; prose constructs once shapes
  travel through the image registry.
+ No cross-book shape visibility in this slice; an `open` imports words, not
  shapes, until the registry lands.
+ No collection literals; `shape` declarations plus words cover lists and
  cons cells.

== Tests derived from this contract

+ Book parse: `shape point with x y.` records the name and ordered fields;
  `shape none.` records zero fields.
+ Construction: in a book body, `point(1 2)` produces the compound value
  that unifies with the pattern `point(a b)`; a two-sentence round-trip
  (construct, then match) returns the first field.
+ Positional order: declared field order is the value's argument order
  (swap-sensitive test).
+ A call to a name that is not a shape is unchanged word-call behavior.
+ Refusals: each row of the refusal table has a named-error test.
+ Prose still refuses `shape` with `ShapeOutsideBook`.
+ A parameter that shadows a shape name stays an ordinary call, not a
  construction (lexical scope wins).
+ A zero-field shape `none.` constructs an atom; `none()` matches it.
+ The lowered construction snapshot pins the instruction shape (snapshot).
+ A generated property: for arbitrary field counts and values, construct
  then destructure with the same arity returns the original values in order
  (property).

== Acceptance for the port

The first proof is the Prelude slice named in prose-prelude-port: a record
(`shape point with x y.`), a recursive function, a higher-order call, and a
dispatch method, where the record is constructed, passed, and destructured
without host-side special cases. This feature is ready when that slice runs
through `cast` and its behavioral tests pass unchanged.
