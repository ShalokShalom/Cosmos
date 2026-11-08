= Quest: Shell layer

#strong[Status: contract and first completion slice landed.] The request/response
types, fish/carapace command construction, and bounded TSV/JSON parsers are
implemented. This Quest keeps interactive behavior in the shell host; it does not
add language semantics.

== Scope

Shell owns:

- interactive input and line editing;
- prompts, history, and completion;
- direct host effects and their confirmation;
- security and safety: capability policy, path containment, process/network
  limits, sandboxed completion providers, and audit.

Shell does not own:

- Action, Question, or Logic semantics;
- definitions or a second module system;
- query evaluation;
- the kernel ABI or image format.

== Phases

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Phase], [Work], [Acceptance]),
  [S0], [Contract: layer boundary, trust gradient, no new semantics.], [Shell is documented as a host layer; all executable behavior lowers to Prose/Scale.],
  [S1], [Completion protocol: request, response, fish provider, carapace provider, limits.], [Pure protocol and adapter tests pass; no shell interpolation; timeout and candidate caps.],
  [S2], [Line editor, history, prompt, and selection UI.], [Host adapter is isolated; core has no terminal code.],
  [S3], [Security and safety: approvals, capability policy, process/network containment.], [Default-deny tests and explicit refusal paths.],
  [S4], [Integration: Prose input, Scale execution, Question results, shell effects.], [One interactive path with the same semantic core.],
)

== Decisions

- Completion is an external protocol, modeled on Nu's external completers:
  a request carries the command word list and cursor; the provider returns
  candidates with optional descriptions.
- Fish is the default provider because `complete --do-complete` already covers
  many tools. Carapace is an optional richer provider.
- The shell passes requests via stdin/structured arguments, never by
  interpolating user text into a shell command.
- Providers are default-deny host capabilities with timeouts and bounded output.
- A full line editor is a host adapter, not a language feature.

== Non-goals

A second parser, a second evaluator, embedded completion scripts in the core IR,
and terminal code in `components/core` or `components/kernel`.
