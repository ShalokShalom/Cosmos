module EvaluatorLowering

// ============================================================
// Lower the expression subset that has a direct opcode. This is Prelude code:
// the kernel owns the instruction set, not the language-to-instruction mapping.
// ============================================================

open ExpressionLogic
open PatternLogic
open LoweredInstructions

/// The closed expression-node universe, named so a refusal can say which node
/// was declined. There is one case per `Expression` constructor; keeping them in
/// step is checked by the exhaustive matches in `nodeOf` and `lowerExpression`.
type LoweredNode =
    | LiteralNode
    | WordNode
    | CallNode
    | PartialNode
    | HoleNode
    | PipelineNode
    | ReversePipelineNode
    | QuotationNode
    | BlockNode
    | LetNode
    | LambdaNode
    | IfNode
    | PatternMatchNode
    | DeclareKindNode
    | DeclarePredicateNode
    | ConditionNode
    | RestartNode
    | GenericNode
    | MessageNode
    | FailNode

/// Why a node was declined. A refusal is a value the emitter can match on, not
/// a string; it names the rule and the next action.
type LoweringRefusal =
    | NodeNotLowered of node: LoweredNode
    | CallTargetNotWord of target: LoweredNode
    | ReservedBindingName of name: string
    | DuplicateParameter of name: string
    | PatternPinNotLowered of name: string
    | PatternRepeatedNotLowered of name: string
    | PatternShadowingNotLowered of name: string
    | PatternGuardNotLowered of name: string
    | DuplicatePatternVariable of name: string

type LoweringWithInferenceFailure =
    | InferenceFailed of TypeRetrieval.InferenceDiagnostic
    | LoweringFailed of LoweringRefusal

let describeNode (node: LoweredNode) : string =
    match node with
    | LiteralNode -> "literal"
    | WordNode -> "word"
    | CallNode -> "call"
    | PartialNode -> "partial"
    | HoleNode -> "hole"
    | PipelineNode -> "pipeline"
    | ReversePipelineNode -> "reverse pipeline"
    | QuotationNode -> "quotation"
    | BlockNode -> "block"
    | LetNode -> "let"
    | LambdaNode -> "lambda"
    | IfNode -> "if"
    | PatternMatchNode -> "pattern-match"
    | DeclareKindNode -> "declare-kind"
    | DeclarePredicateNode -> "declare-predicate"
    | ConditionNode -> "condition"
    | RestartNode -> "restart"
    | GenericNode -> "generic"
    | MessageNode -> "message"
    | FailNode -> "fail"

let describeRefusal (refusal: LoweringRefusal) : string =
    match refusal with
    | NodeNotLowered node ->
        "the "
        + describeNode node
        + " expression is outside the lowered subset; use the interpreter"
    | CallTargetNotWord target ->
        "a call whose target is a "
        + describeNode target
        + " expression is outside the lowered subset; use the interpreter"
    | ReservedBindingName name ->
        "the binding name "
        + name
        + " is reserved syntax and cannot be bound; choose another name"
    | DuplicateParameter name ->
        "the parameter name "
        + name
        + " appears more than once; give each parameter a distinct name"
    | PatternPinNotLowered name ->
        "the pinned pattern variable "
        + name
        + " needs the interpreter's binding table; use the interpreter"
    | PatternRepeatedNotLowered name ->
        "the repeated pattern variable "
        + name
        + " needs unification; use the interpreter"
    | PatternShadowingNotLowered name ->
        "the shadowing pattern variable "
        + name
        + " is not in the lowered subset; use the interpreter"
    | PatternGuardNotLowered name ->
        "the pattern guard "
        + name
        + " needs a pinned predicate unit; use the interpreter"
    | DuplicatePatternVariable name ->
        "the pattern variable "
        + name
        + " is bound more than once in one clause; use distinct names"

let nodeOf (expression: Expression) : LoweredNode =
    match expression with
    | Literal _ -> LiteralNode
    | Word _ -> WordNode
    | Call _ -> CallNode
    | Partial _ -> PartialNode
    | Hole -> HoleNode
    | Pipeline _ -> PipelineNode
    | ReversePipeline _ -> ReversePipelineNode
    | Quotation _ -> QuotationNode
    | Block _ -> BlockNode
    | Let _ -> LetNode
    | Lambda _ -> LambdaNode
    | If _ -> IfNode
    | PatternMatch _ -> PatternMatchNode
    | DeclareKind _ -> DeclareKindNode
    | DeclarePredicate _ -> DeclarePredicateNode
    | Condition _ -> ConditionNode
    | Restart _ -> RestartNode
    | Generic _ -> GenericNode
    | Message _ -> MessageNode
    | ExpressionLogic.Fail _ -> FailNode

