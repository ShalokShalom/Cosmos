= Quest: Shell tools and glue

#strong[Status: contract and first tool-catalog slice landed.] `ToolCatalog`
records the established tools, declarative aliases, safe argument builders, and
structured output decoders. External applications are owned upstream. Scale
contributes only the glue: typed invocation, safe arguments, structured output
adapters, completion metadata, capability policy, and version checks.

== Applications versus glue

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Kind], [Examples], [Rule]),
  [Applications],
  [fd, ripgrep, bat, eza, jq, fzf, sd],
  [Installed and versioned externally. Do not reimplement or vendor them.],
  [Glue],
  [argument builders, output decoders, completion, policy, manifest],
  [Owned here, compact, and tested without executing the tools.],
)

The established tool names remain the command names. A shell wrapper adds
contract and safety; it does not rename a beloved command into a new language.

== Decisions

- `fd` is the path finder; use null-separated output (`--print0`).
- `rg` is the text search; use JSON output when structure is needed.
- `bat` is the text viewer; text passes through.
- `jq` is the JSON transformer; its input/output is structured JSON.
- `fzf` is the interactive selector; it is a host capability, not a pure word.
- `eza` is the directory lister; text is the default.
- Missing tools are a named refusal, not a silent fallback.
- Installation is external package management. Do not build a binary package
  manager now. Use a pinned manifest and `pacman`/`paru`, or a future pinned
  installer if a real cross-platform need appears.
- No runtime downloads by default.

== Phases

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Phase], [Work], [Acceptance]),
  [S1], [Tool catalog: names, executables, versions, output modes, capabilities.], [Catalog and safe argument tests pass without running tools.],
  [S2], [Output decoders: null paths, rg JSON lines, jq JSON.], [Malformed output is a named refusal; decoders are bounded.],
  [S3], [Command registry and execution adapter.], [Policy and timeouts wrap invocation; no shell interpolation.],
  [S4], [Completion metadata and shell composition.], [Completion uses the provider protocol; pipelines consume structured values.],
)

== Non-goals

Vendoring Rust/Go tools, implementing a general package manager, parsing human
help text, dynamic installation during execution, and hiding flags behind a
second DSL.
