module EvaluatorScheduler

// ============================================================
// Scheduler words: spawn, yield, halt, actors, resume, kill.
// W5 — cooperative multitasking with actor supervision and error recovery.
// ============================================================

open Types
open EvaluatorCommon
open EvaluatorChannel
open Scheduler

/// Pops a quotation from the stack and spawns a new actor to run it.
/// The new actor is added to the current actor's supervision tree.
/// Stack: quotation -> handle
let spawnWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("spawn", 1, Stack.depth state.Stack))
    | Some(quotationId, remainingStack) ->
        match spawnActor quotationId state with
        | Error error -> Error error
        | Ok(handle, newState) ->
            // The handle names the child without revealing its address (D64).
            Ok
                { newState with
                    Stack = Stack.push handle remainingStack
                }

/// Yields control to the next ready actor and resets the current actor's budget.
/// With no other ready actor, the current actor continues with a fresh budget.
/// Stack: ->
let yieldWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> = Ok(yieldControl state)

/// Halts the current actor. A halted actor is retained for `resume` or `kill`;
/// control passes to the next ready actor (or the program stops).
/// Stack: ->
let haltWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> = Ok(haltCurrent None state)

/// Pushes one status code per actor, in actor order. No address is exposed:
/// targeting an actor requires the private handle from `spawn`/`supervise`
/// (D64). Stack: -> statusTensor
let actorsWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let data =
        listActors state.SchedulerState
        |> List.map (fun (_, status) -> Scheduler.actorStatusCode status)

    let tensor = Tensor.vector data
    let (identity, newStore) = Store.addTensor tensor state.Store

    Ok
        { state with
            Store = newStore
            Stack = Stack.push identity state.Stack
        }

/// Resumes a halted actor by its private handle (from the stack top). A plain
/// number is not a handle: only an entry registered by `spawn`/`supervise`
/// names an actor (D64). Stack: handle ->
let resumeWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("resume", 1, Stack.depth state.Stack))
    | Some(handle, remainingStack) ->
        match resolveActorHandle handle state.SchedulerState with
        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value handle), ExpectedActorHandle))
        | Some actorId ->
            let newSched = resumeActor actorId state.SchedulerState

            Ok
                { state with
                    SchedulerState = newSched
                    Stack = remainingStack
                }

/// Kills an actor and its descendants by private handle (from the stack top).
/// Stack: handle ->
let killWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("kill", 1, Stack.depth state.Stack))
    | Some(handle, remainingStack) ->
        match resolveActorHandle handle state.SchedulerState with
        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value handle), ExpectedActorHandle))
        | Some actorId ->
            if actorId = state.SchedulerState.SelfId then
                Error(ActorError(ActorFault.CannotKillSelf state.SchedulerState.SelfId))
            else
                let store, sched =
                    Scheduler.killActorAndNotify actorId state.Store state.SchedulerState

                let nextState =
                    { state with
                        Store = store
                        SchedulerState = sched
                        Stack = remainingStack
                    }

                Ok
                    { nextState with
                        Store = Reclaim.compactClosedEpochs nextState
                    }
// ---- D65 actor model --------------------------------------------------------

/// Pushes the current actor's private handle. There is no address on the stack.
/// Stack: -> handle
let selfWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let current = Scheduler.getCurrentActor state.SchedulerState

    match current.Handle with
    | Some handle ->
        Ok
            { state with
                Stack = Stack.push handle state.Stack
            }
    | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value current.Identity), ExpectedActorHandle))

/// Sends a message to an actor's private mailbox or records a remote transport
/// request. Stack: message handle ->
let sendToWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("send-to", 2, Stack.depth state.Stack))
    | Some(handle, afterHandle) ->
        match Scheduler.remoteHandleParts handle state.Store state.SchedulerState with
        | Some(instance, capability) ->
            match Stack.popIndex afterHandle with
            | None -> Error(StackUnderflow("send-to", 2, Stack.depth state.Stack))
            | Some(messageId, rest) ->
                let request =
                    {
                        Instance = instance
                        Capability = capability
                        Message = messageId
                        Sender = state.SchedulerState.SelfId
                    }

                Ok
                    { state with
                        SchedulerState = Scheduler.enqueueTransport request state.SchedulerState
                        Stack = rest
                    }
        | None ->
            match resolveActorHandle handle state.SchedulerState with
            | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value handle), ExpectedActorHandle))
            | Some targetId ->
                let target = state.SchedulerState.Actors.[targetId]

                match target.Mailbox with
                | None -> Error(ActorError(ActorFault.NoMailbox (EntryIdentity.value targetId)))
                | Some mailbox ->
                    // `send` expects the message on top and the channel below it.
                    match Stack.popIndex afterHandle with
                    | None -> Error(StackUnderflow("send-to", 2, Stack.depth state.Stack))
                    | Some(messageId, rest) ->
                        sendWord
                            { state with
                                Stack = Stack.push messageId (Stack.push mailbox rest)
                            }

