## Pattern matching

Context and purpose. The goal is one shared pattern-matching substrate, for it to work with F#, Elixir, and later possibly other languages.
Multiple implementations on different hosts share the same unified pattern matching. This is build around the higher order unification. 
Portable patterns for rules/configs/hot-reload; future hosts to be added by adapter, not by forking the kernel.

Three artifacts, strictly separate:

    General embedded surface — the language's canonical pattern syntax, untyped at the pattern level, embedded via quotation/macros. Carries the HOU-aware features. Host front ends may consult host types; the general surface and the portable form do not.

    Runtime pattern IR — typed sub-language of the general IR. Constructor-headed (never functions). Variables carry roles from a closed set {fresh, pin, repeated, shadowing} — this set is what the kernel can execute; roles from future hosts are elaborated into combinations of these plus host adapter calls. Clauses carry guarded | unguarded markers. Per-clause failure reasons are distinguished (structural / guard-rejected); NoClauseMatched is a per-match composition. Clause ordering preserved.

    Portable serialized form — canonical constructors via pluggable kind registrations, versioned schema, guards as named predicate references only (host-expression guards are not portable — state this), layout metadata sufficient for reconstitution, predicate-version pinning for hot-reload safety, and an explicit distinction between closed patterns (executable) and pattern templates (open, with declared hole signatures, used at elaboration, not executable).

Kernel interface — six functions, not four:

    tag(host_term) -> tag_id

    extract(host_term, tag_id, shape_spec) -> either<bindings, extract_fail>

    evaluate_guard(guard_node, bindings) -> bool — where guard_node is either NamedPredicate or HostOpaque; the adapter interprets both.

    construct(constructor_id, args) -> host_term

    raise_failure(class, info) -> !

    query_kind_metadata(kind_id, meta_key) -> meta_value — used by analyses for "closed?", "arity", "field-presence", "segment-spec".

Nothing else crosses the boundary. Runtime IR does not carry unknown roles or unknown guards; unknown roles or guard references in a portable pattern are a load-time error, not an opaque runtime constraint.

Two matching engines, one interface, conformance-tested. Decision-tree compiler for ground-subject patterns (hot path). Unifier for open/equation terms. Both conform to a shared oracle on the intersection; guard-evaluation timing, failure reporting granularity, and tie-breaking are defined at the interface, not left to each engine.

Analyses over the runtime IR, parameterized by query_kind_metadata. Exhaustiveness and redundancy are best-effort on Elixir (heuristic, warn-only there) and complete on F# DUs/records; state this per host rather than promising parity. Guarded clauses are non-covering; host-extensible failure classes must declare whether they cover, and the declaration lives in query_kind_metadata.

Surfaces. Host-native surfaces are per-host sugar elaborating to the same IR. F# match remains faithful; unification extensions go through our own CE. Elixir macros emit native patterns where possible, kernel calls for extensions. Where a host-native feature is genuinely host-specific and non-portable — bit syntax, Elixir open maps, F# active patterns — mark it as such in the IR and do not require the general surface to absorb it. The rule is "host-native surfaces may carry host-specific marked features; the general surface must cover the portable subset."

Lints, not modes — stated honestly. Compile-time lints, warnings only, heuristic where decidability fails (side-effecting guards is one such case). No project config claims to enforce a discipline; it can only warn.

Deployment. The predicate registry and constructor-kind registrations are versioned; portable patterns pin predicate versions; hot-reload behavior for predicates and kinds is a first-class design concern, not an afterthought.

Tests. Round-trip per host (native/general → IR → portable → re-elaborate → same behavior, modulo host exception type); cross-engine conformance (decision-tree vs unifier); and a versioned-registry test (old pattern + new predicate, new pattern + old predicate).
