module Parser

open Types

/// The sentence-initial quantifier of Scale Basic English.
type SentenceQuantifier =
    | Every
    | No
    | Exists

/// The two licensed sentence shapes. A statement quantifies over its subject;
/// a transition is event-triggered: when its guard holds, the effect must hold.
/// The kind is a case, not a boolean, so a downstream match cannot read a
/// transition as an ordinary statement.
type SentenceKind =
    | Statement of SentenceQuantifier
    | EventTriggered

/// The comparison vocabulary admitted by a `has` condition. The executable
/// projection shares `Types.ComparisonOperator`, so there is one operator type
/// between the grammar, the emitter, and the SMT encoder.
type SentenceComparison = ComparisonOperator

/// A condition in the relative clause. `BooleanCondition` is a boolean field
/// phrase; `NumericCondition` is a numeric comparison.
type Condition = Types.Image.Condition

/// The grammar's sentence before it is projected onto the executable emitter
/// shape. The projection is separate so a grammar-valid but not-yet-executable
/// sentence is refused by name instead of being read as something else.
type Sentence =
    {
        Kind: SentenceKind
        Subject: string
        Conditions: Condition list
        Verb: string
    }

/// A named parser refusal. The executable parser never guesses: every token it
/// cannot place becomes one of these cases.
type ParseError =
    | EmptySentence
    | ExpectedQuantifier of found: string
    | UnsupportedQuantifier of quantifier: string
    | ExpectedSubject of found: string
    | ExpectedCondition of found: string
    | ExpectedComparison of found: string
    | ExpectedVerb of found: string
    | UnexpectedToken of found: string
    | MissingFullStop
    | ArticleNotAllowed of found: string
    | PronounNotAllowed of found: string
    | ConditionalNotAllowed of found: string
    | NegationNotAllowed of found: string
    | UnknownToken of found: string
    | NotAnInteger of found: string
    | ExpectedTransitionComma of found: string
    | TransitionSubjectMismatch of trigger: string * effect: string
    | TransitionOutcomeNotUniversal of found: string

/// The active vocabulary entry named by a canonical token.
let entryOf (vocabulary: VocabularyEntry list) (token: string) : VocabularyEntry option =
    vocabulary |> List.tryFind (fun entry -> entry.Token = token && entry.IsActive)

/// The category of an active vocabulary token.
let categoryOf (vocabulary: VocabularyEntry list) (token: string) : WordCategory option =
    entryOf vocabulary token |> Option.map (fun entry -> entry.Category)

/// A field phrase is a boolean condition only when its surface form names the
/// copula, for example `is alive`. A bare field noun such as `health` is not a
/// sentence condition; it is a value used by a `has` comparison.
let isPredicatePhrase (vocabulary: VocabularyEntry list) (token: string) : bool =
    match entryOf vocabulary token with
    | Some entry ->
        entry.Category = FieldName
        && entry.Form.StartsWith("is ", System.StringComparison.Ordinal)
    | None -> false

/// The canonical token for a comparison. The tokenizer has already combined a
/// multiword form such as `greater than` into `greaterThan`.
let comparisonOf (token: string) : SentenceComparison option =
    match token with
    | "greaterThan" -> Some ComparisonGreaterThan
    | "atLeast" -> Some ComparisonAtLeast
    | "lessThan" -> Some ComparisonLessThan
    | "atMost" -> Some ComparisonAtMost
    | "equalTo" -> Some ComparisonEqualTo
    | _ -> None

/// Scale Basic English comparisons are integer comparisons. The SMT layer is
/// integer-valued, so a decimal literal is refused rather than rounded.
let parseInteger (token: string) : Result<RealNumber, ParseError> =
    match
        System.Int32.TryParse(
            token,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture
        )
    with
    | true, value -> Ok(double value)
    | false, _ -> Error(NotAnInteger token)

let quantifierOf (vocabulary: VocabularyEntry list) (token: string) : Result<SentenceQuantifier, ParseError> =
    match categoryOf vocabulary token with
    | Some WordCategory.Quantifier ->
        match token with
        | "every" -> Ok Every
        | "no" -> Ok No
        | "some" -> Ok Exists
        | "all" -> Error(UnsupportedQuantifier "all")
        | _ -> Error(ExpectedQuantifier token)
    | Some WordCategory.Article -> Error(ArticleNotAllowed token)
    | Some WordCategory.Pronoun -> Error(PronounNotAllowed token)
    | Some WordCategory.Conditional -> Error(ConditionalNotAllowed token)
    | Some WordCategory.Negation -> Error(NegationNotAllowed token)
    | Some _ -> Error(ExpectedQuantifier token)
    | None -> Error(UnknownToken token)

