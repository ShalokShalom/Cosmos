= Prose features for a Prelude port

#strong[Status: proposal.] This is the feature contract for porting the
interpreter Prelude to Prose. It is deliberately conservative: a feature is
added only when it covers a broad class of Prelude needs, and never when an
existing feature already covers the same ground.

== Goal and boundary

The port target is the language and runtime Prelude, not the compiler, writer,
kernel, or host adapters. `components/nu-engine`, `Store`, `Tensor`, sockets,
the BCL, and the AOT writer stay native or host-side.

The port should let a `.book` express the Prelude's algorithms and a `.prose`
file orchestrate them. It should not reproduce F\# syntax, .NET namespaces, or
the F\# standard library wholesale.

== Rules for adding a feature

+ #strong[Widest coverage first.] A feature should cover several Prelude
  constructs, not one module. Multiple dispatch is the model: one mechanism
  covers polymorphism, interfaces, overloading, and method selection.
+ #strong[No strong overlap.] If two features overlap, the document must state
  the unique benefit of each and where the other benefit could come from.
+ #strong[Representation before sugar.] A new literal or sentence form is added
  only after the underlying value and word representation exists.
+ #strong[Host words before syntax.] Effects, processes, stores, and transports
  are host words and capabilities, not new grammar.
+ #strong[Books are the module layer.] Definitions, recursion, exports, and
  imports stay in books; Prose does not gain a second module system.
+ #strong[No feature without a port target.] A feature must name the Prelude
  module or construct it enables and the test that proves it.

== Feature inventory

#table(
  columns: (auto, 1.4fr, 1.8fr, 1.8fr),
  align: (left, left, left, left),
  table.header([Priority], [Feature], [Why the Prelude needs it], [Existing coverage]),
  table.hline(),
  [P0], [Named definitions and recursion], [Functions, `let rec`, mutual recursion], [Books: `logic`, `cycle`, `and`],
  [P0], [Calls, partial application, and pipelines], [Higher-order calls, currying, composition], [Prose calls and `_`],
  [P0], [Pattern matching], [DUs, options, results, tuples, guards], [Prose `match`, `when`, constructors, typed patterns],
  [P0], [Function values], [Callbacks, `List.map`, handlers, actor bodies], [`logic (x y) = body close` implemented; single-line bodies use the sentence period],
  [P0], [Nominal constructors with named fields], [Records, DUs, options, results, tuples, lists], [`shape` declarations, construction, and patterns implemented; named-pair sugar and cross-book visibility deferred (`docs/language/action/nominal-constructors.typ`)],
  [P0], [Local binding], [Intermediate values without deep nesting], [`let name = value in body` implemented (`docs/language/action/local-binding.typ`); `let rec`, `and`, and pattern bindings deferred],
  [P0], [Real numbers], [Prelude arithmetic and benchmarks], [Prose number literals with an optional fraction implemented (`docs/language/action/real-numbers.typ`); exponent and leading-dot forms deferred],
  [P0], [Multiple dispatch], [Polymorphism, interfaces, overloads, class selection], [`generic`/`method` with shape specializers implemented (`docs/language/action/multiple-dispatch.typ`); class specializers and cross-book methods deferred],
  [P1], [Conditions and restarts], [Error recovery without unwinding], [Prelude condition words],
  [P1], [Actor and channel calls], [Scheduler, mailboxes, supervision], [Prelude host/runtime words],
  [P1], [Mutable state], [Prelude caches and counters], [Class slots or actor state; no local mutation],
  [P1], [Classes and slots], [Identity, slot mutation, inheritance, precedence], [Prelude class system],
  [P2], [Ownership and linearity], [Borrow/consume checks], [Prelude ownership words],
  [P2], [Runtime guards], [Guarded execution], [Prelude runtime-guard words],
  [P2], [History and bookmarks], [Rollback and diagnostics], [Prelude history/bookmark words],
  [P2], [Collection libraries], [`List`, `Array`, `Map`, `Set`, `Option`, `Result`], [Constructors plus words; no literals yet],
)

== The P0 features in detail

=== Function values

A quotation or function expression is required for higher-order functions,
callbacks, handlers, and actor bodies. Books provide named functions; a function
value provides an anonymous function with lexical capture.

Unique benefit: a local closure can be passed without exporting a named book
word. Overlap: books already provide named functions; the unique part is
anonymous lexical capture, not naming.

=== Nominal constructors with named fields

The Prelude is built from records and discriminated unions. The surface needs
one declaration form that names a constructor and its fields, and one pattern
form that matches it. This covers records, DUs, options, results, tuples, and
cons cells.

Unique benefit: immutable structural data with exhaustive matching. Overlap:
classes provide identity and mutable slots; they are not a substitute for
structural values. Sets describe membership and classification; they are not a
constructor. Do not add separate record, tuple, option, result, and list
declarations before this one is exhausted.

=== Local binding

A body needs to name intermediate values. The Prelude uses local `let`
everywhere; a port without it produces deeply nested calls and duplicated
arguments. The surface is `let name = value in body`, an expression usable in
book bodies and prose sentences. Contract: `docs/language/action/local-binding.typ`.

Unique benefit: lexical, non-exported names. Overlap: books provide global
named definitions; the unique part is local scope and no module entry.

