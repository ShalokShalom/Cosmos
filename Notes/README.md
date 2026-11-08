# Notes

Internal, non-normative notes for maintainers and agents. Canonical
documentation lives under `docs/`; if a note becomes normative, move it to
Typst under `docs/` and add it to `docs/README.typ`.

## Current notes

| File | Topic |
|---|---|
| `agent-tooling.md` | Why the no-op shell guard exists |
| `action-question-adapter.md` | Action → Question port contract and open result issues |
| `build-time.md` | Build-time policy and measurement notes |
| `deferred-directions.md` | Directions deliberately deferred |
| `declarative-io.md` | Intent → Plan → Execute layer for declarative IO |
| `declarative-io-review.md` | Lower-layer codebase and relationship review |
| `smolvm-review.md` | smolvm code review and OCI/Incus/smolvm split |
| `ir-case-study.md` | IR inventory, duplication, and consolidation options |
| `cosmos-64.md` | Cosmos 64 abstraction-layer idea for the multimedia OS |
| `code-sharing.md` | Low/medium/high-risk code-sharing candidates |
| `code-sharing-review.md` | Whole-graph critical review of sharing risks |
| `compact-over-cheap-review.md` | Critical review against contract-complete, rich semantics |
| `install-files.md` | Capability-literal `.install` surface and slogan |
| `lsp-workspace-plan.md` | Known F# LSP limitation in the full checkout |
| `unification-proposal.edn` | Machine-readable unification proposal |
| `Pattern-matching.md` | Pattern-matching design notes |
| `binary-format.md` | Actor wire-format notes |
| `multi-platform-port.md` | Multi-platform port plan |
| `decisions/` | D decision records and review packet |
| `archive/` | Historical notes kept for context |

## Archive

`archive/` contains older material that is not current guidance:

| File | Topic |
|---|---|
| `Additional_notes` | Older miscellaneous notes |
| `unknown-maybe-duplicate.txt` | Unclassified historical material |
| `effect-sets-vs-hybrid-dialogue.md` | Effect-set design dialogue |
| `Elixir-and-Julia.md` | Comparative language notes |
| `environmental-support-case-study.md` | Environmental-support case study |

## Decisions

`decisions/` contains the D decision records. These are a separate review packet
from the numbered decisions in `docs/process/decision-records.typ`; see
`decisions/DR-1.md` for the convention note.
