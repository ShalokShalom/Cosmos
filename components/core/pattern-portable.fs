module PatternPortable

// ============================================================
// The portable pattern form.
//
// The runtime IR is host-local; this module is the wire form another Scale
// host reads. The form is closed and versioned, constructors are canonical
// names with fixed arities, and guards are named predicate references with
// pinned versions. A pattern template is not executable and is refused until
// hole signatures and elaboration are designed.
//
// JSON is written with Utf8JsonWriter and read with JsonDocument: the
// reflection and source-generation paths are not NativeAOT-compatible for
// F# records.
// ============================================================

open System.Text.Json
open PatternLogic

type PatternPortableError =
    | UnsupportedSchema of version: int
    | UnsupportedForm of form: string
    | MissingKind of name: string
    | KindArityMismatch of name: string * expected: int * actual: int
    | InconsistentKindArity of name: string * first: int * second: int
    | MissingPredicateVersion of name: string
    | InconsistentPredicateVersion of name: string * first: int * second: int
    | UndeclaredHole of name: string
    | DuplicateHole of name: string
    | MalformedJson of reason: string

let private roleText (role: VariableRole) : string =
    match role with
    | Fresh -> "fresh"
    | Pin -> "pin"
    | Repeated -> "repeated"
    | Shadowing -> "shadowing"

let private roleOfText (text: string) : Result<VariableRole, string> =
    match text with
    | "fresh" -> Ok Fresh
    | "pin" -> Ok Pin
    | "repeated" -> Ok Repeated
    | "shadowing" -> Ok Shadowing
    | other -> Error("Unknown variable role: " + other)

let rec collectKinds
    (pattern: Pattern)
    (kinds: Map<string, int>)
    : Result<Map<string, int>, PatternPortableError> =
    match pattern with
    | Wildcard
    | Variable _
    | NumberLiteral _
    | TextLiteral _
    | AtomLiteral _ -> Ok kinds
    | TypedPattern(_, child) -> collectKinds child kinds
    | Constructor(name, children) ->
        let arity = List.length children

        match kinds.TryFind name with
        | Some existing when existing <> arity ->
            Error(InconsistentKindArity(name, existing, arity))
        | Some _ ->
            children
            |> List.fold
                (fun accumulated child ->
                    match accumulated with
                    | Error error -> Error error
                    | Ok current -> collectKinds child current)
                (Ok kinds)
        | None ->
            children
            |> List.fold
                (fun accumulated child ->
                    match accumulated with
                    | Error error -> Error error
                    | Ok current -> collectKinds child current)
                (Ok(kinds.Add(name, arity)))

let private collectPredicates
    (clauses: PatternClause list)
    : Result<(string * int) list, PatternPortableError> =
    clauses
    |> List.choose (fun clause -> clause.Guard)
    |> List.fold
        (fun accumulated guard ->
            match accumulated with
            | Error error -> Error error
            | Ok predicates ->
                match predicates |> List.tryFind (fun (name, _) -> name = guard.Predicate) with
                | Some(_, existing) when existing <> guard.Version ->
                    Error(InconsistentPredicateVersion(guard.Predicate, existing, guard.Version))
                | Some _ -> Ok predicates
                | None -> Ok(predicates @ [ guard.Predicate, guard.Version ]))
        (Ok [])
    |> Result.map (List.sortBy fst)

/// Builds a closed document from clauses, deriving the kind and predicate
/// registries. A kind name may not change arity inside one document.
let document (clauses: PatternClause list) : Result<PatternDocument, PatternPortableError> =
    let kinds =
        clauses
        |> List.fold
            (fun accumulated clause ->
                match accumulated with
                | Error error -> Error error
                | Ok current -> collectKinds clause.Pattern current)
            (Ok Map.empty)

    match kinds with
    | Error error -> Error error
    | Ok kindMap ->
        match collectPredicates clauses with
        | Error error -> Error error
        | Ok predicates ->
            Ok
                {
                    SchemaVersion = 1
                    Form = "closed"
                    Kinds = kindMap |> Map.toList |> List.map (fun (name, arity) -> { Name = name; Arity = arity })
                    Predicates = predicates
                    Clauses = clauses
                }

