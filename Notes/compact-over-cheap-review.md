# Compact over Cheap review

Status: critical review, 2026-11-07. Non-normative.

Criteria used:

- **Contract-complete**: every public surface has a typed contract, named
  refusals, tests, and a specification statement.
- **Semantically rich**: the model covers the real cases, not a demo subset; it
  does not move semantic authority into strings, test lists, or host adapters.
- **Simple and stable**: one authority per fact; no parallel codecs, lowerers, or
  type models.
- **Costs secondary**: extra rebuilds, tests, and generated artifacts are
  acceptable after correctness, security, ABI, NativeAOT, and the documented
  test gates. Cheap glue remains acceptable below those gates.

## Findings

### Critical

1. **The IR surface profiles are descriptive, not enforced.** `profilesOf` is
   exhaustive and tested, but the parser and lowering do not consult it. A
   Question-only node can be constructed in an Action context today. This is a
   comment/test-level contract, not a complete surface contract.
2. **The prelude-port authority is still split.** `ExpressionLogic`, `TermIr`,
   `Evaluator`, and `LoweredAbstraction` remain parallel execution authorities.
   The differential matrix covers 18 token cases and 9 expression cases; it does
   not cover the whole language, patterns, question, or hot-swap state.
3. **`TypeRetrieval` and `Unification` still duplicate type variables, schemes,
   rows, lacks, and `Code`.** No `TypeCore` contract exists; a mechanical merge
   would change inference.
4. **`PreludeImage` and `PackageLogic`/`PackagePortable` still have parallel
   schemas and codecs.** There is no generated schema or shared conformance
   vector, so drift is checked only by the modules that happen to overlap.

### High

5. **The Question surface is named in the profile contract but not implemented.**
   There is runtime `unify`/`bind`/`lookup` and a pattern IR, but no Question parser,
   facts/rules/goals, question plan, or question refusals. This is the largest
   contract-complete gap between the three surfaces.
6. **`PatternLogic.PatternClause.Body` is a string.** The closed pattern document
   carries surface text while `ExpressionLogic.ExpressionClause` carries an
   expression. That is a second, untyped expression representation at the
   pattern boundary.
7. **The word registry is still four lists.** Dispatcher cases, signature
   classification, token lowering, and vocabulary entries must agree by hand.
   The new consistency gate checks only token lowering against classification.
8. **HostPolicy has adapters, not a wired host boundary.** The shell and
   declarative-IO adapters are tested directly, but declarative IO and IPC do not
   yet consume the shared policy; IPC has no adapter at all.

### Medium

9. **Function values are approved but incomplete.** Named parameters and
   period/`close` termination are implemented; currying, partial value
   signatures, recursion markers, and type annotations are still contract text.
   This is acceptable only as an explicit vertical-slice boundary.
10. **The test harness is a 17k-line monolith.** It is rich evidence but weak
    navigation and review locality. Splitting by feature is a lower-risk
    cognitive reduction than another semantic merge.
11. **Documented counts are stale by design.** 1741 documented versus 1812
    actual checks. The alpha policy makes this an expected release-gate debt, not
    a semantic defect, but `just check` cannot be green before release.

### Fixed during this review

12. **The lowered-node completeness test was false.** It listed 15 of 20
    `LoweredNode` cases while claiming to name every expression constructor. The
    list now includes `PartialNode`, `HoleNode`, `PipelineNode`,
    `ReversePipelineNode`, and `FailNode`; the asserted count is 20.

## Verdict

The repository is **not yet Compact over Cheap** at the semantic core. The
cheap parts are appropriately cheap: path policy, wire validation, service
scripts, and the documentation grouping. The expensive parts are concentrated in
the execution authority, type core, schema generation, and Question surface. Those
are the right places to spend cost; completing a second authority would be the
wrong kind of cheap.

## Recommended order

1. Question contract and IR profile enforcement.
2. Word registry as one source for dispatch, signatures, and lowering.
3. TypeCore contract and differential proof before extraction.
4. Generated schema/conformance vectors for image and package documents.
5. Execution-authority consolidation after W1/W2 and the handler contract.

## Categorization

