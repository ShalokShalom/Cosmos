module EvaluatorWord

// ============================================================
// Word metaobject word: word.
// D16 Stage 2 / D21 — declares store-resident word metaobjects.
// Stack-based like generic/method: pops 5 operands from stack.
// ============================================================

open Types
open EvaluatorCommon
open EvaluatorDispatch
open Stack

/// `implementation active category form token word` declares a word metaobject.
/// Stack order (top first): implementation, active, category, form, token
/// token: text literal EntryIdentity (e.g., "myWord")
/// form: text literal EntryIdentity (e.g., "my word")
/// category: WordCategory discriminant as scalar (0=TypeName, ..., 10=Article)
/// active: boolean as scalar (1.0=true, 0.0=false)
/// implementation: EntryIdentity scalar (0.0=built-in, else generic function name EntryIdentity)
/// Stack: implementation active category form token -> (void)
let wordWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popIndex state.Stack with
    | None -> Error(StackUnderflow("word", 5, Stack.depth state.Stack))
    | Some(implementationId, afterImpl) ->
        match popIndex afterImpl with
        | None -> Error(StackUnderflow("word", 5, Stack.depth state.Stack))
        | Some(activeId, afterActive) ->
            match popIndex afterActive with
            | None -> Error(StackUnderflow("word", 5, Stack.depth state.Stack))
            | Some(categoryId, afterCategory) ->
                match popIndex afterCategory with
                | None -> Error(StackUnderflow("word", 5, Stack.depth state.Stack))
                | Some(formId, afterForm) ->
                    match popIndex afterForm with
                    | None -> Error(StackUnderflow("word", 5, Stack.depth state.Stack))
                    | Some(tokenId, remainingStack) ->
                        // Get the tensors
                        match Store.lookupTensor implementationId state.Store with
                        | None -> Error(UnknownIndex(EntryIdentity.value implementationId))
                        | Some implementationTensor ->
                            match Store.lookupTensor activeId state.Store with
                            | None -> Error(UnknownIndex(EntryIdentity.value activeId))
                            | Some activeTensor ->
                                match Store.lookupTensor categoryId state.Store with
                                | None -> Error(UnknownIndex(EntryIdentity.value categoryId))
                                | Some categoryTensor ->
                                    match Store.lookupTensor formId state.Store with
                                    | None -> Error(UnknownIndex(EntryIdentity.value formId))
                                    | Some formTensor ->
                                        match Store.lookupTensor tokenId state.Store with
                                        | None -> Error(UnknownIndex(EntryIdentity.value tokenId))
                                        | Some tokenTensor ->
                                            // Parse implementation reference
                                            match scalarValue "word" implementationTensor with
                                            | Error error -> Error error
                                            | Ok implementationValue ->
                                                let (implementationReference, store) =
                                                    if implementationValue = 0.0 then
                                                        (None, state.Store)
                                                    else
                                                        (Some(EntryIdentity.ofInt (int implementationValue)),
                                                         state.Store)

                                                // Parse active flag
                                                match scalarValue "word" activeTensor with
                                                | Error error -> Error error
                                                | Ok activeValue ->
                                                    let isActiveResult =
                                                        if activeValue = 1.0 then Ok true
                                                        elif activeValue = 0.0 then Ok false
                                                        else Error(EnumError(EnumFault.InvalidWordActive (string activeValue)))

                                                    match isActiveResult with
                                                    | Error error -> Error error
                                                    | Ok isActive ->
                                                        // Parse category
                                                        match scalarValue "word" categoryTensor with
                                                        | Error error -> Error error
                                                        | Ok categoryValue ->
                                                            let cat = int categoryValue

                                                            if cat < 0 || cat > 10 then
                                                                Error(EnumError(EnumFault.InvalidWordCategory (string categoryValue)))
                                                            else
                                                                let category =
                                                                    match cat with
                                                                    | 0 -> TypeName
                                                                    | 1 -> FieldName
                                                                    | 2 -> FunctionName
                                                                    | 3 -> Quantifier
                                                                    | 4 -> Connective
                                                                    | 5 -> Conditional
                                                                    | 6 -> Negation
                                                                    | 7 -> Pronoun
                                                                    | 8 -> Copula
                                                                    | 9 -> Comparison
                                                                    | _ -> Article

                                                                // Get form as text entry
                                                                match Store.readTextAt formId store with
                                                                | None -> Error(TypeMismatch(ErrorWord "word", ExpectedText))
                                                                | Some form ->
                                                                    // Get token as text entry
                                                                    match Store.readTextAt tokenId store with
                                                                    | None -> Error(TypeMismatch(ErrorWord "word", ExpectedText))
                                                                    | Some token ->
                                                                        // Encode word metaobject
                                                                        let categoryCode =
                                                                            match category with
                                                                            | TypeName -> 0.0
                                                                            | FieldName -> 1.0
                                                                            | FunctionName -> 2.0
                                                                            | Quantifier -> 3.0
                                                                            | Connective -> 4.0
                                                                            | Conditional -> 5.0
                                                                            | Negation -> 6.0
                                                                            | Pronoun -> 7.0
                                                                            | Copula -> 8.0
                                                                            | Comparison -> 9.0
                                                                            | Article -> 10.0

                                                                        let activeCode = if isActive then 1.0 else 0.0

                                                                        let implementationCode =
                                                                            match implementationReference with
                                                                            | Some identity ->
                                                                                double (EntryIdentity.value identity)
                                                                            | None -> 0.0

                                                                        let wordTensor =
                                                                            Tensor.vector
                                                                                [
                                                                                    WordTag
                                                                                    double (EntryIdentity.value tokenId)
                                                                                    double (EntryIdentity.value formId)
                                                                                    categoryCode
                                                                                    activeCode
                                                                                    implementationCode
                                                                                ]

                                                                        let (wordId, store1) =
                                                                            Store.addTensor wordTensor store

                                                                        // Add to word root
                                                                        let (newWordRoot, store2) =
                                                                            match state.WordTable with
                                                                            | Some rootId ->
                                                                                match
                                                                                    Store.lookupTensor rootId store1
                                                                                with
                                                                                | Some vector ->
                                                                                    Store.addTensor
                                                                                        (Tensor.concat
                                                                                            vector
                                                                                            (Tensor.vector
                                                                                                [
                                                                                                    double (
                                                                                                        EntryIdentity
                                                                                                            .value
                                                                                                            wordId
                                                                                                    )
                                                                                                ]))
                                                                                        store1
                                                                                | None ->
                                                                                    Store.addTensor
                                                                                        (Tensor.vector
                                                                                            [
                                                                                                double (
                                                                                                    EntryIdentity.value
                                                                                                        wordId
                                                                                                )
                                                                                            ])
                                                                                        store1
                                                                            | None ->
                                                                                Store.addTensor
                                                                                    (Tensor.vector
                                                                                        [
                                                                                            double (
                                                                                                EntryIdentity.value
                                                                                                    wordId
                                                                                            )
                                                                                        ])
                                                                                    store1

                                                                        Ok
                                                                            { state with
                                                                                Store = store2
                                                                                WordTable = Some newWordRoot
                                                                                Stack = remainingStack
                                                                            }

