# Effects First or Sets All the Way Down?

*A dialogue on the shape of Scale's type system.*

**Mira** works on effect systems and program calculi. She supports the effects-only design: closed effect sets, contextual polymorphism, and handlers.

**Jonas** works on type systems and data modeling. He supports the hybrid: closed set-theoretic records plus the same effect sets and contextual polymorphism.

They meet after a design review in which someone proposed ordered row types. The proposal was withdrawn because it introduced a second type system next to Scale's set theory.

---

**Mira.** The effect system is the part that changes what a program means. Records are data; effects are behavior. If we get effects right, the rest is bookkeeping. Effekt's lesson is precise: effect types are not a list of side effects, they are the capabilities a computation requires from its context. A handler subtracts the effect it handles. That is a set operation, not a row unification. We should start there.

**Jonas.** I agree about effects, but I do not accept that records are bookkeeping. Right now the type checker treats a constructed shape as unit. We have `shape point with x y`, we construct `point(1 2)`, and the type system has nothing to say about it. That is a hole in the data model. A type system that cannot type its own records is incomplete. And records are sets too: a record is a finite set of labeled fields. The same set theory covers both.

**Mira.** It covers both only if you make records closed. The moment you want a function over any record containing `x`, you need an open set or a row. Open sets mean set variables and AC unification; rows mean a second type system. You yourself rejected rows. So the honest record story is closed records, and closed records buy you less than they cost. Shapes are already closed declarations. Pattern matching already decomposes them. What does a record type add that a shape declaration does not already give us?

**Jonas.** It gives the checker a type for construction and pattern matching. Today `Message("point", ...)` is inferred as unit. That means a function that expects a point and receives a circle type-checks. Closed record types fix that without any runtime, without handlers, and without rows. A record type is a set of field names and field types; equality is set equality; a function that accepts a point accepts exactly that set. No subtyping, no variables. It is the smallest useful data-model improvement we can make.

**Mira.** And when someone wants width polymorphism — “any record with an `x` field” — you either say no, or you reopen the set-variable question. I would rather say no explicitly than pretend the closed record model is complete.

**Jonas.** I am comfortable saying no explicitly. Closed records are the first slice. Open sets are a separate decision with a concrete consumer. I am not smuggling rows back in. The set-theoretic representation is the point: a record is a set, an effect is a set, and both use the same label-set engine. One story, not two.

**Mira.** Then we agree on the representation and disagree on sequence. Let me make the case for effects first. Handlers are the hard part. They need control flow, resumption, capability passing, and a story for multi-shot versus one-shot resumption. If we defer handlers, effect sets are just annotations that tell you what still needs handling. Useful, but not the differentiator. If we do handlers first, the type system earns its keep immediately.

**Jonas.** Handlers are hard precisely because they are a runtime and control-flow feature. Records are data. They can land before handlers without blocking anything. In fact, records make the effect work easier: a handler's state is often a record, and a typed record is a better carrier than an untyped shape. I would land records first, then effect sets, then handlers.

**Mira.** That order puts the easiest thing first and the thing that changes the language last. It also changes `Type`, `Scheme`, `WordSignature`, and shape inference before we know whether the effect design works. If the effect design forces a different representation, we rework the record code. Effects first is smaller: closed effect sets, contextual polymorphism, and a handler slice. If the handler slice fails, we still have a coherent effect system. If the record slice fails, we have changed the type system for nothing.

**Jonas.** The effect slice is not smaller once you include handlers. Contextual polymorphism is subtle. It says that a function's effect type describes its own requirements, and the effects of an argument block are not part of the function's effect type. That is a different reading of function types. It interacts with first-class functions, with closures, and with the existing `EffectSet` in `TypeRetrieval`. It is not a local change.

**Mira.** It is smaller than records plus effects plus handlers. And it is the part with the strongest external precedent: Effekt has a formal calculus, a capability-passing translation, and a proof of effect safety. Records have precedent too, but the closed-record subset is not where the interesting theory is.

**Jonas.** Precedent is not a sequence. Let me put the strongest version of my case. Scale's identity is set-theoretic typing. If we add effect sets and leave records untyped, we have a set-theoretic effect system bolted onto a type system that still treats data as opaque. That is not one type system. It is an effect annotation layer next to a partial type checker. The hybrid is the only design that makes the whole story set-theoretic: records as closed sets, effects as closed sets, contextual polymorphism for effects, and no rows anywhere.

**Mira.** I accept the destination. I reject the claim that records must come first. And I want to name the fork you keep avoiding: full Effekt safety requires second-class functions. Effekt separates functions from values and treats all functions as second-class so that capabilities cannot leak. Scale has first-class function values. If we adopt Effekt-style effects but keep first-class functions, we are Effekt-inspired, not Effekt-identical. That is a bigger decision than records versus effects.

**Jonas.** I agree it is the bigger decision. I do not think the hybrid forces an answer. We can keep first-class functions and accept a weaker guarantee, or move to second-class functions and get the full guarantee. Either way, the record model is independent. A record type does not care whether functions are first-class.

**Mira.** It cares in one way: a record of closures is exactly the smell we removed from `DomainBinding`. If functions are first-class and records are sets of fields, a record can contain functions. That is fine for data, but it complicates effect safety: a closure can capture a capability. Effekt avoids that by making functions second-class. Our hybrid would need a capability discipline for function values, or an explicit restriction.

