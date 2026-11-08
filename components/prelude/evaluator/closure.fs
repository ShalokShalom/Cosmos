module EvaluatorClosure

// ============================================================
// Closures and lexical scope (D45).
//
// `let` creates a child frame and makes it current; `lambda` captures the
// current frame in an image-resident closure; `apply` on a closure runs its
// quotation in the captured frame and then restores the caller's frame through
// the internal return token. `define` remains a global word definition and
// `bind` remains a global binding; a bare quotation applied with `apply` keeps
// dynamic scope, exactly as before.
//
// Frames and closures are image data. The current frame and the saved caller
// frames are execution state and are never serialized.
// ============================================================

open Types
open EvaluatorCommon
open EvaluatorCellSystem

/// The internal token `apply` places after a closure body. It is not a surface
/// word and cannot be produced by the tokenizer: the leading NUL is the marker.
let returnToken = "\u0000return"

/// `value "name" let` binds a name in a new child frame and makes that frame
/// current for the rest of the program. `define` and `bind` stay global.
let letWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("let", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Stack.popIndex afterName with
        | None -> Error(StackUnderflow("let", 2, Stack.depth state.Stack))
        | Some(valueId, afterValue) ->
            match Store.readTextAt nameId state.Store with
            | None -> Error(TypeMismatch(ErrorWord "let", ExpectedText))
            | Some _ ->
                let (frameId, store) = addFrameBinding nameId valueId state.CurrentFrame state.Store

                Ok
                    { state with
                        Store = store
                        CurrentFrame = Some frameId
                        Stack = afterValue
                    }

/// `{ body } lambda` captures the current frame in a closure and pushes the
/// closure's address. With no current frame it captures an empty frame, so the
/// body still reads global bindings.
let lambdaWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("lambda", 1, Stack.depth state.Stack))
    | Some(quotationId, remainingStack) ->
        match EvaluatorCode.decode quotationId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "lambda", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            let (environmentId, storeWithFrame) = ensureFrame state.CurrentFrame state.Store

            let (closureId, storeWithClosure) =
                Store.addTensor (encodeClosure quotationId environmentId) storeWithFrame

            Ok
                { state with
                    Store = storeWithClosure
                    Stack = Stack.push closureId remainingStack
                }

/// Starts a call to `closure`: the body runs in the captured frame and an
/// internal return token restores the caller's frame when the body ends.
let startCall
    (closure: Closure)
    (state: EvaluationState)
    (remainingStack: EntryIdentity list)
    : Result<EvaluationState, EvaluationError> =
    match EvaluatorCode.decode closure.Quotation state.Store with
    | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "apply", ExpectedQuotation))
    | Error error -> Error(CodeError(EvaluatorCode.describeError error))
    | Ok quotationTerms ->
        Ok
            { state with
                Program = quotationTerms @ (SymbolicAbstraction.Word returnToken :: state.Program)
                Stack = remainingStack
                CallFrames = state.CurrentFrame :: state.CallFrames
                CurrentFrame = Some closure.Environment
            }

/// The internal return token restores the frame saved by `startCall`. A return
/// without a call is a named refusal, never a silent no-op.
let returnWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match state.CallFrames with
    | previousFrame :: remainingFrames ->
        Ok
            { state with
                CurrentFrame = previousFrame
                CallFrames = remainingFrames
            }
    | [] -> Error UnmatchedReturn
