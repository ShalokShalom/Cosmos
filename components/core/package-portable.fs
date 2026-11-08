module PackagePortable

// ============================================================
// Canonical JSON v1 for a package document.
//
// A package is a versioned set of ordered units plus the kind and predicate
// registries those units reference. Unit identity for hot reload is the unit
// name and its source hash. The loader checks the schema, registries, unit
// graph, and exports before any unit runs.
// ============================================================

open System.Text.Json
open PatternLogic
open ExpressionLogic
open PackageLogic
open PatternPortable
open ExpressionPortable

type PackageError =
    | ExpressionError of PatternPortableError
    | DuplicateUnit of name: string
    | UnknownExportUnit of name: string
    | MissingDependency of unit: string * dependency: string
    | EmptySourceHash of subject: string
    | InvalidUnitArity of unit: string
    | InvalidUnitCapture of unit: string * slot: int
    | DuplicateGroup of name: string
    | EmptyUnitGroup of name: string
    | DuplicateGroupUnit of group: string * unit: string
    | UnknownGroupUnit of group: string * unit: string
    | InconsistentGroupOrder of group: string
    | InconsistentGroupClassification of group: string
    | MalformedPackage of reason: string

let private addKind
    (name: string)
    (arity: int)
    (kinds: Map<string, int>)
    : Result<Map<string, int>, PackageError> =
    match kinds.TryFind name with
    | Some existing when existing <> arity ->
        Error(ExpressionError(InconsistentKindArity(name, existing, arity)))
    | Some _ -> Ok kinds
    | None -> Ok(kinds.Add(name, arity))

let private addPredicate
    (name: string)
    (version: int)
    (predicates: (string * int) list)
    : Result<(string * int) list, PackageError> =
    match predicates |> List.tryFind (fun (candidate, _) -> candidate = name) with
    | Some(_, existing) when existing <> version ->
        Error(ExpressionError(InconsistentPredicateVersion(name, existing, version)))
    | Some _ -> Ok predicates
    | None -> Ok(predicates @ [ name, version ])

let private validateUnits (units: PackageUnit list) (exports: PackageExport list) : Result<unit, PackageError> =
    let names =
        units
        |> List.map (fun unit -> unit.Name)

    let duplicate =
        names
        |> List.countBy id
        |> List.tryFind (fun (_, count) -> count > 1)

    match duplicate with
    | Some(name, _) -> Error(DuplicateUnit name)
    | None ->
        let known = Set.ofList names

        match units |> List.tryFind (fun unit -> unit.SourceHash.Trim() = "") with
        | Some unit -> Error(EmptySourceHash unit.Name)
        | None ->
            match units |> List.tryFind (fun unit -> unit.Arity < 0) with
            | Some unit -> Error(InvalidUnitArity unit.Name)
            | None ->
                match
                    units
                    |> List.tryPick (fun unit ->
                        unit.Captures
                        |> List.tryFind (fun slot -> slot < 0)
                        |> Option.map (fun slot -> unit.Name, slot))
                with
                | Some(unit, slot) -> Error(InvalidUnitCapture(unit, slot))
                | None ->
                    match
                        units
                        |> List.collect (fun unit -> unit.Dependencies |> List.map (fun dependency -> unit.Name, dependency))
                        |> List.tryFind (fun (_, dependency) -> not (known.Contains dependency))
                    with
                    | Some(unit, dependency) -> Error(MissingDependency(unit, dependency))
                    | None ->
                        match exports |> List.tryFind (fun export -> not (known.Contains export.Unit)) with
                        | Some export -> Error(UnknownExportUnit export.Unit)
                        | None -> Ok()

