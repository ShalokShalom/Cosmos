module PreludeImage

// ============================================================
// The kernel-owned Prelude image format and loader.
//
// An image is the compiled, ordered set of hot-reload units the kernel runs:
// each unit carries its lowered instructions, and the document carries the
// pinned kind and predicate registries, capabilities, imports, and exports.
// The loader validates the whole graph before any unit can run. The JSON is
// read with `JsonDocument` and written with `Utf8JsonWriter`, so the path is
// NativeAOT-safe and never uses reflection serialization.
// ============================================================

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open LoweredInstructions

type ImageImport =
    {
        Name: string
        Version: string
    }

type ImageExport =
    {
        Name: string
        Unit: string
    }

type ImageKind =
    {
        Name: string
        Arity: int
    }

type ImagePredicate =
    {
        Name: string
        Version: int
    }

type ImagePrimitive =
    {
        Name: string
        Arity: int
        Results: int
    }

type BindingKind =
    | NonRecursiveFunction
    | RecursiveFunction
    | MutuallyRecursiveFunction of string list
    | NonRecursiveValue
    | RecursiveValue

type BindingClassification =
    {
        Name: string
        Kind: BindingKind
    }

type ImageUnitGroup =
    {
        Name: string
        Units: string list
        InitOrder: string list
        Classification: BindingClassification list
    }

type ImageUnit =
    {
        Name: string
        SourceHash: string
        Dependencies: string list
        Arity: int
        Captures: int list
        Instructions: Instruction list
    }

type ImageUnitPatch =
    {
        Unit: ImageUnit
        Exports: ImageExport list
        Groups: ImageUnitGroup list
        DependencyPins: Map<string, string>
    }

type ImageDocument =
    {
        SchemaVersion: int
        Form: string
        Name: string
        Version: string
        SourceHash: string
        DependencyPins: Map<string, string>
        Imports: ImageImport list
        Exports: ImageExport list
        Capabilities: string list
        Kinds: ImageKind list
        Predicates: ImagePredicate list
        Primitives: ImagePrimitive list
        Units: ImageUnit list
        Groups: ImageUnitGroup list
    }

type ImageError =
    | MalformedImage of reason: string
    | UnsupportedSchema of schema: int
    | UnsupportedForm of form: string
    | EmptyImage
    | OversizedImage of bytes: int * limit: int
    | EmptyField of name: string
    | DuplicateUnit of name: string
    | UnknownUnit of name: string
    | UnitArityChanged of name: string * previous: int * next: int
    | UnitCapturesChanged of name: string * previous: int list * next: int list
    | DependencyPinMismatch of unit: string * dependency: string * expected: string * actual: string
    | MissingDependency of unit: string * dependency: string
    | DependencyCycle of unit: string
    | DuplicateExport of name: string
    | UnknownExportUnit of name: string
    | UnknownEntry of name: string
    | DuplicateImport of name: string
    | DuplicateCapability of name: string
    | DuplicateKind of name: string
    | InconsistentKindArity of name: string * first: int * second: int
    | DuplicatePredicate of name: string
    | InconsistentPredicateVersion of name: string * first: int * second: int
    | DuplicatePrimitive of name: string
    | ConflictingWord of name: string
    | InvalidPrimitive of name: string
    | InvalidUnitArity of unit: string
    | InvalidUnitCapture of unit: string * slot: int
    | DuplicateGroup of name: string
    | EmptyUnitGroup of name: string
    | DuplicateGroupUnit of group: string * unit: string
    | UnknownGroupUnit of group: string * unit: string
    | InconsistentGroupOrder of group: string
    | InconsistentGroupClassification of group: string
    | UnknownInstruction of op: string
    | DuplicateLabel of unit: string * label: int
    | UnresolvedLabel of unit: string * label: int
    | InvalidSlot of unit: string * slot: int
    | InvalidGuard of unit: string
    | TooManyUnits of count: int * limit: int
    | TooManyInstructions of unit: string * count: int * limit: int

let describeError (error: ImageError) : string =
    match error with
    | MalformedImage reason -> "malformed image: " + reason
    | UnsupportedSchema schema -> "unsupported image schema: " + string schema
    | UnsupportedForm form -> "unsupported image form: " + form
    | EmptyImage -> "the image has no units"
    | OversizedImage(bytes, limit) -> "the image is " + string bytes + " bytes; the limit is " + string limit
    | EmptyField name -> "the image field is empty: " + name
    | DuplicateUnit name -> "duplicate unit: " + name
    | UnknownUnit name -> "unknown unit: " + name
    | UnitArityChanged(name, previous, next) ->
        "unit " + name + " changed arity from " + string previous + " to " + string next
    | UnitCapturesChanged(name, previous, next) ->
        "unit " + name + " changed captures from " + String.concat "," (List.map string previous)
        + " to " + String.concat "," (List.map string next)
    | DependencyPinMismatch(unit, dependency, expected, actual) ->
        "unit " + unit + " pins " + dependency + " at " + expected + " but found " + actual
    | MissingDependency(unit, dependency) -> "unit " + unit + " is missing dependency " + dependency
    | DependencyCycle unit -> "dependency cycle at unit: " + unit
    | DuplicateExport name -> "duplicate export: " + name
    | UnknownExportUnit name -> "an export names an unknown unit: " + name
    | UnknownEntry name -> "unknown entry: " + name
    | DuplicateImport name -> "duplicate import: " + name
    | DuplicateCapability name -> "duplicate capability: " + name
    | DuplicateKind name -> "duplicate kind: " + name
    | InconsistentKindArity(name, first, second) ->
        "kind " + name + " has arity " + string first + " and " + string second
    | DuplicatePredicate name -> "duplicate predicate: " + name
    | InconsistentPredicateVersion(name, first, second) ->
        "predicate " + name + " has version " + string first + " and " + string second
    | DuplicatePrimitive name -> "duplicate primitive: " + name
    | ConflictingWord name -> "a primitive and a unit share the name " + name
    | InvalidPrimitive name -> "invalid primitive: " + name
    | InvalidUnitArity name -> "unit has a negative arity: " + name
    | InvalidUnitCapture(unit, slot) -> "unit " + unit + " has a negative capture slot: " + string slot
    | DuplicateGroup name -> "duplicate unit group: " + name
    | EmptyUnitGroup name -> "the unit group has no units: " + name
    | DuplicateGroupUnit(group, unit) -> "unit group " + group + " contains " + unit + " twice"
    | UnknownGroupUnit(group, unit) -> "unit group " + group + " names an unknown unit: " + unit
    | InconsistentGroupOrder group -> "unit group has an inconsistent initialization order: " + group
    | InconsistentGroupClassification group -> "unit group has an inconsistent classification: " + group
    | UnknownInstruction op -> "unknown image instruction: " + op
    | DuplicateLabel(unit, label) -> "unit " + unit + " has duplicate label " + string label
    | UnresolvedLabel(unit, label) -> "unit " + unit + " has unresolved label " + string label
    | InvalidSlot(unit, slot) -> "unit " + unit + " has invalid slot " + string slot
    | InvalidGuard unit -> "unit " + unit + " has an invalid call guard"
    | TooManyUnits(count, limit) -> "the image has " + string count + " units; the limit is " + string limit
    | TooManyInstructions(unit, count, limit) ->
        "unit " + unit + " has " + string count + " instructions; the limit is " + string limit

