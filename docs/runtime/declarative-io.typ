= Declarative IO

#strong[Status: lower layers implemented; prose surface deferred.] The
`DeclarativeIo` module parses a `.install` source, elaborates an Intent into a
reviewable Plan, and applies non-skipped items through a host executor. No
prose parser or destructive host adapter is part of this slice.

== The two layers

```text
.install source
    -> Installation      capabilities and defaults
    -> Intent            what the user asked
    -> Plan              candidates, verdicts, actions, confirmation
    -> IPlanExecutor     host effects, one item at a time
```

The plan is data. It can be printed, diffed, cached, reviewed, or refused before
the executor runs.

== The `.install` source

A `.install` file is a composition root. The file stem is the installation name;
the block repeats that name explicitly.

```text
install photos {
  resource files : directory at "photos" recursive true follow-links false hidden false
  resource trash : trash at "photos/.trash"
  resource vision : vision at "aesthetic-v1" threshold 0.85
  default delete trash
  default all recursive true follow-links false hidden false
  default undecided skip
  default confirmation required
}
```

The parser accepts:

- `resource NAME : directory at "PATH" [recursive BOOL] [follow-links BOOL] [hidden BOOL]`
- `resource NAME : trash at "PATH"`
- `resource NAME : unlink at "PATH"`
- `resource NAME : vision at "MODEL" threshold NUMBER`
- `default delete NAME`
- `default all [recursive BOOL] [follow-links BOOL] [hidden BOOL]`
- `default undecided skip|refuse`
- `default confirmation required|not-required`

Duplicate capability names, unknown resource kinds, invalid booleans, invalid
thresholds, malformed lines, missing blocks, and unclosed blocks are named
refusals.

== World profile

An installation may also declare the world it runs:

```text
world image "images:kaos/latest"
world type virtual-machine
world cpus 4
world memory 8192
world disk 64
world network "default"
```

The world fields are optional; `image` is required once any `world` line is
present. Defaults are `virtual-machine`, 2 CPUs, 2048 MiB memory, 20 GiB disk,
and network `default`. `renderIncusProfile` emits a deterministic Incus profile
with the image and instance type as `user.scale.*` keys, CPU/memory limits, a
root disk, and a NIC. It does not call Incus; that is the host adapter.

== Defaults

Defaults are installation policy, not parser guesses. A directory capability may
override any `all` attribute; an omitted attribute falls back to `default all`:

| Default | Value |
|---|---|
| `delete` | the `default delete` capability; otherwise the first trash/unlink capability |
| `all` | recursive, no symlink following, no hidden files |
| `undecided` | skip and count |
| `confirmation` | required |

`delete` defaults to trash when a trash capability is installed. `unlink` is an
explicit capability. `Undecided` is never treated as `NotPretty`.

== Intent

The implemented intent is:

```text
Remove(scope, action option, NotPrettyPredicate)
```

The scope must be a directory capability. The action must be trash or unlink.
The predicate is the model verdict, not an ambient boolean.

== Plan

The planner:

- stays inside the scope directory;
- requires a configured `vision` capability;
- requires each candidate's evidence to name that model and meet its threshold;
- considers regular files and, when `follow-links` is true, symlink entries;
  a symlink item is the link itself, never its target; its hash is the resolved
  target's content hash, so `apply` still refuses a target outside the scope;
- applies the directory's recursive, hidden, and symlink policy;
- moves `NotPretty` files to the configured action;
- skips `Pretty`, `NotApplicable`, and, by default, `Undecided`;
- refuses the whole plan on `Undecided` when the policy says `refuse`;
- counts undecided and not-applicable candidates;
- carries the confirmation policy.

The plan item carries path, content hash, verdict, model evidence, and action. A
host executor receives only non-skip items.

== Execution

`IPlanExecutor` is the only effect boundary. `apply` requires a confirmation
receipt that matches the plan fingerprint when the plan says confirmation is
required, resolves the scope and item through real paths, verifies the item
hash, and then calls the executor. It stops on the first host failure and returns
the already-applied items with the failing item and reason. Skipped items are
reported, never executed.

The AI vision adapter is not implemented. The first host adapter is
`DeclarativeIoExecutor.FileSystemExecutor` in `components/ipc`: it hashes with
SHA-256, moves to a trash directory, and refuses unlink unless the adapter is
constructed with `allowUnlink = true`. The realpath, confirmation, and hash
checks remain in `apply`.

== Boundaries

- The `.install` file never grants itself host authority.
- The model only supplies `Pretty`, `NotPretty`, `NotApplicable`, or `Undecided`.
- Defaults are deterministic policy data.
- The prose surface is underdeveloped and is not part of this contract yet.
- A plan is not a proof of external effects; it is a reviewed description of the
  intended effects.

== Tests

Checks cover parsing, defaults, scope containment, path traversal, hidden and
symlink skipping, undecided policy, confirmation, wrong capability kind,
executor dispatch, fail-fast apply, hash invalidation, realpath containment, and
parser refusals.
