# Term vectors

The P1 canonical term-program documents.

Positive vectors: `number-add`, `quote`, `branch`, `closure`, `fail`.
Negative vectors: `bad-schema`, `bad-form`, `unknown-term`, `missing-field`,
`wrong-type`, `negative-slot`, `negative-capture`, `negative-arity`.

The F# test decodes each positive vector, re-encodes it, and requires the
canonical bytes to match. A negative vector must be refused by name.

This is P1 shape and serialization only. No term executes and no stack is typed;
P2 adds the kernel verifier and receipt.
