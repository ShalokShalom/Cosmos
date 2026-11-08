module ClassificationWire

open System.Text.Json

/// The IPC-facing F# classification metadata. The payload is deliberately
/// closed: a name, a kind, and the metadata version. A body is never allowed.
type MetadataError =
    | MetadataJson of string
    | MetadataSchema of int
    | MetadataFieldMissing of string
    | MetadataFieldInvalid of string * string
    | UnknownMetadataKind of string
    | MetadataVersionMismatch of int
    | BodyNotAllowed
    | DuplicateName of string

let describeError (error: MetadataError) : string =
    match error with
    | MetadataJson reason -> "classification metadata: invalid JSON: " + reason
    | MetadataSchema version -> "classification metadata: unsupported schema " + string version
    | MetadataFieldMissing name -> "classification metadata: missing field " + name
    | MetadataFieldInvalid(name, reason) -> "classification metadata: field " + name + " is " + reason
    | UnknownMetadataKind kind -> "classification metadata: unknown kind " + kind
    | MetadataVersionMismatch version -> "classification metadata: version " + string version + " does not match " + string FSharpAbstraction.metadataVersion
    | BodyNotAllowed -> "classification metadata: a function body is not allowed"
    | DuplicateName name -> "classification metadata: duplicate binding " + name

let private knownKind (kind: string) : bool =
    match kind with
    | "non-recursive-function"
    | "recursive-function"
    | "mutually-recursive-function"
    | "non-recursive-value"
    | "recursive-value" -> true
    | _ -> false

let encode (bindings: FSharpAbstraction.BindingMetadata list) : string =
    let stream = new System.IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     writer.WriteStartObject()
     writer.WriteNumber("schemaVersion", FSharpAbstraction.metadataVersion)
     writer.WriteStartArray("bindings")

     for binding in bindings do
         writer.WriteStartObject()
         writer.WriteString("name", binding.Name)
         writer.WriteString("kind", binding.Kind)
         writer.WriteNumber("version", binding.Version)
         writer.WriteEndObject()

     writer.WriteEndArray()
     writer.WriteEndObject()
     writer.Flush())

    System.Text.Encoding.UTF8.GetString(stream.ToArray())

let private requiredString (element: JsonElement) (name: string) : Result<string, MetadataError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
    | true, _ -> Error(MetadataFieldInvalid(name, "not a string"))
    | false, _ -> Error(MetadataFieldMissing name)

let private requiredInt (element: JsonElement) (name: string) : Result<int, MetadataError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Number ->
        match value.TryGetInt32() with
        | true, number -> Ok number
        | false, _ -> Error(MetadataFieldInvalid(name, "not an integer"))
    | true, _ -> Error(MetadataFieldInvalid(name, "not a number"))
    | false, _ -> Error(MetadataFieldMissing name)

let private readBinding (element: JsonElement) : Result<FSharpAbstraction.BindingMetadata, MetadataError> =
    if element.TryGetProperty "body" |> fst then
        Error BodyNotAllowed
    else
        match requiredString element "name" with
        | Error error -> Error error
        | Ok name ->
            match requiredString element "kind" with
            | Error error -> Error error
            | Ok kind when not (knownKind kind) -> Error(UnknownMetadataKind kind)
            | Ok kind ->
                match requiredInt element "version" with
                | Error error -> Error error
                | Ok version when version <> FSharpAbstraction.metadataVersion ->
                    Error(MetadataVersionMismatch version)
                | Ok version ->
                    Ok
                        ({ Name = name
                           Kind = kind
                           Version = version } : FSharpAbstraction.BindingMetadata)

let decode (text: string) : Result<FSharpAbstraction.BindingMetadata list, MetadataError> =
    try
        use document = JsonDocument.Parse text
        let root = document.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error(MetadataFieldInvalid("metadata", "not an object"))
        else
            match requiredInt root "schemaVersion" with
            | Error error -> Error error
            | Ok schema when schema <> FSharpAbstraction.metadataVersion -> Error(MetadataSchema schema)
            | Ok _ ->
                match root.TryGetProperty "bindings" with
                | true, bindings when bindings.ValueKind = JsonValueKind.Array ->
                    bindings.EnumerateArray()
                    |> Seq.map readBinding
                    |> Seq.fold
                        (fun accumulated item ->
                            accumulated
                            |> Result.bind (fun current ->
                                item
                                |> Result.bind (fun binding ->
                                    if current |> List.exists (fun candidate -> candidate.Name = binding.Name) then
                                        Error(DuplicateName binding.Name)
                                    else
                                        Ok(current @ [ binding ]))))
                        (Ok [])
                | true, _ -> Error(MetadataFieldInvalid("bindings", "not an array"))
                | false, _ -> Error(MetadataFieldMissing "bindings")
    with error ->
        Error(MetadataJson error.Message)
