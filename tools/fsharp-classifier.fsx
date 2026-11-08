// Build-time F# classification read-off. This script uses FCS and emits the
// `parseReadOff` schema consumed by components/core/fsharp-abstraction.fs.
// It is intentionally not part of the product or the AOT runtime.
//
// Usage:
//   dotnet fsi tools/fsharp-classifier.fsx path/to/Source.fs

#r "/usr/share/dotnet/sdk/10.0.401/FSharp/FSharp.Compiler.Service.dll"
#r "../components/kernel/bin/Debug/net10.0/Scale.Kernel.dll"
#r "../components/core/bin/Debug/net10.0/Scale.Core.dll"

open System
open System.IO
open System.Text
open System.Text.Json
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open ExpressionLogic
open ExpressionPortable

let sourcePath =
    match fsi.CommandLineArgs |> Array.skip 1 with
    | [| path |] -> path
    | _ ->
        eprintfn "usage: dotnet fsi tools/fsharp-classifier.fsx path/to/Source.fs"
        exit 2

let source = File.ReadAllText sourcePath

let lineStarts =
    let starts = ResizeArray<int> [ 0 ]

    source.Split('\n')
    |> Array.iteri (fun index line ->
        if index > 0 then
            starts.Add(starts.[index - 1] + line.Length + 1))

    starts.ToArray()

let offsetOf (line: int) (column: int) : int =
    if line <= 0 then 0
    elif line > lineStarts.Length then source.Length
    else min source.Length (lineStarts.[line - 1] + column)

let nameOfPattern (pattern: SynPat) : string option =
    match pattern with
    | SynPat.LongIdent(longDotId = SynLongIdent(id = identifiers)) ->
        identifiers |> List.tryLast |> Option.map (fun identifier -> identifier.idText)
    | SynPat.Named(ident = SynIdent(identifier, _)) -> Some identifier.idText
    | _ -> None

let parametersOf (valueData: SynValData) : string list =
    match valueData with
    | SynValData(_, SynValInfo(arguments, _), _) ->
        arguments
        |> List.collect id
        |> List.choose (fun (SynArgInfo(_, _, identifier)) -> identifier |> Option.map (fun value -> value.idText))

let private identText (identifier: Ident) : string = identifier.idText

let rec private calledNames (expression: SynExpr) : string list =
    match expression with
    | SynExpr.App(funcExpr = functionExpression; argExpr = argument) ->
        let called =
            match functionExpression with
            | SynExpr.Ident identifier -> [ identText identifier ]
            | SynExpr.LongIdent(longDotId = SynLongIdent(id = identifiers)) ->
                identifiers |> List.tryLast |> Option.map identText |> Option.toList
            | _ -> calledNames functionExpression

        called @ calledNames argument
    | SynExpr.Paren(expr = inner)
    | SynExpr.Typed(expr = inner)
    | SynExpr.DotGet(expr = inner)
    | SynExpr.New(expr = inner)
    | SynExpr.Lambda(body = inner) -> calledNames inner
    | SynExpr.Sequential(expr1 = first; expr2 = second) -> calledNames first @ calledNames second
    | SynExpr.IfThenElse(ifExpr = condition; thenExpr = thenBranch; elseExpr = elseBranch) ->
        calledNames condition
        @ calledNames thenBranch
        @ (elseBranch |> Option.map calledNames |> Option.defaultValue [])
    | SynExpr.Match(clauses = clauses) ->
        clauses
        |> List.collect (fun (SynMatchClause(resultExpr = body)) -> calledNames body)
    | SynExpr.Tuple(exprs = expressions)
    | SynExpr.ArrayOrList(exprs = expressions) ->
        expressions |> List.collect calledNames
    | _ -> []

let rec private allIdentifiers (expression: SynExpr) : string list =
    match expression with
    | SynExpr.Ident identifier -> [ identText identifier ]
    | SynExpr.App(funcExpr = functionExpression; argExpr = argument) ->
        allIdentifiers functionExpression @ allIdentifiers argument
    | SynExpr.Paren(expr = inner)
    | SynExpr.Typed(expr = inner)
    | SynExpr.DotGet(expr = inner)
    | SynExpr.New(expr = inner)
    | SynExpr.Lambda(body = inner) -> allIdentifiers inner
    | SynExpr.Sequential(expr1 = first; expr2 = second) ->
        allIdentifiers first @ allIdentifiers second
    | SynExpr.IfThenElse(ifExpr = condition; thenExpr = thenBranch; elseExpr = elseBranch) ->
        allIdentifiers condition
        @ allIdentifiers thenBranch
        @ (elseBranch |> Option.map allIdentifiers |> Option.defaultValue [])
    | SynExpr.Match(clauses = clauses) ->
        clauses
        |> List.collect (fun (SynMatchClause(resultExpr = body)) -> allIdentifiers body)
    | SynExpr.Tuple(exprs = expressions)
    | SynExpr.ArrayOrList(exprs = expressions) ->
        expressions |> List.collect allIdentifiers
    | _ -> []