The rule has three tiers, not two: **Compact over Cheap** for anything whose
mistake becomes a language, security, persistence, ABI, or cross-host error;
**Cheap over Compact** for anything local, regenerable, or replaceable whose
missing generalization would only add ceremony; and **Complete over Compact,
and Cheap** for features that are not needed now but may be useful later. A
cheap implementation may sit behind a complete contract; the tier follows the
contract's authority, not the line count.

| Part | Category | Explanation |
|---|---|---|
| Kernel ABI, `scale_abi.h`, exports, callbacks, handle gate | Compact over Cheap | A wrong answer is an ABI or memory-safety failure. Every export is specified and conformance-tested. |
| Image schema, loader, hot swap, pins, rollback, retained state | Compact over Cheap | The image is persistent authority. A cheap shortcut would corrupt replay and hot reload. |
| Core lexical/parser/IR semantics (`prose-parser`, `expression-logic`, `pattern-logic`, `term-ir`) | Compact over Cheap | These define the language. Three surface profiles must share one semantic model. |
| Lowering and execution contracts (`lowering.fs`, `lowered-abstraction`, evaluator differential) | Compact over Cheap | Execution authority must be singular; a missing case is a silent semantic divergence. |
| Type system (`TypeRetrieval`, `Unification`, future `TypeCore`) | Compact over Cheap | Rows, lacks, schemes, effects, and `Code` are correctness facts; cheap inference is unsound inference. |
| Portable documents and codecs (expression, pattern, package, term, wire) | Compact over Cheap | They cross host boundaries and are versioned. A cheap codec becomes a compatibility bug. |
| Security and authority (`PathPolicy`, `HostPolicy`, capability grants, signatures) | Compact over Cheap | Default-deny and path containment are security contracts, even when the implementation is small. |
| Verification evidence (differential matrix, properties, refusals, conformance) | Compact over Cheap | This is the proof layer. A cheap test that mirrors implementation gives false confidence. |
| Question surface (facts, rules, goals, question plan) | Compact over Cheap | It is a named language surface; a shortcut would create a second untyped unification path. |
| Host adapters (shell, declarative IO, IPC, C, Elixir) | Compact over Cheap | Their contracts are complete, but each adapter's implementation should be as thin as possible. |
| Compiler and AOT writer | Compact over Cheap | Lowering/writer semantics must be predictable and NativeAOT-safe; only the optimization level is negotiable. |
| `components/demo` and sample books | Cheap over Compact | They are evidence and onboarding, not authority. Keep them small; do not grow language features for demo convenience. |
| `bases/scale-cli`, `components/cli`, flags, file selection | Cheap over Compact | CLI behavior is replaceable glue; it must call the complete core, not duplicate it. |
| `components/nu-host`, `components/nu-engine` integration | Cheap over Compact | The external Nu node is out of the sharing boundary; keep the bridge thin and versioned. |
| `workspace.edn`, `.fsproj`, project wiring, package metadata | Cheap over Compact | Build graph glue. Keep it mechanical and generated where possible. |
| `tools/consistency-check`, `architecture-check`, `spec-model` | Compact over Cheap | They enforce facts and generated authority, so their checks must be exact. |
| `tools/update-line-counts`, `docs-pdf`, `docs-clean`, `fsharp-classifier` | Cheap over Compact | Regenerable maintenance helpers; no semantic authority. |
| Canonical docs (`specification`, `now`, architecture, decisions, feature contracts) | Compact over Cheap | These are the readable contract. Generated views and PDFs are cheap artifacts. |
| `Notes/`, review packets, archived notes | Cheap over Compact | Non-normative memory; useful, but never a second specification. |
| OpenCode plugins, service files, `justfile`, shell scripts | Cheap over Compact | Local tooling. They must be small, fail open where appropriate, and never become language authority. |
| Test harness layout (`tests.fs` split, runners, fixtures) | Cheap over Compact | Organizing tests is local engineering; the test contracts themselves remain Compact over Cheap. |
| `PathPolicy` and `WireCore` helpers | Cheap over Compact, with a complete security contract | The code is small and shared; the containment/validation rules are not negotiable. |
| Component extension inventories | Complete over Compact, and Cheap | Each component keeps a future-feature inventory at the bottom of its implementation file. The current code stays compact; the inventory is not a commitment. |

