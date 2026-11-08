module TermPortable

open System
open System.IO
open System.Text.Json
open SymbolicAbstraction

type Error =
    | InvalidJson
    | InvalidSchema of int
    | InvalidForm of string
    | MissingField of string
    | WrongType of string
    | UnknownTerm of string
    | NegativeSlot of int
    | NegativeCapture of int
    | NegativeArity of int
    | NonFiniteNumber
    | EncodeFailed of string

let describeError (error: Error) : string =
    match error with
    | InvalidJson -> "term document: invalid JSON"
    | InvalidSchema schema -> $"term document: unsupported schema {schema}"
    | InvalidForm form -> $"term document: unsupported form {form}"
    | MissingField name -> $"term document: missing field {name}"
    | WrongType name -> $"term document: field {name} has the wrong type"
    | UnknownTerm term -> $"term document: unknown term {term}"
    | NegativeSlot slot -> $"term document: negative slot {slot}"
    | NegativeCapture capture -> $"term document: negative capture {capture}"
    | NegativeArity arity -> $"term document: negative arity {arity}"
    | NonFiniteNumber -> "term document: a number is not finite"
    | EncodeFailed reason -> "term document: encode failed: " + reason

let rec private validateTerm (term: Term) : Result<unit, Error> =
    match term with
    | LiteralNumber value when not (Double.IsFinite value) -> Error NonFiniteNumber
    | Quote terms -> validateTerms terms
    | Branch(condition, thenTerm, elseTerm) -> validateTerms [ condition; thenTerm; elseTerm ]
    | SlotGet slot
    | SlotSet slot when slot < 0 -> Error(NegativeSlot slot)
    | MakeClosure(_, captures, arity) ->
        match captures |> List.tryFind (fun capture -> capture < 0) with
        | Some capture -> Error(NegativeCapture capture)
        | None when arity < 0 -> Error(NegativeArity arity)
        | None -> Ok()
    | _ -> Ok()

and private validateTerms (terms: Term list) : Result<unit, Error> =
    terms
    |> List.fold
        (fun state term ->
            match state with
            | Error error -> Error error
            | Ok() -> validateTerm term)
        (Ok())

let rec private writeTerm (writer: Utf8JsonWriter) (term: Term) : unit =
    writer.WriteStartObject()

    match term with
    | LiteralNumber value ->
        writer.WriteString("term", "number")
        writer.WriteNumber("value", value)
    | LiteralText value ->
        writer.WriteString("term", "text")
        writer.WriteString("value", value)
    | LiteralAtom value ->
        writer.WriteString("term", "atom")
        writer.WriteString("value", value)
    | Word name ->
        writer.WriteString("term", "word")
        writer.WriteString("name", name)
    | Quote terms ->
        writer.WriteString("term", "quote")
        writer.WritePropertyName("terms")
        writeTerms writer terms
    | Branch(condition, thenTerm, elseTerm) ->
        writer.WriteString("term", "branch")
        writer.WritePropertyName("condition")
        writeTerm writer condition
        writer.WritePropertyName("then")
        writeTerm writer thenTerm
        writer.WritePropertyName("else")
        writeTerm writer elseTerm
    | SlotGet slot ->
        writer.WriteString("term", "slot-get")
        writer.WriteNumber("slot", slot)
    | SlotSet slot ->
        writer.WriteString("term", "slot-set")
        writer.WriteNumber("slot", slot)
    | MakeClosure(name, captures, arity) ->
        writer.WriteString("term", "make-closure")
        writer.WriteString("name", name)
        writer.WritePropertyName("captures")
        writer.WriteStartArray()
        captures |> List.iter writer.WriteNumberValue
        writer.WriteEndArray()
        writer.WriteNumber("arity", arity)
    | CallClosure -> writer.WriteString("term", "call-closure")
    | Fail reason ->
        writer.WriteString("term", "fail")
        writer.WriteString("reason", reason)

    writer.WriteEndObject()

and private writeTerms (writer: Utf8JsonWriter) (terms: Term list) : unit =
    writer.WriteStartArray()
    terms |> List.iter (writeTerm writer)
    writer.WriteEndArray()

let encode (program: TermProgram) : Result<byte[], Error> =
    if program.SchemaVersion <> schemaVersion then
        Error(InvalidSchema program.SchemaVersion)
    elif program.Form <> form then
        Error(InvalidForm program.Form)
    else
        match validateTerms program.Terms with
        | Error error -> Error error
        | Ok() ->
            try
                let stream = new MemoryStream()

                try
                    let writer = new Utf8JsonWriter(stream)

                    try
                        writer.WriteStartObject()
                        writer.WriteNumber("schema", program.SchemaVersion)
                        writer.WriteString("form", program.Form)
                        writer.WritePropertyName("terms")
                        writeTerms writer program.Terms
                        writer.WriteEndObject()
                        writer.Flush()
                    finally
                        writer.Dispose()

                    Ok(stream.ToArray())
                with ex ->
                    Error(EncodeFailed ex.Message)
            with ex ->
                Error(EncodeFailed ex.Message)

let private tryProperty (name: string) (element: JsonElement) : Result<JsonElement, Error> =
    let mutable value = Unchecked.defaultof<JsonElement>

    if element.TryGetProperty(name, &value) then
        Ok value
    else
        Error(MissingField name)