/// Helper to decode a word metaobject from the store
let private decodeWord (wordId: EntryIdentity) (store: Store) : Word option =
    match Store.lookupTensor wordId store with
    | Some tensor when
        Tensor.rank tensor = 1
        && Tensor.size tensor >= 6
        && Tensor.item tensor 0 = WordTag
        ->
        let tokenId = EntryIdentity.ofInt (int (Tensor.item tensor 1))
        let formId = EntryIdentity.ofInt (int (Tensor.item tensor 2))
        let categoryCode = int (Tensor.item tensor 3)
        let activeCode = Tensor.item tensor 4
        let implementationCode = Tensor.item tensor 5

        let category =
            match categoryCode with
            | 0 -> Some TypeName
            | 1 -> Some FieldName
            | 2 -> Some FunctionName
            | 3 -> Some Quantifier
            | 4 -> Some Connective
            | 5 -> Some Conditional
            | 6 -> Some Negation
            | 7 -> Some Pronoun
            | 8 -> Some Copula
            | 9 -> Some Comparison
            | 10 -> Some Article
            | _ -> None

        match category with
        | Some category ->
            let isActive = activeCode <> 0.0

            let implementationReference =
                if implementationCode = 0.0 then
                    None
                else
                    Some(EntryIdentity.ofInt (int implementationCode))

            Some
                {
                    Address = wordId
                    Token = tokenId
                    Form = formId
                    Category = category
                    IsActive = isActive
                    Implementation = implementationReference
                }
        | None -> None
    | _ -> None

/// Looks up a word metaobject by token (latest-write-wins)
let findByToken (token: string) (wordRoot: EntryIdentity option) (store: Store) : Word option =
    match wordRoot with
    | None -> None
    | Some rootId ->
        match Store.lookupTensor rootId store with
        | None -> None
        | Some vector ->
            vector.Data
            |> Array.toList
            |> List.rev
            |> List.tryPick (fun identity ->
                let wordId = EntryIdentity.ofInt (int identity)

                match decodeWord wordId store with
                | Some word when Store.readText word.Token store = Some token -> Some word
                | _ -> None)
