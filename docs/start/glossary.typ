// Glossary of the current public names. Included by README.typ.
// Every glossary term has a label <g-...>; descriptions link to those labels.

#let g(label, body) = link(label)[#body]

=== Lia <g-lia>
The mascot. #strong[Scale] is the product name; Lia is not a synonym for the
product and does not appear in code or generated artifacts. Historical prose
used Lia as the prototype name.
Definition: #link("README.typ")[README.typ].
Used in: #link("README.typ")[README.typ].

=== Set <g-set>
A collection of values. In Scale the values are #g(<g-tensor>)[tensors], so a
set is a collection of tensor values; #g(<g-membership>)[membership] is structural. An anonymous set
is written `[ … ]`; a named set is a #g(<g-value-set>)[value set]. The
#g(<g-store>)[store] holds the members as store entries.
Definition: #link("components/prelude/evaluator/value-sets.fs")[components/prelude/evaluator/value-sets.fs].
Used in: #link("components/prelude/evaluator/set-algebra.fs")[set-algebra.fs],
#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== ValueSet <g-value-set>
A named #g(<g-set>)[set]: a seal (a text name), an extension (member identities), and an
optional narrowing #g(<g-quotation>)[quotation] or
#g(<g-evaluator-runtime-guard>)[runtime guard]-style
constructor. The code type is `EvaluatorValueSets.ValueSet`. A value set is not
an ordering; the prose term is #emph[sort].
Definition: #link("components/prelude/evaluator/value-sets.fs")[components/prelude/evaluator/value-sets.fs].
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs],
#link("components/prelude/evaluator/set-algebra.fs")[set-algebra.fs],
#link("type-lattice.fs")[type-lattice.fs].

=== Cell system <g-cell-system>

The layer above the metaobject substrate: classes, slots, instances, class precedence, nominal dispatch, method combination, closures, and metaclasses. The encoding landed as D44, closures/lexical scope as D45, classes/slots/instances as D46, precedence/nominal dispatch as D47, method qualifiers and combination as D48, and metaclasses as D49; conditions and restarts (D50) landed. The surface stays Scale Basic English.
Definition: #link("docs/runtime/cell-system.typ")[docs/runtime/cell-system.typ] (D43),
#link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding),
#link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D46).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs]; A--G landed.
=== Cell <g-cell>
A cell is an instance: stable address identity plus appended slot state. A cell is not a #g(<g-tensor>)[tensor]. A tensor is the structural numeric value, compared by data and shape; a cell is a nominal instance, compared by identity, whose slot values are tensors. A class is the cell's class, not its data type; two cells with equal slot values are still distinct cells.
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding), #link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D46).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs].
The layer above the metaobject substrate: classes, slots, instances,
class precedence, nominal dispatch, method combination, closures, and
metaclasses. The encoding landed as D44, closures/lexical scope as D45,
classes/slots/instances as D46, and precedence/nominal dispatch as D47; the
conditions and restarts (D50) landed. The surface stays Scale Basic English.
Definition: #link("docs/runtime/cell-system.typ")[docs/runtime/cell-system.typ] (D43),
#link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding),
#link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D46).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs]; landed.

=== Cell system, Scale versus the typical <g-cell-system-scale>
The typical class-based system is a type declaration: a class fixes its
slots and methods at compile time, an instance is a mutable cell, and dispatch
is usually on one class. Scale's cell system is a return, in spirit,
to Alan Kay's original framing: cells communicate, binding is
late, and the definition is data. Classes remain, but a class is an
image-resident metaobject, a slot is a definition with a value set, an instance
is stable identity plus appended state, and dispatch is nominal over classes
and precedence (D43--D50). This is a Lisp-flavoured cell system: definitions
are store entries, a new class, slot, method, combination, or metaclass is data,
the REPL is the development loop, and redefinition appends instead of
rewriting. It borrows CLOS's generic functions, method combination, and
metaobject protocol (D16, D17) without a reflective tower. Live coding and
flexibility are the point: redefinition is an append, history can roll back, and
a domain that does not opt in is unaffected. The remaining behavior is
deferred; the encoding landed as D44, closures as D45, classes/slots/instances
as D46, and precedence/nominal dispatch as D47. The verification boundary is
unchanged: the cell system is a runtime feature, not an SMT theory.

#table(
  columns: (2fr, 3fr, 3fr),
  align: (left, left, left),
    table.header(table.cell(fill: luma(240))[Aspect], table.cell(fill: luma(240))[Typical class-based], table.cell(fill: luma(240))[Scale cell system]),
    table.hline(),
    [Class], [compile-time nominal type], [image-resident definition; nominal identity plus precedence],
    [Slot], [fixed layout], [definition as data; a value set; appended state per write],
    [Instance], [mutable cell], [stable identity plus appended state],
    [Dispatch], [single dispatch on the class], [multiple dispatch over classes and value sets],
    [Methods], [attached to a class], [generic functions; qualifiers and combination are data],
    [Redefinition], [recompile and restart], [append a new definition; history and rollback],
    [Development], [compile and run], [live REPL over the image],
)

Definition: #link("docs/runtime/cell-system.typ")[docs/runtime/cell-system.typ] (D43--D50).
Used in: #link("README.typ")[README.typ]; A--G landed.

=== Cell system versus Smalltalk, CLOS, and Ruby/Python <g-cell-system-comparison>
Scale shares their late-bound, image-based spirit, but its class is image data,
dispatch is a generic-function protocol over classes and value sets, instances
are stable identity plus appended state, and the image is append-only.

+ #strong[Smalltalk] is the closest in spirit: classes are cells, messages are
  late-bound, and the image is live. Scale differs in its append-only image (no
  host cell graph), in multiple dispatch through generic functions rather than
  method lookup on the receiver, and in the verification boundary. There is no
  `self`/`super` or `become:`; redefinition appends.
+ #strong[CLOS] is the closest in structure: generic functions, multiple
  dispatch, method combination, and the MOP are the model. Scale borrows those
  but keeps a fixed meta-level (no reflective tower), uses an append-only image
  instead of mutable instances, and has no Lisp macros or condition system yet.
+ #strong[Ruby] has open classes and modules; Scale's classes are image entries,
  so a redefinition appends rather than reopens. Ruby methods live on the
  receiver's class/module chain; Scale dispatches through generic functions over
  all arguments and value sets. There is no `method_missing` silent fallback.
+ #strong[Python] uses C3 MRO, bound methods, `self`, descriptors, and a mutable
  `__dict__`. Scale has a nominal class, a C3 deterministic precedence, and
  declared slots only; a missing slot or an undecidable dispatch is named. Both
  support multiple inheritance, but Scale's dispatch is multiple and nominal rather
  than single-dispatch on the first argument.

The load-bearing difference is the image: their cells live in a mutable host
heap, Scale's live in an append-only tensor image. That is why redefinition,
instance state, and history work differently.
Definition: #link("docs/runtime/cell-system.typ")[docs/runtime/cell-system.typ] (D43--D50).
Used in: #link("README.typ")[README.typ]; A--G landed.

=== Class <g-class>
A class is an image-resident metaobject that owns a name, a superclass list, a
slot list, a metaclass, and a method-combination policy. Its extension is a
#g(<g-value-set>)[value set]; the cell-system class is not the old `ClassTag`
verdict (now #g(<g-value-tag>)[ValueTag]). The `class` word declares one in the
lab surface; the Basic English declaration grammar is still deferred.
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding),
#link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D46).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Slot <g-slot>
A slot is an image-resident definition with a name, a #g(<g-value-set>)[value set], an optional default, and an initialization rule. The `slot` word declares one; a slot's value is checked against its set.
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding),
#link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D46).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Instance <g-instance>
An instance is a value with stable address identity and an appended state: the
identity entry is written once, and every slot write appends a new state entry,
exactly like #g(<g-evaluator-channel>)[channel] state (D27). `create-instance`
creates one; `slot-value` reads the latest state and `set-slot-value` appends a
new one.
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding),
#link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D46).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Class precedence <g-class-precedence>
Class precedence is a deterministic linearization of the superclass graph. Scale uses C3; a cycle is `ClassCycle` and a conflicting order is `InconsistentPrecedence`.
Definition: #link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D47).
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Class dispatch <g-class-dispatch>
Dispatch on a class is nominal (class identity plus precedence), not extensional, so it is stable as instances are added. Class specializers use precedence inclusion; a class/value-set tie is ambiguous.
Definition: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs] (D47),
#link("components/prelude/evaluator/class-system.fs")[class-system.fs] (D47).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Method combination <g-method-combination>
Methods gain a qualifier, and a method combination orders them. The qualifiers are Primary, Before, After, and Around.
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs]; landed.

=== Closure <g-closure>
Closures are image-resident code cells with an image-resident environment chain, so lexical scope does not become host state. Lexical scope landed as D45: `let` creates a frame, `lambda` captures it, and `apply` runs in it and restores the caller.
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding),
#link("components/prelude/evaluator/closure.fs")[closure.fs] (D45).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Environment frame <g-environment-frame>
An environment frame is an image-resident lexical frame with a parent and name/value pairs. The current frame is execution state, never serialized (D45).
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding),
#link("components/prelude/evaluator/closure.fs")[closure.fs] (D45).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Metaclass <g-metaclass>
Metaclasses make classes instances of a class, with a finite class-of closure (the cell query is `cell-class`) terminating at a root metaclass. The old classification word `class-of` is now `value-of`.
Definition: #link("components/prelude/evaluator/cell-system.fs")[cell-system.fs] (D44 encoding).
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs]; landed.

