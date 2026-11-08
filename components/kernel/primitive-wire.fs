module PrimitiveWire

// ============================================================
// The host primitive request and response wire format.
//
// A primitive receives its stack operands as tensors and returns its result
// tensors. The JSON is written with `Utf8JsonWriter` and read with
// `JsonDocument`, so the path is NativeAOT-safe and never uses reflection
// serialization. Non-finite tensor data is refused rather than handed to a
// writer that cannot represent it. Shared JSON validation is WireCore.
// ============================================================

open System
open System.Text
open System.Text.Json
open Types

let private finite (tensor: Tensor) : bool =
    tensor.Data |> Array.forall Double.IsFinite

let encodeRequest (word: string) (operands: Tensor list) : byte[] option =
    if not (operands |> List.forall finite) then
        None
    else
        let stream = new System.IO.MemoryStream()

        (use writer = new Utf8JsonWriter(stream)
         writer.WriteStartObject()
         writer.WriteString("word", word)
         writer.WriteStartArray("operands")

         for tensor in operands do
             writer.WriteStartObject()
             writer.WriteStartArray("shape")

             for dimension in tensor.Shape do
                 writer.WriteNumberValue dimension

             writer.WriteEndArray()
             writer.WriteStartArray("data")

             for value in tensor.Data do
                 writer.WriteNumberValue value

             writer.WriteEndArray()
             writer.WriteEndObject()

         writer.WriteEndArray()
         writer.WriteEndObject()
         writer.Flush())

        Some(stream.ToArray())

let private readShape (element: JsonElement) : Result<int[], string> =
    match element.TryGetProperty "shape" with
    | true, shape when shape.ValueKind = JsonValueKind.Array ->
        try
            let dimensions =
                shape.EnumerateArray()
                |> Seq.map (fun item ->
                    match item.TryGetInt32() with
                    | true, value when value >= 0 -> value
                    | _ -> invalidArg "shape" "a shape dimension is not a non-negative integer")
                |> Seq.toArray

            Ok dimensions
        with exn ->
            Error exn.Message
    | _ -> Error "a primitive result has no shape array"

let private readData (element: JsonElement) : Result<double[], string> =
    match element.TryGetProperty "data" with
    | true, data when data.ValueKind = JsonValueKind.Array ->
        try
            let values =
                data.EnumerateArray()
                |> Seq.map (fun item ->
                    match item.TryGetDouble() with
                    | true, value -> value
                    | false, _ -> invalidArg "data" "a primitive result value is not numeric")
                |> Seq.toArray

            Ok values
        with exn ->
            Error exn.Message
    | _ -> Error "a primitive result has no data array"

let private readTensor (element: JsonElement) : Result<Tensor, string> =
    match readShape element, readData element with
    | Error error, _ -> Error error
    | _, Error error -> Error error
    | Ok shape, Ok data ->
        Tensor.reshape shape (Tensor.vector (List.ofArray data))
        |> Result.mapError (fun reason -> "a primitive result has an invalid shape: " + reason)

let decodeResults (expected: int) (bytes: byte[]) : Result<Tensor list, string> =
    if expected = 0 && bytes.Length = 0 then
        Ok []
    else
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement

            match root.TryGetProperty "results" with
            | false, _ -> Error "a primitive response has no results array"
            | true, results when results.ValueKind = JsonValueKind.Array ->
                let tensors = results.EnumerateArray() |> Seq.toList |> List.map readTensor

                match WireCore.sequence tensors with
                | Error error -> Error error
                | Ok values when values.Length = expected -> Ok values
                | Ok values ->
                    Error("a primitive returned " + string values.Length + " results; expected " + string expected)
            | _ -> Error "a primitive response results field is not an array"
        with exn ->
            Error exn.Message
