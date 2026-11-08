module LanguageTransport

open Types
open ActorHost

/// Sends every recorded request and delivers each reply to its sender's
/// mailbox. The remote reply is a scalar in this slice.
let private sendAll
    (path: string)
    (state: EvaluationState)
    (results: (double * string * int) list)
    (requests: TransportRequest list)
    : Result<EvaluationState * (double * string * int) list, HostError> =
    let rec loop
        (state: EvaluationState)
        (results: (double * string * int) list)
        (requests: TransportRequest list)
        : Result<EvaluationState * (double * string * int) list, HostError> =
        match requests with
        | [] -> Ok(state, results)
        | request :: rest ->
            match Store.lookupTensor request.Message state.Store with
            | None -> Error(MessageFailure "the transport message is not in the store")
            | Some tensor ->
                match ScalarRead.readScalar tensor with
                | Error reason -> Error(MessageFailure reason)
                | Ok value ->
                    match connectSupervisedWith path (uint64 request.Capability) value with
                    | Error error -> Error error
                    | Ok(reply, down, monitor) ->
                        let (replyId, store) = Store.addTensor (Tensor.scalar reply) state.Store

                        match state.SchedulerState.Actors.TryFind request.Sender with
                        | None -> Error(MessageFailure "the sending actor is gone")
                        | Some sender ->
                            match sender.Mailbox with
                            | None -> Error(MessageFailure "the sending actor has no mailbox")
                            | Some mailbox ->
                                match Scheduler.deliverMessage mailbox replyId store state.SchedulerState with
                                | Error error -> Error(ActorFailure error)
                                | Ok(store, scheduler) ->
                                    loop
                                        { state with
                                            Store = store
                                            SchedulerState = scheduler
                                        }
                                        ((reply, down, monitor) :: results)
                                        rest

    loop state results requests

/// Drains the language's pending transport requests, sends them, delivers the
/// replies to the sender's mailbox, and resumes the sender. The first slice
/// allows one sender per drain and a numeric reply.
let deliverPending
    (path: string)
    (state: EvaluationState)
    : Result<EvaluationState * (double * string * int) list, HostError> =
    let rec loop
        (state: EvaluationState)
        (results: (double * string * int) list)
        (steps: int)
        : Result<EvaluationState * (double * string * int) list, HostError> =
        if steps > 64 then
            Error(MessageFailure "the transport pump exceeded 64 steps")
        else
            let requests, scheduler = Scheduler.takePendingTransport state.SchedulerState
            let state = { state with SchedulerState = scheduler }

            match requests with
            | [] -> Ok(state, List.rev results)
            | _ ->
                let senders =
                    requests |> List.map (fun request -> request.Sender) |> Set.ofList

                if Set.count senders <> 1 then
                    Error(MessageFailure "one sender per transport drain")
                else
                    let senderId = Set.minElement senders

                    match sendAll path state results requests with
                    | Error error -> Error error
                    | Ok(state, results) ->
                        match Scheduler.getActorState senderId state.SchedulerState with
                        | None -> loop state results (steps + 1)
                        | Some actorState ->
                            let hostId = state.SchedulerState.SelfId

                            let runState =
                                {
                                    actorState with
                                        Store = state.Store
                                        SchedulerState = { state.SchedulerState with SelfId = senderId }
                                }

                            match Evaluator.run runState with
                            | Error diagnostic -> Error(ActorDiagnostic diagnostic)
                            | Ok final ->
                                loop
                                    { final with
                                        SchedulerState = { final.SchedulerState with SelfId = hostId }
                                    }
                                    results
                                    (steps + 1)

    loop state [] 0
