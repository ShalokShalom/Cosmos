module EvaluatorCondition

// ============================================================
// Conditions and restarts (D50).
//
// A condition is an image-resident value: kind, name, and message. A restart is
// an image-resident value carrying a name and a handler quotation. A dynamic
// handler frame is execution state, never serialized. `with-handler` catches a
// failed body, turns the failure into an error condition, and runs the handler
// with that condition. `signal` raises a condition explicitly; with no handler
// it leaves the condition on the stack as a value. `invoke-restart` runs a
// named restart handler with the condition.
// ============================================================

open Types
open EvaluatorCommon

let codeTerms
    (word: string)
    (identity: EntryIdentity)
    (store: Store)
    : Result<SymbolicAbstraction.Term list, EvaluationError> =
    match EvaluatorCode.decode identity store with
    | Ok terms -> Ok terms
    | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord word, ExpectedQuotation))
    | Error error -> Error(CodeError(EvaluatorCode.describeError error))
open EvaluatorCellSystem

/// The internal token a handler body carries. It is not a surface word.
let handlerReturnToken = "\u0000handler-return"

let private handlerFrame (state: EvaluationState) : HandlerFrame option =
    match state.Handlers with
    | frame :: _ -> Some frame
    | [] -> None

/// `"kind" "name" "message" condition` creates an image-resident condition.
let conditionWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("condition", 3, Stack.depth state.Stack))
    | Some(messageId, afterMessage) ->
        match Store.readTextAt messageId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "condition", ExpectedText))
        | Some _ ->
            match Stack.popIndex afterMessage with
            | None -> Error(StackUnderflow("condition", 3, Stack.depth state.Stack))
            | Some(nameId, afterName) ->
                match Store.readTextAt nameId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "condition", ExpectedText))
                | Some _ ->
                    match Stack.popIndex afterName with
                    | None -> Error(StackUnderflow("condition", 3, Stack.depth state.Stack))
                    | Some(kindId, remainingStack) ->
                        match Store.readTextAt kindId state.Store with
                        | None -> Error(TypeMismatch(ErrorWord "condition", ExpectedText))
                        | Some kind ->
                            let parsedKind =
                                match kind with
                                | "error" -> Ok ErrorCondition
                                | "warning" -> Ok WarningCondition
                                | other -> Error(EnumError(EnumFault.InvalidConditionKind other))

                            parsedKind
                            |> Result.bind (fun conditionKind ->
                                let (conditionId, store) =
                                    Store.addTensor (encodeCondition conditionKind nameId messageId) state.Store

                                Ok
                                    { state with
                                        Store = store
                                        Stack = Stack.push conditionId remainingStack
                                    })

/// `"name" handler restart` creates an image-resident restart. The handler is a
/// quotation run by `invoke-restart` with the condition on the stack.
let restartWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("restart", 2, Stack.depth state.Stack))
    | Some(handlerId, afterHandler) ->
        match codeTerms "restart" handlerId state.Store with
        | Error error -> Error error
        | Ok _ ->
            match Stack.popIndex afterHandler with
            | None -> Error(StackUnderflow("restart", 2, Stack.depth state.Stack))
            | Some(nameId, remainingStack) ->
                match Store.readTextAt nameId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "restart", ExpectedText))
                | Some _ ->
                    let (restartId, store) =
                        Store.addTensor (encodeRestart nameId handlerId) state.Store

                    Ok
                        { state with
                            Store = store
                            Stack = Stack.push restartId remainingStack
                        }

/// `body handler with-handler` runs `body`; if it fails, the failure becomes an
/// error condition and `handler` runs with that condition on the stack.
let withHandlerWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("with-handler", 2, Stack.depth state.Stack))
    | Some(handlerId, afterHandler) ->
        match codeTerms "with-handler" handlerId state.Store with
        | Error error -> Error error
        | Ok _ ->
            match Stack.popIndex afterHandler with
            | None -> Error(StackUnderflow("with-handler", 2, Stack.depth state.Stack))
            | Some(bodyId, remainingStack) ->
                match codeTerms "with-handler" bodyId state.Store with
                | Error error -> Error error
                | Ok bodyTerms ->
                    let frame =
                        {
                            Handler = handlerId
                            CallerProgram = state.Program
                            CallerStack = remainingStack
                            Phase = BodyPhase
                        }

                    Ok
                        { state with
                            Program = bodyTerms @ (SymbolicAbstraction.Word handlerReturnToken :: state.Program)
                            Stack = []
                            Handlers = frame :: state.Handlers
                        }

