= Field access

#strong[Status: implemented for books.] `field("name" record)` reads a named
field from a record value. The name must be a text literal; the record type is
projected or extended by the type checker.

== Contract

+ The form is `field("name" record)`. The first argument is a text literal; the
  second is a record expression.
+ A closed record with the field returns that field's type.
+ An open record with the field returns the field's type.
+ An open record without the field extends its row with the field and returns a
  fresh type; the row variable is bound to the extension.
+ A closed record without the field is `UnknownField`.
+ A non-record value is `TypeMismatch` with context `field access`.
+ The runtime word reads the field by name from a shape compound. It uses the
  book's declared shape field order.
+ A missing shape or field is a named host failure, never a silent default.

== Surface

```text
shape point with x y.
logic xof p = field("x" p).
logic yof p = field("y" p).
logic main = add(xof(point(1 2)) yof(point(3 4))).
```

`xof` infers `(record { x: t | r }) -> t`; `yof` infers
`(record { y: t | r }) -> t`. Applying either to `point(1 2)` closes the row
with the shape's fields.

== Implementation

+ `components/verification/typing/type-retrieval.fs`: the `field` call special
  case and `projectField`; `UnknownField`; open-row extension through
  `RecordRows`.
+ `components/core/host-words.fs`: `field` is a book host word with arity 2.
+ `components/book-host/book-runner.fs`: the field adapter uses
  `book.Document.Shapes` to map a shape name and field name to the compound
  argument position.
+ The `book` capability covers the adapter; a book run grants it.

== Limits

+ Field names must be text literals. A computed field name is not typed.
+ Field access is a book host word; it is not available in a bare expression.
+ No field update, nested projection syntax, or structural subtyping beyond the
  row unifier.
+ The runtime reads fields by the declared shape order; a record value whose
  head is not a declared shape is refused.

== Tests

+ A book with `field` passes the check gate.
+ `field("x" point(1 2))` and `field("y" point(3 4))` produce `5` through
  `main`.
