# Scale typing and emission

The expression type inference and its source-map diagnostics, the domain AST,
the type lattice, SMT, violations, nearest-sentence analysis, presentation, and
the F# emitter. This is the typing/emission foundation the evaluator's runtime
guard needs; it is a component, not a test layer.

`Scale.Typing.fsproj`; depends on `components/core`.
