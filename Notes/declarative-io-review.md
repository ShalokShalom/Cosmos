# Declarative IO review

Status: lower-layer review; production executor not landed.

## Changed

- `components/core/declarative-io.fs`: `.install` parser, capability/policy
  model, Intent, Plan, planner, and `IPlanExecutor`.
- `components/core/Scale.Core.fsproj`: added the module at the end of the core
  compile order.
- `components/verification/tests/tests.fs`: seventeen checks for parsing,
  planning, defaults, scope containment, traversal, hidden/symlink policy,
  undecided policy, confirmation, executor dispatch, fail-fast apply, and
  parser refusals.
- `docs/runtime/declarative-io.typ`: canonical contract.
- `docs/README.typ`: documentation index entry.

## Relationship to the rest

```text
DeclarativeIo (core)
  ├── no dependency on kernel, Prelude, IPC, verification, or image code
  ├── pure except System.IO path normalization
  ├── consumed by the verification test project
  └── host adapters are future: Incus, trash, vision, watcher
```

`Scale.Core` still references only `Scale.Kernel`; `DeclarativeIo` adds no new
project reference and no cycle. `poly-check` passes with 11 components, 2 bases,
3 projects, 0 development, 1 external.

## Verified

- Suite: 1741 checks (1683 examples, 58 properties), 100/100 tests.
- `docs-check`: OK, ABI 3, 36 granular, 199 decisions.
- `line-counts-check`: current.
- `poly-check`: OK.

## Critical findings

1. **Symlink traversal is not fully closed.** `Path.GetFullPath` blocks textual
   `..` traversal, but if `FollowLinks` is true a symlink can resolve outside the
   scope. The host executor must call a realpath check and enforce the scope
   again. The planner is not the final security boundary.
2. **`FollowLinks` is now used but not resolved.** A symlink candidate is skipped
   when the policy is false and accepted when true. The executor must resolve it
   and re-check containment.
3. **`Vision` is metadata only.** The planner trusts the supplied verdicts. It
   does not call a model, enforce the threshold, or validate model id/version.
   The host adapter must attach provenance and cache by content hash.
4. **Hashes are not verified.** A plan carries a hash, but no executor or
   invalidation step checks it against the file.
5. **Confirmation is carried, not enforced.** `Plan.Confirmation` is data; an
   executor can ignore it. The host apply path must refuse a required plan
   without a confirmation receipt.
6. **No rollback.** `apply` is fail-fast and reports already-applied items, but
   does not undo them. Trash makes this tolerable; unlink does not.
7. **No schema version.** `.install` has no version field. A future grammar
   change will need one before the format is public.
8. **No watcher or mount.** There is no file discovery, remount, reverse release,
   or Incus adapter. This is the lower contract, not the live environment.
9. **No property tests.** The planner is example-tested; a property over generated
   paths/policies would be the next evidence step.
10. **Prose is absent by design.** No prose parser change; the prose surface is
    still deferred.

## Next production steps

1. Add a `realpath`/containment gate in the host executor.
2. Add confirmation receipts and plan-hash validation.
3. Add a trash adapter and an unlink adapter with explicit failure policy.
4. Add model provenance (`model`, `version`, `score`, `contentHash`) to the
   verdict boundary.
5. Add an Incus/mount adapter and reverse release.
6. Add a property test for path containment and policy combinations.
