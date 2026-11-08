# DR-3: Message policy at quiesce

- Status: Accepted; Phase 1 policy
- Date: 2026-11-06
- Verified against: `6fd51af7247cd84be80d15dec99f24e3524794bd` (2026-11-06)
- Depends on: DR-1, DR-2

## Context

A swap must quiesce the handle before releasing tokens. Actors have mailboxes,
channels, links, monitors, and supervision (`docs/specification.typ:787`). Actor
state is execution state and is never serialized. The specification states that
execution state is never serialized and compiled artifacts exclude rollback
(`docs/specification.typ:257`). A swap therefore has to decide what happens to
queued messages and sends that arrive during quiesce.

## Options

1. **Drain.** Deliver all queued messages before teardown. Problem: delivery can
   acquire new tokens or run arbitrary code after the decision to swap; quiesce
   may never finish.
2. **Reject at quiesce.** Reject new sends during quiesce, then drop queued
   messages for the torn-down image. Chosen.
3. **Serialize.** Persist queued messages for the new image. Rejected: this is
   state continuity, explicitly out of scope, and conflicts with "execution
   state is never serialized."

## Decision

Choose **option 2: reject new sends, wait for in-flight turns, drop queued
messages, and report the drop count**.

Protocol:

```text
Active
  -> Quiescing
       - reject new sends and new scale_run calls with a named quiesce refusal
       - wait for in-flight calls and actor turns to finish
  -> Releasing
       - release tokens in reverse acquisition order (DR-2)
  -> Activating
       - activate the new image
```

At `Quiescing`, a send to an actor owned by the old image is refused, not
queued. At the `Quiescing -> Releasing` boundary, queued messages that were
already accepted are dropped. The host reports the count per actor; the count is
diagnostic data, not serialized state. The new image starts with empty
mailboxes.

## Consequences

- Message loss across swap is explicit and measurable, not implicit.
- No message is delivered after the swap decision; no new token can be acquired
  by a late delivery.
- A host that wants graceful drain must do it before requesting the swap; D does
  not add a drain phase.
- This is consistent with "execution state is never serialized." It is not a
  replay or continuity feature.
- Actor supervision state for the old image is execution state and is discarded;
  the new image starts fresh.

## Testability

Tests must cover:

- a send during quiesce is refused with a named refusal;
- a queued message accepted before quiesce is not delivered after swap;
- the reported drop count matches the queue contents;
- the new image sees empty mailboxes;
- no token is acquired by a message delivered after quiesce.

## Evidence

- `docs/specification.typ:787` — actors, mailboxes, channels, and supervision.
- `docs/specification.typ:257` — execution state is not serialized; compiled
  artifacts exclude rollback.
- `components/kernel/abi.fs:373` — image import is the swap point.
- `components/prelude/evaluator/channel.fs` — channel and mailbox execution
  state.

## Open

- Whether quiesce refusal is a new ABI status or `SCALE_INVALID` with a
  diagnostic. This is a Phase 1 ABI detail.
- Whether actors owned by the old image must be explicitly terminated before
  release; DR-2's quarantine path may leave them suspended. D does not add a
  supervisor lifecycle.
