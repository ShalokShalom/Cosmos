module TypeRetrieval

open ExpressionLogic

type TypeVariable = int
type Effect = string
type EffectSet = LabelSets.LabelSet<Effect>

let emptyEffects: EffectSet = LabelSets.Closed Set.empty

let closedEffects (effects: Set<Effect>) : EffectSet = LabelSets.Closed effects

let effectSet (effect: Effect) : EffectSet = closedEffects (Set.singleton effect)

type Type =
    | Variable of TypeVariable
    | Primitive of string
    | Sort of string
    | Record of row: RowTypes.Row<RecordTypes.RecordField<Type>>
    | Function of parameters: Type list * result: Type * effects: EffectSet
    | Code of result: Type * effects: EffectSet

type RecordField = RecordTypes.RecordField<Type>

type Scheme =
    {
        Quantified: TypeVariable list
        QuantifiedEffects: LabelSets.SetVariable list
        QuantifiedRows: RowTypes.RowVariable list
        EffectLacks: LabelSets.SetLacks<Effect>
        RowLacks: RowTypes.RowLacks<RecordField>
        Type: Type
    }

type Constraint =
    | EqualType of Type * Type
    | EqualEffects of EffectSet * EffectSet * context: string

type InferenceError =
    | OccursCheck of variable: TypeVariable * value: Type
    | TypeMismatch of expected: Type * actual: Type * context: string
    | ArityMismatch of expected: int * actual: int * context: string
    | EffectMismatch of expected: EffectSet * actual: EffectSet * context: string
    | EffectLacksViolation of variable: LabelSets.SetVariable * labels: string
    | EffectOccursCheck of variable: LabelSets.SetVariable * value: EffectSet
    | EffectSubtractionOpen of value: EffectSet
    | RowOccursCheck of variable: RowTypes.RowVariable * value: string
    | RowLacksViolation of variable: RowTypes.RowVariable * labels: string
    | DuplicateLabel of label: string
    | UnknownField of name: string
    | UnsolvedConstraints of Constraint list
    | UnknownWord of name: string
    | AmbiguousWord of name: string
    | UnsupportedExpression of form: string

type Substitution = Map<TypeVariable, Type>

let emptySubstitution: Substitution = Map.empty

let rec applyTypeWith
    (substitution: Substitution)
    (effectSubstitution: LabelSets.SetSubstitution<Effect>)
    (rowSubstitution: RowTypes.RowSubstitution<RecordField>)
    (typeExpression: Type)
    : Type =
    match typeExpression with
    | Variable variable ->
        match Map.tryFind variable substitution with
        | Some replacement ->
            applyTypeWith substitution effectSubstitution rowSubstitution replacement
        | None -> typeExpression
    | Primitive _
    | Sort _ -> typeExpression
    | Record row ->
        let rec applyRow (current: RowTypes.Row<RecordField>) : RowTypes.Row<RecordField> =
            match current with
            | RowTypes.Empty -> RowTypes.Empty
            | RowTypes.Extend(field, rest) ->
                RowTypes.Extend(
                    {
                        field with
                            Type =
                                applyTypeWith
                                    substitution
                                    effectSubstitution
                                    rowSubstitution
                                    field.Type
                    },
                    applyRow rest
                )
            | RowTypes.Variable variable ->
                match Map.tryFind variable rowSubstitution with
                | Some replacement -> applyRow replacement
                | None -> RowTypes.Variable variable

        Record(applyRow (RowTypes.apply rowSubstitution row))
    | Function(parameters, result, effects) ->
        Function(
            parameters
            |> List.map (applyTypeWith substitution effectSubstitution rowSubstitution),
            applyTypeWith substitution effectSubstitution rowSubstitution result,
            LabelSets.apply effectSubstitution effects
        )
    | Code(result, effects) ->
        Code(
            applyTypeWith substitution effectSubstitution rowSubstitution result,
            LabelSets.apply effectSubstitution effects
        )

let applyType (substitution: Substitution) (typeExpression: Type) : Type =
    applyTypeWith substitution Map.empty Map.empty typeExpression

let recordFields (row: RowTypes.Row<RecordField>) : RecordField list =
    RecordTypes.fields row

let recordTail (row: RowTypes.Row<RecordField>) : RowTypes.Row<RecordField> =
    RecordTypes.tail row

let rec occurs (variable: TypeVariable) (typeExpression: Type) : bool =
    match typeExpression with
    | Variable other -> variable = other
    | Primitive _
    | Sort _ -> false
    | Record row ->
        recordFields row |> List.exists (fun field -> occurs variable field.Type)
    | Function(parameters, result, _) -> List.exists (occurs variable) parameters || occurs variable result
    | Code(result, _) -> occurs variable result

let rec describeType (typeExpression: Type) : string =
    match typeExpression with
    | Variable variable -> "t" + string variable
    | Primitive name -> name
    | Sort name -> "sort " + name
    | Record row ->
        let fieldText =
            recordFields row
            |> List.map (fun field -> field.Name + ": " + describeType field.Type)
            |> String.concat ", "

        match recordTail row with
        | RowTypes.Empty -> "{" + fieldText + "}"
        | RowTypes.Variable(RowTypes.RowVariable variable) ->
            "{" + fieldText + (if fieldText = "" then "" else " | ") + "r" + string variable + "}"
        | RowTypes.Extend _ -> "{" + fieldText + "}"
    | Function(parameters, result, effects) ->
        let parameterText = parameters |> List.map describeType |> String.concat ", "
        let effectText = LabelSets.describe id effects

        if effectText = "{}" then
            "(" + parameterText + ") -> " + describeType result
        else
            "(" + parameterText + ") -> " + describeType result + " ! " + effectText
    | Code(result, effects) ->
        let effectText = LabelSets.describe id effects

        if effectText = "{}" then
            "code " + describeType result
        else
            "code " + describeType result + " ! " + effectText