=== Set-operation protocol <g-set-operation-protocol>
The six set words are image-resident generic functions: `member`, `subset`,
`union`, `intersect`, `except`, and `complement`. Each has a default method
specialized on the empty `set` declaration; a set entry is classified as `set`
by the dispatcher, and the method body calls the kernel primitive of the same
operation (`set-member`, `set-subset`, and so on). Replacing a method is an image
change, not an F\# change.
Definition: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs],
#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Tensor <g-tensor>

The only semantic value: a rank-n array of numbers with a shape. A scalar is a rank-0 #g(<g-tensor>)[tensor]. Tensors are compared structurally; two tensors with the same data and shape are the same #g(<g-set>)[set] member.

A tensor is not a #g(<g-cell>)[cell]. A tensor has no address identity and no appended state; a cell has both, and its slot values are tensors. Structural tensor equality is not cell identity: two cells with equal slot tensors remain distinct cells.
Definition: #link("tensor.fs")[tensor.fs].
Used in: #link("store.fs")[store.fs], #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs],
#link("smt.fs")[smt.fs].
=== EntryIdentity <g-entry-identity>
A nominal address of an entry in the #g(<g-store>)[store]. It is a rank-0
#g(<g-tensor>)[tensor] projection, not a host pointer; the store is the only
authority.
Definition: #link("types.fs")[types.fs].
Used in: #link("store.fs")[store.fs], #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs],
#link("persistence.fs")[persistence.fs].

=== Store <g-store>
The append-only image of every #g(<g-tensor>)[tensor] and definition. It is not
the evaluation #g(<g-stack>)[stack]: the store holds data, the stack holds control
state. The old name was `Image`.
Definition: #link("store.fs")[store.fs].
Used in: #link("persistence.fs")[persistence.fs],
#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Stack <g-stack>
The evaluator's control stack: push, pop, and peek of #g(<g-entry-identity>)[EntryIdentity]s.
It is not the #g(<g-store>)[store].
Definition: #link("stack.fs")[stack.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs],
#link("components/prelude/evaluator/stack.fs")[components/prelude/evaluator/stack.fs].

=== Lexicon <g-lexicon>
The #g(<g-store>)[store]'s tensor table: address to #g(<g-tensor>)[tensor]. The
#g(<g-vocabulary>)[vocabulary] is stored separately, as image entries.
Definition: #link("lexicon.fs")[lexicon.fs].
Used in: #link("store.fs")[store.fs], #link("types.fs")[types.fs].

=== Vocabulary <g-vocabulary>
The #g(<g-store>)[store]-resident word list. A
#g(<g-vocabulary-entry>)[VocabularyEntry] is a
word's token, form, category, active flag, and implementation reference; the
runtime #g(<g-word>)[word] metaobject projects it.
Definition: #link("vocabulary.fs")[vocabulary.fs],
#link("vocabulary-in-image.fs")[vocabulary-in-image.fs].
Used in: #link("tokenizer.fs")[tokenizer.fs], #link("parser.fs")[parser.fs],
#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs].

=== VocabularyEntry <g-vocabulary-entry>
A word's image projection: token, form, #g(<g-word-category>)[word category],
active flag, and implementation name. The runtime #g(<g-word>)[word] metaobject
projects it.
Definition: #link("types.fs")[types.fs],
#link("vocabulary-in-image.fs")[vocabulary-in-image.fs].
Used in: #link("vocabulary.fs")[vocabulary.fs], #link("tokenizer.fs")[tokenizer.fs],
#link("parser.fs")[parser.fs].

=== Word <g-word>
A word metaobject: `[WordTag; token; form; category; active; implementation]`.
It makes a new word family data rather than an F\# case.
Definition: #link("components/prelude/evaluator/word.fs")[components/prelude/evaluator/word.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs],
#link("vocabulary-in-image.fs")[vocabulary-in-image.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== WordCategory <g-word-category>
The role of a vocabulary word: TypeName, FieldName, FunctionName, Copula,
Comparison, or one of the other closed-class roles. It is not the
#g(<g-value-tag>)[value tag] of a value.
Definition: #link("types.fs")[types.fs].
Used in: #link("vocabulary.fs")[vocabulary.fs], #link("parser.fs")[parser.fs],
#link("components/prelude/evaluator/word.fs")[components/prelude/evaluator/word.fs].

=== ValueTag <g-value-tag>
The image tag `-12.0` of a value classification verdict: `[ValueTag; status;
payload…]`. Statuses are Unknown, a definite value set, Ambiguous, Undecided,
and Metaobject.
The #g(<g-value-verdict>)[value verdict] is computed by
#g(<g-evaluator-dispatch>)[valueVerdictOf] and returned by the
#g(<g-word-value-of>)[value-of] surface word.
Definition: #link("types.fs")[types.fs],
#link("components/prelude/evaluator/dispatch.fs")[components/prelude/evaluator/dispatch.fs].
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== ValueVerdict <g-value-verdict>
The internal classification answer: `ValueOf of #g(<g-value-set>)[ValueSet]`,
`ValueUnknown`, `ValueAmbiguous of #g(<g-value-set>)[ValueSet] list`, or
`ValueUndecided of string`. It is not a cell-system #g(<g-class>)[class].
Definition: #link("components/prelude/evaluator/dispatch.fs")[components/prelude/evaluator/dispatch.fs].
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Membership <g-membership>
The answer to "is this value in this #g(<g-value-set>)[value set]?": `Member`,
`NotMember`, or `Undecided reason`. An undecidable predicate is never a silent
false.
Definition: #link("components/prelude/evaluator/value-sets.fs")[components/prelude/evaluator/value-sets.fs].
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs],
#link("components/prelude/evaluator/set-algebra.fs")[set-algebra.fs].

=== EntryKind <g-entry-kind>
The kind of a tagged store entry: #g(<g-word>)[word], generic function, method,
#g(<g-evaluator-channel>)[channel], channel state, #g(<g-channel-event>)[channel
event], #g(<g-history-step>)[history step], predicate #g(<g-value-set>)[value
set], #g(<g-evaluator-runtime-guard>)[runtime guard], or constructor. It is not a
value classification.
Definition: #link("components/prelude/evaluator/dispatch.fs")[components/prelude/evaluator/dispatch.fs].
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== EvaluationState <g-evaluation-state>
The execution state: the #g(<g-stack>)[stack], the `Term list` program and
program counter, roots into the #g(<g-store>)[store],
#g(<g-scheduler>)[scheduler] state, and #g(<g-ownership-state>)[ownership]
state. It is never serialized. The program representation is
#g(<g-term-ir>)[Term IR].
Definition: #link("types.fs")[types.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs],
#link("components/prelude/evaluator/scheduler.fs")[scheduler.fs], #link("persistence.fs")[persistence.fs].

=== Quotation <g-quotation>
A code value in braces, not token text: `[QuoteTag; documentId]` naming the
canonical term-program document for the body. It runs on a fresh stack with a
reduction budget. Control words, predicate sets, runtime guards, wrappers, and
method bodies use it. See #g(<g-code-value>)[code value] and
#g(<g-term-ir>)[Term IR].
Definition: #link("components/prelude/evaluator/control.fs")[control.fs],
#link("components/prelude/evaluator/code.fs")[code.fs].
Used in: #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs],
#link("components/prelude/evaluator/runtime-guard.fs")[runtime-guard.fs],
#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs].

=== Scheduler <g-scheduler>
The cooperative actor scheduler and its per-process state: spawn, yield, halt,
resume, kill, channels, and event waiters.
Definition: #link("components/prelude/evaluator/scheduler.fs")[components/prelude/evaluator/scheduler.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Actor <g-actor>
A scheduled process with an identity, parent, children, status, and its own
#g(<g-evaluation-state>)[EvaluationState].
Definition: #link("types.fs")[types.fs].
Used in: #link("components/prelude/evaluator/scheduler.fs")[scheduler.fs],
#link("components/prelude/evaluator/ownership.fs")[ownership.fs].

=== OwnershipState <g-ownership-state>
Per-run linear ownership: owners and consumed addresses. It is execution state,
never serialized.
Definition: #link("components/prelude/evaluator/ownership.fs")[components/prelude/evaluator/ownership.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Evaluator <g-evaluator>
The stack-machine entry point: `executeWord` dispatches a token through the
built-ins, #g(<g-word>)[word] metaobjects, generic functions, user words, and the
#g(<g-vocabulary>)[vocabulary].
Definition: #link("components/prelude/evaluator/evaluator.fs")[components/prelude/evaluator/evaluator.fs].
Used in: #link("development/scale/tests.fs")[development/scale/tests.fs], #link("bases/scale-cli/entry.fs")[bases/scale-cli/entry.fs],
#link("properties.fs")[properties.fs].