let private validateGroups
    (units: PackageUnit list)
    (groups: PackageUnitGroup list)
    : Result<unit, PackageError> =
    let known = units |> List.map (fun unit -> unit.Name) |> Set.ofList

    let duplicateGroup =
        groups
        |> List.countBy (fun group -> group.Name)
        |> List.tryFind (fun (_, count) -> count > 1)

    match duplicateGroup with
    | Some(name, _) -> Error(DuplicateGroup name)
    | None ->
        groups
        |> List.tryPick (fun group ->
            if group.Units.IsEmpty then
                Some(Error(EmptyUnitGroup group.Name))
            else
                match
                    group.Units
                    |> List.countBy id
                    |> List.tryFind (fun (_, count) -> count > 1)
                with
                | Some(unit, _) -> Some(Error(DuplicateGroupUnit(group.Name, unit)))
                | None ->
                    match group.Units |> List.tryFind (fun unit -> not (known.Contains unit)) with
                    | Some unit -> Some(Error(UnknownGroupUnit(group.Name, unit)))
                    | None ->
                        let ordered = List.sort group.InitOrder
                        let classified = group.Classification |> List.map (fun entry -> entry.Name) |> List.sort

                        if ordered <> (List.sort group.Units) then
                            Some(Error(InconsistentGroupOrder group.Name))
                        elif classified <> (List.sort group.Units) then
                            Some(Error(InconsistentGroupClassification group.Name))
                        else
                            None)
        |> Option.defaultValue (Ok())

/// Builds a package document and derives the pinned registries from its units.
let document
    (name: string)
    (version: string)
    (sourceHash: string)
    (imports: PackageImport list)
    (exports: PackageExport list)
    (capabilities: string list)
    (groups: PackageUnitGroup list)
    (units: PackageUnit list)
    : Result<PackageDocument, PackageError> =
    if sourceHash.Trim() = "" then
        Error(EmptySourceHash name)
    else
        match validateUnits units exports with
        | Error error -> Error error
        | Ok() ->
            match validateGroups units groups with
            | Error error -> Error error
            | Ok() ->
                let collected =
                    units
                    |> List.fold
                        (fun accumulated unit ->
                            match accumulated with
                            | Error error -> Error error
                            | Ok(currentKinds, currentPredicates) ->
                                match ExpressionPortable.document unit.Root with
                                | Error error -> Error(ExpressionError error)
                                | Ok expressionDocument ->
                                    let withKinds =
                                        expressionDocument.Kinds
                                        |> List.fold
                                            (fun (kindsResult: Result<Map<string, int>, PackageError>) (kind: PatternKind) ->
                                                match kindsResult with
                                                | Error error -> Error error
                                                | Ok current -> addKind kind.Name kind.Arity current)
                                            (Ok currentKinds)

                                    match withKinds with
                                    | Error error -> Error error
                                    | Ok nextKinds ->
                                        expressionDocument.Predicates
                                        |> List.fold
                                            (fun (predicatesResult: Result<(string * int) list, PackageError>) (predicateName, predicateVersion) ->
                                                match predicatesResult with
                                                | Error error -> Error error
                                                | Ok current -> addPredicate predicateName predicateVersion current)
                                            (Ok currentPredicates)
                                        |> Result.map (fun nextPredicates -> nextKinds, nextPredicates))
                        (Ok(Map.empty, []))

                match collected with
                | Error error -> Error error
                | Ok(kinds, predicates) ->
                    Ok
                        {
                            SchemaVersion = 2
                            Form = "package"
                            Name = name
                            Version = version
                            SourceHash = sourceHash
                            Imports = imports
                            Exports = exports
                            Capabilities = capabilities
                            Kinds =
                                kinds
                                |> Map.toList
                                |> List.map (fun (kindName, arity) -> { Name = kindName; Arity = arity })
                            Predicates = predicates |> List.sortBy fst
                            Units = units
                            Groups = groups
                        }

/// The hot-reload identity of a unit.
let unitIdentity (unit: PackageUnit) : string = unit.Name + "@" + unit.SourceHash

/// A replacement is allowed only when the unit names agree and the source
/// hashes differ; the loader still checks the unit graph and pins.
let isReplacement (previous: PackageUnit) (next: PackageUnit) : bool =
    previous.Name = next.Name && previous.SourceHash <> next.SourceHash

let private malformed (reason: string) : Result<'T, PackageError> = Error(MalformedPackage reason)

