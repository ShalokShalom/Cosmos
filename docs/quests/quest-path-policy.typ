= Quest: path policy

#strong[Status: shared module landed; direct contract tests added.] This is the
first Quest work item from the code-sharing review. It owns the shared path
checks used by declarative IO and the shell host.

== Contract

`components/core/path-policy.fs` provides:

- `resolveRealPath path` --- resolves the existing path or its nearest existing
  parent through the real-path chain;
- `relativePath root path` --- the lexical relative path;
- `isUnderRoot root path` --- root equality or a non-traversing descendant;
- `isHidden relative` --- any path segment beginning with `.`;
- `isDirectChild relative` --- no directory separator.

The module is policy-free. Callers decide what a violation means.

== Tests

- root equals path;
- a direct child is under the root;
- a sibling is outside the root;
- `..` traversal is outside the root;
- hidden segment detection;
- direct-child detection;
- a symlinked parent resolves to its target.

== Boundary

The shell host maps a violation to its write-policy refusal. Declarative IO maps
it to the plan/apply scope refusal. The shared module never performs an effect.
