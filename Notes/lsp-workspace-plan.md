# LSP workspace failure and prevention plan

This note explains why Serena's F# symbol tools stopped working in the full
`Scale` checkout, and how the repository should prevent the same failure again.
It is internal development documentation; the user-facing workspace description
remains in `docs/process/jj-workspaces.typ`.

## Observed failure

A `find_symbol` request for
`components/prelude/evaluator/evaluator.fs` failed with:

```text
Couldn't find .../components/prelude/evaluator/evaluator.fs in LoadedProjects.
```

The same file exists and the project builds. The failure was not a missing file,
a stale path, or an editing mistake. The language server had no loaded Scale
project.

## Root cause

The full `Scale` checkout contains the external Nu engine at
`components/nu-engine` (source skeleton tracked; prebuilt binaries/assets fetched
separately; many `.fsproj` projects). The F#
language server starts with `automaticWorkspaceInit: true`, so it scans the
workspace root for projects. It discovered the Nu projects, attempted to build
their design-time graph, and hit a failed `Nu.Pipe.fsproj` build. That failure
poisoned the workspace load, so the Scale projects were never registered in
`LoadedProjects`.

The LSP log shows the sequence directly:

- projects under `components/nu-engine/Nu/...` are built;
- `Nu.Pipe.fsproj` and the game templates fail;
- the request then reports `Couldn't find ... in LoadedProjects`.

`just lsp-check` did not catch this because it checks that the server answers a
health request, not that the active project contains the Scale files. A healthy
server with an empty loaded-project set is still unusable for symbol work.

## Directory and Polylith model

The repository is a Polylith workspace (`workspace.edn`):

- `components/` holds the implementation nodes:
  `kernel`, `core`, `prelude`, `ipc`, `verification`, `demo`, `cli`,
  `shell-host`, `book-host`, `nu-host`, and `adapter-c`;
- `bases/` holds entry-point compositions: `scale-cli` and `nu-game`;
- `projects/` holds deployables: `scale`, `game`, and `scale-tests`;
- `development/` is retired; the verification tests live under
  `components/verification/tests`;
- `components/nu-engine` is the external Nu node, not a Scale component. It is
  excluded from Polylith discovery and from the sparse Scale-only workspace.

All Scale work happens in the full checkout:

| Workspace | Path | Contents | Job |
|---|---|---|---|
| `Scale` | `/home/shalok/Projects/Scale` | full checkout, including Nu | all Scale and Nu work |

`Scale-code` is an outdated secondary checkout. It is retired and must not be
used or modified. The full checkout is the only working tree, even though
FsAutoComplete currently scans the whole root rather than only the Polylith
nodes.

## Resolution: the client chooses the wrong workspace

The load failure is a client-configuration problem, not a repository defect, and
it is fixed. `fsautocomplete` picks one workspace during the peek and the picker
returned `components/nu-engine/Nu.slnx` first, so the Nu projects were built and
their failure poisoned `LoadedProjects`; `Scale.slnx` was never chosen.

Measured in this checkout (fsautocomplete 0.85.4, real `initialize` handshake):

| Initialization options | Peek result | Component file symbols |
|---|---|---|
| defaults (`AutomaticWorkspaceInit=false`, peek depth 2) | nothing loads | `not in LoadedProjects` |
| `AutomaticWorkspaceInit=true`, peek depth 4 | `Nu.slnx` chosen, Nu build fails | unusable |
| `AutomaticWorkspaceInit=true`, peek depth 4, `ExcludeProjectDirectories=["nu-engine"]` | `Scale.slnx` chosen, every project builds | `documentSymbol` returns `EvaluatorLowering`, `describeNode`, ... |

The working configuration is written into the Crush config
(`~/.config/crush/crushrc`, `lsp add fsautocomplete --init-options ...`). Nu stays
in the repository and in the Polylith list; only the language server's scan
excludes it.

Two consequences worth stating:

- The language server runs its own MSBuild graph load for the projects it chooses.
  That is the client's build, not an agent build, and it does not replace
  SageFs as the compiler or the harness.