let subjectOf (vocabulary: VocabularyEntry list) (token: string) : Result<string, ParseError> =
    match categoryOf vocabulary token with
    | Some WordCategory.TypeName -> Ok token
    | Some WordCategory.Article -> Error(ArticleNotAllowed token)
    | Some WordCategory.Pronoun -> Error(PronounNotAllowed token)
    | Some _ -> Error(ExpectedSubject token)
    | None -> Error(UnknownToken token)

let verbOf (vocabulary: VocabularyEntry list) (token: string) : Result<string, ParseError> =
    match categoryOf vocabulary token with
    | Some WordCategory.FunctionName -> Ok token
    | Some WordCategory.Article -> Error(ArticleNotAllowed token)
    | Some WordCategory.Pronoun -> Error(PronounNotAllowed token)
    | Some _ -> Error(ExpectedVerb token)
    | None -> Error(UnknownToken token)

let unexpectedOf (vocabulary: VocabularyEntry list) (token: string) : ParseError =
    match categoryOf vocabulary token with
    | None -> UnknownToken token
    | Some _ -> UnexpectedToken token

/// Parses the value side of `has <field> <comparison> <integer>`.
let parseHasCondition
    (vocabulary: VocabularyEntry list)
    (fieldToken: string)
    (tokens: string list)
    : Result<Condition * string list, ParseError> =
    match tokens with
    | comparisonToken :: valueToken :: rest ->
        match categoryOf vocabulary fieldToken, categoryOf vocabulary comparisonToken, parseInteger valueToken with
        | Some WordCategory.FieldName, Some WordCategory.Comparison, Ok value ->
            match comparisonOf comparisonToken with
            | Some comparison -> Ok(NumericCondition(fieldToken, comparison, value), rest)
            | None -> Error(ExpectedComparison comparisonToken)
        | Some WordCategory.FieldName, Some WordCategory.Comparison, Error error -> Error error
        | Some WordCategory.FieldName, _, _ -> Error(ExpectedComparison comparisonToken)
        | _ -> Error(ExpectedCondition fieldToken)
    | _ -> Error(ExpectedComparison "")

let parseCondition
    (vocabulary: VocabularyEntry list)
    (tokens: string list)
    : Result<Condition * string list, ParseError> =
    match tokens with
    | [] -> Error(ExpectedCondition "")
    | token :: rest ->
        match categoryOf vocabulary token with
        | Some WordCategory.FieldName when isPredicatePhrase vocabulary token -> Ok(BooleanCondition token, rest)
        | Some WordCategory.FieldName -> Error(ExpectedCondition token)
        | Some WordCategory.Copula when token = "is" ->
            match rest with
            | fieldToken :: tail ->
                match categoryOf vocabulary fieldToken with
                | Some WordCategory.FieldName -> Ok(BooleanCondition fieldToken, tail)
                | Some WordCategory.Article -> Error(ArticleNotAllowed fieldToken)
                | Some WordCategory.Pronoun -> Error(PronounNotAllowed fieldToken)
                | Some _ -> Error(ExpectedCondition fieldToken)
                | None -> Error(UnknownToken fieldToken)
            | [] -> Error(ExpectedCondition "")
        | Some WordCategory.Copula when token = "has" ->
            match rest with
            | fieldToken :: more -> parseHasCondition vocabulary fieldToken more
            | [] -> Error(ExpectedCondition "")
        | Some WordCategory.Conditional -> Error(ConditionalNotAllowed token)
        | Some WordCategory.Negation -> Error(NegationNotAllowed token)
        | Some WordCategory.Article -> Error(ArticleNotAllowed token)
        | Some WordCategory.Pronoun -> Error(PronounNotAllowed token)
        | Some _ -> Error(ExpectedCondition token)
        | None -> Error(UnknownToken token)

let rec parseConditions
    (vocabulary: VocabularyEntry list)
    (tokens: string list)
    (accumulated: Condition list)
    : Result<Condition list * string list, ParseError> =
    match parseCondition vocabulary tokens with
    | Error error -> Error error
    | Ok(condition, rest) ->
        match rest with
        | next :: more when next = "and" && categoryOf vocabulary next = Some WordCategory.Connective ->
            parseConditions vocabulary more (condition :: accumulated)
        | _ -> Ok(List.rev (condition :: accumulated), rest)

/// Parses one relative clause's disjunction: conjunctions joined by `or`.
/// `and` binds tighter than `or`; a single conjunction is returned as its
/// atoms so the common path is unchanged, and a real disjunction is one
/// `Disjunction` atom in the top-level conjunction.
let rec parseClauses
    (vocabulary: VocabularyEntry list)
    (tokens: string list)
    (accumulated: Condition list list)
    : Result<Condition list list * string list, ParseError> =
    match parseConditions vocabulary tokens [] with
    | Error error -> Error error
    | Ok(conditions, rest) ->
        match rest with
        | next :: more when next = "or" && categoryOf vocabulary next = Some WordCategory.Connective ->
            parseClauses vocabulary more (conditions :: accumulated)
        | _ -> Ok(List.rev (conditions :: accumulated), rest)

