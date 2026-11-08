module Discourse

// ============================================================
// Discourse rollback, phase P3 (D70).
//
// One utterance is one attempt. A rejected utterance publishes its violations
// and restores the state before it; an accepted one commits the final state.
// The policy is blame-conservative: only an input fault at Structural or above
// rejects. A coverage gap still publishes a violation, but it does not block.
// ============================================================

open Types

type Utterance =
    {
        /// The discourse policy did not reject the attempt.
        Accepted: bool
        /// The attempt's final state is the state that was returned. A coverage
        /// gap is accepted (it does not block) but not committed, because
        /// `Evaluator.run` does not return a final state on an error.
        Committed: bool
        State: EvaluationState
        Violations: Violations.Violation list
    }

let private layerRank (layer: Violations.Layer) : int =
    match layer with
    | Violations.Lexical -> 0
    | Violations.Structural -> 1
    | Violations.Agreement -> 2
    | Violations.Sortal -> 3
    | Violations.Binding -> 4
    | Violations.Semantic -> 5
    | Violations.Discourse -> 6

/// A violation rejects an utterance only when the input is at fault and the
/// detector is at or above the structural layer.
let private rejects (violation: Violations.Violation) : bool =
    violation.Blame = Violations.Input
    && layerRank violation.Layer >= layerRank Violations.Structural

/// The part of a state a rollback promises to restore. The store is
/// append-only, so it is not part of the equality.
let sameObservable (left: EvaluationState) (right: EvaluationState) : bool =
    left.Stack = right.Stack
    && left.Program = right.Program
    && left.Location = right.Location
    && left.Baseline = right.Baseline
    && left.Position = right.Position

/// The quantifier of an ordinary statement; a transition has no quantifier and
/// cannot contradict a statement by this rule.
let private statementQuantifier (sentence: Parser.Sentence) : Parser.SentenceQuantifier option =
    match sentence.Kind with
    | Parser.Statement quantifier -> Some quantifier
    | Parser.EventTriggered -> None

/// Two universal/negative statements over the same subject, conditions and verb
/// contradict. A possibility contradicts only a universal negative.
let private contradictory (left: Parser.SentenceQuantifier) (right: Parser.SentenceQuantifier) : bool =
    match left, right with
    | Parser.Every, Parser.No
    | Parser.No, Parser.Every
    | Parser.Exists, Parser.No
    | Parser.No, Parser.Exists -> true
    | _ -> false

let private sameClaim (left: Parser.Sentence) (right: Parser.Sentence) : bool =
    left.Subject = right.Subject
    && left.Verb = right.Verb
    && left.Conditions = right.Conditions

/// Would committing `candidate` contradict any committed statement? The check
/// is intentionally syntactic: same subject, conditions and verb, with an
/// Every/No or Some/No pair. It is a named detector, not a complete logic.
let contradicts (committed: Parser.Sentence) (candidate: Parser.Sentence) : bool =
    match statementQuantifier committed, statementQuantifier candidate with
    | Some left, Some right -> sameClaim committed candidate && contradictory left right
    | _ -> false

/// The first committed statement the candidate contradicts, as one Discourse
/// violation. A consistent candidate produces none.
let contradiction (committed: Parser.Sentence list) (candidate: Parser.Sentence) : Violations.Violation option =
    committed
    |> List.tryPick (fun earlier ->
        if contradicts earlier candidate then
            Some
                {
                    Layer = Violations.Discourse
                    Rule = "Contradiction"
                    Span =
                        {
                            Token = candidate.Subject
                            Start = None
                            End = None
                        }
                    Expected = Some "a consistent statement"
                    Given = Some(candidate.Subject + " " + candidate.Verb)
                    Blame = Violations.Input
                    Witness = None
                    Suggestion = None
                }
        else
            None)

/// Runs one utterance. A rejected utterance keeps the image it started with and
/// returns the pre-utterance state; an accepted utterance returns the final
/// state. A coverage-gap failure is published but does not roll back; it is
/// accepted but not committed because `Evaluator.run` has no final state to
/// return.
let utter (program: string list) (before: EvaluationState) : Utterance =
    match Evaluator.run { before with Program = EvaluatorProgram.ofTokensOrFail program } with
    | Ok after ->
        {
            Accepted = true
            Committed = true
            State = after
            Violations = []
        }
    | Error diagnostic ->
        let violation = Violations.ofEvaluationError diagnostic.Error

        if rejects violation then
            {
                Accepted = false
                Committed = false
                State = before
                Violations = [ violation ]
            }
        else
            {
                Accepted = true
                Committed = false
                State = before
                Violations = [ violation ]
            }
