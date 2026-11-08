# Small case studies

Status: exploratory, not acceptance tests. The inventory book is exercised by the
suite; the two core programs are represented by existing actor/condition tests.

## 1. Core: retry with a condition handler

```text
{ 1 0 divide } { drop 42 } with-handler
```

Observed: the failure becomes a condition and the handler returns `42`.

Limits:

- `with-handler` has no effect label and no resume.
- The typed rule covers only literal quotations.
- `divide` and the quotations are stack words, not expression forms.
- There is no protocol type for the condition or the handler.

## 2. Core: actor ping/pong

```text
{ receive-message print } spawn
7 send
yield
yield
```

Observed: the child receives `7`, prints it, and the scheduler runs the actor
twice.

Limits:

- `spawn`, `send`, `receive-message`, and `yield` are stack words; only the
  callable host/actor subset has expression signatures.
- There is no mailbox or message protocol type.
- Supervision, links, and monitors have no static contract.
- The core is expressive but untyped at the program boundary.

## 3. Logic: inventory book

`components/verification/tests/fixtures/inventory.book`:

```text
shape item with name qty.
logic total a b = add(field("qty" a) field("qty" b)).
logic bump i = item(field("name" i) add(field("qty" i) 1)).
logic main = total(bump(item("apple" 2)) item("pear" 3)).
```

Observed: check passes and `main` returns `6`.

This is a `logic` definition module. Prose does not define `logic` words; it only
executes them through the shared IR and stack.

Limits:

- No mutation: `bump` constructs a new item.
- No anonymous functions or currying.
- No effect or handler surface in the definition language.
- No explicit type signatures or effect annotations.
- Pattern matching is outside P4 inference.
- Subtyping is not a user-facing word.

## Cross-cutting findings

1. The core is the most expressive surface, but its program boundary has no
   type contract; stack inference covers words, not quotations or protocols.
2. The prose surface is pleasant for records and pure functions, but cannot
   express effects, handlers, or protocols.
3. Effect rows exist as open label sets, but there is no surface syntax.
4. General typed handlers and self-typing remain the largest language gaps.
5. The case studies are small and do not exercise performance, failure
   injection, multi-actor supervision, or hostile host inputs.

## Proposed changes

1. Add a code-value type carrying result type and effect row; use it for general
   handlers and a self-typing representation.
2. Extend stack inference to quotations, `if`, `lambda`, and actor protocols.
3. Add prose syntax for effects, handlers, and explicit signatures.
4. Add protocol types for mailboxes, channels, and supervision.
5. Promote the three case studies to acceptance fixtures with failure cases.
