= Effect handlers

#strong[Status: effect subtraction and a typed `with-handler` rule are landed; the general typed handler surface remains deferred.]

A handler removes a named effect from the body it handles and adds its own
effects. The inference rule for the existing shallow one-shot handler subtracts
the `failure` effect from its body quotation.

== Effect subtraction

+ `LabelSets.subtractClosed` removes known labels from a closed effect set.
+ `TypeRetrieval.subtractEffect` exposes the same operation to inference.
+ `LabelSets.subtractOpen` and `TypeRetrieval.subtractEffectWithLacks` remove a
+ label from an open set and add a lacks constraint to its remainder.
+ `TypeRetrieval.EffectRow` is the explicit row reading: effect rows are
+ represented by open label sets, not by a second effect representation.
+ `TypeRetrieval.handleEffects` removes the handled label, unions the handler's
+ effects, and threads the lacks environment.
+ Quotations have a `Code(result, effects)` type. The typed rule is a fixed-arity
  AST special case that accepts two code values, so it is no longer limited to
  literal quotations; `with-handler` is `NotInferred` in the word registry, which
  is what makes a general handler expression deferred rather than absent.
+ `TypeRetrieval.infer` consumes the open rule for
+ `with-handler(quotation, quotation)`: it subtracts `failure` from the body and
+ adds the handler's effects.
+ A closed set loses the label: `{io, net} - {io} = {net}`.
+ An open set becomes `Open(known - removed, variable)` with `variable` lacking
+ the removed labels; binding a set that contains a lacked label is refused.
+ The refusal is named; no silent under- or over-approximation.

== Existing runtime handler

The evaluator already has a shallow one-shot handler:

+ `body handler with-handler` runs the body quotation; a failure becomes an
  error condition and the handler quotation runs once with that condition.
+ `condition signal` raises a condition; with a body-phase handler it switches
  to the handler, with no handler it leaves the condition as a value.
+ The handler frame is execution state and is never serialized.
+ There is no resume, no multi-shot continuation, and no explicit effect label
+ on the frame.

This is the runtime foundation for the typed handler surface. The typed rule
currently covers only this shallow one-shot form.

== Limits

+ No general typed handler expression and no effect-label matching in the type
+ system.
+ No resume or multi-shot continuation.
+ Open subtraction is consumed only by the `with-handler` rule.
+ No second-class blocks; the existing handler takes quotations.
+ No handler polymorphism.

== Tests

+ A known label is removed from a closed effect set.
+ Open subtraction records a lacks constraint and refuses a later binding that
+ contains the removed label.
+ A typed `with-handler` removes `failure` from its body and adds the handler's
+ effects.