let private readInt (name: string) (element: JsonElement) : Result<int, Error> =
    match tryProperty name element with
    | Error error -> Error error
    | Ok value ->
        match value.ValueKind with
        | JsonValueKind.Number ->
            let mutable number = 0

            if value.TryGetInt32(&number) then
                Ok number
            else
                Error(WrongType name)
        | _ -> Error(WrongType name)

let private readDouble (name: string) (element: JsonElement) : Result<double, Error> =
    match tryProperty name element with
    | Error error -> Error error
    | Ok value ->
        match value.ValueKind with
        | JsonValueKind.Number ->
            let mutable number = 0.0

            if value.TryGetDouble(&number) then
                if Double.IsFinite number then
                    Ok number
                else
                    Error NonFiniteNumber
            else
                Error(WrongType name)
        | _ -> Error(WrongType name)

let private readString (name: string) (element: JsonElement) : Result<string, Error> =
    match tryProperty name element with
    | Error error -> Error error
    | Ok value ->
        match value.ValueKind with
        | JsonValueKind.String -> Ok(value.GetString())
        | _ -> Error(WrongType name)

let private readSlot (name: string) (element: JsonElement) : Result<int, Error> =
    match readInt name element with
    | Error error -> Error error
    | Ok slot when slot < 0 -> Error(NegativeSlot slot)
    | Ok slot -> Ok slot

let private readArity (name: string) (element: JsonElement) : Result<int, Error> =
    match readInt name element with
    | Error error -> Error error
    | Ok arity when arity < 0 -> Error(NegativeArity arity)
    | Ok arity -> Ok arity

let private readCaptures (name: string) (element: JsonElement) : Result<int list, Error> =
    match tryProperty name element with
    | Error error -> Error error
    | Ok value when value.ValueKind = JsonValueKind.Array ->
        value.EnumerateArray()
        |> Seq.fold
            (fun state item ->
                match state with
                | Error error -> Error error
                | Ok captures ->
                    match item.ValueKind with
                    | JsonValueKind.Number ->
                        let mutable capture = 0

                        if item.TryGetInt32(&capture) then
                            if capture < 0 then
                                Error(NegativeCapture capture)
                            else
                                Ok(captures @ [ capture ])
                        else
                            Error(WrongType name)
                    | _ -> Error(WrongType name))
            (Ok [])
    | Ok _ -> Error(WrongType name)

let rec private decodeTerm (element: JsonElement) : Result<Term, Error> =
    if element.ValueKind <> JsonValueKind.Object then
        Error(WrongType "term")
    else
        match readString "term" element with
        | Error error -> Error error
        | Ok tag ->
            match tag with
            | "number" -> readDouble "value" element |> Result.map LiteralNumber
            | "text" -> readString "value" element |> Result.map LiteralText
            | "atom" -> readString "value" element |> Result.map LiteralAtom
            | "word" -> readString "name" element |> Result.map Word
            | "quote" -> readTerms "terms" element |> Result.map Quote
            | "branch" ->
                match readTermProperty "condition" element with
                | Error error -> Error error
                | Ok condition ->
                    match readTermProperty "then" element with
                    | Error error -> Error error
                    | Ok thenTerm ->
                        readTermProperty "else" element
                        |> Result.map (fun elseTerm -> Branch(condition, thenTerm, elseTerm))
            | "slot-get" -> readSlot "slot" element |> Result.map SlotGet
            | "slot-set" -> readSlot "slot" element |> Result.map SlotSet
            | "make-closure" ->
                match readString "name" element with
                | Error error -> Error error
                | Ok name ->
                    match readCaptures "captures" element with
                    | Error error -> Error error
                    | Ok captures ->
                        match readArity "arity" element with
                        | Error error -> Error error
                        | Ok arity -> Ok(MakeClosure(name, captures, arity))
            | "call-closure" -> Ok CallClosure
            | "fail" -> readString "reason" element |> Result.map Fail
            | other -> Error(UnknownTerm other)

and private readTermProperty
    (name: string)
    (element: JsonElement)
    : Result<Term, Error> =
    match tryProperty name element with
    | Error error -> Error error
    | Ok value -> decodeTerm value

and private decodeTerms (element: JsonElement) : Result<Term list, Error> =
    element.EnumerateArray()
    |> Seq.fold
        (fun state item ->
            match state with
            | Error error -> Error error
            | Ok terms -> decodeTerm item |> Result.map (fun term -> terms @ [ term ]))
        (Ok [])

and private readTerms (name: string) (element: JsonElement) : Result<Term list, Error> =
    match tryProperty name element with
    | Error error -> Error error
    | Ok value when value.ValueKind = JsonValueKind.Array -> decodeTerms value
    | Ok _ -> Error(WrongType name)

let decode (bytes: byte[]) : Result<TermProgram, Error> =
    try
        use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes))
        let root = document.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error(WrongType "root")
        else
            match readInt "schema" root with
            | Error error -> Error error
            | Ok schema ->
                if schema <> schemaVersion then
                    Error(InvalidSchema schema)
                else
                    match readString "form" root with
                    | Error error -> Error error
                    | Ok formText ->
                        if formText <> form then
                            Error(InvalidForm formText)
                        else
                            match readTerms "terms" root with
                            | Error error -> Error error
                            | Ok terms ->
                                Ok
                                    {
                                        SchemaVersion = schema
                                        Form = formText
                                        Terms = terms
                                    }
    with :? JsonException ->
        Error InvalidJson
