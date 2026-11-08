module EvaluatorControl

// ============================================================
// Control words: quotations and flow.
//
// `collectQuotation` reads a `{ ... }` block out of the program into a text
// tensor; `if` and `apply` push a branch back onto the program; `define` stores
// a quotation as a user word.
//
// D51: `{`, `if`, `apply`, and `define` are image-resident control words. A
// control word names its implementation entry; the default forms name the
// matching kernel primitive, and `control` installs a code cell instead.
//
// Words: { (quotation), if, apply, define, logic, cycle, control.
// ============================================================

open Types
open EvaluatorCommon
open EvaluatorCellSystem
open EvaluatorClosure

let rec collectQuotation
    (terms: SymbolicAbstraction.Term list)
    (accumulated: SymbolicAbstraction.Term list)
    : Result<SymbolicAbstraction.Term list * SymbolicAbstraction.Term list, EvaluationError> =
    match terms with
    | [] -> Error UnclosedQuotation
    | SymbolicAbstraction.Word "}" :: rest -> Ok(List.rev accumulated, rest)
    | SymbolicAbstraction.Word "{" :: rest ->
        match collectQuotation rest [] with
        | Error error -> Error error
        | Ok(body, afterBody) -> collectQuotation afterBody (SymbolicAbstraction.Quote body :: accumulated)
    | term :: rest -> collectQuotation rest (term :: accumulated)

let codeTerms
    (word: string)
    (identity: EntryIdentity)
    (state: EvaluationState)
    : Result<SymbolicAbstraction.Term list, EvaluationError> =
    match EvaluatorCode.decode identity state.Store with
    | Ok terms -> Ok terms
    | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord word, ExpectedQuotation))
    | Error error -> Error(CodeError(EvaluatorCode.describeError error))

let quotationWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let state =
        match state.Program with
        | term :: rest when term = SymbolicAbstraction.Word returnToken ->
            match returnWord { state with Program = rest } with
            | Ok restored -> restored
            | Error _ -> state
        | _ -> state

    match collectQuotation state.Program [] with
    | Error error -> Error error
    | Ok(quotationTerms, afterQuotation) ->
        match EvaluatorCode.encode quotationTerms state.Store with
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok(identity, newStore) ->
            Ok
                { state with
                    Store = newStore
                    Stack = Stack.push identity state.Stack
                    Program = afterQuotation
                }

let executeIf (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("if", 3, Stack.depth state.Stack))
    | Some(elseId, afterElse) ->
        match Stack.popIndex afterElse with
        | None -> Error(StackUnderflow("if", 3, Stack.depth state.Stack))
        | Some(thenId, afterThen) ->
            match Stack.popIndex afterThen with
            | None -> Error(StackUnderflow("if", 3, Stack.depth state.Stack))
            | Some(conditionId, afterCondition) ->
                match Store.lookupTensor conditionId state.Store with
                | None -> Error(UnknownIndex(EntryIdentity.value conditionId))
                | Some conditionTensor ->
                    match scalarValue "if" conditionTensor with
                    | Error error -> Error error
                    | Ok conditionValue ->
                        match codeTerms "if" (if conditionValue <> 0.0 then thenId else elseId) state with
                        | Error error -> Error error
                        | Ok branchTerms ->
                            Ok
                                { state with
                                    Program = branchTerms @ state.Program
                                    Stack = afterCondition
                                }

let executeApply (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("apply", 1, Stack.depth state.Stack))
    | Some(targetId, afterTarget) ->
        match EvaluatorCode.decode targetId state.Store with
        | Ok quoteTerms ->
            // A bare quotation keeps dynamic scope: it runs with the caller's
            // current frame, exactly as before D45.
            Ok
                { state with
                    Program = quoteTerms @ state.Program
                    Stack = afterTarget
                }
        | Error(EvaluatorCode.NotCode _) ->
            match
                Store.lookupTensor targetId state.Store
                |> Option.bind (EvaluatorCellSystem.decodeClosure targetId)
            with
            | Some closure -> EvaluatorClosure.startCall closure state afterTarget
            | None -> Error(TypeMismatch(ErrorWord "apply", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))

