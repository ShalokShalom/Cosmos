= #text(fill: rgb("#cba6f7"))[Lia] --- Question language

#strong[Status: parser and first-order evaluator implemented.] Question is the
asking part of Scale. It has facts, rules, goals, unification variables, search,
and explicit limits. It performs no host effects and does not depend on Logic
functional definitions.

== At a glance

```text
parent(alice, bob).
ancestor(?x, ?y) :- parent(?x, ?y).
?- ancestor(alice, ?who).
```

The symbols carry the structure: `?-`, `:-`, `,`, `.`, `?`, `_`, and `%`
comments. Relation names carry the domain vocabulary. `query` is not a keyword;
it remains available for a future surface keyword.

== Functionality

#table(
  columns: (auto, 1fr),
  align: (left, left),
  table.header([Form], [Meaning]),
  [`fact.`], [A clause with no body.],
  [`head :- goal, goal.`], [A Horn rule; goals run left to right.],
  [`?- goal, goal.`], [A question; it must be the last item.],
  [`?name`], [A logic variable.],
  [`_`], [A wildcard that never binds.],
  [`name(term, term)`], [A compound term.],
  [`% comment`], [A line comment.],
)

Current evaluator example:

```text
parent(a,b). parent(b,c). parent(c,d).
ancestor(?x,?y) :- parent(?x,?y).
ancestor(?x,?y) :- parent(?x,?z), ancestor(?z,?y).
?- ancestor(a,?who).

answers = 3, steps = 60, who = b, c, d
```

== Semantics

- First-order unification over the shared `PatternLogic.Pattern`.
- Occurs check; a cyclic binding is a normal failure, not an exception.
- Clause order is source order; goals run left to right.
- Every rule invocation is freshened, so recursive rules do not capture caller
  variables.
- A wildcard never binds.
- `maxSteps` bounds resolution; `maxAnswers` bounds returned answers and sets
  `Truncated`.
- No host effects, negation, cut, constraints, or tabling in this slice.
- Curry-style narrowing is deliberately excluded.

== Implementation (#text(fill: rgb("#f9e2af"))[yellow])

- `components/core/question-logic.fs` — clause and program types.
- `components/core/question-parser.fs` — symbolic parser and refusals.
- `components/core/question-engine.fs` — substitution, occurs check, rule
  freshening, search, and result limits.

== Specification and documents

- `docs/specification.typ` — normative language contract.
- `docs/language/question/question-surface.typ` — syntax and grammar.
- `docs/language/question/question-evaluator.typ` — evaluator API, semantics,
  limits, and refusals.
- `docs/quests/quest-question-engine.typ` — engine quest, options, and
  acceptance.
- `docs/language/semiotics.typ` — layer model and trust gradient.

== Tests

- `components/verification/tests/tests.fs` — parser, facts, rules, recursion,
  wildcard, occurs check, limits, and no-question refusal.
- The tests keep Question independent of Logic and Action.

== Limits and current gaps

- No Action `ask` bridge yet.
- No question plan, result-order control, negation, cut, constraints, or tabled
  resolution.
- Bindings are currently a `Map<string, Pattern>`; ordered and explicit-unbound
  result shape is still a contract decision.
- Query/Question has no host effects; predicate adapters are future work and
  must be ground-only.

== Related

- Action: `docs/language/action/README.typ`.
- Shell: `docs/language/shell-layer.typ`.
- Action–Question adapter research: `Notes/action-question-adapter.md`.
