= Multiple dispatch

#strong[Status: implemented (first slice).] This is the feature contract for the
Prose multiple-dispatch slice of the Prelude port. Both decisions are taken:
specializers name shapes, and method bodies are ordinary lowered book
definitions called by a synthesized dispatcher.

== Goal and boundary

The Prelude selects behavior by the runtime types of arguments and extends that
behavior without editing the caller. This feature adds a book-level declaration
for a generic function and for methods of that function, and selects the most
specific applicable method at call time.

Port targets covered: ad-hoc overloading, interfaces and protocols, record and
DU selection, and open extension by later methods in the same book. Unique
benefit: selection is open across method declarations, unlike `match`, which is
closed at the definition site. Overlap: `match` stays the closed decomposition
of one value; dispatch is the open, extensible alternative. No separate
interface or type-class feature is added.

== The surface

```text
generic area with 1.
shape point with x y.
shape circle with r.
method area(p : point) = 1.
method area(p : circle) = 2.
logic main = area(point(2 3)).
```

+ `generic NAME with ARITY.` declares the generic function and its arity. The
  arity is a non-negative integer.
+ `method NAME(p : SHAPE ...) = BODY.` declares one primary method. The
  parameter names bind the arguments in the body; each `: SHAPE` is one
  specializer, in argument order.
+ A method declaration is open extension: later `method` declarations for the
  same generic add methods, and the dispatcher includes them even when the call
  appears earlier in the book.
+ A generic is called like a word: `area(point(2 3))`, or as a bare word when
  its arity is zero. The name is not a `logic` definition and does not enter the
  definition table.

== Decision 1: what a specializer names

#strong[Decided: shapes (structural).] A specializer names a declared shape;
the compound's head selects the method. The port's records and discriminated
unions are shapes, not classes, so shape specializers are the only choice that
serves the port's acceptance proof. Classes remain the identity/mutation feature
and are not part of this slice. A later slice may add class specializers to the
same generic surface; that addition needs a stated precedence rule between a
class match and a shape match, and is deferred.

== Decision 2: how a method body is represented

#strong[Decided: ordinary lowered definitions plus a synthesized dispatcher.]
The parser compiles the declarations into the existing language:

+ each method becomes a synthetic `logic` definition (`$method:NAME:INDEX`) with
  the declared parameters and body; it is lowered like any other definition, so
  the body is compiled code with named slots, not an interpreter quotation;
+ each generic becomes a synthetic dispatcher definition (`$dispatch:NAME`) whose
  body is a nested `match` over the argument shapes; each clause tests the
  compound head and arity and calls the matching method definition with the
  original arguments;
+ a call to a generic is rewritten to a call to its dispatcher;
+ the `otherwise` branch of each dispatcher is `Fail "NoApplicableMethod: NAME"`.

Synthetic definitions are marked and are not exported. This adds no IR node,
instruction, kernel change, or image schema change. Method bodies are shared
lowered units, so a recursive method does not expand at the call site, and
selection is compiled pattern tests rather than an interpreter lookup.

== Refusals

#table(
  columns: (auto, 2fr),
  align: (left, left),
  table.header(table.cell(fill: luma(240))[Case], table.cell(fill: luma(240))[Meaning]),
  table.hline(),
  [`GenericOutsideBook`], [`generic` in a `.prose` file],
  [`MethodOutsideBook`], [`method` in a `.prose` file],
  [`ExpectedGenericName`], [`generic` with no lowercase name],
  [`ExpectedGenericArity`], [`generic name with` with no arity],
  [`InvalidGenericArity`], [an arity that is not a non-negative integer],
  [`DuplicateGeneric`], [the same generic name declared twice],
  [`GenericNameConflict`], [a name that is both a generic and a definition or shape],
  [`ExpectedMethodName`], [`method` with no generic name],
  [`ExpectedMethodParameters`], [a method without its parenthesized parameter list],
  [`ExpectedParameterName`], [a parameter position with no name],
  [`ExpectedParameterColon`], [a parameter name without `:`],
  [`ExpectedParameterShape`], [a parameter `:` without a shape name],
  [`UnknownGeneric`], [a method naming an undeclared generic],
  [`UnknownShape`], [a method naming an undeclared shape],
  [`MethodArityMismatch`], [a method whose parameter count differs from the generic arity],
  [`DuplicateMethod`], [two methods with the same specializer list],
  [`DuplicateParameter`], [a repeated method parameter name],
  [`GenericCallArityMismatch`], [a generic call with the wrong argument count],
  [`NoApplicableMethod`], [the runtime refusal when no method matches the argument shapes],
)

Every refusal is named and has a test; none is a silent pick or a guessed
specializer.

== Limits and non-goals

+ No class specializers in this slice; classes are the identity/mutation
  feature and a mixed class/shape space needs a precedence rule.
+ No cross-book generic or method visibility; an `open` imports words, not
  methods. Methods are collected and dispatched within one book.
+ No method combination surface (`before`/`after`/`around`) in this slice; the
  core words exist and the book surface can follow.
+ No dispatch on the values inside a shape; a shape specializer matches the
  head and arity, and field-level selection is `match`.
+ No method specificity beyond an exact shape vector; a method applies when
  every argument's shape matches its specializer exactly.
+ No generic as a first-class value; a generic name is a call, not a value.
+ No change to `match`; closed decomposition and open dispatch stay separate.
+ `--book-check` type-checks the user definitions; the synthetic dispatcher and
  method definitions are trusted compiler output and are not re-inferred.

== Tests derived from this contract

+ A book parses a generic declaration and a method declaration, and the
  dispatcher definition is synthesized and marked.
+ Two methods on two shapes select by the runtime argument shape.
+ A method declared after the call still extends the generic.
+ A two-argument generic matches every argument shape.
+ A zero-arity generic dispatches from a bare word.
+ A missing method is `NoApplicableMethod: NAME`.
+ Each refusal row has a named-error test.
+ A generated property: for generated argument shapes, exactly one applicable
  method is selected.

== Acceptance for the port

The port's first proof --- a record, a recursive function, a higher-order call,
and a dispatch method --- can now be written as one book and run through `cast`
with no host-side special case.