let describeError (error: InferenceError) : string =
    match error with
    | OccursCheck(variable, value) -> "occurs check: t" + string variable + " appears in " + describeType value
    | TypeMismatch(expected, actual, context) ->
        context
        + ": expected "
        + describeType expected
        + ", found "
        + describeType actual
    | ArityMismatch(expected, actual, context) ->
        context
        + ": expected "
        + string expected
        + " parameters, found "
        + string actual
    | EffectMismatch(expected, actual, context) ->
        context
        + ": expected effects "
        + LabelSets.describe id expected
        + ", found "
        + LabelSets.describe id actual
    | EffectLacksViolation(variable, labels) ->
        let (LabelSets.SetVariable identifier) = variable

        "effect set s" + string identifier + " lacks " + labels
    | EffectOccursCheck(variable, value) ->
        let (LabelSets.SetVariable identifier) = variable

        "effect occurs check: s"
        + string identifier
        + " appears in "
        + LabelSets.describe id value
    | EffectSubtractionOpen value ->
        "effect subtraction from an open set needs a lacks constraint: "
        + LabelSets.describe id value
    | RowOccursCheck(variable, value) ->
        let (RowTypes.RowVariable identifier) = variable
        "record row occurs check: r" + string identifier + " appears in " + value
    | RowLacksViolation(variable, labels) ->
        let (RowTypes.RowVariable identifier) = variable
        "record row r" + string identifier + " lacks " + labels
    | DuplicateLabel label -> "duplicate record field: " + label
    | UnknownField name -> "unknown record field: " + name
    | UnsolvedConstraints constraints -> "unsolved constraints: " + string (List.length constraints)
    | UnknownWord name -> "unknown word: " + name
    | AmbiguousWord name -> "ambiguous word signature: " + name
    | UnsupportedExpression form -> "type inference does not support " + form

let rec freeTypeVariables (typeExpression: Type) : Set<TypeVariable> =
    match typeExpression with
    | Variable variable -> Set.singleton variable
    | Primitive _
    | Sort _ -> Set.empty
    | Record row ->
        recordFields row
        |> List.map (fun field -> freeTypeVariables field.Type)
        |> Set.unionMany
    | Function(parameters, result, _) ->
        let fromParameters = parameters |> List.map freeTypeVariables |> Set.unionMany
        Set.union fromParameters (freeTypeVariables result)
    | Code(result, _) -> freeTypeVariables result

let rec freeEffectVariables (typeExpression: Type) : Set<LabelSets.SetVariable> =
    match typeExpression with
    | Variable _
    | Primitive _
    | Sort _ -> Set.empty
    | Record row ->
        recordFields row
        |> List.map (fun field -> freeEffectVariables field.Type)
        |> Set.unionMany
    | Function(parameters, result, effects) ->
        Set.unionMany
            [
                parameters |> List.map freeEffectVariables |> Set.unionMany
                freeEffectVariables result
                LabelSets.freeVariables effects
            ]
    | Code(result, effects) ->
        Set.union (freeEffectVariables result) (LabelSets.freeVariables effects)

let rec freeRowVariables (typeExpression: Type) : Set<RowTypes.RowVariable> =
    match typeExpression with
    | Variable _
    | Primitive _
    | Sort _ -> Set.empty
    | Record row ->
        Set.union
            (RowTypes.freeVariables row)
            (recordFields row
             |> List.map (fun field -> freeRowVariables field.Type)
             |> Set.unionMany)
    | Function(parameters, result, _) ->
        let fromParameters = parameters |> List.map freeRowVariables |> Set.unionMany
        Set.union fromParameters (freeRowVariables result)
    | Code(result, _) -> freeRowVariables result

let freeSchemeVariables (scheme: Scheme) : Set<TypeVariable> =
    Set.difference (freeTypeVariables scheme.Type) (Set.ofList scheme.Quantified)

let freeSchemeEffectVariables (scheme: Scheme) : Set<LabelSets.SetVariable> =
    Set.difference
        (freeEffectVariables scheme.Type)
        (Set.ofList scheme.QuantifiedEffects)

let freeSchemeRowVariables (scheme: Scheme) : Set<RowTypes.RowVariable> =
    Set.difference
        (freeRowVariables scheme.Type)
        (Set.ofList scheme.QuantifiedRows)

let generalizeWithLacks
    (environmentVariables: Set<TypeVariable>)
    (environmentEffects: Set<LabelSets.SetVariable>)
    (environmentRows: Set<RowTypes.RowVariable>)
    (effectLacks: LabelSets.SetLacks<Effect>)
    (rowLacks: RowTypes.RowLacks<RecordField>)
    (typeExpression: Type)
    : Scheme =
    let quantifiedEffects =
        Set.difference (freeEffectVariables typeExpression) environmentEffects
        |> Set.toList

    let quantifiedRows =
        Set.difference (freeRowVariables typeExpression) environmentRows
        |> Set.toList

    let quantifiedEffectSet = Set.ofList quantifiedEffects
    let quantifiedRowSet = Set.ofList quantifiedRows

    {
        Quantified =
            Set.difference (freeTypeVariables typeExpression) environmentVariables
            |> Set.toList
        QuantifiedEffects = quantifiedEffects
        QuantifiedRows = quantifiedRows
        EffectLacks =
            effectLacks
            |> Map.filter (fun variable _ -> quantifiedEffectSet.Contains variable)
        RowLacks =
            rowLacks
            |> Map.filter (fun variable _ -> quantifiedRowSet.Contains variable)
        Type = typeExpression
    }

let generalizeWith
    (environmentVariables: Set<TypeVariable>)
    (environmentEffects: Set<LabelSets.SetVariable>)
    (environmentRows: Set<RowTypes.RowVariable>)
    (typeExpression: Type)
    : Scheme =
    generalizeWithLacks
        environmentVariables
        environmentEffects
        environmentRows
        LabelSets.emptyLacks
        RowTypes.emptyLacks
        typeExpression

let generalize (environmentVariables: Set<TypeVariable>) (typeExpression: Type) : Scheme =
    generalizeWith environmentVariables Set.empty Set.empty typeExpression

let instantiateWithLacks
    (nextVariable: int)
    (scheme: Scheme)
    : Type * LabelSets.SetLacks<Effect> * RowTypes.RowLacks<RecordField> * int =
    let highestQuantified =
        List.append
            scheme.Quantified
            (List.append
                (scheme.QuantifiedEffects
                 |> List.map (fun (LabelSets.SetVariable identifier) -> identifier))
                (scheme.QuantifiedRows
                 |> List.map (fun (RowTypes.RowVariable identifier) -> identifier)))
        |> List.fold max -1

    let nextVariable = max nextVariable (highestQuantified + 1)

    let typeSubstitution, nextVariable =
        scheme.Quantified
        |> List.fold
            (fun (substitution, current) variable ->
                Map.add variable (Variable current) substitution, current + 1)
            (Map.empty, nextVariable)

    let effectSubstitution, nextVariable =
        scheme.QuantifiedEffects
        |> List.fold
            (fun (substitution, current) variable ->
                Map.add
                    variable
                    (LabelSets.Open(Set.empty, LabelSets.SetVariable current))
                    substitution,
                current + 1)
            (Map.empty, nextVariable)

    let rowSubstitution, nextVariable =
        scheme.QuantifiedRows
        |> List.fold
            (fun (substitution, current) (RowTypes.RowVariable identifier) ->
                Map.add
                    (RowTypes.RowVariable identifier)
                    (RowTypes.Variable(RowTypes.RowVariable current))
                    substitution,
                current + 1)
            (Map.empty, nextVariable)

    let effectLacks =
        scheme.EffectLacks
        |> Map.fold
            (fun lacks variable labels ->
                match Map.tryFind variable effectSubstitution with
                | Some(LabelSets.Open(_, fresh)) -> Map.add fresh labels lacks
                | _ -> lacks)
            LabelSets.emptyLacks

    let rowLacks =
        scheme.RowLacks
        |> Map.fold
            (fun lacks variable labels ->
                match Map.tryFind variable rowSubstitution with
                | Some(RowTypes.Variable fresh) -> Map.add fresh labels lacks
                | _ -> lacks)
            RowTypes.emptyLacks

    let instantiated =
        applyTypeWith
            typeSubstitution
            effectSubstitution
            rowSubstitution
            scheme.Type

    instantiated, effectLacks, rowLacks, nextVariable

