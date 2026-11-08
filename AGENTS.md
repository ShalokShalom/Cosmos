# AGENTS.md

This is the working agreement for Scale. Keep it short. Detailed history and
rationale live in `Notes/` and the Typst documents under `docs/`; load those
only when the task needs them.

When the user says "max tokens", use the full available token budget until the user revokes that instruction.

## 0. Preflight

Six non-negotiables. Everything below is the detail; this list is the check.

1. **SageFs is the loop**: `get_session_status` -> `send_fsharp_code` -> `run_tests`,
   and `Tests.runAll()` for the example/property suite. No MSBuild, no Fantomas.
2. **Freshness first**: a result counts only when `sourceState` is `InSync`; rebuild
   only after a persisted `.fs`/`.fsproj` edit.
3. **Symbols, not shells, for code**: `initial_instructions` once per session, then
   `get_symbols_overview`/`find_symbol`/`search_for_pattern`. No `grep`/`sed` navigation.
4. **One canonical doc set**: `docs/*.typ` is normative, `Notes/` is not, decisions are
   D-numbered, test counts stay frozen until release.
5. **Verify, then claim**: a filtered run, a stale build, or an un-rechecked doc
   statement is not evidence.
6. **Local only**: commit verified work, stage only your own files, never push.

A Crush hook (`~/.config/crush/hooks/scale-preflight.sh`) injects this list at the first
tool call of a session and refuses the commands §5 forbids.

## 1. Start here

1. Read `docs/now.typ` for the current state.
2. For the feature under change, read `docs/specification.typ` by section and
   the feature's `.typ`.
3. Use `docs/process/decision-index.typ` to find one active decision, then read that
   section of `docs/process/decision-records.typ`.
4. Do not read `docs/archive/roadmap-archive.typ`, `docs/process/decision-records.typ`, or the
   full `.typ` set end to end.

Start the F\# session with `just sage-ensure` and use SageFs for the inner loop.

## 2. Workflow

The order is always:

1. **Critical Planning**: evaluate all viable options before writing the
   contract. Read study abstracts and current implementations, audit the current
   codebase against the specification, update the specification first if it is
   stale, and compare the options. Close with a full critical review of the
   planning result: evidence, assumptions, option completeness, risks, and every
   compromise requiring user confirmation. Prefer the most complete, rich, and
   elegant solution. Do not compromise when a complete option is available; every
   considered compromise must be explicitly confirmed by the user.
   **Compact over Cheap** is the tie-breaker between technologies: choose the
   implementation with the richest complete semantics and a compact, stable
   design, not the one that is quickest to write. Implementation cost is
   secondary after correctness, security, ABI, NativeAOT, and the documented
   test gates.
   **Cheap over Compact** applies below those gates: local, regenerable, or
   replaceable glue should stay cheap rather than grow a generalized design.
   **Complete over Compact, and Cheap** covers features that are not needed now
   but may be useful later: keep a complete extension inventory at the bottom of
   each implementation file, with use cases, while the current implementation
   stays compact and cheap. The per-area table is in
   `Notes/compact-over-cheap-review.md`; when a cheap helper enforces a security
   or semantic invariant, its contract is complete even when its code is small.
2. **Documentation**: write the user-visible contract first: inputs, outputs,
   refusals, limits, non-goals. Close with a focused contract review for
   completeness, stale facts, and consistency with the specification.
3. **Tests**: derive them from that contract. They must fail for the right
   reason before the implementation exists. Close with a focused test review:
   contract-derived, red-first, no implementation-mirroring, and refusal
   coverage.
4. **Implementation**: make the tests pass with the smallest durable design.
   Close with a full critical review of correctness, edge cases, security,
   dependencies, and performance.
5. **Specification**: update `docs/specification.typ`, the feature document, and
   generated status/counts in the same work item. Close with a consistency review
   that the specification matches the implementation and the documentation.
6. **Commit**: inspect the diff and stage only the work item's files. Close with
   a mechanical review for scope, secrets, generated artifacts, and accidental
   changes. Commit the verified state locally; do not push.

Reviews are proportional to risk: full at Planning and Implementation, focused
at Documentation and Tests, consistency at Specification, and mechanical at
Commit. A failed review loops back to the owning step before proceeding.

A feature is incomplete if its documentation is missing, its tests were written
from the implementation, or its specification is stale.

