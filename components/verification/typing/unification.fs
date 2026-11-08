module Unification

open RowTypes
open LabelSets

type TypeVariable = int

type TypeTerm =
    | Variable of TypeVariable
    | Primitive of string
    | Sort of string
    | Record of row: Row<RecordTypes.RecordField<TypeTerm>>
    | Function of parameters: TypeTerm list * result: TypeTerm * effects: LabelSet<string>
    | Code of result: TypeTerm * effects: LabelSet<string>

type RecordField = RecordTypes.RecordField<TypeTerm>

type KindedTerm =
    | TypeTerm of TypeTerm
    | RowTerm of Row<string>
    | SetTerm of LabelSet<string>

type UnificationError =
    | OccursCheck of kind: string * variable: int * term: string
    | KindMismatch of left: string * right: string * context: string
    | TypeMismatch of expected: TypeTerm * actual: TypeTerm * context: string
    | ArityMismatch of expected: int * actual: int * context: string
    | EffectMismatch of expected: LabelSet<string> * actual: LabelSet<string> * context: string
    | RowMismatch of expected: Row<string> * actual: Row<string> * context: string
    | SetMismatch of expected: LabelSet<string> * actual: LabelSet<string> * context: string
    | RecordMismatch of context: string
    | DuplicateLabel of label: string
    | LacksViolation of kind: string * variable: int * labels: string

type State =
    {
        NextType: int
        NextRow: int
        NextSet: int
        Types: Map<TypeVariable, TypeTerm>
        Rows: RowSubstitution<string>
        RowsLacks: RowLacks<string>
        RecordRows: RowSubstitution<RecordField>
        RecordLacks: RowLacks<RecordField>
        Sets: SetSubstitution<string>
        SetsLacks: SetLacks<string>
    }

type Scheme =
    {
        QuantifiedTypes: TypeVariable list
        QuantifiedSets: SetVariable list
        QuantifiedRows: RowVariable list
        SetLacks: SetLacks<string>
        RowLacks: RowLacks<string>
        Type: TypeTerm
    }

let emptyState: State =
    {
        NextType = 0
        NextRow = 0
        NextSet = 0
        Types = Map.empty
        Rows = RowTypes.emptySubstitution
        RowsLacks = RowTypes.emptyLacks
        RecordRows = RowTypes.emptySubstitution
        RecordLacks = RowTypes.emptyLacks
        Sets = Map.empty
        SetsLacks = LabelSets.emptyLacks
    }

let rec applyType (state: State) (term: TypeTerm) : TypeTerm =
    match term with
    | Variable variable ->
        match Map.tryFind variable state.Types with
        | Some replacement -> applyType state replacement
        | None -> term
    | Primitive _
    | Sort _ -> term
    | Record row ->
        let rec applyRow (current: Row<RecordField>) : Row<RecordField> =
            match current with
            | Empty -> Empty
            | Extend(field, rest) ->
                Extend({ field with Type = applyType state field.Type }, applyRow rest)
            | RowTypes.Variable variable ->
                match Map.tryFind variable state.RecordRows with
                | Some replacement -> applyRow replacement
                | None -> RowTypes.Variable variable

        Record(applyRow (RowTypes.apply state.RecordRows row))
    | Function(parameters, result, effects) ->
        Function(
            parameters |> List.map (applyType state),
            applyType state result,
            LabelSets.apply state.Sets effects
        )
    | Code(result, effects) ->
        Code(applyType state result, LabelSets.apply state.Sets effects)

let recordFields (row: Row<RecordField>) : RecordField list =
    RecordTypes.fields row

let recordTail (row: Row<RecordField>) : Row<RecordField> =
    RecordTypes.tail row

let rec occursType (variable: TypeVariable) (term: TypeTerm) : bool =
    match term with
    | Variable other -> variable = other
    | Primitive _
    | Sort _ -> false
    | Record row -> recordFields row |> List.exists (fun field -> occursType variable field.Type)
    | Function(parameters, result, _) ->
        List.exists (occursType variable) parameters || occursType variable result
    | Code(result, _) -> occursType variable result

