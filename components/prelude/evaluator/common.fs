module EvaluatorCommon

// ============================================================
// Shared plumbing for the evaluation slices.
//
// This module holds what more than one slice needs: the Railway helpers, the
// association-vector primitives that `control`, `environment`, and the
// dispatcher all touch, and the generic stack and pop helpers. It holds no word
// handlers, so it cannot depend on any slice — that is what keeps the slices
// acyclic.
//
// The handlers themselves live in stack / control / bookmark / environment /
// sort / setAlgebra / dispatch; `evaluator` is the dispatcher and the main loop.
// ============================================================

open Types

// ---- Text tokens -----------------------------------------------------------

let splitText (text: string) : string list =
    text.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
    |> List.ofArray

let isTextLiteral (token: string) : bool =
    token.Length >= 2 && token.StartsWith("\"") && token.EndsWith("\"")

let literalBody (token: string) : string = token.Substring(1, token.Length - 2)

/// A token that is syntax or a literal, never a lexical name. A lexical binding
/// may not shadow these, and the lowerer refuses to bind one (D122).
let isReservedToken (token: string) : bool =
    token = "{"
    || token = "}"
    || token = "["
    || token = "]"
    || isTextLiteral token
    || token.StartsWith("?")
    || (token.Length > 1 && token.StartsWith(":"))
    || (token.Length > 0 && token[0] = '\u0000')
    || (match System.Double.TryParse token with
        | true, _ -> true
        | false, _ -> false)

// ---- Store-resident association vectors (Wave 1, decision D2) ---------------
//
// An environment is a vector of EntryIdentity pairs [nameId; valueId; ...], addressed
// by a root. The store is immutable, so an update writes a new vector.

let nameText (identity: EntryIdentity) (store: Store) : string =
    match Store.readText identity store with
    | Some text -> text
    | None -> ""

/// Scans a pairs vector [name; value; name; value; ...] for a name and returns
/// the associated value identity, latest write wins.
let findPair (nameId: EntryIdentity) (root: EntryIdentity option) (store: Store) : EntryIdentity option =
    Store.findPair nameId root store

/// Appends a [nameId; valueId] pair, returning the new environment root. The
/// pair goes after the existing vector, which `findPair` reads latest-wins.
let addToEnvironment
    (nameId: EntryIdentity)
    (valueId: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity * Store =
    Store.addToEnvironment nameId valueId root store

// ---- Stack and value plumbing ---------------------------------------------

let tensorsEqual (left: Tensor) (right: Tensor) : bool =
    left.Data.Length = right.Data.Length
    && left.Shape.Length = right.Shape.Length
    && Array.forall2 (=) left.Data right.Data
    && Array.forall2 (=) left.Shape right.Shape

let scalarValue (word: string) (tensor: Tensor) : Result<RealNumber, EvaluationError> =
    if Tensor.size tensor = 0 then
        Error(EmptyValue word)
    else
        Ok(Tensor.item tensor 0)

let popResolved (word: string) (state: EvaluationState) : Result<Tensor * EntryIdentity list, EvaluationError> =
    match Stack.popResolved state.Store state.Stack with
    | Some(resolved, remaining) -> Ok(resolved, remaining)
    | None -> Error(StackUnderflow(word, 1, Stack.depth state.Stack))

/// Pops two resolved values. Returns (lower, upper, remaining).
let popTwoResolved
    (word: string)
    (state: EvaluationState)
    : Result<Tensor * Tensor * EntryIdentity list, EvaluationError> =
    match popResolved word state with
    | Error error -> Error error
    | Ok(secondTensor, afterSecond) ->
        match Stack.popResolved state.Store afterSecond with
        | Some(firstTensor, afterFirst) -> Ok(firstTensor, secondTensor, afterFirst)
        | None -> Error(StackUnderflow(word, 2, Stack.depth state.Stack))

/// Pops two raw stack references. Used by swap/over/rotate, which reorder
/// references rather than resolve them. Returns (lower, upper, remaining).
let popTwoIndices
    (word: string)
    (state: EvaluationState)
    : Result<EntryIdentity * EntryIdentity * EntryIdentity list, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow(word, 2, Stack.depth state.Stack))
    | Some(secondId, afterSecond) ->
        match Stack.popIndex afterSecond with
        | Some(firstId, afterFirst) -> Ok(firstId, secondId, afterFirst)
        | None -> Error(StackUnderflow(word, 2, Stack.depth state.Stack))

/// Applies an elementwise operation to two tensors of the same shape. A scalar
/// is the rank-0 case, so the old scalar arithmetic is unchanged. A shape
/// mismatch is a named error; the operation is never guessed or broadcast
/// (D60).
let binaryOperation
    (word: string)
    (operation: RealNumber -> RealNumber -> Result<RealNumber, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match popTwoResolved word state with
    | Error error -> Error error
    | Ok(firstTensor, secondTensor, afterFirst) ->
        if firstTensor.Shape <> secondTensor.Shape then
            Error(ShapeMismatch(word, firstTensor.Shape, secondTensor.Shape))
        else
            let results = Array.map2 operation firstTensor.Data secondTensor.Data

            let rec sequence index accumulated =
                if index = results.Length then
                    Ok(List.rev accumulated)
                else
                    match results.[index] with
                    | Error error -> Error error
                    | Ok value -> sequence (index + 1) (value :: accumulated)

            match sequence 0 [] with
            | Error error -> Error error
            | Ok values ->
                let tensor =
                    {
                        Data = Array.ofList values
                        Shape = firstTensor.Shape
                    }

                let (identity, newStore) = Store.addTensor tensor state.Store

                Ok
                    { state with
                        Store = newStore
                        Stack = Stack.push identity afterFirst
                    }
