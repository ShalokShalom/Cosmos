// Generated from docs/architecture/architecture.edn. Do not edit.
#table(
  columns: (auto, auto, 1fr, auto),
  align: (left, left, left, left),
  table.header([Canonical], [Code], [Role], [Documentation]),
  [#raw("ExpressionLogic")], [#raw("ExpressionAst.Expression")], [General expression abstraction: literals, words, calls, partials, holes, pipelines, quotations, blocks, let, lambda, if, match, declarations, conditions, restarts, generics, messages.], [#raw("docs/architecture/general-ir.typ")],
  [#raw("PatternLogic")], [#raw("PatternAst.Pattern")], [Pattern abstraction: wildcard, role-carrying variables, literals, constructors, typed patterns; templates elaborate to closed patterns.], [#raw("docs/architecture/general-ir.typ")],
  [#raw("PackageLogic")], [#raw("PackageAst.PackageDocument")], [Package and unit abstraction: identity, source hash, imports, exports, capabilities, kinds, predicates, ordered units.], [#raw("docs/architecture/general-ir.typ")],
  [#raw("SymbolicAbstraction")], [#raw("TermIr.Term")], [Interpreter's symbolic term list and quotation body: literal, word, quote, branch, slot, closure, fail.], [#raw("docs/architecture/term-ir.typ")],
  [#raw("LoweredAbstraction")], [#raw("LoweredInstructions.Instruction + LoweredAbstraction")], [Lowered instruction abstraction and VM: literals, slots, calls, closures, tensor operations, branches, failure.], [#raw("docs/kernel/native-dotnet-port.typ")],
  [#raw("TypeRetrieval")], [#raw("TypeInference")], [Retrieves and elaborates types against a signature table; not a type checker.], [#raw("docs/types/type-inference.typ")],
  [#raw("Portable documents")], [#raw("TermProgram, ExpressionDocument, PatternDocument, PackageDocument")], [Canonical schema-1 JSON forms used at the image, ABI, persistence, and package boundaries.], [#raw("docs/architecture/general-ir.typ")],
  [#raw("DomainExpression and SMT")], [#raw("DomainExpression, Smt")], [Verification abstraction: typed domain expressions, sorts, guards, obligations, and SMT encodings.], [#raw("docs/types/type-lattice.typ")],
  [#raw("Image values")], [#raw("Tensor, EntryIdentity, Store, Lexicon")], [Runtime value model; not code, but the representation every abstraction lowers into.], [#raw("docs/runtime/memory-model.typ")],
  [#raw("Execution state")], [#raw("KernelState, EvaluationState")], [State, not code: store, stack, program, roots, frames, scheduler, and effects.], [#raw("docs/runtime/evaluator-slicing-and-scheduler.typ")],
)
