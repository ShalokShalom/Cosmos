module RecordTypes

open RowTypes

[<CustomEquality; CustomComparison>]
type RecordField<'fieldType> =
    {
        Name: string
        Type: 'fieldType
    }

    override this.Equals(other: obj) =
        match other with
        | :? RecordField<'fieldType> as field -> this.Name = field.Name
        | _ -> false

    override this.GetHashCode() = hash this.Name

    interface System.IComparable with
        member this.CompareTo(other: obj) =
            match other with
            | :? RecordField<'fieldType> as field -> compare this.Name field.Name
            | _ -> invalidArg "other" "a RecordField is required"

let ofList
    (fields: (string * 'fieldType) list)
    : Result<Row<RecordField<'fieldType>>, string> =
    fields
    |> List.map (fun (name, fieldType) -> { Name = name; Type = fieldType })
    |> RowTypes.ofList
    |> Result.mapError (fun error ->
        match error with
        | DuplicateLabel field -> "duplicate record field: " + field.Name
        | OccursCheck _ -> "record row occurs check"
        | Mismatch _ -> "record row mismatch")

let fields (row: Row<RecordField<'fieldType>>) : RecordField<'fieldType> list =
    RowTypes.labelsOf row

let tail (row: Row<RecordField<'fieldType>>) : Row<RecordField<'fieldType>> =
    RowTypes.tail row

let rec map
    (transform: 'fieldType -> 'fieldType)
    (row: Row<RecordField<'fieldType>>)
    : Row<RecordField<'fieldType>> =
    match row with
    | Empty -> Empty
    | Extend(field, rest) ->
        Extend({ field with Type = transform field.Type }, map transform rest)
    | Variable variable -> Variable variable
