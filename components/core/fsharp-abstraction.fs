module FSharpAbstraction

open System.Text.Json
open ExpressionLogic
open PackageLogic

/// The F# recursion classification read from the source layer. The abstraction
/// carries the classification; the runtime never calls FCS.
type FSharpRecursionKind =
    | NonRecursive
    | Recursive
    | MutuallyRecursive of string list

type FSharpBinding =
    {
        Name: string
        Parameters: string list
        Body: Expression
        Kind: FSharpRecursionKind
        Calls: string list
        Range: SourceRange
    }

/// Small versioned representation of the F# subset consumed by the Prelude and
/// F# packages. It is not a second runtime: `toPackageDocument` lowers it onto
/// the same unit/group substrate as books and `logic`/`cycle` words.
type FSharpAbstraction =
    {
        SchemaVersion: int
        Module: string
        Version: string
        SourceHash: string
        Bindings: FSharpBinding list
    }

/// The classification-only output of the build-time FCS adapter. A hand port
/// supplies the expression bodies; the adapter never fabricates a body it did
/// not read.
type ReadOffBinding =
    {
        Name: string
        Parameters: string list
        Kind: FSharpRecursionKind
        Calls: string list
        Range: SourceRange
        Body: Expression option
    }

type ReadOffModule =
    {
        SchemaVersion: int
        Module: string
        Bindings: ReadOffBinding list
    }

type ReadOffError =
    | UnsupportedReadOffSchema of int
    | ReadOffJson of string
    | ReadOffFieldMissing of string
    | ReadOffFieldInvalid of string * string
    | UnknownReadOffKind of string
    | ReadOffBody of string
    | MissingBody of string

/// A reason for an explicit manifest override. `Other` keeps an unfamiliar
/// reason auditable instead of silently accepting it.
type DeclaredReason =
    | UninitializedValue
    | CrossModule
    | ValueRestriction
    | Other of string

type ManifestBinding =
    {
        Name: string
        Kind: FSharpRecursionKind
        Reason: DeclaredReason option
    }

type ManifestMode =
    | Automatic
    | Mixed
    | Strict

type Manifest =
    {
        SchemaVersion: int
        Package: string
        Mode: ManifestMode
        Bindings: ManifestBinding list
    }

type ManifestError =
    | InvalidManifestJson of string
    | UnsupportedManifestSchema of int
    | ManifestFieldMissing of string
    | ManifestFieldInvalid of string * string
    | UnknownManifestKind of string
    | UnknownManifestReason of string

type ClassificationDisagreement =
    {
        Name: string
        FSharp: FSharpRecursionKind
        Declared: FSharpRecursionKind
        Reason: DeclaredReason option
    }

type ClassificationError =
    | AbstractionSchemaMismatch of int
    | ManifestSchemaMismatch of int
    | DuplicateBinding of string
    | ManifestPackageMismatch of string * string
    | UnknownManifestBinding of string
    | MissingManifestBinding of string
    | ManifestDisagreement of ClassificationDisagreement

type ClassifiedBinding =
    {
        Name: string
        Kind: BindingKind
        DeclaredReason: DeclaredReason option
    }

type ClassificationResult =
    {
        Bindings: ClassifiedBinding list
        Overrides: ClassificationDisagreement list
    }

type FSharpDiagnostic =
    {
        Code: string
        Message: string
        Range: SourceRange
    }

type ScaleDiagnosticCode =
    | RecursiveValueWithoutInstantiation
    | UnusedRecursiveMarker
    | ValueRestriction
    | OtherFSharpDiagnostic of string

type ScaleDiagnostic =
    {
        Code: ScaleDiagnosticCode
        Message: string
        Range: SourceRange
    }

type BindingMetadata =
    {
        Name: string
        Kind: string
        Version: int
    }

let abstractionSchemaVersion = 1
let manifestSchemaVersion = 1
let readOffSchemaVersion = 1
let metadataVersion = 1

let describeKind (kind: FSharpRecursionKind) : string =
    match kind with
    | NonRecursive -> "non-recursive"
    | Recursive -> "recursive"
    | MutuallyRecursive names -> "mutually-recursive(" + String.concat "," names + ")"

