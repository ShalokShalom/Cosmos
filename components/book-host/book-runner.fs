module BookRunner

// ============================================================
// Books and spells (D184). A book is a package built from a `.book` file; a
// spell is a book with an exported `main`. The runner binds the definitions in
// a fresh frame, then calls the requested word with the caller's values.
// ============================================================

open Types
open LabelSets
open RowTypes

type BookKind =
    | Book
    | Spell

type LoadedBook =
    {
        Path: string
        Document: ProseParser.BookDocument
        Package: PackageLogic.PackageDocument
        Opened: LoadedBook list
    }

/// The ordered project index. Every dependency appears before the file that
/// needs it; a cycle, duplicate, or out-of-order dependency is refused.
type IndexEntry =
    {
        Name: string
        Path: string
        Dependencies: string list
    }

type BookIndex = { Entries: IndexEntry list }

type IndexError =
    | DuplicateIndexEntry of string
    | MissingIndexDependency of string * string
    | OutOfOrderIndexDependency of string * string

let describeIndexError (error: IndexError) : string =
    match error with
    | DuplicateIndexEntry name -> "the index contains " + name + " twice"
    | MissingIndexDependency(entry, dependency) -> "the index entry " + entry + " depends on missing " + dependency
    | OutOfOrderIndexDependency(entry, dependency) ->
        "the index entry " + entry + " depends on " + dependency + ", which is later"

let private hash (text: string) : string =
    use sha = System.Security.Cryptography.SHA256.Create()

    sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes text)
    |> Array.map (fun byte -> byte.ToString("x2"))
    |> String.concat ""

let private describePackageError (error: PackagePortable.PackageError) : string =
    match error with
    | PackagePortable.ExpressionError _ -> "a book definition is not portable"
    | PackagePortable.DuplicateUnit name -> "the book defines the unit " + name + " twice"
    | PackagePortable.UnknownExportUnit name -> "the book exports an unknown unit " + name
    | PackagePortable.MissingDependency(unitName, dependency) ->
        "the book unit " + unitName + " depends on an unknown unit " + dependency
    | PackagePortable.EmptySourceHash subject -> "the book unit " + subject + " has no source hash"
    | PackagePortable.InvalidUnitArity name -> "the book unit " + name + " has a negative arity"
    | PackagePortable.InvalidUnitCapture(name, slot) ->
        "the book unit " + name + " has a negative capture slot " + string slot
    | PackagePortable.DuplicateGroup name -> "the book defines the group " + name + " twice"
    | PackagePortable.EmptyUnitGroup name -> "the book group " + name + " has no units"
    | PackagePortable.DuplicateGroupUnit(group, name) -> "the book group " + group + " contains " + name + " twice"
    | PackagePortable.UnknownGroupUnit(group, name) -> "the book group " + group + " names an unknown unit " + name
    | PackagePortable.InconsistentGroupOrder name ->
        "the book group " + name + " has an inconsistent initialization order"
    | PackagePortable.InconsistentGroupClassification name ->
        "the book group " + name + " has an inconsistent classification"
    | PackagePortable.MalformedPackage reason -> reason

let private vocabularyOf (state: EvaluationState) : Result<VocabularyEntry list, string> =
    match VocabularyInImage.readVocabulary (Some state.VocabularyTable) state.Store with
    | Ok vocabulary -> Ok vocabulary
    | Error reason -> Error reason

let private exportOf (definition: ProseParser.BookDefinition) : PackageLogic.PackageExport =
    {
        Name = definition.Name
        Unit = definition.Name
    }

let private resolvePath (state: EvaluationState) (path: string) : string =
    if System.IO.Path.IsPathRooted path then
        path
    else
        let baseDirectory =
            state.BookBaseDirectory
            |> Option.defaultValue (System.IO.Directory.GetCurrentDirectory())

        System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, path))

let rec private patternNames (pattern: PatternLogic.Pattern) : Set<string> =
    match pattern with
    | PatternLogic.Wildcard
    | PatternLogic.NumberLiteral _
    | PatternLogic.TextLiteral _
    | PatternLogic.AtomLiteral _ -> Set.empty
    | PatternLogic.Variable(_, PatternLogic.Pin) -> Set.empty
    | PatternLogic.Variable(name, _) -> Set.singleton name
    | PatternLogic.Constructor(_, children) ->
        children
        |> List.fold (fun names child -> Set.union names (patternNames child)) Set.empty
    | PatternLogic.TypedPattern(_, child) -> patternNames child

