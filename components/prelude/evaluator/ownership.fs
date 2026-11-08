module EvaluatorOwnership

// ============================================================
// Linear ownership (W9, D30).
//
// Ownership is execution state (D1): a run owns references to store entries,
// and the append-only store is never rewritten. The checks are dynamic:
// `own` takes ownership for the current actor, `borrow` is a checked read,
// `consume` ends the value's life for the run, and `release` gives ownership
// up. A value owned by another actor is a named refusal; a spawned actor
// starts owning nothing.
//
// Words: own, borrow, consume, release.
// ============================================================

open Types
open EvaluatorCommon

/// Pops a value reference and checks that the entry exists.
let private popValue
    (word: string)
    (state: EvaluationState)
    : Result<EntryIdentity * EntryIdentity list, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow(word, 1, Stack.depth state.Stack))
    | Some(identity, remaining) ->
        match Store.lookupTensor identity state.Store with
        | Some _ -> Ok(identity, remaining)
        | None -> Error(UnknownIndex(EntryIdentity.value identity))

let private ownerOf (state: EvaluationState) : EntryIdentity = state.SchedulerState.SelfId

let private pushBack (identity: EntryIdentity) (state: EvaluationState) : EvaluationState =
    { state with
        Stack = Stack.push identity state.Stack
    }

/// Takes ownership of a value for the current actor. Idempotent for a value
/// the actor already owns; a consumed value or another actor's value is
/// refused by name.
let ownWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popValue "own" state with
    | Error error -> Error error
    | Ok(identity, remaining) ->
        let owner = ownerOf state
        let state = { state with Stack = remaining }

        if state.Ownership.Consumed.Contains identity then
            Error(ValueConsumed(EntryIdentity.value identity))
        else
            match state.Ownership.Owners.TryFind identity with
            | Some current when current <> owner ->
                Error(OwnedByAnother(EntryIdentity.value identity, EntryIdentity.value current))
            | _ ->
                let ownership =
                    { state.Ownership with
                        Owners = state.Ownership.Owners.Add(identity, owner)
                    }

                Ok(pushBack identity { state with Ownership = ownership })

/// A checked read: the value must be owned by the current actor and not
/// consumed. The reference stays on the stack.
let borrowWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popValue "borrow" state with
    | Error error -> Error error
    | Ok(identity, remaining) ->
        let owner = ownerOf state
        let state = { state with Stack = remaining }

        if state.Ownership.Consumed.Contains identity then
            Error(ValueConsumed(EntryIdentity.value identity))
        else
            match state.Ownership.Owners.TryFind identity with
            | Some current when current = owner -> Ok(pushBack identity state)
            | Some current -> Error(OwnedByAnother(EntryIdentity.value identity, EntryIdentity.value current))
            | None -> Error(NotOwned(EntryIdentity.value identity, EntryIdentity.value owner))

/// Ends the value's life for this run: a second consume, or a borrow
/// afterwards, is `ValueConsumed`. The entry stays in the append-only store;
/// the check is the discipline, not an store rewrite.
let consumeWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popValue "consume" state with
    | Error error -> Error error
    | Ok(identity, remaining) ->
        let owner = ownerOf state
        let state = { state with Stack = remaining }

        if state.Ownership.Consumed.Contains identity then
            Error(ValueConsumed(EntryIdentity.value identity))
        else
            match state.Ownership.Owners.TryFind identity with
            | Some current when current = owner ->
                let ownership =
                    { state.Ownership with
                        Consumed = state.Ownership.Consumed.Add identity
                    }

                Ok(pushBack identity { state with Ownership = ownership })
            | Some current -> Error(OwnedByAnother(EntryIdentity.value identity, EntryIdentity.value current))
            | None -> Error(NotOwned(EntryIdentity.value identity, EntryIdentity.value owner))

/// Gives ownership up without consuming the value. Releasing a consumed value
/// is allowed: it only drops the ownership record.
let releaseWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popValue "release" state with
    | Error error -> Error error
    | Ok(identity, remaining) ->
        let owner = ownerOf state
        let state = { state with Stack = remaining }

        match state.Ownership.Owners.TryFind identity with
        | Some current when current = owner ->
            let ownership =
                { state.Ownership with
                    Owners = state.Ownership.Owners.Remove identity
                }

            Ok(pushBack identity { state with Ownership = ownership })
        | Some current -> Error(OwnedByAnother(EntryIdentity.value identity, EntryIdentity.value current))
        | None -> Error(NotOwned(EntryIdentity.value identity, EntryIdentity.value owner))

/// Pushes the addresses the append-only image can reclaim: entries with no
/// possible reference from a live root. The report is conservative, and it only
/// names candidates; it never rewrites the image.
let reclaimableWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let report = Reclaim.reportForState state

    let (identity, store) =
        Store.addTensor
            (Tensor.vector (
                report.Reclaimable
                |> List.map (fun candidate -> double (EntryIdentity.value candidate))
            ))
            state.Store

    Ok
        { state with
            Store = store
            Stack = Stack.push identity state.Stack
        }

/// Replaces the current image with a sparse image that keeps only the entries
/// reachable from the live roots. Existing addresses are unchanged, so no
/// reference needs rewriting; the previous image is not mutated.
let compactWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    Ok
        { state with
            Store = Reclaim.compactForState state
        }

/// Pushes a static linear-contract verdict for a quotation. The checker is
/// conservative: a word it does not model is Undecided, never a silent pass.
let linearityWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("linearity", 1, Stack.depth state.Stack))
    | Some(identity, remaining) ->
        match Store.readTextAt identity state.Store with
        | None -> Error(TypeMismatch(ErrorWord "linearity", ExpectedQuotation))
        | Some text ->
            let verdict = OwnershipTypes.checkQuotation text

            let (reasonId, store1) =
                match verdict with
                | OwnershipTypes.Linear -> (EntryIdentity.ofInt 0, state.Store)
                | OwnershipTypes.Nonlinear reason
                | OwnershipTypes.Undecided reason -> Store.addText reason state.Store

            let status =
                match verdict with
                | OwnershipTypes.Linear -> 0.0
                | OwnershipTypes.Nonlinear _ -> 1.0
                | OwnershipTypes.Undecided _ -> 2.0

            let (resultId, store2) =
                Store.addTensor
                    (Tensor.vector [ LinearVerdictTag; status; double (EntryIdentity.value reasonId) ])
                    store1

            Ok
                { state with
                    Store = store2
                    Stack = Stack.push resultId remaining
                }