let private describeBindingKind (kind: BindingKind) : string =
    match kind with
    | NonRecursiveFunction -> "non-recursive-function"
    | RecursiveFunction -> "recursive-function"
    | MutuallyRecursiveFunction _ -> "mutually-recursive-function"
    | NonRecursiveValue -> "non-recursive-value"
    | RecursiveValue -> "recursive-value"

let private invalid (field: string) (value: string) : Result<'a, ManifestError> =
    Error(ManifestFieldInvalid(field, value))

let private requiredString (element: JsonElement) (name: string) : Result<string, ManifestError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
    | true, _ -> invalid name "not a string"
    | false, _ -> Error(ManifestFieldMissing name)

let private requiredInt (element: JsonElement) (name: string) : Result<int, ManifestError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Number ->
        match value.TryGetInt32() with
        | true, number -> Ok number
        | false, _ -> invalid name "not an integer"
    | true, _ -> invalid name "not a number"
    | false, _ -> Error(ManifestFieldMissing name)

let private kindOfString (text: string) : Result<FSharpRecursionKind, ManifestError> =
    match text with
    | "non-recursive" -> Ok NonRecursive
    | "recursive" -> Ok Recursive
    | "mutually-recursive" -> Ok(MutuallyRecursive [])
    | other -> Error(UnknownManifestKind other)

let private reasonOfString (text: string) : Result<DeclaredReason, ManifestError> =
    match text with
    | "uninitialized-value" -> Ok UninitializedValue
    | "cross-module" -> Ok CrossModule
    | "value-restriction" -> Ok DeclaredReason.ValueRestriction
    | value when value.StartsWith("other:", System.StringComparison.Ordinal) ->
        Ok(Other(value.Substring("other:".Length).Trim()))
    | other -> Error(UnknownManifestReason other)

let private readStringArray (element: JsonElement) (name: string) : Result<string list, ManifestError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Array ->
        value.EnumerateArray()
        |> Seq.map (fun item ->
            if item.ValueKind = JsonValueKind.String then
                Ok(item.GetString())
            else
                invalid name "an item is not a string")
        |> Seq.fold
            (fun accumulated item ->
                accumulated
                |> Result.bind (fun current -> item |> Result.map (fun value -> current @ [ value ])))
            (Ok [])
    | true, _ -> invalid name "not an array"
    | false, _ -> Error(ManifestFieldMissing name)

let private readKind (element: JsonElement) : Result<FSharpRecursionKind, ManifestError> =
    match requiredString element "kind" with
    | Error error -> Error error
    | Ok "mutually-recursive" ->
        readStringArray element "mutual" |> Result.map MutuallyRecursive
    | Ok text -> kindOfString text

let private readBinding (element: JsonElement) : Result<ManifestBinding, ManifestError> =
    match requiredString element "name" with
    | Error error -> Error error
    | Ok name ->
        match readKind element with
        | Error error -> Error error
        | Ok kind ->
            let reason =
                match element.TryGetProperty "reason" with
                | true, value when value.ValueKind = JsonValueKind.String ->
                    reasonOfString (value.GetString()) |> Result.map Some
                | true, value ->
                    invalid "reason" (value.ValueKind.ToString())
                | false, _ -> Ok None

            reason |> Result.map (fun reason -> { Name = name; Kind = kind; Reason = reason })

let private readMode (element: JsonElement) : Result<ManifestMode, ManifestError> =
    match requiredString element "mode" with
    | Ok "automatic" -> Ok Automatic
    | Ok "mixed" -> Ok Mixed
    | Ok "strict" -> Ok Strict
    | Ok other -> invalid "mode" other
    | Error error -> Error error

