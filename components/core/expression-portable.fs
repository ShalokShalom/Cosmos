module ExpressionPortable

// ============================================================
// Canonical JSON v1 for the general expression IR.
//
// The document carries the closed expression tree plus the canonical kind and
// predicate registries it references. Pattern clauses reuse the pattern
// portable writer/reader, so one schema owns both. Source maps and inferred
// hints are deliberately absent: they are unit metadata, not semantic nodes.
// ============================================================

open System.Text.Json
open PatternLogic
open ExpressionLogic
open PatternPortable

let private addKind
    (name: string)
    (arity: int)
    (kinds: Map<string, int>)
    : Result<Map<string, int>, PatternPortableError> =
    match kinds.TryFind name with
    | Some existing when existing <> arity -> Error(InconsistentKindArity(name, existing, arity))
    | Some _ -> Ok kinds
    | None -> Ok(kinds.Add(name, arity))

let private addPredicate
    (name: string)
    (version: int)
    (predicates: (string * int) list)
    : Result<(string * int) list, PatternPortableError> =
    match predicates |> List.tryFind (fun (candidate, _) -> candidate = name) with
    | Some(_, existing) when existing <> version -> Error(InconsistentPredicateVersion(name, existing, version))
    | Some _ -> Ok predicates
    | None -> Ok(predicates @ [ name, version ])

let rec private collect
    (expression: Expression)
    (kinds: Map<string, int>)
    (predicates: (string * int) list)
    : Result<Map<string, int> * (string * int) list, PatternPortableError> =
    let collectBody body =
        body
        |> List.fold
            (fun accumulated child ->
                match accumulated with
                | Error error -> Error error
                | Ok(currentKinds, currentPredicates) -> collect child currentKinds currentPredicates)
            (Ok(kinds, predicates))

    match expression with
    | Literal _
    | Word _
    | Hole -> Ok(kinds, predicates)
    | Call(target, arguments) -> collectBody (target :: arguments)
    | Partial(target, arguments) -> collectBody (target :: arguments)
    | ReversePipeline(target, value) -> collectBody [ target; value ]
    | Pipeline(value, stage, _) -> collectBody [ value; stage ]
    | Quotation body
    | Block body -> collectBody body
    | Let(_, value, body) -> collectBody [ value; body ]
    | Lambda(_, body) -> collect body kinds predicates
    | If(condition, thenBranch, elseBranch) -> collectBody [ condition; thenBranch; elseBranch ]
    | PatternMatch(subject, clauses) ->
        match collect subject kinds predicates with
        | Error error -> Error error
        | Ok(subjectKinds, subjectPredicates) ->
            clauses
            |> List.fold
                (fun accumulated clause ->
                    match accumulated with
                    | Error error -> Error error
                    | Ok(currentKinds, currentPredicates) ->
                        match PatternPortable.collectKinds clause.Pattern currentKinds with
                        | Error error -> Error error
                        | Ok(nextKinds) ->
                            let guardResult =
                                match clause.Guard with
                                | None -> Ok currentPredicates
                                | Some guard -> addPredicate guard.Predicate guard.Version currentPredicates

                            match guardResult with
                            | Error error -> Error error
                            | Ok(nextPredicates) ->
                                clause.Body
                                |> List.fold
                                    (fun nested child ->
                                        match nested with
                                        | Error error -> Error error
                                        | Ok(currentKinds, currentPredicates) ->
                                            collect child currentKinds currentPredicates)
                                    (Ok(nextKinds, nextPredicates)))
                (Ok(subjectKinds, subjectPredicates))
    | DeclareKind kind ->
        addKind kind.Name kind.Arity kinds
        |> Result.map (fun nextKinds -> nextKinds, predicates)
    | DeclarePredicate(name, version) ->
        addPredicate name version predicates
        |> Result.map (fun nextPredicates -> kinds, nextPredicates)
    | Condition(_, message) -> collect message kinds predicates
    | Restart(_, handler) -> collect handler kinds predicates
    | Generic(_, _, methods) -> collectBody methods
    | Message(_, fields) -> collectBody fields
    | Fail _ -> Ok(kinds, predicates)