let rec writePattern (writer: Utf8JsonWriter) (pattern: Pattern) : unit =
    writer.WriteStartObject()

    match pattern with
    | Wildcard -> writer.WriteString("tag", "wildcard")
    | Variable(name, role) ->
        writer.WriteString("tag", "variable")
        writer.WriteString("name", name)
        writer.WriteString("role", roleText role)
    | NumberLiteral value ->
        writer.WriteString("tag", "number")
        writer.WriteNumber("value", value)
    | TextLiteral value ->
        writer.WriteString("tag", "text")
        writer.WriteString("value", value)
    | AtomLiteral value ->
        writer.WriteString("tag", "atom")
        writer.WriteString("value", value)
    | Constructor(name, children) ->
        writer.WriteString("tag", "constructor")
        writer.WriteString("name", name)
        writer.WriteStartArray("children")

        for child in children do
            writePattern writer child

        writer.WriteEndArray()
    | TypedPattern(sortName, child) ->
        writer.WriteString("tag", "typed")
        writer.WriteString("sort", sortName)
        writer.WritePropertyName("child")
        writePattern writer child

    writer.WriteEndObject()

let private writeJson (write: Utf8JsonWriter -> unit) : string =
    use stream = new System.IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     write writer
     writer.Flush())

    System.Text.Encoding.UTF8.GetString(stream.ToArray())

let toJson (document: PatternDocument) : string =
    writeJson (fun writer ->
        writer.WriteStartObject()
        writer.WriteNumber("schema", document.SchemaVersion)
        writer.WriteString("form", document.Form)

        writer.WriteStartArray("kinds")

        for kind in document.Kinds do
            writer.WriteStartObject()
            writer.WriteString("name", kind.Name)
            writer.WriteNumber("arity", kind.Arity)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteStartArray("predicates")

        for (name, version) in document.Predicates do
            writer.WriteStartObject()
            writer.WriteString("name", name)
            writer.WriteNumber("version", version)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteStartArray("clauses")

        for clause in document.Clauses do
            writer.WriteStartObject()
            writer.WritePropertyName("pattern")
            writePattern writer clause.Pattern

            match clause.Guard with
            | Some guard ->
                writer.WriteStartObject("guard")
                writer.WriteString("predicate", guard.Predicate)
                writer.WriteNumber("version", guard.Version)
                writer.WriteEndObject()
            | None -> writer.WriteNull("guard")

            writer.WriteString("body", clause.Body)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject())

let private malformed (reason: string) : Result<'T, PatternPortableError> = Error(MalformedJson reason)

let private sequence results = PortableJson.sequence results
let private requiredString element name = PortableJson.requiredString element name |> Result.mapError (fun reason -> MalformedJson reason)
let private requiredInt element name = PortableJson.requiredInt element name |> Result.mapError (fun reason -> MalformedJson reason)

let rec readPattern
    (kinds: Map<string, int>)
    (element: JsonElement)
    : Result<Pattern, PatternPortableError> =
    match requiredString element "tag" with
    | Error error -> Error error
    | Ok tag ->
        match tag with
        | "wildcard" -> Ok Wildcard
        | "variable" ->
            match requiredString element "name", requiredString element "role" with
            | Ok name, Ok roleText ->
                match roleOfText roleText with
                | Ok role -> Ok(Variable(name, role))
                | Error reason -> malformed reason
            | Error error, _ -> Error error
            | _, Error error -> Error error
        | "number" ->
            match element.TryGetProperty "value" with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetDouble() with
                | true, number -> Ok(NumberLiteral number)
                | false, _ -> malformed "A number pattern has a non-numeric value."
            | _ -> malformed "A number pattern is missing value."
        | "text" ->
            requiredString element "value" |> Result.map TextLiteral
        | "atom" ->
            requiredString element "value" |> Result.map AtomLiteral
        | "typed" ->
            match requiredString element "sort" with
            | Error error -> Error error
            | Ok sortName ->
                match element.TryGetProperty "child" with
                | true, child -> readPattern kinds child |> Result.map (fun value -> TypedPattern(sortName, value))
                | _ -> malformed "A typed pattern is missing child."
        | "constructor" ->
            match requiredString element "name" with
            | Error error -> Error error
            | Ok name ->
                match kinds.TryFind name with
                | None -> Error(MissingKind name)
                | Some arity ->
                    match element.TryGetProperty "children" with
                    | false, _ -> malformed "A constructor pattern is missing children."
                    | true, children ->
                        let parsed =
                            children.EnumerateArray()
                            |> Seq.map (readPattern kinds)
                            |> Seq.toList

                        let errors = parsed |> List.choose (function Error error -> Some error | Ok _ -> None)

                        match errors with
                        | error :: _ -> Error error
                        | [] ->
                            let values = parsed |> List.choose (function Ok value -> Some value | Error _ -> None)

                            if List.length values <> arity then
                                Error(KindArityMismatch(name, arity, List.length values))
                            else
                                Ok(Constructor(name, values))
        | other -> malformed ("Unknown pattern tag: " + other)

