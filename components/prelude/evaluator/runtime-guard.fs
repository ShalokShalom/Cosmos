module EvaluatorRuntimeGuard

// ============================================================
// Guard metaobjects (D35): an store-resident guard definition.
//
// A guard is `[RuntimeGuardTag; nameId; quotationId]`. The quotation is the
// implementation: it runs in a quotationRun with the value on the stack, so a new
// guard form is data, not an F# case. `guard-holds` answers 1 or 0; an
// undecidable quotationRun is a named refusal, never a silent false.
//
// Words: guard-define, guard-holds.
// ============================================================

open Types
open EvaluatorCommon

/// Encodes a guard definition.
let encode (nameId: EntryIdentity) (quotationId: EntryIdentity) : Tensor =
    Tensor.vector
        [
            RuntimeGuardTag
            double (EntryIdentity.value nameId)
            double (EntryIdentity.value quotationId)
        ]

/// Decodes a guard definition to its name and quotation addresses.
let decode (tensor: Tensor) : (EntryIdentity * EntryIdentity) option =
    if
        Tensor.rank tensor = 1
        && Tensor.size tensor = 3
        && Tensor.item tensor 0 = RuntimeGuardTag
    then
        Some(EntryIdentity.ofInt (int (Tensor.item tensor 1)), EntryIdentity.ofInt (int (Tensor.item tensor 2)))
    else
        None

/// A numeric tensor whose shape is `[1]` is a guard address, so a spawned
/// actor can name a guard without sharing the parent's stack.
let guardOperandId (identity: EntryIdentity) (store: Store) : EntryIdentity =
    match Store.lookupTensor identity store with
    | Some tensor when
        Tensor.rank tensor = 1
        && Tensor.size tensor = 3
        && Tensor.item tensor 0 = RuntimeGuardTag
        ->
        identity
    | Some tensor when Tensor.size tensor = 1 -> EntryIdentity.ofInt (int (Tensor.item tensor 0))
    | _ -> identity

/// Finds a guard metaobject by its declared name. Guards have no root, so this
/// scans the image up to the allocation frontier; the image is append-only, so
/// the newest declaration is the one a name resolves to (latest-write-wins).
let findByName (name: string) (store: Store) : EntryIdentity option =
    Seq.init store.NextIndex (fun index -> EntryIdentity.ofInt index)
    |> Seq.filter (fun identity ->
        match Store.lookupTensor identity store |> Option.bind decode with
        | Some(nameId, _) -> Store.readTextAt nameId store = Some name
        | None -> false)
    |> Seq.tryLast

/// The tensor a field value contributes to a guard quotation.
let valueTensor (value: ScaleFile.FieldValue) : Tensor =
    match value with
    | ScaleFile.BooleanValue flag -> Tensor.scalar (if flag then 1.0 else 0.0)
    | ScaleFile.NumericValue number -> Tensor.scalar number

/// Evaluates the guard metaobject named `name` against `value` without needing
/// the guard on the stack. The probe's store changes are discarded.
let evaluateByName
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (name: string)
    (value: ScaleFile.FieldValue)
    (state: EvaluationState)
    : Result<bool, string> =
    match findByName name state.Store with
    | None -> Error("no guard metaobject is named '" + name + "'")
    | Some guardId ->
        match Store.lookupTensor guardId state.Store |> Option.bind decode with
        | None -> Error("the entry named '" + name + "' is not a guard metaobject")
        | Some(_, quotationId) ->
            let (valueId, store) = Store.addTensor (valueTensor value) state.Store

            match EvaluatorQuotationRun.run executeWord quotationId valueId { state with Store = store } with
            | Error reason -> Error reason
            | Ok tensor ->
                match scalarValue "runtime-guard-evaluate" tensor with
                | Error error -> Error(describeError error)
                | Ok number -> Ok(number <> 0.0)

/// The verification lattice's resolver (D41): a named document guard asks the
/// image-resident guard metaobject. An unreadable or undecidable guard is
/// `GuardUndecided` with the reason, never a silent false.
let resolver
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : TypeLattice.GuardResolver =
    fun name value ->
        match evaluateByName executeWord name value state with
        | Ok true -> TypeLattice.GuardHolds
        | Ok false -> TypeLattice.GuardDoesNotHold
        | Error reason -> TypeLattice.GuardUndecided reason

/// Pushes a boolean scalar.
let private pushBool (state: EvaluationState) (value: bool) (stack: EntryIdentity list) : EvaluationState =
    let (identity, store) =
        Store.addTensor (Tensor.scalar (if value then 1.0 else 0.0)) state.Store

    { state with
        Store = store
        Stack = Stack.push identity stack
    }

/// `"name" { predicate } guard-define` declares an store-resident guard.
let defineWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("runtime-guard-define", 2, Stack.depth state.Stack))
    | Some(quotationId, afterQuotation) ->
        match EvaluatorCode.decode quotationId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "runtime-guard-define", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            match Stack.popIndex afterQuotation with
            | None -> Error(StackUnderflow("runtime-guard-define", 2, Stack.depth state.Stack))
            | Some(nameId, remaining) ->
                match Store.readTextAt nameId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "runtime-guard-define", ExpectedText))
                | Some _ ->
                    let (identity, store) = Store.addTensor (encode nameId quotationId) state.Store

                    Ok
                        { state with
                            Store = store
                            Stack = Stack.push identity remaining
                        }

/// `value guard guard-holds` runs the guard's quotation against the value.
let holdsWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("runtime-guard-holds", 2, Stack.depth state.Stack))
    | Some(reference, afterGuard) ->
        let guardId = guardOperandId reference state.Store

        match Stack.popIndex afterGuard with
        | None -> Error(StackUnderflow("runtime-guard-holds", 2, Stack.depth state.Stack))
        | Some(valueId, remaining) ->
            match Store.lookupTensor guardId state.Store |> Option.bind decode with
            | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value guardId), ExpectedRuntimeGuard))
            | Some(_, quotationId) ->
                match EvaluatorQuotationRun.run executeWord quotationId valueId state with
                | Error reason -> Error(RuntimeGuardUndecided reason)
                | Ok tensor ->
                    match scalarValue "runtime-guard-holds" tensor with
                    | Error error -> Error(RuntimeGuardUndecided(describeError error))
                    | Ok value -> Ok(pushBool state (value <> 0.0) remaining)