Core syntax, semantics, and architecture decisions require explicit user
confirmation before implementation. A proposal is not a decision record.

Work in vertical slices: contract, tests, code, specification together. Do not
stop at a private helper and call it a feature.

## 3. Project shape

The repository is a Polylith workspace (`workspace.edn`):

- `components/`: `kernel`, `core`, `prelude`, `ipc`, `verification`, `demo`,
  `cli`, `shell-host`, `book-host`, `nu-host`, `adapter-c`;
- `bases/`: `scale-cli`, `nu-game`;
- `projects/`: `scale`, `game`, `scale-tests`;
- `components/nu-engine`: the external Nu node, not a Scale component.

`README.md`, `components/README.md`, `bases/README.md`, and
`projects/README.md` are short orientation maps. `docs/` is the canonical
documentation; `docs/pdf/` holds frozen renderings; `Notes/` is non-normative.

New files go into the owning component's `.fsproj` in dependency order. Generated
artifacts are checked in and must be byte-identical when re-emitted.

`Scale-code` is outdated and retired. Do not use or modify it. Work in the full
`Scale` checkout.

The F\# LSP loads Scale only when the client excludes the vendored Nu workspace:
fsautocomplete's peek otherwise picks `components/nu-engine/Nu.slnx` first and the
Nu build poisons the load. The working client configuration is recorded in
`Notes/lsp-workspace-plan.md`; with it, `lsp_symbols`, `lsp_definition`, and
`lsp_references` work on `.fs` files, and `just lsp-check` asserts a real
document-symbol response. Serena's own F\# server cannot be configured this way
(SolidLSP passes no initialization options), so its index-file request is a
warning, not the gate. The LSP runs its own MSBuild graph load for
the projects it chooses; that is the client's build, not an agent build, and it
does not replace SageFs. Fall back to exact direct reads/edits only when no
client is configured.

## 4. Tools

Use the tool that owns the operation. Prefer the smallest tool surface.

| Job | Tool |
|---|---|
| F\# evaluation, tests, reload | SageFs (`send_fsharp_code`, `run_tests`, `hard_reset_fsi_session`) |
| Profiling, allocation measurement | FsiMcp (`profile`, `measure`) |
| Fast project type-check without a rebuild | FsiMcp (`check_project`), for the touched graph only |
| Exact F\# edit | `read` the region, then exact `edit`; verify in SageFs |
| Typst document section | Typst MCP `doc_get_section`, `doc_rewrite_section`, `doc_insert_paragraph` |
| `.fsproj`, `.json`, `.md` | exact `edit` or `write` |
| Project/package/SDK | .NET MCP |
| External command with an effect | `bash` |

Rules:

- FsiMcp is a second FSI host, not a replacement. Acceptance evidence still comes
  from SageFs `run_tests` and `Tests.runAll()`, and freshness still comes from
  `sourceState`; FsiMcp has neither a test runner nor a freshness receipt. Its
  `load_project` and `check_project` invoke MSBuild, which is the one profiling
  exception to §5 and only for code that has already passed the SageFs loop.
- Never point two hosts at one project at the same time: a SageFs rebuild in
  flight plus an FsiMcp load is the duplicate-assembly and DLL-lock trap. When
  they must overlap, use a separate jj worktree.

- Bash is for observable commands. Never run `true`, `:`, or `exit 0` as a
  placeholder. The minimal plugin guard blocks those three forms only.
- Do not use `sed`, `awk`, `perl`, `python -c`, heredocs, or shell redirection to
  edit files. Use exact `edit`/`write` or a SageFs-run `.fsx`.
- Use F\# for repository scripts and transformations. JavaScript is only for the
  OpenCode plugin API; do not write one-off JavaScript tools. Use Fable/Xantham
  only when a task genuinely needs JavaScript output.
- Avoid `grep`/`rg` for code navigation when a symbol-aware tool is available.
  Prose with no symbol may be read directly.
- Report a fallback when the preferred tool is unavailable.

## 5. Alpha build policy

The project is in alpha. **Do not run MSBuild or anything that invokes it.**
That includes:

- `dotnet build`, `dotnet run`, `dotnet test`, `just build`, `just check`,
  `just test`, `just test-all`, `just release-test-count`, `just run`,
  `just publish`, and `just release` (the last only at the explicit release gate,
  §11);
- Fantomas in any form.