/// The current book contract is ordered and non-recursive. A call to the
/// current or a later definition is refused before the package is built; it is
/// never allowed to fall through to a bare `CallWord` that would push the
/// closure as a value.
let private validateDefinition
    (names: Set<string>)
    (allowed: Set<string>)
    (definition: ProseParser.BookDefinition)
    : Result<unit, string> =
    let refuse (name: string) : Result<unit, string> =
        Error(
            "recursive or forward book word "
            + name
            + " in "
            + definition.Name
            + "; a book definition may call only earlier definitions"
        )

    let rec walk (bound: Set<string>) (expression: ExpressionLogic.Expression) : Result<unit, string> =
        let checkWord (name: string) : Result<unit, string> =
            if names.Contains name && not (bound.Contains name) && not (allowed.Contains name) then
                refuse name
            else
                Ok()

        let walkAll (currentBound: Set<string>) (expressions: ExpressionLogic.Expression list) : Result<unit, string> =
            expressions
            |> List.fold (fun state child -> state |> Result.bind (fun () -> walk currentBound child)) (Ok())

        match expression with
        | ExpressionLogic.Literal _ -> Ok()
        | ExpressionLogic.Word name -> checkWord name
        | ExpressionLogic.Call(ExpressionLogic.Word name, arguments) ->
            checkWord name |> Result.bind (fun () -> walkAll bound arguments)
        | ExpressionLogic.Call(target, arguments) ->
            walk bound target |> Result.bind (fun () -> walkAll bound arguments)
        | ExpressionLogic.Partial(target, arguments) ->
            walk bound target |> Result.bind (fun () -> walkAll bound arguments)
        | ExpressionLogic.Hole -> Ok()
        | ExpressionLogic.Pipeline(value, stage, _) -> walk bound value |> Result.bind (fun () -> walk bound stage)
        | ExpressionLogic.ReversePipeline(target, value) ->
            walk bound target |> Result.bind (fun () -> walk bound value)
        | ExpressionLogic.Quotation body
        | ExpressionLogic.Block body -> walkAll bound body
        | ExpressionLogic.Let(name, value, body) ->
            walk bound value |> Result.bind (fun () -> walk (bound.Add name) body)
        | ExpressionLogic.Lambda(parameters, body) ->
            let nextBound =
                parameters
                |> List.fold (fun (names: Set<string>) (name: string) -> names.Add name) bound

            walk nextBound body
        | ExpressionLogic.If(condition, thenBranch, elseBranch) ->
            walk bound condition
            |> Result.bind (fun () -> walk bound thenBranch)
            |> Result.bind (fun () -> walk bound elseBranch)
        | ExpressionLogic.PatternMatch(subject, clauses) ->
            walk bound subject
            |> Result.bind (fun () ->
                clauses
                |> List.fold
                    (fun (state: Result<unit, string>) (clause: ExpressionLogic.ExpressionClause) ->
                        state
                        |> Result.bind (fun () ->
                            let clauseBound =
                                patternNames clause.Pattern
                                |> Set.fold (fun (names: Set<string>) (name: string) -> names.Add name) bound

                            walkAll clauseBound clause.Body))
                    (Ok()))
        | ExpressionLogic.DeclareKind _
        | ExpressionLogic.DeclarePredicate _ -> Ok()
        | ExpressionLogic.Condition(_, message) -> walk bound message
        | ExpressionLogic.Restart(_, handler) -> walk bound handler
        | ExpressionLogic.Generic(_, _, methods) -> walkAll bound methods
        | ExpressionLogic.Message(_, fields) -> walkAll bound fields
        | ExpressionLogic.Fail _ -> Ok()

    walk (Set.ofList definition.Parameters) definition.Body

let private validateBook (document: ProseParser.BookDocument) : Result<unit, string> =
    let names =
        document.Definitions
        |> List.map (fun definition -> definition.Name)
        |> Set.ofList

    let rec loop (allowed: Set<string>) (remaining: ProseParser.BookDefinition list) =
        match remaining with
        | [] -> Ok()
        | definition :: rest ->
            match validateDefinition names allowed definition with
            | Error reason -> Error reason
            | Ok() -> loop (allowed.Add definition.Name) rest

    loop Set.empty document.Definitions