=== Real numbers

The Prelude's arithmetic and benchmarks use floating point. Prose previously
accepted integer literals only. One literal form covers both: an optionally
signed base-10 number with an optional fractional part, parsed as a 64-bit
float. There is no separate integer and real arithmetic. Contract:
`docs/language/action/real-numbers.typ`.

=== Multiple dispatch

Multiple dispatch is the widest feature in the set. The book surface is
`generic name with arity.` plus `method name(p : shape) = body.`, selecting the
method whose shape vector matches the arguments; it compiles to a synthesized
dispatcher definition and ordinary lowered method definitions. Contract:
`docs/language/action/multiple-dispatch.typ`. It covers:

+ ad-hoc overloading;
+ interfaces and protocols;
+ record and DU selection;
+ open extension by later methods in one book;
+ before/after/around behavior (core words; the book surface is deferred).

Unique benefit: behavior is selected by the runtime types of arguments and can
be extended without changing the caller or the data declaration. Overlap:
pattern matching selects a branch inside one function and is closed at the
definition site. Dispatch selects a method across a generic function and is
open. Keep both; do not add a separate interface or type-class feature.

== Deliberately not separate features

+ #strong[`if`] is `match` with two choices.
+ #strong[Loops] are `cycle` recursion; no `while`, `for`, or loop syntax.
+ #strong[Namespaces] are books and `open`; no second module system.
+ #strong[Interfaces] are multiple dispatch; no separate interface declaration.
+ #strong[Exceptions] are conditions and restarts; no separate `try`/`throw`
  syntax unless a port test proves the condition words insufficient.
+ #strong[Record, tuple, option, result, and list declarations] are nominal
  constructors with named fields; no separate declaration forms.
+ #strong[Collection literals] are constructor-backed words; add literal syntax
  only after the word representation is proven and the ergonomic gain is real.
+ #strong[Actor and channel syntax] is host words plus function values; no
  `spawn`/`send` grammar.
+ #strong[Ownership, guards, and history syntax] are words; they are not new
  sentence forms.

== Overlap decisions

#table(
  columns: (auto, 1.6fr, 1.6fr, 2fr),
  align: (left, left, left, left),
  table.header([Pair], [Unique benefit A], [Unique benefit B], [Decision]),
  table.hline(),
  [Match vs dispatch], [Closed decomposition of one value], [Open behavior selected by argument types], [Both; match is local, dispatch is extensible],
  [Constructors vs classes], [Immutable structural values and exhaustive patterns], [Identity, slots, mutation, inheritance], [Constructors first; classes only for identity/mutation],
  [Books vs function values], [Named, exported, recursive groups], [Anonymous, lexical capture, local], [Both; no overlap in scope or export],
  [Books vs namespaces], [Module, package, imports, recursion], [Qualified name grouping], [Books only],
  [Sets vs constructors], [Membership and classification], [Structural tagged data], [Both; sets are not a data declaration],
  [Conditions vs results], [Signalling and restart protocols], [Ordinary value return], [Both; conditions carry recovery, results carry success/failure],
  [Actors vs classes], [Concurrent state and message boundaries], [Object identity and slot layout], [Actors for state; classes only if inheritance is required],
  [Collection words vs literals], [Library behavior and representation], [Surface convenience], [Words first; literals only with measured need],
)

== Port sequence

1. Function values, local binding, real numbers, and constructors with named
   fields.
2. Multiple dispatch, then the generic/method-based parts of the Prelude.
3. Conditions and restarts; then actor and channel calls.
4. Classes only if the port still needs identity, slot mutation, or inheritance
   after constructors, dispatch, and actors.
5. Ownership, runtime guards, history, and collection libraries as words.

== Acceptance for the port

The first proof is implemented and tested: one book declares a record
(`shape point with x y.`), a generic with a method (`area`), a recursive
function (`cycle countdown`), and a higher-order call
(`applytwice(logic (x) = add(x 1) close 3)`); it runs through `cast` and
returns 36 for the combined record/dispatch/recursion value and 5 for the
higher-order value, with no host-side special case.

The second proof is also implemented: a book replaces the pure core of
`DecisionTable.select` (rows of guards, hit policies, selection and ambiguity)
using cons-cell lists, shapes, recursion, and match. It is exercised through
`cast` against the F\# behavior: first-wins, unique, no-match, overlap
ambiguity, and priority selection all agree.

The third proof is a whole collection module in a book: cons cells plus
`length`, `head`, `map`, `filter`, `fold`, `append`, `reverse`, `contains`,
`insert`, and insertion `sort`, with anonymous functions passed to the
higher-order words. It is exercised through `cast` with numeric lists; every
result agrees with the F\# behavior.

The fourth proof ports the whole `PatternAnalysis` module: pattern
subsumption, unreachable-clause analysis, and top-level exhaustiveness over
closed and open kinds, including a mutually recursive cycle group. It is
exercised through `cast`; every reachability and exhaustiveness result agrees
with the F\# module.

== Non-goals

+ Reproducing F\# syntax or the .NET BCL.
+ A second type system beside dispatch and typed patterns.
+ Separate syntax for every collection type.
+ New effect or concurrency syntax before the word representation is proven.
+ Porting the compiler, writer, kernel, or host adapters into Prose.