let parseDisjunction
    (vocabulary: VocabularyEntry list)
    (tokens: string list)
    : Result<Condition list * string list, ParseError> =
    match parseClauses vocabulary tokens [] with
    | Error error -> Error error
    | Ok(clauses, rest) ->
        match clauses with
        | [ single ] -> Ok(single, rest)
        | _ -> Ok([ Disjunction clauses ], rest)

let parseAfterSubject
    (vocabulary: VocabularyEntry list)
    (tokens: string list)
    : Result<Condition list * string * string list, ParseError> =
    match tokens with
    | next :: rest when next = "that" && categoryOf vocabulary next = Some WordCategory.Connective ->
        match parseDisjunction vocabulary rest with
        | Error error -> Error error
        | Ok(conditions, afterConditions) ->
            match afterConditions with
            | verbToken :: tail ->
                match verbOf vocabulary verbToken with
                | Ok verb -> Ok(conditions, verb, tail)
                | Error error -> Error error
            | [] -> Error(ExpectedVerb "")
    | verbToken :: tail ->
        match verbOf vocabulary verbToken with
        | Ok verb -> Ok([], verb, tail)
        | Error error -> Error error
    | [] -> Error(ExpectedVerb "")

let parseStatement (vocabulary: VocabularyEntry list) (tokens: string list) : Result<Sentence, ParseError> =
    match tokens with
    | [] -> Error EmptySentence
    | quantifierToken :: rest ->
        match quantifierOf vocabulary quantifierToken with
        | Error error -> Error error
        | Ok quantifier ->
            match rest with
            | subjectToken :: afterSubject ->
                match subjectOf vocabulary subjectToken with
                | Error error -> Error error
                | Ok subject ->
                    match parseAfterSubject vocabulary afterSubject with
                    | Error error -> Error error
                    | Ok(conditions, verb, remainder) ->
                        match remainder with
                        | [] ->
                            Ok
                                {
                                    Kind = Statement quantifier
                                    Subject = subject
                                    Conditions = conditions
                                    Verb = verb
                                }
                        | extra :: _ -> Error(unexpectedOf vocabulary extra)
            | [] -> Error(ExpectedSubject "")

/// Parses the event-triggered shape `when <subject> <condition> and … , every
/// <subject> <verb>.` (D55). The trigger names its subject explicitly because
/// the grammar refuses pronouns (D18); the effect must name the same subject,
/// so an omitted or changed subject is a named refusal rather than an anaphor
/// the parser invents. A transition's outcome is universal: an effect is
/// required, not negated or merely possible.
let parseTransition (vocabulary: VocabularyEntry list) (tokens: string list) : Result<Sentence, ParseError> =
    match tokens with
    | [] -> Error(ExpectedSubject "")
    | guardSubjectToken :: afterGuardSubject ->
        match subjectOf vocabulary guardSubjectToken with
        | Error error -> Error error
        | Ok guardSubject ->
            let conditionTokens =
                match afterGuardSubject with
                | connective :: rest when
                    connective = "that"
                    && categoryOf vocabulary connective = Some WordCategory.Connective
                    ->
                    rest
                | _ -> afterGuardSubject

            match conditionTokens with
            | [] -> Error(ExpectedCondition "")
            | comma :: _ when comma = "," -> Error(ExpectedCondition "")
            | _ ->
                match parseDisjunction vocabulary conditionTokens with
                | Error error -> Error error
                | Ok([], _) -> Error(ExpectedCondition "")
                | Ok(conditions, afterConditions) ->
                    match afterConditions with
                    | comma :: outcomeTokens when comma = "," ->
                        match outcomeTokens with
                        | quantifierToken :: afterQuantifier ->
                            match quantifierOf vocabulary quantifierToken with
                            | Error error -> Error error
                            | Ok Every ->
                                match afterQuantifier with
                                | effectSubjectToken :: afterSubject ->
                                    match subjectOf vocabulary effectSubjectToken with
                                    | Error error -> Error error
                                    | Ok effectSubject when effectSubject = guardSubject ->
                                        match afterSubject with
                                        | verbToken :: remainder ->
                                            match verbOf vocabulary verbToken with
                                            | Error error -> Error error
                                            | Ok verb ->
                                                match remainder with
                                                | [] ->
                                                    Ok
                                                        {
                                                            Kind = EventTriggered
                                                            Subject = effectSubject
                                                            Conditions = conditions
                                                            Verb = verb
                                                        }
                                                | extra :: _ -> Error(unexpectedOf vocabulary extra)
                                        | [] -> Error(ExpectedVerb "")
                                    | Ok effectSubject -> Error(TransitionSubjectMismatch(guardSubject, effectSubject))
                                | [] -> Error(ExpectedSubject "")
                            | Ok _ -> Error(TransitionOutcomeNotUniversal quantifierToken)
                        | [] -> Error(ExpectedQuantifier "")
                    | found :: _ -> Error(ExpectedTransitionComma found)
                    | [] -> Error(ExpectedTransitionComma "")

