= Semiotics of the Scale language layers

Scale has three user-facing layers: **Shell**, **Prose**, and **Scale**.
Shell and Prose are the same language with and without shell capabilities;
Scale is the file-based programming layer that combines procedural Action with
unification Question. Logic is the definition language used by Scale and Prose;
it is not a fourth interaction layer.

Reading names: #text(fill: rgb("#f5c2e7"))[Arthur] is Action,
#text(fill: rgb("#cba6f7"))[Lia] is Question, and Arthur and Lia together are
Scale. Implementation anchors are #text(fill: rgb("#f9e2af"))[yellow]. The
palette and rules are in `docs/process/semiotic-style.typ`.

#table(
  columns: (1.2fr, 2.4fr, 2.4fr, 2.4fr),
  align: (left, left, left, left),
  table.header([Layer], [Semantics], [Syntax], [Pragmatics]),
  [Shell],
  [The same Scale semantics plus interactive shell capabilities: host effects, line editing, history, completion, prompts, and job control.],
  [Shell lines and prompts, with fish/Nu-style external completion protocols and host commands.],
  [Interact: explore, run commands, and complete arguments. This layer owns shell-specific behavior, not new language semantics.],
  [Prose],
  [The same language without shell capabilities: batch execution, sequencing, local binding, choice, and host words.],
  [Natural-language-shaped prose sentences, lowercase words, calls, `let ... in`, `match`/`when`/`otherwise`, and anonymous `logic (x y) = ...`.],
  [Explain and script a task in files. Prose is the non-interactive form of Shell, not a second implementation.],
  [Scale],
  [The file-based programming layer: procedural Action plus first-order unification Question, with Logic definitions for reusable semantics.],
  [Action sentences, symbolic Question facts/rules/goals (`:-`, `?-`, `,`, `.`), and `logic`/`cycle` definitions.],
  [Program: tell the computer what to do, ask what is true, and define reusable words and packages.],
)

== Semantics

All layers elaborate to the same semantic IR and type/effect contract. Action
adds imperative sequencing; Question adds unification and clause search; Logic
adds definitions and lexical recursion. Shell adds host interaction; Prose is
the same language without shell capabilities. A value or a failure means the
same thing at the IR boundary.

The shared rule is: one term family, one pattern IR, one package container, and
one lowered target. Layer-specific lowering is a projection, not a second
authority.

== Syntax

- Shell: shell lines, prompts, history, and completion requests; completion is
  a host protocol, not language syntax.
- Prose: sentence-oriented, lowercase, period-terminated, with `close` only for
  multi-line nested constructs.
- Scale / Action: the imperative subset of the same language.
- Scale / Question: symbols and ligatures — relation names, `?variables`, `_`,
  `:-`, `?-`, `,`, `.`, and `%` comments.
- Scale / Logic: `logic f x = body.`, `cycle`, shapes, generics, methods, and
  anonymous `logic (x y) = body`.

A single source form (`.scale`) carries the file layers. Shell is the
interactive entry point; it does not change the file syntax.

== Pragmatics

- Shell is for interaction: run, inspect, complete, and recover.
- Prose is for scripts and readable batch work without shell machinery.
- Scale is for programs and definitions: Action executes, Question asks, Logic
  defines.

The reader chooses a layer by interaction: shell, script, or program. The
writer can move between them without changing the underlying semantics or IR.

== Documentation split

Action and Question are **one language**, but they are documented as two learning
representations. This is deliberate pedagogy, not a second implementation:

- `docs/language/action/prose-surface.typ` teaches Action as imperative execution.
- `docs/language/question/question-surface.typ` teaches Question as symbolic facts, rules, and
  questions.
- This page and the specification state the technical truth: one grammar, one
  IR, one type/effect contract, and no separate runtime.

A learner can stay in one representation until it is comfortable; a maintainer
must see the shared core.

== Trust gradient

The layers trade hand-holding for trust as the developer goes deeper:

#table(
  columns: (1fr, 2fr, 2fr),
  align: (left, left, left),
  table.header([Layer], [Hand-holding], [Trust and safety]),
  [Shell],
  [Highest: prompts, completion, history, interactive recovery, and explicit confirmations.],
  [Lowest: it can touch the system directly. It owns security and safety code: capability policy, path containment, process and network limits, approval prompts, and sandboxed external completers.],
  [Prose],
  [Moderate: readable sentences, diagnostics, and script-level structure.],
  [Programming use, not a beginner sandbox. Host effects stay explicit; safety is inherited from the host boundary, not hidden in prose.],
  [Scale],
  [Lower: explicit Action, symbolic Question, and Logic definitions.],
  [The developer is trusted to compose effects and definitions. The contract is complete, the API stays slim, and refusals are named.],
  [Logic],
  [Lowest: definitions are read and written by developers who know the semantics.],
  [Greatest trust and least hand-holding. It is the absolute definition layer; it carries no shell conveniences and no hidden authority.],
)

Security and safety belong to the **Shell** layer because that is where direct
system interaction lives. They must not leak into Logic as language semantics.
Prose is a programming language, not a newbie mode; deeper layers deliberately
demand more knowledge and grant more trust.

== Status

Scale / Action and Logic are implemented. Scale / Question has a symbolic parser
and a first-order evaluator with rule freshening, occurs check, and limits; the
question plan and result ordering are next. Prose is the current non-interactive
execution form. Shell is not implemented; its completion and line-editing design
is a host layer, not a new semantic core.

== Open API compromises

- Shell line editing and completion: no compact cross-platform NativeAOT
  implementation is settled; a host adapter or delegated shell is likely.
- Question glyph precedence and error recovery.
- One compact question plan and binding result shape.
- One word registry under NativeAOT.
- Prose-to-Scale lowering when Prose becomes an abstraction over Action and
  Question, rather than the current Action runner.