let instantiate (nextVariable: int) (scheme: Scheme) : Type * int =
    let instantiated, _, _, next = instantiateWithLacks nextVariable scheme
    instantiated, next

type Unification =
    {
        Types: Substitution
        Effects: LabelSets.SetSubstitution<Effect>
        EffectLacks: LabelSets.SetLacks<Effect>
        RecordRows: RowTypes.RowSubstitution<RecordField>
        RecordLacks: RowTypes.RowLacks<RecordField>
        NextEffect: int
        NextRow: int
    }

let emptyUnification: Unification =
    {
        Types = emptySubstitution
        Effects = Map.empty
        EffectLacks = LabelSets.emptyLacks
        RecordRows = Map.empty
        RecordLacks = Map.empty
        NextEffect = 0
        NextRow = 0
    }

let subtractEffect
    (effect: string)
    (effects: EffectSet)
    : Result<EffectSet, InferenceError> =
    match LabelSets.subtractClosed (Set.singleton effect) effects with
    | Ok result -> Ok result
    | Error _ -> Error(EffectSubtractionOpen effects)

let subtractEffectWithLacks
    (effect: string)
    (effects: EffectSet)
    (lacks: LabelSets.SetLacks<Effect>)
    : Result<EffectSet * LabelSets.SetLacks<Effect>, InferenceError> =
    match LabelSets.subtractOpen (Set.singleton effect) effects lacks with
    | Ok result -> Ok result
    | Error _ -> Error(EffectSubtractionOpen effects)

let effectSubset (left: EffectSet) (right: EffectSet) : bool =
    match left, right with
    | LabelSets.Closed leftLabels, LabelSets.Closed rightLabels ->
        Set.isSubset leftLabels rightLabels
    | LabelSets.Closed leftLabels, LabelSets.Open(rightLabels, _) ->
        Set.isSubset leftLabels rightLabels
    | LabelSets.Open _, LabelSets.Closed _ -> false
    | LabelSets.Open(leftLabels, leftVariable), LabelSets.Open(rightLabels, rightVariable) ->
        leftVariable = rightVariable && Set.isSubset leftLabels rightLabels

/// Effect rows are represented by open label sets. This alias makes the row
/// reading explicit without introducing a second effect representation.
type EffectRow = LabelSets.LabelSet<Effect>

let effectRowClosed (labels: Set<Effect>) : EffectRow = LabelSets.Closed labels

let effectRowOpen (known: Set<Effect>) (variable: LabelSets.SetVariable) : EffectRow =
    LabelSets.Open(known, variable)

let effectRowExtend (label: Effect) (row: EffectRow) : EffectRow = LabelSets.extend label row

let effectRowSubtract
    (label: Effect)
    (row: EffectRow)
    (lacks: LabelSets.SetLacks<Effect>)
    : Result<EffectRow * LabelSets.SetLacks<Effect>, InferenceError> =
    subtractEffectWithLacks label row lacks

/// A checked subtyping relation for the closed fragment. It is not yet used by
/// application; unification remains the operational rule.
let rec subtype (left: Type) (right: Type) : bool =
    match left, right with
    | Variable leftVariable, Variable rightVariable -> leftVariable = rightVariable
    | Primitive leftName, Primitive rightName -> leftName = rightName
    | Sort leftName, Sort rightName -> leftName = rightName
    | Record leftRow, Record rightRow ->
        let leftFields = recordFields leftRow |> List.map (fun field -> field.Name, field) |> Map.ofList

        recordFields rightRow
        |> List.forall (fun rightField ->
            match Map.tryFind rightField.Name leftFields with
            | Some leftField -> subtype leftField.Type rightField.Type
            | None -> false)
    | Function(leftParameters, leftResult, leftEffects),
      Function(rightParameters, rightResult, rightEffects) ->
        List.length leftParameters = List.length rightParameters
        && List.forall2 (fun leftParameter rightParameter ->
            subtype rightParameter leftParameter) leftParameters rightParameters
        && subtype leftResult rightResult
        && effectSubset leftEffects rightEffects
    | Code(leftResult, leftEffects), Code(rightResult, rightEffects) ->
        subtype leftResult rightResult && effectSubset leftEffects rightEffects
    | _ -> false

let unifyEffectSets
    (context: string)
    (left: EffectSet)
    (right: EffectSet)
    (state: Unification)
    : Result<Unification, InferenceError> =
    match LabelSets.unifyWithLacks context left right state.Effects state.EffectLacks state.NextEffect with
    | Ok(effects, lacks, next) ->
        Ok(
            {
                state with
                    Effects = effects
                    EffectLacks = lacks
                    NextEffect = next
            }
        )
    | Error(LabelSets.OccursCheck(variable, set)) -> Error(EffectOccursCheck(variable, set))
    | Error(LabelSets.Mismatch(leftSet, rightSet, mismatchContext)) ->
        Error(EffectMismatch(leftSet, rightSet, mismatchContext))
    | Error(LabelSets.LacksViolation(variable, labels)) ->
        Error(
            EffectLacksViolation(
                variable,
                labels |> Set.toList |> String.concat ", "
            )
        )

let rec unifyMany
    (left: Type list)
    (right: Type list)
    (state: Unification)
    : Result<Unification, InferenceError> =
    match left, right with
    | [], [] -> Ok state
    | leftType :: leftRest, rightType :: rightRest ->
        unifyWith leftType rightType state
        |> Result.bind (fun next -> unifyMany leftRest rightRest next)
    | _ -> Error(ArityMismatch(List.length left, List.length right, "function parameters"))

