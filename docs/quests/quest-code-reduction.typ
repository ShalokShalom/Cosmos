= Quest: code and cognitive reduction

#strong[Status: Q1 gate implemented; Q2 adapter implemented for declarative IO;
Q2 IPC and Q3--Q5 staged.] The profile comments and `profilesOf` classify the
semantic IR; the consistency gate checks token lowering against the signature
catalog; `DeclarativeIoPolicy.Adapter` authorizes delete capabilities.

== Problem

The repository has several places that must agree by hand: the builtin
dispatcher and its signature classification, the token and expression lowering
paths, the type-retrieval and unification type models, and the kernel image and
package schemas. The duplication is semantic, not textual, so a mechanical
merge would change behavior.

== Options

+ #strong[A. Big-bang consolidation.] One rewrite of the interpreter, lowered
  VM, type system, and schemas. Rejected: it destroys the differential evidence
  and blocks every other workstream.
+ #strong[B. Staged reduction with drift gates.] Add a catalog/drift gate first,
  then extract contracts, then retire authority one slice at a time. Chosen.
+ #strong[C. Leave the duplication.] Rejected: the drift already produced stale
  code memories and parallel error paths.

== Phases

#table(
  columns: (auto, 1fr, 1fr),
  align: (left, left, left),
  table.header([Phase], [Work], [Gate]),
  [Q1], [Word catalog and drift gate across dispatch, classification, and token lowering.], [One catalog source; every listed word has one classification and one lowering decision; consistency check passes.],
  [Q2], [HostPolicy adapters for declarative IO and IPC, without merging their authority models.], [Each adapter is default-deny and has a direct test; shell adapter remains unchanged.],
  [Q3], [Generate PreludeImage/PackageLogic codecs from one schema or share conformance vectors.], [Round-trip and forged-input vectors pass; no kernel-to-core reference.],
  [Q4], [TypeCore: shared variables, schemes, rows, lacks, and Code behind a contract.], [Differential inference tests pass before code movement.],
  [Q5], [One execution authority: ExpressionLogic/TermIr semantic family and one lowered IR.], [W1 matrix is green; W2 handlers land after the contract; ABI/hot-swap/AOT gates pass.],
)

== Non-goals

- Do not merge shell, declarative IO, and IPC policy models.
- Do not add a kernel-to-core project reference.
- Do not include the external Nu node in sharing.
- Do not rewrite the interpreter and lowered VM in one change.

== Critical review

- Q1 is a gate, not a new runtime abstraction: the catalog must not become a
  second dispatcher.
- Q2 adapters authorize; they never perform effects.
- Q3 must preserve named errors and NativeAOT-compatible serialization.
- Q4 requires a semantic contract for variables, schemes, lacks, and Code
  before any type moves.
- Q5 is the highest risk; the differential matrix and the kernel/Prelude
  boundary are prerequisites.
