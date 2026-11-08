# Agent tooling

The project keeps exactly one OpenCode plugin: a no-op Bash guard. It refuses
the exact commands `true`, `:`, and `exit 0`, because those are almost always
tool-selection slips rather than useful commands.

The earlier edit guard is removed. It blocked direct edits of `.fs`, `.fsx`, and
`.typ` files, which required a bypass marker once the full checkout's F\# LSP
stopped loading Scale projects. That was more tooling than the problem
justified. Direct exact edits are now the normal fallback when the LSP is
unavailable; SageFs verifies the result.

Why the no-op guard remains:

- prose instructions are passive, while the guard stops the call before it runs;
- the pattern is exact, so legitimate commands containing `true` still run;
- it is one file with one rule, not a routing layer.

Do not add another guard or plugin without a demonstrated failure that cannot be
fixed at the source.

## Vestige post-compaction brief

`.opencode/plugin/vestige-brief.js` is the one non-guard plugin. It runs after
`experimental.compaction.autocontinue`, queries Vestige by exact tag
(`workflow`, `policy`, `gotcha`, `agents`, `area:style`) plus `session_start`
and `codebase get_context`, and injects a short briefing into the next system
prompt. It never runs on ordinary turns. Query text is not used: Vestige finds
memories by exact handle only.

The brief reports workflow/self-improvement memories, codebase decisions and
patterns, stale code anchors, and coverage gaps such as a missing
`user-preference` tag. It is bounded to 3600 characters and fails open: a
Vestige error is logged and compaction continues.

The same hook also feeds Vestige: one digest of messages since the last cursor,
plus at most three explicit user-preference sentences. The digest is tagged
`post-compaction-capture`; preferences are tagged `user-preference`. Secrets are
redacted and tool output is excluded. `VESTIGE_FEED_DISABLED` disables the
write half independently of `VESTIGE_BRIEF_DISABLED`.

Known limits: the cursor is in memory, so a restart can repeat the last window;
the preference extractor is heuristic and only catches explicit markers; and
the older global `vestige-capture.js` may also store a pre-compaction digest
under `session-capture`. Treat this plugin as the post-compaction record and
the global capture as a fallback, not two authorities.