let document (root: Expression) : Result<ExpressionDocument, PatternPortableError> =
    match collect root Map.empty [] with
    | Error error -> Error error
    | Ok(kinds, predicates) ->
        Ok
            {
                SchemaVersion = 1
                Form = "closed"
                Kinds =
                    kinds
                    |> Map.toList
                    |> List.map (fun (name, arity) -> { Name = name; Arity = arity })
                Predicates = predicates |> List.sortBy fst
                Root = root
            }

let private malformed (reason: string) : Result<'T, PatternPortableError> = Error(MalformedJson reason)

let private sequence results = PortableJson.sequence results

let private writeArray writer name items writeItem =
    PortableJson.writeArray writer name items writeItem

let private requiredString element name =
    PortableJson.requiredString element name
    |> Result.mapError (fun reason -> MalformedJson reason)

let private requiredInt element name =
    PortableJson.requiredInt element name
    |> Result.mapError (fun reason -> MalformedJson reason)

let private arrayElements element name =
    PortableJson.arrayElements element name
    |> Result.mapError (fun reason -> MalformedJson reason)

let private writeGuard (writer: Utf8JsonWriter) (guard: PatternGuard option) : unit =
    match guard with
    | None -> writer.WriteNull("guard")
    | Some value ->
        writer.WriteStartObject("guard")
        writer.WriteString("predicate", value.Predicate)
        writer.WriteNumber("version", value.Version)
        writer.WriteEndObject()

let rec writeExpression (writer: Utf8JsonWriter) (expression: Expression) : unit =
    writer.WriteStartObject()

    match expression with
    | Literal(ExpressionLiteral.NumberLiteral value) ->
        writer.WriteString("tag", "number")
        writer.WriteNumber("value", value)
    | Literal(ExpressionLiteral.TextLiteral value) ->
        writer.WriteString("tag", "text")
        writer.WriteString("value", value)
    | Literal(ExpressionLiteral.AtomLiteral value) ->
        writer.WriteString("tag", "atom")
        writer.WriteString("value", value)
    | Word name ->
        writer.WriteString("tag", "word")
        writer.WriteString("name", name)
    | Hole -> writer.WriteString("tag", "hole")
    | Partial(target, arguments) ->
        writer.WriteString("tag", "partial")
        writer.WritePropertyName("target")
        writeExpression writer target
        writeArray writer "arguments" arguments writeExpression
    | ReversePipeline(target, value) ->
        writer.WriteString("tag", "reverse-pipeline")
        writer.WritePropertyName("target")
        writeExpression writer target
        writer.WritePropertyName("value")
        writeExpression writer value
    | Call(target, arguments) ->
        writer.WriteString("tag", "call")
        writer.WritePropertyName("target")
        writeExpression writer target
        writeArray writer "arguments" arguments writeExpression
    | Pipeline(value, stage, style) ->
        writer.WriteString("tag", "pipeline")
        writer.WritePropertyName("value")
        writeExpression writer value
        writer.WritePropertyName("stage")
        writeExpression writer stage

        writer.WriteString(
            "style",
            match style with
            | LastArgument -> "last"
            | FirstArgument -> "first"
        )
    | Quotation body ->
        writer.WriteString("tag", "quotation")
        writeArray writer "body" body writeExpression
    | Block body ->
        writer.WriteString("tag", "block")
        writeArray writer "body" body writeExpression
    | Let(name, value, body) ->
        writer.WriteString("tag", "let")
        writer.WriteString("name", name)
        writer.WritePropertyName("value")
        writeExpression writer value
        writer.WritePropertyName("body")
        writeExpression writer body
    | Lambda(parameters, body) ->
        writer.WriteString("tag", "lambda")
        writeArray writer "parameters" parameters (fun target value -> target.WriteStringValue value)
        writer.WritePropertyName("body")
        writeExpression writer body
    | If(condition, thenBranch, elseBranch) ->
        writer.WriteString("tag", "if")
        writer.WritePropertyName("condition")
        writeExpression writer condition
        writer.WritePropertyName("then")
        writeExpression writer thenBranch
        writer.WritePropertyName("else")
        writeExpression writer elseBranch
    | PatternMatch(subject, clauses) ->
        writer.WriteString("tag", "match")
        writer.WritePropertyName("subject")
        writeExpression writer subject

        writeArray writer "clauses" clauses (fun target clause ->
            target.WriteStartObject()
            target.WritePropertyName("pattern")
            PatternPortable.writePattern target clause.Pattern
            writeGuard target clause.Guard
            writeArray target "body" clause.Body writeExpression
            target.WriteEndObject())
    | DeclareKind kind ->
        writer.WriteString("tag", "declare-kind")
        writer.WriteString("name", kind.Name)
        writer.WriteNumber("arity", kind.Arity)
    | DeclarePredicate(name, version) ->
        writer.WriteString("tag", "declare-predicate")
        writer.WriteString("name", name)
        writer.WriteNumber("version", version)
    | Condition(name, message) ->
        writer.WriteString("tag", "condition")
        writer.WriteString("name", name)
        writer.WritePropertyName("message")
        writeExpression writer message
    | Restart(name, handler) ->
        writer.WriteString("tag", "restart")
        writer.WriteString("name", name)
        writer.WritePropertyName("handler")
        writeExpression writer handler
    | Generic(name, arity, methods) ->
        writer.WriteString("tag", "generic")
        writer.WriteString("name", name)
        writer.WriteNumber("arity", arity)
        writeArray writer "methods" methods writeExpression
    | Message(kind, fields) ->
        writer.WriteString("tag", "message")
        writer.WriteString("kind", kind)
        writeArray writer "fields" fields writeExpression
    | Fail reason ->
        writer.WriteString("tag", "fail")
        writer.WriteString("reason", reason)

    writer.WriteEndObject()

