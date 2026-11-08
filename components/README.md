# Components

Polylith components, in dependency order. `workspace.edn` is the manifest and
`just poly-check` enforces the graph.

| Component | Role |
|---|---|
| `kernel` | NativeAOT abstract machine, image loader, primitive wire, C ABI |
| `core` | Shared host data: types, store, persistence, runtime shapes, verification data |
| `prelude` | Evaluator, vocabulary, prose parser, dispatch, scheduler, ownership |
| `ipc` | Actor wire protocol, host transport, host resource table |
| `verification` | Verification lattice, typing, SMT, refiner, book runner |
| `demo` | Product showcase |
| `cli` | CLI implementation |
| `shell-host` | Shell effects and prose script runner |
| `book-host` | Book check/cast host |
| `nu-host` | Nu host integration |
| `adapter-c` | C ABI adapter and conformance harness |

`nu-engine` is the external Nu node, not a Scale component. Its source skeleton
is tracked; prebuilt dependencies and binary assets are fetched separately and
are ignored by Git. Bases and projects live in `../bases/` and `../projects/`.
