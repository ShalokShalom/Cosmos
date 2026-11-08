module EvaluatorStack

// ============================================================
// Stack shuffling: words that reorder the stack by reference rather than
// resolving values.
//
// Words: swap, over, rotate, duplicate, drop.
// ============================================================

open Types
open EvaluatorCommon

let swapWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoIndices "swap" state with
    | Error error -> Error error
    | Ok(firstId, secondId, afterFirst) ->
        // Stack is [..; first; second] with second on top; exchange them.
        Ok
            { state with
                Stack = afterFirst |> Stack.push secondId |> Stack.push firstId
            }

let overWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoIndices "over" state with
    | Error error -> Error error
    | Ok(firstId, secondId, afterFirst) ->
        Ok
            { state with
                Stack = afterFirst |> Stack.push firstId |> Stack.push secondId |> Stack.push firstId
            }

let rotateWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("rotate", 3, Stack.depth state.Stack))
    | Some(thirdId, afterThird) ->
        match Stack.popIndex afterThird with
        | None -> Error(StackUnderflow("rotate", 3, Stack.depth state.Stack))
        | Some(secondId, afterSecond) ->
            match Stack.popIndex afterSecond with
            | None -> Error(StackUnderflow("rotate", 3, Stack.depth state.Stack))
            | Some(firstId, afterFirst) ->
                Ok
                    { state with
                        Stack = afterFirst |> Stack.push secondId |> Stack.push thirdId |> Stack.push firstId
                    }

let duplicateWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.peek state.Stack with
    | Some identity ->
        Ok
            { state with
                Stack = Stack.push identity state.Stack
            }
    | None -> Error(StackUnderflow("duplicate", 1, Stack.depth state.Stack))

let dropWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | Some(_, remainingStack) -> Ok { state with Stack = remainingStack }
    | None -> Error(StackUnderflow("drop", 1, Stack.depth state.Stack))
