module WriterHarness

// ============================================================
// D128: the AOT-safe runtime harness for emitted code.
//
// The writer emits IL that threads the real `KernelState<EvaluationState>`
// through these static helpers. Value instructions delegate to
// `LoweredAbstraction.step`, so the interpreter and emitted code share one semantics.
// The writer is JIT-only; this module is compiled always and uses no
// reflection, so an emitted assembly statically referenced by the AOT build can
// call it.
// ============================================================

open Types
open KernelState
open LoweredInstructions

type AbstractionFault(reason: string) =
    inherit System.Exception(reason)
    member _.Reason = reason

let executeOne
    (instruction: Instruction)
    (state: KernelState<EvaluationState>)
    : KernelState<EvaluationState> =
    match LoweredAbstraction.step EvaluatorKernelBridge.executeWord instruction state with
    | Ok next -> next
    | Error(LoweredAbstraction.LoweredFailure reason) -> raise (AbstractionFault reason)
    | Error(LoweredAbstraction.HostFailure reason) -> raise (AbstractionFault reason)

[<AbstractClass; Sealed>]
type WriterRuntime =
    static member LiteralNumber(state, value) = executeOne (LiteralNumber value) state
    static member LiteralText(state, value) = executeOne (LiteralText value) state
    static member Add(state) = executeOne Add state
    static member Subtract(state) = executeOne Subtract state
    static member Multiply(state) = executeOne Multiply state
    static member Divide(state) = executeOne Divide state
    static member LessThan(state) = executeOne LessThan state
    static member GreaterThan(state) = executeOne GreaterThan state
    static member Equals(state) = executeOne Equals state
    static member Duplicate(state) = executeOne Duplicate state
    static member Drop(state) = executeOne Drop state
    static member Swap(state) = executeOne Swap state
    static member Over(state) = executeOne Over state
    static member Rotate(state) = executeOne Rotate state
    static member CallWord(state, name) = executeOne (CallWord(name, None)) state

    static member EnterUnit(state: KernelState<EvaluationState>, arity: int, captures: EntryIdentity[]) =
        let rec pop count stack accumulated =
            if count = 0 then
                (accumulated, stack)
            else
                match Stack.popIndex stack with
                | Some(identity, rest) -> pop (count - 1) rest (identity :: accumulated)
                | None -> raise (AbstractionFault("unit needs " + string arity + " arguments"))

        let (arguments, remaining) = pop arity state.Stack []

        { state with
            Stack = remaining
            CaptureFrames = (arguments @ List.ofArray captures) :: state.CaptureFrames }

    static member ExitUnit(state: KernelState<EvaluationState>) =
        match state.CaptureFrames with
        | _ :: tail -> { state with CaptureFrames = tail }
        | [] -> raise (AbstractionFault "unit exit has no frame")

    static member PushCapture(state: KernelState<EvaluationState>, index: int) =
        match state.CaptureFrames with
        | frame :: _ ->
            match List.tryItem index frame with
            | Some identity -> { state with Stack = Stack.push identity state.Stack }
            | None -> raise (AbstractionFault("closure capture " + string index + " is not present"))
        | [] -> raise (AbstractionFault "push-capture has no closure frame")

    static member Push(state: KernelState<EvaluationState>, identity: EntryIdentity) =
        { state with Stack = Stack.push identity state.Stack }

    static member Pop(state: KernelState<EvaluationState>) : EntryIdentity * KernelState<EvaluationState> =
        match Stack.popIndex state.Stack with
        | Some(identity, rest) -> (identity, { state with Stack = rest })
        | None -> raise (AbstractionFault "pop has no operand")

    static member PopNumber(state: KernelState<EvaluationState>) : double * KernelState<EvaluationState> =
        match Stack.popResolved state.Store state.Stack with
        | Some(tensor, rest) ->
            match ScalarRead.readScalar tensor with
            | Ok value -> (value, { state with Stack = rest })
            | Error reason -> raise (AbstractionFault reason)
        | None -> raise (AbstractionFault "pop-number has no operand")

    static member Fail(state: KernelState<EvaluationState>, reason: string) : KernelState<EvaluationState> =
        ignore state
        raise (AbstractionFault reason)