SageFs is the compiler, runner, and test harness. The one build-performance
exception is `just measure-build`; run it only for a deliberate build-time
investigation, never as part of the normal loop. The only shell NativeAOT step
allowed in alpha is `just kernel-aot` after a kernel change (D180). It publishes
the kernel shared library and runs the C conformance harness.

Every technique must be NativeAOT-compatible. Excluded: numeric `printf`,
`%A`/reflection serialization of F\# records, `Expression.Compile`, quotation
evaluation, `Reflection.Emit` outside the JIT-only backend, and `Assembly.Load`.
Use string interpolation, named formatters, and `Utf8JsonWriter`.

## 6. SageFs loop

1. `just sage-ensure`; reuse the session for this checkout.
2. Read `sourceState` before trusting an eval or test.
3. Write the smallest failing expression, then redefine until green.
4. Persist to `.fs`; `hard_reset_fsi_session` with `rebuild=true` only after a
   persisted file or `.fsproj` change.
5. Re-verify the persisted state.
6. `run_tests` for the granular tree; call `Tests.runAll()` for the full suite.

Do not `#load` or `#r` a project file the session already loaded. Do not reset
for an ordinary eval error; fix the earlier statement.

Batch persisted edits and do one hard reset per slice. Use `check_fsharp_code`
and `send_fsharp_code` for type and shape experiments before persisting. Do not
reset for documentation-only, Typst-only, or read-only work. A `.fsproj` change
forces a full graph rebuild, so save it until the slice is complete.

After persisting a batch, run `just rebuild-plan`. When it predicts a rebuild,
start `hard_reset_fsi_session rebuild=true` immediately: it returns before the
build finishes. Do independent documentation or review while it builds, and do
not poll with `wait_seconds`; check once when a test or eval actually needs the
new build. Do not start ahead when more source edits are imminent.

Status waits are a choice, not a default:

| Need | Call | Budget |
|---|---|---|
| Health, state, or freshness | `get_session_status` with `wait_seconds=0` | Instant; never blocks. |
| Build almost certainly done | `get_session_status` with `wait_seconds=5` | One short grace check. |
| Blocked and no other work | `get_session_status` with `wait_seconds=15` | Only when the next action needs the build. |
| Cold session or known long build | `get_session_status` with `wait_seconds=30` | At most once; use the time for docs/review first. |
| Normal post-`hard_reset` check | not `wait_seconds=60` | The reset already returned; check `0` after doing other work. |

`run_tests` follows the same rule: do not wait longer than the work you can do
elsewhere. If it returns a receipt, continue with review and read the receipt
once. A status call that blocks for 60 seconds is a waiting-policy mistake, not
a SageFs failure.

## 7. Documentation

- **Typst** is for presentation and user-facing documentation under `docs/`.
- **Markdown** is for internal notes for agents and humans under `Notes/`.
  Root, component, base, and project `README.md` files are short orientation
  maps; they must point to the canonical Typst documents, not duplicate them.
- Existing `.typ` files stay; do not convert them.
- Do not compile PDFs during development or on `.typ` edits. `just docs-pdf` is
  only for an explicit presentation request. Existing PDFs are frozen under
  `docs/pdf/`.
- One fact lives in one place. Link to the specification instead of repeating it
  in `now`, roadmap, ledger, and feature documents.
- Documentation budget per work item: one feature document, one specification
  update, and at most one decision record.
- Record a decision only for an irreversible or cross-cutting choice. Local
  implementation choices belong in the feature document or a `Notes/` entry.
- `docs/specification.typ`, `docs/generated/specification-status.typ`, `docs/generated/line-counts.typ`,
  `docs/generated/architecture-*`, and `docs/process/decision-index.typ` are generated or checked
  from `docs/architecture/architecture.edn`. Do not hand-maintain duplicate counts.
- Test counts and generated count files are release artifacts. Normal work leaves
  them frozen; only the release command refreshes them after the full suite,
  NativeAOT, and Fantomas. The write path requires `SCALE_RELEASE=1`, which only
  `just release` sets.
- Vestige is a memory log, not a second documentation set. Record only durable
  decisions, gotchas, and verified state; one to three memories per work item.

## 8. Code rules

- Put a durable fact in the type system where it can be correct by construction,
  else in a test that fails without it, else in a sited comment, in that order
  of preference.
