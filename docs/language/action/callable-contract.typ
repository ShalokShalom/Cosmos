= The callable contract

#strong[Status: R0, durable resolver slice implemented.] This is the root
contract for calls in the lowered path. It separates a #strong[reference] from a
#strong[call], so a name that resolves to a value can never be silently invoked
as a word, and a word call can never silently return a value. The suite is 1741
checks (1683 examples, 58 properties).

== The rule

A call site is one of:

- a #strong[lexical closure] bound by a static `Let`/`Lambda`; lowering emits
  `PushSlot; CallClosure`;
- a #strong[word] (builtin, vocabulary word, user word, generic, word
  metaobject, host word, or book word); lowering emits `CallWord`.

A bare word reference is a value reference: a statically bound name lowers to
`PushSlot`; a free name lowers to `CallWord` only when it denotes a word.

The lowered/compiled path is #strong[strict]. If a `CallWord` reaches a runtime
lexical binding, the program is malformed for that path; it is a named refusal
(`a lowered word call cannot resolve a runtime lexical binding`), never a
silent value push. The interpreter keeps its own dynamic `let`/`define`
semantics.

== Why

`CallWord` used to serve both a word call and a name reference. A call to a
name that was not in the static environment lowered to `CallWord`; at runtime,
if the frame held that name, `Evaluator.executeWord` pushed the value. A
function that called itself or a later definition returned its own closure with
exit code zero. The book loader now admits recursive groups (D194);
this contract also protects the lowered path when a dynamic binding reaches a
word call.

== The durable target

The full callable model replaces the static/dynamic split with one resolver:

- one `Callable` kind (builtin, closure, unit, word metaobject, generic/method,
  host word, book word, F\# primitive);
- one `Invoke` operation with arity and effect checks;
- one guard/epoch cache;
- one refusal vocabulary.

That model is backlog item R0's durable form. The implemented slice covers
control primitives, control closures, builtin tokens, host words, and the
non-builtin name kinds: word value, generic function, user word, and vocabulary
entry. Control resolution, name resolution, and the builtin fallback each
produce one `Callable`, and one `Invoke` performs the state transition. Builtin
classification still delegates to the primitive table; book words and closure
arity/effect checks join the same resolver in later slices.

Recursion markers, host words, book words, and type signatures all use the same
answer to “what did this name resolve to, and is this a call?”
== Tests

+ a word call through the lowered bridge still works;
+ a lowered call that reaches a runtime lexical binding is a named refusal;
+ an earlier book definition call still works;
+ a self, forward, or mutual book call is a real group call (D194);
+ a control primitive, a control closure, a builtin token, and a host word
  resolve through the same callable path as a word value, generic call, user
  word, and vocabulary entry.
== Non-goals

This slice routes builtin tokens through `Invoke` but keeps their primitive-table
classification. Book words and closure arity/effect checks remain later R0
slices. The `logic`/`cycle` definition words and book recursion build on this
boundary: book recursion is D194, and the definition words are D195.