let toJson (document: ExpressionDocument) : string =
    let stream = new System.IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     writer.WriteStartObject()
     writer.WriteNumber("schema", document.SchemaVersion)
     writer.WriteString("form", document.Form)

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

     writer.WritePropertyName("root")
     writeExpression writer document.Root
     writer.WriteEndObject()
     writer.Flush())

    System.Text.Encoding.UTF8.GetString(stream.ToArray())

let rec readExpression
    (kinds: Map<string, int>)
    (predicates: (string * int) list)
    (element: JsonElement)
    : Result<Expression, PatternPortableError> =
    match requiredString element "tag" with
    | Error error -> Error error
    | Ok tag ->
        match tag with
        | "number" ->
            match element.TryGetProperty "value" with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetDouble() with
                | true, number -> Ok(Literal(ExpressionLiteral.NumberLiteral number))
                | false, _ -> malformed "A number literal has a non-numeric value."
            | _ -> malformed "A number literal is missing value."
        | "text" ->
            requiredString element "value"
            |> Result.map (fun value -> Literal(ExpressionLiteral.TextLiteral value))
        | "atom" ->
            requiredString element "value"
            |> Result.map (fun value -> Literal(ExpressionLiteral.AtomLiteral value))
        | "word" -> requiredString element "name" |> Result.map Word
        | "hole" -> Ok Hole
        | "partial" ->
            match element.TryGetProperty "target" with
            | false, _ -> malformed "A partial application is missing target."
            | true, target ->
                match readExpression kinds predicates target, arrayElements element "arguments" with
                | Error error, _ -> Error error
                | _, Error error -> Error error
                | Ok targetValue, Ok argumentElements ->
                    argumentElements
                    |> List.map (readExpression kinds predicates)
                    |> sequence
                    |> Result.map (fun arguments -> Partial(targetValue, arguments))
        | "reverse-pipeline" ->
            match element.TryGetProperty "target", element.TryGetProperty "value" with
            | (true, target), (true, value) ->
                match readExpression kinds predicates target, readExpression kinds predicates value with
                | Ok targetValue, Ok valueExpression -> Ok(ReversePipeline(targetValue, valueExpression))
                | Error error, _ -> Error error
                | _, Error error -> Error error
            | _ -> malformed "A reverse pipeline is missing target or value."
        | "call" ->
            match element.TryGetProperty "target" with
            | false, _ -> malformed "A call is missing target."
            | true, target ->
                match readExpression kinds predicates target, arrayElements element "arguments" with
                | Error error, _ -> Error error
                | _, Error error -> Error error
                | Ok targetValue, Ok argumentElements ->
                    argumentElements
                    |> List.map (readExpression kinds predicates)
                    |> sequence
                    |> Result.map (fun arguments -> Call(targetValue, arguments))
        | "pipeline" ->
            match requiredString element "style" with
            | Error error -> Error error
            | Ok style ->
                let pipelineStyle =
                    match style with
                    | "last" -> Ok LastArgument
                    | "first" -> Ok FirstArgument
                    | _ -> malformed "A pipeline has an unknown style."

                match pipelineStyle with
                | Error error -> Error error
                | Ok pipelineStyle ->
                    match element.TryGetProperty "value", element.TryGetProperty "stage" with
                    | (true, value), (true, stage) ->
                        match readExpression kinds predicates value, readExpression kinds predicates stage with
                        | Ok valueExpression, Ok stageExpression ->
                            Ok(Pipeline(valueExpression, stageExpression, pipelineStyle))
                        | Error error, _ -> Error error
                        | _, Error error -> Error error
                    | _ -> malformed "A pipeline is missing value or stage."
        | "quotation" ->
            arrayElements element "body"
            |> Result.bind (fun body -> body |> List.map (readExpression kinds predicates) |> sequence)
            |> Result.map Quotation
        | "block" ->
            arrayElements element "body"
            |> Result.bind (fun body -> body |> List.map (readExpression kinds predicates) |> sequence)
            |> Result.map Block
        | "let" ->
            match requiredString element "name" with
            | Error error -> Error error
            | Ok name ->
                match element.TryGetProperty "value", element.TryGetProperty "body" with
                | (true, value), (true, body) ->
                    match readExpression kinds predicates value, readExpression kinds predicates body with
                    | Ok valueExpression, Ok bodyExpression -> Ok(Let(name, valueExpression, bodyExpression))
                    | Error error, _ -> Error error
                    | _, Error error -> Error error
                | _ -> malformed "A let is missing value or body."
        | "lambda" ->
            match arrayElements element "parameters", element.TryGetProperty "body" with
            | Error error, _ -> Error error
            | _, (false, _) -> malformed "A lambda is missing body."
            | Ok parameterElements, (true, body) ->
                let parameters =
                    parameterElements
                    |> List.map (fun parameter ->
                        if parameter.ValueKind = JsonValueKind.String then
                            Ok(parameter.GetString())
                        else
                            malformed "A lambda parameter is not a string.")

                match sequence parameters, readExpression kinds predicates body with
                | Ok parameterNames, Ok bodyExpression -> Ok(Lambda(parameterNames, bodyExpression))
                | Error error, _ -> Error error
                | _, Error error -> Error error
        | "if" ->
            match element.TryGetProperty "condition", element.TryGetProperty "then", element.TryGetProperty "else" with
            | (true, condition), (true, thenBranch), (true, elseBranch) ->
                match
                    readExpression kinds predicates condition,
                    readExpression kinds predicates thenBranch,
                    readExpression kinds predicates elseBranch
                with
                | Ok conditionExpression, Ok thenExpression, Ok elseExpression ->
                    Ok(If(conditionExpression, thenExpression, elseExpression))
                | Error error, _, _ -> Error error
                | _, Error error, _ -> Error error
                | _, _, Error error -> Error error
            | _ -> malformed "An if is missing condition, then, or else."
        | "match" ->
            match element.TryGetProperty "subject" with
            | false, _ -> malformed "A match is missing subject."
            | true, subject ->
                match readExpression kinds predicates subject, arrayElements element "clauses" with
                | Error error, _ -> Error error
                | _, Error error -> Error error
                | Ok subjectExpression, Ok clauseElements ->
                    clauseElements
                    |> List.map (readClause kinds predicates)
                    |> sequence
                    |> Result.map (fun clauses -> PatternMatch(subjectExpression, clauses))
        | "declare-kind" ->
            match requiredString element "name", requiredInt element "arity" with
            | Ok name, Ok arity ->
                match kinds.TryFind name with
                | Some existing when existing <> arity -> Error(KindArityMismatch(name, existing, arity))
                | Some _ -> Ok(DeclareKind { Name = name; Arity = arity })
                | None -> Error(MissingKind name)
            | Error error, _ -> Error error
            | _, Error error -> Error error
        | "declare-predicate" ->
            match requiredString element "name", requiredInt element "version" with
            | Ok name, Ok version ->
                match predicates |> List.tryFind (fun (candidate, _) -> candidate = name) with
                | Some(_, existing) when existing = version -> Ok(DeclarePredicate(name, version))
                | _ -> Error(MissingPredicateVersion name)
            | Error error, _ -> Error error
            | _, Error error -> Error error
        | "condition" ->
            match requiredString element "name", element.TryGetProperty "message" with
            | Error error, _ -> Error error
            | _, (false, _) -> malformed "A condition is missing message."
            | Ok name, (true, message) ->
                readExpression kinds predicates message
                |> Result.map (fun messageExpression -> Condition(name, messageExpression))
        | "restart" ->
            match requiredString element "name", element.TryGetProperty "handler" with
            | Error error, _ -> Error error
            | _, (false, _) -> malformed "A restart is missing handler."
            | Ok name, (true, handler) ->
                readExpression kinds predicates handler
                |> Result.map (fun handlerExpression -> Restart(name, handlerExpression))
        | "generic" ->
            match requiredString element "name", requiredInt element "arity", arrayElements element "methods" with
            | Ok name, Ok arity, Ok methodElements ->
                methodElements
                |> List.map (readExpression kinds predicates)
                |> sequence
                |> Result.map (fun methods -> Generic(name, arity, methods))
            | Error error, _, _ -> Error error
            | _, Error error, _ -> Error error
            | _, _, Error error -> Error error
        | "message" ->
            match requiredString element "kind", arrayElements element "fields" with
            | Ok kind, Ok fieldElements ->
                fieldElements
                |> List.map (readExpression kinds predicates)
                |> sequence
                |> Result.map (fun fields -> Message(kind, fields))
            | Error error, _ -> Error error
            | _, Error error -> Error error
        | other -> malformed ("Unknown expression tag: " + other)