/// Receives the oldest message from the current actor's mailbox, blocking on
/// an empty mailbox like `receive`. Stack: -> message
let receiveMessageWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let current = Scheduler.getCurrentActor state.SchedulerState

    match current.Mailbox with
    | None -> Error(ActorError(ActorFault.NoMailbox (EntryIdentity.value current.Identity)))
    | Some mailbox ->
        receiveWord
            { state with
                Stack = Stack.push mailbox state.Stack
            }

/// Links the current actor to the actor named by a handle. Stack: handle ->
let linkWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("link", 1, Stack.depth state.Stack))
    | Some(handle, remaining) ->
        match resolveActorHandle handle state.SchedulerState with
        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value handle), ExpectedActorHandle))
        | Some targetId ->
            Ok
                { state with
                    SchedulerState = linkActors state.SchedulerState.SelfId targetId state.SchedulerState
                    Stack = remaining
                }

/// Removes a link to the actor named by a handle. Stack: handle ->
let unlinkWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("unlink", 1, Stack.depth state.Stack))
    | Some(handle, remaining) ->
        match resolveActorHandle handle state.SchedulerState with
        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value handle), ExpectedActorHandle))
        | Some targetId ->
            Ok
                { state with
                    SchedulerState = unlinkActors state.SchedulerState.SelfId targetId state.SchedulerState
                    Stack = remaining
                }

// ---- D67 monitors and the named registry ------------------------------------

/// Creates a one-way monitor on the actor named by a handle.
/// Stack: handle -> monitorRef
let monitorWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("monitor", 1, Stack.depth state.Stack))
    | Some(handle, remaining) ->
        match resolveActorHandle handle state.SchedulerState with
        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value handle), ExpectedActorHandle))
        | Some targetId ->
            let reference, sched =
                monitorActor state.SchedulerState.SelfId targetId state.SchedulerState

            let (identity, store) =
                Store.addTensor (Tensor.scalar (float reference)) state.Store

            Ok
                { state with
                    Store = store
                    SchedulerState = sched
                    Stack = Stack.push identity remaining
                }

/// Removes a monitor owned by the current actor. Stack: monitorRef ->
let demonitorWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "demonitor" state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        match scalarValue "demonitor" tensor with
        | Error error -> Error error
        | Ok reference ->
            match demonitorActor state.SchedulerState.SelfId (int reference) state.SchedulerState with
            | Error error -> Error error
            | Ok sched ->
                Ok
                    { state with
                        SchedulerState = sched
                        Stack = remaining
                    }

/// Pushes 1 when the value is a monitor Down message, 0 otherwise.
let downWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "down?" state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        let flag =
            if Tensor.size tensor > 0 && Tensor.item tensor 0 = DownTag then
                1.0
            else
                0.0

        let (identity, store) = Store.addTensor (Tensor.scalar flag) state.Store

        Ok
            { state with
                Store = store
                Stack = Stack.push identity remaining
            }

/// Pushes the monitor reference carried by a Down message, or -1 otherwise.
let downRefWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "down-ref" state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        let reference =
            if Tensor.size tensor > 1 && Tensor.item tensor 0 = DownTag then
                Tensor.item tensor 1
            else
                -1.0

        let (identity, store) = Store.addTensor (Tensor.scalar reference) state.Store

        Ok
            { state with
                Store = store
                Stack = Stack.push identity remaining
            }

/// Pushes the reason code carried by a Down message, or -1 otherwise.
let downReasonWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "down-reason" state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        let code =
            if Tensor.size tensor > 2 && Tensor.item tensor 0 = DownTag then
                Tensor.item tensor 2
            else
                -1.0

        let (identity, store) = Store.addTensor (Tensor.scalar code) state.Store

        Ok
            { state with
                Store = store
                Stack = Stack.push identity remaining
            }

let private popName (word: string) (state: EvaluationState) : Result<string * EntryIdentity list, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow(word, 1, Stack.depth state.Stack))
    | Some(identity, remaining) ->
        match Store.readTextAt identity state.Store with
        | Some name -> Ok(name, remaining)
        | None -> Error(TypeMismatch(ErrorWord word, ExpectedText))