let private sequence results = PortableJson.sequence results
let private writeArray writer name items writeItem = PortableJson.writeArray writer name items writeItem
let private requiredString element name = PortableJson.requiredString element name |> Result.mapError (fun reason -> MalformedPackage reason)
let private requiredInt element name = PortableJson.requiredInt element name |> Result.mapError (fun reason -> MalformedPackage reason)
let private arrayElements element name = PortableJson.arrayElements element name |> Result.mapError (fun reason -> MalformedPackage reason)

let private stringArray element name =
    arrayElements element name
    |> Result.bind (fun items ->
        items
        |> List.map (fun item ->
            if item.ValueKind = JsonValueKind.String then
                Ok(item.GetString())
            else
                malformed "A string array contains a non-string.")
        |> sequence)

let private writeBindingKind (writer: Utf8JsonWriter) (kind: BindingKind) : unit =
    match kind with
    | NonRecursiveFunction -> writer.WriteString("kind", "non-recursive-function")
    | RecursiveFunction -> writer.WriteString("kind", "recursive-function")
    | MutuallyRecursiveFunction names ->
        writer.WriteString("kind", "mutually-recursive-function")
        writeArray writer "mutual" names (fun target name -> target.WriteStringValue name)
    | NonRecursiveValue -> writer.WriteString("kind", "non-recursive-value")
    | RecursiveValue -> writer.WriteString("kind", "recursive-value")

let toJson (document: PackageDocument) : string =
    let stream = new System.IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     writer.WriteStartObject()
     writer.WriteNumber("schema", document.SchemaVersion)
     writer.WriteString("form", document.Form)
     writer.WriteString("name", document.Name)
     writer.WriteString("version", document.Version)
     writer.WriteString("sourceHash", document.SourceHash)

     writeArray writer "imports" document.Imports (fun target import ->
         target.WriteStartObject()
         target.WriteString("name", import.Name)
         target.WriteString("version", import.Version)
         target.WriteEndObject())

     writeArray writer "exports" document.Exports (fun target export ->
         target.WriteStartObject()
         target.WriteString("name", export.Name)
         target.WriteString("unit", export.Unit)
         target.WriteEndObject())

     writeArray writer "capabilities" document.Capabilities (fun target capability ->
         target.WriteStringValue capability)

     writeArray writer "kinds" document.Kinds (fun target kind ->
         target.WriteStartObject()
         target.WriteString("name", kind.Name)
         target.WriteNumber("arity", kind.Arity)
         target.WriteEndObject())

     writeArray writer "predicates" document.Predicates (fun target (name, version) ->
         target.WriteStartObject()
         target.WriteString("name", name)
         target.WriteNumber("version", version)
         target.WriteEndObject())

     writeArray writer "units" document.Units (fun target unit ->
         target.WriteStartObject()
         target.WriteString("name", unit.Name)
         target.WriteString("sourceHash", unit.SourceHash)
         writeArray target "dependencies" unit.Dependencies (fun dependencyTarget dependency ->
             dependencyTarget.WriteStringValue dependency)
         target.WriteNumber("arity", unit.Arity)
         writeArray target "captures" unit.Captures (fun captureTarget slot ->
             captureTarget.WriteNumberValue slot)
         target.WritePropertyName("root")
         ExpressionPortable.writeExpression target unit.Root
         target.WriteEndObject())

     writeArray writer "groups" document.Groups (fun target group ->
         target.WriteStartObject()
         target.WriteString("name", group.Name)
         writeArray target "units" group.Units (fun unitTarget unitName ->
             unitTarget.WriteStringValue unitName)
         writeArray target "initOrder" group.InitOrder (fun orderTarget unitName ->
             orderTarget.WriteStringValue unitName)
         writeArray target "classification" group.Classification (fun classificationTarget entry ->
             classificationTarget.WriteStartObject()
             classificationTarget.WriteString("name", entry.Name)
             writeBindingKind classificationTarget entry.Kind
             classificationTarget.WriteEndObject())
         target.WriteEndObject())

     writer.WriteEndObject()
     writer.Flush())

    System.Text.Encoding.UTF8.GetString(stream.ToArray())

