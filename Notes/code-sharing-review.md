# Code-sharing review

Status: critical cross-component review after the low-risk extraction.

## Scope

Low-risk and medium-risk sharing candidates, checked against the whole
dependency graph rather than one component at a time.

## Findings not visible in the individual checks

1. **`PathPolicy` changes shell behavior.**
   `ShellHost` previously resolved one symlink level; the shared policy resolves
   the full existing path/parent chain. That is a security improvement, but it is
   a behavior change and has no direct shared-module regression test. Existing
   shell and declarative tests exercise the callers, not the module boundary.

2. **`PortableJson` is not a project-wide JSON layer.**
   `ClassificationWire` has its own named errors and JSON helpers. Sharing
   `PortableJson` there would either erase named refusals or require a generic
   error adapter. The low-risk claim was too broad; share only the primitive
   shape, not the error model.

3. **`Wire`, `PrimitiveWire`, and `HostTokenWire` are not one wire layer.**
   `Wire` is a binary actor frame; `PrimitiveWire` and `HostTokenWire` are JSON
   protocols. `WireCore` therefore shares only JSON validation and `Result`
   sequencing; it does not merge protocol semantics.

4. **`TypeRetrieval` / `Unification` duplication is semantic, not textual.**
   They use different type namespaces, scheme fields, error types, and now both
   carry `Code`. Extracting `TypeCore` requires a contract for variables,
   schemes, lacks, and Code before code movement. A mechanical merge would
   change inference behavior.

5. **`ProgramPipeline` candidates have different authority.**
   `BookRunner` owns book capabilities and cache; `ProseShell` owns process
   effects; `Cli` owns flags and file selection. A shared pipeline needs a host
   policy interface, not a shared function body.

6. **`HostBoundary` is not one capability model.**
   Shell policy is programs/roots/environment; declarative IO is file
   capabilities/trash/unlink; IPC is opaque resource tokens. Only path
   containment and resource-release shape overlap. Do not merge the policies.

7. **Kernel/core schema duplication is intentional.**
   `PreludeImage` and `PackageLogic` cannot share a project reference without
   breaking the kernel/Prelude boundary. Generate both from one schema or share
   conformance vectors; do not link them.

8. **The build graph cost is real.**
   Adding `path-policy.fs` to `core` forces a full core graph rebuild. Low-risk
   code movement still has a high iteration cost; batch such moves.

9. **Test coverage is caller-shaped.**
   Path-policy tests cover declarative IO and shell callers, but not:
   - root equal to path,
   - missing roots,
   - multi-level symlink parents,
   - case/path-separator differences,
   - failure of `GetFullPath`.

10. **.NET 10 / F# 10 is not the current lever.**
    No current sharing blocker is solved by F# 10. `System.Text.Json` source
    generation could help NativeAOT wire code, but only after the wire contract
    is separated from protocol semantics. The ILCompiler warning is a toolchain
    dependency issue.

## Recommendation

- Keep `PathPolicy`; add direct shared-module tests.
- Keep `PortableJson` inside `core`; do not force it into IPC.
- Do not build a generic `WireCore`; share framing only if a third protocol
  actually needs it.
- Treat `TypeCore` as a Critical Planning item with a semantic contract first.
- Treat `ProgramPipeline` and `HostBoundary` as interfaces, not extracted
  implementations.
- Generate kernel/core schemas; do not add a project reference.