/// Registers the actor named by a handle under a text name.
/// Stack: name handle ->
let registerWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popName "register" state with
    | Error error -> Error error
    | Ok(name, afterName) ->
        match Stack.popIndex afterName with
        | None -> Error(StackUnderflow("register", 2, Stack.depth state.Stack))
        | Some(handle, remaining) ->
            match resolveActorHandle handle state.SchedulerState with
            | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value handle), ExpectedActorHandle))
            | Some actorId ->
                match registerName name actorId state.SchedulerState with
                | Error error -> Error error
                | Ok sched ->
                    Ok
                        { state with
                            SchedulerState = sched
                            Stack = remaining
                        }

/// Removes a name from the registry. Stack: name ->
let unregisterWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popName "unregister" state with
    | Error error -> Error error
    | Ok(name, remaining) ->
        match unregisterName name state.SchedulerState with
        | Error error -> Error error
        | Ok sched ->
            Ok
                { state with
                    SchedulerState = sched
                    Stack = remaining
                }

/// Pushes the handle registered under a name. Stack: name -> handle
let whereisWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popName "whereis" state with
    | Error error -> Error error
    | Ok(name, remaining) ->
        match whereisName name state.SchedulerState with
        | None -> Error(ActorError(ActorFault.UnknownRegisteredName name))
        | Some actorId ->
            match state.SchedulerState.Actors.TryFind actorId with
            | Some actor ->
                match actor.Handle with
                | Some handle ->
                    Ok
                        { state with
                            Stack = Stack.push handle remaining
                        }
                | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value actorId), ExpectedActorHandle))
            | None -> Error(ActorError(ActorFault.UnknownRegisteredName name))

/// Receives the first mailbox message matching a predicate quotation.
/// Stack: predicate -> message
/// If nothing matches, the actor blocks and rescans when a send wakes it.
let receiveMatchingWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("receive-matching", 1, Stack.depth state.Stack))
    | Some(predicateId, afterPredicate) ->
        match EvaluatorCode.decode predicateId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "receive-matching", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            let current = Scheduler.getCurrentActor state.SchedulerState

            match current.Mailbox with
            | None -> Error(ActorError(ActorFault.NoMailbox (EntryIdentity.value current.Identity)))
            | Some mailbox ->
                let isOpen, queued = ChannelStore.readChannelState mailbox state.Store

                if not isOpen then
                    Error(ChannelError(mailbox, ChannelFault.ChannelClosed))
                else
                    let rec scan (before: EntryIdentity list) (remaining: EntryIdentity list) =
                        match remaining with
                        | [] -> Ok None
                        | messageId :: rest ->
                            match EvaluatorQuotationRun.run executeWord predicateId messageId state with
                            | Error reason -> Error(SortError(SortFault.Undecided reason))
                            | Ok tensor ->
                                if Tensor.size tensor > 0 && Tensor.item tensor 0 <> 0.0 then
                                    Ok(Some(messageId, List.rev before @ rest))
                                else
                                    scan (messageId :: before) rest

                    match scan [] queued with
                    | Error error -> Error error
                    | Ok(Some(messageId, rest)) ->
                        let store = ChannelStore.appendChannelState true rest mailbox state.Store

                        let sched =
                            Scheduler.unregisterSelectiveWaiter mailbox current.Identity state.SchedulerState

                        Ok
                            { state with
                                Store = store
                                SchedulerState = sched
                                Stack = Stack.push messageId afterPredicate
                            }
                    | Ok None ->
                        let sched =
                            Scheduler.registerSelectiveWaiter mailbox current.Identity state.SchedulerState

                        let waiting =
                            { state with
                                SchedulerState = sched
                                Stack = Stack.push predicateId afterPredicate
                                Program = SymbolicAbstraction.Word "receive-matching" :: state.Program
                            }

                        Ok(
                            Scheduler.haltCurrent
                                (Some(ChannelError(mailbox, ChannelFault.ReceiveFailed ("waiting for a matching message"))))
                                waiting
                        )

/// Toggles whether the current actor traps link exits. Stack: flag ->
let trapExitWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "trap-exit" state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        match scalarValue "trap-exit" tensor with
        | Error error -> Error error
        | Ok value ->
            let sched = state.SchedulerState
            let current = Scheduler.getCurrentActor sched

            Ok
                { state with
                    SchedulerState =
                        Scheduler.updateActor
                            { current with
                                TrappingExits = value <> 0.0
                            }
                            sched
                    Stack = remaining
                }

