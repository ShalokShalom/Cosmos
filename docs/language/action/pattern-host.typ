= Host pattern boundary

#strong[Status: host-boundary slice implemented; F# and Elixir elaborators are next.]
`PatternHost` keeps the executable `PatternLogic.Pattern` portable and puts
host-specific pattern features outside it.

== The split

```text
F# surface      Elixir surface
     \               /
      host elaboration
             |
      HostPattern
       /       \
  Portable    HostOnly
       |          |
PatternLogic   host adapter
```

`PatternLogic.Pattern` remains the closed, executable core: wildcard, variable
roles, literals, constructors, typed patterns, and named guards. It does not
gain active-pattern, bit-syntax, open-map, or macro nodes.

`PatternHost.HostPattern` adds exactly two cases:

```text
Portable of Pattern
HostOnly of host: string * feature: string * children: HostPattern list
```

A host-specific feature is explicit and named. It never reaches the runtime
engine as an unknown node.

== Lowering

`IHostPatternElaborator` lowers a `HostOnly` node into the portable core:

```text
Lower(host, feature, loweredChildren) -> Result<Pattern, HostPatternError>
```

Without an elaborator, `HostOnly` is refused as `HostSpecific(host, feature)`.
With an elaborator, the node is lowered or refused; there is no silent fallback.

== Host features

- F# active patterns, type tests, and host-specific guards are `HostOnly` until
  an F# elaborator lowers the portable part.
- Elixir bit syntax, open maps, and host-specific guards are `HostOnly` until an
  Elixir elaborator lowers the portable part.
- DU/record patterns, pin, repeated, shadowing, constructors, literals, and
  named guards can lower to the shared core.

The portable document still contains only `PatternLogic.Pattern`; host-only
features cannot cross the portable boundary without lowering.

== Boundaries

- The runtime engine never sees a host-specific node.
- The portable reader never sees a host-specific node.
- Host-specific analysis remains host-specific: F# exhaustiveness over closed
  DUs/records; Elixir heuristic exhaustiveness.
- A host adapter may refuse a feature rather than invent a portable meaning.

== Tests

Checks cover portable passthrough, refusal of a host-only pattern without an
elaborator, and lowering through a host elaborator.