let parseManifest (text: string) : Result<Manifest, ManifestError> =
    try
        use document = JsonDocument.Parse text
        let root = document.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error(ManifestFieldInvalid("manifest", "not an object"))
        else
            match requiredInt root "schemaVersion" with
            | Error error -> Error error
            | Ok schema when schema <> manifestSchemaVersion ->
                Error(UnsupportedManifestSchema schema)
            | Ok schema ->
                match requiredString root "package" with
                | Error error -> Error error
                | Ok package ->
                        match readMode root with
                        | Error error -> Error error
                        | Ok mode ->
                            match root.TryGetProperty "bindings" with
                            | true, bindings when bindings.ValueKind = JsonValueKind.Array ->
                                bindings.EnumerateArray()
                                |> Seq.map readBinding
                                |> Seq.fold
                                    (fun accumulated item ->
                                        accumulated
                                        |> Result.bind (fun current ->
                                            item |> Result.map (fun binding -> current @ [ binding ])))
                                    (Ok [])
                                |> Result.map (fun bindings ->
                                    {
                                        SchemaVersion = schema
                                        Package = package
                                        Mode = mode
                                        Bindings = bindings
                                    })
                            | true, _ -> invalid "bindings" "not an array"
                            | false, _ -> Error(ManifestFieldMissing "bindings")
    with error ->
        Error(InvalidManifestJson error.Message)

let private readOffString (element: JsonElement) (name: string) : Result<string, ReadOffError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
    | true, _ -> Error(ReadOffFieldInvalid(name, "not a string"))
    | false, _ -> Error(ReadOffFieldMissing name)

let private readOffInt (element: JsonElement) (name: string) : Result<int, ReadOffError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Number ->
        match value.TryGetInt32() with
        | true, number -> Ok number
        | false, _ -> Error(ReadOffFieldInvalid(name, "not an integer"))
    | true, _ -> Error(ReadOffFieldInvalid(name, "not a number"))
    | false, _ -> Error(ReadOffFieldMissing name)

let private readOffStringArray (element: JsonElement) (name: string) : Result<string list, ReadOffError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Array ->
        value.EnumerateArray()
        |> Seq.map (fun item ->
            if item.ValueKind = JsonValueKind.String then
                Ok(item.GetString())
            else
                Error(ReadOffFieldInvalid(name, "an item is not a string")))
        |> Seq.fold
            (fun accumulated item ->
                accumulated
                |> Result.bind (fun current -> item |> Result.map (fun value -> current @ [ value ])))
            (Ok [])
    | true, _ -> Error(ReadOffFieldInvalid(name, "not an array"))
    | false, _ -> Error(ReadOffFieldMissing name)

let private readOffKind (element: JsonElement) : Result<FSharpRecursionKind, ReadOffError> =
    match readOffString element "kind" with
    | Error error -> Error error
    | Ok "non-recursive" -> Ok NonRecursive
    | Ok "recursive" -> Ok Recursive
    | Ok "mutually-recursive" ->
        readOffStringArray element "mutual" |> Result.map MutuallyRecursive
    | Ok other -> Error(UnknownReadOffKind other)

let private readOffBinding (element: JsonElement) : Result<ReadOffBinding, ReadOffError> =
    match readOffString element "name" with
    | Error error -> Error error
    | Ok name ->
        match readOffStringArray element "parameters" with
        | Error error -> Error error
        | Ok parameters ->
            match readOffKind element with
            | Error error -> Error error
            | Ok kind ->
                match readOffStringArray element "calls" with
                | Error error -> Error error
                | Ok calls ->
                    match readOffInt element "startOffset" with
                    | Error error -> Error error
                    | Ok startOffset ->
                        match readOffInt element "endOffset" with
                        | Error error -> Error error
                        | Ok endOffset ->
                            let body =
                                match element.TryGetProperty "body" with
                                | false, _ -> Ok None
                                | true, value when value.ValueKind = JsonValueKind.Object ->
                                    match ExpressionPortable.ofJson(value.GetRawText()) with
                                    | Ok document -> Ok(Some document.Root)
                                    | Error _ -> Error(ReadOffBody "the body is not a valid expression document")
                                | true, _ -> Error(ReadOffFieldInvalid("body", "not an object"))

                            body
                            |> Result.map (fun body ->
                                {
                                    Name = name
                                    Parameters = parameters
                                    Kind = kind
                                    Calls = calls
                                    Range = { StartOffset = startOffset; EndOffset = endOffset }
                                    Body = body
                                })