=== EvaluatorValueSets <g-evaluator-value-sets>
The evaluator slice that owns #g(<g-value-set>)[value sets], structural set
operations, membership, and predicate/constructor narrowing.
Definition: #link("components/prelude/evaluator/value-sets.fs")[components/prelude/evaluator/value-sets.fs].
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs],
#link("components/prelude/evaluator/set-algebra.fs")[set-algebra.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== EvaluatorDispatch <g-evaluator-dispatch>
The multiple-dispatch slice: generic functions, methods, type-first selection,
and the #g(<g-value-tag>)[value tag] / #g(<g-entry-kind>)[entry kind] queries.
Definition: #link("components/prelude/evaluator/dispatch.fs")[components/prelude/evaluator/dispatch.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== EvaluatorSetAlgebra <g-evaluator-set-algebra>
The slice for the `set`, `add-member`, `and`, `or`, `nor`, and `is` words over
image-resident #g(<g-value-set>)[value sets]. The old name was `Vivid`.
Definition: #link("components/prelude/evaluator/set-algebra.fs")[components/prelude/evaluator/set-algebra.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== EvaluatorChannelEvent <g-evaluator-channel-event>
The ChannelEvents event slice: `send-event`, `receive-event`, `choose`, `wrap`, `guard`,
`with-fallback`, and `sync` over store-resident
#g(<g-channel-event>)[ChannelEvent] values.
Definition: #link("components/prelude/evaluator/channel-event.fs")[components/prelude/evaluator/channel-event.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== EvaluatorChannel <g-evaluator-channel>
The channel slice: `channel`, `send`, `receive`, and `close`. A blocked receive
is a waiter, not an error.
Definition: #link("components/prelude/evaluator/channel.fs")[components/prelude/evaluator/channel.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== ChannelEvent <g-channel-event>
A ChannelEvents communication event: send, receive, choose, wrap, guard, or with-fallback.
`sync` commits one; a blocked sync is a waiter, not an error.
Definition: #link("components/prelude/evaluator/channel-event.fs")[components/prelude/evaluator/channel-event.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs],
#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== EvaluatorRuntimeGuard <g-evaluator-runtime-guard>
The runtime guard metaobject slice: `runtime-guard-define` and
`runtime-guard-holds`; the guard's quotation runs through the shared
#g(<g-quotation-run>)[quotation run].
Definition: #link("components/prelude/evaluator/runtime-guard.fs")[components/prelude/evaluator/runtime-guard.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== QuotationRun <g-quotation-run>
The shared probe that runs a #g(<g-quotation>)[quotation] on one input with a
reduction budget and no actor words; #g(<g-membership>)[membership] and
#g(<g-evaluator-runtime-guard>)[runtime guards] both use it. The old name was
`EvaluatorProbe`.
Definition: #link("components/prelude/evaluator/quotation-run.fs")[components/prelude/evaluator/quotation-run.fs].
Used in: #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs],
#link("components/prelude/evaluator/runtime-guard.fs")[runtime-guard.fs].

=== HistoryStep <g-history-step>
One executed term in the append-only event stream: program counter metadata,
the current term, the stack before it ran, and the program it resumes. The term
and program are #g(<g-code-value>)[code values], not token text. `history` and
`rollback` read and restore steps.
Definition: #link("components/prelude/evaluator/history.fs")[components/prelude/evaluator/history.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== UnificationTerm <g-unification-term>
A first-order term: a variable (`?name`), an atom, a compound (`-2` tagged), or
a typed term (`-3` tagged). `unify`, `resolve`, `term`, and `typed` operate on
them. The old name was `Term`; current `Term` means a
#g(<g-term-ir>)[Term IR] term and is not this type.
Definition: #link("components/prelude/evaluator/environment.fs")[components/prelude/evaluator/environment.fs].
Used in: #link("components/prelude/evaluator/dispatch.fs")[dispatch.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== ProvenClosure <g-proven-closure>
The proved guard closure for a refinement: `pre ∧ guard ∧ ¬post` is
unsatisfiable. The old name was `Closure`.
Definition: #link("refiner.fs")[refiner.fs].
Used in: #link("refiner.fs")[refiner.fs], #link("repair.fs")[repair.fs],
#link("development/scale/tests.fs")[development/scale/tests.fs].

=== ScalarRead <g-scalar-read>
The projection helpers that read a rank-0 #g(<g-tensor>)[tensor] as a number or a
text tensor as a string. The old name was `Lace`.
Definition: #link("scalar-read.fs")[scalar-read.fs].
Used in: #link("components/prelude/evaluator/evaluator.fs")[evaluator.fs], #link("bases/scale-cli/entry.fs")[bases/scale-cli/entry.fs].

=== VocabularyList <g-vocabulary-list>
The read-only vocabulary browser: `showVocabulary` and `explainWord`. The old
name was `Bag`.
Definition: #link("vocabulary-list.fs")[vocabulary-list.fs].
Used in: #link("bases/scale-cli/entry.fs")[bases/scale-cli/entry.fs], #link("demo.fs")[demo.fs].

=== Smt <g-smt>
The in-process #g(<g-z3>)[Z3] bridge: Satisfiability Modulo Theories (SMT)
encoding, satisfaction, and discharge of obligations. It returns `Proved`,
`Falsified`, `Unknown`, or `Unavailable`.
Definition: #link("smt.fs")[smt.fs].
Used in: #link("refiner.fs")[refiner.fs], #link("repair.fs")[repair.fs],
#link("decision-table.fs")[decision-table.fs].

=== SolverAssignment <g-solver-assignment>
A #g(<g-smt>)[solver] model read back as domain values, so tests, bundles, and
decision tables do not hold a #g(<g-z3>)[Z3] context. The old type name was
`Model`.
Definition: #link("smt.fs")[smt.fs].
Used in: #link("refiner.fs")[refiner.fs], #link("repair.fs")[repair.fs],
#link("decision-table.fs")[decision-table.fs].

=== Refinement <g-refinement>
A `.scale` refinement: a #g(<g-refinement-guard>)[refinement guard], its origin,
and its counterexample. It is append-only in the document.
Definition: #link("scale-file.fs")[scale-file.fs].
Used in: #link("refiner.fs")[refiner.fs], #link("emitter.fs")[emitter.fs],
#link("repair.fs")[repair.fs].

=== RefinementGuard <g-refinement-guard>
A typed single-field guard from a `.scale` refinement: `GreaterThan`, `AtLeast`,
`LessThan`, `AtMost`, `IsTrue`, or `IsFalse`. It is the verification projection,
separate from the #g(<g-evaluator-runtime-guard>)[runtime guard] metaobject.
Definition: #link("scale-file.fs")[scale-file.fs].
Used in: #link("refiner.fs")[refiner.fs], #link("type-lattice.fs")[type-lattice.fs],
#link("emitter.fs")[emitter.fs].

=== FieldType <g-field-type>

A verification field\'s domain type: `BooleanType`, `RealType`, `IntegerType`,
or `NaturalType`. It is not a #g(<g-value-set>)[value set]; it is the
lattice\'s field coordinate.

- `BooleanType` is a truth value. Use it for flags, predicates, and any field
  whose guards are only `is true` or `is false`. It has no numeric threshold.
- `IntegerType` is an exact, discrete number on the SMT `Int` sort. Use it for
  counts, indices, versions, retries, and token budgets: quantities where a
  fractional value is meaningless.
- `NaturalType` is an `IntegerType` constrained to be at least zero. Use it
  when the same discrete quantity cannot be negative.
- `RealType` is a continuous number on the SMT `Real` sort. Use it for
  measurements and ratios where fractional bounds matter: positions, speeds,
  temperatures, probabilities, and normalized scores.

The emitted F\# type is `bool` for Boolean, `int` for Integer and Natural, and
`float` for Real. A mixed Real and integer domain selects `QF_LIRA`; Real alone
selects `QF_LRA`; the integer-only path stays `QF_LIA`.

Definition: #link("domain-expression.fs")[domain-expression.fs],
#link("type-lattice.fs")[type-lattice.fs].
Used in: #link("smt.fs")[smt.fs], #link("emitter.fs")[emitter.fs],
#link("refiner.fs")[refiner.fs].

=== TypeLattice <g-type-lattice>

The one verification coordinate system shared by Z3, Hedgehog, and the refiner:
fields, finite coordinates, and narrowing guards.
Definition: #link("type-lattice.fs")[type-lattice.fs].
Used in: #link("smt.fs")[smt.fs], #link("refiner.fs")[refiner.fs],
#link("emitter.fs")[emitter.fs].

=== Type inference <g-type-inference>

Scale does not have a general type-inference engine hidden somewhere. What it
has is *signature-directed elaboration*: declarations and word contracts supply
the types, the checker propagates and verifies them, and an expression with no
usable signature is refused rather than guessed. That distinction matters
because "inference" sounds like the compiler can recover any type from any
program, which is not true here.

There are three sources of type information, and only one of them is inference:

+ #strong[Scale word signatures.] The signature and effect table is the
  authority. A call checks its arguments and result against the called word; a
  local binding is checked against the words that consume it. This is local
  constraint propagation over known signatures, not general Hindley--Milner
  inference. If the constraints do not determine a type, the result is a named
  diagnostic, not a default.
+ #strong[The F\# compiler at the package boundary.] For F\# definitions and
  the F\# Prelude port, the F\# compiler has already done the inference. Scale
  reads the result through *FSharp.Compiler.Service* (FCS): the checked symbol,
  `FullType`, curried parameter groups, return parameter, generic parameters,
  and constraints. This is compiler read-off. It is not Scale inferring an F\#
  binding, and it does not transfer to arbitrary Scale syntax.
