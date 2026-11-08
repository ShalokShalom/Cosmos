module EvaluatorCallable

// ============================================================
// The durable callable resolver (R0).
//
// A token that is not a builtin resolves once to one Callable kind. Invoke then
// performs the state transition for that kind. The resolver owns the ordering
// that used to live inline at the bottom of executeBuiltinWord:
//
//   word metaobject -> generic function -> user word -> vocabulary entry
//
// Lexical bindings stay in Evaluator because they are interpreter-only and the
// strict lowered path refuses them before reaching this resolver.
// ============================================================

open Types
open EvaluatorCommon

/// What a non-builtin name resolved to. The identity is carried with the kind so
/// Invoke never re-resolves the token.
type Callable =
    | ControlPrimitive of primitive: string
    | ControlClosure of closure: EvaluatorCellSystem.Closure
    | BuiltinCallable of token: string
    | HostCallable of token: string
    | WordValue of nameId: EntryIdentity
    | GenericCallable of nameId: EntryIdentity * generic: EvaluatorDispatch.GenericFunction
    | UserCallable of nameId: EntryIdentity * bodyId: EntryIdentity
    | VocabularyValue of nameId: EntryIdentity

/// Resolve a control word to its primitive or code cell. A missing
/// implementation is not a control call.
let resolveControl
    (token: string)
    (state: EvaluationState)
    : Result<Callable option, EvaluationError> =
    match EvaluatorWord.findByToken token state.WordTable state.Store with
    | Some word when not word.IsActive ->
        let form = Store.readTextAt word.Form state.Store |> Option.defaultValue token
        Error(InactiveWord form)
    | Some word ->
        match word.Implementation with
        | Some implementationId ->
            match Store.lookupTensor implementationId state.Store with
            | None -> Ok None
            | Some implementation ->
                match EvaluatorCellSystem.decodeClosure implementationId implementation with
                | Some closure -> Ok(Some(ControlClosure closure))
                | None ->
                    match Store.readTextAt implementationId state.Store with
                    | Some primitive -> Ok(Some(ControlPrimitive primitive))
                    | None -> Ok None
        | None -> Ok None
    | None -> Ok None

/// Resolve one token to at most one callable, returning the store that holds the
/// interned name. An inactive word or vocabulary entry is a named refusal, not a
/// missing name.
let rec resolveName (token: string) (state: EvaluationState) : Result<(Callable * Store) option, EvaluationError> =
    if HostWords.isHostToken token then
        Ok(Some(HostCallable token, state.Store))
    else
        resolveNameWithoutHost token state

and resolveNameWithoutHost
    (token: string)
    (state: EvaluationState)
    : Result<(Callable * Store) option, EvaluationError> =
    let (nameId, imageWithName) = Store.addText token state.Store

    match EvaluatorWord.findByToken token state.WordTable imageWithName with
    | Some word when not word.IsActive ->
        let form = Store.readTextAt word.Form imageWithName |> Option.defaultValue token
        Error(InactiveWord form)
    | Some word ->
        match word.Implementation with
        | Some genericId ->
            match EvaluatorDispatch.findByName genericId state.GenericFunctionTable imageWithName with
            | Some generic -> Ok(Some(GenericCallable(genericId, generic), imageWithName))
            | None ->
                let form = Store.readText genericId imageWithName |> Option.defaultValue ""
                Error(Unresolved(UnknownKind.Generic, form))
        | None -> Ok(Some(WordValue nameId, imageWithName))
    | None ->
        match EvaluatorDispatch.findByName nameId state.GenericFunctionTable imageWithName with
        | Some generic -> Ok(Some(GenericCallable(nameId, generic), imageWithName))
        | None ->
            match findPair nameId state.UserWordTable imageWithName with
            | Some bodyId -> Ok(Some(UserCallable(nameId, bodyId), imageWithName))
            | None ->
                match VocabularyInImage.lookupByToken token state.VocabularyTable imageWithName with
                | Some entry when entry.IsActive -> Ok(Some(VocabularyValue nameId, imageWithName))
                | Some entry -> Error(InactiveWord entry.Form)
                | None -> Ok None

/// Perform the call for one resolved kind. The caller passes the two evaluator
/// entry points, so the resolver does not depend on the evaluator module.
let invoke
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (executeBuiltinWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (token: string)
    (callable: Callable)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match callable with
    | ControlPrimitive primitive -> executeBuiltinWord primitive state
    | ControlClosure closure -> EvaluatorClosure.startCall closure state state.Stack
    | BuiltinCallable builtinToken -> executeBuiltinWord builtinToken state
    | HostCallable hostToken ->
        let rec tryAdapters (adapters: HostWordAdapter list) =
            match adapters with
            | [] -> Error(HostFailure("host words are unavailable: " + hostToken))
            | adapter :: rest ->
                if state.Capabilities.Contains adapter.Capability then
                    match adapter.TryExecute(hostToken, state) with
                    | Some result -> result
                    | None -> tryAdapters rest
                else
                    tryAdapters rest

        tryAdapters state.HostWords
    | WordValue nameId
    | VocabularyValue nameId -> Ok { state with Stack = Stack.push nameId state.Stack }
    | GenericCallable(_, generic) -> EvaluatorDispatch.invoke executeWord token generic state
    | UserCallable(_, bodyId) ->
        match EvaluatorCode.decode bodyId state.Store with
        | Ok bodyTerms ->
            Ok
                { state with
                    Program = bodyTerms @ state.Program
                }
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord token, ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
