= Quest: Question engine

#strong[Status: documentation and first evaluator slice landed.] The parser and
the first-order evaluator are implemented. This Quest keeps Question first-order,
deterministic in clause order, and independent of Logic functional definitions.

== Contract

A question program is a list of facts and rules followed by an optional goal
list. The engine:

- unifies goals against clause heads from left to right;
- preserves clause order and returns answers in search order;
- freshens every clause invocation so recursive rules cannot capture caller
  variables;
- performs an occurs check;
- treats a wildcard as non-binding;
- has explicit step and answer limits;
- performs no host effects and does not call Logic functions with unbound
  variables.

== Options

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Option], [Benefit], [Cost/decision]),
  [Depth-first, clause order],
  [Simple, predictable, matches Prolog-like expectations],
  [Can loop; the step limit turns that into a named refusal. Chosen for the first slice.],
  [Tabled resolution],
  [Terminates more recursive queries],
  [Larger engine and different answer ordering; deferred.],
  [Curry-style narrowing],
  [Functions callable with free variables],
  [Non-determinism leaks into Action/Logic; rejected.],
)

== Acceptance

- Fact, rule, recursive rule, wildcard, repeated variable, and occurs-check
  tests pass.
- A recursive rule with the same variable names in two invocations returns the
  correct bindings.
- Step and answer limits are observable.
- Query without clauses returns no answers, not an execution error.
- Questions remain independent of Logic definitions.

== Non-goals

Negation, cut, constraints, tabled resolution, host effects, and arbitrary
Logic calls with unbound variables.