- The client must be restarted after the config changes; `lsp restart` alone
  re-spawns the previously registered options.

`just lsp-check` now asserts this instead of trusting health:
`tools/lsp-symbol-check.fsx` speaks LSP with the options above and fails with a
named reason when the workspace does not load.

## Serena's own server cannot be configured around this

Serena launches fsautocomplete through SolidLSP, and
`solidlsp/language_servers/fsharp_language_server.py` builds its launch
information with no initialization options: `ls_specific_settings["fsharp"]`
accepts only `fsautocomplete_version`. So Serena's server has no way to exclude
the Nu workspace, picks `Nu.slnx`, and answers `not in LoadedProjects` for every
component file.

That is a wrapper limitation, not a repository defect, and it is why the gate is
structured the way it is: `tools/lsp-symbol-check.fsx` speaks LSP itself with the
options that work and is authoritative, while Serena's index-file request is
reported as a warning. Symbol navigation for this checkout goes through the
configured agent client (`lsp_symbols`, `lsp_definition`, `lsp_references`).

## Why the obvious fixes are wrong

- **Deleting Nu or adding it to `.gitignore`.** It is a tracked external node
  with its own build and license. Hiding it from Git does not make the project
  graph correct and breaks the full-checkout job.
- **Treating `lsp-check` as proof.** The health check is necessary but not
  sufficient. It must include a real symbol request against a Scale file.
- **Using direct file reads and edits as the normal fallback.** That loses the
  LSP's safety and bloats context. It is a recovery path, not the daily path.
- **Loading the full checkout through `Scale-code` symlinks.** The sparse
  workspace is a working-copy arrangement over the same history; symlinks would
  reintroduce the Nu directory into the LSP root.

## Prevention plan

### 1. Keep one working tree

All work uses `/home/shalok/Projects/Scale`. `Scale-code` is retired and must not
be used, modified, or treated as a fallback. The agent must report when the full
checkout's project load fails instead of silently leaving the repository.

### 2. Make `lsp-check` prove project load

`just lsp-check` must do more than `serena project health-check`. It should:

1. start or reuse the project's language server;
2. request document symbols for one known Scale file, such as
   `components/prelude/evaluator/evaluator.fs`;
3. fail with a named message if the file is not in `LoadedProjects`;
4. report the active project root and whether `components/nu-engine` is present.

That turns the current false green into a precise failure.

### 3. Make `lsp-restart` report the real failure

`just lsp-restart` should report that the full checkout contains Nu and that
the Scale project load failed. It should not kill a healthy language server to
repeat the same scan.

### 4. Keep the workspace rule visible

`AGENTS.md`, `docs/process/jj-workspaces.typ`, and `docs/now.typ` should state the rule
in one place each: the full `Scale` checkout is the only working tree, and
`Scale-code` is retired.

`docs/process/critical-review.typ` already records the underlying limitation; the
prevention plan makes the operational rule explicit.

### 5. Keep Polylith discovery authoritative

New Scale code goes into the owning Polylith node. Do not add a new top-level
source directory that bypasses `components/`, `bases/`, or `projects/`. The LSP
project list, `just poly-check`, and the sparse workspace set all derive from
that layout.

### 6. Recovery when symbol loading fails

Report the failure, then use a direct read/edit fallback with the reason
recorded. Do not leave the repository or switch to another checkout. Keep the
fallback narrow: read the resolved region, make exact edits, and verify with
SageFs.

## Concrete next actions

1. Add a real document-symbol assertion to `just lsp-check`.
2. Add workspace-root and Nu-presence reporting to `just lsp-check`.
3. Make `just lsp-restart` report the full-checkout failure instead of
   silently repeating a full scan.
4. Add the rule to `AGENTS.md` and `docs/process/jj-workspaces.typ`.
5. Re-run the check and record the result.

## What this plan does not do

- It does not remove Nu from the repository.
- It does not change the Polylith node list.
- It does not make the full checkout a reliable Scale symbol workspace.
- It does not replace `Scale-code`; it makes the boundary explicit and testable.