let rec private containsWord (name: string) (terms: SymbolicAbstraction.Term list) : bool =
    terms
    |> List.exists (function
        | SymbolicAbstraction.Word word -> word = name
        | SymbolicAbstraction.Quote body -> containsWord name body
        | _ -> false)

let private parseDefinition
    (marker: string)
    (terms: SymbolicAbstraction.Term list)
    : Result<string * SymbolicAbstraction.Term list * SymbolicAbstraction.Term list, EvaluationError> =
    match terms with
    | SymbolicAbstraction.Word name :: SymbolicAbstraction.Word "{" :: rest ->
        match collectQuotation rest [] with
        | Error error -> Error error
        | Ok(body, afterBody) -> Ok(name, body, afterBody)
    | _ -> Error(CodeError(marker + ": expected a name followed by a quotation body"))

let private storeDefinitions
    (state: EvaluationState)
    (definitions: (string * SymbolicAbstraction.Term list) list)
    (afterDefinitions: SymbolicAbstraction.Term list)
    : Result<EvaluationState, EvaluationError> =
    let names = definitions |> List.map fst

    if names.IsEmpty then
        Error(CodeError("definition: expected at least one name"))
    elif names.Length <> (names |> Set.ofList |> Set.count) then
        Error(CodeError("definition: duplicate name in group"))
    else
        let stored =
            definitions
            |> List.fold
                (fun accumulated (name, body) ->
                    accumulated
                    |> Result.bind (fun (currentStore, currentRoot) ->
                        let (nameId, store1) = Store.addText name currentStore

                        match EvaluatorCode.encode body store1 with
                        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                        | Ok(bodyId, store2) ->
                            let (nextRoot, store3) =
                                addToEnvironment nameId bodyId currentRoot store2

                            Ok(store3, Some nextRoot)))
                (Ok(state.Store, state.UserWordTable))

        match stored with
        | Error error -> Error error
        | Ok(store, root) ->
            Ok
                { state with
                    Store = store
                    UserWordTable = root
                    Program = afterDefinitions
                }