and unifyWith (left: Type) (right: Type) (state: Unification) : Result<Unification, InferenceError> =
    let left = applyTypeWith state.Types state.Effects state.RecordRows left
    let right = applyTypeWith state.Types state.Effects state.RecordRows right

    match left, right with
    | Variable first, Variable second when first = second -> Ok state
    | Variable variable, other
    | other, Variable variable ->
        if occurs variable other then
            Error(OccursCheck(variable, other))
        else
            Ok(
                {
                    state with
                        Types = Map.add variable other state.Types
                }
            )
    | Primitive first, Primitive second when first = second -> Ok state
    | Sort first, Sort second when first = second -> Ok state
    | Record leftRow, Record rightRow -> unifyRecord leftRow rightRow state
    | Function(leftParameters, leftResult, leftEffects), Function(rightParameters, rightResult, rightEffects) ->
        if List.length leftParameters <> List.length rightParameters then
            Error(ArityMismatch(List.length leftParameters, List.length rightParameters, "function parameters"))
        else
            unifyMany leftParameters rightParameters state
            |> Result.bind (fun next -> unifyWith leftResult rightResult next)
            |> Result.bind (fun next -> unifyEffectSets "function effects" leftEffects rightEffects next)
    | Code(leftResult, leftEffects), Code(rightResult, rightEffects) ->
        unifyWith leftResult rightResult state
        |> Result.bind (fun next -> unifyEffectSets "code effects" leftEffects rightEffects next)
    | _ -> Error(TypeMismatch(left, right, "type equality"))

and unifyRecord
    (leftRow: RowTypes.Row<RecordField>)
    (rightRow: RowTypes.Row<RecordField>)
    (state: Unification)
    : Result<Unification, InferenceError> =
    let rec flatten
        (substitution: RowTypes.RowSubstitution<RecordField>)
        (row: RowTypes.Row<RecordField>)
        : RecordField list * RowTypes.Row<RecordField> =
        let rec loop (current: RowTypes.Row<RecordField>) (accumulated: RecordField list) =
            match current with
            | RowTypes.Empty -> List.rev accumulated, RowTypes.Empty
            | RowTypes.Extend(field, rest) -> loop rest (field :: accumulated)
            | RowTypes.Variable variable ->
                match Map.tryFind variable substitution with
                | Some replacement -> loop replacement accumulated
                | None -> List.rev accumulated, RowTypes.Variable variable

        loop row []

    let leftFields, leftTail = flatten state.RecordRows leftRow
    let rightFields, rightTail = flatten state.RecordRows rightRow

    let bindRecord (current: Unification) variable row =
        match RowTypes.bindRow variable row current.RecordRows current.RecordLacks with
        | Ok(rows, lacks) ->
            Ok
                {
                    current with
                        RecordRows = rows
                        RecordLacks = lacks
                }
        | Error(RowTypes.OccursCheck(variable, _)) ->
            Error(RowOccursCheck(variable, "a record row"))
        | Error(RowTypes.LacksViolation(variable, labels)) ->
            let names =
                labels
                |> Set.toList
                |> List.map (fun field -> field.Name)
                |> String.concat ", "

            Error(RowLacksViolation(variable, names))
        | Error(RowTypes.DuplicateLabel label) -> Error(DuplicateLabel label.Name)
        | Error(RowTypes.Mismatch(_, _, context)) ->
            Error(TypeMismatch(leftRow |> Record, rightRow |> Record, context))

    let duplicate (fields: RecordField list) =
        fields
        |> List.countBy (fun field -> field.Name)
        |> List.tryFind (fun (_, count) -> count > 1)
        |> Option.map fst

    match duplicate leftFields, duplicate rightFields with
    | Some name, _ -> Error(DuplicateLabel name)
    | _, Some name -> Error(DuplicateLabel name)
    | None, None ->
        let rightByName =
            rightFields |> List.map (fun field -> field.Name, field) |> Map.ofList

        let leftByName =
            leftFields |> List.map (fun field -> field.Name, field) |> Map.ofList

        let common =
            leftFields
            |> List.choose (fun leftField ->
                Map.tryFind leftField.Name rightByName
                |> Option.map (fun rightField -> leftField, rightField))

        unifyMany
            (common |> List.map (fun (leftField, _) -> leftField.Type))
            (common |> List.map (fun (_, rightField) -> rightField.Type))
            state
        |> Result.bind (fun state ->
            let onlyLeft =
                leftFields
                |> List.filter (fun field -> not (Map.containsKey field.Name rightByName))

            let onlyRight =
                rightFields
                |> List.filter (fun field -> not (Map.containsKey field.Name leftByName))

            let extendMany fields tail =
                List.foldBack (fun field rest -> RowTypes.Extend(field, rest)) fields tail

            match onlyLeft, onlyRight, leftTail, rightTail with
            | [], [], RowTypes.Empty, RowTypes.Empty -> Ok state
            | [], [], RowTypes.Variable leftVariable, RowTypes.Variable rightVariable ->
                if leftVariable = rightVariable then
                    Ok state
                else
                    bindRecord state leftVariable (RowTypes.Variable rightVariable)
            | [], [], RowTypes.Variable variable, RowTypes.Empty
            | [], [], RowTypes.Empty, RowTypes.Variable variable ->
                bindRecord state variable RowTypes.Empty
            | _, [], _, RowTypes.Variable rightVariable ->
                let absorbed = extendMany onlyLeft leftTail
                bindRecord state rightVariable absorbed
            | [], _, RowTypes.Variable leftVariable, _ ->
                let absorbed = extendMany onlyRight rightTail
                bindRecord state leftVariable absorbed
            | _, _, RowTypes.Variable leftVariable, RowTypes.Variable rightVariable
                when leftVariable <> rightVariable ->
                let fresh = RowTypes.RowVariable state.NextRow
                let leftAbsorbed = extendMany onlyRight (RowTypes.Variable fresh)
                let rightAbsorbed = extendMany onlyLeft (RowTypes.Variable fresh)

                bindRecord state leftVariable leftAbsorbed
                |> Result.bind (fun next ->
                    bindRecord next rightVariable rightAbsorbed
                    |> Result.map (fun final ->
                        {
                            final with
                                NextRow = state.NextRow + 1
                        }))
            | _ -> Error(TypeMismatch(leftRow |> Record, rightRow |> Record, "record fields")))

let unify (left: Type) (right: Type) (substitution: Substitution) : Result<Substitution, InferenceError> =
    unifyWith left right { emptyUnification with Types = substitution }
    |> Result.map (fun state -> state.Types)

let applyConstraint
    (state: Unification)
    (constraintExpression: Constraint)
    : Result<Unification, InferenceError> =
    match constraintExpression with
    | EqualType(left, right) -> unifyWith left right state
    | EqualEffects(expected, actual, context) -> unifyEffectSets context expected actual state

