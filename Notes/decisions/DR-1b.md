# DR-1b: Token representation

- Status: Accepted; Phase 1 prerequisite
- Date: 2026-11-06
- Verified against: `6fd51af7247cd84be80d15dec99f24e3524794bd` (2026-11-06)
- Depends on: DR-1

## Context

D needs an unforgeable, non-dereferenceable value that can travel through
closures, capture slots, and exported-unit results. The token is the irreducible
addition. The current value model has one runtime value shape, `Tensor`:

```fsharp
type Tensor = { Data: RealNumber[]; Shape: int[] }
```

(`components/kernel/types.fs:19`). The lowered instruction set has no token
constructor (`components/kernel/lowered-instructions.fs:39`). The image unit has
capture slot indices, not per-unit state (`components/kernel/prelude-image.fs:71`).

The ABI is not a raw-handle API today: `scale_host_call` returns opaque result
bytes (`components/kernel/scale_abi.h:135`), and `scale_transport_send` returns
only a status and has no result channel (`components/kernel/scale_abi.h:129`).
D's statement that these calls return a token therefore requires an ABI result
change, not merely a rename.

## Options

1. **Tagged integer or tagged tensor with a host-side map.** Encode a token id in
   a reserved numeric tag or tensor shape. Rejected: language code can construct
   arbitrary tensors and numbers, so the token is forgeable; a guessed id is
   dereferenceable through the host table.
2. **Reuse `TensorHandle`.** Rejected: `TensorHandle` is a public struct with
   public fields (`components/core/arena.fs:7`), not an opaque language value,
   and it is not the image value representation.
3. **New opaque runtime value kind.** Add a distinct token value kind at the
   kernel/image boundary. The value carries an opaque host-issued identifier and
   no language-level constructor, accessor, or dereference operation. Chosen.

## Decision

Choose **option 3: a new opaque token value kind**.

- The token value carries only an opaque identifier issued by the host table. It
  has no numeric projection, shape, item access, or equality that reveals the
  handle.
- The host table is the only mapping from identifier to live handle. Language
  code can move, capture, return, and release-by-obligation a token, but cannot
  dereference it.
- A token is a runtime value, not image data. A live token inside a closure or
  capture slot is execution state and is never serialized. "Export transfer"
  means an exported unit may return a token to the host; it does not mean the
  image JSON serializes a live token. If the requester intended image-level
  token persistence, that is a separate state-continuity feature and is out of
  scope.
- The ABI result encoding must distinguish a token result from ordinary tensor
  results. `scale_host_call`'s result bytes need a tagged result form. Because
  `scale_transport_send` has no result channel, D must either add an out-result
  parameter to it or add a separate acquisition/result export; this is an ABI
  version change and must be reviewed before Phase 1.
- The image schema is currently version 2. A new runtime value kind that can
  appear in capture slots and exported results requires an image-schema decision
  and version bump. This is a real cost, not an "existing field."

## Compatibility and constraints

- **Captures:** capture slots already hold runtime values; a token is a new value
  kind in that slot. No per-unit `ImageUnit` field is added.
- **Export:** an exported unit may return a token. The token is meaningful only
  with the host table that issued it. A token imported into a different host
  table must be refused as unknown; it must not be rebound by identifier.
- **Forgery:** no language instruction constructs a token. The host creates it
  only as the result of a registered host acquisition. A token id alone is not a
  capability.
- **Dereference:** no language operation resolves a token to a host handle. Only
  the host table and the host's release path do.
- **Serialization:** image export never serializes execution state, including
  live tokens. A token cannot be recovered after process restart unless the host
  separately reconstructs it; that is outside D.

## Consequences

- Phase 1 is an ABI and image-schema change, not a local wrapper. It must be
  priced before implementation.
- The token value kind must be NativeAOT-compatible: no reflection, no dynamic
  code generation, no `Assembly.Load`.
- The kernel and host adapters must agree on the tagged result form; a mismatch
  is a protocol failure, never a silent tensor interpretation.
- The token is not a multi-tenant capability and not a state-continuity token.

## Alternatives rejected

- Tagged integer/tensor: forgeable and dereferenceable.
- `TensorHandle`: public fields and wrong layer.
- Host-only opaque pointer: cannot be captured or returned through the existing
  value/capture machinery without a language value kind.

## Evidence

- `components/kernel/types.fs:19` — the single runtime value shape is `Tensor`.
- `components/kernel/lowered-instructions.fs:39` — no token instruction.
- `components/kernel/prelude-image.fs:71` — `ImageUnit` has capture indices, no
  per-unit state.
- `components/kernel/scale_abi.h:129` — `scale_transport_send` has no result
  channel.
- `components/kernel/scale_abi.h:135` — `scale_host_call` returns opaque bytes.
- `components/core/arena.fs:7` — `TensorHandle` is a public struct, not an
  opaque language value.