let private definitionReferences (names: Set<string>) (definition: ProseParser.BookDefinition) : Set<string> =
    let rec walk (bound: Set<string>) (expression: ExpressionLogic.Expression) : Set<string> =
        let walkAll (currentBound: Set<string>) (expressions: ExpressionLogic.Expression list) =
            expressions
            |> List.fold (fun accumulated child -> Set.union accumulated (walk currentBound child)) Set.empty

        let reference (name: string) (accumulated: Set<string>) =
            if names.Contains name && not (bound.Contains name) then
                accumulated.Add name
            else
                accumulated

        match expression with
        | ExpressionLogic.Literal _ -> Set.empty
        | ExpressionLogic.Word name -> reference name Set.empty
        | ExpressionLogic.Call(ExpressionLogic.Word name, arguments) -> reference name (walkAll bound arguments)
        | ExpressionLogic.Call(target, arguments) -> Set.union (walk bound target) (walkAll bound arguments)
        | ExpressionLogic.Partial(target, arguments) -> Set.union (walk bound target) (walkAll bound arguments)
        | ExpressionLogic.Hole -> Set.empty
        | ExpressionLogic.Pipeline(value, stage, _) -> Set.union (walk bound value) (walk bound stage)
        | ExpressionLogic.ReversePipeline(target, value) -> Set.union (walk bound target) (walk bound value)
        | ExpressionLogic.Quotation body
        | ExpressionLogic.Block body -> walkAll bound body
        | ExpressionLogic.Let(name, value, body) -> Set.union (walk bound value) (walk (bound.Add name) body)
        | ExpressionLogic.Lambda(parameters, body) ->
            let nextBound =
                parameters
                |> List.fold (fun (current: Set<string>) (name: string) -> current.Add name) bound

            walk nextBound body
        | ExpressionLogic.If(condition, thenBranch, elseBranch) ->
            Set.union (walk bound condition) (Set.union (walk bound thenBranch) (walk bound elseBranch))
        | ExpressionLogic.PatternMatch(subject, clauses) ->
            let clauseReferences =
                clauses
                |> List.fold
                    (fun accumulated (clause: ExpressionLogic.ExpressionClause) ->
                        let clauseBound =
                            patternNames clause.Pattern
                            |> Set.fold (fun (current: Set<string>) (name: string) -> current.Add name) bound

                        Set.union accumulated (walkAll clauseBound clause.Body))
                    Set.empty

            Set.union (walk bound subject) clauseReferences
        | ExpressionLogic.DeclareKind _
        | ExpressionLogic.DeclarePredicate _ -> Set.empty
        | ExpressionLogic.Condition(_, message) -> walk bound message
        | ExpressionLogic.Restart(_, handler) -> walk bound handler
        | ExpressionLogic.Generic(_, _, methods) -> walkAll bound methods
        | ExpressionLogic.Message(_, fields) -> walkAll bound fields
        | ExpressionLogic.Fail _ -> Set.empty

    walk (Set.ofList definition.Parameters) definition.Body

let private unitOf
    (names: Set<string>)
    (sourceHash: string)
    (definition: ProseParser.BookDefinition)
    : PackageLogic.PackageUnit =
    let root =
        if List.isEmpty definition.Parameters then
            definition.Body
        else
            ExpressionLogic.Lambda(definition.Parameters, definition.Body)

    let definitionHash =
        match ExpressionPortable.document root with
        | Error _ -> sourceHash
        | Ok document -> hash (ExpressionPortable.toJson document)

    {
        Name = definition.Name
        SourceHash = definitionHash
        Dependencies = definitionReferences names definition |> Set.toList |> List.sort
        Arity = List.length definition.Parameters
        Captures = []
        Root = root
    }

let private validateMarkers (document: ProseParser.BookDocument) : Result<unit, string> =
    let names =
        document.Definitions
        |> List.map (fun definition -> definition.Name)
        |> Set.ofList

    document.Definitions
    |> List.tryPick (fun definition ->
        let references = definitionReferences names definition

        match definition.Marker with
        | ProseParser.Logic when references.Contains definition.Name ->
            Some("book word " + definition.Name + " is marked logic but calls itself; use cycle")
        | ProseParser.Cycle when references.IsEmpty ->
            Some("book word " + definition.Name + " is marked cycle but calls no book word")
        | _ -> None)
    |> function
        | Some reason -> Error reason
        | None -> Ok()

let private bookCache =
    System.Collections.Concurrent.ConcurrentDictionary<string, LoadedBook>()

/// Clears the derived book cache. It is execution state and is never serialized.
let clearCache () : unit = bookCache.Clear()

let private parseBookSource (state: EvaluationState) (source: string) : Result<ProseParser.BookDocument, string> =
    match vocabularyOf state with
    | Error reason -> Error reason
    | Ok vocabulary ->
        match ProseParser.parseBook vocabulary source with
        | Error error -> Error(ProseParser.describeError error)
        | Ok document -> validateMarkers document |> Result.map (fun () -> document)

let private definitionCapabilities (definition: ProseParser.BookDefinition) : string list =
    let root =
        if List.isEmpty definition.Parameters then
            definition.Body
        else
            ExpressionLogic.Lambda(definition.Parameters, definition.Body)

    match EvaluatorLowering.lowerExpression root with
    | Error _ -> []
    | Ok program ->
        (program.Instructions
         :: (program.Units |> List.map (fun unit -> unit.Instructions)))
        |> List.collect id
        |> List.choose (function
            | LoweredInstructions.CallWord(name, _) when HostWords.tryDecode name |> Option.isSome -> Some "shell"
            | LoweredInstructions.CallWord(name, _) when HostWords.tryDecodeBook name |> Option.isSome -> Some "book"
            | _ -> None)
        |> List.distinct