and private readClause
    (kinds: Map<string, int>)
    (predicates: (string * int) list)
    (element: JsonElement)
    : Result<ExpressionClause, PatternPortableError> =
    match element.TryGetProperty "pattern" with
    | false, _ -> malformed "A match clause is missing pattern."
    | true, patternElement ->
        match PatternPortable.readPattern kinds patternElement with
        | Error error -> Error error
        | Ok pattern ->
            let guard =
                match element.TryGetProperty "guard" with
                | false, _ -> Ok None
                | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
                | true, value ->
                    match requiredString value "predicate", requiredInt value "version" with
                    | Ok predicate, Ok version ->
                        match predicates |> List.tryFind (fun (candidate, _) -> candidate = predicate) with
                        | Some(_, expected) when expected = version ->
                            Ok(
                                Some
                                    {
                                        Predicate = predicate
                                        Version = version
                                    }
                            )
                        | _ -> Error(MissingPredicateVersion predicate)
                    | Error error, _ -> Error error
                    | _, Error error -> Error error
                | _ -> malformed "A clause guard is neither an object nor null."

            match guard, arrayElements element "body" with
            | Ok guardValue, Ok bodyElements ->
                bodyElements
                |> List.map (readExpression kinds predicates)
                |> sequence
                |> Result.map (fun body ->
                    ({
                        Pattern = pattern
                        Guard = guardValue
                        Body = body
                    }
                    : ExpressionClause))
            | Error error, _ -> Error error
            | _, Error error -> Error error