let private readImport (element: JsonElement) : Result<PackageImport, PackageError> =
    match requiredString element "name", requiredString element "version" with
    | Ok name, Ok version -> Ok { Name = name; Version = version }
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private readExport (element: JsonElement) : Result<PackageExport, PackageError> =
    match requiredString element "name", requiredString element "unit" with
    | Ok name, Ok unitName -> Ok { Name = name; Unit = unitName }
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private readBindingKind (element: JsonElement) : Result<BindingKind, PackageError> =
    match requiredString element "kind" with
    | Ok "non-recursive-function" -> Ok NonRecursiveFunction
    | Ok "recursive-function" -> Ok RecursiveFunction
    | Ok "mutually-recursive-function" ->
        arrayElements element "mutual"
        |> Result.bind (fun items ->
            items
            |> List.map (fun item ->
                if item.ValueKind = JsonValueKind.String then
                    Ok(item.GetString())
                else
                    malformed "A mutual-recursion name is not a string.")
            |> sequence)
        |> Result.map MutuallyRecursiveFunction
    | Ok "non-recursive-value" -> Ok NonRecursiveValue
    | Ok "recursive-value" -> Ok RecursiveValue
    | Ok other -> malformed ("Unknown binding kind: " + other)
    | Error error -> Error error

let private readClassification (element: JsonElement) : Result<BindingClassification, PackageError> =
    match requiredString element "name" with
    | Error error -> Error error
    | Ok name ->
        readBindingKind element
        |> Result.map (fun kind -> { Name = name; Kind = kind })

let private readGroup (element: JsonElement) : Result<PackageUnitGroup, PackageError> =
    match requiredString element "name" with
    | Error error -> Error error
    | Ok name ->
        match stringArray element "units", stringArray element "initOrder", arrayElements element "classification" with
        | Error error, _, _ -> Error error
        | _, Error error, _ -> Error error
        | _, _, Error error -> Error error
        | Ok units, Ok initOrder, Ok classifications ->
            classifications
            |> List.map readClassification
            |> sequence
            |> Result.map (fun classification ->
                {
                    Name = name
                    Units = units
                    InitOrder = initOrder
                    Classification = classification
                })

let private readUnit
    (kinds: Map<string, int>)
    (predicates: (string * int) list)
    (element: JsonElement)
    : Result<PackageUnit, PackageError> =
    match requiredString element "name", requiredString element "sourceHash" with
    | Error error, _ -> Error error
    | _, Error error -> Error error
    | Ok name, Ok sourceHash ->
        let dependencies =
            arrayElements element "dependencies"
            |> Result.bind (fun items ->
                items
                |> List.map (fun item ->
                    if item.ValueKind = JsonValueKind.String then
                        Ok(item.GetString())
                    else
                        malformed "A unit dependency is not a string.")
                |> sequence)

        match dependencies, requiredInt element "arity", arrayElements element "captures", element.TryGetProperty "root" with
        | Error error, _, _, _ -> Error error
        | _, Error error, _, _ -> Error error
        | _, _, Error error, _ -> Error error
        | _, _, _, (false, _) -> malformed ("Unit " + name + " has no root.")
        | Ok dependencyNames, Ok arity, Ok captureElements, (true, rootElement) ->
            if arity < 0 then
                Error(InvalidUnitArity name)
            else
                let captures =
                    captureElements
                    |> List.map (fun capture ->
                        match capture.TryGetInt32() with
                        | true, slot when slot >= 0 -> Ok slot
                        | true, slot -> Error(InvalidUnitCapture(name, slot))
                        | _ -> malformed "A unit capture is not an integer.")
                    |> sequence

                match captures with
                | Error error -> Error error
                | Ok captureSlots ->
                    ExpressionPortable.readExpression kinds predicates rootElement
                    |> Result.mapError ExpressionError
                    |> Result.map (fun root ->
                        {
                            Name = name
                            SourceHash = sourceHash
                            Dependencies = dependencyNames
                            Arity = arity
                            Captures = captureSlots
                            Root = root
                        })