- Immutable records and discriminated unions processed by pure functions are the
  default shape of code.
- Use `Option` and `Result` extensively, but beware of `None` standing in for
  multiple downstream interpretations. That signals the need for a specific DU.
- Mutation is justified on performance or algorithmic-elegance grounds, but it
  must have a clear scope boundary.
- Use interfaces to segregate deterministic and non-deterministic code.
- Use `Result` with a named refusal over strings and exceptions.
- Parse, don't validate. Put durable facts in the type system; score a change by
  the runtime checks it deletes, not the sites it touches.
- A tuple of three or more components becomes a record. Pairs are fine, except
  `string * string`.
- A stage that discards an intermediate so consumers re-derive it is wrong; pass
  the intermediate.
- Do not use the backward pipe `<|`.
- Do not add comments unless asked.
- No abbreviations in names or prose; established technology names stay.
- Prefer the smallest public surface. `private` is allowed for implementation
  details and smart constructors; do not hide a fact or refusal the component
  contract promises.
- A record of closures (function-typed fields) is a smell that requires strong
  justification. Prefer, in order: reorder to break a cycle; individual function
  parameters for a narrow scope; an interface for state shared across a boundary.
- Keep the image append-only. Never discard the image returned by
  `addText`/`addTensor`. Execution state is never serialized.
- `findPair`/`findModel` are latest-write-wins. ValueSet roots are execution
  state and must be re-threaded by a reader.
- `Vocabulary.gameVocabulary` is only a seed; consumers use
  `VocabularyInImage.readVocabulary` and respect `IsActive`.
- `Tensor.append` appends at the end; the stack top is the last element.
- Z3 `Status.UNKNOWN` is reported, never treated as a pass. Dispose `Context`
  and `Solver` with `use`.
- Emitted multi-clause preconditions are parenthesized; `==>` binds tighter than
  `&&`.

## 9. Tests and evidence

- Every fix ships a regression test.
- Every "done" claim has a test that fails without the change.
- The documented suite count is the last release baseline, not a live counter.
  Do not update it, `docs/process/test-strategy.typ`, generated status/count files, or
  `justfile` counts during normal work. Only the release command updates them,
  and only after the full suite, NativeAOT, and Fantomas have run. Until then,
  the suite may run more checks than the documents say and `docs-check` may fail
  on the count check; that is expected.
- A filtered run is not an acceptance check. Read the receipt's actual count.
- A green result is evidence only when `sourceState` is in sync.
- Test counts are generated from the suite; do not hand-edit them outside the
  scheduled release refresh.
- `just docs-check` checks the factual layer: ABI/schema versions, counts,
  decision ids, generated status, and the documentation index.
- `just freshness-check` detects a stale Debug build.
- A kernel change is not verified by JIT tests alone; run `just kernel-aot`
  after the change.

## 10. Git and multiple agents

- Commit each verified work item locally; report the hash; never push unless
  asked.
- Inspect `git status` and `git diff` before staging. Stage only the work item's
  files.
- With uncommitted work from another agent, stage only your hunks or use an
  isolated worktree.
- If a number in an append-only list is taken, take the next one. Do not
  renumber someone else's record.
- Do not use `git checkout --` on another agent's changes or on committed docs.
- Formatting is release-only. Do not run Fantomas during alpha.

## 11. Release gate

Only when the user explicitly calls a major public release:

```sh
just release
```

`just release` runs Fantomas, the full granular and example/property suites, the
NativeAOT kernel and product smoke, then refreshes test counts and generated
documentation, verifies them, and archives the product. Expected: no warnings,
all example and property checks pass, granular tests pass, the NativeAOT product
smoke passes, and the ABI/image version agree everywhere. `just check` remains
the normal handoff gate; it does not update counts.

## 12. References

Load only what the task needs:

- `docs/now.typ` — current state
- `docs/specification.typ` — normative language contract
- `docs/architecture/INVARIANTS.typ` — architectural invariants
- `docs/architecture/irs.typ` — abstractions and dependency graph
- `docs/runtime/performance.typ` — hot paths
- `docs/process/sagefs.typ` — SageFs wiring
- `docs/kernel/nativeaot.typ` — NativeAOT limits
- `docs/process/vestige.typ` — memory server
- `Notes/lsp-workspace-plan.md` — the known LSP limitation
- `Notes/agent-tooling.md` — why the no-op guard exists
