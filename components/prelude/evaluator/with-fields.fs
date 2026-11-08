module EvaluatorWithFields

// ============================================================
// `instance { field ... } { body } with-fields`
//
// Reads each named slot, binds it with `let` in a child frame, closes over the
// body, and applies the closure. The caller frame is restored by the normal
// closure return token.
// ============================================================

open Types
open EvaluatorCommon

let withFieldsWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("with-fields", 3, Stack.depth state.Stack))
    | Some(bodyId, afterBody) ->
        match Stack.popIndex afterBody with
        | None -> Error(StackUnderflow("with-fields", 3, Stack.depth state.Stack))
        | Some(fieldsId, afterFields) ->
            match Stack.popIndex afterFields with
            | None -> Error(StackUnderflow("with-fields", 3, Stack.depth state.Stack))
            | Some(instanceId, remainingStack) ->
                match EvaluatorCode.decode fieldsId state.Store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "with-fields", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok fieldTerms ->
                    let fieldText = EvaluatorProgram.toKernelTokens fieldTerms |> String.concat " "

                    let bindField
                        (current: EvaluationState)
                        (fieldName: string)
                        : Result<EvaluationState, EvaluationError> =
                        let (nameId, store1) = Store.addText fieldName current.Store

                        let readState =
                            { current with
                                Store = store1
                                Stack = Stack.push nameId (Stack.push instanceId current.Stack)
                            }

                        EvaluatorClassSystem.slotValueWord readState
                        |> Result.bind (fun afterRead ->
                            EvaluatorClosure.letWord { afterRead with Stack = Stack.push nameId afterRead.Stack })

                    let bound =
                        splitText fieldText
                        |> List.fold
                            (fun accumulated fieldName ->
                                match accumulated with
                                | Error error -> Error error
                                | Ok current -> bindField current fieldName)
                            (Ok { state with Stack = remainingStack })

                    bound
                    |> Result.bind (fun afterBindings ->
                        EvaluatorClosure.lambdaWord
                            { afterBindings with
                                Stack = Stack.push bodyId afterBindings.Stack
                            }
                        |> Result.bind (fun withClosure -> executeWord "apply" withClosure))
