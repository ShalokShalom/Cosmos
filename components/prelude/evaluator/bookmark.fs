module EvaluatorBookmark

// ============================================================
// Bookmarks: a proto event-stream (see docs/commitments-ledger.typ).
//
// A bookmark tensor stores [Location; Baseline; Position; programId], that is,
// the very program-counter metadata the split-state decision (D4) keeps out of
// the store for *data*. The ledger says the event-stream should absorb and
// generalize these.
//
// Words: bookmark, jump, copyBookmark, showBookmark.
// `showBookmark` appends to `EvaluationState.Output` rather than writing to
// standard output, so this slice stays a pure function of its state.
// ============================================================

open Types
open EvaluatorCommon

let createBookmark (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    // A bookmark is a degenerate one-point event (W8/D29): it records a step of
    // the event-stream and pushes a view naming that step. The step carries the
    // stack and the remaining program, so `jump` restores through the history
    // rather than a parallel mechanism.
    let (historyStepId, recorded) =
        EvaluatorHistory.record (SymbolicAbstraction.Word "bookmark") state.Program state

    let bookmarkTensor =
        Tensor.vector
            [
                double state.Location
                double state.Baseline
                double state.Position
                double (EntryIdentity.value historyStepId)
            ]

    let (bookmarkId, finalImage) = Store.addTensor bookmarkTensor recorded.Store

    Ok
        { recorded with
            Store = finalImage
            Stack = Stack.push bookmarkId recorded.Stack
        }

let executeJump (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "jump" state with
    | Error error -> Error error
    | Ok(bookmarkTensor, _) ->
        if Tensor.size bookmarkTensor < 4 then
            Error InvalidBookmark
        else
            // The fourth field names the step the bookmark recorded. Restoring
            // is the event-stream's rollback, not a second implementation.
            let historyStepId = EntryIdentity.ofInt (int (Tensor.item bookmarkTensor 3))
            EvaluatorHistory.rollbackToStep historyStepId state

let copyBookmark (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.peek state.Stack with
    | Some identity ->
        Ok
            { state with
                Stack = Stack.push identity state.Stack
            }
    | None -> Error(StackUnderflow("copyBookmark", 1, Stack.depth state.Stack))

let showBookmark (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "showBookmark" state with
    | Error error -> Error error
    | Ok(bookmarkTensor, remainingStack) ->
        if Tensor.size bookmarkTensor < 4 then
            Error InvalidBookmark
        else
            let location = int (Tensor.item bookmarkTensor 0)
            let baseline = int (Tensor.item bookmarkTensor 1)
            let position = int (Tensor.item bookmarkTensor 2)

            let line = $"Bookmark: line {baseline}, column {position}, position {location}"

            Ok
                { state with
                    Stack = remainingStack
                    Output = state.Output @ [ line ]
                }