let private buildBook
    (resolved: string)
    (sourceHash: string)
    (document: ProseParser.BookDocument)
    (included: LoadedBook list)
    : Result<LoadedBook, string> =
    let name = System.IO.Path.GetFileNameWithoutExtension resolved

    let includedDefinitions =
        included |> List.collect (fun book -> book.Document.Definitions)

    let allDefinitions = includedDefinitions @ document.Definitions

    let names =
        allDefinitions |> List.map (fun definition -> definition.Name) |> Set.ofList

    let exports =
        document.Definitions
        |> List.filter (fun definition -> not definition.Synthetic)
        |> List.map exportOf

    let imports: PackageLogic.PackageImport list =
        included
        |> List.map (fun book ->
            {
                Name = book.Package.Name
                Version = book.Package.Version
            })

    let capabilities =
        (included |> List.collect (fun book -> book.Package.Capabilities))
        @ (document.Definitions |> List.collect definitionCapabilities)
        |> List.distinct

    let units =
        (included |> List.collect (fun book -> book.Package.Units))
        @ (document.Definitions |> List.map (unitOf names sourceHash))

    let group: PackageLogic.PackageUnitGroup =
        {
            Name = name
            Units = allDefinitions |> List.map (fun definition -> definition.Name)
            InitOrder = allDefinitions |> List.map (fun definition -> definition.Name)
            Classification =
                allDefinitions
                |> List.map (fun definition ->
                    {
                        Name = definition.Name
                        Kind =
                            match definition.Marker with
                            | ProseParser.Logic ->
                                if List.isEmpty definition.Parameters then
                                    PackageLogic.NonRecursiveValue
                                else
                                    PackageLogic.NonRecursiveFunction
                            | _ ->
                                if List.isEmpty definition.Parameters then
                                    PackageLogic.RecursiveValue
                                else
                                    PackageLogic.RecursiveFunction
                    })
        }

    match PackagePortable.document name "1" sourceHash imports exports capabilities [ group ] units with
    | Error error -> Error(describePackageError error)
    | Ok package ->
        Ok
            {
                Path = resolved
                Document =
                    { document with
                        Definitions = allDefinitions
                    }
                Package = package
                Opened = included
            }

let private openPath (parent: string) (name: string) : string =
    let directory =
        System.IO.Path.GetDirectoryName parent
        |> Option.ofObj
        |> Option.defaultValue (System.IO.Directory.GetCurrentDirectory())

    System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, name + ".book"))

let rec private loadInternal
    (state: EvaluationState)
    (resolved: string)
    (stack: Set<string>)
    : Result<LoadedBook, string> =
    try
        let source = System.IO.File.ReadAllText resolved
        let sourceHash = hash source
        let cacheKey = resolved + "|" + sourceHash

        match bookCache.TryGetValue cacheKey with
        | true, cached when cached.Opened.IsEmpty -> Ok cached
        | _ ->
            match parseBookSource state source with
            | Error reason -> Error reason
            | Ok document ->
                let openPaths = document.Opens |> List.map (openPath resolved)
                let distinctPaths = openPaths |> List.distinct

                if distinctPaths.Length <> openPaths.Length then
                    Error("the book opens the same module twice")
                else
                    let included =
                        openPaths
                        |> List.fold
                            (fun accumulated path ->
                                accumulated
                                |> Result.bind (fun books ->
                                    if stack.Contains path then
                                        Error("book open cycle at " + path)
                                    else
                                        loadInternal state path (stack.Add resolved)
                                        |> Result.map (fun book -> books @ [ book ])))
                            (Ok [])

                    match included with
                    | Error reason -> Error reason
                    | Ok included ->
                        match buildBook resolved sourceHash document included with
                        | Error reason -> Error reason
                        | Ok book ->
                            if document.Opens.IsEmpty then
                                bookCache.[cacheKey] <- book

                            Ok book
    with error ->
        Error("the book could not be read: " + error.Message)

/// Loads a book from a `.book` source. The file stem is the package name, the
/// version is 1, and the source hash is the SHA-256 of the file. An `open name`
/// resolves to `name.book` beside the opening book. A parsed book is cached by
/// resolved path and source hash; a book with opens is rebuilt so an opened
/// change cannot leave a stale parent.
let load (state: EvaluationState) (path: string) : Result<LoadedBook, string> =
    loadInternal state (resolvePath state path) Set.empty

let rec private flattenIndex (book: LoadedBook) : IndexEntry list =
    (book.Opened |> List.collect flattenIndex)
    @ [
        {
            Name = book.Package.Name
            Path = book.Path
            Dependencies = book.Opened |> List.map (fun opened -> opened.Package.Name)
        }
    ]