**Jonas.** Then we write the restriction down. A record may contain function values, but a function value may not capture an unhandled capability unless its type says so. That is a contract, not a row type. We can enforce it with the effect system once we have capabilities. It does not block closed records.

**Mira.** It does not block them, but it means the record slice is not as independent as you claim. The type of a function-valued field has an effect set. The record type must carry it. That is fine; it is still a set of fields.

**Jonas.** Good. Let us separate the decisions:

1. Records are closed sets of labeled fields. No row variables, no subtyping in the first slice.
2. Effects are closed sets of operation names. Contextual polymorphism. Handlers subtract the handled effect.
3. Function values are first-class unless we explicitly decide otherwise. If first-class, effect safety is weaker and must be stated.
4. Open set variables are a separate decision with a consumer. No AC unification by default.
5. No ordered row types. They are a second type system.

**Mira.** I can live with that, but I insist on the sequence: effect sets and contextual polymorphism first, then handlers, then records. The effect system is the reason to do this at all.

**Jonas.** And I insist records are not optional. If we do effects first and stop, we have a partial type system. If we do records first, the effect work inherits a typed data model. I propose a different sequence: effect sets and closed records together, as one set-theoretic slice, then handlers. That avoids doing the `Type`/`Scheme` change twice.

**Mira.** That is a bigger first slice. What is the realistic chance it lands?

**Jonas.** The record/effect core is a bounded change: `Type` gains a record case; `EffectSet` stays a set; contextual polymorphism is a typing rule; handlers are deferred. I would put the core at 60–70 percent. Handlers are the uncertain part, 40–60 percent. If we try to do all three at once, the chance drops.

**Mira.** My estimate for effects-only is 70 percent or better for the effect-set and contextual-polymorphism core, and 40–60 for handlers. The record addition is another 60–70 on top. The difference is not the destination; it is where the risk sits.

**Jonas.** Then the disagreement is about risk allocation, not about the model. You want the risky, high-value part first. I want the low-risk, structural part first, so the risky part lands on a complete data model.

**Mira.** There is a compromise. Do the effect-set and contextual-polymorphism core first, because it is the semantic heart. Add closed records immediately after, before handlers. Handlers last. That gives you the complete data model before the runtime risk, and it gives me the effect semantics before the record plumbing.

**Jonas.** I can accept that, with one condition: the record design is fixed before the effect work starts. We agree on closed sets, no rows, no open variables, and a record is a set of labeled fields. Then the effect slice cannot accidentally force a different data model.

**Mira.** Agreed. And we decide the second-class-function question explicitly before handlers, not after. If we keep first-class functions, we document the weaker guarantee and the capability restriction. If we move to second-class functions, that is a language change with its own slice.

**Jonas.** Agreed. Let me also put the error-quality argument on the table. Closed sets give excellent errors: a missing field, an extra field, an unhandled effect. Rows and open sets make those errors worse because the solver has to explain a difference rather than a set. That is a practical reason to stay closed.

**Mira.** And handler subtraction gives excellent effect errors: the handler removes exactly the effect it names; the remaining set is the caller's obligation. No row unification, no presence/absence flags. That is Effekt's real contribution.

**Jonas.** One more difference: records are data, so they are easy to serialize, compare, and prove. Handlers are control flow, so they are hard to serialize, compare, and prove. That is another reason to land records before handlers.

**Mira.** It is a reason to land records before handlers, not before effects. The effect sets are also data; only handlers are control flow.

**Jonas.** Fair. The final sequence is: closed effect sets and contextual polymorphism, closed set records, then handlers. The type-system changes for records and effects happen in one coordinated slice if possible, so `Type`, `Scheme`, and `WordSignature` change once.

**Mira.** I will take that. The destination is the hybrid; the first deliverable is the effect-set core; the second is closed records; the third is handlers. No rows, no AC unification by default, and an explicit decision on second-class functions.

---

## Moderator's synthesis

The two positions converge on the same destination and differ on sequencing and risk.

**Agreed model.**
- Records are closed sets of labeled fields. No row variables, no subtyping in the first slice.
- Effects are closed sets of operation names. Effect types express capabilities required from the context.
- Effect polymorphism is contextual: a function's effect type describes its own requirements; argument-block effects belong to the caller's context.
- Handlers subtract the handled effect from the effect set.
- No ordered row types. No AC unification by default. Open set variables only with a concrete consumer.
- The second-class-function question is decided explicitly before handlers. If functions stay first-class, the weaker effect-safety guarantee and the capability restriction are documented.

**Agreed sequence.**
1. Closed effect sets and contextual polymorphism.
2. Closed set-theoretic records.
3. Handlers and capability passing.
4. Open set variables only if a consumer proves the need.

**Realistic chances.**
- Effect-set and contextual-polymorphism core: 70 percent or better.
- Closed records: 60–70 percent.
- Handlers: 40–60 percent.
- Full hybrid with handlers: 40–55 percent, and only if the second-class-function decision is made.

**Why not rows.** Rows are a second type system. They introduce order, tails, and lacks constraints next to Scale's set theory. The set-theoretic version is simpler, gives better errors, and keeps one representation for records and effects.

**Open questions.**
- Are functions first-class or second-class?
- Does a record field carry an effect set when its value is a function?
- What is the exact capability discipline for captured capabilities?
- Which consumer justifies open set variables, if any?
- How are closed records serialized and compared in the image?

The dialogue ends with a working agreement: the hybrid is the destination, the effect-set core is the first slice, closed records are the second, and handlers are the third. The disagreement was never about whether records and effects belong together; it was about which risk to take first.