let readKind (element: JsonElement) : Result<PatternKind, PatternPortableError> =
    match requiredString element "name", requiredInt element "arity" with
    | Ok name, Ok arity ->
        if arity < 0 then
            malformed ("Constructor " + name + " has a negative arity.")
        else
            Ok { Name = name; Arity = arity }
    | Error error, _ -> Error error
    | _, Error error -> Error error

let readPredicate (element: JsonElement) : Result<string * int, PatternPortableError> =
    match requiredString element "name", requiredInt element "version" with
    | Ok name, Ok version -> Ok(name, version)
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private readClause
    (kinds: Map<string, int>)
    (predicates: (string * int) list)
    (element: JsonElement)
    : Result<PatternClause, PatternPortableError> =
    let pattern =
        match element.TryGetProperty "pattern" with
        | true, value -> readPattern kinds value
        | _ -> malformed "A clause is missing pattern."

    let guard =
        match element.TryGetProperty "guard" with
        | false, _ -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, value ->
            match requiredString value "predicate", requiredInt value "version" with
            | Ok predicate, Ok version ->
                match predicates |> List.tryFind (fun (name, _) -> name = predicate) with
                | Some(_, expected) when expected = version ->
                    Ok(Some { Predicate = predicate; Version = version })
                | Some(_, expected) ->
                    Error(MissingPredicateVersion(predicate + ":" + string expected))
                | None -> Error(MissingPredicateVersion predicate)
            | Error error, _ -> Error error
            | _, Error error -> Error error
        | _ -> malformed "A clause guard is neither an object nor null."

    let body = requiredString element "body"

    match pattern, guard, body with
    | Ok pattern, Ok guard, Ok body -> Ok { Pattern = pattern; Guard = guard; Body = body }
    | Error error, _, _ -> Error error
    | _, Error error, _ -> Error error
    | _, _, Error error -> Error error

let ofJson (text: string) : Result<PatternDocument, PatternPortableError> =
    try
        use document = JsonDocument.Parse text
        let root = document.RootElement

        match requiredInt root "schema" with
        | Error error -> Error error
        | Ok schema when schema <> 1 -> Error(UnsupportedSchema schema)
        | Ok _ ->
            match requiredString root "form" with
            | Error error -> Error error
            | Ok form when form <> "closed" -> Error(UnsupportedForm form)
            | Ok _ ->
                let kinds =
                    match root.TryGetProperty "kinds" with
                    | true, value when value.ValueKind = JsonValueKind.Array ->
                        value.EnumerateArray() |> Seq.map readKind |> Seq.toList |> sequence
                    | _ -> malformed "The document has no kinds array."

                let predicates =
                    match root.TryGetProperty "predicates" with
                    | true, value when value.ValueKind = JsonValueKind.Array ->
                        value.EnumerateArray() |> Seq.map readPredicate |> Seq.toList |> sequence
                    | _ -> malformed "The document has no predicates array."

                match kinds, predicates with
                | Ok kindList, Ok predicateList ->
                    let kindMap =
                        kindList
                        |> List.fold
                            (fun (accumulated: Result<Map<string, int>, PatternPortableError>) (kind: PatternKind) ->
                                match accumulated with
                                | Error error -> Error error
                                | Ok current ->
                                    match current.TryFind kind.Name with
                                    | Some existing when existing <> kind.Arity ->
                                        Error(InconsistentKindArity(kind.Name, existing, kind.Arity))
                                    | Some _ -> Ok current
                                    | None -> Ok(current.Add(kind.Name, kind.Arity)))
                            (Ok Map.empty)

                    match kindMap with
                    | Error error -> Error error
                    | Ok kindsMap ->
                        let clauses =
                            match root.TryGetProperty "clauses" with
                            | true, value when value.ValueKind = JsonValueKind.Array ->
                                value.EnumerateArray()
                                |> Seq.map (readClause kindsMap predicateList)
                                |> Seq.toList
                                |> sequence
                            | _ -> malformed "The document has no clauses array."

                        match clauses with
                        | Error error -> Error error
                        | Ok clauseResults ->
                            Ok
                                {
                                    SchemaVersion = 1
                                    Form = "closed"
                                    Kinds = kindList
                                    Predicates = predicateList
                                    Clauses = clauseResults
                                }
                | Error error, _ -> Error error
                | _, Error error -> Error error
    with exn ->
        Error(MalformedJson exn.Message)

// ---- Templates -------------------------------------------------------------