let parseReadOff (text: string) : Result<ReadOffModule, ReadOffError> =
    try
        use document = JsonDocument.Parse text
        let root = document.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error(ReadOffFieldInvalid("readOff", "not an object"))
        else
            match readOffInt root "schemaVersion" with
            | Error error -> Error error
            | Ok schema when schema <> readOffSchemaVersion -> Error(UnsupportedReadOffSchema schema)
            | Ok schema ->
                match readOffString root "module" with
                | Error error -> Error error
                | Ok moduleName ->
                    match root.TryGetProperty "bindings" with
                    | true, bindings when bindings.ValueKind = JsonValueKind.Array ->
                        bindings.EnumerateArray()
                        |> Seq.map readOffBinding
                        |> Seq.fold
                            (fun accumulated item ->
                                accumulated
                                |> Result.bind (fun current ->
                                    item |> Result.map (fun binding -> current @ [ binding ])))
                            (Ok [])
                        |> Result.map (fun bindings ->
                            {
                                SchemaVersion = schema
                                Module = moduleName
                                Bindings = bindings
                            })
                    | true, _ -> Error(ReadOffFieldInvalid("bindings", "not an array"))
                    | false, _ -> Error(ReadOffFieldMissing "bindings")
    with error ->
        Error(ReadOffJson error.Message)

let fromReadOff
    (version: string)
    (sourceHash: string)
    (readOff: ReadOffModule)
    (bodies: Map<string, Expression>)
    : Result<FSharpAbstraction, ReadOffError list> =
    if readOff.SchemaVersion <> readOffSchemaVersion then
        Error [ UnsupportedReadOffSchema readOff.SchemaVersion ]
    else
        let bindings =
            readOff.Bindings
            |> List.map (fun binding ->
                let body =
                    match Map.tryFind binding.Name bodies with
                    | Some body -> Some body
                    | None -> binding.Body

                match body with
                | None -> Error(MissingBody binding.Name)
                | Some body ->
                    Ok
                        ({ Name = binding.Name
                           Parameters = binding.Parameters
                           Body = body
                           Kind = binding.Kind
                           Calls = binding.Calls
                           Range = binding.Range } : FSharpBinding))

        let errors = bindings |> List.choose (function Error error -> Some error | Ok _ -> None)

        if not errors.IsEmpty then
            Error errors
        else
            Ok
                {
                    SchemaVersion = abstractionSchemaVersion
                    Module = readOff.Module
                    Version = version
                    SourceHash = sourceHash
                    Bindings = bindings |> List.choose (function Ok binding -> Some binding | Error _ -> None)
                }

let private bindingKind (parameters: int) (kind: FSharpRecursionKind) : BindingKind =
    match kind with
    | NonRecursive -> if parameters = 0 then NonRecursiveValue else NonRecursiveFunction
    | Recursive -> if parameters = 0 then RecursiveValue else RecursiveFunction
    | MutuallyRecursive names -> MutuallyRecursiveFunction names

let private classifyOne
    (binding: FSharpBinding)
    (entry: ManifestBinding option)
    : Result<ClassifiedBinding * ClassificationDisagreement option, ClassificationError> =
    match entry with
    | None -> Ok({ Name = binding.Name; Kind = bindingKind binding.Parameters.Length binding.Kind; DeclaredReason = None }, None)
    | Some declared when declared.Kind = binding.Kind ->
        Ok({ Name = binding.Name; Kind = bindingKind binding.Parameters.Length binding.Kind; DeclaredReason = declared.Reason }, None)
    | Some declared ->
        let disagreement =
            {
                Name = binding.Name
                FSharp = binding.Kind
                Declared = declared.Kind
                Reason = declared.Reason
            }

        match declared.Reason with
        | None -> Error(ManifestDisagreement disagreement)
        | Some reason ->
            Ok({ Name = binding.Name; Kind = bindingKind binding.Parameters.Length declared.Kind; DeclaredReason = Some reason }, Some disagreement)