let solveWith (constraints: Constraint list) (state: Unification) : Result<Unification, InferenceError> =
    constraints
    |> List.fold
        (fun result constraintExpression ->
            result
            |> Result.bind (fun current -> applyConstraint current constraintExpression))
        (Ok state)

let solve (constraints: Constraint list) : Result<Substitution, InferenceError> =
    solveWith constraints emptyUnification
    |> Result.map (fun state -> state.Types)

let resolve (substitution: Substitution) (typeExpression: Type) : Type = applyType substitution typeExpression

type WordSignature =
    {
        Quantified: TypeVariable list
        QuantifiedEffects: LabelSets.SetVariable list
        QuantifiedRows: RowTypes.RowVariable list
        Parameters: Type list
        Result: Type
        Effects: EffectSet
    }

type SignatureTable = Map<string, WordSignature list>

type InferenceState =
    {
        NextVariable: int
        Substitution: Substitution
        EffectSubstitution: LabelSets.SetSubstitution<Effect>
        EffectLacks: LabelSets.SetLacks<Effect>
        RecordRows: RowTypes.RowSubstitution<RecordField>
        RecordLacks: RowTypes.RowLacks<RecordField>
        NextRow: int
        Shapes: Map<string, string list>
    }

type Inferred = { Type: Type; Effects: EffectSet }

type InferenceDiagnostic =
    {
        Path: ExpressionPath
        Range: SourceRange option
        Error: InferenceError
        Description: string
    }

let describeDiagnostic (diagnostic: InferenceDiagnostic) : string =
    diagnostic.Description
    + " at path ["
    + (diagnostic.Path |> List.map string |> String.concat ".")
    + "]"

let emptyInferenceState: InferenceState =
    {
        NextVariable = 0
        Substitution = emptySubstitution
        EffectSubstitution = Map.empty
        EffectLacks = LabelSets.emptyLacks
        RecordRows = Map.empty
        RecordLacks = Map.empty
        NextRow = 0
        Shapes = Map.empty
    }

let freshVariable (state: InferenceState) : Type * InferenceState =
    Variable state.NextVariable,
    { state with
        NextVariable = state.NextVariable + 1
    }

let rec freshVariables (state: InferenceState) (count: int) : Type list * InferenceState =
    if count <= 0 then
        [], state
    else
        let first, state1 = freshVariable state
        let rest, state2 = freshVariables state1 (count - 1)
        first :: rest, state2

let unifyState (state: InferenceState) (left: Type) (right: Type) : Result<InferenceState, InferenceError> =
    let unification =
        {
            Types = state.Substitution
            Effects = state.EffectSubstitution
            EffectLacks = state.EffectLacks
            RecordRows = state.RecordRows
            RecordLacks = state.RecordLacks
            NextEffect = state.NextVariable
            NextRow = max state.NextRow state.NextVariable
        }

    unifyWith left right unification
    |> Result.map (fun next ->
        { state with
            Substitution = next.Types
            EffectSubstitution = next.Effects
            EffectLacks = next.EffectLacks
            RecordRows = next.RecordRows
            RecordLacks = next.RecordLacks
            NextVariable = max state.NextVariable next.NextEffect
            NextRow = max state.NextRow next.NextRow
        })

let unifyManyState
    (state: InferenceState)
    (left: Type list)
    (right: Type list)
    : Result<InferenceState, InferenceError> =
    let unification =
        {
            Types = state.Substitution
            Effects = state.EffectSubstitution
            EffectLacks = state.EffectLacks
            RecordRows = state.RecordRows
            RecordLacks = state.RecordLacks
            NextEffect = state.NextVariable
            NextRow = max state.NextRow state.NextVariable
        }

    unifyMany left right unification
    |> Result.map (fun next ->
        { state with
            Substitution = next.Types
            EffectSubstitution = next.Effects
            EffectLacks = next.EffectLacks
            RecordRows = next.RecordRows
            RecordLacks = next.RecordLacks
            NextVariable = max state.NextVariable next.NextEffect
            NextRow = max state.NextRow next.NextRow
        })

/// Application accepts a closed subtype before falling back to unification.
/// The expected parameter is on the left; the supplied argument is on the right.
let rec unifyManySubtype
    (state: InferenceState)
    (expected: Type list)
    (supplied: Type list)
    : Result<InferenceState, InferenceError> =
    match expected, supplied with
    | [], [] -> Ok state
    | expectedType :: expectedRest, suppliedType :: suppliedRest ->
        if subtype suppliedType expectedType then
            unifyManySubtype state expectedRest suppliedRest
        else
            unifyState state expectedType suppliedType
            |> Result.bind (fun next -> unifyManySubtype next expectedRest suppliedRest)
    | _ ->
        Error(
            ArityMismatch(
                List.length expected,
                List.length supplied,
                "function parameters"
            )
        )

let unionEffects
    (state: InferenceState)
    (left: EffectSet)
    (right: EffectSet)
    : EffectSet * InferenceState =
    let left = LabelSets.apply state.EffectSubstitution left
    let right = LabelSets.apply state.EffectSubstitution right

    match left, right with
    | LabelSets.Closed leftLabels, LabelSets.Closed rightLabels ->
        LabelSets.Closed(Set.union leftLabels rightLabels), state
    | LabelSets.Open(leftLabels, variable), LabelSets.Closed rightLabels
    | LabelSets.Closed leftLabels, LabelSets.Open(rightLabels, variable) ->
        LabelSets.Open(Set.union leftLabels rightLabels, variable), state
    | LabelSets.Open(leftLabels, leftVariable), LabelSets.Open(rightLabels, rightVariable) ->
        if leftVariable = rightVariable then
            LabelSets.Open(Set.union leftLabels rightLabels, leftVariable), state
        else
            let fresh = LabelSets.SetVariable state.NextVariable

            let effects =
                state.EffectSubstitution
                |> Map.add leftVariable (LabelSets.Open(rightLabels, fresh))
                |> Map.add rightVariable (LabelSets.Open(leftLabels, fresh))

            LabelSets.Open(Set.union leftLabels rightLabels, fresh),
            { state with
                NextVariable = state.NextVariable + 1
                EffectSubstitution = effects
            }

/// Chooses a common branch type. A subtype is accepted before unification so
/// `if` can join a wider branch to a narrower one.
let commonType
    (state: InferenceState)
    (left: Type)
    (right: Type)
    : Result<Type * InferenceState, InferenceError> =
    let apply current value =
        applyTypeWith
            current.Substitution
            current.EffectSubstitution
            current.RecordRows
            value

    let left = apply state left
    let right = apply state right

    if subtype right left then
        Ok(left, state)
    elif subtype left right then
        Ok(right, state)
    else
        unifyState state left right
        |> Result.map (fun next -> apply next left, next)