let rec freeTypeVariables (term: TypeTerm) : Set<TypeVariable> =
    match term with
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

let rec freeSetVariables (term: TypeTerm) : Set<SetVariable> =
    match term with
    | Variable _
    | Primitive _
    | Sort _ -> Set.empty
    | Record row ->
        recordFields row
        |> List.map (fun field -> freeSetVariables field.Type)
        |> Set.unionMany
    | Function(parameters, result, effects) ->
        let fromParameters = parameters |> List.map freeSetVariables |> Set.unionMany

        Set.unionMany
            [
                fromParameters
                freeSetVariables result
                LabelSets.freeVariables effects
            ]
    | Code(result, effects) ->
        Set.union (freeSetVariables result) (LabelSets.freeVariables effects)

let rec freeRowVariables (term: TypeTerm) : Set<RowVariable> =
    match term with
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

let rec describeType (term: TypeTerm) : string =
    match term with
    | Variable variable -> "t" + string variable
    | Primitive name -> name
    | Sort name -> "sort " + name
    | Record row ->
        let fieldText =
            recordFields row
            |> List.map (fun field -> field.Name + ": " + describeType field.Type)
            |> String.concat ", "

        match recordTail row with
        | Empty -> "{" + fieldText + "}"
        | RowTypes.Variable(RowTypes.RowVariable variable) ->
            "{" + fieldText + (if fieldText = "" then "" else " | ") + "r" + string variable + "}"
        | Extend _ -> "{" + fieldText + "}"
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

let describeError (error: UnificationError) : string =
    match error with
    | OccursCheck(kind, variable, term) -> "occurs check: " + kind + " variable " + string variable + " appears in " + term
    | KindMismatch(left, right, context) -> context + ": cannot unify a " + left + " with a " + right
    | TypeMismatch(expected, actual, context) ->
        context + ": expected " + describeType expected + ", found " + describeType actual
    | ArityMismatch(expected, actual, context) ->
        context + ": expected " + string expected + " parameters, found " + string actual
    | EffectMismatch(expected, actual, context) ->
        context + ": expected effects " + LabelSets.describe id expected + ", found " + LabelSets.describe id actual
    | RowMismatch(expected, actual, context) ->
        context + ": expected row " + RowTypes.describe id expected + ", found " + RowTypes.describe id actual
    | SetMismatch(expected, actual, context) ->
        context + ": expected set " + LabelSets.describe id expected + ", found " + LabelSets.describe id actual
    | RecordMismatch context -> context + ": record labels and fields disagree"
    | DuplicateLabel label -> "duplicate label: " + label
    | LacksViolation(kind, variable, labels) ->
        kind + " variable " + string variable + " lacks " + labels

let unifyRow
    (context: string)
    (left: Row<string>)
    (right: Row<string>)
    (state: State)
    : Result<State, UnificationError> =
    match RowTypes.unifyWithLacks context left right state.Rows state.RowsLacks state.NextRow with
    | Ok(rows, lacks, next) ->
        Ok(
            {
                state with
                    Rows = rows
                    RowsLacks = lacks
                    NextRow = next
            }
        )
    | Error(RowTypes.OccursCheck(variable, row)) ->
        Error(OccursCheck("row", (let (RowVariable value) = variable in value), RowTypes.describe id row))
    | Error(RowTypes.Mismatch(leftRow, rightRow, mismatchContext)) ->
        Error(RowMismatch(leftRow, rightRow, mismatchContext))
    | Error(RowTypes.DuplicateLabel label) -> Error(DuplicateLabel label)
    | Error(RowTypes.LacksViolation(variable, (labels: Set<string>))) ->
        Error(
            LacksViolation(
                "row",
                (let (RowVariable value) = variable in value),
                String.concat ", " (Set.toList labels)
            )
        )

