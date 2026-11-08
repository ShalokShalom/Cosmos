module Violations

// Violation reporting, phase P0 (D57). Every parser refusal becomes one
// uniform, blamable value: the layer that detected it, the rule it broke, the
// offending constituent, what was expected, what was found, and whether the
// fault is the input's or the fixed specification's. Later phases add the
// other detectors (sorts, unification, guards, SMT, discourse) to the same
// value; P0 wires the parser.

open Types
open Parser
open Smt

/// The pipeline stage that detected a violation. The layer is user-facing: it
/// selects which explanation and repair strategies are available.
type Layer =
    | Lexical
    | Structural
    | Agreement
    | Sortal
    | Binding
    | Semantic
    | Discourse

/// The party responsible. `Input` is something Scale Basic English forbids;
/// `CoverageGap` is something the fixed specification does not cover.
type Blame =
    | Input
    | CoverageGap

/// The offending constituent. `Start` and `End` are character offsets into the
/// original sentence when the caller supplied it (`parseText`); `End` is
/// exclusive. A token-only caller leaves them `None`.
type Span =
    {
        Token: string
        Start: int option
        End: int option
    }

/// Advisory text attached to a violation. Advice is diagnostic: it can point at
/// a repair, but it is never a proposal and cannot be accepted by the gate.
type Advice =
    {
        Text: string
        Span: Span option
        Distance: int option
        Source: string
    }

let private adviceText (text: string) : Advice =
    {
        Text = text
        Span = None
        Distance = None
        Source = "hand-written"
    }

/// The evidence a stage can attach. Parser and semantic refusals produce none;
/// the SMT stage produces the solver's counterexample or its unsat core.
type Witness =
    | UnsatCore of string list
    | Model of Smt.SolverAssignment

/// One diagnostic from any stage. An optional field is absent only when the
/// layer makes it meaningless, never to make a message shorter.
type Violation =
    {
        Layer: Layer
        Rule: string
        Span: Span
        Expected: string option
        Given: string option
        Blame: Blame
        Witness: Witness option
        Suggestion: Advice option
    }

let private make
    (layer: Layer)
    (rule: string)
    (found: string)
    (expected: string option)
    (blame: Blame)
    (repair: string option)
    : Violation =
    {
        Layer = layer
        Rule = rule
        Span =
            {
                Token = found
                Start = None
                End = None
            }
        Expected = expected
        Given = (if found = "" then None else Some found)
        Blame = blame
        Witness = None
        Suggestion = Option.map adviceText repair
    }

/// The layer, rule, blame, and repair for each grammar refusal. The rule is
/// the `ParseError` case name, so a caller can switch on a stable identifier
/// without reparsing prose.
let ofParseError (error: ParseError) : Violation =
    match error with
    | EmptySentence -> make Structural "EmptySentence" "" (Some "a sentence") Input None
    | ExpectedQuantifier found ->
        make Structural "ExpectedQuantifier" found (Some "'every', 'no', or 'some'") Input None
    | UnsupportedQuantifier quantifier ->
        make
            Structural
            "UnsupportedQuantifier"
            quantifier
            (Some "'every' or 'no'")
            CoverageGap
            (Some "Use 'every' or 'no'.")
    | ExpectedSubject found -> make Structural "ExpectedSubject" found (Some "a subject noun") Input None
    | ExpectedCondition found -> make Structural "ExpectedCondition" found (Some "a condition") Input None
    | ExpectedComparison found -> make Structural "ExpectedComparison" found (Some "a comparison") Input None
    | ExpectedVerb found -> make Structural "ExpectedVerb" found (Some "a verb phrase") Input None
    | UnexpectedToken found -> make Structural "UnexpectedToken" found (Some "a licensed token") Input None
    | MissingFullStop -> make Structural "MissingFullStop" "" (Some "a full stop") Input (Some "Add a full stop.")
    | ArticleNotAllowed found ->
        make Structural "ArticleNotAllowed" found (Some "no article") Input (Some "Remove the article.")
    | PronounNotAllowed found ->
        make
            Structural
            "PronounNotAllowed"
            found
            (Some "no pronoun")
            Input
            (Some "Name the subject instead of using a pronoun.")
    | ConditionalNotAllowed found ->
        make
            Structural
            "ConditionalNotAllowed"
            found
            (Some "a transition sentence")
            CoverageGap
            (Some "Use 'when <subject> <condition>, every <subject> <verb>.'")
    | NegationNotAllowed found ->
        make
            Structural
            "NegationNotAllowed"
            found
            (Some "sentence-initial 'no'")
            Input
            (Some "Use 'no' before the subject.")
    | UnknownToken found -> make Lexical "UnknownToken" found (Some "an active vocabulary word") CoverageGap None
    | NotAnInteger found -> make Structural "NotAnInteger" found (Some "an integer") Input None
    | ExpectedTransitionComma found ->
        make
            Structural
            "ExpectedTransitionComma"
            found
            (Some "a comma")
            Input
            (Some "Put a comma between the trigger and the effect.")
    | TransitionSubjectMismatch(trigger, effect) ->
        make
            Structural
            "TransitionSubjectMismatch"
            effect
            (Some("the trigger subject '" + trigger + "'"))
            Input
            (Some("Use '" + trigger + "' as the effect subject."))
    | TransitionOutcomeNotUniversal found ->
        make
            Structural
            "TransitionOutcomeNotUniversal"
            found
            (Some "the universal outcome 'every'")
            CoverageGap
            (Some "Use 'every' for a transition's outcome.")

