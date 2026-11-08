module Presentation

// ============================================================
// Presentation layer, phase P5 (D72).
//
// One `Violation` becomes a layer-aware explanation for a person who is not a
// logician: what kind of failure it is, where it is, what was expected and
// given, whether the input is at fault, the evidence, and the repair. The
// technical rendering keeps the rule identifier; the ordinary rendering leaves
// it out. Character offsets are still open, so the span is the canonical token.
// ============================================================

open Types
open Violations

let private layerHeadline (layer: Layer) : string =
    match layer with
    | Lexical -> "One word is not in the fixed Basic English vocabulary."
    | Structural -> "This is not a licensed Basic English sentence shape."
    | Agreement -> "The parts of the sentence do not agree."
    | Sortal -> "A value has the wrong sort for this use."
    | Binding -> "A name or resource is not validly bound here."
    | Semantic -> "The sentence cannot hold together with the fixed specification."
    | Discourse -> "This utterance conflicts with the committed conversation."

let private blameLine (blame: Blame) : string =
    match blame with
    | Input -> "This is an input error; the sentence can be repaired."
    | CoverageGap -> "This is outside the fixed specification's coverage; the checker cannot decide it."

let private witnessLines (witness: Witness option) : string list =
    match witness with
    | None -> []
    | Some(UnsatCore core) ->
        let assumptions =
            if core.IsEmpty then
                "(the solver returned an empty core)"
            else
                String.concat "; " core

        [ "Conflicting assumptions: " + assumptions ]
    | Some(Model model) -> [ "Counterexample: " + Smt.describeModel model ]

let private optionalLine (label: string) (value: string option) : string list =
    match value with
    | Some text -> [ label + ": " + text ]
    | None -> []

let private suggestionLine (suggestion: Advice option) : string list =
    match suggestion with
    | Some advice -> [ "Suggestion: " + advice.Text ]
    | None -> []

let private spanLine (span: Span) : string =
    let range =
        match span.Start, span.End with
        | Some start, Some finish -> " (characters " + string start + "-" + string finish + ")"
        | _ -> ""

    if span.Token = "" then
        "At: the whole sentence" + range
    else
        "At: " + span.Token + range

let private details (violation: Violation) (includeRule: bool) : string list =
    let rule = if includeRule then [ "Rule: " + violation.Rule ] else []

    rule
    @ [ spanLine violation.Span ]
    @ optionalLine "Expected" violation.Expected
    @ optionalLine "Given" violation.Given
    @ [ blameLine violation.Blame ]
    @ witnessLines violation.Witness
    @ suggestionLine violation.Suggestion

/// The ordinary, non-logician rendering.
let render (violation: Violation) : string =
    String.concat "\n" (layerHeadline violation.Layer :: details violation false)

/// The same rendering with the stable rule identifier, for a developer or a
/// log. It is not the child-facing output.
let renderTechnical (violation: Violation) : string =
    String.concat "\n" (layerHeadline violation.Layer :: details violation true)

/// Adds the nearest accepted sentence from the fixed grammar when the violation
/// carries no hand-written suggestion (P4/D71). The result is advice, never a
/// proposal.
let withNearestSentence (vocabulary: VocabularyEntry list) (tokens: string list) (violation: Violation) : Violation =
    match violation.Suggestion with
    | Some _ -> violation
    | None ->
        match NearestSentence.nearest vocabulary tokens with
        | Some candidate ->
            { violation with
                Suggestion = Some(NearestSentence.advice candidate)
            }
        | None -> violation
