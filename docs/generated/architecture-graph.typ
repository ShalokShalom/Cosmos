// Generated from docs/architecture/architecture.edn. Do not edit.
#table(
  columns: (auto, auto, auto, 1fr),
  align: (left, left, left, left),
  table.header([From], [Kind], [To], [Rationale]),
  [#raw("expression-logic")], [#raw("embeds")], [#raw("pattern-logic")], [PatternMatch clauses carry PatternLogic patterns.],
  [#raw("expression-logic")], [#raw("lowers-to")], [#raw("lowered-abstraction")], [EvaluatorLowering maps the expression subset to instructions.],
  [#raw("package-logic")], [#raw("contains")], [#raw("expression-logic")], [A PackageUnit root is an ExpressionLogic node.],
  [#raw("expression-logic")], [#raw("serializes-to")], [#raw("portable-documents")], [ExpressionPortable writes the canonical expression document.],
  [#raw("pattern-logic")], [#raw("serializes-to")], [#raw("portable-documents")], [PatternPortable writes the canonical pattern document.],
  [#raw("package-logic")], [#raw("serializes-to")], [#raw("portable-documents")], [PackagePortable writes the canonical package document.],
  [#raw("expression-logic")], [#raw("inferred-by")], [#raw("type-retrieval")], [TypeInference elaborates against the signature table.],
  [#raw("lowered-abstraction")], [#raw("executes-on")], [#raw("image-values")], [The VM reads and writes the image store.],
  [#raw("symbolic-abstraction")], [#raw("executes-on")], [#raw("image-values")], [The interpreter reads and writes the image store.],
  [#raw("domain-expression")], [#raw("projects-to")], [#raw("image-values")], [Verification expressions project into the image value model.],
)

```text
expression-logic -> pattern-logic : embeds
expression-logic -> lowered-abstraction : lowers-to
package-logic -> expression-logic : contains
expression-logic -> portable-documents : serializes-to
pattern-logic -> portable-documents : serializes-to
package-logic -> portable-documents : serializes-to
expression-logic -> type-retrieval : inferred-by
lowered-abstraction -> image-values : executes-on
symbolic-abstraction -> image-values : executes-on
domain-expression -> image-values : projects-to
```
