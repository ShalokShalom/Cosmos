module UnificationHost

open TypeRetrieval
open Unification

let ofEffects (effects: TypeRetrieval.EffectSet) : LabelSets.LabelSet<string> = effects

let rec ofType (typeExpression: TypeRetrieval.Type) : Unification.TypeTerm =
    match typeExpression with
    | TypeRetrieval.Variable variable -> Unification.Variable variable
    | TypeRetrieval.Primitive name -> Unification.Primitive name
    | TypeRetrieval.Sort name -> Unification.Sort name
    | TypeRetrieval.Record row ->
        let fieldList =
            TypeRetrieval.recordFields row
            |> List.map (fun field -> field.Name, ofType field.Type)

        match Unification.recordOfList fieldList with
        | Ok convertedRow -> Unification.Record convertedRow
        | Error _ -> Unification.Record RowTypes.Empty
    | TypeRetrieval.Function(parameters, result, effects) ->
        Unification.Function(parameters |> List.map ofType, ofType result, ofEffects effects)
    | TypeRetrieval.Code(result, effects) ->
        Unification.Code(ofType result, ofEffects effects)

let rec toType (term: Unification.TypeTerm) : Result<TypeRetrieval.Type, string> =
    match term with
    | Unification.Variable variable -> Ok(TypeRetrieval.Variable variable)
    | Unification.Primitive name -> Ok(TypeRetrieval.Primitive name)
    | Unification.Sort name -> Ok(TypeRetrieval.Sort name)
    | Unification.Record row ->
        let rec convertFields (fields: Unification.RecordField list) =
            match fields with
            | [] -> Ok []
            | field :: rest ->
                toType field.Type
                |> Result.bind (fun convertedType ->
                    convertFields rest
                    |> Result.map (fun convertedRest ->
                        (field.Name, convertedType) :: convertedRest))

        convertFields (Unification.recordFields row)
        |> Result.bind (fun fields ->
            match RecordTypes.ofList fields with
            | Ok convertedRow -> Ok(TypeRetrieval.Record convertedRow)
            | Error reason -> Error reason)
    | Unification.Function(parameters, result, effects) ->
        let convertParameters =
            parameters
            |> List.fold
                (fun current parameter ->
                    current
                    |> Result.bind (fun accumulated ->
                        toType parameter
                        |> Result.map (fun converted -> accumulated @ [ converted ])))
                (Ok [])

        match effects with
        | LabelSets.Closed effectSet ->
            convertParameters
            |> Result.bind (fun convertedParameters ->
                toType result
                |> Result.map (fun convertedResult ->
                    TypeRetrieval.Function(
                        convertedParameters,
                        convertedResult,
                        LabelSets.Closed effectSet
                    )))
        | LabelSets.Open _ -> Error "open effect sets cannot be converted to the closed host type"
    | Unification.Code(result, effects) ->
        match effects with
        | LabelSets.Closed effectSet ->
            toType result
            |> Result.map (fun convertedResult ->
                TypeRetrieval.Code(convertedResult, LabelSets.Closed effectSet))
        | LabelSets.Open _ -> Error "open effect sets cannot be converted to the closed host type"

let unify
    (left: TypeRetrieval.Type)
    (right: TypeRetrieval.Type)
    : Result<TypeRetrieval.Type * TypeRetrieval.Type, string> =
    match Unification.unifyType (ofType left) (ofType right) Unification.emptyState with
    | Error error -> Error(Unification.describeError error)
    | Ok state ->
        let leftResult = Unification.applyType state (ofType left)
        let rightResult = Unification.applyType state (ofType right)

        match toType leftResult, toType rightResult with
        | Ok convertedLeft, Ok convertedRight -> Ok(convertedLeft, convertedRight)
        | Error reason, _ -> Error reason
        | _, Error reason -> Error reason

let agrees (left: TypeRetrieval.Type) (right: TypeRetrieval.Type) : bool =
    let hostSucceeded =
        match TypeRetrieval.unify left right TypeRetrieval.emptySubstitution with
        | Ok _ -> true
        | Error _ -> false

    let modelSucceeded =
        match unify left right with
        | Ok _ -> true
        | Error _ -> false

    hostSucceeded = modelSucceeded