/// `logic name { body }` defines a word. The name is not in scope in its own
/// body, so a self-reference is a named refusal; `cycle` is the recursive form.
let executeLogic (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match parseDefinition "logic" state.Program with
    | Error error -> Error error
    | Ok(name, body, afterBody) ->
        if containsWord name body then
            Error(CodeError("logic " + name + ": a non-recursive definition cannot call itself; use cycle"))
        else
            match afterBody with
            | SymbolicAbstraction.Word "and" :: _ -> Error(CodeError("logic " + name + ": and belongs to a cycle group"))
            | _ -> storeDefinitions state [ (name, body) ] afterBody

let rec private parseCycleGroup
    (accumulated: (string * SymbolicAbstraction.Term list) list)
    (terms: SymbolicAbstraction.Term list)
    : Result<(string * SymbolicAbstraction.Term list) list * SymbolicAbstraction.Term list, EvaluationError> =
    match parseDefinition "cycle" terms with
    | Error error -> Error error
    | Ok(name, body, SymbolicAbstraction.Word "and" :: rest) ->
        parseCycleGroup ((name, body) :: accumulated) rest
    | Ok(name, body, afterBody) -> Ok(List.rev ((name, body) :: accumulated), afterBody)

/// `cycle name { body }` and `cycle first { body } and second { body }` define a
/// recursive group. The plain name is the self-reference; there is no separate
/// recurse word.
let executeCycle (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match parseCycleGroup [] state.Program with
    | Error error -> Error error
    | Ok(definitions, afterBody) ->
        let groupNames = definitions |> List.map fst |> Set.ofList

        let callsGroup =
            definitions
            |> List.exists (fun (_, body) -> groupNames |> Set.exists (fun name -> containsWord name body))

        if not callsGroup then
            Error(CodeError("cycle: no definition calls a group word; use logic"))
        else
            storeDefinitions state definitions afterBody

let executeDefine (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("define", 2, Stack.depth state.Stack))
    | Some(bodyId, afterBody) ->
        match Stack.popIndex afterBody with
        | None -> Error(StackUnderflow("define", 2, Stack.depth state.Stack))
        | Some(nameId, afterName) ->
            match Store.readTextAt nameId state.Store with
            | None -> Error(TypeMismatch(ErrorWord "define", ExpectedText))
            | Some _ ->
                match EvaluatorCode.decode bodyId state.Store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "define", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok _ ->
                    let (newRoot, newStore) =
                        addToEnvironment nameId bodyId state.UserWordTable state.Store

                    Ok
                        { state with
                            Store = newStore
                            UserWordTable = Some newRoot
                            Stack = afterName
                        }

/// A word metaobject whose implementation is a code cell. The code cell is a
/// closure: the quotation plus the frame captured when the form was declared.
let private encodeWord (tokenId: EntryIdentity) (formId: EntryIdentity) (implementationId: EntryIdentity) : Tensor =
    Tensor.vector
        [
            WordTag
            double (EntryIdentity.value tokenId)
            double (EntryIdentity.value formId)
            2.0
            1.0
            double (EntryIdentity.value implementationId)
        ]

let private appendWord (wordId: EntryIdentity) (root: EntryIdentity option) (store: Store) : EntryIdentity * Store =
    match root with
    | Some rootId ->
        match Store.lookupTensor rootId store with
        | Some vector ->
            Store.addTensor (Tensor.concat vector (Tensor.vector [ double (EntryIdentity.value wordId) ])) store
        | None -> Store.addTensor (Tensor.vector [ double (EntryIdentity.value wordId) ]) store
    | None -> Store.addTensor (Tensor.vector [ double (EntryIdentity.value wordId) ]) store

/// `handler name control` declares a control form. The handler is a quotation;
/// the declaration captures the current frame, so the form is a first-class
/// code cell. A later declaration under the same name wins, exactly as a word
/// metaobject does.
let controlWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("control", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Stack.popIndex afterName with
        | None -> Error(StackUnderflow("control", 2, Stack.depth state.Stack))
        | Some(handlerId, afterHandler) ->
            match Store.readTextAt nameId state.Store with
            | None -> Error(TypeMismatch(ErrorWord "control", ExpectedText))
            | Some _ ->
                match EvaluatorCode.decode handlerId state.Store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "control", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok _ ->
                    let (environmentId, store1) = ensureFrame state.CurrentFrame state.Store

                    let (closureId, store2) =
                        Store.addTensor (encodeClosure handlerId environmentId) store1

                    let (wordId, store3) = Store.addTensor (encodeWord nameId nameId closureId) store2

                    let (newRoot, store4) = appendWord wordId state.WordTable store3

                    Ok
                        { state with
                            Store = store4
                            WordTable = Some newRoot
                            Stack = afterHandler
                        }

/// The implementation reference of a control word. A default form names a
/// kernel primitive; a user form is a code cell. Both are image entries, so a
/// later `word` or `control` declaration replaces the form without F#.
type ControlImplementation =
    | ControlPrimitive of string
    | ControlCode of Closure

/// Seeds the default control protocol: `{`, `if`, `apply`, `define`, `logic`,
/// and `cycle` are words whose implementations name the matching kernel
/// primitive, so replacing the word replaces the form without F#.
let seedControlForms (store: Store) : Store * EntryIdentity option =
    let forms =
        [
            ("{", "control-quotation")
            ("if", "control-if")
            ("apply", "control-apply")
            ("define", "control-define")
            ("logic", "control-logic")
            ("cycle", "control-cycle")
        ]

    let mutable currentStore = store
    let mutable root = None

    for (token, handler) in forms do
        let (tokenId, store2) = Store.addText token currentStore
        let (handlerId, store3) = Store.addText handler store2
        let (wordId, store4) = Store.addTensor (encodeWord tokenId tokenId handlerId) store3
        let (newRoot, store5) = appendWord wordId root store4
        currentStore <- store5
        root <- Some newRoot

    (currentStore, root)