/// Validates an already ordered file list. Dependencies must be earlier.
let indexEntries (entries: IndexEntry list) : Result<BookIndex, IndexError> =
    let names = entries |> List.map (fun entry -> entry.Name) |> Set.ofList

    let duplicate =
        entries
        |> List.countBy (fun entry -> entry.Name)
        |> List.tryFind (fun (_, count) -> count > 1)
        |> Option.map fst

    match duplicate with
    | Some name -> Error(DuplicateIndexEntry name)
    | None ->
        let rec check (seen: Set<string>) (remaining: IndexEntry list) =
            match remaining with
            | [] -> Ok { Entries = entries }
            | entry :: rest ->
                match
                    entry.Dependencies
                    |> List.tryFind (fun dependency -> not (names.Contains dependency))
                with
                | Some dependency -> Error(MissingIndexDependency(entry.Name, dependency))
                | None ->
                    match
                        entry.Dependencies
                        |> List.tryFind (fun dependency -> not (seen.Contains dependency))
                    with
                    | Some dependency -> Error(OutOfOrderIndexDependency(entry.Name, dependency))
                    | None -> check (seen.Add entry.Name) rest

        check Set.empty entries

/// Returns the dependency-ordered file index of a loaded book and its opens.
let index (book: LoadedBook) : Result<BookIndex, IndexError> = indexEntries (flattenIndex book)

/// A book with a `main` export is a spell; otherwise it is a library.
let classify (book: LoadedBook) : BookKind =
    if book.Package.Exports |> List.exists (fun export -> export.Name = "main") then
        Spell
    else
        Book

let exports (book: LoadedBook) : string list =
    book.Package.Exports |> List.map (fun export -> export.Name)

let private mainArity (book: LoadedBook) : int option =
    book.Document.Definitions
    |> List.tryFind (fun definition -> definition.Name = "main")
    |> Option.map (fun definition -> definition.Parameters.Length)

let private describeAbstractionError (error: LoweredAbstraction.AbstractionError) : string =
    match error with
    | LoweredAbstraction.LoweredFailure reason
    | LoweredAbstraction.HostFailure reason -> reason

let private rewriteClosures (prefix: string) (instructions: LoweredInstructions.Instruction list) =
    instructions
    |> List.map (function
        | LoweredInstructions.MakeClosure(name, captures, arity) ->
            LoweredInstructions.MakeClosure(prefix + name, captures, arity)
        | instruction -> instruction)

let private lowerBookDefinition
    (sourceHash: string)
    (definition: ProseParser.BookDefinition)
    : Result<PreludeImage.ImageUnit list, string> =
    let expression =
        if List.isEmpty definition.Parameters then
            definition.Body
        else
            ExpressionLogic.Lambda(definition.Parameters, definition.Body)

    let prefix = definition.Name + "__"

    match EvaluatorLowering.lowerExpression expression with
    | Error refusal -> Error(EvaluatorLowering.describeRefusal refusal)
    | Ok program ->
        let nestedUnits =
            if List.isEmpty definition.Parameters then
                program.Units
            else
                program.Units |> List.take (max 0 (program.Units.Length - 1))

        let nested: PreludeImage.ImageUnit list =
            nestedUnits
            |> List.map (fun unit ->
                {
                    Name = prefix + unit.Name
                    SourceHash = sourceHash
                    Dependencies = []
                    Arity = unit.Arity
                    Captures = []
                    Instructions = rewriteClosures prefix unit.Instructions
                })

        let target: Result<PreludeImage.ImageUnit, string> =
            if List.isEmpty definition.Parameters then
                Ok
                    {
                        Name = definition.Name
                        SourceHash = sourceHash
                        Dependencies = []
                        Arity = 0
                        Captures = []
                        Instructions = rewriteClosures prefix program.Instructions
                    }
            else
                match List.tryLast program.Units with
                | None -> Error("the book definition " + definition.Name + " produced no unit")
                | Some lambdaUnit ->
                    Ok
                        {
                            Name = definition.Name
                            SourceHash = sourceHash
                            Dependencies = []
                            Arity = lambdaUnit.Arity
                            Captures = []
                            Instructions = rewriteClosures prefix lambdaUnit.Instructions
                        }

        target |> Result.map (fun unit -> unit :: nested)

