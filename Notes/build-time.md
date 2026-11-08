# Build-time baseline

Measured on 2026-11-05 in the full `Scale` checkout.

## Machine and project

- 16 logical cores, 13.8 GB RAM, about 4.8 GB available.
- SageFs machine tier: `Constrained`; worker-ready stage: about 30 s.
- Default target: `projects/scale-tests/scale-tests.fsproj`, Debug, no restore.
- Scale source outside Nu: about 50,777 F# lines.
- Test assembly alone: about 18,036 F# lines; `tests.fs` alone is 14,432.

## Matrix

Command: `just measure-build`, or
`dotnet fsi tools/build-time.fsx <project>`.

| Target | Warm | No-op | Leaf change | Core change | Full rebuild |
|---|---:|---:|---:|---:|---:|
| `scale-tests` | 15.1 s | 4.9 s | 9.6 s | 10.7 s | 77.9 s |
| `Scale.Prelude` | 33.5 s | 2.3 s | 2.4 s | 11.1 s | 33.4 s |
| minimal Core+Prelude project | 5.0 s | 2.4 s | 2.5 s | 11.0 s | 35.7 s |

The leaf change touches `components/demo/demo.fs`; the core change touches
`components/prelude/evaluator/evaluator.fs`. Times are wall-clock.

## Full rebuild breakdown

From `/tmp/scale-build-full.log` for `scale-tests`:

- `Fsc`: **90.5 s aggregate across 12 calls**.
- `CoreCompile`: **90.5 s aggregate**.
- `MSBuild` recursive task: **332.1 s aggregate**.
- Wall time: **77.9 s**, so the project graph is the critical path, with F\#
  compiler work dominating.

Slowest project summaries in the full rebuild:

| Project | Summary time |
|---|---:|
| `scale-tests` | 75.6 s |
| `Scale.Development` | 72.2 s |
| `Scale.Demo` | 41.5 s |
| `Scale.BookHost` | 36.6 s |
| `Scale.Verification` | 36.3 s |
| `Scale.Ipc` | 36.2 s |
| `Scale.ShellHost` | 36.2 s |
| `Scale.Prelude` | 31.9 s |
| `Scale.Typing` | 23.7 s |
| `Scale.Core` | 18.2 s |
| `Scale.Kernel` | 7.4 s |
| `Scale.Cli` | 3.2 s |

These are overlapping project-summary times, not additive.

## Interpretation

The normal incremental loop is fine. No-op is 4.9 s and a prelude change is
10.7 s. The test assembly is the expensive consumer: it references nearly every
component and compiles 18k lines.

`hard_reset_fsi_session rebuild=true` measured about 90–112 s. It does a full
rebuild plus worker respawn and warm-up, not the 10.7 s incremental path. Avoid
it after every file edit.

A minimal Core+Prelude project does not make incremental builds faster: the same
prelude change still costs about 11 s. It only reduces the full rebuild from
77.9 s to 35.7 s, and with the about 30 s worker stage a reset would still be
roughly 65 s. That is a useful option for a focused session, but it is not a
substitute for batching resets.

The first measurement showed `MSB9008` warnings for stale `components/typing`
project references. They came from stale restore assets, not the `.fsproj`
files. `dotnet restore projects/scale-tests/scale-tests.fsproj` removed them.

## Reducing hard resets

1. Batch source and `.fsproj` edits per slice, then do one persisted hard reset.
2. Use `check_fsharp_code` and `send_fsharp_code` for type and shape experiments
   before persisting. Do not reset for an eval error.
3. Do not change an `.fsproj` just to reorder or add a file until the slice is
   complete; a project-file change forces a full graph rebuild.
4. Use `send_fsharp_code` for targeted checks against the changed definitions,
   and run `Tests.runAll()` once at the end.
5. Do not reset for documentation-only, Typst-only, or read-only work.
6. If a slice is confined to `core` plus `prelude`, use a focused project
   session; the measured full rebuild drops from 77.9 s to 35.7 s. Do not adopt a
   permanent extra project until this saves more than it costs in maintenance.

### Start ahead

`just rebuild-plan` classifies the working tree before a reset:

- no project or `.fs` changes: `NoRebuild`;
- any `.fsproj` or `.fs` change: `FullRebuild`.

The name is deliberate: SageFs `hard_reset_fsi_session rebuild=true` performs a
full rebuild even when an ordinary `dotnet build` would be incremental. A source
change that costs 10.7 s incrementally still costs about 90 s through a hard
reset.

`hard_reset_fsi_session rebuild=true` returns before the build finishes. After
persisting a batch, start it immediately, then do documentation, review, or
other independent work while the worker rebuilds. Wait only when a test or eval
needs the new assembly. Do not start ahead when more source edits are imminent;
the next edit makes the running rebuild stale.

A direct `dotnet build` immediately after a hard reset recompiled all 12 projects
in 79.8 s despite no source change. The reset does not leave the outputs in the
state a normal incremental build treats as up to date. Do not use a direct
build as a shortcut around the reset path; it does not remove the full-build
cost.

## Measurement tools

- `just measure-build` for the default matrix.
- `dotnet fsi tools/build-time.fsx <project>` for another target.
- `/tmp/scale-build-<scenario>.binlog` for MSBuild Structured Log Viewer.
- `-clp:PerformanceSummary` for target/task ranking.
- F\# compiler `--times` for phase timing when `Fsc` is dominant.
- PerfView or `dotnet-trace` for CPU stacks when the binlog identifies the task
  but not the cause.

## Next experiments

1. Inspect the binlog critical path to confirm whether
   `Core -> Typing -> Prelude -> hosts -> tests` is the real chain.
2. Measure whether splitting `Scale.Development` into smaller test assemblies
   reduces the critical path; start with a temporary split, not a permanent
   project.
3. Measure a no-op build after every experiment; a slow no-op means an input or
   output is forcing work, not that F\# compilation is slow.