/// Pushes 1 when the value is a trapped exit signal, 0 otherwise.
/// Stack: value -> flag
let exitSignalWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "exit-signal?" state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        let flag =
            if Tensor.size tensor > 0 && Tensor.item tensor 0 = ExitSignalTag then
                1.0
            else
                0.0

        let (identity, store) = Store.addTensor (Tensor.scalar flag) state.Store

        Ok
            { state with
                Store = store
                Stack = Stack.push identity remaining
            }

/// Pushes the exit reason code of a trapped exit signal, or -1 for any other
/// value. Stack: value -> code
let exitReasonWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "exit-reason" state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        let code =
            if Tensor.size tensor > 1 && Tensor.item tensor 0 = ExitSignalTag then
                Tensor.item tensor 1
            else
                -1.0

        let (identity, store) = Store.addTensor (Tensor.scalar code) state.Store

        Ok
            { state with
                Store = store
                Stack = Stack.push identity remaining
            }

// ---- D63 supervision --------------------------------------------------------

/// Pops one non-negative integer limit. A fractional or negative value is a
/// named refusal, not a silent truncation.
let private popLimit (word: string) (state: EvaluationState) : Result<int * EvaluationState, EvaluationError> =
    match popResolved word state with
    | Error error -> Error error
    | Ok(tensor, remaining) ->
        match scalarValue word tensor with
        | Error error -> Error error
        | Ok value ->
            let limit = int value

            if value < 0.0 || value <> System.Math.Floor value then
                Error(LimitError(LimitFault.NegativeSupervisionLimit(word, limit)))
            else
                Ok(limit, { state with Stack = remaining })

/// A strategy word installs the current actor's supervision policy.
/// Stack: maxRestarts withinSteps deadline ->
let private strategyWord
    (word: string)
    (strategy: SupervisionStrategy)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match popLimit word state with
    | Error error -> Error error
    | Ok(deadline, afterDeadline) ->
        match popLimit word afterDeadline with
        | Error error -> Error error
        | Ok(withinSteps, afterWithin) ->
            match popLimit word afterWithin with
            | Error error -> Error error
            | Ok(maxRestarts, remaining) ->
                let policy =
                    {
                        Strategy = strategy
                        Limits =
                            {
                                MaxRestarts = maxRestarts
                                WithinSteps = withinSteps
                                Deadline = deadline
                            }
                    }

                let sched = remaining.SchedulerState
                let current = Scheduler.getCurrentActor sched

                let current' =
                    { current with
                        Policy = Some policy
                        Restarts = 0
                        RestartWindowStart = 0
                    }

                Ok
                    { remaining with
                        SchedulerState = Scheduler.updateActor current' sched
                    }

let oneForOneWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    strategyWord "one-for-one" OneForOne state

let oneForAllWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    strategyWord "one-for-all" OneForAll state

let restForOneWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    strategyWord "rest-for-one" RestForOne state

let private parseRestartType (kind: string) : Result<RestartType, EvaluationError> =
    match kind with
    | "permanent" -> Ok Permanent
    | "transient" -> Ok Transient
    | "temporary" -> Ok Temporary
    | other -> Error(EnumError(EnumFault.InvalidRestartType other))

/// `quotation "permanent" supervise` spawns a supervised child under the current
/// actor's policy. Stack: quotation restartTypeText -> handle
let superviseWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("supervise", 2, Stack.depth state.Stack))
    | Some(kindId, afterKind) ->
        match Store.readTextAt kindId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "supervise", ExpectedText))
        | Some kind ->
            match Stack.popIndex afterKind with
            | None -> Error(StackUnderflow("supervise", 2, Stack.depth state.Stack))
            | Some(quotationId, remaining) ->
                match EvaluatorCode.decode quotationId state.Store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "supervise", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok _ ->
                    match parseRestartType kind with
                    | Error error -> Error error
                    | Ok restartType ->
                        let parent = Scheduler.getCurrentActor state.SchedulerState

                        match parent.Policy with
                        | None -> Error(NoSupervisionPolicy "supervise")
                        | Some policy ->
                            let specification =
                                {
                                    Quotation = quotationId
                                    RestartType = restartType
                                }

                            let childState = { state with Stack = remaining }

                            match Scheduler.superviseActor specification policy.Limits.Deadline childState with
                            | Error error -> Error error
                            | Ok(handle, newState) ->
                                // The handle names the child without revealing
                                // its address (D64).
                                Ok
                                    { newState with
                                        Stack = Stack.push handle remaining
                                    }