/// A handler removes one handled effect from its body and adds the handler's
/// effects. The lacks environment is threaded so an open body row keeps the
/// removal constraint.
let handleEffects
    (handled: Effect)
    (body: EffectSet)
    (handler: EffectSet)
    (lacks: LabelSets.SetLacks<Effect>)
    : Result<EffectSet * LabelSets.SetLacks<Effect>, InferenceError> =
    effectRowSubtract handled body lacks
    |> Result.bind (fun (bodyEffects, lacks) ->
        let combined, _ = unionEffects emptyInferenceState bodyEffects handler
        Ok(combined, lacks))

let wordScheme (signature: WordSignature) : Scheme =
    {
        Quantified = signature.Quantified
        QuantifiedEffects = signature.QuantifiedEffects
        QuantifiedRows = signature.QuantifiedRows
        EffectLacks = LabelSets.emptyLacks
        RowLacks = RowTypes.emptyLacks
        Type = Function(signature.Parameters, signature.Result, signature.Effects)
    }

let environmentVariables (environment: Map<string, Scheme list>) : Set<TypeVariable> =
    environment
    |> Map.toSeq
    |> Seq.map (fun (_, schemes) -> schemes |> List.map freeSchemeVariables |> Set.unionMany)
    |> Set.unionMany

let environmentEffectVariables
    (environment: Map<string, Scheme list>)
    : Set<LabelSets.SetVariable> =
    environment
    |> Map.toSeq
    |> Seq.map (fun (_, schemes) ->
        schemes |> List.map freeSchemeEffectVariables |> Set.unionMany)
    |> Set.unionMany

let environmentRowVariables
    (environment: Map<string, Scheme list>)
    : Set<RowTypes.RowVariable> =
    environment
    |> Map.toSeq
    |> Seq.map (fun (_, schemes) ->
        schemes |> List.map freeSchemeRowVariables |> Set.unionMany)
    |> Set.unionMany

let inferWord
    (state: InferenceState)
    (environment: Map<string, Scheme list>)
    (name: string)
    : Result<Type * InferenceState, InferenceError> =
    match Map.tryFind name environment with
    | None -> Error(UnknownWord name)
    | Some [ scheme ] ->
        let instantiated, effectLacks, rowLacks, next =
            instantiateWithLacks state.NextVariable scheme

        let state =
            {
                state with
                    NextVariable = next
                    EffectLacks =
                        effectLacks
                        |> Map.fold (fun current variable labels -> Map.add variable labels current) state.EffectLacks
                    RecordLacks =
                        rowLacks
                        |> Map.fold (fun current variable labels -> Map.add variable labels current) state.RecordLacks
            }

        Ok(instantiated, state)
    | Some [] -> Error(UnknownWord name)
    | Some _ -> Error(AmbiguousWord name)

let rec applyArguments
    (state: InferenceState)
    (targetType: Type)
    (arguments: Type list)
    : Result<Type * EffectSet * InferenceState, InferenceError> =
    let rec loop
        (currentState: InferenceState)
        (currentType: Type)
        (remainingArguments: Type list)
        (appliedEffects: EffectSet)
        : Result<Type * EffectSet * InferenceState, InferenceError> =
        let currentType =
            applyTypeWith
                currentState.Substitution
                currentState.EffectSubstitution
                currentState.RecordRows
                currentType

        match currentType, remainingArguments with
        | Function([], result, effects), [] ->
            let nextEffects, nextState = unionEffects currentState appliedEffects effects
            Ok(result, nextEffects, nextState)
        | _, [] -> Ok(currentType, appliedEffects, currentState)
        | Function(parameters, result, effects), _ ->
            if List.length remainingArguments < List.length parameters then
                let supplied = List.take (List.length remainingArguments) parameters
                let remaining = List.skip (List.length remainingArguments) parameters

                unifyManySubtype currentState supplied remainingArguments
                |> Result.map (fun next ->
                    Function(remaining, result, effects),
                    appliedEffects,
                    next)
            else
                let supplied = parameters
                let passed = List.take (List.length parameters) remainingArguments
                let extra = List.skip (List.length parameters) remainingArguments

                unifyManySubtype currentState supplied passed
                |> Result.bind (fun nextState ->
                    let nextEffects, nextState = unionEffects nextState appliedEffects effects

                    if List.isEmpty extra then
                        Ok(result, nextEffects, nextState)
                    else
                        loop nextState result extra nextEffects)
        | Variable _, _ ->
            let result, state1 = freshVariable currentState
            let freshParameters, state2 = freshVariables state1 (List.length remainingArguments)

            unifyState state2 currentType (Function(freshParameters, result, emptyEffects))
            |> Result.map (fun next -> result, appliedEffects, next)
        | other, _ ->
            Error(
                TypeMismatch(
                    Function(arguments, Primitive "unknown", emptyEffects),
                    other,
                    "call target"
                )
            )

    loop state targetType arguments emptyEffects

let projectField
    (state: InferenceState)
    (name: string)
    (recordType: Type)
    : Result<Type * InferenceState, InferenceError> =
    let recordType =
        applyTypeWith
            state.Substitution
            state.EffectSubstitution
            state.RecordRows
            recordType

    let extendRow (nextState: InferenceState) =
        let fieldType, nextState = freshVariable nextState
        let tailIdentifier = max nextState.NextRow nextState.NextVariable
        let freshTail = RowTypes.RowVariable tailIdentifier

        let field: RecordField =
            { Name = name; Type = fieldType }

        RowTypes.Extend(field, RowTypes.Variable freshTail),
        fieldType,
        {
            nextState with
                NextRow = tailIdentifier + 1
                NextVariable = max nextState.NextVariable (tailIdentifier + 1)
        }

    let bindRow (current: InferenceState) variable absorbed =
        match RowTypes.bindRow variable absorbed current.RecordRows current.RecordLacks with
        | Ok(rows, lacks) ->
            Ok
                {
                    current with
                        RecordRows = rows
                        RecordLacks = lacks
                }
        | Error(RowTypes.OccursCheck(variable, _)) ->
            Error(RowOccursCheck(variable, "a projected record row"))
        | Error(RowTypes.LacksViolation(variable, labels)) ->
            let names =
                labels
                |> Set.toList
                |> List.map (fun field -> field.Name)
                |> String.concat ", "

            Error(RowLacksViolation(variable, names))
        | Error(RowTypes.DuplicateLabel label) -> Error(DuplicateLabel label.Name)
        | Error(RowTypes.Mismatch(_, _, context)) ->
            Error(TypeMismatch(recordType, recordType, context))

    match recordType with
    | Record row ->
        match recordFields row |> List.tryFind (fun field -> field.Name = name) with
        | Some field -> Ok(field.Type, state)
        | None ->
            match recordTail row with
            | RowTypes.Variable variable ->
                let absorbed, fieldType, nextState = extendRow state

                bindRow nextState variable absorbed
                |> Result.map (fun final -> fieldType, final)
            | RowTypes.Empty -> Error(UnknownField name)
            | RowTypes.Extend _ -> Error(UnknownField name)
    | Variable variable ->
        let absorbed, fieldType, nextState = extendRow state

        Ok(
            fieldType,
            { nextState with
                Substitution = Map.add variable (Record absorbed) nextState.Substitution
            }
        )
    | other -> Error(TypeMismatch(Primitive "record", other, "field access"))

