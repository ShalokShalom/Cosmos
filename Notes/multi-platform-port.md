# Brief for the Implementation Agent

## Mission

Refactor the existing F# Scale implementation into a **three-layer architecture**: a small native kernel, a self-hosted Scale prelude, and per-host adapter bindings. This is to port the language to multiple different platforms, who then communicate via the actor models inter-process communication, optionally. The F# codebase you have been given is correct in semantics but wrong in shape. Your job is to fix the shape *in F# first*, verify it against the existing test suite, then port the Prelude to Scale and produce the C ABI bindings.

Do not redesign semantics. The F# code is the specification. Your job is restructure, port, and bind — in that order.

---

## The three layers you are producing

**Layer 1 — Kernel (~2,000–2,500 lines, NativeAOT F# shared library).** The abstract machine and its primitives. Reducer, stack, arithmetic, store, tensor, quotations (`call_quotation`, `try_call`, `call_with_stack`), control flow (`{`, `if`, `apply`, `define`), closures, history/rollback, and the *state mechanics* of scheduler and channels (not policy). Exposes nine C ABI exports and consumes five callback pointers. Does not know what dispatch, classes, conditions, sets, unification, guards, ownership, or supervision are.

**Layer 2 — Prelude (~15,000 lines, Scale).** Everything that manipulates store and stack and calls `call_quotation`. Dispatch, class precedence, unification, set algebra, ownership checking, guards, word metaobjects, channel events, condition semantics, supervision policy, ready queue ordering, monitors, registry, links. Loaded at kernel init as a byte buffer. Versioned independently from the kernel. This is the file the F# code becomes after porting.

**Layer 3 — Host adapters (~300 lines each, per host language).** Load the shared library, wire the callback table, marshal between Scale tensors and host values, implement Scale IPC over host sockets. **No Scale semantics.** One adapter per host: Julia, .NET, BEAM (Rustler), and any others added later. Each conforms to the same frozen ABI header.

---

## The ABI is singular; the bindings are plural

There is **one** C ABI specification (`scale_abi.h`), designed once, frozen before the port begins, and versioned (`SCALE_ABI_VERSION 2`). There are **N** per-host bindings, one per host language, each implementing the same specification using the host's native FFI idiom. Changing the spec is a breaking change to every host and should be avoided; changing a binding affects only that host.

The exports (all arguments `double`/`int`/`ptr`/`size_t`):

```
scale_init(callbacks, prelude_bytes, prelude_len, out_handle) → int
scale_set_callbacks(handle, callbacks) → int
scale_run(handle, program, len, out_result, out_len) → int
scale_verify(handle, program, len, out_result, out_len) → int
scale_transport_send(handle, target, ntarget, payload, npayload) → int
scale_host_call(handle, pkg, npkg, fun, nfun, args, nargs, out_result, out_len) → int
scale_image(handle, direction, in_bytes, in_len, out_bytes, out_len) → int
scale_free(handle)
```

`scale_init` installs the callback table and validates the image. `scale_set_callbacks`
replaces that table for a live handle. `scale_run` treats its bytes as an exported
unit name and executes it in the kernel; `scale_image` exports or imports the
retained image; `scale_host_call` forwards a named package/function call;
`scale_transport_send` forwards a framed target/payload to the host.

The callbacks: `alloc`, `free`, `host_dispatch`, `log`, `abort`. Nothing else crosses. Every payload is a byte buffer; the host adapter decodes it.

---

## Phase 0: Restructure F# (3–4 weeks)

This happens **before** any port. Porting an unstable target wastes the port. The existing test suite is the correctness check throughout.

### Step 1 — Split `types.fs` (precondition for everything)

Split into `Kernel.Types`, `Prelude.Types`, `Edge.Types`:

- **Kernel:** `EntryIdentity`, `RealNumber`, `Tensor`, `Lexicon`, `Store`, `EpochState`, `Diagnostic`, a trimmed `EvaluationError` (~15 cases), and a trimmed `EvaluationState`.
- **Prelude:** `Actor`, `SchedulerState`, `Channel`, `MethodFrame`, `HandlerFrame`, `OwnershipState`, `SupervisionPolicy`, `ActorSpec`, `ExitReason`, `RestartType`, `SupervisionStrategy`, plus the ~85 Prelude-specific cases from `EvaluationError` consolidated into `PreludeError(code: int, message: string)`.
- **Edge:** `VocabularyEntry`, `WordCategory`, `Condition`, `ComparisonOperator`, `SentencePattern`, `ParsedSentence`, and the grammar helpers (`conditionText`, `parseConditionText`, `mapConditionField`).

### Step 2 — Trim `EvaluationState` to ~12 kernel fields

Keep: `Store`, `Stack`, `VocabularyTable`, `UserWordTable`, `BindingTable`, `WordTable`, `HistoryTable`, `Program`, `Reductions`, `Consumed`, `Deadline`, `Location`, `Baseline`, `Position`, `Output`, `PendingError`, and the host seam. The implemented seam is `KernelState<'Host>`: the store, the operand stack, the pending program, and one opaque host value. A host value is not a field of `EvaluationState`.

Everything else (`SetTable`, `DeclarationTable`, `GenericFunctionTable`, `ClassTable`, `InstanceStateTable`, `CurrentFrame`, `CallFrames`, `MethodFrames`, `Handlers`, `Ownership`, `SchedulerState`) becomes **store roots that Prelude manages**. Prelude reads the root, reads the vector, modifies, writes a new entry, updates the root — using `addToEnvironment` and `findPair`, which stay as kernel primitives. The kernel does not know what these roots mean.

### Step 3 — Replace `EvaluationError`

Kernel cases only: `StackUnderflow`, `EmptyValue`, `ShapeMismatch`, `UnknownIndex`, `DivisionByZero`, `UnclosedQuotation`, `UnclosedSet`, `UnexpectedSetClose`, `ReductionExhausted`, `UnmatchedReturn`, `NotAQuotation`, `NotAText`, `InvalidBookmark`, `InvalidHistoryStep`, `UnknownWord`, plus a wrapper `PreludeError(code, message)` for everything Prelude emits.

The `Violations.ofEvaluationError` function moves to the edge. It becomes a mapping from kernel error to `Violation` *and* a mapping from Prelude error code (which the Prelude supplies) to `Violation`. The edge owns that mapping entirely.

### Step 4 — Move `SchedulerState` to store roots (the hard one)

This is the single largest piece of work and the one that determines whether the port succeeds. `SchedulerState` becomes a set of store roots:

- `actors-root` → vector of actor record IDs
- `ready-queue-root` → vector of actor IDs
- `monitors-root` → vector of (ref, watcher, target) triples
- `registry-root` → association vector (name-ID, actor-ID)
- `next-actor-id-root` → scalar

The kernel keeps exactly three primitives:

- **`snapshot`** — write the current `EvaluationState` to a store entry, return the entry ID.
- **`restore`** — load an `EvaluationState` from a store entry, replace kernel state.
- **`switch-to(actor-id)`** — snapshot current, update the actor's record, restore target's snapshot.

Everything else — which actor runs next, when to restart, how to deliver, how to supervise — is a Prelude word the kernel looks up by name (`pick-next`, `on-failure`, `on-supervision-decide`) and calls. If the Prelude has not defined these (early boot, minimal prelude), the kernel uses defaults (round-robin, abort, no supervision).

**Do this in F# first, before porting.** The current `Scheduler` has subtle semantics (restart budgets, selective receive, supervision strategies, monitor firing order). Move the state to store roots in F#, run the existing suite, catch every bug cheaply, *then* port the policy to Scale. If you port the scheduler directly from the current F# shape to Scale, you will introduce bugs in two places at once and the test suite will tell you "something is wrong" but not where.

**Performance mitigation:** keep the ready queue and the current actor ID in the host value of `KernelState<'Host>` (the adapter's opaque handle) with a cached fast path. The kernel reads the fast path; Prelude updates both the fast path and the store. This preserves the self-hosting semantics while keeping `yield` and `halt` cheap.

### Step 5 — Replace `quotation-run.fs` with kernel primitives

Three new primitives: `call_quotation`, `try_call`, `call_with_stack`. All Prelude code (predicate sorts, guards, method bodies, dispatch) uses these instead of a shared helper module. Delete `quotation-run.fs`.

### Step 6 — Split `environment.fs` and `word.fs`

- `environment.fs`: `bind` and `lookup` stay in kernel; unification (`unify`, `resolve`, `term`, `typed`, `lambda-term`, `reduce`, `unify-verdict`) moves to `Prelude.Unification`.
- `word.fs`: lookup (`findByToken`, `decodeWord`) stays in kernel as a dispatch primitive; `wordWord` (the declaration) moves to `Prelude.WordMetaobjects`.

### Step 7 — Table-drive `evaluator.fs` dispatch

Replace the ~400-line `executeBuiltinWord` match with:

1. A ~15-entry builtin table (core primitives).
2. A word-table lookup for everything declared by Prelude.
3. A fall-through to `host_dispatch` for anything else.

The word-table lookup uses the kernel's `findByToken` primitive from Step 6.

### Step 8 — Move channel functions out of `store.fs`

`channelIdentityTensor`, `isChannelIdentity`, `readChannelState`, `appendChannelState` move to `Prelude.Channels`. The kernel store keeps only tensor/text/table/root primitives. If channels are Prelude (see below), these are Prelude functions that manipulate store entries exactly like any other Prelude code.

### Channels: Prelude, not kernel

`send`, `receive`, and `close` become Prelude definitions on top of a single kernel primitive **`halt-current`** (block the actor until woken). The current `receiveWord` already does this — it halts on empty — so the primitive is already effectively there, just not exposed. Move it. The kernel footprint becomes one primitive; channel semantics become Prelude-defined and portable across hosts.

### Files that require no restructuring

`lexicon.fs`, `tensor.fs`, `scalar-read.fs`, `index-transformation.fs`, `stack.fs` (EvaluatorStack), `common.fs`, `control.fs`, `closure.fs`, `history.fs`. These are already pure kernel. Add a `try_call` helper to `common.fs` (~30 lines); everything else stays.

### Files to relocate out of the kernel binary

Move to `Edge/`: `vocabulary.fs`, `vocabulary-in-image.fs`, `tokenizer.fs`, `parser.fs`, `violations.fs`, `presentation.fs`, `nearest-sentence.fs`, `discourse.fs`, `diagnostics.fs`, `scale-file.fs`, `decision-table*.fs`, `emitter.fs`, `smt.fs`, `refiner.fs`, `type-lattice.fs`, `domain-expression.fs`, `fixed-specification-acceptance.fs`.

Move to `Host/`: `entry.fs`, `cli.fs`, `vocabulary-list.fs`, `arena.fs`, and the file-I/O half of `persistence.fs` (`writeToFile`, `readFromFile`; `toBytes`/`fromBytes` stay in kernel).

### Phase 0 exit criteria

The F# kernel builds, the existing test suite passes, and the kernel binary's dependencies exclude Z3, Hedgehog, and any edge/host code. You can now freeze the C ABI and begin the port.

---

## Phase 1: Freeze the C ABI (2–3 days, during Phase 0)

Design the header now. It will not change after Phase 0 completes. Write `scale_abi.h`, the five callback signatures, and one conformance test (load kernel, init with minimal prelude, run known program, verify result). Every host binding must pass this test.

---

## Phase 2: Port the Prelude (10–12 weeks)

Feature by feature, in this order. Each port is verified against the corresponding F# reference and the existing test suite. The F# file is deleted from the compiled kernel binary only after the Scale version passes; the F# source is retained in the repository as documentation.

1. **Bookmark** (60 lines) — trivial, exercises `record-step`/`rollback-to`.
2. **Ownership** (200 lines) — pure store manipulation, no `call_quotation`.
3. **Runtime guards** (180 lines) — exercises `try_call`.
4. **Value sets** (600 lines) — exercises `call_with_stack` for predicate sorts.
5. **Set algebra** (250 lines) — builds on value sets.
6. **Dispatch** (900 lines) — the critical test: if dispatch works self-hosted, the strategy is validated. Uses `call_with_stack`.
7. **Classes** (800 lines) — C3 linearization, slots, instances, metaclasses.
8. **Conditions** (400 lines) — exercises `try_call` and `raise_condition`.
9. **Unification** (600 lines) — the heaviest algorithm; benchmark after porting.
10. **Word metaobjects** (150 lines) — trivial once dispatch works.
11. **Channel events** (700 lines) — builds on channels.
12. **Channels** (400 lines) — becomes Prelude on top of `halt-current`.
13. **Scheduler policy** (1,200 lines) — uses `spawn`, `yield`, `halt`, `snapshot`, `restore`, `switch-to`, plus the `pick-next`/`on-failure` upcalls.
14. **Cell-system encode/decode** (600 lines) — pure, no primitives.

The kernel does not change during the port unless a **reversal** is triggered (see below).

### Expect 2–4 reversals

Some feature will be too slow self-hosted. The likely candidates, in order, are: **dispatch** (called per method invocation), **unification** (called per unification), **channel fast path** (called per message). When a reversal happens, move that one feature into the kernel as a builtin (~100–200 lines of F#), delete the Prelude version, re-run the test suite. This is not a failure of the plan; it is the plan working. The kernel primitive set is versioned and additive.

Target after reversals: kernel ~3,000 lines, Prelude ~14,000 lines. If the numbers are ~3,500/~13,500, that is acceptable.

---

## Phase 3: Host adapters (~1 week per host)

Each adapter is ~300 lines. No adapter contains Scale semantics. The shape is identical across hosts:

1. **Load library.** Julia `ccall`; .NET `DllImport`; BEAM Rustler `load`.
2. **Callback table.** Julia `@cfunction` (assign to `const` to prevent GC); .NET `[UnmanagedCallersOnly]` delegates; BEAM `#[nif]` functions.
3. **Kernel handle.** Julia `mutable struct` with `finalizer`; .NET `SafeHandle`; BEAM `ResourceArc`.
4. **Object protocol.** Julia `hasproperty`/`getproperty`/`setproperty!`/`Base.invokelatest`; .NET reflection or cached delegates; BEAM `:erlang.apply/3`.
5. **Tensor marshalling.** Julia `unsafe_wrap`; .NET `Span<double>`; BEAM `Vec<f64>`.
6. **Scale IPC.** Julia `Sockets.TCPSocket`; .NET `System.Net.Sockets`; BEAM `:gen_tcp`/`:ranch`.

Order: **.NET** first (it is the reference host, and the F# code is already there), then **BEAM** (Rustler is well-trodden), then **Julia** (`ccall` is zero-overhead and the adapter is small). Each adapter passes the ABI conformance test before being considered complete.

**Rustler note:** ~200 lines of Rust for the NIF lifecycle, term marshalling, resource management. It is boilerplate, not algorithmic code. Rustler handles panic isolation and resource safety; do not hand-write the NIF glue.


## Verification at each phase

- **Phase 0 exit:** F# kernel builds, existing test suite passes, kernel binary does not depend on Z3/Hedgehog/edge/host.
- **Phase 1 exit:** `scale_abi.h` is committed, conformance test passes against a stub binding.
- **Phase 2 exit:** Prelude passes the same test suite as the F# reference. Every Prelude file has a corresponding F# reference file retained in the repo (tagged `// reference implementation, do not compile`).
- **Phase 3 exit:** each host adapter passes the conformance test. A cross-host test sends a Scale message from .NET to BEAM to Julia and back, verifying delivery and semantics.

---

## Summary

**The F# code is the specification. Restructure it first. Freeze the ABI. Port incrementally. Verify against the reference at every step. Do not put semantics in host adapters. Do not put policy in the kernel. Expect 2–4 reversals and plan for them.**

The target is ~2,500 lines of kernel, ~15,000 lines of Prelude, ~300 lines per host adapter. If you hit ~3,000 / ~14,000 / ~300 after reversals, the architecture is on track. If you find yourself writing more than ~500 lines per host adapter, or moving more than four features into the kernel, the design has drifted — stop and re-read this brief.