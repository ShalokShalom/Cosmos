= Local binding

#strong[Status: implemented.] This is the feature contract for the Prose
local-binding slice of the Prelude port. It adds one expression form that names
an intermediate value inside a body; it does not add a second module system or
local mutation.

== Goal and boundary

A body needs to name intermediate values. Without it, a port of the Prelude
duplicates arguments and nests calls. This slice adds `let name = value in body`
as an expression, usable wherever an expression is allowed: a book definition
body and a prose sentence.

Port targets covered: intermediate arithmetic, repeated subexpressions,
destructured values passed to several uses, and any Prelude body that binds a
local before using it twice. The slice is surface and scope only; the value
representation, the `let` word, and the kernel do not change.

== Decided

+ #strong[Form is `let NAME = VALUE in BODY`.] `let` and `in` are prose
  keywords. The value is any expression; the body is any expression. The whole
  form is one expression and ends where the enclosing expression ends (a
  period, a comma choice, or a closing parenthesis).
+ #strong[Sequential, lexical scope.] The value is evaluated once, before the
  body. The name is in scope in the body only, not in the value and not after
  the body. A local name is not exported and does not enter the book's
  definition table.
+ #strong[Nesting and shadowing are allowed.] `let a = 1 in let b = 2 in ...`
  nests; a local may shadow a parameter, another local, or a shape name. The
  innermost binding wins, so a let-bound name that shadows a shape stays a
  lexical value, not a construction.
+ #strong[Lowering reuses the existing `Let` node.] The value is bound through
  the existing `let` word into a fresh slot, and the body runs with that slot
  in scope. No new IR node, instruction, or value representation is added.
+ #strong[One name per binding.] A binding binds exactly one lowercase name. A
  pattern is not a binding form; destructure with `match`. `let ... and ...`
  mutual local groups and `let rec` are not provided.

== The form

```text
logic distance x y = let dx = subtract(x 1) in let dy = subtract(y 2) in add(multiply(dx dx) multiply(dy dy)).
logic main = let y = add(1 2) in multiply(y y).
```

A prose sentence may bind a local too:

```text
let y = 2 in add(y y).
```

The name is a name token: a lowercase word, a spaced vocabulary phrase, or a
head `of` owner phrase, exactly like a definition name. `let` and `in` are
reserved words and cannot be names.

== Refusals

#table(
  columns: (auto, 2fr),
  align: (left, left),
  table.header(table.cell(fill: luma(240))[Case], table.cell(fill: luma(240))[Meaning]),
  table.hline(),
  [`ExpectedBindingName`], [`let` with no name],
  [`ExpectedEquals`], [`let name` with no `=`],
  [`ExpectedIn`], [a binding with no `in` before the expression ends],
  [`ReservedBindingName`], [a lowering refusal for a reserved token used as the name],
)

Every refusal is a named `ProseError` case (or the existing lowering refusal)
with a test; none is a silent success or a guessed scope.

== Limits and non-goals

+ No `let ... and ...` mutual local group; nest the bindings instead.
+ No `let rec`; recursion is a book definition (`logic`/`cycle`), the module
  layer.
+ No local pattern binding; bind one name and destructure with `match`.
+ No assignment or mutation; a local is immutable.
+ No `where`/`with` alternative binding form; one surface only.
+ No second scoping rule for prose; a prose local is the same lexical binding
  as a book local.

== Tests derived from this contract

+ Parse: `let y = 1 in y` produces `Let("y", 1, Word "y")`.
+ Evaluation: `let y = add(1 2) in multiply(y y)` returns 9.
+ Sequential nesting: `let a = 1 in let b = 2 in add(a b)` returns 3.
+ Shadowing: a local `x` shadows a parameter `x`; the local value wins.
+ Lexical scope beats construction: a local named after a declared shape stays
  a value, not a construction.
+ Prose: a `.prose` sentence with `let` parses and runs.
+ Refusals: each row of the refusal table has a named-error test.
+ A generated property: `let x = a in let y = b in add(x y)` equals `a + b`
  for generated integers (property).

== Acceptance for the port

The slice is ready when a Prelude body can bind an intermediate value and the
existing behavioral tests pass without host-side special cases.