+ #strong[Runtime value tags.] Tensor tags validate a value at execution time.
  They are a safety check, not a type-inference source; Scale does not observe a
  running program and then infer its static type.

The phrase "inference without a dedicated implementation" is therefore true
only in a narrow sense. F\# inference is free because the F\# compiler already
implements it and FCS exposes the answer. Scale inference is not free. P4 now
supplies the missing implementation: P4a is a host-side first-order type
constraint and unification core with schemes, substitutions, an occurs-check,
exact effect sets, generalization, instantiation, and named diagnostics. P4b
connects it to `ExpressionAst` and the word-signature table; P4c integrates the
result into lowering and source-ranged diagnostics. A faithful F\# projection
checked by FCS is an alternative only if it preserves effects, stack behavior,
source ranges, and Scale-only words; it is not a substitute by default.

The durable rules:

+ Prefer an explicit signature at a boundary; inference is a convenience inside
  a checked body.
+ The signature table, not the runtime, is authoritative.
+ An unknown or ambiguous type is a diagnostic; never silently choose one.
+ Do not infer a tuple or a function from the observed stack shape.
+ Inferred facts are elaboration-time hints, never portable semantics.
+ Higher-order type unification is not needed for ordinary higher-order
  programming; function types and schemes cover that. It is reserved for a
  future type-level lambda or higher-kinded feature.

Definition: #link("docs/types/type-inference.typ")[docs/types/type-inference.typ],
#link("components/typing/type-inference.fs")[type-inference.fs],
#link("docs/process/decision-records.typ")[docs/process/decision-records.typ] (D173),
#link("docs/language/action/function-values.typ")[docs/language/action/function-values.typ],
#link("docs/architecture/language-feature-ir.typ")[docs/architecture/language-feature-ir.typ].
Used in: #link("docs/language/action/function-values.typ")[docs/language/action/function-values.typ],
#link("docs/kernel/prelude-port.typ")[docs/kernel/prelude-port.typ],
#g(<g-field-type>)[FieldType], #g(<g-type-lattice>)[TypeLattice],
#g(<g-expression-ir>)[Expression IR].
=== Refiner <g-refiner>
The candidate-guard synthesizer: it consumes a falsification and a
#g(<g-proven-closure>)[proven closure] oracle and returns a refinement.
Definition: #link("refiner.fs")[refiner.fs].
Used in: #link("demo.fs")[demo.fs], #link("repair.fs")[repair.fs],
#link("development/scale/tests.fs")[development/scale/tests.fs].

=== Repair <g-repair>
The fixed-spec acceptance gate: it turns a witness into a versioned repair bundle
and accepts a patch only when the target proves and no passing witness regresses.
Definition: #link("repair.fs")[repair.fs].
Used in: #link("bases/scale-cli/entry.fs")[bases/scale-cli/entry.fs], #link("cli.fs")[cli.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== DecisionTable <g-decision-table>
The optional symbolic table of guarded rows; `check` proves completeness and,
under `Unique`, disjointness. It is a specification refinement, not the emitter.
Definition: #link("decision-table.fs")[decision-table.fs].
Used in: #link("docs/process/decision-models.typ")[docs/process/decision-models.typ],
#link("development/scale/tests.fs")[development/scale/tests.fs].

=== Parser <g-parser>
The Prose sentence parser (historically Basic English): quantifier, subject, optional relative clause,
verb, full stop. It refuses unsupported sentences by name. The
#g(<g-prose-parser>)[prose parser] is a separate surface parser for match,
calls, pipelines, and partial application.
Definition: #link("parser.fs")[parser.fs].
Used in: #link("emitter.fs")[emitter.fs], #link("development/scale/tests.fs")[development/scale/tests.fs],
#link("properties.fs")[properties.fs].

=== Persistence <g-persistence>
The binary image format: it serializes the #g(<g-store>)[store] data only; roots
and ownership are execution state and are re-threaded by the reader.
Definition: #link("persistence.fs")[persistence.fs].
Used in: #link("bases/scale-cli/entry.fs")[bases/scale-cli/entry.fs], #link("development/scale/tests.fs")[development/scale/tests.fs].

=== Surface vocabulary <g-surface-vocabulary>
The words a user types. Each row links to its definition in the dispatcher and
the slice that owns it. #emph[These are the current spellings.]