[<Literal>]
let MaxBytes = 16 * 1024 * 1024

[<Literal>]
let MaxUnits = 4096

[<Literal>]
let MaxInstructionsPerUnit = 1000000

let private malformed (reason: string) : Result<'T, ImageError> = Error(MalformedImage reason)

let private nonEmpty (name: string) (value: string) : Result<string, ImageError> =
    if value.Trim() = "" then Error(EmptyField name) else Ok value

let private sequence (results: Result<'T, ImageError> list) : Result<'T list, ImageError> =
    match results |> List.choose (function Error error -> Some error | Ok _ -> None) with
    | error :: _ -> Error error
    | [] -> Ok(results |> List.choose (function Ok value -> Some value | Error _ -> None))

let private requiredString (element: JsonElement) (name: string) : Result<string, ImageError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
    | _ -> malformed ("Missing string field: " + name)

let private requiredInt (element: JsonElement) (name: string) : Result<int, ImageError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Number ->
        match value.TryGetInt32() with
        | true, number -> Ok number
        | false, _ -> malformed ("Field is not a 32-bit integer: " + name)
    | _ -> malformed ("Missing integer field: " + name)

let private optionalInt (element: JsonElement) (name: string) (fallback: int) : Result<int, ImageError> =
    match element.TryGetProperty name with
    | false, _ -> Ok fallback
    | true, value when value.ValueKind = JsonValueKind.Number ->
        match value.TryGetInt32() with
        | true, number -> Ok number
        | false, _ -> malformed ("Field is not a 32-bit integer: " + name)
    | _ -> malformed ("Field is not an integer: " + name)

let private arrayElements (element: JsonElement) (name: string) : Result<JsonElement list, ImageError> =
    match element.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.Array -> Ok(value.EnumerateArray() |> Seq.toList)
    | _ -> malformed ("Missing array field: " + name)

let private stringArray (element: JsonElement) (name: string) : Result<string list, ImageError> =
    arrayElements element name
    |> Result.bind (fun items ->
        items
        |> List.map (fun item ->
            if item.ValueKind = JsonValueKind.String then
                Ok(item.GetString())
            else
                malformed ("A value in " + name + " is not a string."))
        |> sequence)

let private readImport (element: JsonElement) : Result<ImageImport, ImageError> =
    match requiredString element "name", requiredString element "version" with
    | Ok name, Ok version ->
        nonEmpty "import.name" name
        |> Result.bind (fun validName -> nonEmpty "import.version" version |> Result.map (fun validVersion -> { Name = validName; Version = validVersion }))
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private readExport (element: JsonElement) : Result<ImageExport, ImageError> =
    match requiredString element "name", requiredString element "unit" with
    | Ok name, Ok unitName ->
        nonEmpty "export.name" name
        |> Result.bind (fun validName -> nonEmpty "export.unit" unitName |> Result.map (fun validUnit -> { Name = validName; Unit = validUnit }))
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private readKind (element: JsonElement) : Result<ImageKind, ImageError> =
    match requiredString element "name", requiredInt element "arity" with
    | Ok name, Ok arity ->
        nonEmpty "kind.name" name
        |> Result.bind (fun validName ->
            if arity < 0 then
                malformed ("A kind arity is negative: " + validName)
            else
                Ok { Name = validName; Arity = arity })
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private readPredicate (element: JsonElement) : Result<ImagePredicate, ImageError> =
    match requiredString element "name", requiredInt element "version" with
    | Ok name, Ok version ->
        nonEmpty "predicate.name" name
        |> Result.bind (fun validName ->
            if version < 1 then
                malformed ("A predicate version is below one: " + validName)
            else
                Ok { Name = validName; Version = version })
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private readPrimitive (element: JsonElement) : Result<ImagePrimitive, ImageError> =
    match requiredString element "name", requiredInt element "arity", requiredInt element "results" with
    | Ok name, Ok arity, Ok results ->
        nonEmpty "primitive.name" name
        |> Result.bind (fun validName ->
            if arity < 0 || results < 0 then
                Error(InvalidPrimitive validName)
            else
                Ok { Name = validName; Arity = arity; Results = results })
    | Error error, _, _ -> Error error
    | _, Error error, _ -> Error error
    | _, _, Error error -> Error error

let private optionalArrayElements (element: JsonElement) (name: string) : Result<JsonElement list, ImageError> =
    match element.TryGetProperty name with
    | false, _ -> Ok []
    | true, value when value.ValueKind = JsonValueKind.Array -> Ok(value.EnumerateArray() |> Seq.toList)
    | _ -> malformed ("Field is not an array: " + name)

let rec private readInstruction (unitName: string) (element: JsonElement) : Result<Instruction, ImageError> =
    match requiredString element "op" with
    | Error error -> Error error
    | Ok op ->
        match op with
        | "number" ->
            match element.TryGetProperty "value" with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetDouble() with
                | true, number -> Ok(LiteralNumber number)
                | false, _ -> malformed ("A number literal in " + unitName + " is not numeric.")
            | _ -> malformed ("A number literal in " + unitName + " has no value.")
        | "text" -> requiredString element "value" |> Result.map LiteralText
        | "make-code" ->
            optionalArrayElements element "instructions"
            |> Result.bind (fun elements ->
                elements
                |> List.map (readInstruction unitName)
                |> sequence)
            |> Result.map MakeCode
        | "push-slot" ->
            requiredInt element "slot"
            |> Result.bind (fun slot ->
                if slot < 0 then Error(InvalidSlot(unitName, slot)) else Ok(PushSlot slot))
        | "store-slot" ->
            requiredInt element "slot"
            |> Result.bind (fun slot ->
                if slot < 0 then Error(InvalidSlot(unitName, slot)) else Ok(StoreSlot slot))
        | "call" ->
            match requiredString element "name" with
            | Error error -> Error error
            | Ok name ->
                match element.TryGetProperty "target" with
                | false, _ ->
                    match element.TryGetProperty "epoch" with
                    | true, _ -> Error(InvalidGuard unitName)
                    | false, _ -> Ok(CallWord(name, None))
                | true, targetElement when targetElement.ValueKind = JsonValueKind.String ->
                    match requiredInt element "epoch" with
                    | Error _ -> Error(InvalidGuard unitName)
                    | Ok epoch when epoch < 0 -> Error(InvalidGuard unitName)
                    | Ok epoch ->
                        let target = targetElement.GetString()
                        if target.Trim() = "" then
                            Error(InvalidGuard unitName)
                        else
                            Ok(CallWord(name, Some { Target = target; Epoch = epoch }))
                | _ -> Error(InvalidGuard unitName)
        | "apply" -> Ok Apply
        | "duplicate" -> Ok Duplicate
        | "swap" -> Ok Swap
        | "drop" -> Ok Drop
        | "over" -> Ok Over
        | "rotate" -> Ok Rotate
        | "push-root" -> requiredString element "name" |> Result.map PushRoot
        | "set-root" -> requiredString element "name" |> Result.map SetRoot
        | "lookup-pair" -> requiredString element "root" |> Result.map LookupPair
        | "add-pair" -> requiredString element "root" |> Result.map AddPair
        | "define-word" -> Ok DefineWord
        | "make-closure" ->
            match requiredString element "name", optionalInt element "arity" 0, arrayElements element "captures" with
            | Error error, _, _ -> Error error
            | _, Error error, _ -> Error error
            | _, _, Error error -> Error error
            | Ok _, Ok arity, Ok _ when arity < 0 -> malformed "A closure arity is negative."
            | Ok name, Ok arity, Ok captures ->
                captures
                |> List.map (fun capture ->
                    match capture.TryGetInt32() with
                    | true, slot when slot >= 0 -> Ok slot
                    | _ -> malformed "A closure capture is not a non-negative slot.")
                |> sequence
                |> Result.map (fun slots -> MakeClosure(name, slots, arity))
        | "call-closure" -> Ok CallClosure
        | "push-capture" ->
            requiredInt element "index"
            |> Result.bind (fun index -> if index < 0 then malformed "A capture index is negative." else Ok(PushCapture index))
        | "tensor-rank" -> Ok TensorRank
        | "tensor-size" -> Ok TensorSize
        | "tensor-item" -> Ok TensorItem
        | "tensor-entry" -> Ok TensorEntry
        | "text-read" -> Ok TextRead
        | "text-equals" -> Ok TextEquals
        | "is-text" -> Ok IsText
        | "add" -> Ok Add
        | "subtract" -> Ok Subtract
        | "multiply" -> Ok Multiply
        | "divide" -> Ok Divide
        | "less-than" -> Ok LessThan
        | "greater-than" -> Ok GreaterThan
        | "equals" -> Ok Equals
        | "jump" ->
            requiredInt element "label"
            |> Result.bind (fun label -> if label < 0 then malformed "A jump label is negative." else Ok(Jump label))
        | "jump-if-false" ->
            requiredInt element "label"
            |> Result.bind (fun label ->
                if label < 0 then malformed "A conditional jump label is negative." else Ok(JumpIfFalse label))
        | "label" ->
            requiredInt element "label"
            |> Result.bind (fun label -> if label < 0 then malformed "A label is negative." else Ok(Label label))
        | "step-record" ->
            requiredInt element "step"
            |> Result.bind (fun step ->
                if step < 0 then malformed "A step record position is negative." else Ok(RecordStep step))
        | "fail" -> requiredString element "reason" |> Result.map Fail
        | other -> Error(UnknownInstruction other)

let private readBindingKind (element: JsonElement) : Result<BindingKind, ImageError> =
    match requiredString element "kind" with
    | Ok "non-recursive-function" -> Ok NonRecursiveFunction
    | Ok "recursive-function" -> Ok RecursiveFunction
    | Ok "mutually-recursive-function" ->
        stringArray element "mutual"
        |> Result.map MutuallyRecursiveFunction
    | Ok "non-recursive-value" -> Ok NonRecursiveValue
    | Ok "recursive-value" -> Ok RecursiveValue
    | Ok other -> malformed ("Unknown binding kind: " + other)
    | Error error -> Error error

let private readClassification (element: JsonElement) : Result<BindingClassification, ImageError> =
    match requiredString element "name" with
    | Error error -> Error error
    | Ok name ->
        readBindingKind element
        |> Result.map (fun kind -> { Name = name; Kind = kind })

let private readGroup (element: JsonElement) : Result<ImageUnitGroup, ImageError> =
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

let private readUnit (element: JsonElement) : Result<ImageUnit, ImageError> =
    match requiredString element "name", requiredString element "sourceHash" with
    | Error error, _ -> Error error
    | _, Error error -> Error error
    | Ok name, Ok sourceHash ->
        match nonEmpty "unit.name" name, nonEmpty "unit.sourceHash" sourceHash with
        | Error error, _ -> Error error
        | _, Error error -> Error error
        | Ok validName, Ok validHash ->
            match
                stringArray element "dependencies",
                requiredInt element "arity",
                arrayElements element "captures",
                arrayElements element "instructions"
            with
            | Error error, _, _, _ -> Error error
            | _, Error error, _, _ -> Error error
            | _, _, Error error, _ -> Error error
            | _, _, _, Error error -> Error error
            | Ok dependencies, Ok arity, Ok captureElements, Ok instructionElements ->
                if arity < 0 then
                    Error(InvalidUnitArity validName)
                elif List.length instructionElements > MaxInstructionsPerUnit then
                    Error(TooManyInstructions(validName, List.length instructionElements, MaxInstructionsPerUnit))
                else
                    let captures =
                        captureElements
                        |> List.map (fun capture ->
                            match capture.TryGetInt32() with
                            | true, slot when slot >= 0 -> Ok slot
                            | true, slot -> Error(InvalidUnitCapture(validName, slot))
                            | _ -> malformed "A unit capture is not an integer.")
                        |> sequence

                    match captures with
                    | Error error -> Error error
                    | Ok captureSlots ->
                        instructionElements
                        |> List.map (readInstruction validName)
                        |> sequence
                        |> Result.map (fun instructions ->
                            {
                                Name = validName
                                SourceHash = validHash
                                Dependencies = dependencies
                                Arity = arity
                                Captures = captureSlots
                                Instructions = instructions
                            })

let private uniqueNames (subject: string) (names: string list) : Result<unit, ImageError> =
    match
        names
        |> List.countBy id
        |> List.tryFind (fun (_, count) -> count > 1)
    with
    | Some(name, _) ->
        if subject = "unit" then Error(DuplicateUnit name)
        elif subject = "export" then Error(DuplicateExport name)
        elif subject = "import" then Error(DuplicateImport name)
        elif subject = "capability" then Error(DuplicateCapability name)
        else Error(MalformedImage("duplicate " + subject + ": " + name))
    | None -> Ok()

let private validateLabels (unit: ImageUnit) : Result<unit, ImageError> =
    let labels =
        unit.Instructions
        |> List.choose (function
            | Label label -> Some label
            | _ -> None)

    let duplicates =
        labels
        |> List.countBy id
        |> List.tryFind (fun (_, count) -> count > 1)

    match duplicates with
    | Some(label, _) -> Error(DuplicateLabel(unit.Name, label))
    | None ->
        let known = Set.ofList labels

        match
            unit.Instructions
            |> List.tryPick (function
                | Jump label
                | JumpIfFalse label -> if known.Contains label then None else Some label
                | _ -> None)
        with
        | Some label -> Error(UnresolvedLabel(unit.Name, label))
        | None -> Ok()

let private validateKinds (kinds: ImageKind list) : Result<ImageKind list, ImageError> =
    kinds
    |> List.fold
        (fun (accumulated: Result<Map<string, int>, ImageError>) kind ->
            match accumulated with
            | Error error -> Error error
            | Ok current ->
                match current.TryFind kind.Name with
                | Some arity when arity <> kind.Arity -> Error(InconsistentKindArity(kind.Name, arity, kind.Arity))
                | Some _ -> Ok current
                | None -> Ok(current.Add(kind.Name, kind.Arity)))
        (Ok Map.empty)
    |> Result.map (fun map ->
        map
        |> Map.toList
        |> List.map (fun (name, arity) -> { Name = name; Arity = arity }))

let private validatePredicates (predicates: ImagePredicate list) : Result<ImagePredicate list, ImageError> =
    predicates
    |> List.fold
        (fun (accumulated: Result<Map<string, int>, ImageError>) predicate ->
            match accumulated with
            | Error error -> Error error
            | Ok current ->
                match current.TryFind predicate.Name with
                | Some version when version <> predicate.Version ->
                    Error(InconsistentPredicateVersion(predicate.Name, version, predicate.Version))
                | Some _ -> Ok current
                | None -> Ok(current.Add(predicate.Name, predicate.Version)))
        (Ok Map.empty)
    |> Result.map (fun map ->
        map
        |> Map.toList
        |> List.map (fun (name, version) -> { Name = name; Version = version }))

let private validateDependencies (units: ImageUnit list) : Result<unit, ImageError> =
    let known = units |> List.map (fun unit -> unit.Name) |> Set.ofList

    match
        units
        |> List.collect (fun unit -> unit.Dependencies |> List.map (fun dependency -> unit.Name, dependency))
        |> List.tryFind (fun (_, dependency) -> not (known.Contains dependency))
    with
    | Some(unit, dependency) -> Error(MissingDependency(unit, dependency))
    | None ->
        let byName = units |> List.map (fun unit -> unit.Name, unit) |> Map.ofList

        let rec visit (path: Set<string>) (doneSet: Set<string>) (name: string) : Result<Set<string>, ImageError> =
            if doneSet.Contains name then
                Ok doneSet
            elif path.Contains name then
                Error(DependencyCycle name)
            else
                match byName.TryFind name with
                | None -> Ok doneSet
                | Some unit ->
                    let nextPath = path.Add name

                    unit.Dependencies
                    |> List.fold
                        (fun (accumulated: Result<Set<string>, ImageError>) dependency ->
                            accumulated |> Result.bind (fun currentDone -> visit nextPath currentDone dependency))
                        (Ok doneSet)
                    |> Result.map (Set.add name)

        units
        |> List.fold
            (fun (accumulated: Result<Set<string>, ImageError>) unit ->
                accumulated |> Result.bind (fun doneSet -> visit Set.empty doneSet unit.Name))
            (Ok Set.empty)
        |> Result.map ignore

let private validateDependencyPins (document: ImageDocument) : Result<unit, ImageError> =
    document.DependencyPins
    |> Map.toList
    |> List.tryPick (fun (dependency, expected) ->
        match document.Units |> List.tryFind (fun unit -> unit.Name = dependency) with
        | None -> Some(MissingDependency("image", dependency))
        | Some unit when unit.SourceHash <> expected ->
            Some(DependencyPinMismatch("image", dependency, expected, unit.SourceHash))
        | Some _ -> None)
    |> function
        | Some error -> Error error
        | None -> Ok()

let private validatePrimitives
    (units: ImageUnit list)
    (primitives: ImagePrimitive list)
    : Result<unit, ImageError> =
    let unitNames = units |> List.map (fun unit -> unit.Name) |> Set.ofList

    match
        primitives
        |> List.countBy (fun primitive -> primitive.Name)
        |> List.tryFind (fun (_, count) -> count > 1)
    with
    | Some(name, _) -> Error(DuplicatePrimitive name)
    | None ->
        match primitives |> List.tryFind (fun primitive -> unitNames.Contains primitive.Name) with
        | Some primitive -> Error(ConflictingWord primitive.Name)
        | None ->
            match primitives |> List.tryFind (fun primitive -> primitive.Arity < 0 || primitive.Results < 0) with
            | Some primitive -> Error(InvalidPrimitive primitive.Name)
            | None -> Ok()

let private validateGroups (units: ImageUnit list) (groups: ImageUnitGroup list) : Result<unit, ImageError> =
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

let private validateDocumentCore (document: ImageDocument) : Result<ImageDocument, ImageError> =
    if document.SchemaVersion <> 2 then
        Error(UnsupportedSchema document.SchemaVersion)
    elif document.Form <> "image" then
        Error(UnsupportedForm document.Form)
    else
        match nonEmpty "image.name" document.Name, nonEmpty "image.version" document.Version, nonEmpty "image.sourceHash" document.SourceHash with
        | Error error, _, _ -> Error error
        | _, Error error, _ -> Error error
        | _, _, Error error -> Error error
        | Ok name, Ok version, Ok sourceHash ->
            if document.Units.IsEmpty then
                Error EmptyImage
            elif List.length document.Units > MaxUnits then
                Error(TooManyUnits(List.length document.Units, MaxUnits))
            else
                let unitNames = document.Units |> List.map (fun unit -> unit.Name)
                let exportNames = document.Exports |> List.map (fun export -> export.Name)
                let importNames = document.Imports |> List.map (fun import -> import.Name)

                match uniqueNames "unit" unitNames with
                | Error error -> Error error
                | Ok() ->
                    match uniqueNames "export" exportNames with
                    | Error error -> Error error
                    | Ok() ->
                        match uniqueNames "import" importNames with
                        | Error error -> Error error
                        | Ok() ->
                            match uniqueNames "capability" document.Capabilities with
                            | Error error -> Error error
                            | Ok() ->
                                match validateDependencies document.Units with
                                | Error error -> Error error
                                | Ok() ->
                                    match validateDependencyPins document with
                                    | Error error -> Error error
                                    | Ok() ->
                                        match
                                            document.Units
                                            |> List.tryPick (fun unit ->
                                                match validateLabels unit with
                                                | Error error -> Some error
                                                | Ok() -> None)
                                        with
                                        | Some error -> Error error
                                        | None ->
                                            let knownUnits = Set.ofList unitNames

                                            match
                                                document.Exports
                                                |> List.tryFind (fun export -> not (knownUnits.Contains export.Unit))
                                            with
                                            | Some export -> Error(UnknownExportUnit export.Unit)
                                            | None ->
                                                match validateKinds document.Kinds, validatePredicates document.Predicates with
                                                | Error error, _ -> Error error
                                                | _, Error error -> Error error
                                                | Ok kinds, Ok predicates ->
                                                    Ok
                                                        {
                                                            document with
                                                                Name = name
                                                                Version = version
                                                                SourceHash = sourceHash
                                                                Kinds = kinds
                                                                Predicates = predicates
                                                        }

let private validateDocument (document: ImageDocument) : Result<ImageDocument, ImageError> =
    match validatePrimitives document.Units document.Primitives with
    | Error error -> Error error
    | Ok() ->
        match document.Units |> List.tryFind (fun unit -> unit.Arity < 0) with
        | Some unit -> Error(InvalidUnitArity unit.Name)
        | None ->
            match
                document.Units
                |> List.tryPick (fun unit ->
                    unit.Captures
                    |> List.tryFind (fun slot -> slot < 0)
                    |> Option.map (fun slot -> unit.Name, slot))
            with
            | Some(unit, slot) -> Error(InvalidUnitCapture(unit, slot))
            | None ->
                match validateGroups document.Units document.Groups with
                | Error error -> Error error
                | Ok() -> validateDocumentCore document

let private readDependencyPins (root: JsonElement) : Result<Map<string, string>, ImageError> =
    match root.TryGetProperty "dependencyPins" with
    | false, _ -> Ok Map.empty
    | true, element when element.ValueKind = JsonValueKind.Object ->
        element.EnumerateObject()
        |> Seq.map (fun property ->
            match property.Value.GetString() with
            | null -> Error(MalformedImage "a dependency pin has no hash")
            | value when String.IsNullOrWhiteSpace value ->
                Error(MalformedImage "a dependency pin has an empty hash")
            | value -> Ok(property.Name, value))
        |> Seq.toList
        |> sequence
        |> Result.map Map.ofList
    | true, _ -> Error(MalformedImage "dependencyPins must be an object")

let private readDocument (root: JsonElement) : Result<ImageDocument, ImageError> =
    match requiredInt root "schema" with
    | Error error -> Error error
    | Ok schema ->
        match requiredString root "form" with
        | Error error -> Error error
        | Ok form ->
            match
                requiredString root "name",
                requiredString root "version",
                requiredString root "sourceHash"
            with
            | Error error, _, _ -> Error error
            | _, Error error, _ -> Error error
            | _, _, Error error -> Error error
            | Ok name, Ok version, Ok sourceHash ->
                let readAll
                    (name: string)
                    (reader: JsonElement -> Result<'T, ImageError>)
                    : Result<'T list, ImageError> =
                    arrayElements root name
                    |> Result.bind (fun elements -> elements |> List.map reader |> sequence)

                match
                    readAll "imports" readImport,
                    readAll "exports" readExport,
                    stringArray root "capabilities",
                    readAll "kinds" readKind,
                    readAll "predicates" readPredicate,
                    readAll "units" readUnit,
                    readAll "groups" readGroup
                with
                | Error error, _, _, _, _, _, _ -> Error error
                | _, Error error, _, _, _, _, _ -> Error error
                | _, _, Error error, _, _, _, _ -> Error error
                | _, _, _, Error error, _, _, _ -> Error error
                | _, _, _, _, Error error, _, _ -> Error error
                | _, _, _, _, _, Error error, _ -> Error error
                | _, _, _, _, _, _, Error error -> Error error
                | Ok imports, Ok exports, Ok capabilities, Ok kinds, Ok predicates, Ok units, Ok groups ->
                    match readDependencyPins root with
                    | Error error -> Error error
                    | Ok dependencyPins ->
                        let primitives =
                            optionalArrayElements root "primitives"
                            |> Result.bind (fun elements -> elements |> List.map readPrimitive |> sequence)

                        match primitives with
                        | Error error -> Error error
                        | Ok primitiveList ->
                            validateDocument
                                {
                                    SchemaVersion = schema
                                    Form = form
                                    Name = name
                                    Version = version
                                    SourceHash = sourceHash
                                    DependencyPins = dependencyPins
                                    Imports = imports
                                    Exports = exports
                                    Capabilities = capabilities
                                    Kinds = kinds
                                    Predicates = predicates
                                    Primitives = primitiveList
                                    Units = units
                                    Groups = groups
                                }

let ofText (text: string) : Result<ImageDocument, ImageError> =
    try
        use json = JsonDocument.Parse text
        readDocument json.RootElement
    with exn ->
        Error(MalformedImage exn.Message)

let ofBytes (bytes: byte[]) : Result<ImageDocument, ImageError> =
    if bytes.Length = 0 then
        Error EmptyImage
    elif bytes.Length > MaxBytes then
        Error(OversizedImage(bytes.Length, MaxBytes))
    else
        try
            ofText (UTF8Encoding(false, true).GetString bytes)
        with exn ->
            Error(MalformedImage exn.Message)

let ofUnitText (text: string) : Result<ImageUnit, ImageError> =
    try
        use json = JsonDocument.Parse text
        readUnit json.RootElement
    with exn ->
        Error(MalformedImage exn.Message)

let ofUnitBytes (bytes: byte[]) : Result<ImageUnit, ImageError> =
    if bytes.Length = 0 then
        Error(MalformedImage "empty unit document")
    elif bytes.Length > MaxBytes then
        Error(OversizedImage(bytes.Length, MaxBytes))
    else
        try
            ofUnitText (UTF8Encoding(false, true).GetString bytes)
        with exn ->
            Error(MalformedImage exn.Message)

let ofUnitPatchText (text: string) : Result<ImageUnitPatch, ImageError> =
    try
        use json = JsonDocument.Parse text
        let root = json.RootElement

        match root.TryGetProperty "unit" with
        | false, _ -> Error(MalformedImage "a unit patch has no unit")
        | true, unitElement ->
            match readUnit unitElement with
            | Error error -> Error error
            | Ok unit ->
                match
                    optionalArrayElements root "exports"
                    |> Result.bind (fun elements -> elements |> List.map readExport |> sequence),
                    optionalArrayElements root "groups"
                    |> Result.bind (fun elements -> elements |> List.map readGroup |> sequence),
                    readDependencyPins root
                with
                | Error error, _, _ -> Error error
                | _, Error error, _ -> Error error
                | _, _, Error error -> Error error
                | Ok exports, Ok groups, Ok pins ->
                    Ok
                        {
                            Unit = unit
                            Exports = exports
                            Groups = groups
                            DependencyPins = pins
                        }
    with exn ->
        Error(MalformedImage exn.Message)

let ofUnitPatchBytes (bytes: byte[]) : Result<ImageUnitPatch, ImageError> =
    if bytes.Length = 0 then
        Error(MalformedImage "empty unit patch")
    elif bytes.Length > MaxBytes then
        Error(OversizedImage(bytes.Length, MaxBytes))
    else
        try
            ofUnitPatchText (UTF8Encoding(false, true).GetString bytes)
        with exn ->
            Error(MalformedImage exn.Message)

let resolveExport (image: ImageDocument) (name: string) : Result<ImageUnit, ImageError> =
    match image.Exports |> List.tryFind (fun export -> export.Name = name) with
    | None -> Error(UnknownEntry name)
    | Some export ->
        match image.Units |> List.tryFind (fun unit -> unit.Name = export.Unit) with
        | Some unit -> Ok unit
        | None -> Error(UnknownExportUnit export.Unit)

let findUnit (image: ImageDocument) (name: string) : ImageUnit option =
    image.Units |> List.tryFind (fun unit -> unit.Name = name)

let private writeArray
    (writer: Utf8JsonWriter)
    (name: string)
    (items: 'T list)
    (writeItem: Utf8JsonWriter -> 'T -> unit)
    : unit =
    writer.WriteStartArray name

    for item in items do
        writeItem writer item

    writer.WriteEndArray()

let private writeBindingKind (writer: Utf8JsonWriter) (kind: BindingKind) : unit =
    match kind with
    | NonRecursiveFunction -> writer.WriteString("kind", "non-recursive-function")
    | RecursiveFunction -> writer.WriteString("kind", "recursive-function")
    | MutuallyRecursiveFunction names ->
        writer.WriteString("kind", "mutually-recursive-function")
        writeArray writer "mutual" names (fun target name -> target.WriteStringValue name)
    | NonRecursiveValue -> writer.WriteString("kind", "non-recursive-value")
    | RecursiveValue -> writer.WriteString("kind", "recursive-value")

let rec private writeInstruction (writer: Utf8JsonWriter) (instruction: Instruction) : unit =
    writer.WriteStartObject()

    match instruction with
    | LiteralNumber value ->
        writer.WriteString("op", "number")
        writer.WriteNumber("value", value)
    | LiteralText value ->
        writer.WriteString("op", "text")
        writer.WriteString("value", value)
    | PushSlot slot ->
        writer.WriteString("op", "push-slot")
        writer.WriteNumber("slot", slot)
    | StoreSlot slot ->
        writer.WriteString("op", "store-slot")
        writer.WriteNumber("slot", slot)
    | CallWord(name, guard) ->
        writer.WriteString("op", "call")
        writer.WriteString("name", name)

        match guard with
        | Some cached ->
            writer.WriteString("target", cached.Target)
            writer.WriteNumber("epoch", cached.Epoch)
        | None -> ()
    | Apply -> writer.WriteString("op", "apply")
    | Duplicate -> writer.WriteString("op", "duplicate")
    | Swap -> writer.WriteString("op", "swap")
    | Drop -> writer.WriteString("op", "drop")
    | Over -> writer.WriteString("op", "over")
    | Rotate -> writer.WriteString("op", "rotate")
    | PushRoot name ->
        writer.WriteString("op", "push-root")
        writer.WriteString("name", name)
    | SetRoot name ->
        writer.WriteString("op", "set-root")
        writer.WriteString("name", name)
    | LookupPair root ->
        writer.WriteString("op", "lookup-pair")
        writer.WriteString("root", root)
    | AddPair root ->
        writer.WriteString("op", "add-pair")
        writer.WriteString("root", root)
    | DefineWord -> writer.WriteString("op", "define-word")
    | MakeClosure(name, captures, arity) ->
        writer.WriteString("op", "make-closure")
        writer.WriteString("name", name)

        if arity > 0 then
            writer.WriteNumber("arity", arity)

        writer.WriteStartArray("captures")

        for capture in captures do
            writer.WriteNumberValue capture

        writer.WriteEndArray()
    | CallClosure -> writer.WriteString("op", "call-closure")
    | PushCapture index ->
        writer.WriteString("op", "push-capture")
        writer.WriteNumber("index", index)
    | TensorRank -> writer.WriteString("op", "tensor-rank")
    | TensorSize -> writer.WriteString("op", "tensor-size")
    | TensorItem -> writer.WriteString("op", "tensor-item")
    | TensorEntry -> writer.WriteString("op", "tensor-entry")
    | TextRead -> writer.WriteString("op", "text-read")
    | TextEquals -> writer.WriteString("op", "text-equals")
    | IsText -> writer.WriteString("op", "is-text")
    | Add -> writer.WriteString("op", "add")
    | Subtract -> writer.WriteString("op", "subtract")
    | Multiply -> writer.WriteString("op", "multiply")
    | Divide -> writer.WriteString("op", "divide")
    | LessThan -> writer.WriteString("op", "less-than")
    | GreaterThan -> writer.WriteString("op", "greater-than")
    | Equals -> writer.WriteString("op", "equals")
    | Jump label ->
        writer.WriteString("op", "jump")
        writer.WriteNumber("label", label)
    | JumpIfFalse label ->
        writer.WriteString("op", "jump-if-false")
        writer.WriteNumber("label", label)
    | Label label ->
        writer.WriteString("op", "label")
        writer.WriteNumber("label", label)
    | RecordStep step ->
        writer.WriteString("op", "step-record")
        writer.WriteNumber("step", step)
    | MakeCode body ->
        writer.WriteString("op", "make-code")
        writeArray writer "instructions" body writeInstruction
    | Fail reason ->
        writer.WriteString("op", "fail")
        writer.WriteString("reason", reason)

    writer.WriteEndObject()

let toJson (image: ImageDocument) : string =
    let stream = new System.IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     writer.WriteStartObject()
     writer.WriteNumber("schema", image.SchemaVersion)
     writer.WriteString("form", image.Form)
     writer.WriteString("name", image.Name)
     writer.WriteString("version", image.Version)
     writer.WriteString("sourceHash", image.SourceHash)

     if not image.DependencyPins.IsEmpty then
         writer.WriteStartObject "dependencyPins"

         for KeyValue(name, sourceHash) in image.DependencyPins do
             writer.WriteString(name, sourceHash)

         writer.WriteEndObject()

     writeArray writer "imports" image.Imports (fun target import ->
         target.WriteStartObject()
         target.WriteString("name", import.Name)
         target.WriteString("version", import.Version)
         target.WriteEndObject())

     writeArray writer "exports" image.Exports (fun target export ->
         target.WriteStartObject()
         target.WriteString("name", export.Name)
         target.WriteString("unit", export.Unit)
         target.WriteEndObject())

     writeArray writer "capabilities" image.Capabilities (fun target capability -> target.WriteStringValue capability)

     writeArray writer "kinds" image.Kinds (fun target kind ->
         target.WriteStartObject()
         target.WriteString("name", kind.Name)
         target.WriteNumber("arity", kind.Arity)
         target.WriteEndObject())

     writeArray writer "predicates" image.Predicates (fun target predicate ->
         target.WriteStartObject()
         target.WriteString("name", predicate.Name)
         target.WriteNumber("version", predicate.Version)
         target.WriteEndObject())

     writeArray writer "primitives" image.Primitives (fun target primitive ->
         target.WriteStartObject()
         target.WriteString("name", primitive.Name)
         target.WriteNumber("arity", primitive.Arity)
         target.WriteNumber("results", primitive.Results)
         target.WriteEndObject())

     writeArray writer "units" image.Units (fun target unit ->
         target.WriteStartObject()
         target.WriteString("name", unit.Name)
         target.WriteString("sourceHash", unit.SourceHash)
         writeArray target "dependencies" unit.Dependencies (fun dependencyTarget dependency ->
             dependencyTarget.WriteStringValue dependency)
         target.WriteNumber("arity", unit.Arity)
         writeArray target "captures" unit.Captures (fun captureTarget slot ->
             captureTarget.WriteNumberValue slot)
         writeArray target "instructions" unit.Instructions writeInstruction
         target.WriteEndObject())

     writeArray writer "groups" image.Groups (fun target group ->
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

    Encoding.UTF8.GetString(stream.ToArray())

/// The standalone instruction document: one program as JSON text. It uses the
/// same opcode reader and writer as the image, so an opcode is spelled in one
/// place and an older reader refuses a newer op by name. This is the payload a
/// code value and a history step carry (D202, W1a).
let toInstructionDocumentJson (instructions: Instruction list) : string =
    let stream = new System.IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     writer.WriteStartObject()
     writer.WriteString("form", "instruction-program")
     writer.WriteNumber("schema", 1)
     writeArray writer "instructions" instructions writeInstruction
     writer.WriteEndObject()
     writer.Flush())

    Encoding.UTF8.GetString(stream.ToArray())

/// Reads a standalone instruction document. A different form, a different
/// schema, and an unknown op are each refused by name.
let fromInstructionDocumentJson (text: string) : Result<Instruction list, ImageError> =
    try
        use document = JsonDocument.Parse text
        let root = document.RootElement

        match requiredString root "form" with
        | Error error -> Error error
        | Ok form when form = "instruction-program" ->
            match root.TryGetProperty "schema" with
            | true, schema when schema.ValueKind = JsonValueKind.Number && schema.GetInt32() = 1 ->
                optionalArrayElements root "instructions"
                |> Result.bind (fun elements ->
                    elements
                    |> List.map (readInstruction "instruction document")
                    |> sequence)
            | _ -> malformed "The instruction document schema is not 1."
        | Ok form -> malformed ("Unknown document form: " + form)
    with :? JsonException as error ->
        malformed ("The instruction document is not valid JSON: " + error.Message)

let private withArtifactHash (image: ImageDocument) : Result<ImageDocument, ImageError> =
    let unsigned = { image with SourceHash = "" }

    let artifactHash =
        toJson unsigned
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    validateDocument
        {
            unsigned with
                SourceHash = artifactHash
        }

let replaceUnit (image: ImageDocument) (unit: ImageUnit) : Result<ImageDocument, ImageError> =
    match image.Units |> List.tryFind (fun current -> current.Name = unit.Name) with
    | None -> Error(UnknownUnit unit.Name)
    | Some current when current.Arity <> unit.Arity ->
        Error(UnitArityChanged(unit.Name, current.Arity, unit.Arity))
    | Some current when current.Captures <> unit.Captures ->
        Error(UnitCapturesChanged(unit.Name, current.Captures, unit.Captures))
    | Some _ ->
        {
            image with
                Units =
                    image.Units
                    |> List.map (fun current ->
                        if current.Name = unit.Name then unit else current)
        }
        |> withArtifactHash

let applyUnitPatch
    (image: ImageDocument)
    (patch: ImageUnitPatch)
    : Result<ImageDocument, ImageError> =
    let duplicateExport =
        patch.Exports
        |> List.countBy (fun export -> export.Name)
        |> List.tryFind (fun (_, count) -> count > 1)

    match duplicateExport with
    | Some(name, _) -> Error(DuplicateExport name)
    | None ->
        let pinError =
            patch.DependencyPins
            |> Map.toList
            |> List.tryPick (fun (dependency, expected) ->
                match image.Units |> List.tryFind (fun unit -> unit.Name = dependency) with
                | None -> Some(MissingDependency(patch.Unit.Name, dependency))
                | Some unit when unit.SourceHash <> expected ->
                    Some(
                        DependencyPinMismatch(
                            patch.Unit.Name,
                            dependency,
                            expected,
                            unit.SourceHash
                        )
                    )
                | Some _ -> None)

        match pinError with
        | Some error -> Error error
        | None ->
            let existing =
                image.Units
                |> List.tryFind (fun current -> current.Name = patch.Unit.Name)

            match existing with
            | Some current when current.Arity <> patch.Unit.Arity ->
                Error(UnitArityChanged(patch.Unit.Name, current.Arity, patch.Unit.Arity))
            | Some current when current.Captures <> patch.Unit.Captures ->
                Error(UnitCapturesChanged(patch.Unit.Name, current.Captures, patch.Unit.Captures))
            | Some _ ->
                let units =
                    image.Units
                    |> List.map (fun current ->
                        if current.Name = patch.Unit.Name then patch.Unit else current)

                let exports: ImageExport list =
                    patch.Exports
                    |> List.fold
                        (fun (current: ImageExport list) (export: ImageExport) ->
                            match current |> List.tryFindIndex (fun item -> item.Name = export.Name) with
                            | Some index -> List.updateAt index export current
                            | None -> current @ [ export ])
                        image.Exports

                let groups =
                    patch.Groups
                    |> List.fold
                        (fun (current: ImageUnitGroup list) (group: ImageUnitGroup) ->
                            match current |> List.tryFindIndex (fun item -> item.Name = group.Name) with
                            | Some index -> List.updateAt index group current
                            | None -> current @ [ group ])
                        image.Groups

                withArtifactHash
                    {
                        image with
                            Units = units
                            Exports = exports
                            Groups = groups
                            DependencyPins =
                                patch.DependencyPins
                                |> Map.fold (fun current name hash -> Map.add name hash current) image.DependencyPins
                    }
            | None ->
                let exports: ImageExport list =
                    patch.Exports
                    |> List.fold
                        (fun (current: ImageExport list) (export: ImageExport) ->
                            match current |> List.tryFindIndex (fun item -> item.Name = export.Name) with
                            | Some index -> List.updateAt index export current
                            | None -> current @ [ export ])
                        image.Exports

                let groups =
                    patch.Groups
                    |> List.fold
                        (fun (current: ImageUnitGroup list) (group: ImageUnitGroup) ->
                            match current |> List.tryFindIndex (fun item -> item.Name = group.Name) with
                            | Some index -> List.updateAt index group current
                            | None -> current @ [ group ])
                        image.Groups

                withArtifactHash
                    {
                        image with
                            Units = image.Units @ [ patch.Unit ]
                            Exports = exports
                            Groups = groups
                            DependencyPins =
                                patch.DependencyPins
                                |> Map.fold (fun current name hash -> Map.add name hash current) image.DependencyPins
                    }