let unifySet
    (context: string)
    (left: LabelSet<string>)
    (right: LabelSet<string>)
    (state: State)
    : Result<State, UnificationError> =
    match LabelSets.unifyWithLacks context left right state.Sets state.SetsLacks state.NextSet with
    | Ok(sets, lacks, next) ->
        Ok(
            {
                state with
                    Sets = sets
                    SetsLacks = lacks
                    NextSet = next
            }
        )
    | Error(LabelSets.OccursCheck(variable, set)) ->
        Error(OccursCheck("set", (let (SetVariable value) = variable in value), LabelSets.describe id set))
    | Error(LabelSets.Mismatch(leftSet, rightSet, mismatchContext)) ->
        Error(SetMismatch(leftSet, rightSet, mismatchContext))
    | Error(LabelSets.LacksViolation(variable, labels)) ->
        Error(
            LacksViolation(
                "set",
                (let (SetVariable value) = variable in value),
                String.concat ", " (Set.toList labels)
            )
        )

let recordOfList
    (fields: (string * TypeTerm) list)
    : Result<Row<RecordField>, UnificationError> =
    fields
    |> RecordTypes.ofList
    |> Result.mapError (fun reason -> RecordMismatch reason)

let private extendRecordMany
    (fields: RecordField list)
    (tail: Row<RecordField>)
    : Row<RecordField> =
    List.foldBack (fun field rest -> Extend(field, rest)) fields tail

let private flattenRecord
    (substitution: RowSubstitution<RecordField>)
    (row: Row<RecordField>)
    : RecordField list * Row<RecordField> =
    let rec loop (current: Row<RecordField>) (accumulated: RecordField list) =
        match current with
        | Empty -> List.rev accumulated, Empty
        | Extend(field, rest) -> loop rest (field :: accumulated)
        | RowTypes.Variable variable ->
            match Map.tryFind variable substitution with
            | Some replacement -> loop replacement accumulated
            | None -> List.rev accumulated, RowTypes.Variable variable

    loop row []

let rec unifyTypeMany
    (left: TypeTerm list)
    (right: TypeTerm list)
    (state: State)
    : Result<State, UnificationError> =
    match left, right with
    | [], [] -> Ok state
    | leftType :: leftRest, rightType :: rightRest ->
        unifyType leftType rightType state
        |> Result.bind (fun next -> unifyTypeMany leftRest rightRest next)
    | _ -> Error(ArityMismatch(List.length left, List.length right, "function parameters"))

and unifyType (left: TypeTerm) (right: TypeTerm) (state: State) : Result<State, UnificationError> =
    let left = applyType state left
    let right = applyType state right

    match left, right with
    | Variable first, Variable second when first = second -> Ok state
    | Variable variable, other
    | other, Variable variable ->
        if occursType variable other then
            Error(OccursCheck("type", variable, describeType other))
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
            unifyTypeMany leftParameters rightParameters state
            |> Result.bind (fun next -> unifyType leftResult rightResult next)
            |> Result.bind (fun next ->
                unifySet "function effects" leftEffects rightEffects next
                |> Result.mapError (fun error ->
                    match error with
                    | SetMismatch(expected, actual, _) -> EffectMismatch(expected, actual, "function effects")
                    | other -> other))
    | Code(leftResult, leftEffects), Code(rightResult, rightEffects) ->
        unifyType leftResult rightResult state
        |> Result.bind (fun next ->
            unifySet "code effects" leftEffects rightEffects next
            |> Result.mapError (fun error ->
                match error with
                | SetMismatch(expected, actual, _) -> EffectMismatch(expected, actual, "code effects")
                | other -> other))
    | _ -> Error(TypeMismatch(left, right, "type equality"))

