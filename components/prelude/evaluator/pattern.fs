module EvaluatorPattern

// ============================================================
// Pattern matching: the runtime pattern IR and the ground-subject matcher.
//
// A pattern is constructor-headed and variables carry the executable roles
// fresh, pin, repeated, and shadowing. The matcher uses the existing
// unification terms for pinned values; an open subject is matched by encoding
// the pattern as a term and unifying it, with an undecidable case reported as a
// named refusal rather than a silent structural failure.
//
// Surface form:
//   subject { pattern -> body | pattern -> body } match
//
// `?x` is fresh on its first occurrence in a clause and repeated afterwards,
// `^?x` is a pin, `!?x` shadows, `_` is a wildcard, `(name child ...)` is a
// constructor, `:name` is an atom, a number is a numeric literal, and quoted
// text is a text literal. A matched clause binds `?x` in the binding table and
// puts its body tokens on the program.
// ============================================================

open Types
open EvaluatorCommon
open PatternLogic

type PatternFailure =
    | Structural
    | MissingPin of name: string
    | UnboundRepeated of name: string
    | Undecided of reason: string

// ---- Surface parsing -------------------------------------------------------

let private parseNumber (token: string) : RealNumber option =
    match System.Double.TryParse token with
    | true, value -> Some value
    | false, _ -> None

let rec private parsePatternTokens
    (seen: Set<string>)
    (tokens: string list)
    : Result<Pattern * string list * Set<string>, string> =
    match tokens with
    | [] -> Error "A pattern is empty."
    | token :: rest ->
        match token with
        | "_" -> Ok(Wildcard, rest, seen)
        | token when token.StartsWith("^?") && token.Length > 2 ->
            Ok(Variable(token.Substring(1), Pin), rest, seen)
        | token when token.StartsWith("!?") && token.Length > 2 ->
            Ok(Variable(token.Substring(1), Shadowing), rest, seen)
        | token when token.StartsWith("?") && token.Length > 1 ->
            let name = token
            let role = if seen.Contains name then Repeated else Fresh
            Ok(Variable(name, role), rest, seen.Add name)
        | token when token.StartsWith("\"") && token.EndsWith("\"") && token.Length >= 2 ->
            Ok(TextLiteral(literalBody token), rest, seen)
        | token when token.StartsWith(":") && token.Length > 1 ->
            Ok(AtomLiteral(token.Substring(1)), rest, seen)
        | token when token = "(" ->
            match rest with
            | name :: afterName ->
                parseChildren seen name afterName
            | [] -> Error "A constructor opens without a name."
        | token ->
            match parseNumber token with
            | Some value -> Ok(NumberLiteral value, rest, seen)
            | None -> Ok(Constructor(token, []), rest, seen)

and private parseChildren
    (seen: Set<string>)
    (name: string)
    (tokens: string list)
    : Result<Pattern * string list * Set<string>, string> =
    match tokens with
    | ")" :: rest -> Ok(Constructor(name, []), rest, seen)
    | [] -> Error("Constructor " + name + " is not closed.")
    | _ ->
        parsePatternTokens seen tokens
        |> Result.bind (fun (child, afterChild, seen1) ->
            parseMoreChildren seen1 name [ child ] afterChild)

and private parseMoreChildren
    (seen: Set<string>)
    (name: string)
    (children: Pattern list)
    (tokens: string list)
    : Result<Pattern * string list * Set<string>, string> =
    match tokens with
    | ")" :: rest -> Ok(Constructor(name, List.rev children), rest, seen)
    | [] -> Error("Constructor " + name + " is not closed.")
    | _ ->
        parsePatternTokens seen tokens
        |> Result.bind (fun (child, afterChild, seen1) ->
            parseMoreChildren seen1 name (child :: children) afterChild)

let parsePattern (tokens: string list) : Result<Pattern, string> =
    parsePatternTokens Set.empty tokens
    |> Result.bind (fun (pattern, rest, _) ->
        match rest with
        | [] -> Ok pattern
        | unexpected :: _ -> Error("Unexpected token after the pattern: " + unexpected))

// ---- Matching --------------------------------------------------------------

let private textAt (identity: EntryIdentity) (store: Store) : string option =
    Store.readTextAt identity store

