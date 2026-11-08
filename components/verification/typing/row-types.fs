module RowTypes

/// A row variable is a distinct identity for an unknown row tail. It is
/// nominal, so it cannot be confused with a set variable.
type RowVariable = RowVariable of int

/// A data row: an ordered sequence of labels with an optional unknown tail.
/// Labels are compared as a set for unification; order is presentation only.
/// This is for structural data (records and variants), not for effects.
type Row<'label> =
    | Empty
    | Extend of label: 'label * rest: Row<'label>
    | Variable of RowVariable

type RowError<'label when 'label : comparison> =
    | OccursCheck of variable: RowVariable * row: Row<'label>
    | Mismatch of left: Row<'label> * right: Row<'label> * context: string
    | DuplicateLabel of label: 'label
    | LacksViolation of variable: RowVariable * labels: Set<'label>

type RowSubstitution<'label> = Map<RowVariable, Row<'label>>

type RowLacks<'label when 'label : comparison> = Map<RowVariable, Set<'label>>

let emptySubstitution<'label> : RowSubstitution<'label> = Map.empty

let emptyLacks<'label when 'label : comparison> : RowLacks<'label> = Map.empty

let empty: Row<'label> = Empty

let singleton (label: 'label) : Row<'label> = Extend(label, Empty)

let rec labelsOf (row: Row<'label>) : 'label list =
    match row with
    | Empty
    | Variable _ -> []
    | Extend(label, rest) -> label :: labelsOf rest

let ofList (input: 'label list) : Result<Row<'label>, RowError<'label>> =
    let rec loop (remaining: 'label list) (accumulated: Row<'label>) =
        match remaining with
        | [] -> Ok accumulated
        | label :: rest ->
            if List.contains label (labelsOf accumulated) then
                Error(DuplicateLabel label)
            else
                loop rest (Extend(label, accumulated))

    loop input Empty

let rec tail (row: Row<'label>) : Row<'label> =
    match row with
    | Empty
    | Variable _ -> row
    | Extend(_, rest) -> tail rest

let rec freeVariables (row: Row<'label>) : Set<RowVariable> =
    match row with
    | Empty -> Set.empty
    | Extend(_, rest) -> freeVariables rest
    | Variable variable -> Set.singleton variable

let rec apply (substitution: RowSubstitution<'label>) (row: Row<'label>) : Row<'label> =
    match row with
    | Empty -> Empty
    | Extend(label, rest) -> Extend(label, apply substitution rest)
    | Variable variable ->
        match Map.tryFind variable substitution with
        | Some replacement -> apply substitution replacement
        | None -> row

let rec occurs (variable: RowVariable) (row: Row<'label>) : bool =
    match row with
    | Empty -> false
    | Extend(_, rest) -> occurs variable rest
    | Variable other -> variable = other

let private extendMany (labels: 'label list) (rest: Row<'label>) : Row<'label> =
    List.foldBack (fun label row -> Extend(label, row)) labels rest

let private flatten
    (substitution: RowSubstitution<'label>)
    (row: Row<'label>)
    : 'label list * Row<'label> =
    let rec loop (current: Row<'label>) (accumulated: 'label list) =
        match current with
        | Empty -> List.rev accumulated, Empty
        | Extend(label, rest) -> loop (apply substitution rest) (label :: accumulated)
        | Variable variable -> List.rev accumulated, Variable variable

    loop (apply substitution row) []

let private checkedLabels
    (row: Row<'label>)
    : Result<Set<'label>, RowError<'label>> =
    let rowLabels = labelsOf row
    let distinct = Set.ofList rowLabels

    if distinct.Count <> rowLabels.Length then
        let duplicate =
            rowLabels
            |> List.countBy id
            |> List.find (fun (_, count) -> count > 1)
            |> fst

        Error(DuplicateLabel duplicate)
    else
        Ok distinct

let private lacksFor
    (lacks: RowLacks<'label>)
    (variable: RowVariable)
    : Set<'label> =
    Map.tryFind variable lacks |> Option.defaultValue Set.empty

let private addLacksToRow
    (labels: Set<'label>)
    (row: Row<'label>)
    (lacks: RowLacks<'label>)
    : RowLacks<'label> =
    freeVariables row
    |> Set.fold
        (fun current variable ->
            Map.add variable (Set.union (lacksFor current variable) labels) current)
        lacks

let bindRow
    (variable: RowVariable)
    (row: Row<'label>)
    (substitution: RowSubstitution<'label>)
    (lacks: RowLacks<'label>)
    : Result<RowSubstitution<'label> * RowLacks<'label>, RowError<'label>> =
    if occurs variable row then
        Error(OccursCheck(variable, row))
    else
        let forbidden =
            Set.intersect (lacksFor lacks variable) (Set.ofList (labelsOf row))

        if not forbidden.IsEmpty then
            Error(LacksViolation(variable, forbidden))
        else
            let lacks =
                addLacksToRow
                    (lacksFor lacks variable)
                    row
                    (Map.remove variable lacks)

            Ok(Map.add variable row substitution, lacks)

/// Unifies two data rows with lacks constraints. A row variable may not be
/// bound to a row containing a label it lacks; the constraint propagates to
/// every free tail in the bound row.
let unifyWithLacks
    (context: string)
    (left: Row<'label>)
    (right: Row<'label>)
    (substitution: RowSubstitution<'label>)
    (lacks: RowLacks<'label>)
    (nextVariable: int)
    : Result<RowSubstitution<'label> * RowLacks<'label> * int, RowError<'label>> =
    let left = apply substitution left
    let right = apply substitution right
    let leftLabels, leftTail = flatten substitution left
    let rightLabels, rightTail = flatten substitution right

    match checkedLabels left, checkedLabels right with
    | Error error, _ -> Error error
    | _, Error error -> Error error
    | Ok leftSet, Ok rightSet ->
        let onlyLeft = Set.difference leftSet rightSet
        let onlyRight = Set.difference rightSet leftSet

        match onlyLeft.IsEmpty, onlyRight.IsEmpty, leftTail, rightTail with
        | true, true, Empty, Empty -> Ok(substitution, lacks, nextVariable)
        | true, true, Variable leftVariable, Variable rightVariable ->
            if leftVariable = rightVariable then
                Ok(substitution, lacks, nextVariable)
            else
                bindRow leftVariable (Variable rightVariable) substitution lacks
                |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable)
        | true, true, Variable variable, Empty
        | true, true, Empty, Variable variable ->
            bindRow variable Empty substitution lacks
            |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable)
        | false, true, _, Variable rightVariable ->
            let absorbed = extendMany (Set.toList onlyLeft) leftTail

            bindRow rightVariable absorbed substitution lacks
            |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable)
        | true, false, Variable leftVariable, _ ->
            let absorbed = extendMany (Set.toList onlyRight) rightTail

            bindRow leftVariable absorbed substitution lacks
            |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable)
        | false, false, Variable leftVariable, Variable rightVariable when leftVariable <> rightVariable ->
            let fresh = RowVariable nextVariable
            let leftAbsorbed = extendMany (Set.toList onlyRight) (Variable fresh)
            let rightAbsorbed = extendMany (Set.toList onlyLeft) (Variable fresh)

            bindRow leftVariable leftAbsorbed substitution lacks
            |> Result.bind (fun (substitution, lacks) ->
                bindRow rightVariable rightAbsorbed substitution lacks)
            |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable + 1)
        | _ -> Error(Mismatch(left, right, context))

/// Unifies two data rows without lacks constraints. This is the compatibility
/// path for callers that do not yet carry a lacks environment.
let unify
    (context: string)
    (left: Row<'label>)
    (right: Row<'label>)
    (substitution: RowSubstitution<'label>)
    (nextVariable: int)
    : Result<RowSubstitution<'label> * int, RowError<'label>> =
    unifyWithLacks context left right substitution emptyLacks nextVariable
    |> Result.map (fun (substitution, _, nextVariable) -> substitution, nextVariable)

let describe (labelText: 'label -> string) (row: Row<'label>) : string =
    let rowLabels = labelsOf row
    let rowTail = tail row
    let labelPart = rowLabels |> List.map labelText |> String.concat ", "

    match rowTail with
    | Empty -> "{" + labelPart + "}"
    | Variable(RowVariable variable) -> "{" + labelPart + " | r" + string variable + "}"
    | Extend _ -> "{" + labelPart + "}"
