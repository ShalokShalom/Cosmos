# DR-2: Teardown-failure policy

- Status: Accepted; Phase 1 policy
- Date: 2026-11-06
- Verified against: `6fd51af7247cd84be80d15dec99f24e3524794bd` (2026-11-06)
- Depends on: DR-1, DR-1b

## Context

At swap the host releases live tokens in descending acquisition sequence. A host
release can fail: a transport close can return an error, a host handle can be in
an unknown state, or a callback can fail. The old image must not be silently
discarded, and the new image must not be activated while known handles remain.
The kernel has no teardown protocol today; `scale_image(ImageImport)` validates
and replaces the retained image directly (`components/kernel/abi.fs:373`).

## Options

1. **Abort on first failure and retain the old image.** Stop releasing, leave
   the old image active, and report the failing token. Problem: some handles
   were already released; the old image is no longer intact, so "active" is a
   false claim.
2. **Best-effort release, activate the new image, report leaks.** Continue
   releasing, activate new, and report leaked tokens. Problem: the new image is
   active while known host resources remain; this violates the zero-live-handle
   goal.
3. **Continue best-effort release, quarantine the old image, do not activate the
   new image.** Continue releasing all remaining tokens to minimize residue;
   record failures; mark the old image `Quarantined` and non-runnable; return an
   error naming the first failed token; require host recovery or restart.
   Chosen.

## Decision

Choose **option 3: best-effort release, quarantine, no activation**.

State machine:

```text
Active
  -> Quiescing
  -> Releasing
       -> Released
            -> Activating
                 -> ActiveNew
       -> ReleaseFailed
            -> QuarantinedOld
```

- `Quiescing`: reject new `scale_run`/host calls for this handle; wait for
  in-flight calls and actor turns to finish.
- `Releasing`: release live tokens in descending acquisition sequence. A release
  failure does not stop the remaining releases; the first failure is recorded as
  the primary error and every failure is recorded.
- `ReleaseFailed`: no new image is activated. The old image is `QuarantinedOld`,
  not `Active`; calls return `SCALE_INVALID` (or a dedicated status if the ABI
  grows one). The host may retry the swap, or restart the process.
- `Released`: all live tokens were released; activate the new image.
- `ActiveNew`: the new image is retained and runnable.

A token whose release failed remains in the table with state `Leaked` and its
original acquisition sequence. It is not silently removed. The host can inspect
the table, retry release, or restart.

## Consequences

- The old image is never represented as healthy after a partial release.
- A failed swap is a host-visible error, not a silent leak.
- The host table must distinguish `Live`, `Released`, and `Leaked`; release is
  idempotent only for `Live` entries.
- The kernel's image replacement path must be gated by the host's release
  outcome. If the host owns the table, the host performs release before calling
  `scale_image(ImageImport)`; if the kernel owns a release callback, the callback
  returns the outcome before replacement. DR-1's host-table choice makes the
  host the owner.
- A token cannot un-send a transport message or undo a console write. The
  guarantee covers acquired handles, not external effects.

## Testability

Tests must cover:

- release failure at the first token;
- release failure after some tokens were released;
- table state `Leaked` for the failed token;
- no activation of the new image;
- calls on the quarantined handle are refused;
- a successful retry releases remaining live tokens and activates the new image.

## Evidence

- `components/kernel/abi.fs:373` — image import replaces the retained image.
- `components/kernel/scale_abi.h:129` — transport send has no result channel, so
  release failure reporting needs an ABI decision.
- `docs/specification.typ:257` — compiled images have no rollback guarantee.

## Open

- Whether the ABI needs a dedicated status or uses `SCALE_ERROR` with a
  diagnostic. This is a Phase 1 ABI detail.
- Whether a quarantined old image can be recovered by a host repair operation;
  D does not require it.
