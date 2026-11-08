# Code sharing plan

Status: low-risk extraction started; medium items require Critical Planning.

## Landed

- `components/core/path-policy.fs`: shared real-path resolution, root
  containment, hidden-path, and direct-child checks.
- Used by `DeclarativeIo` and `ShellHost`; this removes duplicate path-policy
  code without crossing the kernel boundary.
- `components/kernel/wire-core.fs`: shared JSON validation and `Result`
  sequencing for kernel/core wire modules. Protocol fields and named errors stay
  in their owning modules.

## Low risk, next

1. Shared test fixtures for portable documents and conformance vectors.
2. Shared binary framing only if a third binary protocol actually needs it.

## Medium risk, needs Critical Planning

1. `TypeCore`: extract shared type/row/set/scheme terms and unifiers from
   `TypeRetrieval` and `Unification`; keep host/reference adapters thin.
2. `ProgramPipeline`: parse -> check -> lower -> execute for prose, books, and
   CLI; host-specific policy stays at the edge.
3. `HostBoundary`: capability grant, path containment, resource release for
   `ShellHost`, `DeclarativeIoExecutor`, and `HostResources`; executors remain
   separate.
4. `WireCore`: shared canonical framing and validation, not protocol semantics.

## Obvious semantic overlap, high risk

1. `ExpressionLogic` / `TermIr`: one semantic term family plus one lowered IR.
2. `PreludeImage` / `PackageLogic` / `PackagePortable`: generated schema and
   codecs, no kernel-to-core project reference.
3. `Evaluator` / `LoweredAbstraction`: shared machine contract, not shared
   implementation; the kernel/Prelude boundary and NativeAOT rule remain.

## .NET 10 / F# 10 opportunities

- Use `System.Text.Json` source generation where NativeAOT allows it; avoid
  reflection serialization.
- Use F# 10 nullness and `[<TailCall>]` only after measuring; do not add
  annotations as decoration.
- Use `Span`/`ReadOnlySpan` in wire and path hot paths where the boundary is
  already allocation-sensitive.
- Re-check `Microsoft.DotNet.ILCompiler` after an SDK update; the current
  toolchain warning is a dependency issue, not a language issue.

## Rule

Share code downward within one dependency tier. Share contracts, schemas, and
test vectors across the kernel/host boundary. Never create a sideways dependency
through a host component.
