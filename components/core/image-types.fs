namespace Types

[<AutoOpen>]
module Image =
    /// A category of vocabulary entry.
    type WordCategory =
        | TypeName
        | FieldName
        | FunctionName
        | Quantifier
        | Connective
        | Conditional
        | Negation
        | Pronoun
        | Copula
        | Comparison
        | Article

    /// A vocabulary entry.
    type VocabularyEntry =
        {
            Token: string
            Form: string
            ImplementationName: string
            Category: WordCategory
            IsActive: bool
        }

    /// Word metaobject (D16 Stage 2, D21) — store-resident word definition.
    /// Replaces the vocabulary's ImplementationName seam with an implementation reference.
    /// Tensor schema: [WordTag; tokenId; formId; categoryCode; active; implRef]
    /// categoryCode = WordCategory discriminant (0=TypeName, 1=FieldName, 2=FunctionName, ...)
    /// implRef = 0.0 for built-ins (dispatcher matches token directly),
    ///           or genericFunctionId for store-resident words.
    [<Literal>]
    let WordTag = -6.0

    /// Predicate sort (D31) — a sort whose membership is decided by an
    /// store-resident quotation rather than only by a finite extension.
    /// Tensor schema: [PredicateSortTag; nameValue; quotationId; member…]
    /// The members are the base extension; the quotation decides the narrowing.
    [<Literal>]
    let PredicateSortTag = -11.0

    /// Class verdict (D32) — the answer to `class-of`.
    /// Tensor schema: [ValueTag; status; payload…]
    /// status 0 = Unknown (no payload), 1 = Class (the sort's name identity),
    /// 2 = Ambiguous (the candidate name identities), 3 = Undecided (the reason's text identity),
    /// 4 = Metaobject (the entry-kind code: 1 word, 2 generic, 3 method,
    /// 4 channel, 5 channel state, 6 event, 7 step, 8 predicate sort, 9 guard,
    /// 10 sort constructor).
    [<Literal>]
    let ValueTag = -12.0

    /// Guard metaobject (D35) — an store-resident guard definition.
    /// Tensor schema: [RuntimeGuardTag; nameId; quotationId]. The quotation decides the
    /// guard against a value; a new guard is data, not an F# case.
    [<Literal>]
    let RuntimeGuardTag = -13.0

    /// ValueSet constructor metaobject (D36) — an store-resident constructor that
    /// decides a sort's narrowing. Tensor schema: [ConstructorTag; nameId;
    /// quotationId]. A new constructor is data, not an F# case.
    [<Literal>]
    let ConstructorTag = -14.0

    /// Class definition metaobject (D44) — the cell-system class.
    /// Tensor schema:
    /// [ClassDefinitionTag; nameId; metaclassId; combinationId(-1 for none);
    ///  superclassCount; superclassId…; slotCount; slotId…]
    [<Literal>]
    let ClassDefinitionTag = -15.0

    /// Slot definition metaobject (D44).
    /// Tensor schema:
    /// [SlotDefinitionTag; nameId; valueSetId; defaultId(-1 for none); initializationCode]
    [<Literal>]
    let SlotDefinitionTag = -16.0

    /// Instance identity (D44) — written once; the state is appended separately.
    /// Tensor schema: [InstanceTag; classId; instanceNumber]
    [<Literal>]
    let InstanceTag = -17.0

    /// Instance state (D44) — appended per slot write; the newest entry is current.
    /// Tensor schema: [InstanceStateTag; instanceId; slotCount; slotId; valueId…]
    [<Literal>]
    let InstanceStateTag = -18.0

    /// Closure / code cell (D44).
    /// Tensor schema: [ClosureTag; quotationId; environmentId]
    [<Literal>]
    let ClosureTag = -19.0

    /// Environment frame (D44) — a lexical frame with a parent and name/value pairs.
    /// Tensor schema: [EnvironmentFrameTag; parentId(-1 for none); bindingCount;
    ///  nameId; valueId…]
    [<Literal>]
    let EnvironmentFrameTag = -20.0

    /// Method combination (D44).
    /// Tensor schema: [MethodCombinationTag; nameId; policyCode; qualifierCount;
    ///  qualifierCode…]
    [<Literal>]
    let MethodCombinationTag = -21.0

    /// Qualified method (D44) — a method with a combination qualifier.
    /// Tensor schema: [QualifiedMethodTag; qualifierCode; arity; bodyId;
    ///  specializerCount; specializerId…]
    [<Literal>]
    let QualifiedMethodTag = -22.0

    /// Metaclass definition (D44).
    /// Tensor schema: [MetaclassTag; nameId; superclassCount; superclassId…;
    ///  slotCount; slotId…]
    [<Literal>]
    let MetaclassTag = -23.0

    /// Condition value (D50).
    /// Tensor schema: [ConditionTag; kindCode; nameId; messageId]
    [<Literal>]
    let ConditionTag = -24.0

    /// Restart value (D50).
    /// Tensor schema: [RestartTag; nameId; handlerId]
    [<Literal>]
    let RestartTag = -25.0

    /// Linear-contract verdict (D54).
    /// Tensor schema: [LinearVerdictTag; status; reasonId]
    /// status 0 = linear, 1 = nonlinear, 2 = undecided; reasonId is 0 when absent.
    [<Literal>]
    let LinearVerdictTag = -26.0

    /// Higher-order unification verdict (D59).
    /// Tensor schema: [UnificationVerdictTag; status; reasonId]
    /// status 0 = unified, 1 = not unified, 2 = undecided; reasonId is 0 when absent.
    [<Literal>]
    let UnificationVerdictTag = -27.0

    /// ChannelEvent metaobject (W7) — store-resident ChannelEvents event description.
    /// Tensor schema: [ActorHandleTag]. A handle is a capability entry, not an
    /// actor address: `kill`/`resume` accept only an entry registered by
    /// `spawn`/`supervise` (D64).
    let ActorHandleTag = -28.0

    /// Tensor schema: [ExitSignalTag; reasonCode]. A trapped link exit becomes a
    /// mailbox message instead of a kill (D66).
    let ExitSignalTag = -29.0

    /// Tensor schema: [DownTag; monitorRef; reasonCode]. A monitor observes an
    /// actor's exit without linking to it (D67).
    let DownTag = -30.0

    /// Reserved runtime system-message class (D75). The runtime handles these
    /// messages before user code sees them. Tensor schema:
    /// [SystemMessageTag; operation; payload…].
    let SystemMessageTag = -31.0

    /// A status reply produced by the runtime (D75). Tensor schema:
    /// [SystemStatusTag; statusCode; suspended; mailboxDepth].
    let SystemStatusTag = -32.0

    /// Tensor schema: [RemoteHandleTag; instance; capability]. A remote handle
    /// names an actor in another instance. The host resolves the pair; the
    /// language records a transport request instead of a local mailbox delivery.
    let RemoteHandleTag = -33.0

    /// Tensor schema: [QuoteTag; documentId]. A quotation is a code value, not
    /// a text tensor. documentId names the document for the quotation body: a
    /// term-program document, or an instruction-program document (D202). The tag
    /// is defined in the kernel because the kernel makes code values too.
    let QuoteTag = LoweredInstructions.CodeValueTag

    /// System-message operations (D75). The first payload entry is the operation.
    let SystemSuspendOperation = 0.0
    let SystemResumeOperation = 1.0
    let SystemCodeChangeOperation = 2.0
    let SystemStatusOperation = 3.0
    let SystemShutdownOperation = 4.0

    /// A stable numeric code for an exit reason, used in exit-signal messages.
    let exitReasonCode (reason: ExitReason) : RealNumber =
        match reason with
        | NormalExit -> 0.0
        | ErrorExit _ -> 1.0
        | TimeoutExit -> 2.0
        | ShutdownExit -> 3.0
        | KilledExit -> 4.0

    /// Tensor schema: [ChannelEventTag; kind; ...]. Kind 0 is never, 1 is send,
    /// 2 is receive, 3 is choose, 4 is wrap, 5 is guard, and 6 is with-fallback.
    [<Literal>]
    let ChannelEventTag = -8.0

    /// A word metaobject projection. The store entry is the authority.
    type Word =
        {
            Address: EntryIdentity
            Token: EntryIdentity
            Form: EntryIdentity
            Category: WordCategory
            IsActive: bool
            Implementation: EntryIdentity option // None = built-in, Some = generic function
        }

    /// A numeric comparison in a Scale Basic English condition. The names are
    /// deliberately distinct from `DomainExpression.Comparison` and
    /// `ScaleFile.RefinementGuard` so a module that opens several of them never resolves a
    /// comparison case by accident.
    type ComparisonOperator =
        | ComparisonGreaterThan
        | ComparisonAtLeast
        | ComparisonLessThan
        | ComparisonAtMost
        | ComparisonEqualTo

    /// One condition in a sentence's relative clause. The grammar and the
    /// executable projection share this type: a `BooleanCondition` names a boolean
    /// field, and a `NumericCondition` compares an integer-valued field. A
    /// `Disjunction` is a disjunctive-normal-form clause: each alternative is a
    /// conjunction of atomic conditions. `and` binds tighter than `or`, and the
    /// parser flattens a single conjunctive alternative to its atoms, so the common
    /// path is unchanged (D58).
    type Condition =
        | BooleanCondition of field: string
        | NumericCondition of field: string * comparison: ComparisonOperator * value: RealNumber
        | Disjunction of alternatives: Condition list list

    /// The canonical surface text of a comparison, used in condition text, emitted
    /// headers, and the repair bundle's stored conditions.
    let comparisonText (comparison: ComparisonOperator) : string =
        match comparison with
        | ComparisonGreaterThan -> ">"
        | ComparisonAtLeast -> ">="
        | ComparisonLessThan -> "<"
        | ComparisonAtMost -> "<="
        | ComparisonEqualTo -> "="

    /// The field a single atomic condition constrains. A disjunction names several
    /// fields, so callers that need all of them use `conditionFields`.
    let conditionField (condition: Condition) : string =
        match condition with
        | BooleanCondition field -> field
        | NumericCondition(field, _, _) -> field
        | Disjunction _ -> invalidOp "A disjunction names several fields; use conditionFields to collect them."

    /// Every atomic condition a condition contains. A disjunction is flattened, so
    /// a caller can inspect every atom without knowing the formula's shape.
    let rec conditionAtoms (condition: Condition) : Condition list =
        match condition with
        | Disjunction alternatives -> alternatives |> List.collect (List.collect conditionAtoms)
        | atom -> [ atom ]

    /// Every field a condition names, in first-seen order.
    let conditionFields (condition: Condition) : string list =
        conditionAtoms condition |> List.map conditionField |> List.distinct

    /// The canonical text of a condition. A boolean condition is the field token;
    /// a numeric condition is `field comparison value`; a disjunction is
    /// `(clause) or (clause)`, where a clause is `atom and atom`. The form
    /// round-trips through `parseConditionText` and is the repair bundle's stored
    /// representation.
    let rec conditionText (condition: Condition) : string =
        match condition with
        | BooleanCondition field -> field
        | NumericCondition(field, comparison, value) ->
            let number = value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

            field + " " + comparisonText comparison + " " + number
        | Disjunction alternatives ->
            alternatives
            |> List.map (fun clause -> "(" + String.concat " and " (clause |> List.map conditionText) + ")")
            |> String.concat " or "

    /// Maps every field a condition names, leaving comparisons and values intact.
    /// Used at the boundary where a grammar token becomes a domain field.
    let rec mapConditionField (mapping: string -> string) (condition: Condition) : Condition =
        match condition with
        | BooleanCondition field -> BooleanCondition(mapping field)
        | NumericCondition(field, comparison, value) -> NumericCondition(mapping field, comparison, value)
        | Disjunction alternatives -> Disjunction(alternatives |> List.map (List.map (mapConditionField mapping)))

    let private operatorOf (text: string) : ComparisonOperator option =
        match text with
        | ">" -> Some ComparisonGreaterThan
        | ">=" -> Some ComparisonAtLeast
        | "<" -> Some ComparisonLessThan
        | "<=" -> Some ComparisonAtMost
        | "=" -> Some ComparisonEqualTo
        | _ -> None

    /// Splits a canonical condition text into atoms, comparisons, integers, and
    /// parentheses. A numeric atom is `field comparison integer`; a boolean atom is
    /// one field token.
    let private splitConditionTokens (text: string) : string list =
        text.Replace("(", " ( ").Replace(")", " ) ").Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList

    let private integerToken (token: string) : Result<RealNumber, string> =
        match
            System.Int32.TryParse(
                token,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture
            )
        with
        | true, value -> Ok(double value)
        | false, _ -> Error("Condition value is not an integer: " + token)

    let rec private parseConditionAtom (tokens: string list) : Result<Condition * string list, string> =
        match tokens with
        | (("and" | "or" | ")") as token) :: _ -> Error("Unexpected token in condition: " + token)
        | field :: comparisonToken :: valueToken :: rest ->
            match operatorOf comparisonToken with
            | Some comparison ->
                integerToken valueToken
                |> Result.map (fun value -> NumericCondition(field, comparison, value), rest)
            | None ->
                match System.Int32.TryParse(valueToken) with
                | true, _ ->
                    Error(
                        "Unknown comparison operator in condition: "
                        + field
                        + " "
                        + comparisonToken
                        + " "
                        + valueToken
                    )
                | false, _ -> Ok(BooleanCondition field, comparisonToken :: valueToken :: rest)
        | field :: rest -> Ok(BooleanCondition field, rest)
        | [] -> Error("Cannot parse condition: empty text.")

    and private parseConditionClause (tokens: string list) : Result<Condition list * string list, string> =
        parseConditionAtom tokens
        |> Result.bind (fun (atom, rest) ->
            match rest with
            | "and" :: more ->
                parseConditionClause more
                |> Result.map (fun (atoms, after) -> atom :: atoms, after)
            | _ -> Ok([ atom ], rest))

    and private parseConditionGroup (tokens: string list) : Result<Condition list * string list, string> =
        match tokens with
        | "(" :: rest ->
            parseConditionClause rest
            |> Result.bind (fun (clause, after) ->
                match after with
                | ")" :: tail -> Ok(clause, tail)
                | _ -> Error("Unclosed parenthesis in condition text."))
        | _ -> parseConditionClause tokens

    and private parseConditionFormula (tokens: string list) : Result<Condition list list * string list, string> =
        parseConditionGroup tokens
        |> Result.bind (fun (clause, rest) ->
            match rest with
            | "or" :: more ->
                parseConditionFormula more
                |> Result.map (fun (clauses, after) -> clause :: clauses, after)
            | _ -> Ok([ clause ], rest))

    /// Parses the canonical condition text produced by `conditionText`. The repair
    /// corpus is the only caller that parses stored conditions, so the format is
    /// validated once at that boundary and the rest of the pipeline carries the
    /// typed value. `and` binds tighter than `or`, matching the sentence grammar.
    let parseConditionText (text: string) : Result<Condition, string> =
        match parseConditionFormula (splitConditionTokens text) with
        | Error reason -> Error reason
        | Ok(clauses, []) ->
            match clauses with
            | [ [ single ] ] -> Ok single
            | _ -> Ok(Disjunction clauses)
        | Ok(_, unexpected :: _) -> Error("Unexpected token in condition: " + unexpected)

    /// A sentence pattern. `Possibility` is existential `some` (D33): the artifact
    /// is a witness predicate and the obligation is `pre ∧ post` satisfiability.
    type SentencePattern =
        | Capability
        | Invariant
        | Transition
        | Possibility

    /// A parsed sentence.
    type ParsedSentence =
        {
            Pattern: SentencePattern
            Subject: string
            Conditions: Condition list
            Verb: string
        }