let rec infer
    (path: ExpressionPath)
    (state: InferenceState)
    (environment: Map<string, Scheme list>)
    (expression: Expression)
    : Result<Inferred * InferenceState, ExpressionPath * InferenceError> =
    match expression with
    | Literal(NumberLiteral _) ->
        Ok(
            {
                Type = Primitive "number"
                Effects = emptyEffects
            },
            state
        )
    | Literal(TextLiteral _) ->
        Ok(
            {
                Type = Primitive "text"
                Effects = emptyEffects
            },
            state
        )
    | Literal(AtomLiteral _) ->
        Ok(
            {
                Type = Primitive "atom"
                Effects = emptyEffects
            },
            state
        )
    | Word name ->
        inferWord state environment name
        |> Result.mapError (fun error -> path, error)
        |> Result.map (fun (wordType, next) -> { Type = wordType; Effects = emptyEffects }, next)
    | Call(Word "field", [ Literal(TextLiteral name); recordExpression ]) ->
        infer (path @ [ 1 ]) state environment recordExpression
        |> Result.bind (fun (recordResult, state1) ->
            projectField state1 name recordResult.Type
            |> Result.mapError (fun error -> path, error)
            |> Result.map (fun (fieldType, state2) ->
                {
                    Type = fieldType
                    Effects = recordResult.Effects
                },
                state2))
    | Call(Word "with-handler", [ body; handler ]) ->
        infer (path @ [ 0 ]) state environment body
        |> Result.bind (fun (bodyResult, state1) ->
            infer (path @ [ 1 ]) state1 environment handler
            |> Result.bind (fun (handlerResult, state2) ->
                match bodyResult.Type, handlerResult.Type with
                | Code(bodyType, bodyEffects), Code(handlerType, handlerEffects) ->
                    unifyState state2 bodyType handlerType
                    |> Result.mapError (fun error -> path, error)
                    |> Result.bind (fun state3 ->
                        match
                            handleEffects
                                "failure"
                                bodyEffects
                                handlerEffects
                                state3.EffectLacks
                        with
                        | Error error -> Error(path, error)
                        | Ok(effects, lacks) ->
                            Ok(
                                {
                                    Type = bodyType
                                    Effects = effects
                                },
                                { state3 with EffectLacks = lacks }
                            ))
                | _ ->
                    Error(
                        path,
                        TypeMismatch(
                            Code(Primitive "unknown", emptyEffects),
                            bodyResult.Type,
                            "with-handler expects code values"
                        )
                    )))
    | Call(target, arguments) ->
        infer (path @ [ 0 ]) state environment target
        |> Result.bind (fun (targetResult, state1) ->
            inferMany path 1 state1 environment arguments
            |> Result.bind (fun (argumentTypes, argumentEffects, state2) ->
                applyArguments state2 targetResult.Type argumentTypes
                |> Result.mapError (fun error -> path, error)
                |> Result.map (fun (resultType, latentEffects, state3) ->
                    let effects, state4 =
                        unionEffects state3 targetResult.Effects argumentEffects

                    let effects, state5 = unionEffects state4 effects latentEffects

                    {
                        Type = resultType
                        Effects = effects
                    },
                    state5)))
    | Partial(target, arguments) ->
        infer (path @ [ 0 ]) state environment target
        |> Result.bind (fun (targetResult, state1) ->
            inferMany path 1 state1 environment arguments
            |> Result.bind (fun (argumentTypes, argumentEffects, state2) ->
                let targetType =
                    applyTypeWith
                        state2.Substitution
                        state2.EffectSubstitution
                        state2.RecordRows
                        targetResult.Type

                match targetType with
                | Function(parameters, result, effects) ->
                    if List.length argumentTypes > List.length parameters then
                        Error(
                            path,
                            ArityMismatch(List.length parameters, List.length argumentTypes, "partial application")
                        )
                    else
                        let supplied = List.take (List.length argumentTypes) parameters
                        let remaining = List.skip (List.length argumentTypes) parameters

                        unifyManyState state2 supplied argumentTypes
                        |> Result.mapError (fun error -> path, error)
                        |> Result.map (fun nextState ->
                            let missing =
                                (arguments
                                 |> List.indexed
                                 |> List.choose (fun (index, argument) ->
                                     match argument with
                                     | Hole -> Some supplied.[index]
                                     | _ -> None))
                                @ remaining

                            let resultType =
                                if List.isEmpty missing then
                                    result
                                else
                                    Function(missing, result, effects)

                            let targetAndArguments, state3 =
                                unionEffects nextState targetResult.Effects argumentEffects

                            let resultEffects, state4 =
                                if List.isEmpty missing then
                                    unionEffects state3 targetAndArguments effects
                                else
                                    targetAndArguments, state3

                            {
                                Type = resultType
                                Effects = resultEffects
                            },
                            state4)
                | Variable _ ->
                    let result, state3 = freshVariable state2
                    let freshParameters, state4 = freshVariables state3 (List.length argumentTypes)

                    unifyState state4 targetType (Function(freshParameters, result, emptyEffects))
                    |> Result.mapError (fun error -> path, error)
                    |> Result.map (fun next ->
                        let missing =
                            arguments
                            |> List.indexed
                            |> List.choose (fun (index, argument) ->
                                match argument with
                                | Hole -> Some freshParameters.[index]
                                | _ -> None)

                        let resultType =
                            if List.isEmpty missing then
                                result
                            else
                                Function(missing, result, emptyEffects)

                        let effects, nextState =
                            unionEffects next targetResult.Effects argumentEffects

                        {
                            Type = resultType
                            Effects = effects
                        },
                        nextState)
                | other -> Error(path, TypeMismatch(Primitive "function", other, "partial application target"))))
    | Hole ->
        let typeExpression, next = freshVariable state

        Ok(
            {
                Type = typeExpression
                Effects = emptyEffects
            },
            next
        )
    | Lambda(parameters, body) ->
        let parameterTypes, state1 = freshVariables state (List.length parameters)

        let environment1 =
            List.fold2
                (fun current name typeExpression ->
                    Map.add
                        name
                        [
                            {
                                Quantified = []
                                QuantifiedEffects = []
                                QuantifiedRows = []
                                EffectLacks = LabelSets.emptyLacks
                                RowLacks = RowTypes.emptyLacks
                                Type = typeExpression
                            }
                        ]
                        current)
                environment
                parameters
                parameterTypes

        infer (path @ [ 0 ]) state1 environment1 body
        |> Result.map (fun (bodyResult, state2) ->
            {
                Type = Function(parameterTypes, bodyResult.Type, bodyResult.Effects)
                Effects = emptyEffects
            },
            state2)
    | Let(name, value, body) ->
        infer (path @ [ 0 ]) state environment value
        |> Result.bind (fun (valueResult, state1) ->
            let valueType =
                applyTypeWith
                    state1.Substitution
                    state1.EffectSubstitution
                    state1.RecordRows
                    valueResult.Type

            let scheme =
                generalizeWithLacks
                    (environmentVariables environment)
                    (environmentEffectVariables environment)
                    (environmentRowVariables environment)
                    state1.EffectLacks
                    state1.RecordLacks
                    valueType

            let environment1 = Map.add name [ scheme ] environment

            infer (path @ [ 1 ]) state1 environment1 body
            |> Result.map (fun (bodyResult, state2) ->
                let effects, state3 = unionEffects state1 valueResult.Effects bodyResult.Effects

                {
                    Type = bodyResult.Type
                    Effects = effects
                },
                state3))
    | If(condition, thenBranch, elseBranch) ->
        infer (path @ [ 0 ]) state environment condition
        |> Result.bind (fun (conditionResult, state1) ->
            unifyState state1 conditionResult.Type (Primitive "boolean")
            |> Result.mapError (fun error -> path @ [ 0 ], error)
            |> Result.bind (fun state2 ->
                infer (path @ [ 1 ]) state2 environment thenBranch
                |> Result.bind (fun (thenResult, state3) ->
                    infer (path @ [ 2 ]) state3 environment elseBranch
                    |> Result.bind (fun (elseResult, state4) ->
                        commonType state4 thenResult.Type elseResult.Type
                        |> Result.mapError (fun error -> path, error)
                        |> Result.map (fun (resultType, state5) ->
                            let branchEffects, state6 =
                                unionEffects state5 thenResult.Effects elseResult.Effects

                            let effects, state7 =
                                unionEffects state6 conditionResult.Effects branchEffects

                            {
                                Type = resultType
                                Effects = effects
                            },
                            state7)))))
    | Pipeline _
    | ReversePipeline _ -> infer path state environment (ExpressionLogic.desugarPipeline expression)
    | Block body ->
        inferMany path 0 state environment body
        |> Result.map (fun (results, effects, next) ->
            let resultType =
                match List.tryLast results with
                | Some lastType -> lastType
                | None -> Primitive "unit"

            { Type = resultType; Effects = effects }, next)
    | Quotation body ->
        infer (path @ [ 0 ]) state environment (Block body)
        |> Result.map (fun (result, next) ->
            {
                Type = Code(result.Type, result.Effects)
                Effects = emptyEffects
            },
            next)
    | PatternMatch _ -> Error(path, UnsupportedExpression "pattern match")
    | DeclareKind _
    | DeclarePredicate _
    | Condition _
    | Restart _
    | Generic _ ->
        Ok(
            {
                Type = Primitive "unit"
                Effects = emptyEffects
            },
            state
        )
    | Fail _ ->
        Ok(
            {
                Type = Primitive "unit"
                Effects = effectSet "failure"
            },
            state
        )
    | Message(kind, fields) ->
        match Map.tryFind kind state.Shapes with
        | Some fieldNames when List.length fieldNames = List.length fields ->
            inferMany path 0 state environment fields
            |> Result.bind (fun (fieldTypes, effects, next) ->
                match RecordTypes.ofList (List.zip fieldNames fieldTypes) with
                | Ok row ->
                    Ok(
                        {
                            Type = Record row
                            Effects = effects
                        },
                        next
                    )
                | Error reason ->
                    Error(path, UnsupportedExpression("record shape: " + reason)))
        | Some fieldNames ->
            Error(path, ArityMismatch(List.length fieldNames, List.length fields, "shape " + kind))
        | None ->
            Ok(
                {
                    Type = Primitive "unit"
                    Effects = emptyEffects
                },
                state
            )