let parseBody (vocabulary: VocabularyEntry list) (tokens: string list) : Result<Sentence, ParseError> =
    match tokens with
    | [] -> Error EmptySentence
    | first :: rest when first = "when" && categoryOf vocabulary first = Some WordCategory.Conditional ->
        parseTransition vocabulary rest
    | _ -> parseStatement vocabulary tokens

/// Parses the full sentence grammar. The final full stop is structural; a
/// sentence without it is not a sentence, not a sentence with a default verb.
let parseDetailed (vocabulary: VocabularyEntry list) (tokens: string list) : Result<Sentence, ParseError> =
    match tokens with
    | [] -> Error EmptySentence
    | _ ->
        match List.rev tokens with
        | "." :: reversedBody -> parseBody vocabulary (List.rev reversedBody)
        | _ -> Error MissingFullStop

/// Projects the grammar onto the executable emitter shape. `every` projects to
/// a universal capability, `no` to an invariant, and `some` to a possibility
/// (D33): the emitted artifact is a witness predicate and the SMT obligation is
/// satisfiability, not the universal implication. A transition keeps its own
/// pattern (D55), so a downstream match cannot read it as a capability. Every
/// admitted condition (boolean or numeric) is carried through unchanged.
let toParsedSentence (sentence: Sentence) : Result<ParsedSentence, ParseError> =
    Ok
        {
            Pattern =
                match sentence.Kind with
                | EventTriggered -> Transition
                | Statement quantifier ->
                    match quantifier with
                    | Every -> Capability
                    | No -> Invariant
                    | Exists -> Possibility
            Subject = sentence.Subject
            Conditions = sentence.Conditions
            Verb = sentence.Verb
        }

/// The executable parser used by the pipeline. It returns `None` for every
/// refusal; callers that need the reason use `parseDetailed` and
/// `toParsedSentence` directly.
let parse (vocabulary: VocabularyEntry list) (tokens: string list) : ParsedSentence option =
    parseDetailed vocabulary tokens
    |> Result.bind toParsedSentence
    |> function
        | Ok parsed -> Some parsed
        | Error _ -> None

/// A human-readable rendering of a refusal, for diagnostics at the edge.
let describeError (error: ParseError) : string =
    match error with
    | EmptySentence -> "The sentence is empty."
    | ExpectedQuantifier found -> "Expected 'every' or 'no', but found '" + found + "'."
    | UnsupportedQuantifier quantifier ->
        "The quantifier '"
        + quantifier
        + "' is not executable yet; use 'every' or 'no'."
    | ExpectedSubject found -> "Expected a subject noun, but found '" + found + "'."
    | ExpectedCondition found -> "Expected a condition, but found '" + found + "'."
    | ExpectedComparison found -> "Expected a comparison, but found '" + found + "'."
    | ExpectedVerb found -> "Expected a verb phrase, but found '" + found + "'."
    | UnexpectedToken found -> "Unexpected token '" + found + "'."
    | MissingFullStop -> "The sentence must end with a full stop."
    | ArticleNotAllowed found -> "The article '" + found + "' is not part of Scale Basic English."
    | PronounNotAllowed found -> "The pronoun '" + found + "' is not part of Scale Basic English."
    | ConditionalNotAllowed found ->
        "The conditional '"
        + found
        + "' is only licensed at the start of a transition sentence: 'when <subject> <condition>, every <subject> <verb>.'"
    | NegationNotAllowed found ->
        "The negation '"
        + found
        + "' is not part of Scale Basic English; use 'no' before the subject."
    | UnknownToken found -> "Unknown or inactive token '" + found + "'."
    | NotAnInteger found -> "Expected an integer value, but found '" + found + "'."
    | ExpectedTransitionComma found ->
        "Expected ',' between a transition's trigger and its effect, but found '"
        + found
        + "'."
    | TransitionSubjectMismatch(trigger, effect) ->
        "A transition's effect subject '"
        + effect
        + "' must be the same as its trigger subject '"
        + trigger
        + "'; Scale Basic English has no pronouns."
    | TransitionOutcomeNotUniversal found ->
        "A transition's outcome must be universal ('every'), but found '"
        + found
        + "'; an effect is required, not negated or merely possible."
