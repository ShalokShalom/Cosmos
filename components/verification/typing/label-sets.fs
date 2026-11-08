module LabelSets

/// A set variable stands for an unknown set of labels. It is nominal, so it
/// cannot be confused with a row variable.
type SetVariable = SetVariable of int

/// A set-theoretic type: a known finite set of labels, optionally with an
/// unknown remainder. There is no order and no duplicate label; the
/// representation is a set, not a row.
type LabelSet<'label when 'label : comparison> =
    | Closed of labels: Set<'label>
    | Open of known: Set<'label> * variable: SetVariable

type SetSubstitution<'label when 'label : comparison> = Map<SetVariable, LabelSet<'label>>

type SetLacks<'label when 'label : comparison> = Map<SetVariable, Set<'label>>

type SetError<'label when 'label : comparison> =
    | OccursCheck of variable: SetVariable * set: LabelSet<'label>
    | Mismatch of left: LabelSet<'label> * right: LabelSet<'label> * context: string
    | OpenSubtraction of set: LabelSet<'label>
    | LacksViolation of variable: SetVariable * labels: Set<'label>

let emptyLacks<'label when 'label : comparison> : SetLacks<'label> = Map.empty

let empty: LabelSet<'label> = Closed Set.empty

let closed (labels: Set<'label>) : LabelSet<'label> = Closed labels

let openSet (labels: Set<'label>) (variable: SetVariable) : LabelSet<'label> =
    Open(labels, variable)

let singleton (label: 'label) : LabelSet<'label> = Closed(Set.singleton label)

let known (set: LabelSet<'label>) : Set<'label> =
    match set with
    | Closed labels -> labels
    | Open(labels, _) -> labels

let freeVariables (set: LabelSet<'label>) : Set<SetVariable> =
    match set with
    | Closed _ -> Set.empty
    | Open(_, variable) -> Set.singleton variable

let rec apply (substitution: SetSubstitution<'label>) (set: LabelSet<'label>) : LabelSet<'label> =
    match set with
    | Closed _ -> set
    | Open(labels, variable) ->
        match Map.tryFind variable substitution with
        | None -> set
        | Some replacement ->
            match apply substitution replacement with
            | Closed replacementLabels -> Closed(Set.union labels replacementLabels)
            | Open(replacementLabels, replacementVariable) ->
                Open(Set.union labels replacementLabels, replacementVariable)

let occurs (variable: SetVariable) (set: LabelSet<'label>) : bool =
    freeVariables set |> Set.contains variable

let extend (label: 'label) (set: LabelSet<'label>) : LabelSet<'label> =
    match set with
    | Closed labels -> Closed(Set.add label labels)
    | Open(labels, variable) -> Open(Set.add label labels, variable)

/// Removes known labels from a closed effect set. An open set needs a lacks
/// constraint to remove a label from its unknown remainder, so it is refused
/// rather than approximated.
let subtractClosed
    (removed: Set<'label>)
    (effects: LabelSet<'label>)
    : Result<LabelSet<'label>, SetError<'label>> =
    match effects with
    | Closed labels -> Ok(Closed(Set.difference labels removed))
    | Open _ -> Error(OpenSubtraction effects)

let private lacksFor
    (lacks: SetLacks<'label>)
    (variable: SetVariable)
    : Set<'label> =
    Map.tryFind variable lacks |> Option.defaultValue Set.empty

let private addLacksToSet
    (labels: Set<'label>)
    (set: LabelSet<'label>)
    (lacks: SetLacks<'label>)
    : SetLacks<'label> =
    freeVariables set
    |> Set.fold
        (fun current variable ->
            Map.add variable (Set.union (lacksFor current variable) labels) current)
        lacks

let bindSet
    (variable: SetVariable)
    (set: LabelSet<'label>)
    (substitution: SetSubstitution<'label>)
    (lacks: SetLacks<'label>)
    : Result<SetSubstitution<'label> * SetLacks<'label>, SetError<'label>> =
    if occurs variable set then
        Error(OccursCheck(variable, set))
    else
        let forbidden = Set.intersect (lacksFor lacks variable) (known set)

        if not forbidden.IsEmpty then
            Error(LacksViolation(variable, forbidden))
        else
            let lacks =
                addLacksToSet
                    (lacksFor lacks variable)
                    set
                    (Map.remove variable lacks)

            Ok(Map.add variable set substitution, lacks)

/// Removes labels from an open effect set by adding a lacks constraint to its
/// unknown remainder. Closed subtraction is unchanged.
let subtractOpen
    (removed: Set<'label>)
    (effects: LabelSet<'label>)
    (lacks: SetLacks<'label>)
    : Result<LabelSet<'label> * SetLacks<'label>, SetError<'label>> =
    match effects with
    | Closed labels -> Ok(Closed(Set.difference labels removed), lacks)
    | Open(labels, variable) ->
        let lacks =
            Map.add variable (Set.union (lacksFor lacks variable) removed) lacks

        Ok(Open(Set.difference labels removed, variable), lacks)

/// Unifies two set expressions with lacks constraints. A set variable may not
/// be bound to a set containing a label it lacks; the constraint propagates to
/// every free remainder in the bound set.
let unifyWithLacks
    (context: string)
    (left: LabelSet<'label>)
    (right: LabelSet<'label>)
    (substitution: SetSubstitution<'label>)
    (lacks: SetLacks<'label>)
    (nextVariable: int)
    : Result<SetSubstitution<'label> * SetLacks<'label> * int, SetError<'label>> =
    let left = apply substitution left
    let right = apply substitution right

    match left, right with
    | Closed leftLabels, Closed rightLabels ->
        if leftLabels = rightLabels then
            Ok(substitution, lacks, nextVariable)
        else
            Error(Mismatch(left, right, context))
    | Closed leftLabels, Open(rightLabels, rightVariable) ->
        if Set.isSubset rightLabels leftLabels then
            bindSet rightVariable (Closed(Set.difference leftLabels rightLabels)) substitution lacks
            |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable)
        else
            Error(Mismatch(left, right, context))
    | Open(leftLabels, leftVariable), Closed rightLabels ->
        if Set.isSubset leftLabels rightLabels then
            bindSet leftVariable (Closed(Set.difference rightLabels leftLabels)) substitution lacks
            |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable)
        else
            Error(Mismatch(left, right, context))
    | Open(leftLabels, leftVariable), Open(rightLabels, rightVariable) ->
        if leftVariable = rightVariable then
            if leftLabels = rightLabels then
                Ok(substitution, lacks, nextVariable)
            else
                Error(Mismatch(left, right, context))
        else
            let fresh = SetVariable nextVariable
            let leftOnly = Set.difference rightLabels leftLabels
            let rightOnly = Set.difference leftLabels rightLabels
            let leftBinding = Open(leftOnly, fresh)
            let rightBinding = Open(rightOnly, fresh)

            bindSet leftVariable leftBinding substitution lacks
            |> Result.bind (fun (substitution, lacks) ->
                bindSet rightVariable rightBinding substitution lacks)
            |> Result.map (fun (substitution, lacks) -> substitution, lacks, nextVariable + 1)

/// Unifies two set expressions without lacks constraints. This is the
/// compatibility path for callers that do not yet carry a lacks environment.
let unify
    (context: string)
    (left: LabelSet<'label>)
    (right: LabelSet<'label>)
    (substitution: SetSubstitution<'label>)
    (nextVariable: int)
    : Result<SetSubstitution<'label> * int, SetError<'label>> =
    unifyWithLacks context left right substitution emptyLacks nextVariable
    |> Result.map (fun (substitution, _, nextVariable) -> substitution, nextVariable)

let describe (labelText: 'label -> string) (set: LabelSet<'label>) : string =
    let labels = known set |> Set.toList |> List.map labelText |> String.concat ", "

    match set with
    | Closed _ -> "{" + labels + "}"
    | Open(_, SetVariable variable) -> "{" + labels + " | s" + string variable + "}"