/// The internal handler-return token. It restores the caller's program and
/// stack and leaves the handler's result on top.
let handlerReturnWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match state.Handlers with
    | frame :: remaining ->
        match Stack.peek state.Stack with
        | None -> Error(EmptyValue "with-handler")
        | Some result ->
            Ok
                { state with
                    Program = frame.CallerProgram
                    Stack = Stack.push result frame.CallerStack
                    Handlers = remaining
                }
    | [] -> Error(UnmatchedReturn)

/// `condition signal` raises a condition. With a body-phase handler it switches
/// to the handler; with no handler it leaves the condition on the stack as a
/// value, never as an exception.
let signalWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("signal", 1, Stack.depth state.Stack))
    | Some(conditionId, remainingStack) ->
        match
            Store.lookupTensor conditionId state.Store
            |> Option.bind (decodeCondition conditionId)
        with
        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value conditionId), ExpectedConditionValue))
        | Some _ ->
            match state.Handlers with
            | frame :: rest when frame.Phase = BodyPhase ->
                match codeTerms "signal" frame.Handler state.Store with
                | Error error -> Error error
                | Ok handlerTerms ->
                    Ok
                        { state with
                            Program = handlerTerms @ (SymbolicAbstraction.Word handlerReturnToken :: state.Program)
                            Stack = conditionId :: frame.CallerStack
                            Handlers = { frame with Phase = HandlerPhase } :: rest
                        }
            | _ ->
                Ok
                    { state with
                        Stack = Stack.push conditionId remainingStack
                    }

/// `condition restart invoke-restart` runs the restart's handler with the
/// condition. The handler's result continues into the caller's program.
let invokeRestartWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("invoke-restart", 2, Stack.depth state.Stack))
    | Some(restartId, afterRestart) ->
        match
            Store.lookupTensor restartId state.Store
            |> Option.bind (decodeRestart restartId)
        with
        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value restartId), ExpectedRestart))
        | Some restart ->
            match Stack.popIndex afterRestart with
            | None -> Error(StackUnderflow("invoke-restart", 2, Stack.depth state.Stack))
            | Some(conditionId, remainingStack) ->
                match
                    Store.lookupTensor conditionId state.Store
                    |> Option.bind (decodeCondition conditionId)
                with
                | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value conditionId), ExpectedConditionValue))
                | Some _ ->
                    match codeTerms "invoke-restart" restart.Handler state.Store with
                    | Error error -> Error error
                    | Ok handlerTerms ->
                        Ok
                            { state with
                                Program = handlerTerms @ state.Program
                                Stack = conditionId :: remainingStack
                            }

/// Turns a failed body into an error condition and starts its handler. A body
/// without an enclosing body-phase handler is not handled.
let handleError (diagnostic: Diagnostic) (state: EvaluationState) : EvaluationState option =
    match state.Handlers with
    | frame :: rest when frame.Phase = BodyPhase ->
        match codeTerms "with-handler" frame.Handler state.Store with
        | Error _ -> None
        | Ok handlerTerms ->
            let (nameId, store1) = Store.addText "evaluation-error" state.Store
            let (messageId, store2) = Store.addText (describeError diagnostic.Error) store1

            let (conditionId, store3) =
                Store.addTensor (encodeCondition ErrorCondition nameId messageId) store2

            Some
                { state with
                    Store = store3
                    Program = handlerTerms @ (SymbolicAbstraction.Word handlerReturnToken :: state.Program)
                    Stack = conditionId :: frame.CallerStack
                    Handlers = { frame with Phase = HandlerPhase } :: rest
                }
    | _ -> None