and unifyRecord
    (leftRow: Row<RecordField>)
    (rightRow: Row<RecordField>)
    (state: State)
    : Result<State, UnificationError> =
    let leftFields, leftTail = flattenRecord state.RecordRows leftRow
    let rightFields, rightTail = flattenRecord state.RecordRows rightRow

    let bindRecord (current: State) variable row =
        match RowTypes.bindRow variable row current.RecordRows current.RecordLacks with
        | Ok(rows, lacks) ->
            Ok
                {
                    current with
                        RecordRows = rows
                        RecordLacks = lacks
                }
        | Error(RowTypes.OccursCheck(variable, _)) ->
            Error(OccursCheck("record", (let (RowVariable value) = variable in value), "record row"))
        | Error(RowTypes.LacksViolation(variable, labels)) ->
            Error(
                LacksViolation(
                    "record",
                    (let (RowVariable value) = variable in value),
                    labels
                    |> Set.toList
                    |> List.map (fun field -> field.Name)
                    |> String.concat ", "
                )
            )
        | Error(RowTypes.DuplicateLabel label) -> Error(DuplicateLabel label.Name)
        | Error(RowTypes.Mismatch(_, _, context)) -> Error(RecordMismatch context)

    let duplicate (fields: RecordField list) =
        fields
        |> List.countBy (fun field -> field.Name)
        |> List.tryFind (fun (_, count) -> count > 1)
        |> Option.map fst

    match duplicate leftFields, duplicate rightFields with
    | Some name, _ -> Error(DuplicateLabel name)
    | _, Some name -> Error(DuplicateLabel name)
    | None, None ->
        let rightByName = rightFields |> List.map (fun field -> field.Name, field) |> Map.ofList
        let leftByName = leftFields |> List.map (fun field -> field.Name, field) |> Map.ofList

        let common =
            leftFields
            |> List.choose (fun leftField ->
                Map.tryFind leftField.Name rightByName
                |> Option.map (fun rightField -> leftField, rightField))

        unifyTypeMany
            (common |> List.map (fun (leftField, _) -> leftField.Type))
            (common |> List.map (fun (_, rightField) -> rightField.Type))
            state
        |> Result.bind (fun state ->
            let onlyLeft =
                leftFields |> List.filter (fun field -> not (Map.containsKey field.Name rightByName))

            let onlyRight =
                rightFields |> List.filter (fun field -> not (Map.containsKey field.Name leftByName))

            match onlyLeft, onlyRight, leftTail, rightTail with
            | [], [], Empty, Empty -> Ok state
            | [], [], RowTypes.Variable leftVariable, RowTypes.Variable rightVariable ->
                if leftVariable = rightVariable then
                    Ok state
                else
                    bindRecord state leftVariable (RowTypes.Variable rightVariable)
            | [], [], RowTypes.Variable variable, Empty
            | [], [], Empty, RowTypes.Variable variable ->
                bindRecord state variable Empty
            | _, [], _, RowTypes.Variable rightVariable ->
                let absorbed = extendRecordMany onlyLeft leftTail
                bindRecord state rightVariable absorbed
            | [], _, RowTypes.Variable leftVariable, _ ->
                let absorbed = extendRecordMany onlyRight rightTail
                bindRecord state leftVariable absorbed
            | _, _, RowTypes.Variable leftVariable, RowTypes.Variable rightVariable
                when leftVariable <> rightVariable ->
                let fresh = RowVariable state.NextRow
                let leftAbsorbed = extendRecordMany onlyRight (RowTypes.Variable fresh)
                let rightAbsorbed = extendRecordMany onlyLeft (RowTypes.Variable fresh)

                bindRecord state leftVariable leftAbsorbed
                |> Result.bind (fun next ->
                    bindRecord next rightVariable rightAbsorbed
                    |> Result.map (fun final ->
                        {
                            final with
                                NextRow = state.NextRow + 1
                        }))
            | _ -> Error(RecordMismatch "record labels"))

let unifyKinded
    (left: KindedTerm)
    (right: KindedTerm)
    (state: State)
    : Result<State, UnificationError> =
    match left, right with
    | TypeTerm leftType, TypeTerm rightType -> unifyType leftType rightType state
    | RowTerm leftRow, RowTerm rightRow -> unifyRow "row" leftRow rightRow state
    | SetTerm leftSet, SetTerm rightSet -> unifySet "set" leftSet rightSet state
    | TypeTerm _, RowTerm _ -> Error(KindMismatch("type", "row", "kind dispatch"))
    | TypeTerm _, SetTerm _ -> Error(KindMismatch("type", "set", "kind dispatch"))
    | RowTerm _, TypeTerm _ -> Error(KindMismatch("row", "type", "kind dispatch"))
    | RowTerm _, SetTerm _ -> Error(KindMismatch("row", "set", "kind dispatch"))
    | SetTerm _, TypeTerm _ -> Error(KindMismatch("set", "type", "kind dispatch"))
    | SetTerm _, RowTerm _ -> Error(KindMismatch("set", "row", "kind dispatch"))

