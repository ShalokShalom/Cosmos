module PatternElaboration

// ============================================================
// Elaborate an open pattern template into a closed runtime pattern.
//
// A template is never executable. Every declared hole must be filled exactly
// once, the template's canonical kinds must be known with their arities, and
// the resulting pattern must reference only registered constructors.
// ============================================================

open PatternLogic

type ElaborationError =
    | DuplicateHole of name: string
    | MissingHole of name: string
    | UnknownHole of name: string
    | KindMismatch of name: string * expected: string * actual: string
    | UnknownKind of name: string
    | ArityMismatch of name: string * expected: int * actual: int

let describeError (error: ElaborationError) : string =
    match error with
    | DuplicateHole name -> "duplicate template hole: " + name
    | MissingHole name -> "missing template hole: " + name
    | UnknownHole name -> "unknown template argument: " + name
    | KindMismatch(name, expected, actual) ->
        "hole " + name + " expected kind " + expected + " but received " + actual
    | UnknownKind name -> "unknown constructor kind: " + name
    | ArityMismatch(name, expected, actual) ->
        "constructor " + name + " has arity " + string actual + "; expected " + string expected

let private kindMap (kinds: PatternKind list) : Result<Map<string, int>, ElaborationError> =
    kinds
    |> List.fold
        (fun (accumulated: Result<Map<string, int>, ElaborationError>) kind ->
            match accumulated with
            | Error error -> Error error
            | Ok current ->
                match current.TryFind kind.Name with
                | Some arity when arity <> kind.Arity -> Error(ArityMismatch(kind.Name, arity, kind.Arity))
                | Some _ -> Ok current
                | None -> Ok(current.Add(kind.Name, kind.Arity)))
        (Ok Map.empty)

let private rootKind (pattern: Pattern) : string =
    match pattern with
    | Constructor(name, _) -> name
    | AtomLiteral name -> name
    | _ -> ""

let rec private validatePattern (kinds: Map<string, int>) (pattern: Pattern) : Result<unit, ElaborationError> =
    match pattern with
    | Constructor(name, children) ->
        match kinds.TryFind name with
        | None -> Error(UnknownKind name)
        | Some expected when expected <> children.Length -> Error(ArityMismatch(name, expected, children.Length))
        | Some _ ->
            children
            |> List.fold
                (fun (accumulated: Result<unit, ElaborationError>) child ->
                    accumulated |> Result.bind (fun () -> validatePattern kinds child))
                (Ok())
    | TypedPattern(_, child) -> validatePattern kinds child
    | _ -> Ok()

let rec private replace
    (kinds: Map<string, int>)
    (holes: Map<string, string>)
    (arguments: Map<string, Pattern>)
    (node: TemplateNode)
    : Result<Pattern, ElaborationError> =
    match node with
    | TemplateHole name ->
        match holes.TryFind name, arguments.TryFind name with
        | None, _ -> Error(UnknownHole name)
        | Some _, None -> Error(MissingHole name)
        | Some expected, Some argument ->
            if expected <> "any" && rootKind argument <> expected then
                Error(KindMismatch(name, expected, rootKind argument))
            else
                validatePattern kinds argument |> Result.map (fun () -> argument)
    | TemplateVariable(name, role) -> Ok(Variable(name, role))
    | TemplateNumber value -> Ok(NumberLiteral value)
    | TemplateText value -> Ok(TextLiteral value)
    | TemplateAtom value -> Ok(AtomLiteral value)
    | TemplateConstructor(name, children) ->
        match kinds.TryFind name with
        | None -> Error(UnknownKind name)
        | Some expected when expected <> children.Length -> Error(ArityMismatch(name, expected, children.Length))
        | Some _ ->
            children
            |> List.fold
                (fun (accumulated: Result<Pattern list, ElaborationError>) child ->
                    match accumulated with
                    | Error error -> Error error
                    | Ok current ->
                        replace kinds holes arguments child
                        |> Result.map (fun next -> current @ [ next ]))
                (Ok [])
            |> Result.map (fun patterns -> Constructor(name, patterns))
    | TemplateTyped(sortName, child) ->
        replace kinds holes arguments child
        |> Result.map (fun pattern -> TypedPattern(sortName, pattern))

let elaborate (template: PatternTemplate) (arguments: Map<string, Pattern>) : Result<Pattern, ElaborationError> =
    match kindMap template.Kinds with
    | Error error -> Error error
    | Ok kinds ->
        let holes =
            template.Holes
            |> List.fold
                (fun (accumulated: Result<Map<string, string>, ElaborationError>) hole ->
                    match accumulated with
                    | Error error -> Error error
                    | Ok current ->
                        if current.ContainsKey hole.Name then
                            Error(DuplicateHole hole.Name)
                        else
                            Ok(current.Add(hole.Name, hole.Kind)))
                (Ok Map.empty)

        match holes with
        | Error error -> Error error
        | Ok holeMap ->
            match arguments |> Map.toList |> List.tryFind (fun (name, _) -> not (holeMap.ContainsKey name)) with
            | Some(name, _) -> Error(UnknownHole name)
            | None ->
                replace kinds holeMap arguments template.Root
                |> Result.bind (fun pattern -> validatePattern kinds pattern |> Result.map (fun () -> pattern))