let private unsupported (what: string) : Result<Expression, string> =
    Error("the F# body uses an unsupported form: " + what)

let rec private translateExpression (expression: SynExpr) : Result<Expression, string> =
    match expression with
    | SynExpr.Const(SynConst.Int32 value, _) ->
        Ok(Literal(ExpressionLiteral.NumberLiteral(double value)))
    | SynExpr.Const(SynConst.Int64 value, _) ->
        Ok(Literal(ExpressionLiteral.NumberLiteral(double value)))
    | SynExpr.Const(SynConst.Double value, _) ->
        Ok(Literal(ExpressionLiteral.NumberLiteral value))
    | SynExpr.Const(SynConst.String(value, _, _), _) ->
        Ok(Literal(ExpressionLiteral.TextLiteral value))
    | SynExpr.Const(SynConst.Bool value, _) ->
        Ok(Literal(ExpressionLiteral.NumberLiteral(if value then 1.0 else 0.0)))
    | SynExpr.Const(SynConst.Unit, _) -> Ok(Block [])
    | SynExpr.Ident identifier -> Ok(Word identifier.idText)
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = identifiers)) ->
        match identifiers |> List.tryLast with
        | Some identifier -> Ok(Word identifier.idText)
        | None -> unsupported "an empty long identifier"
    | SynExpr.Paren(expr = inner)
    | SynExpr.Typed(expr = inner)
    | SynExpr.Do(expr = inner) -> translateExpression inner
    | SynExpr.App(funcExpr = functionExpression; argExpr = argument) ->
        let rec flatten (target: SynExpr) (arguments: SynExpr list) =
            match target with
            | SynExpr.App(funcExpr = nestedFunction; argExpr = nestedArgument) ->
                flatten nestedFunction (nestedArgument :: arguments)
            | _ -> target, arguments

        let target, arguments = flatten functionExpression [ argument ]

        match translateExpression target with
        | Error error -> Error error
        | Ok target ->
            arguments
            |> List.fold
                (fun accumulated argument ->
                    accumulated
                    |> Result.bind (fun current ->
                        translateExpression argument
                        |> Result.map (fun translated -> current @ [ translated ])))
                (Ok [])
            |> Result.map (fun arguments -> Call(target, arguments))
    | SynExpr.Lambda(args = SynSimplePats.SimplePats(pats = parameters); body = body) ->
        let names =
            parameters
            |> List.choose (function
                | SynSimplePat.Id(ident = identifier) -> Some identifier.idText
                | _ -> None)

        translateExpression body |> Result.map (fun body -> Lambda(names, body))
    | SynExpr.IfThenElse(ifExpr = condition; thenExpr = thenBranch; elseExpr = elseBranch) ->
        match translateExpression condition, translateExpression thenBranch with
        | Ok condition, Ok thenBranch ->
            match elseBranch with
            | None -> Ok(If(condition, thenBranch, Block []))
            | Some elseBranch ->
                translateExpression elseBranch
                |> Result.map (fun elseBranch -> If(condition, thenBranch, elseBranch))
        | Error error, _
        | _, Error error -> Error error
    | SynExpr.Sequential(expr1 = first; expr2 = second) ->
        match translateExpression first, translateExpression second with
        | Ok first, Ok second -> Ok(Block [ first; second ])
        | Error error, _
        | _, Error error -> Error error
    | SynExpr.LetOrUse letOrUse ->
        let bindings = letOrUse.Bindings
        let body = letOrUse.Body

        let rec lower (remaining: SynBinding list) (body: Expression) : Result<Expression, string> =
            match remaining with
            | [] -> Ok body
            | binding :: rest ->
                match binding with
                | SynBinding(headPat = pattern; valData = valueData; expr = value) ->
                    match nameOfPattern pattern with
                    | None -> unsupported "a let pattern without a name"
                    | Some name ->
                        let parameters = parametersOf valueData

                        match translateExpression value with
                        | Error error -> Error error
                        | Ok value ->
                            let value =
                                match parameters with
                                | [] -> value
                                | parameters -> Lambda(parameters, value)

                            lower rest (Let(name, value, body))

        translateExpression body
        |> Result.bind (fun body -> lower bindings body)
    | SynExpr.Match _ -> unsupported "a match expression"
    | SynExpr.Tuple(exprs = expressions)
    | SynExpr.ArrayOrList(exprs = expressions) ->
        expressions
        |> List.fold
            (fun accumulated expression ->
                accumulated
                |> Result.bind (fun current ->
                    translateExpression expression |> Result.map (fun translated -> current @ [ translated ])))
            (Ok [])
        |> Result.map Block
    | other -> unsupported (other.GetType().Name)

type RawBinding =
    {
        Name: string
        Parameters: string list
        Body: SynExpr
        Range: Range
    }

