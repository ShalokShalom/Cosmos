= Question surface

#strong[Status: parser and first evaluator slice implemented; query plan is next.]
Question is the asking surface of Scale: facts and rules are clauses, and a
question is a goal list. Its syntax is symbol-oriented; the term shape is the
shared `PatternLogic.Pattern`, so Question does not add a second term model.

== Syntax

```text
parent(alice, bob).
ancestor(?x, ?y) :- parent(?x, ?y).
?- ancestor(alice, ?who).
```

+ A fact is a head followed by `.`.
+ A rule is `head :- goal, goal.`
+ A question is `?- goal, goal.`
+ `?name` is a variable; `_` is a wildcard.
+ Atoms are lowercase names; compounds use `name(term, term)`; numbers and
  text literals use the shared literal forms.
+ `%` starts a line comment.
+ The question, when present, is the last item.

The compact symbol set is deliberate: `:-`, `?-`, `,`, `.`, `?`, and `_` carry
the structure; relation names carry the domain vocabulary. The word `query` is
not part of this surface; it remains available for a future keyword.

== Semantics

A clause is a Horn clause: the head is proved by the body. Facts have an empty
body. A question asks for substitutions that satisfy every goal, left to right.
The future evaluator uses first-order unification over `PatternLogic.Pattern`
with an occurs check; clause order is observable, and failure is structural.
Question has no host effects.

The question program is not a second definition language. Reusable definitions
remain in Logic; Question clauses are data/relations consumed by the question
surface.

== Question and Logic

Question semantics depend on the shared term model and the unifier, not on Logic
functional definitions. A question program with its own facts and rules can stand
alone; it needs no lambdas, closures, or functional definitions. Logic may expose
a predicate or a ground helper, but Question must not silently call an arbitrary
functional definition with unbound variables.

Curry-style functional logic would make functions callable with free variables
through narrowing, with non-deterministic search. That is a different evaluation
model. Scale keeps Question first-order and Logic deterministic, and combines them
through explicit predicates or ground calls. The optional extension is a
typed predicate adapter, not a narrowing evaluator inside Logic.

== Pragmatics

Question is for asking: database lookup, rule-based inference, and binding
discovery. It must not perform host effects or become a module system. A reader
should be able to see the relation names and the symbols, not a long keyword
sentence.

== Refusals

The first slice refuses an empty program, a missing term, an unclosed compound,
a missing period, an empty goal, a duplicate question, a clause after the question,
a missing variable name, unclosed text, invalid numbers, and unexpected
characters. Every refusal carries an offset.

== Next

The question plan, result ordering controls, and a stable public result shape are
the next vertical slice. Negation, cut, and host effects are not part of the
initial contract.
