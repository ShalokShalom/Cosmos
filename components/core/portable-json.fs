module PortableJson

// ============================================================
// Shared JSON plumbing for the portable pattern, expression, and package
// documents. The error constructors stay with each document; the common
// validation is WireCore, and these names preserve the document-local surface.
// ============================================================

open System.Text.Json

let sequence = WireCore.sequence

let writeArray
    (writer: Utf8JsonWriter)
    (name: string)
    (items: 'T list)
    (writeItem: Utf8JsonWriter -> 'T -> unit)
    : unit =
    writer.WriteStartArray name

    for item in items do
        writeItem writer item

    writer.WriteEndArray()

let requiredString = WireCore.requiredString
let requiredInt = WireCore.requiredInt
let arrayElements = WireCore.arrayElements