let rec private collectTemplateKinds
    (node: TemplateNode)
    (kinds: Map<string, int>)
    : Result<Map<string, int>, PatternPortableError> =
    match node with
    | TemplateHole _
    | TemplateVariable _
    | TemplateNumber _
    | TemplateText _
    | TemplateAtom _ -> Ok kinds
    | TemplateTyped(_, child) -> collectTemplateKinds child kinds
    | TemplateConstructor(name, children) ->
        let arity = List.length children

        match kinds.TryFind name with
        | Some existing when existing <> arity ->
            Error(InconsistentKindArity(name, existing, arity))
        | _ ->
            children
            |> List.fold
                (fun accumulated child ->
                    match accumulated with
                    | Error error -> Error error
                    | Ok current -> collectTemplateKinds child current)
                (Ok(kinds.Add(name, arity)))

let rec private usedHoles (node: TemplateNode) (used: Set<string>) : Set<string> =
    match node with
    | TemplateHole name -> used.Add name
    | TemplateTyped(_, child) -> usedHoles child used
    | TemplateConstructor(_, children) ->
        children |> List.fold (fun current child -> usedHoles child current) used
    | _ -> used

let templateDocument (holes: HoleSignature list) (root: TemplateNode) : Result<PatternTemplate, PatternPortableError> =
    let duplicate =
        holes
        |> List.countBy (fun hole -> hole.Name)
        |> List.tryFind (fun (_, count) -> count > 1)

    match duplicate with
    | Some(name, _) -> Error(DuplicateHole name)
    | None ->
        let declared = holes |> List.map (fun hole -> hole.Name) |> Set.ofList
        let used = usedHoles root Set.empty

        match used |> Set.toList |> List.tryFind (fun name -> not (declared.Contains name)) with
        | Some name -> Error(UndeclaredHole name)
        | None ->
            match collectTemplateKinds root Map.empty with
            | Error error -> Error error
            | Ok kinds ->
                Ok
                    {
                        SchemaVersion = 1
                        Form = "template"
                        Holes = holes
                        Kinds =
                            kinds
                            |> Map.toList
                            |> List.map (fun (name, arity) -> { Name = name; Arity = arity })
                        Root = root
                    }

let rec private writeTemplateNode (writer: Utf8JsonWriter) (node: TemplateNode) : unit =
    writer.WriteStartObject()

    match node with
    | TemplateHole name ->
        writer.WriteString("tag", "hole")
        writer.WriteString("name", name)
    | TemplateVariable(name, role) ->
        writer.WriteString("tag", "variable")
        writer.WriteString("name", name)
        writer.WriteString("role", roleText role)
    | TemplateNumber value ->
        writer.WriteString("tag", "number")
        writer.WriteNumber("value", value)
    | TemplateText value ->
        writer.WriteString("tag", "text")
        writer.WriteString("value", value)
    | TemplateAtom value ->
        writer.WriteString("tag", "atom")
        writer.WriteString("value", value)
    | TemplateConstructor(name, children) ->
        writer.WriteString("tag", "constructor")
        writer.WriteString("name", name)
        writer.WriteStartArray("children")

        for child in children do
            writeTemplateNode writer child

        writer.WriteEndArray()
    | TemplateTyped(sortName, child) ->
        writer.WriteString("tag", "typed")
        writer.WriteString("sort", sortName)
        writer.WritePropertyName("child")
        writeTemplateNode writer child

    writer.WriteEndObject()

let templateToJson (template: PatternTemplate) : string =
    writeJson (fun writer ->
        writer.WriteStartObject()
        writer.WriteNumber("schema", template.SchemaVersion)
        writer.WriteString("form", template.Form)
        writer.WriteStartArray("holes")

        for hole in template.Holes do
            writer.WriteStartObject()
            writer.WriteString("name", hole.Name)
            writer.WriteString("kind", hole.Kind)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteStartArray("kinds")

        for kind in template.Kinds do
            writer.WriteStartObject()
            writer.WriteString("name", kind.Name)
            writer.WriteNumber("arity", kind.Arity)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WritePropertyName("root")
        writeTemplateNode writer template.Root
        writer.WriteEndObject())