and inferMany
    (path: ExpressionPath)
    (startIndex: int)
    (state: InferenceState)
    (environment: Map<string, Scheme list>)
    (expressions: Expression list)
    : Result<Type list * EffectSet * InferenceState, ExpressionPath * InferenceError> =
    match expressions with
    | [] -> Ok([], emptyEffects, state)
    | expression :: rest ->
        infer (path @ [ startIndex ]) state environment expression
        |> Result.bind (fun (inferred, state1) ->
            inferMany path (startIndex + 1) state1 environment rest
            |> Result.map (fun (types, effects, state2) ->
                let effects, state3 = unionEffects state2 inferred.Effects effects
                inferred.Type :: types, effects, state3))

let inferExpression (signatures: SignatureTable) (expression: Expression) : Result<Inferred, InferenceError> =
    let environment =
        signatures
        |> Map.map (fun _ wordSignatures -> wordSignatures |> List.map wordScheme)

    infer [] emptyInferenceState environment expression
    |> Result.map (fun (inferred, state) ->
        { inferred with
            Type =
                applyTypeWith
                    state.Substitution
                    state.EffectSubstitution
                    state.RecordRows
                    inferred.Type
            Effects = LabelSets.apply state.EffectSubstitution inferred.Effects
        })
    |> Result.mapError snd

let inferExpressionLocated
    (signatures: SignatureTable)
    (sourceMap: ExpressionSourceMap)
    (expression: Expression)
    : Result<Inferred, InferenceDiagnostic> =
    let environment =
        signatures
        |> Map.map (fun _ wordSignatures -> wordSignatures |> List.map wordScheme)

    infer [] emptyInferenceState environment expression
    |> Result.map (fun (inferred, state) ->
        { inferred with
            Type =
                applyTypeWith
                    state.Substitution
                    state.EffectSubstitution
                    state.RecordRows
                    inferred.Type
            Effects = LabelSets.apply state.EffectSubstitution inferred.Effects
        })
    |> Result.mapError (fun (path, error) ->
        {
            Path = path
            Range = Map.tryFind path sourceMap
            Error = error
            Description = describeError error
        })