/// Parses a sentence and reports the grammar refusal as a `Violation`, not a
/// bare `ParseError`. A valid sentence is still the parser's `Sentence`.
let parse (vocabulary: VocabularyEntry list) (tokens: string list) : Result<Sentence, Violation> =
    Parser.parseDetailed vocabulary tokens |> Result.mapError ofParseError

/// The surface form a canonical token came from, when the vocabulary knows it.
let private surfaceForm (vocabulary: VocabularyEntry list) (token: string) : string =
    vocabulary
    |> List.tryPick (fun entry ->
        if entry.IsActive && entry.Token = token then
            Some entry.Form
        else
            None)
    |> Option.defaultValue token

/// Fills the source range for the first occurrence of the offending surface
/// form. The tokenizer does not retain a token index, so a repeated form uses
/// its first occurrence; an empty token (the whole sentence) has no range.
let private withOffsets (vocabulary: VocabularyEntry list) (sentence: string) (violation: Violation) : Violation =
    if violation.Span.Token = "" then
        violation
    else
        let form = surfaceForm vocabulary violation.Span.Token
        let start = sentence.IndexOf(form, System.StringComparison.OrdinalIgnoreCase)

        if start < 0 then
            violation
        else
            { violation with
                Span =
                    { violation.Span with
                        Start = Some start
                        End = Some(start + form.Length)
                    }
            }

/// Parses a sentence and keeps the character range of the offending surface
/// form when it can be located. This is the entry point for source-text
/// diagnostics; `parse` remains the token-only API.
let parseText (vocabulary: VocabularyEntry list) (sentence: string) : Result<Sentence, Violation> =
    Tokenizer.tokenize vocabulary sentence
    |> parse vocabulary
    |> Result.mapError (withOffsets vocabulary sentence)

// ---- Semantic stages (D61) -------------------------------------------------

let private makeDetail
    (layer: Layer)
    (rule: string)
    (span: string)
    (expected: string option)
    (given: string option)
    (blame: Blame)
    (repair: string option)
    : Violation =
    {
        Layer = layer
        Rule = rule
        Span =
            {
                Token = span
                Start = None
                End = None
            }
        Expected = expected
        Given = given
        Blame = blame
        Witness = None
        Suggestion = Option.map adviceText repair
    }

let private numberText (value: RealNumber) : string =
    value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

let private shapeText (shape: int[]) : string =
    "[" + String.concat ", " (shape |> Array.map string) + "]"

