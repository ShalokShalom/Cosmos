= Question evaluator

#strong[Status: first evaluator slice.] `QuestionEngine.solve` evaluates a
parsed `QuestionProgram` with first-order unification. It does not execute
host effects and does not call Logic functional definitions with unbound
variables.

== Inputs

```fsharp
solve
    (maxSteps: int)
    (maxAnswers: int)
    (program: QuestionProgram)
    : Result<QuestionResult, QuestionError>
```

`QuestionProgram.Clauses` are facts (empty body) and rules. `Question` is the
optional goal list. A program with no question is refused with `NoQuestion`.

== Terms and substitution

The term model is the shared `PatternLogic.Pattern`:

- `Variable(name, Fresh)` is a logic variable;
- `Wildcard` never binds;
- numbers, text, atoms, constructors, and typed patterns unify structurally;
- other variable roles are refused rather than guessed.

Unification walks the current substitution, binds an unbound variable to the
other term, and refuses a binding that would make the variable occur inside its
own value. The occurs check is a normal failure, not an exception.

== Search

Goals are solved left to right. Clauses are tried in source order. A rule
invocation is freshened with a unique suffix, so the same rule can be used
recursively without sharing caller variables. Answers are returned in the
search order produced by this strategy.

A `maxSteps` limit bounds unification/resolution steps. A `maxAnswers` limit
bounds returned answers and sets `Truncated` when reached. Neither limit is a
silent success: the result carries the counts.

== Results

`QuestionResult` contains:

- `Answers`: one binding map per solution, keyed by the question's variable
  names;
- `Steps`: the number of resolution steps used;
- `Truncated`: whether the answer limit stopped the search.

An unbound variable is omitted from an answer map. A question with no variables
has one empty answer map when its goals succeed.

== Refusals

- `NoQuestion`: the program has no question.
- `StepLimitExceeded`: the step budget ran out.
- `UnsupportedVariableRole`: a non-fresh variable reached the engine.

No host effects, negation, cut, constraints, or tabled resolution are part of
this slice. Curry-style narrowing is deliberately excluded: Logic stays
deterministic, and Question never calls arbitrary functions with free variables.
