module EvaluatorQuotationRun

// ============================================================
// A quotation evaluated in a quotationRun state: the input on a fresh stack, no
// output, a reduction budget, and the actor words refused (D31, D35). The
// quotationRun's store changes are discarded; only the top value is returned. A
// scheduler word, an exhausted budget, a non-quotation, or an evaluation error
// is a named reason, never a silent false: membership and guard verdicts both
// build on this one quotationRun.
// ============================================================

open Types
open EvaluatorCommon

/// Words that change or block the actor. One list serves every quotation run
/// (set membership, guards, dispatch, selective receive) and the set literal
/// (D69). Read-only actor words (`self`, `whereis`, `down?`, `exit-signal?`)
/// stay available; event constructors stay available until `sync` commits.
let actorWords =
    [
        "spawn"
        "yield"
        "halt"
        "resume"
        "kill"
        "actors"
        "receive"
        "receive-message"
        "receive-matching"
        "sync"
        "send"
        "send-to"
        "close"
        "link"
        "unlink"
        "trap-exit"
        "monitor"
        "demonitor"
        "register"
        "unregister"
    ]

/// Runs a quotation with `inputId` on the stack and returns the top value of
/// the final stack.
let run
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (quotationId: EntryIdentity)
    (inputId: EntryIdentity)
    (state: EvaluationState)
    : Result<Tensor, string> =
    match EvaluatorCode.decode quotationId state.Store with
    | Error(EvaluatorCode.NotCode _) -> Error "the predicate is not a quotation"
    | Error error -> Error(EvaluatorCode.describeError error)
    | Ok program ->
        match
            program
            |> List.tryPick (fun term ->
                match term with
                | SymbolicAbstraction.Word name when List.contains name actorWords -> Some name
                | _ -> None)
        with
        | Some token ->
            Error(
                "the predicate names the word '"
                + token
                + "', which changes or blocks the actor"
            )
        | None ->
            let quotationRun =
                { state with
                    Program = program
                    Stack = Stack.push inputId Stack.empty
                    Output = []
                    PendingError = None
                    Reductions = DefaultReductions
                    // A probe is isolated: it does not see the caller's lexical frame.
                    CurrentFrame = None
                    CallFrames = []
                    MethodFrames = []
                    Handlers = []
                }

            let rec go (current: EvaluationState) : Result<EvaluationState, string> =
                match current.Program with
                | [] -> Ok current
                | term :: remaining ->
                    if current.Reductions <= 0 then
                        Error "the predicate exceeded its reduction budget"
                    else
                        let beforeToken =
                            { current with
                                Program = remaining
                                Reductions = current.Reductions - 1
                                Location = current.Location + 1
                                Position = current.Position + 1
                            }

                        match EvaluatorProgram.executeTerm executeWord term beforeToken with
                        | Error error -> Error(describeError error)
                        | Ok afterToken -> go afterToken

            match go quotationRun with
            | Error reason -> Error reason
            | Ok final ->
                match Stack.popResolved final.Store final.Stack with
                | Some(tensor, _) -> Ok tensor
                | None -> Error "the predicate left no verdict on the stack"
