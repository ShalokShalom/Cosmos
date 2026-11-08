module WireCore

open System.Text.Json

/// Shared wire framing and validation helpers. Protocol-specific fields and
/// named errors stay in each wire module.
let sequence (results: Result<'T, 'Error> list) : Result<'T list, 'Error> =
    match results |> List.choose (function Error error -> Some error | Ok _ -> None) with
    | error :: _ -> Error error
    | [] -> Ok(results |> List.choose (function Ok value -> Some value | Error _ -> None))

let requiredString (element: JsonElement) (name: string) : Result<string, string> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
    | _ -> Error("Missing string field: " + name)

let requiredInt (element: JsonElement) (name: string) : Result<int, string> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Number ->
        match value.TryGetInt32() with
        | true, number -> Ok number
        | false, _ -> Error("Field is not a 32-bit integer: " + name)
    | _ -> Error("Missing integer field: " + name)

let requiredUInt64 (element: JsonElement) (name: string) : Result<uint64, string> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Number ->
        match value.TryGetUInt64() with
        | true, number -> Ok number
        | false, _ -> Error("Field is not an unsigned 64-bit integer: " + name)
    | _ -> Error("Missing unsigned integer field: " + name)

let arrayElements (element: JsonElement) (name: string) : Result<JsonElement list, string> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Array -> Ok(value.EnumerateArray() |> Seq.toList)
    | _ -> Error("Missing array field: " + name)