let private compileImage (book: LoadedBook) : Result<PreludeImage.ImageDocument, string> =
    let names =
        book.Document.Definitions |> List.map (fun definition -> definition.Name)

    let unitSourceHash (name: string) : string =
        book.Package.Units
        |> List.tryFind (fun unit -> unit.Name = name)
        |> Option.map (fun unit -> unit.SourceHash)
        |> Option.defaultValue book.Package.SourceHash

    let units =
        book.Document.Definitions
        |> List.fold
            (fun accumulated definition ->
                accumulated
                |> Result.bind (fun current ->
                    lowerBookDefinition (unitSourceHash definition.Name) definition
                    |> Result.map (fun lowered -> current @ lowered)))
            (Ok [])

    match units with
    | Error reason -> Error reason
    | Ok unitList ->
        let group: PreludeImage.ImageUnitGroup =
            {
                Name = book.Package.Name
                Units = names
                InitOrder = names
                Classification =
                    book.Document.Definitions
                    |> List.map (fun definition ->
                        {
                            Name = definition.Name
                            Kind =
                                match definition.Marker with
                                | ProseParser.Logic ->
                                    if List.isEmpty definition.Parameters then
                                        PreludeImage.NonRecursiveValue
                                    else
                                        PreludeImage.NonRecursiveFunction
                                | _ ->
                                    if List.isEmpty definition.Parameters then
                                        PreludeImage.RecursiveValue
                                    else
                                        PreludeImage.RecursiveFunction
                        })
            }

        let image: PreludeImage.ImageDocument =
            {
                SchemaVersion = 2
                Form = "image"
                Name = book.Package.Name
                Version = book.Package.Version
                SourceHash = book.Package.SourceHash
                DependencyPins = Map.empty
                Imports = []
                Exports =
                    book.Package.Exports
                    |> List.map (fun export ->
                        {
                            Name = export.Name
                            Unit = export.Unit
                        })
                Capabilities = book.Package.Capabilities
                Kinds = []
                Predicates = []
                Primitives = []
                Units = unitList
                Groups = [ group ]
            }

        Ok image

let private hostEnvironment: Map<string, TypeRetrieval.Scheme list> =
    let signature
        (parameters: TypeRetrieval.Type list)
        (result: TypeRetrieval.Type)
        (effects: TypeRetrieval.EffectSet)
        : TypeRetrieval.Scheme =
        TypeRetrieval.wordScheme (
            {
                Quantified = []
                QuantifiedEffects = []
                QuantifiedRows = []
                Parameters = parameters
                Result = result
                Effects = effects
            }
            : TypeRetrieval.WordSignature
        )

    let text = TypeRetrieval.Primitive "text"
    let number = TypeRetrieval.Primitive "number"
    let shell = TypeRetrieval.effectSet "shell"
    let book = TypeRetrieval.effectSet "book"

    Map.ofList
        [
            "run", [ signature [ text; text ] number shell ]
            "capture", [ signature [ text; text ] text shell ]
            "cast", [ signature [ text; text ] text book ]
        ]

/// Type-checks a book's definitions in declaration order. Every book word gets
/// a provisional scheme so forward and recursive calls are not `UnknownWord`;
/// the completed scheme replaces it after inference.
let checkTypes (book: LoadedBook) : Result<unit, string> =
    let mutable nextVariable = -1

    let freshVariable () =
        let variable = nextVariable
        nextVariable <- nextVariable - 1
        TypeRetrieval.Variable variable

    let provisional (definition: ProseParser.BookDefinition) =
        let parameters = List.init definition.Parameters.Length (fun _ -> freshVariable ())

        let scheme: TypeRetrieval.Scheme =
            if parameters.IsEmpty then
                {
                    Quantified = []
                    QuantifiedEffects = []
                    QuantifiedRows = []
                    EffectLacks = LabelSets.emptyLacks
                    RowLacks = RowTypes.emptyLacks
                    Type = freshVariable ()
                }
            else
                {
                    Quantified = []
                    QuantifiedEffects = []
                    QuantifiedRows = []
                    EffectLacks = LabelSets.emptyLacks
                    RowLacks = RowTypes.emptyLacks
                    Type =
                        TypeRetrieval.Function(
                            parameters,
                            freshVariable (),
                            TypeRetrieval.emptyEffects
                        )
                }

        definition.Name, [ scheme ]

    let primitiveEnvironment =
        PreludeSignatures.primitiveTable
        |> Map.map (fun _ signatures -> signatures |> List.map TypeRetrieval.wordScheme)

    let mutable environment =
        (primitiveEnvironment
         |> Map.fold (fun current name schemes -> Map.add name schemes current) hostEnvironment)
        |> Map.fold
            (fun current name schemes -> Map.add name schemes current)
            (Map.ofList (book.Document.Definitions |> List.map provisional))

    let mutable state =
        { TypeRetrieval.emptyInferenceState with
            Shapes =
                book.Document.Shapes
                |> List.map (fun shape -> shape.Name, shape.Fields)
                |> Map.ofList
        }

    let mutable failure = None

    for definition in book.Document.Definitions do
        if failure.IsNone && not definition.Synthetic then
            let expression =
                if List.isEmpty definition.Parameters then
                    definition.Body
                else
                    ExpressionLogic.Lambda(definition.Parameters, definition.Body)

            match TypeRetrieval.infer [] state environment expression with
            | Error(path, error) ->
                failure <-
                    Some(
                        TypeRetrieval.describeDiagnostic
                            {
                                Path = path
                                Range = None
                                Error = error
                                Description = TypeRetrieval.describeError error
                            }
                    )
            | Ok(inferred, nextState) ->
                let scheme =
                    TypeRetrieval.generalizeWith
                        (TypeRetrieval.environmentVariables environment)
                        (TypeRetrieval.environmentEffectVariables environment)
                        (TypeRetrieval.environmentRowVariables environment)
                        inferred.Type

                environment <- Map.add definition.Name [ scheme ] environment
                state <- nextState

    match failure with
    | Some reason -> Error reason
    | None -> Ok()