let rec private readTemplateNode
    (kinds: Map<string, int>)
    (holes: Set<string>)
    (element: JsonElement)
    : Result<TemplateNode, PatternPortableError> =
    match requiredString element "tag" with
    | Error error -> Error error
    | Ok tag ->
        match tag with
        | "hole" ->
            match requiredString element "name" with
            | Error error -> Error error
            | Ok name ->
                if holes.Contains name then
                    Ok(TemplateHole name)
                else
                    Error(UndeclaredHole name)
        | "variable" ->
            match requiredString element "name", requiredString element "role" with
            | Ok name, Ok roleText ->
                match roleOfText roleText with
                | Ok role -> Ok(TemplateVariable(name, role))
                | Error reason -> malformed reason
            | Error error, _ -> Error error
            | _, Error error -> Error error
        | "number" ->
            match element.TryGetProperty "value" with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetDouble() with
                | true, number -> Ok(TemplateNumber number)
                | false, _ -> malformed "A template number has a non-numeric value."
            | _ -> malformed "A template number is missing value."
        | "text" -> requiredString element "value" |> Result.map TemplateText
        | "atom" -> requiredString element "value" |> Result.map TemplateAtom
        | "typed" ->
            match requiredString element "sort" with
            | Error error -> Error error
            | Ok sortName ->
                match element.TryGetProperty "child" with
                | true, child ->
                    readTemplateNode kinds holes child
                    |> Result.map (fun value -> TemplateTyped(sortName, value))
                | _ -> malformed "A typed template is missing child."
        | "constructor" ->
            match requiredString element "name" with
            | Error error -> Error error
            | Ok name ->
                match kinds.TryFind name with
                | None -> Error(MissingKind name)
                | Some arity ->
                    match element.TryGetProperty "children" with
                    | false, _ -> malformed "A template constructor is missing children."
                    | true, children ->
                        let parsed =
                            children.EnumerateArray()
                            |> Seq.map (readTemplateNode kinds holes)
                            |> Seq.toList
                            |> sequence

                        match parsed with
                        | Error error -> Error error
                        | Ok values ->
                            if List.length values <> arity then
                                Error(KindArityMismatch(name, arity, List.length values))
                            else
                                Ok(TemplateConstructor(name, values))
        | other -> malformed ("Unknown template tag: " + other)

let templateOfJson (text: string) : Result<PatternTemplate, PatternPortableError> =
    try
        use document = JsonDocument.Parse text
        let root = document.RootElement

        match requiredInt root "schema" with
        | Error error -> Error error
        | Ok schema when schema <> 1 -> Error(UnsupportedSchema schema)
        | Ok _ ->
            match requiredString root "form" with
            | Error error -> Error error
            | Ok form when form <> "template" -> Error(UnsupportedForm form)
            | Ok _ ->
                let holes =
                    match root.TryGetProperty "holes" with
                    | true, value when value.ValueKind = JsonValueKind.Array ->
                        value.EnumerateArray()
                        |> Seq.map (fun hole ->
                            match requiredString hole "name", requiredString hole "kind" with
                            | Ok name, Ok kind -> Ok { Name = name; Kind = kind }
                            | Error error, _ -> Error error
                            | _, Error error -> Error error)
                        |> Seq.toList
                        |> sequence
                    | _ -> malformed "The template has no holes array."

                let kinds =
                    match root.TryGetProperty "kinds" with
                    | true, value when value.ValueKind = JsonValueKind.Array ->
                        value.EnumerateArray() |> Seq.map readKind |> Seq.toList |> sequence
                    | _ -> malformed "The template has no kinds array."

                match holes, kinds with
                | Error error, _ -> Error error
                | _, Error error -> Error error
                | Ok holeList, Ok kindList ->
                    let duplicate =
                        holeList
                        |> List.countBy (fun hole -> hole.Name)
                        |> List.tryFind (fun (_, count) -> count > 1)

                    match duplicate with
                    | Some(name, _) -> Error(DuplicateHole name)
                    | None ->
                        let kindMap =
                            kindList
                            |> List.fold
                                (fun (accumulated: Result<Map<string, int>, PatternPortableError>) (kind: PatternKind) ->
                                    match accumulated with
                                    | Error error -> Error error
                                    | Ok current ->
                                        match current.TryFind kind.Name with
                                        | Some existing when existing <> kind.Arity ->
                                            Error(InconsistentKindArity(kind.Name, existing, kind.Arity))
                                        | Some _ -> Ok current
                                        | None -> Ok(current.Add(kind.Name, kind.Arity)))
                                (Ok Map.empty)

                        match kindMap with
                        | Error error -> Error error
                        | Ok kindsMap ->
                            let holeSet = holeList |> List.map (fun hole -> hole.Name) |> Set.ofList

                            match root.TryGetProperty "root" with
                            | false, _ -> malformed "The template has no root."
                            | true, node ->
                                readTemplateNode kindsMap holeSet node
                                |> Result.map (fun rootNode ->
                                    {
                                        SchemaVersion = 1
                                        Form = "template"
                                        Holes = holeList
                                        Kinds = kindList
                                        Root = rootNode
                                    })
    with exn ->
        Error(MalformedJson exn.Message)
