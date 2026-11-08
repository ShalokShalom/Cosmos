= Types in Scale: one map

Scale uses several type-like notions. They are not interchangeable and they do
not share a representation. This page is the cross-reference; the detailed
contract for each remains in its own document.

#table(
  columns: (1.2fr, 2.2fr, 1.5fr, 1.6fr, 2.4fr),
  align: (left, left, left, left, left),
  table.header(
    [Notion],
    [Representation],
    [Authority],
    [Consumers],
    [Boundary],
  ),
  [Expression inference (P4)],
    [`TypeRetrieval.Type`: variables, primitives, sorts, functions with latent effects; schemes and constraints],
    [`components/verification/typing/type-retrieval.fs`],
    [Expression elaborator, `--prose-typecheck`, the lowering handoff],
    [Host-side and compile-time. It consumes `ExpressionLogic.Expression`, not the image or runtime terms. Exact effect sets; no rows, subtyping, or higher-order type unification.],
  [Verification lattice (D25)],
    [`TypeLattice.Lattice`: named fields, field types, finite coordinates or symbolic domains, narrowing guards],
    [`components/verification/typing/type-lattice.fs`],
    [Z3 encoding, Hedgehog generators, the refiner],
    [A projection of a `.scale` domain, not a second source of truth. It is not the runtime set-theoretic sort record; bridging them is a separate decision.],
  [Runtime sorts and value sets],
    [Image-resident `ValueSet`: an optional name plus an extension; declared domains live in `DeclarationsRoot`],
    [`components/prelude/evaluator/value-sets.fs` and the evaluator's set words],
    [Evaluator, set operations, dispatch, sort constructors],
    [Runtime data. The image is the authority; lookup is latest-write-wins and a declared domain only grows. It is not a static type.],
  [Document field types],
    [`ScaleFile.FieldType` and `DomainExpression.FieldType`: boolean, integer, natural, real],
    [`components/core/scale-file.fs` and `components/verification/typing/domain-expression.fs`],
    [The `.scale` parser, the verifier, the F\# emitter, the lattice],
    [The editable specification (D7/D9). It is the source the lattice projects; it is not a runtime sort.],
  [Term types],
    [`SymbolicAbstraction.Term` and the portable term document],
    [`components/kernel/symbolic-abstraction.fs` and `components/kernel/term-portable.fs`],
    [The kernel bridge, the term unifier, the structural verifier],
    [A representation of code as data, not a static type of a value. The portable document is versioned separately. D202 retires this form and its document in W1c; until then it is still the interpreter's program.],
)

== Rule of thumb

+ A fact about a word's call shape at elaboration time is #strong[expression
  inference].
+ A fact about the values a domain field may take is the #strong[verification
  lattice].
+ A fact about the declared sort of a runtime value is the #strong[image-resident
  sort record].
+ A fact declared in a `.scale` document is a #strong[document field type].
+ A fact about code as data is a #strong[term].

None of these converts into another implicitly. A feature that needs a bridge
states it in a decision record and names the direction of the conversion.

== See also

- `docs/types/type-inference.typ` — the P4 contract and the single word index.
- `docs/types/type-lattice.typ` — the verification coordinate system.
- `docs/types/set-theoretic-typing.typ` — the structural/nominal dial and the runtime
  sort record.
- `docs/runtime/cell-system.typ` — slots, classes, and instances.
- `docs/architecture/term-ir.typ` — code as data and the portable term document.