/// Every evaluation refusal as the same blamable value as a grammar refusal.
/// A `Semantic` defect from a bad program is the input's fault; an
/// `Undecided` membership, dispatch, unification, guard, or budget is a
/// coverage gap of the fixed theory, never a false (D61).
let ofEvaluationError (error: EvaluationError) : Violation =
    let simple layer rule found expected blame =
        make layer rule found (Some expected) blame None

    match error with
    | StackUnderflow(word, needed, available) ->
        makeDetail
            Structural
            "StackUnderflow"
            word
            (Some("at least " + string needed + " values"))
            (Some(string available + " available"))
            Input
            None
    | EmptyValue word -> simple Structural "EmptyValue" word "a value" Input
    | ArityError(ArityFault.InvalidTerm(word, arity)) ->
        makeDetail
            Structural
            "InvalidTermArity"
            word
            (Some "a non-negative integer arity")
            (Some(numberText arity))
            Input
            None
    | ShapeMismatch(word, left, right) ->
        makeDetail
            Structural
            "ShapeMismatch"
            word
            (Some "the same shape")
            (Some(shapeText left + " vs " + shapeText right))
            Input
            None
    | UnknownIndex index -> simple Structural "UnknownIndex" (string index) "an existing entry" Input
    | Unresolved(UnknownKind.Word, token) -> simple Lexical "UnknownWord" token "an active word" CoverageGap
    | InactiveWord form -> simple Lexical "InactiveWord" form "an active word" CoverageGap
    | Unresolved(UnknownKind.Binding, name) -> simple Binding "UnknownBinding" name "a bound name" Input
    | Unresolved(UnknownKind.Model, name) -> simple Sortal "UnknownModel" name "a declared model" Input
    | TypeMismatch(ErrorWord word, ExpectedQuotation) -> simple Structural "NotAQuotation" word "a quotation" Input
    | TypeMismatch(ErrorWord word, ExpectedText) -> simple Structural "NotAText" word "text" Input
    | InvalidBookmark -> simple Structural "InvalidBookmark" "" "a valid bookmark" Input
    | UnclosedQuotation -> simple Structural "UnclosedQuotation" "" "a closing quotation" Input
    | CodeError reason -> simple Structural "CodeError" reason "a well-formed code value" Input
    | DivisionByZero -> simple Semantic "DivisionByZero" "" "a non-zero divisor" Input
    | ReductionExhausted -> simple Semantic "ReductionExhausted" "" "a terminating program" CoverageGap
    | UnclosedSet -> simple Structural "UnclosedSet" "" "a closing bracket" Input
    | UnexpectedSetClose -> simple Structural "UnexpectedSetClose" "" "an opening bracket" Input
    | TypeMismatch(ErrorWord word, ExpectedSet) -> simple Structural "NotASet" word "a value set" Input
    | SetLiteralSchedulerWord word ->
        simple Structural "SetLiteralSchedulerWord" word "a set literal without a scheduler word" Input
    | InvalidHistoryStep(index, count) ->
        makeDetail
            Structural
            "InvalidHistoryStep"
            (string index)
            (Some("a step in 0.." + string count))
            (Some(string index))
            Input
            None
    | TypeMismatch(ErrorAddress address, ExpectedStep) -> simple Structural "NotAStep" (string address) "a history step" Input
    | NotOwned(address, _) -> simple Binding "NotOwned" (string address) "an owned value" Input
    | OwnedByAnother(address, _) -> simple Binding "OwnedByAnother" (string address) "ownership by this actor" Input
    | ValueConsumed address -> simple Binding "ValueConsumed" (string address) "a live value" Input
    | SortError(SortFault.Undecided reason) ->
        makeDetail Sortal "ValueSetUndecided" "" (Some "a decidable membership") (Some reason) CoverageGap None
    | DispatchUndecided reason ->
        makeDetail Sortal "DispatchUndecided" "" (Some "a decidable method selection") (Some reason) CoverageGap None
    | UnificationUndecided reason ->
        makeDetail Agreement "UnificationUndecided" "" (Some "a pattern unification") (Some reason) CoverageGap None
    | TypeMismatch(ErrorAddress address, ExpectedRuntimeGuard) ->
        simple Sortal "NotARuntimeGuard" (string address) "a runtime guard" Input
    | TypeMismatch(ErrorAddress address, ExpectedConstructor) ->
        simple Sortal "NotAConstructor" (string address) "a sort constructor" Input
    | RuntimeGuardUndecided reason ->
        makeDetail Sortal "RuntimeGuardUndecided" "" (Some "a decidable guard") (Some reason) CoverageGap None
    | SortError(SortFault.Mismatch(first, second)) -> simple Sortal "ValueSetMismatch" second first Input
    | SortError(SortFault.NotClosed set) -> simple Sortal "ValueSetNotClosed" set "a declared domain" Input
    | ArityError(ArityFault.InvalidGeneric(word, arity)) ->
        makeDetail
            Sortal
            "InvalidGenericArity"
            word
            (Some "a non-negative integer arity")
            (Some(numberText arity))
            Input
            None
    | Unresolved(UnknownKind.Generic, name) -> simple Sortal "UnknownGeneric" name "a declared generic" Input
    | ArityError(ArityFault.GenericMismatch(name, expected, actual)) ->
        makeDetail
            Sortal
            "GenericArityMismatch"
            name
            (Some(string expected + " arguments"))
            (Some(string actual))
            Input
            None
    | ArityError(ArityFault.MethodMismatch(name, expected, actual)) ->
        makeDetail
            Sortal
            "MethodArityMismatch"
            name
            (Some(string expected + " arguments"))
            (Some(string actual))
            Input
            None
    | NoApplicableMethod(name, arguments) ->
        makeDetail
            Sortal
            "NoApplicableMethod"
            name
            (Some "an applicable method")
            (Some(String.concat ", " arguments))
            Input
            None
    | AmbiguousDispatch(name, candidates) ->
        makeDetail
            Sortal
            "AmbiguousDispatch"
            name
            (Some "one most-specific method")
            (Some(String.concat ", " candidates))
            Input
            None
    | ActorError(ActorFault.CannotKillSelf actor) ->
        simple Semantic "CannotKillSelf" (string (EntryIdentity.value actor)) "another actor" Input
    | ChannelError(channelId, ChannelFault.ChannelNotFound) ->
        simple Semantic "ChannelNotFound" (string (EntryIdentity.value channelId)) "an open channel" Input
    | ChannelError(channelId, ChannelFault.ChannelClosed) ->
        simple Semantic "ChannelClosed" (string (EntryIdentity.value channelId)) "an open channel" Input
    | ChannelError(channelId, ChannelFault.SendFailed reason) ->
        makeDetail
            Semantic
            "ChannelSendFailed"
            (string (EntryIdentity.value channelId))
            (Some "a successful send")
            (Some reason)
            Input
            None
    | ChannelError(channelId, ChannelFault.ReceiveFailed reason) ->
        makeDetail
            Semantic
            "ChannelReceiveFailed"
            (string (EntryIdentity.value channelId))
            (Some "a successful receive")
            (Some reason)
            Input
            None
    | TypeMismatch(ErrorWord word, ExpectedEvent) -> simple Semantic "NotAnEvent" word "a ChannelEvent" Input
    | TypeMismatch(ErrorWord word, ExpectedEventList) ->
        simple Semantic "NotAnEventList" word "a list of ChannelEvents" Input
    | EmptyEventChoice word -> simple Semantic "EmptyEventChoice" word "at least one event" Input
    | EventGuardFailed word -> simple Semantic "EventGuardFailed" word "a guard that produces a value" Input
    | EventGuardBudget word -> simple Semantic "EventGuardBudget" word "a guard within budget" CoverageGap
    | EnumError(EnumFault.InvalidWordCategory category) ->
        simple Structural "InvalidWordCategory" category "a word category" Input
    | EnumError(EnumFault.InvalidWordActive value) -> simple Structural "InvalidWordActive" value "true or false" Input
    | UnmatchedReturn -> simple Structural "UnmatchedReturn" "" "an enclosing apply" Input
    | Unresolved(UnknownKind.Class, name) -> simple Sortal "UnknownClass" name "a declared class" Input
    | Unresolved(UnknownKind.ValueSet, name) -> simple Sortal "UnknownValueSet" name "a declared value set" Input
    | Unresolved(UnknownKind.Slot, name) -> simple Sortal "UnknownSlot" name "a declared slot" Input
    | MissingSlot slot -> simple Sortal "MissingSlot" slot "the slot on the instance" Input
    | UnboundSlot slot -> simple Sortal "UnboundSlot" slot "an initialized slot" Input
    | TypeMismatch(ErrorAddress address, ExpectedInstance) -> simple Sortal "NotAnInstance" (string address) "an instance" Input
    | TypeMismatch(ErrorAddress address, ExpectedClass) -> simple Sortal "NotAClass" (string address) "a class" Input
    | SortError(SortFault.SlotMismatch slot) -> simple Sortal "SlotTypeMismatch" slot "a value in the slot's set" Input
    | ClassCycle name -> simple Sortal "ClassCycle" name "an acyclic precedence" Input
    | InconsistentPrecedence name -> simple Sortal "InconsistentPrecedence" name "a consistent C3 order" Input
    | NoNextMethod -> simple Sortal "NoNextMethod" "" "a next method" Input
    | InvalidMethodQualifier qualifier ->
        simple Structural "InvalidMethodQualifier" qualifier "before, after, around, or primary" Input
    | TypeMismatch(ErrorAddress address, ExpectedMethod) -> simple Sortal "NotAMethod" (string address) "a method" Input
    | Unresolved(UnknownKind.Metaclass, name) -> simple Sortal "UnknownMetaclass" name "a declared metaclass" Input
    | TypeMismatch(ErrorAddress address, ExpectedCell) -> simple Sortal "NotACell" (string address) "a cell" Input
    | EnumError(EnumFault.InvalidConditionKind kind) ->
        simple Semantic "InvalidConditionKind" kind "a condition kind" Input
    | TypeMismatch(ErrorAddress address, ExpectedConditionValue) ->
        simple Semantic "NotACondition" (string address) "a condition" Input
    | TypeMismatch(ErrorAddress address, ExpectedRestart) -> simple Semantic "NotARestart" (string address) "a restart" Input
    | NoSupervisionPolicy word -> simple Semantic "NoSupervisionPolicy" word "a configured supervision policy" Input
    | EnumError(EnumFault.InvalidRestartType kind) ->
        simple Structural "InvalidRestartType" kind "permanent, transient, or temporary" Input
    | LimitError(LimitFault.NegativeSupervisionLimit(word, value)) ->
        makeDetail
            Semantic
            "InvalidSupervisionLimit"
            word
            (Some "a non-negative integer limit")
            (Some(string value))
            Input
            None
    | LimitError(LimitFault.SupervisionExceeded(strategy, restarts)) ->
        makeDetail
            Semantic
            "SupervisionLimitExceeded"
            strategy
            (Some "a restart within the configured budget")
            (Some(string restarts + " restart(s)"))
            Input
            None
    | TypeMismatch(ErrorAddress address, ExpectedActorHandle) ->
        simple Semantic "NotAnActorHandle" (string address) "a handle returned by spawn or supervise" Input
    | ActorError(ActorFault.NoMailbox actor) -> simple Semantic "NoActorMailbox" (string actor) "an actor with a mailbox" Input
    | TypeMismatch(ErrorAddress reference, ExpectedMonitor) ->
        simple Semantic "NotAMonitor" (string reference) "a live monitor reference" Input
    | ActorError(ActorFault.UnknownRegisteredName name) ->
        simple Semantic "UnknownRegisteredName" name "a registered actor name" Input
    | ActorError(ActorFault.NameInUse name) -> simple Semantic "NameInUse" name "an unregistered name" Input
    | PatternError(PatternFault.NoClauseMatched subject) ->
        simple Structural "NoClauseMatched" subject "a matching clause" Input
    | PatternError(PatternFault.MalformedPattern reason) ->
        simple Structural "MalformedPattern" reason "a well-formed pattern" Input
    | PatternError(PatternFault.MissingPin name) ->
        simple Structural "MissingPin" name "a bound outer variable" Input
    | PatternError(PatternFault.UnboundRepeated name) ->
        simple Structural "RepeatedBeforeBound" name "an earlier binding in the pattern" Input
    | PatternError(PatternFault.PatternUndecided reason) ->
        simple Semantic "PatternUndecided" reason "a decidable pattern" CoverageGap
    | PatternError(PatternFault.GuardFailed(name, _)) ->
        simple Semantic "GuardFailed" name "a guard that answers true or false" Input

