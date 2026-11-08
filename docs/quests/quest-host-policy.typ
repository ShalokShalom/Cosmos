= Quest: host-policy interface

#strong[Status: first slice implemented; host adapters next.] The shared contract
and shell adapter exist. Declarative IO and IPC adapters remain.

== Problem

Three hosts enforce different authority:

- shell: allowed programs, write roots, cleared environment;
- declarative IO: file capabilities, trash/unlink, confirmation;
- IPC: opaque resource tokens and release.

They share path containment and release shape, not policy. A generic executor
would conflate them.

== Candidate interface

```text
HostPolicy
  authorize : Capability -> Decision
  release   : ResourceToken -> Result<unit, Refusal>

Capability =
  Program name
  WriteRoot path
  Environment inherit-or-clear
  FileDelete path
  ResourceToken opaque

Decision = Allow | Refuse reason
```

Executors stay separate: `ShellExecutor`, `FilePlanExecutor`, `TransportExecutor`.
Each calls `authorize` before its effect.

== Extension mechanisms

1. **Closed capability DU** --- exhaustive, simple, no plugin surface. New
   capability kinds require a versioned change.
2. **Interface per host** --- extensible, but different capability sets leak
   through the interface and tests become host-specific.
3. **Open registry** --- third-party capabilities without a kernel change, but
   ordering, authority, and default-deny become a plugin security problem.
4. **Hybrid** --- closed `Capability` DU plus a host adapter interface.

Recommendation: **hybrid**. Keep capability kinds closed and default-deny; let
host adapters implement the executor. Do not add an open registry until a real
third-party host needs it.

Implemented first slice:

- `HostPolicy` in `components/core/host-policy.fs`;
- `ShellHostPolicy.ShellPolicyAdapter` in `components/shell-host`;
- a direct adapter test for program, root, environment, file, and token
  decisions.

== Critical review

- The interface must not become a second effect system; it authorizes host
  effects, it does not type language effects.
- `ResourceToken` is opaque; the policy never dereferences it.
- Path containment uses `PathPolicy`, not a duplicated check.
- Extension without versioning is a security risk; capability additions need a
  schema/ABI decision.
- Tests must cover deny-by-default, missing capability, token release, and
  executor failure after authorization.
