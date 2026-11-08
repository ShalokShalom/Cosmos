# DR-1: Deployment reversal semantics

- Status: Accepted; Phase 1 in scope
- Date: 2026-11-06
- Verified against: `6fd51af7247cd84be80d15dec99f24e3524794bd` (2026-11-06)
- Owner: D design

## Convention note

`docs/decisions/` does not exist at the verified commit. The existing decision
convention is `docs/process/decision-records.typ` (D1-D199) with the generated
`docs/process/decision-index.typ`. The task requested `docs/decisions/`, but
`tools/consistency-check.fsx:374` rejects any Markdown under `docs/` and directs
non-canonical notes to `Notes/`. These ADRs therefore live in `Notes/decisions/`.
That is a deliberate path divergence from the task, reported rather than
silently adapted.

This is also a deliberate divergence from the repository's
one-decision-record-per-work-item budget: D is a cross-cutting kernel/host
contract and the task requires four separate reviewable decisions. If accepted,
these records should be migrated into the existing decision records and index,
or `Notes/decisions/` should be explicitly adopted.

## Context

Compiled images run through `scale_run`; image state is append-only and execution
state is never serialized. Rollback is opt-in interpreter history, and compiled
artifacts exclude history, bookmarks, and rollback (`docs/specification.typ:257`).
The compiled deployment therefore has no state-reversal guarantee today. D adds
host-owned resource tokens; the missing decision is what reversal guarantee a
compiled image gets when `scale_image(ImageImport)` replaces it
(`components/kernel/abi.fs:373`).

The choice must be testable. "Exempt" cannot mean "silently ignore"; it must have
a test that demonstrates the documented residue.

## Options

1. **Exempt and document.** Compiled images get no host-resource reversal
   guarantee. The swap protocol documents that host handles may survive a swap;
   a test demonstrates the residue. Phase 1's compiled-path release is out of
   scope. This is the smallest change and preserves the current ABI.
2. **Host-table-driven release.** The host owns the token table and releases all
   tokens attributable to the old image at swap, in reverse acquisition order.
   The kernel does not execute teardown words and does not need image-level
   lifecycle. This works for both interpreter and compiled images because the
   table is host-owned. Phase 1's compiled path is in scope.
3. **Minimal acquisition log.** Record acquisitions in a host log and replay the
   inverse log at swap. This adds a second durable structure beside the table
   and duplicates the ordering information the table already has. It is only
   justified if the table cannot be trusted across host restarts; D does not
   require restart continuity.

## Decision

Choose **option 2, host-table-driven release, for both interpreter and compiled
images**.

The host table is the authority:

```text
token -> (live handle, acquirer image, acquirer unit,
          current holder image, current holder unit, acquisition sequence)
```

At swap, after quiesce, the host releases live entries in descending acquisition
sequence. Release is keyed to the acquisition sequence, not to the teardown of
the current holder. A transferred token therefore releases once, at the
acquirer's position, even if the holder's unit is torn down first.

The kernel does not run teardown words and does not gain a unit lifecycle. This
keeps the change at the host boundary and avoids a per-unit activation state
machine. The compiled path is exercised by a test that runs a compiled image,
acquires a host token, swaps the image, and asserts table emptiness.

## Consequences

- Phase 1 includes compiled-path release. Interpreter-only tests are not
  sufficient for the definition of done.
- The host must implement the table and the swap protocol; the kernel remains
  unaware of live host handles.
- A host that does not implement the table gets no guarantee, so the ABI must
  make the obligation visible rather than silently accepting tokens.
- The guarantee is "no live handles attributable to the old image after a
  successful swap". It is not "no external effects": a token cannot un-send a
  transport message or un-write a console line.
- The guarantee is not multi-tenant isolation and not state continuity.

## Alternatives rejected

- **Exempt** was rejected because it leaves the only residue class D exists to
  address and makes the definition of done weaker than the design's claim.
- **Minimal acquisition log** was rejected because it duplicates the table's
  ordering data and adds restart semantics D does not require.
- **Unit teardown words** were rejected because units have no per-unit state and
  the kernel has no teardown callback; label subtraction would not prove handle
  release.

## Evidence

- `components/kernel/prelude-image.fs:71` — `ImageUnit` has no effects, exports,
  or per-unit state.
- `components/kernel/prelude-image.fs:81` — exports are image-level.
- `components/kernel/abi.fs:373` — `ImageImport` validates and replaces the
  retained image.
- `docs/specification.typ:257` — rollback is interpreter-only; compiled
  artifacts exclude it.
- `components/prelude/evaluator/signatures.fs:40` — most words are
  `NotInferred`; the static layer cannot be the only release proof.

## Open

- DR-1b must settle token representation before Phase 1.
- DR-2 must settle release failure mid-swap.
- DR-3 must settle mailbox policy at quiesce.
