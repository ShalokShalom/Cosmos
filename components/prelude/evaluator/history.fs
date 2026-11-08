module EvaluatorHistory

// ============================================================
// The event-stream: every executed token appends a step to an store-resident,
// append-only history (W8).
//
// A step records the program-counter metadata (location, baseline, position),
// the token, the stack before the token ran, and the program it will resume.
// The store is append-only, so everything a step references is still valid
// later: rollback restores a state, it never rewrites or truncates the store.
//
// The history root is a vector of step addresses. Rolling back re-points the
// root at the steps before the target (a new appended vector); the steps after
// it stay in the store but are unreachable, which is the append-only
// discipline. A bookmark is a degenerate one-point event: it records a step
// and names it.
//
// Words: history, rollback.
// ============================================================

open Types
open EvaluatorCommon

let private readVector (vectorId: EntryIdentity) (store: Store) : EntryIdentity list =
    match Store.lookupTensor vectorId store with
    | Some tensor when Tensor.rank tensor = 1 ->
        tensor.Data
        |> Array.toList
        |> List.map (fun value -> EntryIdentity.ofInt (int value))
    | _ -> []

/// The addresses of the steps recorded so far, oldest first. The step tensors
/// form a persistent chain through their final `Previous` field, so walking is
/// O(n) only when a caller asks for the list; appending is O(1).
let stepIds (state: EvaluationState) : EntryIdentity list =
    let rec collect (current: EntryIdentity option) (accumulated: EntryIdentity list) =
        match current with
        | None -> accumulated
        | Some historyStepId ->
            match Store.lookupTensor historyStepId state.Store with
            | Some tensor when
                Tensor.rank tensor = 1
                && Tensor.size tensor = 8
                && Tensor.item tensor 0 = HistoryStepTag
                ->
                let previous = int (Tensor.item tensor 7)

                let next =
                    if previous < 0 then
                        None
                    else
                        Some(EntryIdentity.ofInt previous)

                collect next (historyStepId :: accumulated)
            | _ -> accumulated

    collect state.HistoryTable []

let encodeCode (terms: SymbolicAbstraction.Term list) (store: Store) : EntryIdentity * Store =
    match EvaluatorCode.encode terms store with
    | Ok value -> value
    | Error error -> invalidOp (EvaluatorCode.describeError error)

/// Records the state before `term` runs. `program` is the program the step
/// resumes, which is the term plus everything after it. P3c stores both as code
/// values in the existing seven-field step.
let record (term: SymbolicAbstraction.Term) (program: SymbolicAbstraction.Term list) (state: EvaluationState) : EntryIdentity * EvaluationState =
    let (tokenId, store1) = encodeCode [ term ] state.Store

    let (stackId, store2) =
        Store.addTensor
            (Tensor.vector (state.Stack |> List.map (fun identity -> double (EntryIdentity.value identity))))
            store1

    let (programId, store3) = encodeCode program store2

    let previousValue =
        match state.HistoryTable with
        | Some identity -> double (EntryIdentity.value identity)
        | None -> -1.0

    let step =
        Tensor.vector
            [
                HistoryStepTag
                double state.Location
                double state.Baseline
                double state.Position
                double (EntryIdentity.value tokenId)
                double (EntryIdentity.value stackId)
                double (EntryIdentity.value programId)
                previousValue
            ]

    let (historyStepId, store4) = Store.addTensor step store3

    (historyStepId,
     { state with
         Store = store4
         HistoryTable = Some historyStepId
     })

/// A decoded step: the program-counter metadata, the stack, and the program.
type private HistoryStep =
    {
        Location: int
        Baseline: int
        Position: int
        Stack: EntryIdentity list
        Program: SymbolicAbstraction.Term list
        Previous: EntryIdentity option
    }

let private decodeStep (historyStepId: EntryIdentity) (state: EvaluationState) : Result<HistoryStep, EvaluationError> =
    match Store.lookupTensor historyStepId state.Store with
    | Some tensor when
        Tensor.rank tensor = 1
        && Tensor.size tensor = 8
        && Tensor.item tensor 0 = HistoryStepTag
        ->
        let previous = int (Tensor.item tensor 7)

        Ok
            {
                Location = int (Tensor.item tensor 1)
                Baseline = int (Tensor.item tensor 2)
                Position = int (Tensor.item tensor 3)
                Stack = readVector (EntryIdentity.ofInt (int (Tensor.item tensor 5))) state.Store
                Program =
                    let programId = EntryIdentity.ofInt (int (Tensor.item tensor 6))

                    match EvaluatorCode.decode programId state.Store with
                    | Ok terms -> terms
                    | Error error -> invalidOp (EvaluatorCode.describeError error)
                Previous =
                    if previous < 0 then
                        None
                    else
                        Some(EntryIdentity.ofInt previous)
            }
    | _ -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value historyStepId), ExpectedStep))

/// Rolls back to a recorded step: restores the stack, program, and
/// program-counter metadata, and re-points the history root at the steps
/// before it. Later steps stay in the store but are unreachable.
let rollbackToStep (historyStepId: EntryIdentity) (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let steps = stepIds state

    match steps |> List.tryFindIndex ((=) historyStepId) with
    | None -> Error(InvalidHistoryStep(-1, List.length steps))
    | Some index ->
        match decodeStep historyStepId state with
        | Error error -> Error error
        | Ok step ->
            Ok
                { state with
                    HistoryTable = step.Previous
                    Stack = step.Stack
                    Program = step.Program
                    Location = step.Location
                    Baseline = step.Baseline
                    Position = step.Position
                    // A step records the stack and program, not the lexical call
                    // frames (D45). Rollback returns to the global frame rather
                    // than restoring a frame the step did not capture.
                    CurrentFrame = None
                    CallFrames = []
                    MethodFrames = []
                    Handlers = []
                }

/// Rolls back to the step at `index` (0-based, oldest first).
let rollbackTo (index: int) (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let steps = stepIds state

    if index < 0 || index >= List.length steps then
        Error(InvalidHistoryStep(index, List.length steps))
    else
        rollbackToStep steps.[index] state

/// `history` pushes the steps recorded before the current token. The current
/// token's own step is the last one; it is excluded, so the word reports the
/// history it was asked about rather than including the question.
let historyWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let all = stepIds state

    // The `history` token is recorded before it runs when recording is on, so
    // the newest step is the question itself and is excluded. When recording is
    // off, there is no question step; every recorded step is reported.
    let steps =
        if state.RecordHistory then
            match List.rev all with
            | [] -> []
            | _ :: before -> List.rev before
        else
            all

    let (identity, store) =
        Store.addTensor
            (Tensor.vector (
                steps
                |> List.map (fun historyStepId -> double (EntryIdentity.value historyStepId))
            ))
            state.Store

    Ok
        { state with
            Store = store
            Stack = Stack.push identity state.Stack
        }

/// `rollback` pops a step index and restores that step. A program that rolls
/// back and re-runs the same suffix is a loop by construction: the word is the
/// interactive surface, and `rollbackTo` is the mechanism a debugger calls.
let rollbackWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "rollback" state with
    | Error error -> Error error
    | Ok(tensor, _) ->
        match scalarValue "rollback" tensor with
        | Error error -> Error error
        | Ok rawIndex -> rollbackTo (int rawIndex) state