/// A guard verdict as a violation: a holding guard is no violation, a failed
/// guard is the input's sortal fault, and an undecidable guard is a coverage
/// gap with its reason (D61).
let ofGuardVerdict (field: string) (verdict: TypeLattice.GuardVerdict) : Violation option =
    match verdict with
    | TypeLattice.GuardHolds -> None
    | TypeLattice.GuardDoesNotHold ->
        Some(make Sortal "GuardDoesNotHold" field (Some "a value the guard accepts") Input None)
    | TypeLattice.GuardUndecided reason ->
        Some(makeDetail Sortal "GuardUndecided" field (Some "a decidable guard") (Some reason) CoverageGap None)

// ---- SMT evidence (D62) ----------------------------------------------------

/// An SMT outcome as a violation. A proved universal property and a witnessed
/// possibility are successes and produce none; a falsified property carries the
/// solver's counterexample, and a refuted possibility carries the unsat core.
/// `Unknown` and `Unavailable` are coverage gaps, never a pass.
let ofSmtDischarge (discharge: Smt.Discharge) : Violation option =
    match discharge.Outcome with
    | Smt.Proved -> None
    | Smt.Witnessed _ -> None
    | Smt.Falsified model ->
        Some
            { makeDetail
                  Semantic
                  "Falsified"
                  ""
                  (Some "the postcondition to hold")
                  (Some(Smt.describeModel model))
                  Input
                  None with
                Witness = Some(Model model)
            }
    | Smt.NoWitness ->
        Some
            { makeDetail Semantic "NoWitness" "" (Some "a satisfying witness") None Input None with
                Witness = Some(UnsatCore discharge.Core)
            }
    | Smt.Unknown reason ->
        Some(makeDetail Semantic "Unknown" "" (Some "a decided obligation") (Some reason) CoverageGap None)
    | Smt.Unavailable reason ->
        Some(makeDetail Semantic "Unavailable" "" (Some "an encodable obligation") (Some reason) CoverageGap None)

/// A bare outcome carries no evidence; use `ofSmtDischarge` when the solver's
/// model or core matters.
let ofSmtOutcome (outcome: Smt.Outcome) : Violation option =
    ofSmtDischarge { Outcome = outcome; Core = [] }
