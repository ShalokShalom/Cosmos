module QuestionEngine

// ============================================================
// First-order Question evaluator. Facts and rules are tried in source order;
// goals run left to right. The engine is pure: it returns bindings and limits,
// never host effects.
// ============================================================

open PatternLogic
open QuestionLogic

type QuestionError =
    | NoQuestion
    | StepLimitExceeded of limit: int
    | UnsupportedVariableRole of name: string

type QuestionAnswer = { Bindings: Map<string, Pattern> }

type QuestionResult =
    {
        Answers: QuestionAnswer list
        Steps: int
        Truncated: bool
    }

type private Substitution = Map<string, Pattern>

let describeError (error: QuestionError) : string =
    match error with
    | NoQuestion -> "the question program has no question"
    | StepLimitExceeded limit -> "the question step limit was exceeded: " + string limit
    | UnsupportedVariableRole name -> "the question variable role is not supported: " + name

let rec private walk (substitution: Substitution) (term: Pattern) : Pattern =
    match term with
    | Variable(name, Fresh) ->
        match Map.tryFind name substitution with
        | Some bound -> walk substitution bound
        | None -> term
    | _ -> term

let rec private occurs (name: string) (substitution: Substitution) (term: Pattern) : bool =
    match walk substitution term with
    | Variable(other, _) -> name = other
    | Constructor(_, arguments) -> arguments |> List.exists (occurs name substitution)
    | TypedPattern(_, child) -> occurs name substitution child
    | Wildcard
    | NumberLiteral _
    | TextLiteral _
    | AtomLiteral _ -> false

let private bind
    (name: string)
    (value: Pattern)
    (substitution: Substitution)
    : Result<Substitution option, QuestionError> =
    let value = walk substitution value

    if occurs name substitution value then
        Ok None
    else
        Ok(Some(Map.add name value substitution))

let rec private unify
    (substitution: Substitution)
    (left: Pattern)
    (right: Pattern)
    : Result<Substitution option, QuestionError> =
    let left = walk substitution left
    let right = walk substitution right

    if left = right then
        Ok(Some substitution)
    else
        match left, right with
        | Variable(name, Fresh), other
        | other, Variable(name, Fresh) -> bind name other substitution
        | Variable(name, _), _
        | _, Variable(name, _) -> Error(UnsupportedVariableRole name)
        | Wildcard, _
        | _, Wildcard -> Ok(Some substitution)
        | NumberLiteral leftValue, NumberLiteral rightValue when leftValue = rightValue ->
            Ok(Some substitution)
        | TextLiteral leftValue, TextLiteral rightValue when leftValue = rightValue ->
            Ok(Some substitution)
        | AtomLiteral leftName, AtomLiteral rightName when leftName = rightName ->
            Ok(Some substitution)
        | Constructor(leftName, leftArguments), Constructor(rightName, rightArguments) when
            leftName = rightName && List.length leftArguments = List.length rightArguments
            ->
            List.zip leftArguments rightArguments
            |> List.fold
                (fun state (leftArgument, rightArgument) ->
                    match state with
                    | Error error -> Error error
                    | Ok None -> Ok None
                    | Ok(Some current) -> unify current leftArgument rightArgument)
                (Ok(Some substitution))
        | TypedPattern(leftSort, leftChild), TypedPattern(rightSort, rightChild) when leftSort = rightSort ->
            unify substitution leftChild rightChild
        | _ -> Ok None

let rec private variableNames (term: Pattern) : Set<string> =
    match term with
    | Variable(name, _) -> Set.singleton name
    | Constructor(_, arguments) ->
        arguments
        |> List.fold (fun names argument -> Set.union names (variableNames argument)) Set.empty
    | TypedPattern(_, child) -> variableNames child
    | Wildcard
    | NumberLiteral _
    | TextLiteral _
    | AtomLiteral _ -> Set.empty

let private goalVariables (goals: Pattern list) : string list =
    goals
    |> List.fold (fun names goal -> Set.union names (variableNames goal)) Set.empty
    |> Set.toList

let rec private freshenTerm (names: Map<string, string>) (term: Pattern) : Pattern =
    match term with
    | Variable(name, Fresh) -> Variable(Map.find name names, Fresh)
    | Variable(name, role) -> Variable(Map.find name names, role)
    | Constructor(name, arguments) -> Constructor(name, arguments |> List.map (freshenTerm names))
    | TypedPattern(sortName, child) -> TypedPattern(sortName, freshenTerm names child)
    | Wildcard
    | NumberLiteral _
    | TextLiteral _
    | AtomLiteral _ -> term

let private freshenClause (suffix: int) (clause: QuestionClause) : QuestionClause =
    let names =
        (variableNames clause.Head
         :: (clause.Body |> List.map variableNames))
        |> List.fold Set.union Set.empty

    let renamed =
        names
        |> Set.toList
        |> List.map (fun name -> name, name + "#" + string suffix)
        |> Map.ofList

    {
        Head = freshenTerm renamed clause.Head
        Body = clause.Body |> List.map (freshenTerm renamed)
    }

let solve (maxSteps: int) (maxAnswers: int) (program: QuestionProgram) : Result<QuestionResult, QuestionError> =
    match program.Question with
    | None -> Error NoQuestion
    | Some goals ->
        let steps = ref 0
        let fresh = ref 0
        let answers = ResizeArray<QuestionAnswer>()
        let truncated = ref false
        let abort = ref None
        let queryVariables = goalVariables goals

        let addAnswer (substitution: Substitution) : unit =
            if answers.Count >= maxAnswers then
                truncated.Value <- true
            else
                let bindings =
                    queryVariables
                    |> List.choose (fun name ->
                        match walk substitution (Variable(name, Fresh)) with
                        | Variable(sameName, Fresh) when sameName = name -> None
                        | value -> Some(name, value))
                    |> Map.ofList

                answers.Add { Bindings = bindings }

        let rec solveGoals (substitution: Substitution) (goals: Pattern list) : unit =
            if abort.Value.IsSome || truncated.Value then
                ()
            elif steps.Value >= maxSteps then
                abort.Value <- Some(StepLimitExceeded maxSteps)
            else
                match goals with
                | [] -> addAnswer substitution
                | goal :: rest ->
                    let rec tryClauses (clauses: QuestionClause list) : unit =
                        if abort.Value.IsSome || truncated.Value then
                            ()
                        else
                            match clauses with
                            | [] -> ()
                            | clause :: remaining ->
                                steps.Value <- steps.Value + 1
                                let invocation = freshenClause fresh.Value clause
                                fresh.Value <- fresh.Value + 1

                                match unify substitution goal invocation.Head with
                                | Error error -> abort.Value <- Some error
                                | Ok None -> tryClauses remaining
                                | Ok(Some next) ->
                                    solveGoals next (invocation.Body @ rest)
                                    tryClauses remaining

                    tryClauses program.Clauses

        if maxSteps < 1 then
            Error(StepLimitExceeded maxSteps)
        elif maxAnswers < 1 then
            Ok
                {
                    Answers = []
                    Steps = 0
                    Truncated = true
                }
        else
            solveGoals Map.empty goals

            match abort.Value with
            | Some error -> Error error
            | None ->
                Ok
                    {
                        Answers = List.ofSeq answers
                        Steps = steps.Value
                        Truncated = truncated.Value
                    }