let ofJson (text: string) : Result<ExpressionDocument, PatternPortableError> =
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
                match arrayElements root "kinds", arrayElements root "predicates" with
                | Error error, _ -> Error error
                | _, Error error -> Error error
                | Ok kindElements, Ok predicateElements ->
                    let kinds = kindElements |> List.map PatternPortable.readKind |> sequence

                    let predicates =
                        predicateElements |> List.map PatternPortable.readPredicate |> sequence

                    match kinds, predicates with
                    | Error error, _ -> Error error
                    | _, Error error -> Error error
                    | Ok kindList, Ok predicateList ->
                        let kindMap =
                            kindList
                            |> List.fold
                                (fun (accumulated: Result<Map<string, int>, PatternPortableError>) (kind: PatternKind) ->
                                    match accumulated with
                                    | Error error -> Error error
                                    | Ok current -> addKind kind.Name kind.Arity current)
                                (Ok Map.empty)

                        match kindMap with
                        | Error error -> Error error
                        | Ok kindsMap ->
                            match root.TryGetProperty "root" with
                            | false, _ -> malformed "The expression document has no root."
                            | true, rootElement ->
                                readExpression kindsMap predicateList rootElement
                                |> Result.map (fun rootExpression ->
                                    {
                                        SchemaVersion = 1
                                        Form = "closed"
                                        Kinds = kindList
                                        Predicates = predicateList
                                        Root = rootExpression
                                    })
    with exn ->
        Error(MalformedJson exn.Message)
