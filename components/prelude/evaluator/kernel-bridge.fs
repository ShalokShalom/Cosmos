module EvaluatorKernelBridge

// ============================================================
// The Prelude adapter for the kernel abstraction's Step 1 execution path.
//
// The kernel owns the machine state: store, stack, and pending program. The
// full F# evaluation state travels as the opaque host value, so a primitive call
// may change Prelude-only tables, frames, or ownership and the kernel carries
// those changes forward without knowing their types.
// ============================================================

open Types
open KernelState

let toKernel (state: EvaluationState) : KernelState<EvaluationState> =
    KernelState.create state state.Store state.Stack (EvaluatorProgram.toKernelTokens state.Program)

let fromKernel (state: KernelState<EvaluationState>) : EvaluationState =
    { state.Host with
        Store = state.Store
        Stack = state.Stack
        Program = EvaluatorProgram.ofTokensOrFail state.Program
    }

let executeWord (token: string) (state: KernelState<EvaluationState>) : Result<KernelState<EvaluationState>, string> =
    Evaluator.executeWordStrict token (fromKernel state)
    |> Result.map toKernel
    |> Result.mapError describeError