## Complete over Compact, and Cheap

This tier records features that are **not required now** but are plausibly useful
later. The current implementation must stay compact and cheap; the future feature
gets a use case in the component's implementation file, not speculative code.

Every component has a `Potential extensions (not commitments)` block at the
bottom of its implementation file. `just extensions-check` enforces the marker
and at least two entries:

| Component | Implementation file | Extension examples |
|---|---|---|
| kernel | `components/kernel/abi.fs` | Chunked image transfer, feature negotiation, isolated handles, async callbacks |
| core | `components/core/runtime-types.fs` | Question AST, generated schemas, semantic-term bridge, named-field constructors |
| prelude | `components/prelude/evaluator/evaluator.fs` | Currying, local recursion, conditions, mutable locals, collection words |
| ipc | `components/ipc/actor-host.fs` | Remote send, resource leases, protocol negotiation, streaming transport |
| verification | `components/verification/formal/refiner.fs` | Ownership probe, typed-term verification, proof cache, witness minimization |
| demo | `components/demo/demo.fs` | Question/actor showcases, recovery demos, tutorial mode, benchmarks |
| cli | `components/cli/cli.fs` | Unified mode selection, watch mode, JSON output, package build, completions |
| shell-host | `components/shell-host/shell-host.fs` | Streaming pipelines, network policy, process-tree deadlines, PTY support |
| book-host | `components/book-host/book-runner.fs` | Cross-book methods, incremental cache, signing, typed spell arguments, hot reload |
| nu-host | `components/nu-host/nu-host.fs` | Engine integration, AOT game smoke, host words, remote node, editor integration |
| adapter-c | `components/adapter-c/scale_adapter.c` | Windows/macOS loading, chunked images, callback threading, feature negotiation, generated bindings |

The inventory is complete only as a **list of possibilities**; it does not widen
the current contract. When an extension becomes required, it graduates to the
appropriate tier and gets a contract, tests, and specification update.

The practical boundary is:

- If a wrong result is a **language, security, persistence, ABI, or cross-host**
  error, spend for the complete contract.
- If a wrong result is **local inconvenience, a regenerable artifact, or a
  replaceable adapter**, keep it cheap.
- If a cheap helper enforces a security or semantic invariant, the **contract**
  is complete even when the code is small.
- If a feature is **not needed now but plausibly useful later**, keep the
  complete extension inventory at the bottom of the implementation file; keep
  the current code compact and cheap until the use case is real.

## Vestige evidence

Exact-handle recall on 2026-11-07:

- `mem-0000000000000821` (design principle): the surface should be
  capability-literal, not effect-abstract. Literal host resources are
  installation-scoped tokens; `.install` is the only acquisition composition
  root. This supports a slim API that names capabilities directly instead of
  hiding them behind effect language.
- `mem-0000000000000791` (architecture): Scale has two semantic authorities —
  the evaluator's hard-coded word dispatch and the lowered image VM. The
  differential corpus covers 18 token programs and arithmetic; there is no
  mechanical link between dispatcher words and P4 signatures. A compact surface
  cannot be added without a word-registry contract.
- `mem-00000000000007b9` (limitation): `--prose-typecheck` does not infer host
  words, so shell scripts run with `--prose`. A slim API must keep host effects
  literal and typed at the boundary.
- `mem-00000000000005f5`, `mem-00000000000005f1`, `mem-0000000000000631`
  (language): the current prose path is parse → lower → run; prose is the Action
  runner, not an abstraction above Action and Question.
- `mem-0000000000000665` (decision D151): the unified Julia/Elixir source was
  dropped; separate implementation instances integrate through IPC. “One Scale
  language” must not reintroduce a multi-host source language.
- `mem-00000000000008a9`, `mem-00000000000008ad` (tooling): the
  post-compaction Vestige feed exists; the earlier audit found 244 project
  memories, no `user-preference` tag, and stale code anchors after the docs move.
  The feed now records digest and explicit preferences; the anchors were
  reanchored.

The evidence reinforces the review: the compact API must be a thin,
capability-literal surface over one semantic authority, not another word list or
host abstraction.


