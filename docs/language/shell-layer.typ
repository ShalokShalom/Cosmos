= Shell layer

#strong[Status: contract; completion protocol first slice implemented.] The
protocol, fish command builder, carapace command builder, and bounded TSV/JSON
parsers are implemented. A line editor, history, prompt, selection UI, provider
process runner, and the full safety policy are later phases.

Shell is the interactive layer of Scale. It composes Prose, Scale, and their host
effects; it does not add a second language, parser, or evaluator.

== Completion request

A completion request is structured data:

```text
CompletionRequest =
  line: string
  cursor: int
  command: string list
  cwd: string
```

`command` is the already-split command word list, with the current partial word
as its final element. `line` and `cursor` preserve the unmodified input for
providers that need source positions.

A provider returns candidates:

```text
CompletionItem =
  value: string
  description: string option
  span: (start, end) option
```

The shell owns insertion, quoting, and the replacement range. A provider never
edits the line.

== Providers

The default provider bridges fish:

```text
fish --command "complete '--do-complete=<line>'"
```

Fish returns tab-separated `value<TAB>description` lines. The adapter handles
missing descriptions, quotes paths correctly for Scale, and bounds output.

Carapace is an optional provider:

```text
carapace <command> <shell> ...words
```

Its output is parsed as structured candidates. A provider that fails, times out,
or exceeds the candidate cap fails open: the shell keeps the typed text and
offers no completion rather than blocking the session.

Providers are selected by configuration. The order is:

1. a Scale/Prose-specific provider for the command;
2. carapace for a richer native result;
3. fish as the broad fallback;
4. file and command completion built into the shell.

== Security and safety

Shell is the highest-trust layer and therefore carries the strongest guards:

- provider execution is a capability, not implicit shell interpolation;
- requests pass through stdin or argument arrays, never through a constructed
  shell string;
- providers have a timeout, candidate cap, and output cap;
- paths are quoted by the shell, not trusted from provider text;
- direct effects require the same `HostPolicy` checks as Action;
- external completions are never evaluated as Scale code.

The shell may prompt, refuse, or degrade; it must not silently execute a
provider's text as a command.

== External tool glue

Established tools remain applications. `components/shell-host/tool-catalog.fs`
holds their safe argument builders and output decoders:

| Declarative word | Application | Output |
|---|---|---|
| `files`, `paths` | `fd` | null-separated paths |
| `search`, `matches` | `rg` | JSON-line matches |
| `read`, `show` | `bat` | text |
| `list`, `entries` | `eza` | text |
| `transform`, `json` | `jq` | JSON |
| `pick`, `choose` | `fzf` | selected text |
| `replace`, `substitute` | `sd` | text |

The catalog never executes a process or constructs a shell string. Installation
stays external; no runtime downloads and no custom binary package manager in
this slice. Declarative words are aliases over the literal application; the
language sees intent, not tool flags.

== Boundary

Shell lowers interactive input to Prose/Scale. Question results can be rendered
by the shell, but Question remains pure. Prose is the non-interactive form of
the same language. No shell syntax or completion logic enters the semantic IR.

== Status

The protocol and fish/carapace adapters are the first implementation slice. A
line editor, history, prompt, selection UI, and the full safety policy are
later phases.