let bindingsOf (isRecursive: bool) (bindings: SynBinding list) : RawBinding list =
    bindings
    |> List.choose (fun (SynBinding(headPat = pattern; valData = valueData; expr = body; range = range)) ->
        nameOfPattern pattern
        |> Option.map (fun name ->
            {
                Name = name
                Parameters = parametersOf valueData
                Body = body
                Range = range
            }))

let classifyGroup
    (isRecursive: bool)
    (bindings: RawBinding list)
    : (RawBinding * string * string list * string list) list =
    let names = bindings |> List.map (fun binding -> binding.Name) |> Set.ofList

    if not isRecursive then
        bindings |> List.map (fun binding -> binding, "non-recursive", [], [])
    elif bindings.Length = 1 then
        let binding = bindings.Head
        let calls = calledNames binding.Body |> List.distinct

        let kind =
            if binding.Parameters.IsEmpty then
                if allIdentifiers binding.Body |> List.contains binding.Name then "recursive" else "non-recursive"
            elif calls |> List.contains binding.Name then
                "recursive"
            else
                "non-recursive"

        [ (binding, kind, calls, []) ]
    else
        let calls =
            bindings
            |> List.map (fun binding -> binding, calledNames binding.Body |> List.distinct)

        let recursive =
            calls
            |> List.exists (fun (_, called) -> called |> List.exists (fun name -> names.Contains name))

        if recursive then
            let mutual = names |> Set.toList
            calls |> List.map (fun (binding, called) -> binding, "mutually-recursive", called, mutual)
        else
            calls |> List.map (fun (binding, called) -> binding, "non-recursive", called, [])

let rec declarations
    (moduleDeclarations: SynModuleDecl list)
    : (RawBinding * string * string list * string list) list =
    moduleDeclarations
    |> List.collect (function
        | SynModuleDecl.Let(isRecursive, bindings, _, _) ->
            bindingsOf isRecursive bindings |> classifyGroup isRecursive
        | SynModuleDecl.NestedModule(decls = nested) -> declarations nested
        | _ -> [])

let parseTree () =
    let checker = FSharpChecker.Create()

    let projectOptions, _ =
        checker.GetProjectOptionsFromScript(sourcePath, SourceText.ofString source)
        |> Async.RunSynchronously

    let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions projectOptions

    checker.ParseFile(sourcePath, SourceText.ofString source, parsingOptions)
    |> Async.RunSynchronously

let parsed = parseTree ()

let errors =
    parsed.Diagnostics
    |> Array.filter (fun diagnostic -> diagnostic.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

if errors.Length > 0 then
    errors
    |> Array.iter (fun diagnostic -> eprintfn "%s: %s" diagnostic.ErrorNumberText diagnostic.Message)

    exit 1

let moduleName =
    match parsed.ParseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        modules
        |> List.tryHead
        |> Option.map (fun (SynModuleOrNamespace(longId = identifiers)) ->
            identifiers |> List.tryLast |> Option.map (fun identifier -> identifier.idText) |> Option.defaultValue (Path.GetFileNameWithoutExtension sourcePath))
        |> Option.defaultValue (Path.GetFileNameWithoutExtension sourcePath)
    | _ -> Path.GetFileNameWithoutExtension sourcePath

let classified =
    match parsed.ParseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        modules
        |> List.collect (fun (SynModuleOrNamespace(decls = moduleDeclarations)) -> declarations moduleDeclarations)
    | _ -> []

let stream = new MemoryStream()

(
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteNumber("schemaVersion", 1)
    writer.WriteString("module", moduleName)
    writer.WriteStartArray("bindings")

    for (binding, kind, calls, mutual) in classified do
        writer.WriteStartObject()
        writer.WriteString("name", binding.Name)
        writer.WriteStartArray("parameters")

        for parameter in binding.Parameters do
            writer.WriteStringValue parameter

        writer.WriteEndArray()
        writer.WriteString("kind", kind)

        if kind = "mutually-recursive" then
            writer.WriteStartArray("mutual")

            for name in mutual do
                writer.WriteStringValue name

            writer.WriteEndArray()

        match translateExpression binding.Body with
        | Error error ->
            eprintfn "%s: %s" binding.Name error
            exit 1
        | Ok body ->
            match ExpressionPortable.document body with
            | Error error ->
                eprintfn "%s: the expression body could not be documented" binding.Name
                exit 1
            | Ok document ->
                writer.WritePropertyName("body")
                writer.WriteRawValue(ExpressionPortable.toJson document)

        writer.WriteStartArray("calls")

        for call in calls do
            writer.WriteStringValue call

        writer.WriteEndArray()
        writer.WriteNumber("startOffset", offsetOf binding.Range.StartLine binding.Range.StartColumn)
        writer.WriteNumber("endOffset", offsetOf binding.Range.EndLine binding.Range.EndColumn)
        writer.WriteEndObject()

    writer.WriteEndArray()
    writer.WriteEndObject()
)

printfn "%s" (Encoding.UTF8.GetString(stream.ToArray()))