let private numberAt (identity: EntryIdentity) (store: Store) : RealNumber option =
    match Store.lookupTensor identity store with
    | Some tensor when Tensor.rank tensor = 0 && Tensor.size tensor > 0 -> Some(Tensor.item tensor 0)
    | _ -> None

let private unifyWithBound
    (bound: EntryIdentity)
    (subject: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : Result<Store, PatternFailure> =
    match EvaluatorEnvironment.unifyTerms bound subject root store with
    | EvaluatorEnvironment.Unified _, store1 -> Ok store1
    | EvaluatorEnvironment.NotUnified, _ -> Error Structural
    | EvaluatorEnvironment.Undecided reason, _ -> Error(Undecided reason)

let rec private hasShadow (pattern: Pattern) : bool =
    match pattern with
    | Variable(_, Shadowing) -> true
    | Constructor(_, children) -> children |> List.exists hasShadow
    | TypedPattern(_, child) -> hasShadow child
    | _ -> false

let rec private encodePattern
    (counter: int ref)
    (pattern: Pattern)
    (store: Store)
    : Result<EntryIdentity * Store, PatternFailure> =
    match pattern with
    | Wildcard ->
        counter.Value <- counter.Value + 1
        let (identity, store1) = Store.addText ("?_" + string counter.Value) store
        Ok(identity, store1)
    | Variable(name, Shadowing) ->
        counter.Value <- counter.Value + 1
        let (identity, store1) = Store.addText (name + "#" + string counter.Value) store
        Ok(identity, store1)
    | Variable(name, _) ->
        let (identity, store1) = Store.addText name store
        Ok(identity, store1)
    | NumberLiteral value ->
        let (identity, store1) = Store.addTensor (Tensor.scalar value) store
        Ok(identity, store1)
    | TextLiteral value ->
        let (identity, store1) = Store.addText value store
        Ok(identity, store1)
    | AtomLiteral value ->
        let (identity, store1) = Store.addText value store
        Ok(identity, store1)
    | Constructor(name, children) ->
        let (head, store1) = Store.addText name store

        children
        |> List.fold
            (fun accumulated child ->
                match accumulated with
                | Error failure -> Error failure
                | Ok(argumentIds, currentStore) ->
                    encodePattern counter child currentStore
                    |> Result.map (fun (identity, nextStore) -> argumentIds @ [ identity ], nextStore))
            (Ok([], store1))
        |> Result.bind (fun (arguments, store2) ->
            let (identity, store3) =
                Store.addTensor (EvaluatorEnvironment.encodeCompound head arguments) store2

            Ok(identity, store3))
    | TypedPattern(sortName, child) ->
        let (sortId, store1) = Store.addText sortName store

        encodePattern counter child store1
        |> Result.bind (fun (childId, store2) ->
            let (identity, store3) =
                Store.addTensor (EvaluatorEnvironment.encodeTyped sortId childId) store2

            Ok(identity, store3))

let rec private validateRoles
    (root: EntryIdentity option)
    (seen: Set<string>)
    (pattern: Pattern)
    (store: Store)
    : Result<Set<string> * Store, PatternFailure> =
    match pattern with
    | Variable(name, Pin) ->
        let (nameId, store1) = Store.addText name store

        match findPair nameId root store1 with
        | Some _ -> Ok(seen, store1)
        | None -> Error(MissingPin name)
    | Variable(name, Repeated) ->
        if seen.Contains name then
            Ok(seen, store)
        else
            Error(UnboundRepeated name)
    | Variable(name, Fresh)
    | Variable(name, Shadowing) -> Ok(seen.Add name, store)
    | Constructor(_, children) ->
        children
        |> List.fold
            (fun accumulated child ->
                match accumulated with
                | Error failure -> Error failure
                | Ok(currentSeen, currentStore) -> validateRoles root currentSeen child currentStore)
            (Ok(seen, store))
    | TypedPattern(_, child) -> validateRoles root seen child store
    | _ -> Ok(seen, store)

let rec private collectBindings
    (root: EntryIdentity option)
    (bindings: Map<string, EntryIdentity>)
    (pattern: Pattern)
    (store: Store)
    : Result<Map<string, EntryIdentity> * Store, PatternFailure> =
    match pattern with
    | Variable(name, Pin) -> Ok(bindings, store)
    | Variable(name, Fresh)
    | Variable(name, Repeated) ->
        let (nameId, store1) = Store.addText name store

        match findPair nameId root store1 with
        | None -> Error(Undecided("the open match did not bind " + name))
        | Some value ->
            let (resolved, store2) =
                EvaluatorEnvironment.resolveUnificationTerm value Set.empty root store1

            Ok(bindings.Add(name, resolved), store2)
    | Variable(_, Shadowing) -> Error(Undecided "shadowing is not supported on an open subject")
    | Constructor(_, children) ->
        children
        |> List.fold
            (fun accumulated child ->
                match accumulated with
                | Error failure -> Error failure
                | Ok(currentBindings, currentStore) -> collectBindings root currentBindings child currentStore)
            (Ok(bindings, store))
    | TypedPattern(_, child) -> collectBindings root bindings child store
    | _ -> Ok(bindings, store)

let private openMatch
    (root: EntryIdentity option)
    (bindings: Map<string, EntryIdentity>)
    (pattern: Pattern)
    (subject: EntryIdentity)
    (store: Store)
    : Result<Map<string, EntryIdentity> * Store, PatternFailure> =
    if hasShadow pattern then
        Error(Undecided "shadowing is not supported on an open subject")
    else
        match validateRoles root Set.empty pattern store with
        | Error failure -> Error failure
        | Ok(_, store1) ->
            match encodePattern (ref 0) pattern store1 with
            | Error failure -> Error failure
            | Ok(encoded, store2) ->
                match EvaluatorEnvironment.unifyTerms subject encoded root store2 with
                | EvaluatorEnvironment.NotUnified, _ -> Error Structural
                | EvaluatorEnvironment.Undecided reason, _ -> Error(Undecided reason)
                | EvaluatorEnvironment.Unified root1, store3 -> collectBindings root1 bindings pattern store3

let rec private matchPatternGround
    (root: EntryIdentity option)
    (bindings: Map<string, EntryIdentity>)
    (pattern: Pattern)
    (subject: EntryIdentity)
    (store: Store)
    : Result<Map<string, EntryIdentity> * Store, PatternFailure> =
    match pattern with
    | Wildcard -> Ok(bindings, store)
    | Variable(name, role) ->
        match role, bindings.TryFind name with
        | Pin, None ->
            let (nameId, store1) = Store.addText name store

            match findPair nameId root store1 with
            | Some bound ->
                unifyWithBound bound subject root store1
                |> Result.map (fun store2 -> bindings, store2)
            | None -> Error(MissingPin name)
        | Repeated, None -> Error(UnboundRepeated name)
        | Shadowing, _ -> Ok(bindings.Add(name, subject), store)
        | Fresh, None -> Ok(bindings.Add(name, subject), store)
        | (Fresh | Pin | Repeated), Some bound ->
            unifyWithBound bound subject root store
            |> Result.map (fun store1 -> bindings, store1)
    | NumberLiteral value ->
        match numberAt subject store with
        | Some found when found = value -> Ok(bindings, store)
        | _ -> Error Structural
    | TextLiteral value ->
        match textAt subject store with
        | Some found when found = value -> Ok(bindings, store)
        | _ -> Error Structural
    | AtomLiteral value ->
        match textAt subject store with
        | Some found when found = value -> Ok(bindings, store)
        | _ -> Error Structural
    | TypedPattern(sortName, child) ->
        match EvaluatorEnvironment.viewUnificationTerm subject store with
        | EvaluatorEnvironment.Typed(sortId, term) ->
            match textAt sortId store with
            | Some found when found = sortName -> matchPattern root bindings child term store
            | _ -> Error Structural
        | _ -> Error Structural
    | Constructor(name, children) ->
        match EvaluatorEnvironment.viewUnificationTerm subject store with
        | EvaluatorEnvironment.Compound(head, arguments) ->
            match textAt head store with
            | Some found when found = name && List.length children = List.length arguments ->
                matchChildren root bindings children arguments store
            | _ -> Error Structural
        | EvaluatorEnvironment.Atom _ when children.IsEmpty ->
            match textAt subject store with
            | Some found when found = name -> Ok(bindings, store)
            | _ -> Error Structural
        | EvaluatorEnvironment.Variable _ -> Error(Undecided "the subject is an open term")
        | _ -> Error Structural

and private matchPattern
    (root: EntryIdentity option)
    (bindings: Map<string, EntryIdentity>)
    (pattern: Pattern)
    (subject: EntryIdentity)
    (store: Store)
    : Result<Map<string, EntryIdentity> * Store, PatternFailure> =
    let subjectIsOpen =
        match EvaluatorEnvironment.viewUnificationTerm subject store with
        | EvaluatorEnvironment.Variable _ -> true
        | _ -> false

    if subjectIsOpen && (match pattern with Wildcard | Variable _ -> false | _ -> true) then
        openMatch root bindings pattern subject store
    else
        matchPatternGround root bindings pattern subject store

and private matchChildren
    (root: EntryIdentity option)
    (bindings: Map<string, EntryIdentity>)
    (patterns: Pattern list)
    (subjects: EntryIdentity list)
    (store: Store)
    : Result<Map<string, EntryIdentity> * Store, PatternFailure> =
    match patterns, subjects with
    | [], [] -> Ok(bindings, store)
    | pattern :: restPatterns, subject :: restSubjects ->
        match matchPattern root bindings pattern subject store with
        | Error failure -> Error failure
        | Ok(bindings1, store1) -> matchChildren root bindings1 restPatterns restSubjects store1
    | _ -> Error Structural

/// Matches one pattern against one subject. `root` is the current binding
/// table, so a pin resolves through the same substitution the rest of the
/// evaluator uses.
let matchOne
    (root: EntryIdentity option)
    (pattern: Pattern)
    (subject: EntryIdentity)
    (store: Store)
    : Result<Map<string, EntryIdentity> * Store, PatternFailure> =
    matchPattern root Map.empty pattern subject store

// ---- The `match` word ------------------------------------------------------

let private splitOn (separator: string) (tokens: string list) : string list list =
    let rec loop (current: string list) (accumulated: string list list) (remaining: string list) =
        match remaining with
        | [] -> List.rev (List.rev current :: accumulated)
        | token :: rest when token = separator ->
            loop [] (List.rev current :: accumulated) rest
        | token :: rest -> loop (token :: current) accumulated rest

    loop [] [] tokens

let private splitClause (tokens: string list) : (string list * string list) option =
    let rec loop (patternTokens: string list) (remaining: string list) =
        match remaining with
        | [] -> None
        | "->" :: body -> Some(List.rev patternTokens, body)
        | token :: rest -> loop (token :: patternTokens) rest

    loop [] tokens

let private bindName
    (name: string)
    (value: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity option * Store =
    let (nameId, store1) = Store.addText name store
    let (newRoot, store2) = addToEnvironment nameId value root store1
    (Some newRoot, store2)

let private splitGuard (tokens: string list) : Result<string list * string option, string> =
    match tokens |> List.tryFindIndex ((=) "when") with
    | None -> Ok(tokens, None)
    | Some index ->
        let patternTokens = tokens |> List.take index

        match tokens |> List.skip (index + 1) with
        | [ guard ] -> Ok(patternTokens, Some guard)
        | [] -> Error "A when marker needs a named predicate."
        | _ -> Error "A guard must be one named predicate."

let private runGuard
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (name: string)
    (guardState: EvaluationState)
    : Result<bool * EvaluationState, string> =
    let rec loop (current: EvaluationState) : Result<EvaluationState, string> =
        match current.Program with
        | [] -> Ok current
        | term :: remaining ->
            if current.Reductions <= 0 then
                Error "the guard exceeded its reduction budget"
            else
                let before =
                    { current with
                        Program = remaining
                        Reductions = current.Reductions - 1
                        Location = current.Location + 1
                        Position = current.Position + 1
                    }

                match EvaluatorProgram.executeTerm executeWord term before with
                | Error error -> Error(describeError error)
                | Ok after -> loop after

    match loop guardState with
    | Error reason -> Error reason
    | Ok finished ->
        match Stack.popResolved finished.Store finished.Stack with
        | None -> Error "the guard left no verdict on the stack"
        | Some(tensor, _) ->
            match ScalarRead.readScalar tensor with
            | Error reason -> Error reason
            | Ok value -> Ok(value <> 0.0, finished)

let rec matchWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("match", 2, Stack.depth state.Stack))
    | Some(quotationId, afterQuotation) ->
        match Stack.popIndex afterQuotation with
        | None -> Error(StackUnderflow("match", 2, Stack.depth state.Stack))
        | Some(subjectId, remainingStack) ->
            match EvaluatorCode.decode quotationId state.Store with
            | Error(EvaluatorCode.NotCode _) ->
                Error(TypeMismatch(ErrorAddress(EntryIdentity.value quotationId), ExpectedQuotation))
            | Error error -> Error(CodeError(EvaluatorCode.describeError error))
            | Ok clauseTerms ->
                let clauseText = EvaluatorProgram.toKernelTokens clauseTerms |> String.concat " "

                match splitClauses (splitText clauseText) with
                | Error reason -> Error(PatternError(MalformedPattern reason))
                | Ok clauses -> tryClauses executeWord state clauses subjectId remainingStack state.Store

and private splitClauses (tokens: string list) : Result<(string list * string list) list, string> =
    let raw = splitOn "|" tokens

    raw
    |> List.map splitClause
    |> List.fold
        (fun accumulated clause ->
            match accumulated, clause with
            | Error reason, _ -> Error reason
            | Ok clauses, Some value -> Ok(clauses @ [ value ])
            | Ok _, None -> Error "A clause is missing its -> separator.")
        (Ok [])

and private tryClauses
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    (clauses: (string list * string list) list)
    (subjectId: EntryIdentity)
    (remainingStack: EntryIdentity list)
    (store: Store)
    : Result<EvaluationState, EvaluationError> =
    match clauses with
    | [] ->
        Error(
            PatternError(
                NoClauseMatched(
                    Store.readTextAt subjectId store |> Option.defaultValue (string (EntryIdentity.value subjectId))
                )
            )
        )
    | (patternTokens, body) :: rest ->
        match splitGuard patternTokens with
        | Error reason -> Error(PatternError(MalformedPattern reason))
        | Ok(patternTokens, guardName) ->
            match parsePattern patternTokens with
            | Error reason -> Error(PatternError(MalformedPattern reason))
            | Ok pattern ->
                match matchOne state.BindingTable pattern subjectId store with
                | Error Structural -> tryClauses executeWord state rest subjectId remainingStack store
                | Error(MissingPin name) -> Error(PatternError(PatternFault.MissingPin name))
                | Error(UnboundRepeated name) -> Error(PatternError(PatternFault.UnboundRepeated name))
                | Error(Undecided reason) -> Error(PatternError(PatternUndecided reason))
                | Ok(bindings, store1) ->
                    let (root, store2) =
                        bindings
                        |> Map.fold
                            (fun (root, currentStore) name value ->
                                let (nextRoot, nextStore) = bindName name value root currentStore
                                (nextRoot, nextStore))
                            (state.BindingTable, store1)

                    match guardName with
                    | None ->
                        Ok
                            { state with
                                Store = store2
                                BindingTable = root
                                Stack = remainingStack
                                Program = EvaluatorProgram.ofTokensOrFail body @ state.Program
                            }
                    | Some name ->
                        let guardState =
                            { state with
                                Store = store2
                                BindingTable = root
                                Stack = []
                                Program = [ SymbolicAbstraction.Word name ]
                                Output = []
                                Reductions = DefaultReductions
                                Consumed = 0
                                Deadline = 0
                                PendingError = None
                                CurrentFrame = None
                                CallFrames = []
                                MethodFrames = []
                                Handlers = []
                            }

                        match runGuard executeWord name guardState with
                        | Error reason -> Error(PatternError(PatternFault.GuardFailed(name, reason)))
                        | Ok(false, _) ->
                            tryClauses executeWord state rest subjectId remainingStack store
                        | Ok(true, finished) ->
                            Ok
                                { state with
                                    Store = finished.Store
                                    BindingTable = finished.BindingTable
                                    Stack = remainingStack
                                    Program = EvaluatorProgram.ofTokensOrFail body @ state.Program
                                }
