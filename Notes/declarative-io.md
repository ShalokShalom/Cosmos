# Declarative IO and the intent layer

Status: design investigation; no parser or runtime change. The prose surface is
explicitly underdeveloped and is not ready for review. The lower layers must be
defined first: opaque tokens, `.install` compilation, Intent/Plan, host release,
and failure policy. Prose is a later surface over that contract.

## Question

How should Scale support a sentence such as:

```text
Delete all files that are not pretty.
```

where `pretty` is evaluated by a host harness with access to an AI image model,
while keeping the surface capability-literal and the IO declarative?

## Finding

The missing piece is not another evaluator and not more prose grammar. It is an
explicit **Intent → Plan → Execute** layer between prose and Scale:

```text
.prose sentence
    -> Intent        typed request; no defaults resolved
    -> Plan          candidates, verdicts, action, confirmation
    -> Executor      Scale + installed capability tokens
```

The plan is data. It can be printed, diffed, cached, reviewed, and refused. The
executor performs IO only after the plan is accepted.

This keeps prose accessible and Scale small. It also makes the dangerous part
visible: the user sees which files would be affected before anything happens.

## Defaults are policy data, not parser magic

`delete`, `all`, and `files are` are predefined operational words, but their
meaning is an installed policy bundle, not a hidden parser rule:

| Word | Default | Override |
|---|---|---|
| `delete` | move to `trash` | `delete permanently` with an `unlink` capability |
| `all` | every candidate in the installed scope | explicit scope, recursion, hidden files, symlinks |
| `files are` | regular files only; no directories or symlinks | file-kind policy |
| `pretty` | the installed vision harness and its default threshold | model/version/threshold in `.install` |
| `Undecided` | skip and report | explicit policy |
| `apply` | plan only; confirmation required for destructive actions | explicit confirmation |

The AI model does not decide scope, deletion semantics, or confirmation. It
returns a typed verdict: `Pretty`, `NotPretty`, `NotApplicable`, or `Undecided`,
with model id, version, score, and content hash. Non-image files are
`NotApplicable`, not “not pretty.”

The specification prose grammar keeps refusing `all` (`UnsupportedQuantifier`).
Operational prose is a separate surface and may admit `all` only because it has
an installed scope and a policy.

## Shape

```text
# photos.install
install photos {
  files  = host.directory("photos")
  vision = host.vision("aesthetic-v1")
  trash  = host.trash("photos/.trash")

  defaults {
    delete        = trash
    all           = regular-files(recursive: true, follow-links: false, hidden: false)
    undecided     = skip
    confirmation  = required
  }
}
```

```text
# cleanup.prose
Delete all files that are not pretty.
```

The prose parser produces an intent, not a host call:

```text
Intent.Remove {
  scope: photos.files
  quantifier: all
  predicate: not (photos.vision.pretty)
  action: photos.delete
}
```

The policy elaborates it into a plan:

```text
Plan {
  candidates: file hashes and paths
  verdicts: Pretty | NotPretty | NotApplicable | Undecided
  action: photos.trash
  skipped: non-images and undecided
  confirmation: required
}
```

Only `apply plan` reaches the executor.

## Why this is the appealing solution

- **Capability-literal:** `photos.files`, `photos.vision`, and `photos.trash` are
  installation-scoped tokens. There is no ambient filesystem or model.
- **Declarative IO:** the user states a selection and an action; the plan states
  the consequences; the executor applies it.
- **Good defaults without speculation:** defaults are a versioned policy bundle,
  not an AI guess and not parser folklore.
- **Reviewable:** the plan is the safety artifact; the model verdict is evidence,
  not authority.
- **Accessible:** the prose surface stays sentence-shaped; advanced users can
  inspect or edit the plan.
- **Small core:** Scale does not grow a new IO word for every verb. The core sees
  a plan and capability tokens.

## Alternatives

| Alternative | Problem |
|---|---|
| Extend the prose parser directly with `delete`/`all` | Defaults become hidden parser semantics; no reviewable plan |
| Let the AI model decide “pretty” and delete | Nondeterministic authority; no scope or confirmation guarantee |
| Add an IO monad or effect handlers | Describes effects but does not provide resource identity or release |
| Make users write Scale directly | Precise, but not accessible to the sentence-level user |

## Open decisions

1. Is `.plan` a persisted artifact or only an in-memory review value?
2. Does `all` include hidden files by default? Current proposal: no.
3. Is `delete` always reversible trash unless `permanently` is explicit?
4. Does a plan invalidate on file hash, model version, threshold, or all three?
5. Is confirmation a host UI event, a signed plan, or both?
