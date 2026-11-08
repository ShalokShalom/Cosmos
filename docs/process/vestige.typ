= Vestige (MCP memory server)
#strong[Status: running; exact-handle retrieval only.] The installed server is
`vestige-mcp 6.0.0` (the AUR package is `vestige-git 5.0.1.r50.g5264387-1`). It
stores memory in Strata, an append-only, hash-chained log under
`~/.local/share/vestige/log/`. This build has no embedding runtime.

#line(length: 100%)
== What is installed
#table(
  columns: (auto, 2fr),
  align: (left, left),
    table.header(table.cell(fill: luma(240))[Piece], table.cell(fill: luma(240))[Value]),
    table.hline(),
    [Binary], [`/usr/bin/vestige-mcp`],
    [Version], [`vestige-mcp 6.0.0`],
    [Transport], [stdio, or Streamable HTTP (`--http`, `127.0.0.1:3928`) for a daemon shared by clients],
    [Data directory], [`~/.local/share/vestige`],
    [Store], [Strata: an append-only log of hash-chained frames],
    [Retrieval], [exact handle only: full id, unique id prefix of 8+ characters, or exact tag],
    [Embedding runtime], [none (`embeddingsCompiledIn: false`)],
    [OpenCode config], [`~/.config/opencode/opencode.json` -> `mcp.vestige`],
    [Crush config], [`~/.config/crush/crushrc` -> `mcp.vestige`, `mcp.serena`, `mcp.tokenfold`, `mcp.sagefs`],
    [Running daemon], [`vestige-mcp --http --http-port 3928`],
)

A client that connects over HTTP reaches the one running daemon and so shares
its Strata log; a stdio client starts its own process against the same data
directory. Both were verified against the same store.

Crush uses the stdio form. Its HTTP client sends no `MCP-Protocol-Version`
header after `initialize`, and the server refuses that with `Bad Request`; a
static header works only when it equals the version the client negotiates. The
stdio form reads `~/.local/share/vestige` directly, so nothing is duplicated and
there is no token to copy. Two processes appending to one Strata log is safe
only while one writes at a time; stop the daemon for strict single-writer
behaviour.

A healthy empty store is a normal state. `memory_status` reports
`status: "empty"`, `totalMemories: 0`, and the warning
`No embedding runtime in this build - memories are found by exact handle only`.

#line(length: 100%)
== The `just` coupling
`just mcp-check` opens a real MCP session against Vestige and reads its health
(`justfile:218-248`). It calls `initialize`, `memory_status`, and
`session_start`, and fails when no answer comes back. An empty store passes.

That recipe is a #strong[liveness and session-context gate, not an ingestion
path];. `session_start` assembles status, open intentions, and code context; it
writes nothing. The recipe does not save conversation turns, and no other part
of this repository does either. Persistence is explicit.

#line(length: 100%)
== What works
- #strong[Persistence.]; `smart_ingest` stores a note or up to twenty items;
  `codebase remember_pattern` and `codebase remember_decision` store durable
  code knowledge with anchors. Tags are the index.
- #strong[Retrieval by handle.]; `recall` takes a full memory id, a unique prefix
  of at least eight characters, or an exact tag. It returns the node and its
  receipt-backed history.
- #strong[Causal retrieval.]; `causal_walk` follows recorded edges backward from
  a failing test, stack frame, CI run, version range, or logged write.
- #strong[Associative retrieval without vectors.]; `backfill` raises or lowers
  earlier memories that share an exact entity with a new memory; `ghostlink`
  proposes never-composed pairs over recorded typed edges and `weave` records
  the outcome.
- #strong[Evidence.]; every write returns an `eff-` receipt, and `receipt
  replay` re-derives the state from the log.

A checkout-level smoke test: a note tagged `vestige-check` was written with
`smart_ingest`, then retrieved twice, once by the exact tag and once by its full
id. `just mcp-check` subsequently reported the same store as `1 memories`.

#line(length: 100%)
== Semantic retrieval
Semantic similarity search is #strong[not deprecated]; it was #strong[removed by
design in 4.0] when the engine moved to Strata. The current binary has no
embeddings, no cosine index, no BM25, no full-text search, and no keyword
matching. A free-text `recall` returns:

```text
similarity_disabled: hybrid_search_filtered: embeddings, cosine, BM25, FTS,
Jaccard, and keyword or name matching are not Strata operations; pass an exact
handle
```

`FASTEMBED_CACHE_PATH`, `ORT_DYLIB_PATH`, `HF_HOME`, and
`VESTIGE_DISABLE_VECTOR_SEARCH` are no-ops in this build. The `embeddings` and
`vector-search` Cargo features are empty stubs that select no crates. There is
no flag, environment variable, or `just` recipe that turns semantic retrieval
on in 6.0.0.

Upstream's replacement for resemblance search is a retrieval model, not a
feature: #strong[exact handles, exact shared entities, and recorded edges]. In
practice:

- Resolve an entity or topic to an exact tag, then `recall` that tag.
- Use `backfill` with exact shared entities such as `person:Maya` or
  `project:Scale`; it does not rank by text similarity.
- Use `causal_walk` for "what led here?" and `ghostlink` for "what has never
  been combined?".
- Use `codebase get_context` for anchored code knowledge.

If semantic search is a hard requirement, it needs a different engine or an
external index in front of Vestige:

+ Keep 6.0.0 authoritative and add a local embedding index (for example
  `sqlite-vec` or LanceDB). Store `(vector, memory-id, tags)`, search the index,
  then call `recall(handle=<memory-id>)`.
+ Run legacy v3.1.1 as a separate store. It has the old local Nomic embeddings,
  but it uses SQLite rather than Strata, cannot share this log, and is not the
  official replacement path.

#line(length: 100%)
== What went wrong here
The first check reported an empty store because nothing had ever been ingested:
`mcp-check` starts a session and reads health, and conversation turns are not
captured automatically. Free-text retrieval then failed because the build has no
embedding runtime. Exact retrieval was never broken; it worked as soon as a
memory existed.

The operating rule is therefore: #strong[save deliberately, tag for the index,
and recall by handle]. Do not expect Vestige to search conversation history by
meaning, and do not add an embedding step to `just`; that cannot work in this
build.

#line(length: 100%)
== Post-compaction brief and feed
`.opencode/plugin/vestige-brief.js` runs only after
`experimental.compaction.autocontinue`. It writes one compact post-compaction
session digest and any explicit user-preference sentences, then queries exact
tags (`workflow`, `policy`, `gotcha`, `agents`, `area:style`),
`session_start`, and `codebase get_context` and injects one bounded briefing
into the next system prompt. It reports workflow/self-improvement memories,
codebase decisions and patterns, stale anchors, and missing coverage such as a
`user-preference` tag.

The feed is deliberately narrow: one digest per compaction, at most three
verbatim preference sentences, secrets redacted, and no tool output. The digest
is tagged `post-compaction-capture`; preferences are tagged
`user-preference`. It fails open: a query or write error is logged and
compaction continues. `VESTIGE_BRIEF_DISABLED` and `VESTIGE_FEED_DISABLED`
switch the read and write halves independently.

#line(length: 100%)
== References
- Upstream `docs/CONFIGURATION.md` --- offline-by-default, the no-op embedding
  variables, and the empty `embeddings`/`vector-search` features.
- Upstream `docs/REFERENCE.md` --- "Recall by handle, not resemblance",
  Retroactive Salience Backfill, GhostLink, and the 4.x architecture.
- `justfile` --- the `mcp-check` and `mcp-verify` recipes.
- `AGENTS.md` --- the agent-facing rule for saving and recalling memory.
