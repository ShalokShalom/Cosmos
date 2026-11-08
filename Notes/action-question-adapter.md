# Action–Question adapter research

Status: research note, 2026-11-07. Non-normative.

## What exists

- `QuestionParser` parses facts, rules, and a question into
  `QuestionProgram`.
- `QuestionEngine.solve` is pure and returns `QuestionResult` or
  `QuestionError`.
- `QuestionResult.Answers` currently uses `Map<string, Pattern>` bindings,
  plus `Steps` and `Truncated`.
- Action has no `ask` word, no question term conversion, and no adapter.
- Question has no dependency on Logic or Action. That is deliberate.

## Direction

The useful direction is **Action → Question**:

- Action constructs or loads a question program;
- Question evaluates it purely and returns bindings;
- Action consumes the bindings or refuses the result.

Question → Action callbacks are not part of the first contract. A later
predicate adapter may call Logic/Action only with **ground** arguments. This
keeps Question free of non-deterministic function narrowing.

## Proposed contract

```fsharp
type QuestionLimits =
    { MaxSteps: int
      MaxAnswers: int }

type IQuestionPort =
    abstract Ask:
        QuestionProgram ->
        QuestionLimits ->
        Result<QuestionResult, QuestionError>
```

The port is pure. It does not parse source text, perform host effects, or
depend on Action state. Parsing and source errors stay at the outer adapter.

For values crossing the boundary:

```fsharp
type IQuestionTermAdapter =
    abstract ToQuestion: ActionValue -> Result<Pattern, AdapterError>
    abstract FromQuestion: Pattern -> Result<ActionValue, AdapterError>
```

This adapter belongs to the host/Action side, not to `QuestionEngine`. It must
not silently serialize arbitrary host objects into patterns.

## Result contract issue

`Map<string, Pattern>` loses question-variable order and cannot distinguish
“unbound” from “absent”. For a user-facing contract, prefer:

```text
Answer = (name * Pattern option) list   // order preserved, unbound explicit
```

or a small record with ordered bindings and an explicit unbound case. The
engine can keep its internal map; the port should expose the stable shape.

## Errors and limits

- Parse errors are `QuestionParser` refusals.
- No question is `QuestionError.NoQuestion`.
- Step exhaustion is `StepLimitExceeded`; it is not an empty answer.
- Answer truncation is reported by `Truncated`, not silently ignored.
- An unsupported variable role is a refusal.
- `ask` maps these to named Action refusals; it never throws.

## Options

| Option | Benefit | Cost |
|---|---|---|
| Pure `IQuestionPort` over a parsed `QuestionProgram` | Small, testable, no new syntax | Caller must parse or load the question |
| `ask("question text")` parses inside | Compact Action call | Hides parse errors in the word; couples parsing to Action |
| Host callback predicate (`Question → Action`) | Computed relations | Reintroduces free-variable/narrowing risk; defer |
| Curry-style narrowing | Functional-logic integration | Non-determinism in Logic/Action; rejected |
| Embedded question AST in Action IR | One source form | Adds a second executable representation; avoid |

Recommendation: pure **Action → Question** port over a parsed document, with
ordered/explicit bindings and explicit limits. Add a `QuestionSource` adapter
later if a source-text call is needed.

## Security

Question is free of host effects, so `ask` is not a capability. Its limits are
resource policy, not authority. If a ground predicate adapter is added, that
adapter must be a capability checked by `HostPolicy`; it must not grant
Question arbitrary execution.

## Open questions

- Ordered bindings vs map, and explicit unbound representation.
- Term conversion for numbers, text, atoms, and constructors.
- Whether `ask` takes compiled `QuestionProgram` or a question document.
- Ground-only predicate adapter shape.
- Defaults for `MaxSteps` and `MaxAnswers`.
