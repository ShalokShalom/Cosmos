module BookHost

// ============================================================
// The prose-side book adapter (D184). `cast` is a host word: the adapter pops
// the path, the word, and the argument values, loads the package, and returns
// the book's result to the caller's stack.
// ============================================================

open Types

let private popValues
    (arity: int)
    (state: EvaluationState)
    : Result<EntryIdentity list * EntryIdentity list, EvaluationError> =
    let rec loop
        (remainingCount: int)
        (remainingStack: EntryIdentity list)
        (accumulated: EntryIdentity list)
        : Result<EntryIdentity list * EntryIdentity list, EvaluationError> =
        if remainingCount = 0 then
            Ok(accumulated, remainingStack)
        else
            match Stack.popIndex remainingStack with
            | None -> Error(StackUnderflow("cast", arity, Stack.depth remainingStack))
            | Some(identity, rest) -> loop (remainingCount - 1) rest (identity :: accumulated)

    loop arity state.Stack []

let private executeCast (arity: int) (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    if arity = 0 then
        Error(HostFailure "cast needs a book path")
    else
        match popValues arity state with
        | Error error -> Error error
        | Ok(values, remaining) ->
            match Store.readTextAt values.[0] state.Store with
            | None -> Error(HostFailure "the cast path must be text")
            | Some path ->
                let target =
                    if arity = 1 then
                        Ok(None, [])
                    else
                        match Store.readTextAt values.[1] state.Store with
                        | None -> Error(HostFailure "the cast word must be text")
                        | Some word -> Ok(Some word, values |> List.skip 2)

                match target with
                | Error error -> Error error
                | Ok(word, arguments) ->
                    let caller = { state with Stack = remaining }

                    match BookRunner.cast caller path word arguments with
                    | Error reason -> Error(HostFailure reason)
                    | Ok final -> Ok final

/// The host-word adapter for `cast`.
let wordAdapter : HostWordAdapter =
    { new HostWordAdapter with
        member _.Capability = "book"
        member _.TryExecute(token, state) =
            match HostWords.tryDecodeBook token with
            | Some("cast", arity) -> Some(executeCast arity state)
            | Some(name, _) -> Some(Error(HostFailure("unknown book word: " + name)))
            | None -> None }
