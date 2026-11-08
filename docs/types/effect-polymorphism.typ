= Effect polymorphism

#strong[Status: implemented for the host inference loop.] Function effects are
open set expressions, a scheme quantifies both type variables and set
variables, and a call adds a block's latent effects through the set unifier.

== Contract

+ An #strong[effect set] is a closed finite set of labels or a known set with
  one nominal remainder: `Closed labels` or `Open(known, SetVariable)`.
+ A function type carries its effect set. The set is the function's own
  requirement, not a property of its arguments.
+ A #strong[scheme] quantifies type variables and set variables.
  `generalize` collects free variables not free in the environment;
  `instantiate` replaces each with a fresh variable. Type variables and set
  variables have separate namespaces.
+ #strong[Function unification] unifies parameters, then the result, then the
  effect sets. An open effect set binds to a concrete set when the set
  variable is free.
+ #strong[Contextual effects] are added at the call. Evaluating a function
  literal has no effects; the literal's body effects live in its function
  type. Unifying that type with an effect-polymorphic parameter binds the
  parameter's set variable, so the call's effect set includes the block's
  latent effects exactly once.
+ #strong[Union] is stateful. Closed/closed unions are plain set unions;
  open/closed keeps the variable; two different open sets allocate one fresh
  unknown and bind each original variable to the other's labels plus that
  unknown. The result is the exact union of the two sets.
+ #strong[Refusals] are named: `EffectMismatch` for a non-subset or unequal
  closed set, and `EffectOccursCheck` for a cyclic effect variable.

== What an author can do

+ Write a higher-order function whose effect set depends on the block it
  receives, without annotating the block.
+ Pass a block with effects to a parameter whose effect set is open; the
  caller's effect set records the block's effects.
+ Use a function whose effects are polymorphic in a context that supplies
  concrete effects.
+ Keep effect labels unordered and duplicate-free; set idempotence does the
  work that a lacks constraint would do for rows.

== Implementation

+ `components/verification/typing/type-retrieval.fs`:
  `EffectSet = LabelSets.LabelSet<Effect>`, `Scheme.QuantifiedEffects`,
  `WordSignature.QuantifiedEffects`, `generalizeWith`, `freeEffectVariables`,
  `instantiate`, `unifyWith`, `unifyEffectSets`, `unionEffects`,
  `EffectSubstitution` in `InferenceState`.
+ A word signature can carry an open effect set and quantify its set variables;
  `wordScheme` preserves them and `instantiate` freshens them.
+ `components/verification/typing/label-sets.fs` remains the set engine.
+ `components/verification/typing/unification.fs` is the reference model; the
  host loop now uses the same open set shape.
+ The self-hosted unifier fixture covers the same set rules.

== Limits

+ Word signatures can quantify effects, and the builtin registry has both closed
  and open effect entries: `restart` takes a `Code` whose effect set is open, so
  an open entry is supported and tested.
+ A generalized scheme retains lacks constraints on quantified effect and row
+ variables, and `instantiateWithLacks` restores them on fresh variables.
+ Closed and open effect subtraction are implemented. Open subtraction records a
+ lacks constraint; binding a label the remainder lacks is refused. Effect rows
+ are represented by open label sets (`TypeRetrieval.EffectRow`); there is no
+ second effect representation. Typed handlers are not implemented.
+ No second-class functions.
+ No effect rows or subtype constraints; effects are sets.
+ No user-facing effect annotation syntax.

== Tests

Eight checks cover:

+ a scheme quantifies a free effect variable;
+ instantiation freshens the set variable;
+ the counter advances past it;
+ an open effect set binds to concrete effects;
+ a block's latent effect propagates through an effect-polymorphic parameter
  into the call's effect set;
+ a word signature preserves its quantified effect variable;
+ instantiation freshens a word signature's effect variable;
+ a word call can infer an open effect set.