let ofJson (text: string) : Result<PackageDocument, PackageError> =
    try
        use json = JsonDocument.Parse text
        let root = json.RootElement

        match requiredInt root "schema" with
        | Error error -> Error error
        | Ok schema when schema <> 2 -> Error(ExpressionError(UnsupportedSchema schema))
        | Ok _ ->
            match requiredString root "form" with
            | Error error -> Error error
            | Ok form when form <> "package" -> Error(ExpressionError(UnsupportedForm form))
            | Ok _ ->
                match
                    requiredString root "name",
                    requiredString root "version",
                    requiredString root "sourceHash"
                with
                | Ok name, Ok version, Ok sourceHash ->
                    match arrayElements root "kinds", arrayElements root "predicates" with
                    | Error error, _ -> Error error
                    | _, Error error -> Error error
                    | Ok kindElements, Ok predicateElements ->
                        let kinds =
                            kindElements
                            |> List.map (fun element -> PatternPortable.readKind element |> Result.mapError ExpressionError)
                            |> sequence

                        let predicates =
                            predicateElements
                            |> List.map (fun element -> PatternPortable.readPredicate element |> Result.mapError ExpressionError)
                            |> sequence

                        match kinds, predicates with
                        | Error error, _ -> Error error
                        | _, Error error -> Error error
                        | Ok kindList, Ok predicateList ->
                            let kindMap =
                                kindList
                                |> List.fold
                                    (fun (accumulated: Result<Map<string, int>, PackageError>) (kind: PatternKind) ->
                                        match accumulated with
                                        | Error error -> Error error
                                        | Ok current -> addKind kind.Name kind.Arity current)
                                    (Ok Map.empty)

                            match kindMap with
                            | Error error -> Error error
                            | Ok kindsMap ->
                                let imports =
                                    arrayElements root "imports"
                                    |> Result.bind (fun items -> items |> List.map readImport |> sequence)

                                let exports =
                                    arrayElements root "exports"
                                    |> Result.bind (fun items -> items |> List.map readExport |> sequence)

                                let capabilities =
                                    arrayElements root "capabilities"
                                    |> Result.bind (fun items ->
                                        items
                                        |> List.map (fun item ->
                                            if item.ValueKind = JsonValueKind.String then
                                                Ok(item.GetString())
                                            else
                                                malformed "A capability is not a string.")
                                        |> sequence)

                                let units =
                                    arrayElements root "units"
                                    |> Result.bind (fun items ->
                                        items |> List.map (readUnit kindsMap predicateList) |> sequence)

                                let groups =
                                    arrayElements root "groups"
                                    |> Result.bind (fun items -> items |> List.map readGroup |> sequence)

                                match imports, exports, capabilities, units, groups with
                                | Error error, _, _, _, _ -> Error error
                                | _, Error error, _, _, _ -> Error error
                                | _, _, Error error, _, _ -> Error error
                                | _, _, _, Error error, _ -> Error error
                                | _, _, _, _, Error error -> Error error
                                | Ok importList, Ok exportList, Ok capabilityList, Ok unitList, Ok groupList ->
                                    match validateUnits unitList exportList with
                                    | Error error -> Error error
                                    | Ok() ->
                                        match validateGroups unitList groupList with
                                        | Error error -> Error error
                                        | Ok() ->
                                            Ok
                                                {
                                                    SchemaVersion = 2
                                                    Form = "package"
                                                    Name = name
                                                    Version = version
                                                    SourceHash = sourceHash
                                                    Imports = importList
                                                    Exports = exportList
                                                    Capabilities = capabilityList
                                                    Kinds = kindList
                                                    Predicates = predicateList
                                                    Units = unitList
                                                    Groups = groupList
                                                }
                | Error error, _, _ -> Error error
                | _, Error error, _ -> Error error
                | _, _, Error error -> Error error
    with exn ->
        Error(MalformedPackage exn.Message)