let generalizeWithLacks
    (environmentTypes: Set<TypeVariable>)
    (environmentSets: Set<SetVariable>)
    (environmentRows: Set<RowVariable>)
    (setLacks: SetLacks<string>)
    (rowLacks: RowLacks<string>)
    (term: TypeTerm)
    : Scheme =
    let quantifiedSets =
        Set.difference (freeSetVariables term) environmentSets
        |> Set.toList

    let quantifiedRows =
        Set.difference (freeRowVariables term) environmentRows
        |> Set.toList

    let quantifiedSetSet = Set.ofList quantifiedSets
    let quantifiedRowSet = Set.ofList quantifiedRows

    {
        QuantifiedTypes =
            Set.difference (freeTypeVariables term) environmentTypes
            |> Set.toList
        QuantifiedSets = quantifiedSets
        QuantifiedRows = quantifiedRows
        SetLacks =
            setLacks
            |> Map.filter (fun variable _ -> quantifiedSetSet.Contains variable)
        RowLacks =
            rowLacks
            |> Map.filter (fun variable _ -> quantifiedRowSet.Contains variable)
        Type = term
    }

let generalizeWith
    (environmentTypes: Set<TypeVariable>)
    (environmentSets: Set<SetVariable>)
    (environmentRows: Set<RowVariable>)
    (term: TypeTerm)
    : Scheme =
    generalizeWithLacks
        environmentTypes
        environmentSets
        environmentRows
        LabelSets.emptyLacks
        RowTypes.emptyLacks
        term

let generalize
    (environmentTypes: Set<TypeVariable>)
    (environmentSets: Set<SetVariable>)
    (term: TypeTerm)
    : Scheme =
    generalizeWith environmentTypes environmentSets Set.empty term

let instantiateWithLacks
    (nextType: int, nextSet: int)
    (scheme: Scheme)
    : TypeTerm * SetLacks<string> * RowLacks<string> * int * int =
    let highestType =
        scheme.QuantifiedTypes |> List.fold max -1

    let highestRow =
        scheme.QuantifiedRows
        |> List.fold
            (fun highest (RowVariable identifier) -> max highest identifier)
            -1

    let nextType = max nextType (max highestType highestRow + 1)

    let typeSubstitution, nextType =
        scheme.QuantifiedTypes
        |> List.fold
            (fun (substitution, current) variable ->
                Map.add variable (Variable current) substitution, current + 1)
            (Map.empty, nextType)

    let setSubstitution, nextSet =
        scheme.QuantifiedSets
        |> List.fold
            (fun (substitution, current) variable ->
                Map.add variable (Open(Set.empty, SetVariable current)) substitution, current + 1)
            (Map.empty, nextSet)

    let rowSubstitution, nextType =
        scheme.QuantifiedRows
        |> List.fold
            (fun (substitution, current) (RowVariable identifier) ->
                Map.add
                    (RowVariable identifier)
                    (RowTypes.Variable(RowVariable current))
                    substitution,
                current + 1)
            (Map.empty, nextType)

    let setLacks =
        scheme.SetLacks
        |> Map.fold
            (fun lacks variable labels ->
                match Map.tryFind variable setSubstitution with
                | Some(Open(_, fresh)) -> Map.add fresh labels lacks
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

    let state =
        {
            emptyState with
                Types = typeSubstitution
                Sets = setSubstitution
                RecordRows = rowSubstitution
                NextType = nextType
                NextSet = nextSet
        }

    applyType state scheme.Type, setLacks, rowLacks, nextType, nextSet

let instantiate
    (nextType: int, nextSet: int)
    (scheme: Scheme)
    : TypeTerm * int * int =
    let instantiated, _, _, nextType, nextSet = instantiateWithLacks (nextType, nextSet) scheme
    instantiated, nextType, nextSet