/// Structurally checks a book without running it: load, package, lower every
/// definition, type-check the definitions, and require a zero-argument `main`
/// when it is a spell.
let check (state: EvaluationState) (path: string) : Result<unit, string> =
    match load state path with
    | Error reason -> Error reason
    | Ok book ->
        let mainCheck =
            match mainArity book with
            | Some arity when arity <> 0 ->
                Error("the spell main expects " + string arity + " argument(s); the CLI supplies none")
            | _ -> Ok()

        match mainCheck with
        | Error reason -> Error reason
        | Ok() ->
            match compileImage book with
            | Error reason -> Error reason
            | Ok _ -> checkTypes book

/// Runs one exported word. A value definition returns its bound value; a
/// function definition is called with the supplied argument identities.
let private runWordUnchecked
    (state: EvaluationState)
    (book: LoadedBook)
    (word: string)
    (arguments: EntryIdentity list)
    : Result<EvaluationState, string> =
    match
        book.Document.Definitions
        |> List.tryFind (fun definition -> definition.Name = word)
    with
    | None -> Error("the book has no definition named " + word)
    | Some definition when definition.Parameters.Length <> arguments.Length ->
        Error(
            "the book word "
            + word
            + " expects "
            + string definition.Parameters.Length
            + " argument(s), got "
            + string arguments.Length
        )
    | Some _ ->
        match compileImage book with
        | Error reason -> Error reason
        | Ok image ->
            let entryName = "__book_entry"

            let entry: PreludeImage.ImageUnit =
                {
                    Name = entryName
                    SourceHash = "book-entry"
                    Dependencies = []
                    Arity = 0
                    Captures = []
                    Instructions = [ LoweredInstructions.CallWord(word, None) ]
                }

            let entryExport: PreludeImage.ImageExport = { Name = entryName; Unit = entryName }

            let image: PreludeImage.ImageDocument =
                { image with
                    Units = image.Units @ [ entry ]
                    Exports = [ entryExport ]
                }

            let stack =
                arguments
                |> List.fold (fun accumulated argument -> Stack.push argument accumulated) []

            let shapeFields =
                book.Document.Shapes
                |> List.map (fun shape -> shape.Name, shape.Fields)
                |> Map.ofList

            let fieldWord =
                { new HostWordAdapter with
                    member _.Capability = "book"

                    member _.TryExecute(token, state) =
                        match HostWords.tryDecodeBook token with
                        | Some("field", 2) ->
                            match Stack.popIndex state.Stack with
                            | None -> Some(Error(HostFailure("field: expected a record")))
                            | Some(recordId, afterRecord) ->
                                match Stack.popIndex afterRecord with
                                | None -> Some(Error(HostFailure("field: expected a field name")))
                                | Some(nameId, remaining) ->
                                    match Store.readTextAt nameId state.Store with
                                    | None -> Some(Error(HostFailure("field: the field name is not text")))
                                    | Some fieldName ->
                                        match EvaluatorEnvironment.viewUnificationTerm recordId state.Store with
                                        | EvaluatorEnvironment.Compound(head, arguments) ->
                                            match Store.readTextAt head state.Store with
                                            | Some shapeName ->
                                                match Map.tryFind shapeName shapeFields with
                                                | Some fields ->
                                                    match List.tryFindIndex ((=) fieldName) fields with
                                                    | Some index when index < List.length arguments ->
                                                        Some(
                                                            Ok
                                                                { state with
                                                                    Stack = Stack.push arguments.[index] remaining
                                                                }
                                                        )
                                                    | _ ->
                                                        Some(
                                                            Error(
                                                                HostFailure(
                                                                    "field: "
                                                                    + shapeName
                                                                    + " has no field "
                                                                    + fieldName
                                                                )
                                                            )
                                                        )
                                                | None ->
                                                    Some(
                                                        Error(
                                                            HostFailure(
                                                                "field: unknown shape " + shapeName
                                                            )
                                                        )
                                                    )
                                            | None ->
                                                Some(Error(HostFailure("field: the record head is not text")))
                                        | _ -> Some(Error(HostFailure("field: the value is not a record")))
                        | _ -> None
                }

            let bookState =
                { state with
                    Stack = stack
                    Program = []
                    CurrentFrame = None
                    CallFrames = []
                    MethodFrames = []
                    Handlers = []
                    Output = []
                    RecordHistory = false
                    HistoryTable = None
                    LastCommandStatus = None
                    HostWords = fieldWord :: state.HostWords
                    Capabilities = Set.add "book" state.Capabilities
                    BookBaseDirectory = Some(System.IO.Path.GetDirectoryName book.Path)
                }

            match
                PreludeExecution.runEntryWithPrimitiveAndHost
                    (fun _ _ -> Error "a book image has no primitive")
                    EvaluatorKernelBridge.executeWord
                    image
                    entryName
                    (EvaluatorKernelBridge.toKernel bookState)
            with
            | Error error -> Error(PreludeExecution.describeError error)
            | Ok kernel ->
                let final = EvaluatorKernelBridge.fromKernel kernel

                match Stack.popIndex final.Stack with
                | None -> Error("the book word " + word + " produced no value")
                | Some(result, _) ->
                    let status =
                        match final.LastCommandStatus with
                        | Some _ -> final.LastCommandStatus
                        | None -> state.LastCommandStatus

                    Ok
                        { state with
                            Store = final.Store
                            Stack = Stack.push result state.Stack
                            LastCommandStatus = status
                        }