#table(
  columns: (auto, 3fr, auto, 2fr),
  align: (left, left, left, left),
  table.header(
    table.cell(fill: luma(240))[Word],
    table.cell(fill: luma(240))[Purpose],
    table.cell(fill: luma(240))[Definition],
    table.cell(fill: luma(240))[Used in],
  ),
  table.hline(),

  [#raw("set") <g-word-set>], [Declares a named #g(<g-value-set>)[value set] with an empty extension.], [#link("components/prelude/evaluator/set-algebra.fs")[set-algebra.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("add-member") <g-word-add-member>], [Adds a value to a #g(<g-value-set>)[value set]; refuses a predicate or constructor set.], [#link("components/prelude/evaluator/set-algebra.fs")[set-algebra.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("seal") <g-word-seal>], [Names an anonymous #g(<g-set>)[set] with a text seal.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("predicate-seal") <g-word-predicate-seal>], [Names a #g(<g-set>)[set] narrowed by a predicate quotation.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("constructor") <g-word-constructor>], [Declares a reusable named constructor quotation.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("constructor-seal") <g-word-constructor-seal>], [Builds a named #g(<g-value-set>)[value set] from a constructor and a base extension.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("unname") <g-word-unname>], [Drops a seal, keeping the extension.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("subset") <g-word-subset>], [#g(<g-set-operation-protocol>)[Set-operation] method: structural subset test over a #g(<g-value-set>)[value set].], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs] + #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("union") <g-word-union>], [#g(<g-set-operation-protocol>)[Set-operation] method: structural union of two finite extensions.], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs] + #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("intersect") <g-word-intersect>], [#g(<g-set-operation-protocol>)[Set-operation] method: structural intersection of two finite extensions.], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs] + #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("except") <g-word-except>], [#g(<g-set-operation-protocol>)[Set-operation] method: structural difference of two finite extensions.], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs] + #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("member") <g-word-member>], [#g(<g-set-operation-protocol>)[Set-operation] method: asks whether a value is in a #g(<g-value-set>)[value set]; returns a #g(<g-membership>)[membership] verdict.], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs] + #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("same-shape") <g-word-same-shape>], [Structural equality of two extensions.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("same-name") <g-word-same-name>], [Nominal equality of two seals.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("sort-of") <g-word-sort-of>], [Asks whether a value carries a seal.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("merge") <g-word-merge>], [Seal-preserving union; refuses a seal mismatch.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("complement") <g-word-complement>], [#g(<g-set-operation-protocol>)[Set-operation] method: complements over a declared domain; refuses an anonymous set.], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs] + #link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("is") <g-word-is>], [Membership test used by the sentence surface.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("filter") <g-word-filter>], [Keeps the members a predicate accepts.], [#link("components/prelude/evaluator/value-sets.fs")[value-sets.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("value-of") <g-word-value-of>], [Pushes the value's #g(<g-value-tag>)[value tag] / #g(<g-value-verdict>)[value verdict].], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("word") <g-word-word>], [Declares an image-resident #g(<g-word>)[word] metaobject.], [#link("components/prelude/evaluator/word.fs")[word.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("generic") <g-word-generic>], [Declares an image-resident generic function.], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("method") <g-word-method>], [Adds a method to a generic function.], [#link("components/prelude/evaluator/dispatch.fs")[dispatch.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("term") <g-word-term>], [Builds a compound #g(<g-unification-term>)[unification term].], [#link("components/prelude/evaluator/environment.fs")[environment.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("typed") <g-word-typed>], [Wraps a term with a nominal seal.], [#link("components/prelude/evaluator/environment.fs")[environment.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("bind") <g-word-bind>], [Binds a name globally in the store-resident binding table; `let` is the frame-local form.], [#link("components/prelude/evaluator/environment.fs")[environment.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("lookup") <g-word-lookup>], [Resolves a name through the binding table.], [#link("components/prelude/evaluator/environment.fs")[environment.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("unify") <g-word-unify>], [Structurally unifies two #g(<g-unification-term>)[unification terms] with an occurs-check.], [#link("components/prelude/evaluator/environment.fs")[environment.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("resolve") <g-word-resolve>], [Zonks a #g(<g-unification-term>)[unification term] through the substitution chain.], [#link("components/prelude/evaluator/environment.fs")[environment.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("runtime-guard-define") <g-word-runtime-guard-define>], [Declares an image-resident #g(<g-evaluator-runtime-guard>)[runtime guard].], [#link("components/prelude/evaluator/runtime-guard.fs")[runtime-guard.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("runtime-guard-holds") <g-word-runtime-guard-holds>], [Runs a guard quotation against a value; returns 1 or 0.], [#link("components/prelude/evaluator/runtime-guard.fs")[runtime-guard.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("channel") <g-word-channel>], [Creates a store-resident #g(<g-evaluator-channel>)[channel].], [#link("components/prelude/evaluator/channel.fs")[channel.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("send") <g-word-send>], [Sends a message to a #g(<g-evaluator-channel>)[channel].], [#link("components/prelude/evaluator/channel.fs")[channel.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("receive") <g-word-receive>], [Receives a message; a blocked receive is a waiter, not an error.], [#link("components/prelude/evaluator/channel.fs")[channel.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("close") <g-word-close>], [Closes a #g(<g-evaluator-channel>)[channel] and wakes its waiters.], [#link("components/prelude/evaluator/channel.fs")[channel.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("send-event") <g-word-send-event>], [Builds a send #g(<g-evaluator-channel-event>)[channel event].], [#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("receive-event") <g-word-receive-event>], [Builds a receive channel event.], [#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("choose") <g-word-choose>], [Selects one ready event from a list.], [#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("wrap") <g-word-wrap>], [Runs a quotation over the selected event value.], [#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("guard") <g-word-guard>], [Suspends until the guard quotation holds.], [#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("with-fallback") <g-word-with-fallback>], [Commits a fallback event when the main event loses.], [#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("sync") <g-word-sync>], [Commits one event; a blocked sync is a waiter.], [#link("components/prelude/evaluator/channel-event.fs")[channel-event.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("spawn") <g-word-spawn>], [Starts a child #g(<g-actor>)[actor] in the #g(<g-scheduler>)[scheduler].], [#link("components/prelude/evaluator/scheduler.fs")[scheduler.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("yield") <g-word-yield>], [Yields control to another #g(<g-actor>)[actor].], [#link("components/prelude/evaluator/scheduler.fs")[scheduler.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("halt") <g-word-halt>], [Halts the current #g(<g-actor>)[actor].], [#link("components/prelude/evaluator/scheduler.fs")[scheduler.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("actors") <g-word-actors>], [Lists the #g(<g-actor>)[actors] and their states.], [#link("components/prelude/evaluator/scheduler.fs")[scheduler.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("resume") <g-word-resume>], [Resumes a halted #g(<g-actor>)[actor].], [#link("components/prelude/evaluator/scheduler.fs")[scheduler.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("kill") <g-word-kill>], [Terminates an #g(<g-actor>)[actor]; killing self is a named refusal.], [#link("components/prelude/evaluator/scheduler.fs")[scheduler.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("own") <g-word-own>], [Takes linear ownership of a value for the run.], [#link("components/prelude/evaluator/ownership.fs")[ownership.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("borrow") <g-word-borrow>], [Checks a read against ownership.], [#link("components/prelude/evaluator/ownership.fs")[ownership.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("consume") <g-word-consume>], [Ends a value's life for the run.], [#link("components/prelude/evaluator/ownership.fs")[ownership.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("release") <g-word-release>], [Gives ownership up.], [#link("components/prelude/evaluator/ownership.fs")[ownership.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("history") <g-word-history>], [Reports the #g(<g-history-step>)[history steps].], [#link("components/prelude/evaluator/history.fs")[history.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("rollback") <g-word-rollback>], [Restores a history step.], [#link("components/prelude/evaluator/history.fs")[history.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("bookmark") <g-word-bookmark>], [Records a named #g(<g-history-step>)[history step].], [#link("components/prelude/evaluator/bookmark.fs")[bookmark.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("jump") <g-word-jump>], [Restores through a bookmark.], [#link("components/prelude/evaluator/bookmark.fs")[bookmark.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("copyBookmark") <g-word-copy-bookmark>], [Duplicates a bookmark value.], [#link("components/prelude/evaluator/bookmark.fs")[bookmark.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("showBookmark") <g-word-show-bookmark>], [Renders a bookmark's saved location.], [#link("components/prelude/evaluator/bookmark.fs")[bookmark.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("if") <g-word-if>], [Runs one of two #g(<g-quotation>)[quotations].], [#link("components/prelude/evaluator/control.fs")[control.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("apply") <g-word-apply>], [Runs a #g(<g-quotation>)[quotation] with the current frame, or a #g(<g-closure>)[closure] with its captured frame.], [#link("components/prelude/evaluator/control.fs")[control.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("let") <g-word-let>], [Binds a name in a new child frame and makes it current.], [#link("components/prelude/evaluator/closure.fs")[closure.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("lambda") <g-word-lambda>], [Captures the current frame in a #g(<g-closure>)[closure].], [#link("components/prelude/evaluator/closure.fs")[closure.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("class") <g-word-class>], [Declares a #g(<g-class>)[class]; the name lists are the lab surface.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("slot") <g-word-slot>], [Declares a #g(<g-slot>)[slot] with a named value set.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("create-instance") <g-word-create-instance>], [Creates a #g(<g-instance>)[instance] with stable identity and an initial state.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("slot-value") <g-word-slot-value>], [Reads an instance's latest slot value; unbound and missing are named refusals.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("set-slot-value") <g-word-set-slot-value>], [Validates a value and appends a new instance state.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("class-precedence") <g-word-class-precedence>], [Pushes the class names in C3 precedence order.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("subclass-of") <g-word-subclass-of>], [Answers whether one class is the other or a subclass.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("instance-of") <g-word-instance-of>], [Answers whether a value is an instance of a class or its subclass.], [#link("components/prelude/evaluator/class-system.fs")[class-system.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("define") <g-word-define>], [Defines a user #g(<g-word>)[word] globally by splicing its body.], [#link("components/prelude/evaluator/control.fs")[control.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("duplicate") <g-word-duplicate>], [Duplicates the top #g(<g-stack>)[stack] value.], [#link("components/prelude/evaluator/stack.fs")[stack.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("swap") <g-word-swap>], [Swaps the top two #g(<g-stack>)[stack] values.], [#link("components/prelude/evaluator/stack.fs")[stack.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("drop") <g-word-drop>], [Drops the top #g(<g-stack>)[stack] value.], [#link("components/prelude/evaluator/stack.fs")[stack.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("over") <g-word-over>], [Copies the second #g(<g-stack>)[stack] value to the top.], [#link("components/prelude/evaluator/stack.fs")[stack.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("rotate") <g-word-rotate>], [Rotates the third #g(<g-stack>)[stack] value to the top.], [#link("components/prelude/evaluator/stack.fs")[stack.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("add") <g-word-add>], [Adds two numbers.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("subtract") <g-word-subtract>], [Subtracts two numbers.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("multiply") <g-word-multiply>], [Multiplies two numbers.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("divide") <g-word-divide>], [Divides two numbers; zero is a named refusal.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("lessThan") <g-word-less-than>], [Compares two numbers.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("greaterThan") <g-word-greater-than>], [Compares two numbers.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("equals") <g-word-equals>], [Compares two values.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
  [#raw("print") <g-word-print>], [Appends output to the actor's output.], [#link("components/prelude/evaluator/evaluator.fs")[evaluator.fs]], [#link("development/scale/tests.fs")[development/scale/tests.fs]],
)

=== Expression IR <g-expression-ir>
The *Expression IR* is `ExpressionAst`: the surface/elaborated expression
language. It holds literals, words, calls, pipelines, explicit partial
application, quotations, blocks, `let`, `lambda`, `if`, `pattern-match`,
declarations, conditions, restarts, generics, and messages. It is code, not
values, and it is host-independent.

It includes a `PatternMatch` node whose clauses contain
#g(<g-pattern-ir>)[Pattern IR] trees. That is composition, not a second
expression language: expressions produce values; patterns bind names.

What belongs here:

+ application forms: `Call`, `Pipeline`, `ReversePipeline`, `Partial`;
+ value-producing forms: literals, `Quotations`, `Blocks`, `Let`, `Lambda`,
  `If`;
+ declaration and runtime forms: match, conditions, restarts, generic
  functions, messages;
+ source offsets and portable-document tags needed by diagnostics and tooling.

What does not belong here:

+ stack effects, slots, closure instructions, or kernel execution;
+ pattern binding modes, exhaustiveness, or pattern decision trees;
+ kernel image tags and ABI encoding.

The Expression IR lowers to the #g(<g-microkernel-prelude>)[kernel/Prelude]
boundary: pipelines and partials desugar to calls or closures, then the
lowering path produces `LoweredInstructions`, and the term runtime uses
#link("components/kernel/term-ir.fs")[`TermIr`]. The pattern sublanguage is
linked as #g(<g-pattern-ir>)[Pattern IR].

Why it is separate from Pattern IR: an expression answers “what value is
produced?”; a pattern answers “which variables are bound, and is the shape
possible?”. Sharing one union would make an `if` representable in pattern
position and a pattern representable as a value, forcing every consumer to
partially validate. Keeping the two phases total is parse-don't-validate at the
IR level.

Definition: #link("components/core/expression-ast.fs")[expression-ast.fs],
#link("docs/architecture/general-ir.typ")[docs/architecture/general-ir.typ].
Used in: #link("components/core/prose-parser.fs")[prose-parser.fs],
#link("components/prelude/evaluator/lowering.fs")[lowering.fs].

=== Pattern IR <g-pattern-ir>
The *Pattern IR* is `PatternAst`: the pattern sublanguage used by
`ExpressionAst.PatternMatch` and by the pattern analyses. It holds wildcards,
variables with usage modes (`Fresh`, `Repeated`, `Pin`, `Shadowing`),
constructors, typed patterns, and literal patterns; guards and clauses live at
the expression clause boundary.

What belongs here:

+ pattern shape and binding: `Wildcard`, `Variable`, `Constructor`,
  `TypedPattern`, literal patterns;
+ binding intent: fresh, repeated, pinned, shadowing;
+ pattern identity and source position for diagnostics;
+ the portable pattern document and its kinds/predicates.

What does not belong here:

+ expression evaluation, calls, lambdas, or control flow;
+ variable values or stack effects;
+ a whole program or a top-level expression.

The Pattern IR is embedded in the Expression IR, not a peer top-level IR:
`ExpressionClause.Pattern` is a `PatternAst.Pattern`. It is consumed by
matching, exhaustiveness/refutation analysis, shadowing and duplicate checks,
the decision-tree compiler, the SMT bridge, and the portable pattern document.
It lowers to the ordinary expression/lowered control flow; the kernel sees no
pattern node.

Why it is separate from the #g(<g-expression-ir>)[Expression IR]: a pattern
binds names and has its own validity rules; an expression produces values and
has effects. Merging them would couple unrelated schema versions, refusal
types, and analyses, and would put pattern semantics into every expression
consumer. The split lets pattern compilation and matching be tested and
optimized without changing expression evaluation.

Definition: #link("components/core/pattern-ast.fs")[pattern-ast.fs],
#link("components/core/pattern-portable.fs")[pattern-portable.fs],
#link("components/prelude/evaluator/pattern.fs")[pattern.fs].
Used in: #link("components/core/expression-ast.fs")[expression-ast.fs],
#link("docs/specification.typ")[docs/specification.typ] (7).

=== Term IR <g-term-ir>
The kernel's core executable representation, `TermIr`: literals, words,
quotations, branches, slots, closures, and failure. `TermProgram` is the
canonical `schema:1`, `form:"term-program"` document. The active program is a
`Term list`; a quotation is a #g(<g-code-value>)[code value]. It is the input to
#g(<g-pattern-ir>)[pattern] and #g(<g-expression-ir>)[expression] lowering, not
a surface language. See #g(<g-lowered-ir>)[Lowered IR] for the compilation
target and #g(<g-receipt>)[receipt] for the verified form.
Definition: #link("components/kernel/term-ir.fs")[term-ir.fs],
#link("docs/architecture/term-ir.typ")[docs/architecture/term-ir.typ].
Used in: #link("components/prelude/evaluator/program.fs")[program.fs],
#link("development/scale/tests.fs")[tests.fs].

=== Lowered IR <g-lowered-ir>
`LoweredInstructions` is the compilation target: literals, local slots, branches, labels,
word calls, closures, and failure. `LoweredAbstraction` executes it; the AOT
writer emits it. It is not the source language and does not carry surface
syntax. It is produced from #g(<g-expression-ir>)[Expression IR] and shares the
runtime value model with #g(<g-term-ir>)[Term IR].
Definition: #link("components/kernel/lowered-ir.fs")[lowered-ir.fs],
#link("components/kernel/lowered-abstraction.fs")[lowered-abstraction.fs].
Used in: #link("components/prelude/evaluator/lowering.fs")[lowering.fs],
#link("development/scale/tests.fs")[tests.fs].

=== Code value <g-code-value>
A quotation value is `[QuoteTag; documentId]`; `documentId` names the canonical
term-program document for the body. It is code, not token text: `apply` decodes
it and splices its terms. It is the runtime form of a #g(<g-quotation>)[quotation]
and the value a #g(<g-term-ir>)[Term IR] `Quote` pushes.
Definition: #link("components/prelude/evaluator/code.fs")[code.fs],
#link("docs/architecture/term-ir.typ")[docs/architecture/term-ir.typ].
Used in: #link("components/prelude/evaluator/control.fs")[control.fs],
#link("development/scale/tests.fs")[tests.fs].

=== Receipt <g-receipt>
`VerifiedTermProgram` is the opaque capability issued by `TermVerification`. It
carries the program and a SHA-256 digest of its canonical bytes; the portable
form is `term-receipt` JSON. `scale_verify` issues it; execution enforcement is
separate. See #g(<g-term-ir>)[Term IR] and #g(<g-kernel-abi>)[Kernel ABI].
Definition: #link("components/kernel/term-verification.fs")[term-verification.fs],
#link("docs/architecture/term-ir.typ")[docs/architecture/term-ir.typ].
Used in: #link("components/kernel/abi.fs")[abi.fs],
#link("development/scale/tests.fs")[tests.fs].

=== Microkernel / Prelude split <g-microkernel-prelude>
The architecture separates a small trusted *kernel* (the abstract machine, the
C ABI, the image loader, the structural verifier, and the execution safety
rules) from the *Prelude* (the language: word semantics, types, effects,
classes, dispatch, conditions, actors, macros, and lowering). The kernel is the
machine; the Prelude is the language. The split is not only a way to support
several host languages.

The benefits, beyond one shared language core:

+ #strong[Small trusted computing base.] The code that can corrupt the machine,
  allocate outside the image, or authorize execution is small enough to audit
  and to keep stable. Language growth does not enlarge the TCB.
+ #strong[Independent evolution.] A new word, type rule, effect, class, pattern,
  or pipeline desugaring lands in the Prelude image. The kernel and its ABI
  version do not move unless machine behavior changes.
+ #strong[Fault isolation and attribution.] A language bug is a Prelude failure
  with a Prelude layer; a kernel/ABI failure is a separate class. A supervised
  actor can fail and restart without taking down the kernel or its siblings.
+ #strong[Security and capabilities.] The kernel performs no host I/O and knows
  no host semantics. Host calls, transport, files, and secrets are explicit
  capabilities at the adapter edge; the AOT kernel does not execute dynamic
  host code.
+ #strong[Portability and conformance.] One portable Prelude image and one narrow
  ABI serve every adapter. A new host is a marshalling adapter plus the same
  conformance suite, not a second implementation of the language.
+ #strong[Performance and specialization.] Hot machine primitives stay native;
  the Prelude can be swapped, JIT-ed, or specialized per host without an ABI
  break. The alternative is N hosts times M features of duplicated semantics.
+ #strong[Deployment and live operation.] The Prelude is swappable data. A live
  instance can change code through the actor protocol, and rollback is a
  history operation; the kernel process need not restart.
+ #strong[Testability.] Kernel boundary tests run without the language; Prelude
  tests run without an FFI; the differential corpus compares interpreter and
  lowered paths; conformance compares every adapter. The layers fail
  independently and visibly.
+ #strong[Organizational scaling.] Kernel, language, verification, and adapter
  work can proceed on separate contracts and release cadence. The ABI header and
  the invariants are the coordination points, not a shared mutable codebase.
+ #strong[Formal reasoning at the right layer.] The kernel has a small operational
  semantics; the Prelude owns the rich type rules; SMT/domain verification is a
  third property. A proof or verifier does not have to model the whole host.
+ #strong[Incremental migration.] The F\# Prelude remains the reference while
  the Scale Prelude is ported feature by feature. Both can run against the same
  tests, and a feature can fall back during the transition.
+ #strong[Heterogeneous actors and IPC.] Instances can run different Preludes or
  host languages while sharing one actor wire protocol. Language-level
  send/receive, remote handles, monitors, and supervision cross the boundary as
  data, not as a shared host heap.
+ #strong[Observability and proof.] The verifier issues a receipt bound to the
  canonical term bytes; the image is append-only; errors are named values. The
  system can say which layer refused, which program was verified, and what
  changed.
+ #strong[Language pluralism.] Pattern matching, pipelines, prose, and future
  front-ends are surface frameworks that elaborate into the same core terms.
  The core does not grow a case per surface syntax.
+ #strong[Multi-tenancy and resource policy.] The kernel owns the machine and the
  adapter owns the effects, so isolation, quotas, deadlines, and supervision can
  be enforced without giving each language its own runtime.

Definition: #link("docs/architecture/term-ir.typ")[docs/architecture/term-ir.typ] (kernel/Prelude split),
#link("docs/architecture/INVARIANTS.typ")[docs/architecture/INVARIANTS.typ].
Used in: #link("docs/specification.typ")[docs/specification.typ],
#link("docs/process/decision-records.typ")[docs/process/decision-records.typ] (D95, D160).

=== Pattern-matching framework <g-pattern-framework>
Pattern matching is a feature-specific framework, not an ad hoc evaluator
branch. It has its own `PatternAst`, a portable pattern document, a lowering
path, and a decision procedure. The framework buys:

+ #strong[One syntax, many consumers.] The parser, interpreter, lowering,
  verifier, emitter, and diagnostics share one pattern tree instead of each
  re-deriving clauses from text.
+ #strong[Portable, versioned patterns.] A pattern document is host-independent
  data with a schema and kinds/predicates. It can cross an actor or instance
  boundary and be re-checked on arrival.
+ #strong[Analysis before execution.] Exhaustiveness, refutation, coverage,
  clause order, duplicate variables, shadowing, pins, repeated variables, and
  guards are checked as named failures. A missing case is not a silent false.
+ #strong[Compilation and parity.] A pattern can lower to a decision tree or
  lowered instructions, while the interpreter remains the reference. The
  differential corpus compares the two.
+ #strong[Extensibility as data.] Constructors, kinds, predicates, and guards
  are image entries. A new pattern form does not require a new kernel case.
+ #strong[Verification bridge.] Patterns carry enough structure for SMT
  obligations, witnesses, and undecided outcomes; the domain verifier stays
  separate from matching.
+ #strong[Error attribution.] Pattern failures carry the clause, pattern,
  offset, and rule, so a refusal names the next action.
+ #strong[Testing leverage.] Pattern vectors, malformed documents, property
  round-trips, and differential cases test the framework without running a
  whole program.

Definition: #link("components/core/pattern-ast.fs")[pattern-ast.fs],
#link("components/core/pattern-portable.fs")[pattern-portable.fs],
#link("components/prelude/evaluator/pattern.fs")[pattern.fs].
Used in: #link("docs/specification.typ")[docs/specification.typ] (7),
#link("development/scale/tests.fs")[tests.fs].

=== Pipeline framework <g-pipeline-framework>
A pipeline is a feature-specific surface framework with an explicit
#raw("Pipeline") node and style, elaborated to calls. It is not the
concatenative stack and it is not Elixir's macro semantics. The reverse pipe
#raw("<|") is supported for F\# package compatibility and is discouraged;
#raw("f <| x") is #raw("f(x)") and the value is on the right. The framework buys:

+ #strong[1:1 F\# semantic fidelity.] The piped value is inserted as the *last*
  argument, the operator is left-associative, and it binds looser than a call.
  Imported F\# code keeps its meaning.
+ #strong[Style as data.] `LastArgument` and `FirstArgument` are explicit and
  language-independent. An F\#-compatible surface uses the default last
  position; an Elixir-compatible surface uses the first position. Neither
  requires a kernel change or forks the core.
+ #strong[Surface preservation.] The `Pipeline` node keeps the source offset and
  style in the portable expression document; lowering desugars it to `Call`.
  Diagnostics can name the stage, not just the final call.
+ #strong[Reuse across front-ends.] Prose, a future F\#-syntax surface, and a
  host DSL can share one pipeline semantics and one portable document.
+ #strong[Static analysis and optimization.] An explicit pipeline exposes stage
  arity, argument order, effects, and dataflow. A compiler can type-check,
  fuse, or schedule stages; the value is evaluated once.
+ #strong[Testability against references.] Golden ASTs, desugaring equality,
  evaluation order, argument position, and differential runs against F\# make
  semantic drift visible.
+ #strong[Error attribution.] A missing stage, a non-callable stage, and an
  arity mismatch are distinct refusals at the stage boundary.
+ #strong[Evolution without kernel changes.] `||>`, operator sections, Elixir
+  style, or a pipeline macro are Prelude/front-end work; the kernel term set
+  stays the same.
+ #strong[Effect and capability policy.] Pipeline stages are ordinary calls, so
  the existing effect/capability checks apply uniformly; a pipeline cannot hide
  an effect in a special evaluator path.

The current boundary is explicit: a pipeline stage may carry whitespace
arguments (#raw("x |> f a")), and a call may use explicit holes
(#raw("subtract(_ 10)")). General automatic currying is not expressible; the
holes are the partial-application syntax. That is a declared limitation, not a
silent rewrite.

Definition: #link("components/core/expression-ast.fs")[expression-ast.fs],
#link("components/core/prose-parser.fs")[prose-parser.fs],
#link("components/prelude/evaluator/lowering.fs")[lowering.fs].
Used in: #link("docs/specification.typ")[docs/specification.typ] (3.10),
#link("docs/language/action/prose-surface.typ")[docs/language/action/prose-surface.typ].

=== Kernel ABI <g-kernel-abi>
The single C ABI contract in `scale_abi.h`: ten exports, one integer version
(currently 3), one independent handle per instance, callback table, status
codes, and image transfer. It is the
boundary between the kernel and a #g(<g-host-adapter>)[host adapter]; version
equality is the compatibility rule. See #g(<g-receipt>)[receipt] for `scale_verify`.
Definition: #link("components/kernel/scale_abi.h")[scale_abi.h],
#link("docs/kernel/abi.typ")[docs/kernel/abi.typ].
Used in: #link("components/adapter-c/scale_adapter.c")[scale_adapter.c],
#link("development/scale/tests.fs")[tests.fs].

=== Host adapter <g-host-adapter>
The marshalling edge for one host language: loads the kernel, checks the
#g(<g-kernel-abi>)[ABI], allocates callback buffers, and implements transport.
It contains no Scale semantics. The C adapter is the reference; the Elixir
harness is a wire peer, not a language adapter. See
#g(<g-implementation-instance>)[implementation instance].
Definition: #link("components/adapter-c/README.md")[components/adapter-c/README.md],
#link("docs/adapters/README.typ")[docs/adapters/README.typ].
Used in: #link("components/adapter-c/conformance/conformance.c")[conformance.c],
#link("adapters/elixir")[adapters/elixir].

=== Implementation instance <g-implementation-instance>
A running Scale instance with an actor scheduler, connected to other instances
by the #g(<g-actor-wire>)[actor wire]. S1 is the numeric envelope, S2 the
worker capability and supervision; language-level send uses a
#g(<g-remote-handle>)[remote handle]. See #g(<g-actor>)[Actor].
Definition: #link("components/ipc/actor-host.fs")[actor-host.fs],
#link("docs/kernel/actor-ipc.typ")[docs/kernel/actor-ipc.typ].
Used in: #link("components/ipc/language-transport.fs")[language-transport.fs],
#link("development/scale/granular-tests.fs")[granular-tests.fs].

=== Actor wire <g-actor-wire>
The frozen Cap'n Proto-compatible wire subset: one segment, a fixed data/pointer
layout, and send/reply/monitor operations. Reference vectors and the Elixir
harness pin the bytes; far pointers and multi-segment messages are out. See
#g(<g-implementation-instance>)[implementation instance] and
#g(<g-remote-handle>)[remote handle].
Definition: #link("components/ipc/wire.fs")[wire.fs],
#link("docs/kernel/binary-format.typ")[docs/kernel/binary-format.typ].
Used in: #link("components/ipc/vectors")[components/ipc/vectors],
#link("adapters/elixir")[adapters/elixir].

=== Remote handle <g-remote-handle>

A registered capability `[RemoteHandleTag; instance; capability]` naming an
actor in another #g(<g-implementation-instance>)[implementation instance]. The
language records a transport request; the host resolves and sends it. A tagged
tensor the host did not register is not a capability.
Definition: #link("components/prelude/scheduler.fs")[scheduler.fs],
#link("docs/kernel/actor-ipc.typ")[docs/kernel/actor-ipc.typ].
Used in: #link("components/ipc/language-transport.fs")[language-transport.fs],
#link("development/scale/granular-tests.fs")[granular-tests.fs].

=== Adjustable semantic construct <g-adjustable-semantic-construct>

A semantic fact at a boundary that Scale can classify, verify, and---only with a
declared reason---override. The point is not to make semantics arbitrary. It is
to keep a foreign definition's real semantics auditable while leaving an
ordinary package call simple.

#strong[Why the split.] A package call is black-box: the callee owns recursion,
initialization, diagnostics, and its body. The caller needs a fixed *call
contract*: exported name, arity, parameter and result types, effects and errors,
marshalling, and package version/ABI. It must not infer F\#'s `let rec` from the
call edge, and Elixir calls have no equivalent definition-site classification.
Importing an F\# definition is white-box: Scale is taking responsibility for the
definition-site semantics, so it must record whether the binding is
non-recursive, self-recursive, or mutually recursive, how it initializes, and
which diagnostics apply. Only the import/port lane needs the adjustable
machinery; the call lane stays a fixed contract.

#strong[How it works.] The default is automatic: read FCS's classification
(`let`, `let rec`, `let rec ... and ...`) and lower it to the Scale definition
marker. The opt-in explicit tier adds a version-controlled package manifest
keyed by binding; it still runs FCS and compares. Agreement is silent.
Disagreement is a diagnostic. Disagreement with a declared reason is accepted
and tracked; disagreement without a reason is a build error. Package modes are
pure automatic, mixed, and strict explicit. Both tiers emit one
`BindingClassification` record with name, kind, calls in body, source range, and
declared reason; the record has no tier field.

#strong[The constructs.]

+ #strong[Tier A automatic classification] --- FCS read-off is the default; no
  manifest. See #link("docs/language/action/function-values.typ")[function-values.typ].
+ #strong[Tier B explicit override] --- per-binding manifest declaration,
  checked against FCS; a disagreement requires a declared reason. See
  #link("docs/process/decision-records.typ")[D172].
+ #strong[Package modes] --- pure automatic, mixed, strict explicit; a
  per-package annotation, not a language fork.
+ #strong[BindingClassification] --- the versioned record shared by the
  definition words and the IPC bridge; it has no tier field.
+ #strong[Definition markers] --- `logic` is non-recursive and refuses a
  self-call; `cycle` is recursive and uses the plain name; `and` groups cycle
  definitions. This is a build-time check, not a convention. See
  #link("docs/language/action/function-values.typ")[function-values.typ].
+ #strong[Initialization phase] --- image construction checks definition and
  value initialization order before execution begins. See
  #link("docs/language/action/function-values.typ")[function-values.typ].
+ #strong[Diagnostic mapping] --- FS0040, the unused-`rec` warning, and the value
  restriction are re-presented with Scale source ranges. See
  #link("docs/process/decision-records.typ")[D172].
+ #strong[Import/port lane] --- the only lane that consumes Tier A/B,
  classification, initialization, and diagnostics. F\# source port:
  #link("docs/kernel/prelude-port.typ")[prelude-port.typ]; feature gate:
  #link("docs/architecture/language-feature-ir.typ")[language-feature-ir.typ].
+ #strong[Scale definition markers] --- Scale's own `logic`/`cycle` recursion
  contract, separate from package classification; registered in
  #g(<g-decision-record>)[D172].
+ #strong[Fixed call contract] --- exported name, arity, types, effects,
  marshalling, and version/ABI. This is intentionally *not* adjustable. See
  #g(<g-kernel-abi>)[kernel ABI], #g(<g-host-adapter>)[host adapter],
  #g(<g-implementation-instance>)[implementation instance],
  #g(<g-actor-wire>)[actor wire], #g(<g-remote-handle>)[remote handle], and
  #g(<g-conformance>)[conformance].

The IPC bridge may carry classification metadata `(name, kind, version)`, but
invocation still uses the fixed call contract. The bridge never serializes a
function body. Elixir calls use the fixed contract only; F\# classification is
meaningless at an Elixir call edge.

Definition: #link("docs/language/action/function-values.typ")[docs/language/action/function-values.typ],
#link("docs/process/decision-records.typ")[docs/process/decision-records.typ] (D172),
#link("docs/kernel/prelude-port.typ")[docs/kernel/prelude-port.typ].
Used in: #link("docs/kernel/prelude-port.typ")[docs/kernel/prelude-port.typ],
#link("docs/architecture/language-feature-ir.typ")[docs/architecture/language-feature-ir.typ],
#link("docs/start/glossary.typ")[this glossary].
=== Prose parser <g-prose-parser>
The controlled-prose expression parser: match/clauses, calls, pipelines,
explicit partial holes, and source positions. It produces
#g(<g-expression-ir>)[Expression IR] and is distinct from the Basic English
#g(<g-parser>)[Parser].
Definition: #link("components/core/prose-parser.fs")[prose-parser.fs],
#link("docs/language/action/prose-surface.typ")[docs/language/action/prose-surface.typ].
Used in: #link("development/scale/tests.fs")[tests.fs],
#link("bases/scale-cli/entry.fs")[bases/scale-cli/entry.fs].

=== Portable document <g-portable-document>
A canonical JSON envelope with a schema version and form, carrying no host
objects. The forms are pattern, expression, package, image, term-program, and
term-receipt. The term and pattern documents are the portable forms of
#g(<g-term-ir>)[Term IR] and #g(<g-pattern-ir>)[Pattern IR]; the expression
document is the portable form of #g(<g-expression-ir>)[Expression IR].
Definition: #link("components/core/portable-json.fs")[portable-json.fs],
#link("components/kernel/term-portable.fs")[term-portable.fs].
Used in: #link("components/core/expression-portable.fs")[expression-portable.fs],
#link("components/core/pattern-portable.fs")[pattern-portable.fs].

=== Conformance <g-conformance>
The C harness that loads a kernel through an adapter, checks the
#g(<g-kernel-abi>)[ABI] version, initialises an image, exercises a declared
primitive, checks the result, and tears down. It compiles in alpha and runs at
release. See #g(<g-host-adapter>)[host adapter].
Definition: #link("components/adapter-c/conformance/conformance.c")[conformance.c],
#link("docs/process/test-strategy.typ")[docs/process/test-strategy.typ].
Used in: #link("components/adapter-c/README.md")[components/adapter-c/README.md].

=== Decision record <g-decision-record>
A historical, numbered note in `docs/process/decision-records.typ` explaining one
semantic choice. The heading is `D<n>`; the body records `Decision`, `Why`,
`Boundary`, `Status`, and `Non-goals` (older records may use `Options`,
`Recommendation`, and `Unblocks`). Records are append-only: a later record
supersedes an earlier one, numbers are never reused, and old status/count
snapshots are not rewritten. Live status is the
#link("docs/process/commitments-ledger.typ")[commitments ledger].
Definition: #link("docs/process/decision-records.typ")[docs/process/decision-records.typ].
Used in: #link("docs/process/commitments-ledger.typ")[docs/process/commitments-ledger.typ],
#link("docs/specification.typ")[docs/specification.typ].

=== Image document <g-image-document>
The canonical, versioned Prelude image JSON: name, version, source hash, kinds,
predicates, units, exports, imports, capabilities, and primitives. It is data,
not the #g(<g-store>)[Store] execution store, and its schema version is checked
at load. See #g(<g-kernel-abi>)[Kernel ABI].
Definition: #link("components/kernel/prelude-image.fs")[prelude-image.fs],
#link("docs/kernel/binary-format.typ")[docs/kernel/binary-format.typ].
Used in: #link("components/kernel/abi.fs")[abi.fs],
#link("development/scale/tests.fs")[tests.fs].

// rename-check: historical-start
=== Historical aliases <g-aliases>
Older prose may still use these names. The current name is on the right; each
current name links to its entry above.

#table(
  columns: (auto, auto),
  align: (left, left),
  table.header(table.cell(fill: luma(240))[Old name], table.cell(fill: luma(240))[Current name]),
  table.hline(),
  [`Image` / `Heap`], [#g(<g-image-document>)[Image document] / #g(<g-store>)[Store]],
  [`ClassTag` / `class-of` / `TypeVerdict`], [#g(<g-value-tag>)[ValueTag] / #g(<g-word-value-of>)[value-of] / #g(<g-value-verdict>)[ValueVerdict]],
  [`model` / `admit`], [#g(<g-word-set>)[set] / #g(<g-word-add-member>)[add-member]],
  [`Vivid`], [#g(<g-evaluator-set-algebra>)[SetAlgebra]],
  [`Lace`], [#g(<g-scalar-read>)[ScalarRead]],
  [`Bag`], [#g(<g-vocabulary-list>)[VocabularyList]],
  [`Probe`], [#g(<g-quotation-run>)[QuotationRun]],
  [`Term` (old)], [#g(<g-unification-term>)[UnificationTerm]; current `Term` is #g(<g-term-ir>)[Term IR]],
  [`Closure`], [#g(<g-proven-closure>)[ProvenClosure]],
  [`Event` / `Step`], [#g(<g-channel-event>)[ChannelEvent] / #g(<g-history-step>)[HistoryStep]],
  [`Guard`], [#g(<g-refinement-guard>)[RefinementGuard] / #g(<g-evaluator-runtime-guard>)[RuntimeGuard]],
  [`FsharpName`], [#link("types.fs")[ImplementationName]],
  [`Spec`], [#link("repair.fs")[Specification]],
  [`withNack` / `nack`], [#g(<g-word-with-fallback>)[with-fallback] / fallback],
  [`Reifier` / `reification`], [#g(<g-refiner>)[Refiner] / refinement],
  [`snoc`], `appendElement`,
  [`BAG`], [#g(<g-vocabulary>)[Vocabulary]],
  [`FixedSpec`], `FixedSpecification`,
  [`zonk`], ["apply the substitution" in prose],
  [`Lia` as the prototype], [#g(<g-lia>)[Lia] is the mascot; Scale is the product],
)

// rename-check: historical-end

=== Tools <g-tools>
The external tools the index names. Each is linked to its own documentation.

#table(
  columns: (auto, 2fr, auto),
  align: (left, left, left),
  table.header(table.cell(fill: luma(240))[Tool], table.cell(fill: luma(240))[Purpose], table.cell(fill: luma(240))[Documentation]),
  table.hline(),
  [#strong[SageFs] <g-sagefs>], [The warm F\# REPL and MCP inner loop.], [#link("docs/process/sagefs.typ")[docs/process/sagefs.typ]],
  [#strong[MCP] <g-mcp>], [The protocol that exposes SageFs and .NET tools.], [#link("https://modelcontextprotocol.io")[modelcontextprotocol.io]],
  [#strong[LSP] <g-lsp>], [Language-server navigation and diagnostics.], [#link("https://microsoft.github.io/language-server-protocol/")[language-server-protocol]],
  [#strong[Typst] <g-typst>], [The documentation typesetting system.], [#link("https://typst.app")[typst.app]],
  [#strong[Tinymist] <g-tinymist>], [The Typst language server.], [#link("https://github.com/Myriad-Dreamin/tinymist")[tinymist]],
  [#strong[Hedgehog] <g-hedgehog>], [Property generation and shrinking.], [#link("https://github.com/hedgehogqa/fsharp-hedgehog")[fsharp-hedgehog]],
  [#strong[Z3] <g-z3>], [The SMT solver behind the obligations.], [#link("https://github.com/Z3Prover/z3")[Z3Prover/z3]],
  [#strong[Tokenfold] <g-tokenfold>], [Lossless-first shell and payload compression. Default is Tokenfold-only; RTK is optional only when raw capture is available. A wrapper failure is a tool failure, not a license to rerun raw.], [#link("AGENTS.md")[AGENTS.md §5]],
  [#strong[Fantomas] <g-fantomas>], [The F\# formatting authority.], [#link("https://github.com/fsprojects/fantomas")[fsprojects/fantomas]],
  [#strong[just] <g-just>], [The recipe runner for the gate and the count update.], [#link("https://github.com/casey/just")[casey/just]],
)