let classify
    (abstraction: FSharpAbstraction)
    (manifest: Manifest option)
    : Result<ClassificationResult, ClassificationError list> =
    if abstraction.SchemaVersion <> abstractionSchemaVersion then
        Error [ AbstractionSchemaMismatch abstraction.SchemaVersion ]
    else
        let names = abstraction.Bindings |> List.map (fun binding -> binding.Name)
        let duplicateNames = names |> List.countBy id |> List.filter (fun (_, count) -> count > 1) |> List.map fst

        if not duplicateNames.IsEmpty then
            Error(duplicateNames |> List.map DuplicateBinding)
        else
            match manifest with
            | Some value when value.SchemaVersion <> manifestSchemaVersion ->
                Error [ ManifestSchemaMismatch value.SchemaVersion ]
            | Some value when value.Package <> abstraction.Module ->
                Error [ ManifestPackageMismatch(value.Package, abstraction.Module) ]
            | _ ->
                let entries =
                    match manifest with
                    | Some value -> value.Bindings |> List.map (fun entry -> entry.Name, entry) |> Map.ofList
                    | None -> Map.empty

                let unknown =
                    entries
                    |> Map.toList
                    |> List.map fst
                    |> List.filter (fun name -> not (List.contains name names))
                    |> List.map UnknownManifestBinding

                let missing =
                    match manifest with
                    | Some { Mode = Strict } ->
                        names |> List.filter (fun name -> not (Map.containsKey name entries)) |> List.map MissingManifestBinding
                    | _ -> []

                if not unknown.IsEmpty || not missing.IsEmpty then
                    Error(unknown @ missing)
                else
                    let classified =
                        abstraction.Bindings
                        |> List.fold
                            (fun accumulated binding ->
                                accumulated
                                |> Result.bind (fun (current, overrides) ->
                                    match classifyOne binding (Map.tryFind binding.Name entries) with
                                    | Error error -> Error [ error ]
                                    | Ok(classified, disagreement) ->
                                        let overrides =
                                            match disagreement with
                                            | Some value -> overrides @ [ value ]
                                            | None -> overrides

                                        Ok(current @ [ classified ], overrides)))
                            (Ok([], []))

                    classified
                    |> Result.map (fun (bindings, overrides) ->
                        { Bindings = bindings; Overrides = overrides })

let mapDiagnostic (diagnostic: FSharpDiagnostic) : ScaleDiagnostic =
    let code =
        match diagnostic.Code with
        | "FS0040" -> RecursiveValueWithoutInstantiation
        | "FS0040-unused-rec" -> UnusedRecursiveMarker
        | "FS0040-value-restriction" -> ValueRestriction
        | other -> OtherFSharpDiagnostic other

    {
        Code = code
        Message = diagnostic.Message
        Range = diagnostic.Range
    }

let metadata (bindings: ClassifiedBinding list) : BindingMetadata list =
    bindings
    |> List.map (fun binding ->
        {
            Name = binding.Name
            Kind = describeBindingKind binding.Kind
            Version = metadataVersion
        })

let toPackageDocument
    (abstraction: FSharpAbstraction)
    (classified: ClassifiedBinding list)
    : Result<PackageDocument, PackagePortable.PackageError> =
    let units =
        abstraction.Bindings
        |> List.map (fun binding ->
            {
                Name = binding.Name
                SourceHash = abstraction.SourceHash
                Dependencies = binding.Calls
                Arity = binding.Parameters.Length
                Captures = []
                Root =
                    match binding.Parameters with
                    | [] -> binding.Body
                    | parameters -> Lambda(parameters, binding.Body)
            })

    let names = abstraction.Bindings |> List.map (fun binding -> binding.Name)
    let exports = names |> List.map (fun name -> { Name = name; Unit = name })

    let group =
        {
            Name = abstraction.Module
            Units = names
            InitOrder = names
            Classification =
                classified
                |> List.map (fun binding -> { Name = binding.Name; Kind = binding.Kind })
        }

    PackagePortable.document
        abstraction.Module
        abstraction.Version
        abstraction.SourceHash
        []
        exports
        []
        [ group ]
        units