/// Checks a package's declared capabilities against the run's grants.
let checkCapabilities (required: string list) (state: EvaluationState) : Result<unit, string> =
    let missing =
        required
        |> List.filter (fun capability -> not (state.Capabilities.Contains capability))
        |> List.distinct

    if missing.IsEmpty then
        Ok()
    else
        Error("the package requires capability " + String.concat ", " missing)

/// Runs one exported word. A package must have every declared capability before
/// any definition runs.
let runWord
    (state: EvaluationState)
    (book: LoadedBook)
    (word: string)
    (arguments: EntryIdentity list)
    : Result<EvaluationState, string> =
    match checkCapabilities book.Package.Capabilities state with
    | Error reason -> Error reason
    | Ok() -> runWordUnchecked state book word arguments

/// Runs a spell's `main`, or refuses a book that has no `main`.
let runSpell (state: EvaluationState) (path: string) : Result<EvaluationState, string> =
    match load state path with
    | Error reason -> Error reason
    | Ok book ->
        match classify book with
        | Spell ->
            match mainArity book with
            | Some 0 -> runWord state book "main" []
            | Some arity -> Error("the spell main expects " + string arity + " argument(s); the CLI supplies none")
            | None -> Error("the book has no main; it is a library, not a spell")
        | Book -> Error("the book has no main; it is a library, not a spell")

/// Casts a book word, or `main` when the book is a spell and no word is given.
let cast
    (state: EvaluationState)
    (path: string)
    (word: string option)
    (arguments: EntryIdentity list)
    : Result<EvaluationState, string> =
    match load state path with
    | Error reason -> Error reason
    | Ok book ->
        let target =
            match word with
            | Some word -> Ok word
            | None ->
                match classify book with
                | Spell ->
                    match mainArity book with
                    | Some 0 -> Ok "main"
                    | Some arity ->
                        Error(
                            "the spell main expects "
                            + string arity
                            + " argument(s); specify a word to cast instead"
                        )
                    | None -> Error("the book " + book.Package.Name + " has no main; specify a word to cast")
                | Book -> Error("the book " + book.Package.Name + " has no main; specify a word to cast")

        match target with
        | Error reason -> Error reason
        | Ok word -> runWord state book word arguments

/// Adds CLI arguments to the store, returning the identities in call order. A
/// numeric argument becomes a scalar; every other argument is text.
let withArguments (state: EvaluationState) (arguments: string list) : EvaluationState * EntryIdentity list =
    let (store, identities) =
        arguments
        |> List.fold
            (fun (currentStore, currentIds) argument ->
                let mutable numeric = 0.0

                if
                    System.Double.TryParse(
                        argument,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        &numeric
                    )
                then
                    let (identity, nextStore) = Store.addTensor (Tensor.scalar numeric) currentStore
                    (nextStore, identity :: currentIds)
                else
                    let (identity, nextStore) = Store.addText argument currentStore
                    (nextStore, identity :: currentIds))
            (state.Store, [])

    { state with Store = store }, List.rev identities

// ============================================================
// Potential extensions (not commitments)
// - Cross-book method visibility: extend a generic across packages.
// - Incremental book cache and lazy units: faster reload for large books.
// - Package signing and verification: supply-chain integrity.
// - Typed spell arguments and generated CLIs: safer spell entry points.
// - Hot reload of a book: edit a unit without restarting the host.
// ============================================================