/// One lowered code object: a named unit with its arity and instructions. A
/// lambda's body is a unit, and the unit table is what the phase-2 emitter will
/// turn into methods.
type LoweredUnit =
    {
        Name: string
        Arity: int
        Instructions: Instruction list
    }

/// The lowering result: the unit table and the entry instructions.
type LoweredProgram =
    {
        Units: LoweredUnit list
        Instructions: Instruction list
    }

/// Lower a closed expression node, or decline it by name. A supported node is
/// fully lowered; a declined one is never emitted as a run-time failure, so the
/// emitter can fall back to the interpreter at lowering time. Whether a
/// multi-statement sequence carries a step record at each statement start is a
/// parameter (D201), never a default, so an emitter that does not ask for
/// history keeps the program `HistoryExcluded` and pays nothing.
let lowerExpressionWith
    (recordStatements: bool)
    (sourceExpression: Expression)
    : Result<LoweredProgram, LoweringRefusal> =
    let expression = ExpressionLogic.desugarPipeline sourceExpression
    let nextLabel = ref 0
    let nextSlot = ref 0
    let nextUnit = ref 0

    let freshLabel () =
        let value = nextLabel.Value
        nextLabel.Value <- value + 1
        value

    let freshSlot () =
        let value = nextSlot.Value
        nextSlot.Value <- value + 1
        value

    let freshUnit () =
        let value = nextUnit.Value
        nextUnit.Value <- value + 1
        value

    let mergeBindings
        (left: Map<string, Slot>)
        (right: Map<string, Slot>)
        : Result<Map<string, Slot>, LoweringRefusal> =
        right
        |> Map.fold
            (fun (accumulated: Result<Map<string, Slot>, LoweringRefusal>) name slot ->
                match accumulated with
                | Error refusal -> Error refusal
                | Ok bindings ->
                    if bindings.ContainsKey name then
                        Error(DuplicatePatternVariable name)
                    else
                        Ok(bindings.Add(name, slot)))
            (Ok left)

    let rec patternVariables (pattern: Pattern) : Set<string> =
        match pattern with
        | Variable(name, Fresh)
        | Variable(name, Repeated)
        | Variable(name, Shadowing) -> Set.singleton name
        | Variable(_, Pin) -> Set.empty
        | Constructor(_, children) ->
            children
            |> List.fold (fun accumulated child -> Set.union accumulated (patternVariables child)) Set.empty
        | TypedPattern(_, child) -> patternVariables child
        | Wildcard
        | NumberLiteral _
        | TextLiteral _
        | AtomLiteral _ -> Set.empty

    let rec usedNames (bound: Set<string>) (node: Expression) : Set<string> =
        match node with
        | Literal _ -> Set.empty
        | Word name ->
            if bound.Contains name then
                Set.empty
            else
                Set.singleton name
        | Call(Word name, arguments) ->
            let target =
                if bound.Contains name then
                    Set.empty
                else
                    Set.singleton name

            Set.union target (usedNamesList bound arguments)
        | Call(target, arguments) -> Set.union (usedNames bound target) (usedNamesList bound arguments)
        | Partial(target, arguments) -> Set.union (usedNames bound target) (usedNamesList bound arguments)
        | Hole -> Set.empty
        | Pipeline(value, stage, _) -> Set.union (usedNames bound value) (usedNames bound stage)
        | ReversePipeline(target, value) -> Set.union (usedNames bound target) (usedNames bound value)
        | Block body
        | Quotation body -> usedNamesList bound body
        | Let(name, value, body) -> Set.union (usedNames bound value) (usedNames (bound.Add name) body)
        | Lambda(parameters, body) ->
            usedNames
                (parameters
                 |> List.fold (fun (accumulated: Set<string>) (name: string) -> accumulated.Add name) bound)
                body
        | If(condition, thenBranch, elseBranch) ->
            Set.union (usedNames bound condition) (Set.union (usedNames bound thenBranch) (usedNames bound elseBranch))
        | PatternMatch(subject, clauses) ->
            clauses
            |> List.fold
                (fun accumulated clause ->
                    let clauseBound =
                        patternVariables clause.Pattern
                        |> Set.fold (fun (current: Set<string>) (name: string) -> current.Add name) bound

                    Set.union accumulated (usedNamesList clauseBound clause.Body))
                (usedNames bound subject)
        | DeclareKind _
        | DeclarePredicate _ -> Set.empty
        | Condition(_, message) -> usedNames bound message
        | Restart(_, handler) -> usedNames bound handler
        | Generic(_, _, methods) -> usedNamesList bound methods
        | Message(_, fields) -> usedNamesList bound fields
        | ExpressionLogic.Fail _ -> Set.empty

    and usedNamesList (bound: Set<string>) (nodes: Expression list) : Set<string> =
        nodes
        |> List.fold (fun accumulated node -> Set.union accumulated (usedNames bound node)) Set.empty

    let rec lowerPattern
        (failureLabel: LabelId)
        (subjectSlot: Slot)
        (pattern: Pattern)
        : Result<Instruction list * Map<string, Slot>, LoweringRefusal> =
        match pattern with
        | Wildcard -> Ok([], Map.empty)
        | Variable(name, Fresh) ->
            let slot = freshSlot ()
            Ok([ PushSlot subjectSlot; StoreSlot slot ], Map.ofList [ name, slot ])
        | Variable(name, Pin) -> Error(PatternPinNotLowered name)
        | Variable(name, Repeated) -> Error(PatternRepeatedNotLowered name)
        | Variable(name, Shadowing) -> Error(PatternShadowingNotLowered name)
        | NumberLiteral value ->
            Ok(
                [
                    PushSlot subjectSlot
                    TensorRank
                    LiteralNumber 0.0
                    Equals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    LiteralNumber 0.0
                    TensorItem
                    LiteralNumber value
                    Equals
                    JumpIfFalse failureLabel
                ],
                Map.empty
            )
        | TextLiteral value
        | AtomLiteral value ->
            Ok(
                [
                    PushSlot subjectSlot
                    IsText
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    LiteralText value
                    TextEquals
                    JumpIfFalse failureLabel
                ],
                Map.empty
            )
        | TypedPattern(sortName, child) ->
            let childSlot = freshSlot ()

            let check =
                [
                    PushSlot subjectSlot
                    TensorRank
                    LiteralNumber 1.0
                    Equals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    TensorSize
                    LiteralNumber 3.0
                    Equals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    LiteralNumber 0.0
                    TensorItem
                    LiteralNumber -3.0
                    Equals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    LiteralNumber 1.0
                    TensorEntry
                    LiteralText sortName
                    TextEquals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    LiteralNumber 2.0
                    TensorEntry
                    StoreSlot childSlot
                ]

            lowerPattern failureLabel childSlot child
            |> Result.map (fun (childInstructions, bindings) -> (check @ childInstructions, bindings))
        | Constructor(name, children) ->
            let check =
                [
                    PushSlot subjectSlot
                    TensorRank
                    LiteralNumber 1.0
                    Equals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    TensorSize
                    LiteralNumber(double (2 + List.length children))
                    Equals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    LiteralNumber 0.0
                    TensorItem
                    LiteralNumber -2.0
                    Equals
                    JumpIfFalse failureLabel
                    PushSlot subjectSlot
                    LiteralNumber 1.0
                    TensorEntry
                    LiteralText name
                    TextEquals
                    JumpIfFalse failureLabel
                ]

            children
            |> List.mapi (fun index child ->
                let childSlot = freshSlot ()

                lowerPattern failureLabel childSlot child
                |> Result.map (fun (childInstructions, bindings) ->
                    ([
                        PushSlot subjectSlot
                        LiteralNumber(double (2 + index))
                        TensorEntry
                        StoreSlot childSlot
                     ]
                     @ childInstructions,
                     bindings)))
            |> List.fold
                (fun (accumulated: Result<Instruction list * Map<string, Slot>, LoweringRefusal>) child ->
                    match accumulated, child with
                    | Error refusal, _ -> Error refusal
                    | _, Error refusal -> Error refusal
                    | Ok(instructions, bindings), Ok(childInstructions, childBindings) ->
                        mergeBindings bindings childBindings
                        |> Result.map (fun merged -> (instructions @ childInstructions, merged)))
                (Ok(check, Map.empty))
            |> Result.map (fun (instructions, bindings) ->
                if children.IsEmpty then
                    let compoundLabel = freshLabel ()
                    let doneLabel = freshLabel ()

                    ([
                        PushSlot subjectSlot
                        IsText
                        JumpIfFalse compoundLabel
                        PushSlot subjectSlot
                        LiteralText name
                        TextEquals
                        JumpIfFalse failureLabel
                        Jump doneLabel
                        Label compoundLabel
                     ]
                     @ instructions
                     @ [ Label doneLabel ],
                     bindings)
                else
                    (instructions, bindings))

    let rec lower
        (environment: Map<string, Slot>)
        (node: Expression)
        : Result<LoweredUnit list * Instruction list, LoweringRefusal> =
        match node with
        | Literal(ExpressionLiteral.NumberLiteral value) -> Ok([], [ LiteralNumber value ])
        | Literal(ExpressionLiteral.TextLiteral value) -> Ok([], [ LiteralText value ])
        | Literal(ExpressionLiteral.AtomLiteral value) -> Ok([], [ LiteralText value ])
        | Word "apply" -> Ok([], [ Apply ])
        | Word name ->
            match environment.TryFind name with
            | Some slot -> Ok([], [ PushSlot slot ])
            | None -> Ok([], [ CallWord(name, None) ])
        | Call(Word name, arguments) ->
            match environment.TryFind name with
            | Some slot ->
                lowerAll environment arguments
                |> Result.map (fun (units, instructions) -> (units, instructions @ [ PushSlot slot; CallClosure ]))
            | None when HostWords.isShellWord name ->
                lowerAll environment arguments
                |> Result.map (fun (units, instructions) ->
                    (units, instructions @ [ CallWord(HostWords.encode name arguments.Length, None) ]))
            | None when HostWords.isBookWord name ->
                lowerAll environment arguments
                |> Result.map (fun (units, instructions) ->
                    (units, instructions @ [ CallWord(HostWords.encodeBook name arguments.Length, None) ]))
            | None ->
                lowerAll environment arguments
                |> Result.map (fun (units, instructions) -> (units, instructions @ [ CallWord(name, None) ]))
        | Call((Lambda _ as target), arguments) ->
            lowerAll environment arguments
            |> Result.bind (fun (argumentUnits, argumentInstructions) ->
                lower environment target
                |> Result.map (fun (targetUnits, targetInstructions) ->
                    (argumentUnits @ targetUnits, argumentInstructions @ targetInstructions @ [ CallClosure ])))
        | Call(target, _) -> Error(CallTargetNotWord(nodeOf target))
        | Hole -> Error(NodeNotLowered HoleNode)
        | Partial(target, arguments) ->
            let holes =
                arguments
                |> List.indexed
                |> List.choose (fun (index, argument) ->
                    match argument with
                    | Hole -> Some index
                    | _ -> None)

            if holes.IsEmpty then
                lower environment (Call(target, arguments))
            else
                let holeNames =
                    holes
                    |> List.mapi (fun index argumentIndex -> argumentIndex, "__partialHole$" + string (freshUnit ()))
                    |> Map.ofList

                let argumentNames =
                    arguments
                    |> List.mapi (fun index _ -> index, "__partialArg$" + string (freshUnit ()))
                    |> Map.ofList

                let finalArguments =
                    arguments
                    |> List.mapi (fun index argument ->
                        match argument with
                        | Hole -> ExpressionLogic.Word holeNames.[index]
                        | _ -> ExpressionLogic.Word argumentNames.[index])

                let partialLambda =
                    ExpressionLogic.Lambda(
                        holes |> List.map (fun index -> holeNames.[index]),
                        ExpressionLogic.Call(target, finalArguments)
                    )

                let supplied =
                    arguments
                    |> List.indexed
                    |> List.choose (fun (index, argument) ->
                        match argument with
                        | Hole -> None
                        | _ -> Some(argumentNames.[index], argument))

                let wrapped =
                    List.foldBack
                        (fun (name, value) body -> ExpressionLogic.Let(name, value, body))
                        supplied
                        partialLambda

                lower environment wrapped
        | Pipeline(value, stage, style) ->
            lower environment (ExpressionLogic.desugarPipeline (Pipeline(value, stage, style)))
        | ReversePipeline(target, value) ->
            lower environment (ExpressionLogic.desugarPipeline (ReversePipeline(target, value)))
        | Block body
        | Quotation body -> lowerSequence recordStatements environment body
        | If(condition, thenBranch, elseBranch) ->
            let elseLabel = freshLabel ()
            let endLabel = freshLabel ()

            lower environment condition
            |> Result.bind (fun (conditionUnits, loweredCondition) ->
                lower environment thenBranch
                |> Result.bind (fun (thenUnits, loweredThen) ->
                    lower environment elseBranch
                    |> Result.map (fun (elseUnits, loweredElse) ->
                        (conditionUnits @ thenUnits @ elseUnits,
                         loweredCondition
                         @ [ JumpIfFalse elseLabel ]
                         @ loweredThen
                         @ [ Jump endLabel; Label elseLabel ]
                         @ loweredElse
                         @ [ Label endLabel ]))))
        | Let(name, value, body) ->
            if EvaluatorCommon.isReservedToken name then
                Error(ReservedBindingName name)
            else
                let slot = freshSlot ()

                lower environment value
                |> Result.bind (fun (valueUnits, loweredValue) ->
                    lower (environment.Add(name, slot)) body
                    |> Result.map (fun (bodyUnits, loweredBody) ->
                        (valueUnits @ bodyUnits,
                         loweredValue
                         @ [ Duplicate; LiteralText name; CallWord("let", None); StoreSlot slot ]
                         @ loweredBody)))
        | Lambda(parameters, body) ->
            let duplicate =
                parameters |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1)

            match duplicate with
            | Some(name, _) -> Error(DuplicateParameter name)
            | None ->
                match parameters |> List.tryFind EvaluatorCommon.isReservedToken with
                | Some name -> Error(ReservedBindingName name)
                | None ->
                    let arity = List.length parameters

                    let captured =
                        let used = usedNames (Set.ofList parameters) body

                        environment
                        |> Map.toList
                        |> List.filter (fun (name, _) -> used.Contains name)
                        |> List.sortBy fst

                    let parameterBindings = parameters |> List.map (fun name -> (name, freshSlot ()))

                    let captureBindings =
                        captured |> List.map (fun (name, outerSlot) -> (name, outerSlot, freshSlot ()))

                    let unitEnvironment =
                        let capturedEnv =
                            captureBindings
                            |> List.fold
                                (fun (env: Map<string, Slot>) (name, _, slot) -> env.Add(name, slot))
                                (Map.empty: Map<string, Slot>)

                        parameterBindings
                        |> List.fold (fun (env: Map<string, Slot>) (name, slot) -> env.Add(name, slot)) capturedEnv

                    let parameterPrologue =
                        parameterBindings
                        |> List.mapi (fun index (_, slot) -> [ PushCapture index; StoreSlot slot ])
                        |> List.concat

                    let capturePrologue =
                        captureBindings
                        |> List.mapi (fun index (_, _, slot) -> [ PushCapture(arity + index); StoreSlot slot ])
                        |> List.concat

                    let unitName = "$lambda" + string (freshUnit ())

                    lower unitEnvironment body
                    |> Result.map (fun (bodyUnits, bodyInstructions) ->
                        let unit =
                            {
                                Name = unitName
                                Arity = arity
                                Instructions = parameterPrologue @ capturePrologue @ bodyInstructions
                            }

                        (bodyUnits @ [ unit ], [ MakeClosure(unitName, captured |> List.map snd, arity) ]))
        | PatternMatch(subject, clauses) ->
            let subjectSlot = freshSlot ()
            let endLabel = freshLabel ()

            lower environment subject
            |> Result.bind (fun (subjectUnits, subjectInstructions) ->
                let rec lowerClauses
                    (remaining: ExpressionClause list)
                    (units: LoweredUnit list)
                    (instructions: Instruction list)
                    : Result<LoweredUnit list * Instruction list, LoweringRefusal> =
                    match remaining with
                    | [] -> Ok(units, instructions @ [ Fail "pattern-match: no clause matched"; Label endLabel ])
                    | clause :: rest ->
                        match clause.Guard with
                        | Some guard -> Error(PatternGuardNotLowered guard.Predicate)
                        | None ->
                            let failureLabel = freshLabel ()
                            let scratchSlot = freshSlot ()

                            lowerPattern failureLabel scratchSlot clause.Pattern
                            |> Result.bind (fun (patternInstructions, bindings) ->
                                let bodyEnvironment =
                                    bindings
                                    |> Map.fold
                                        (fun (env: Map<string, Slot>) name slot -> env.Add(name, slot))
                                        environment

                                lowerAll bodyEnvironment clause.Body
                                |> Result.bind (fun (bodyUnits, bodyInstructions) ->
                                    let clauseInstructions =
                                        [ PushSlot subjectSlot; StoreSlot scratchSlot ]
                                        @ patternInstructions
                                        @ bodyInstructions
                                        @ [ Jump endLabel; Label failureLabel ]

                                    lowerClauses rest (units @ bodyUnits) (instructions @ clauseInstructions)))

                lowerClauses clauses [] (subjectInstructions @ [ StoreSlot subjectSlot ])
                |> Result.map (fun (units, instructions) -> (subjectUnits @ units, instructions)))
        | DeclareKind _ -> Error(NodeNotLowered DeclareKindNode)
        | DeclarePredicate _ -> Error(NodeNotLowered DeclarePredicateNode)
        | Condition _ -> Error(NodeNotLowered ConditionNode)
        | Restart _ -> Error(NodeNotLowered RestartNode)
        | Generic _ -> Error(NodeNotLowered GenericNode)
        | Message(kind, []) -> Ok([], [ LiteralText kind ])
        | Message(kind, fields) ->
            let slots = fields |> List.map (fun _ -> freshSlot ())

            let rec storeFields
                (remaining: (Expression * int) list)
                (units: LoweredUnit list)
                (instructions: Instruction list)
                : Result<LoweredUnit list * Instruction list, LoweringRefusal> =
                match remaining with
                | [] -> Ok(units, instructions)
                | (field, slot) :: rest ->
                    lower environment field
                    |> Result.bind (fun (fieldUnits, fieldInstructions) ->
                        storeFields rest (units @ fieldUnits) (instructions @ fieldInstructions @ [ StoreSlot slot ]))

            storeFields (List.zip fields slots) [] []
            |> Result.map (fun (units, instructions) ->
                (units,
                 instructions
                 @ [ LiteralText kind ]
                 @ (slots |> List.rev |> List.map PushSlot)
                 @ [ LiteralNumber(float fields.Length); CallWord("term", None) ]))
        | ExpressionLogic.Fail reason -> Ok([], [ LoweredInstructions.Fail reason ])

    and lowerAll
        (environment: Map<string, Slot>)
        (nodes: Expression list)
        : Result<LoweredUnit list * Instruction list, LoweringRefusal> =
        lowerSequence false environment nodes

    // D201: a statement boundary is the only resume point a position-only record
    // can describe. An emitter that asks for history records one at each
    // statement start, before that statement's own instructions and never
    // between them, with the value being the instruction index in this
    // sequence's output. A single-statement sequence records nothing because
    // there is no later statement to resume to. Argument lists, message fields
    // and pattern clause bodies are not statement sequences: a record between
    // operands would be a resume point with a half-built operand stack, which
    // the kernel cannot restore.
    and lowerSequence
        (recordBoundaries: bool)
        (environment: Map<string, Slot>)
        (nodes: Expression list)
        : Result<LoweredUnit list * Instruction list, LoweringRefusal> =
        let record = recordBoundaries && List.length nodes > 1

        let rec loop
            (position: int)
            (remaining: Expression list)
            (units: LoweredUnit list)
            (emitted: Instruction list)
            : Result<LoweredUnit list * Instruction list, LoweringRefusal> =
            match remaining with
            | [] -> Ok(units, emitted)
            | node :: rest ->
                lower environment node
                |> Result.bind (fun (nodeUnits, nodeInstructions) ->
                    let boundary =
                        if record then
                            [ LoweredInstructions.RecordStep position ]
                        else
                            []

                    loop
                        (position + List.length boundary + List.length nodeInstructions)
                        rest
                        (units @ nodeUnits)
                        (emitted @ boundary @ nodeInstructions))

        loop 0 nodes [] []

    lower Map.empty expression
    |> Result.map (fun (units, instructions) ->
        {
            Units = units
            Instructions = instructions
        })

/// The emitter's default: no step record, so the program stays
/// `HistoryExcluded` and an emitted artifact is unchanged by the history work.
let lowerExpression (sourceExpression: Expression) : Result<LoweredProgram, LoweringRefusal> =
    lowerExpressionWith false sourceExpression

/// The history-asking emitter: one step record at the start of each statement of
/// a multi-statement sequence, so a caller can resume at a statement boundary
/// with the state it supplies (D201).
let lowerExpressionWithStepRecords
    (sourceExpression: Expression)
    : Result<LoweredProgram, LoweringRefusal> =
    lowerExpressionWith true sourceExpression

let lowerExpressionWithInference
    (signatures: TypeRetrieval.SignatureTable)
    (sourceMap: ExpressionSourceMap)
    (expression: Expression)
    : Result<LoweredProgram, LoweringWithInferenceFailure> =
    match TypeRetrieval.inferExpressionLocated signatures sourceMap expression with
    | Error diagnostic -> Error(InferenceFailed diagnostic)
    | Ok _ -> lowerExpression expression |> Result.mapError LoweringFailed
