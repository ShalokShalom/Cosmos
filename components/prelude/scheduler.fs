module Scheduler

// ============================================================
// Scheduler: per-process cooperative multitasking with actor supervision.
// W5 — replaces the module-global sketch with state threaded through
// EvaluationState. Each process carries its own scheduler; no global queue.
//
// The canonical live state is the EvaluationState flowing through `run`. An
// actor's `State` field is a *suspended snapshot*: it is written when the
// actor yields, halts, or terminates, and read when the actor is switched in.
// ============================================================

open Types
open EvaluatorCommon
open Stack

/// Stable status codes used by `actors` and by the D75 status reply.
let actorStatusCode (status: ActorStatus) : RealNumber =
    match status with
    | Running -> 0.0
    | Ready -> 1.0
    | Halted _ -> 2.0
    | Terminated -> 3.0

// ---- SchedulerState helpers --------------------------------------------------

/// Gets the currently running actor from the scheduler state.
let getCurrentActor (sched: SchedulerState) : Actor = sched.Actors.[sched.SelfId]

/// Constructs the canonical root scheduler around a freshly seeded state. This
/// is the one place the actor/scheduler self-reference is built: the root
/// snapshot points back at the scheduler, and the scheduler carries the root
/// actor's private handle and mailbox. D179 stage 2 moves the scheduler out of
/// `EvaluationState`; stage 3 removes the dummy scheduler from initial
/// construction entirely.
let initializeRoot
    (selfId: EntryIdentity)
    (state: EvaluationState)
    (handle: EntryIdentity)
    (mailbox: EntryIdentity)
    (mailboxScheduler: SchedulerState)
    : SchedulerState =
    let scheduler = Types.Runtime.initializeSchedulerState selfId state
    let root = getCurrentActor scheduler

    { scheduler with
        Actors =
            scheduler.Actors.Add(
                selfId,
                { root with
                    Handle = Some handle
                    Mailbox = Some mailbox
                }
            )
        ActorHandles = scheduler.ActorHandles.Add(handle, selfId)
        Channels = mailboxScheduler.Channels
        ChannelsRoot = mailboxScheduler.ChannelsRoot
    }

/// Registers a host-issued remote capability. A tensor with the remote tag is
/// not a capability until the host registers its entry.
let registerRemoteHandle (handle: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    { sched with
        RemoteHandles = sched.RemoteHandles.Add handle
    }

/// Reads a registered remote handle's instance and capability. An unregistered
/// or malformed handle is not remote.
let remoteHandleParts
    (handle: EntryIdentity)
    (store: Store)
    (sched: SchedulerState)
    : (int * int) option =
    if not (sched.RemoteHandles.Contains handle) then
        None
    else
        match Store.lookupTensor handle store with
        | Some tensor when Tensor.size tensor >= 3 && Tensor.item tensor 0 = RemoteHandleTag ->
            Some(int (Tensor.item tensor 1), int (Tensor.item tensor 2))
        | _ -> None

/// Records a message that crosses an instance boundary. The message stays in
/// the store; the host owns the transport.
let enqueueTransport (request: TransportRequest) (sched: SchedulerState) : SchedulerState =
    { sched with
        PendingTransport = sched.PendingTransport @ [ request ]
    }

/// Takes the pending transport requests and clears the queue. The host calls
/// this after a run and owns delivery from there.
let takePendingTransport (sched: SchedulerState) : TransportRequest list * SchedulerState =
    sched.PendingTransport, { sched with PendingTransport = [] }

/// Updates an actor in the scheduler state.
let updateActor (actor: Actor) (sched: SchedulerState) : SchedulerState =
    { sched with
        Actors = sched.Actors.Add(actor.Identity, actor)
    }

/// Makes `nextId` the running actor and returns its suspended state. The
/// append-only store is shared by all actors: the live store is threaded into
/// the resumed state so entries another actor wrote are visible.
let private switchTo
    (store: Store)
    (nextId: EntryIdentity)
    (rest: EntryIdentity list)
    (sched: SchedulerState)
    : EvaluationState * SchedulerState =
    let nextActor = sched.Actors.[nextId]
    let nextActor' = { nextActor with Status = Running }

    let sched' =
        { sched with
            SelfId = nextId
            Actors = sched.Actors.Add(nextId, nextActor')
            ReadyQueue = rest
        }

    let resumed =
        { nextActor.State with
            Store = store
            SchedulerState = sched'
            Ownership =
                { nextActor.State.Ownership with
                    Epoch = Epoch.resumeAt store.NextIndex nextActor.State.Ownership.Epoch
                }
        }

    resumed, sched'

/// Adds a new actor to the supervision tree and the back of the ready queue.
let addActor (parentId: EntryIdentity) (childState: EvaluationState) (sched: SchedulerState) : Actor * SchedulerState =
    let childId = EntryIdentity.ofInt sched.NextActorId

    let childActor =
        {
            Identity = childId
            State = childState
            Status = Ready
            Suspended = false
            Parent = Some parentId
            Children = []
            Specification = None
            Exit = None
            Consumed = 0
            Handle = None
            Mailbox = None
            Links = []
            PendingExit = None
            TrappingExits = false
            Policy = None
            Restarts = 0
            RestartWindowStart = 0
        }

    let parentActor = sched.Actors.[parentId]

    let parentActor' =
        { parentActor with
            Children = childId :: parentActor.Children
        }

    let sched' =
        { sched with
            NextActorId = sched.NextActorId + 1
            Actors = sched.Actors.Add(childId, childActor).Add(parentId, parentActor')
            ReadyQueue = sched.ReadyQueue @ [ childId ]
        }

    childActor, sched'

/// Marks an actor as terminated with a named reason and removes it from the
/// queue. Termination does not cascade to children: a parent may spawn a child
/// and finish while the child keeps running. `killActor` is the cascading one.
let private terminateActorWith (reason: ExitReason) (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    match sched.Actors.TryFind actorId with
    | None -> sched
    | Some actor ->
        let closedEpoch =
            Epoch.closeEpoch actor.State.Ownership.Epoch.Current actor.State.Ownership.Epoch

        let actor' =
            { actor with
                Status = Terminated
                State =
                    { actor.State with
                        Ownership =
                            { actor.State.Ownership with
                                Epoch = closedEpoch
                            }
                    }
                Suspended = false
                Exit = Some reason
                Links = []
                PendingExit = None
            }

        let sched' = updateActor actor' sched

        // Links are per incarnation: no live actor keeps a link to a dead one.
        let schedLinked =
            { sched' with
                Actors =
                    sched'.Actors
                    |> Map.map (fun _ other ->
                        if other.Links |> List.contains actorId then
                            { other with
                                Links = other.Links |> List.filter ((<>) actorId)
                            }
                        else
                            other)
            }

        let sched'' =
            { schedLinked with
                ReadyQueue = List.filter ((<>) actorId) schedLinked.ReadyQueue
                EventWaiters = Map.remove actorId schedLinked.EventWaiters
                // A terminated actor's handles are retired: a stale or restarted
                // actor must never be reachable through an old capability (D64).
                ActorHandles = schedLinked.ActorHandles |> Map.filter (fun _ owner -> owner <> actorId)
                Registry = schedLinked.Registry |> Map.filter (fun _ owner -> owner <> actorId)
                SelectiveWaiters =
                    schedLinked.SelectiveWaiters
                    |> Map.map (fun _ waiters -> waiters |> List.filter ((<>) actorId))
                    |> Map.filter (fun _ waiters -> not waiters.IsEmpty)

                // A terminated actor cannot receive: it leaves every channel's
                // waiter list, so a later send cannot hand it a message that
                // nobody would ever collect.
                Channels =
                    schedLinked.Channels
                    |> Map.map (fun _ channel ->
                        { channel with
                            ReceiveWaiters = List.filter ((<>) actorId) channel.ReceiveWaiters
                        })
            }

        match actor.Parent with
        | Some parentId when sched''.Actors.ContainsKey parentId ->
            let parentActor = sched''.Actors.[parentId]

            let parentActor' =
                { parentActor with
                    Children = List.filter ((<>) actorId) parentActor.Children
                }

            updateActor parentActor' sched''
        | _ -> sched''

/// Registers an actor waiting for an event to become ready. The event ID is
/// stored so a wake can restore the retry without inspecting the store.
let registerEventWaiter (actorId: EntryIdentity) (eventId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    { sched with
        EventWaiters = Map.add actorId eventId sched.EventWaiters
    }

/// Resumes a halted actor, moving it to the back of the ready queue. A halted
/// actor's stored error becomes a pending error, so a blocked receive that is
/// resumed without a message reports why instead of silently continuing. A
/// registered event waiter is restored to `eventId sync` so the event is
/// evaluated again instead of continuing after the failed sync.
let private resumeWith
    (pendingError: EvaluationError option)
    (actorId: EntryIdentity)
    (sched: SchedulerState)
    : SchedulerState =
    match sched.Actors.TryFind actorId with
    | Some actor ->
        match actor.Status with
        | Halted _ ->
            let eventId = sched.EventWaiters.TryFind actorId

            let actorState =
                match eventId with
                | Some eventId ->
                    { actor.State with
                        PendingError = pendingError
                        Stack = Stack.push eventId actor.State.Stack
                        Program = SymbolicAbstraction.Word "sync" :: actor.State.Program
                    }
                | None ->
                    { actor.State with
                        PendingError = pendingError
                    }

            let actor' =
                { actor with
                    Status = Ready
                    State = actorState
                }

            let sched' = updateActor actor' sched

            { sched' with
                ReadyQueue =
                    if actor.Suspended then
                        sched'.ReadyQueue
                    else
                        sched'.ReadyQueue @ [ actorId ]
                EventWaiters = Map.remove actorId sched'.EventWaiters
            }
        | _ -> sched
    | None -> sched

let resumeActor (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    match sched.Actors.TryFind actorId with
    | Some actor ->
        match actor.Status, actor.Suspended with
        | Halted error, false -> resumeWith error actorId sched
        | _ -> sched
    | None -> sched

/// Wakes every blocked sync. A send or close can make an event ready, so the
/// waiter retries and either commits or registers again. Waking all waiters is
/// conservative but cannot lose a wakeup; a retry that is still blocked simply
/// halts again.
let wakeEventWaiters (sched: SchedulerState) : SchedulerState =
    sched.EventWaiters
    |> Map.toList
    |> List.fold (fun current (actorId, _) -> resumeWith None actorId current) sched

/// Resumes a halted actor because a message was delivered. The receive that
/// blocked it is satisfied, so any channel-wait error is cleared.
let resumeActorForDelivery (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    resumeWith None actorId sched

/// Resumes a halted actor with a failure, used by `close` for waiting receivers.
let resumeActorWithError (error: EvaluationError) (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    resumeWith (Some error) actorId sched

/// Appends an actor to the ready queue once.
let private enqueueReady (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    if List.contains actorId sched.ReadyQueue then
        sched
    else
        { sched with
            ReadyQueue = sched.ReadyQueue @ [ actorId ]
        }

/// Suspends an actor's user execution. Its status and state are retained; it
/// leaves the ready queue and cannot be resumed by the user-level `resume`
/// word. The runtime `Resume` system message is the only way back.
let suspendActor (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    match sched.Actors.TryFind actorId with
    | Some actor when actor.Status <> Terminated && not actor.Suspended ->
        let sched' = updateActor { actor with Suspended = true } sched

        { sched' with
            ReadyQueue = List.filter ((<>) actorId) sched'.ReadyQueue
        }
    | _ -> sched

/// Resumes a suspended actor. A ready actor is queued again; a blocked actor
/// wakes only when its mailbox has a queued message.
let resumeSuspendedActor (actorId: EntryIdentity) (store: Store) (sched: SchedulerState) : SchedulerState =
    match sched.Actors.TryFind actorId with
    | Some actor when actor.Suspended && actor.Status <> Terminated ->
        let cleared = { actor with Suspended = false }

        let sched' = updateActor cleared sched

        match cleared.Status with
        | Ready -> enqueueReady actorId sched'
        | Running -> sched'
        | Halted _ ->
            let queued =
                match cleared.Mailbox with
                | Some mailbox -> snd (ChannelStore.readChannelState mailbox store)
                | None -> []

            if queued.IsEmpty then
                sched'
            else
                resumeWith None actorId sched'
        | Terminated -> sched'
    | _ -> sched

/// Terminates an actor and all its descendants with one reason.
let private terminateSubtree (reason: ExitReason) (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    let rec loop (identities: EntryIdentity list) (s: SchedulerState) : SchedulerState =
        match identities with
        | [] -> s
        | identity :: rest ->
            match s.Actors.TryFind identity with
            | Some actor -> loop (actor.Children @ rest) (terminateActorWith reason identity s)
            | None -> loop rest s

    loop [ actorId ] sched

/// Kills an actor and all its descendants. A kill is explicit, so the exit
/// reason is `KilledExit` and an explicit kill never triggers a restart.
let killActor (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    terminateSubtree KilledExit actorId sched

/// Yields control: snapshots the live state as the current actor's suspended
/// state, then runs the actor at the front of the ready queue. With no other
/// ready actor, the current actor keeps the CPU with a fresh budget.
let yieldControl (state: EvaluationState) : EvaluationState =
    let sched = state.SchedulerState
    let currentId = sched.SelfId
    let currentActor = getCurrentActor sched

    let suspended =
        { state with
            Reductions = DefaultReductions
            Ownership =
                { state.Ownership with
                    Epoch = Epoch.recordAllocation state.Store.NextIndex state.Ownership.Epoch
                }
        }

    let suspended =
        { suspended with Store = Reclaim.compactClosedEpochs suspended }

    let currentActor' =
        { currentActor with
            Status = Ready
            State = suspended
            Consumed = suspended.Consumed
        }

    let sched' = updateActor currentActor' sched

    // The current actor goes to the back; the front gets the CPU. A suspended
    // actor stays out of the queue until the runtime resumes it.
    let queue =
        if currentActor.Suspended then
            sched'.ReadyQueue
        else
            sched'.ReadyQueue @ [ currentId ]

    match queue with
    | nextId :: rest ->
        let nextState, _ = switchTo suspended.Store nextId rest sched'
        nextState
    | [] ->
        { suspended with
            SchedulerState = sched'
        }

/// Halts the current actor and runs the next ready actor. The halted actor's
/// live state — including the program after `halt` — is retained for `resume`.
/// With no ready actor, the program stops and the actor stays halted.
let haltCurrent (error: EvaluationError option) (state: EvaluationState) : EvaluationState =
    let sched = state.SchedulerState
    let currentId = sched.SelfId
    let currentActor = getCurrentActor sched

    let haltedState =
        { state with
            Reductions = DefaultReductions
            Ownership =
                { state.Ownership with
                    Epoch = Epoch.recordAllocation state.Store.NextIndex state.Ownership.Epoch
                }
        }

    let haltedState =
        { haltedState with Store = Reclaim.compactClosedEpochs haltedState }

    let sched' =
        updateActor
            { currentActor with
                State = haltedState
                Status = Halted error
                Consumed = state.Consumed
            }
            sched

    match sched'.ReadyQueue with
    | nextId :: rest ->
        let nextState, _ = switchTo haltedState.Store nextId rest sched'
        nextState
    | [] ->
        { haltedState with
            SchedulerState = sched'
            Program = []
        }

/// A fresh child's live state: empty stack, no ownership, the quotation as the
/// program, and the supervisor's token deadline when it set one (D63).
let private freshChildState
    (parentState: EvaluationState)
    (deadline: int)
    (quotationTerms: SymbolicAbstraction.Term list)
    (childId: EntryIdentity)
    : EvaluationState =
    { parentState with
        Stack = Stack.empty
        // A spawned actor starts owning nothing: ownership never transfers
        // implicitly (W9/D30). Its incarnation epoch starts now.
        Ownership =
            { emptyOwnership with
                Epoch =
                    Epoch.beginAt
                        (EntryIdentity.value childId + 1)
                        parentState.Store.NextIndex
                        emptyOwnership.Epoch
            }
        Program = quotationTerms
        Reductions = DefaultReductions
        Consumed = 0
        Deadline = deadline
        Location = 0
        Position = 1
        Output = []
        PendingError = None
        // A child starts with no command status of its own; the adapter is
        // inherited because the host is a process-wide effect port (D183).
        LastCommandStatus = None
        // A spawned actor is a new process: it starts at the global frame. Pass
        // a closure to share lexical state (D45).
        CurrentFrame = None
        CallFrames = []
        MethodFrames = []
        Handlers = []
    }

/// Creates a private mailbox: an ordinary channel identity whose id is held
/// only by the actor. The mailbox is never pushed to a program; `send-to` and
/// `receive-message` resolve it from the actor (D65).
let createMailbox (store: Store) (sched: SchedulerState) : EntryIdentity * Store * SchedulerState =
    let (mailboxId, store1) = Store.addTensor ChannelStore.channelIdentityTensor store

    let (rootId, store2) =
        match sched.ChannelsRoot with
        | Some rootId ->
            match Store.lookupTensor rootId store1 with
            | Some vector ->
                Store.addTensor (Tensor.concat vector (Tensor.vector [ double (EntryIdentity.value mailboxId) ])) store1
            | None -> Store.addTensor (Tensor.vector [ double (EntryIdentity.value mailboxId) ]) store1
        | None -> Store.addTensor (Tensor.vector [ double (EntryIdentity.value mailboxId) ]) store1

    let sched' =
        { sched with
            ChannelsRoot = Some rootId
            Channels =
                sched.Channels.Add(
                    mailboxId,
                    {
                        Identity = mailboxId
                        ReceiveWaiters = []
                    }
                )
        }

    mailboxId, store2, sched'

/// Adds a child to a parent at the back of the ready queue. The parent is named
/// explicitly because a restart adds to a supervisor that is not the current
/// actor. Returns the child's private handle and the parent's threaded state.
/// The actor's address is never returned to the program (D64).
let private addChild
    (parentId: EntryIdentity)
    (parentState: EvaluationState)
    (specification: ActorSpec option)
    (deadline: int)
    (quotationTerms: SymbolicAbstraction.Term list)
    : EntryIdentity * EvaluationState =
    let sched0 = parentState.SchedulerState
    let childId = EntryIdentity.ofInt sched0.NextActorId

    let (handleId, store0) =
        Store.addTensor (Tensor.vector [ ActorHandleTag ]) parentState.Store

    let (mailboxId, store1, schedWithMailbox) = createMailbox store0 sched0
    let parentState = { parentState with Store = store1 }
    let childState0 = freshChildState parentState deadline quotationTerms childId

    let child =
        {
            Identity = childId
            State = childState0
            Status = Ready
            Suspended = false
            Parent = Some parentId
            Children = []
            Specification = specification
            Exit = None
            Consumed = 0
            Handle = Some handleId
            Mailbox = Some mailboxId
            Links = []
            PendingExit = None
            TrappingExits = false
            Policy = None
            Restarts = 0
            RestartWindowStart = 0
        }

    let parentActor = schedWithMailbox.Actors.[parentId]

    let parentActor' =
        { parentActor with
            State =
                { parentState with
                    SchedulerState = sched0
                }
            Children = childId :: parentActor.Children
        }

    let sched1 =
        { schedWithMailbox with
            NextActorId = schedWithMailbox.NextActorId + 1
            Actors = schedWithMailbox.Actors.Add(childId, child).Add(parentId, parentActor')
            ReadyQueue = schedWithMailbox.ReadyQueue @ [ childId ]
            ActorHandles = schedWithMailbox.ActorHandles.Add(handleId, childId)
        }

    // Re-thread: the child's snapshot and the parent's live state must both
    // point at the scheduler that already contains them.
    let child' =
        { child with
            State =
                { childState0 with
                    SchedulerState = sched1
                }
        }

    let parentActor'' =
        { parentActor' with
            State =
                { parentState with
                    SchedulerState = sched1
                }
        }

    let sched2 =
        { sched1 with
            Actors = sched1.Actors.Add(childId, child').Add(parentId, parentActor'')
        }

    handleId,
    { parentState with
        SchedulerState = sched2
    }

/// Resolves a private handle to the actor it names. `None` is a forged,
/// retired, or foreign handle, which `kill`/`resume` refuse (D64).
let resolveActorHandle (handle: EntryIdentity) (sched: SchedulerState) : EntryIdentity option =
    sched.ActorHandles.TryFind handle

/// Finds the actor whose private mailbox is this channel.
let private actorForMailbox (mailbox: EntryIdentity) (sched: SchedulerState) : (EntryIdentity * Actor) option =
    sched.Actors
    |> Map.tryPick (fun identity actor ->
        if actor.Mailbox = Some mailbox then
            Some(identity, actor)
        else
            None)

/// Is this tensor a D75 reserved system message?
let private isSystemMessage (tensor: Tensor) : bool =
    Tensor.rank tensor = 1
    && Tensor.size tensor >= 2
    && Tensor.item tensor 0 = SystemMessageTag

/// Delivers a message to a channel or mailbox: directly to the oldest live
/// waiter, else appended to the queue. The store is threaded because the queue
/// is image state (D27). Used by `send` and by link/monitor signals (D66).
/// A D75 system message is handled by the runtime before user delivery.
let rec deliverMessage
    (channelId: EntryIdentity)
    (messageId: EntryIdentity)
    (store: Store)
    (sched: SchedulerState)
    : Result<Store * SchedulerState, EvaluationError> =
    match Store.lookupTensor messageId store with
    | Some message when isSystemMessage message ->
        match actorForMailbox channelId sched with
        | Some _ -> handleSystemMessage channelId message store sched
        | None -> Error(InvalidSystemMessage "system message target is not an actor mailbox")
    | _ -> deliverMessageCore channelId messageId store sched

and deliverMessageCore
    (channelId: EntryIdentity)
    (messageId: EntryIdentity)
    (store: Store)
    (sched: SchedulerState)
    : Result<Store * SchedulerState, EvaluationError> =
    let channel =
        match sched.Channels.TryFind channelId with
        | Some channel -> Ok channel
        | None ->
            match Store.lookupTensor channelId store with
            | Some tensor when ChannelStore.isChannelIdentity tensor ->
                Ok
                    {
                        Identity = channelId
                        ReceiveWaiters = []
                    }
            | _ -> Error(ChannelError(channelId, ChannelFault.ChannelNotFound))

    match channel with
    | Error error -> Error error
    | Ok channel ->
        let isOpen, queued = ChannelStore.readChannelState channelId store

        if not isOpen then
            Error(ChannelError(channelId, ChannelFault.ChannelClosed))
        else
            let waiting =
                channel.ReceiveWaiters
                |> List.filter (fun waiterId ->
                    match sched.Actors.TryFind waiterId with
                    | Some actor -> actor.Status <> Terminated
                    | None -> false)

            match waiting with
            | receiverId :: restReceivers ->
                match sched.Actors.TryFind receiverId with
                | None -> Error(ChannelError(channelId, ChannelFault.SendFailed ("waiting receiver is no longer registered")))
                | Some receiver ->
                    let newReceiverState =
                        { receiver.State with
                            Stack = Stack.push messageId receiver.State.Stack
                            PendingError = None
                        }

                    let sched1 =
                        updateActor
                            { receiver with
                                State = newReceiverState
                            }
                            sched

                    let sched2 = resumeActorForDelivery receiverId sched1

                    let sched3 =
                        { sched2 with
                            Channels =
                                Map.add
                                    channelId
                                    { channel with
                                        ReceiveWaiters = restReceivers
                                    }
                                    sched2.Channels
                        }

                    let sched4 = wakeEventWaiters sched3
                    Ok(store, sched4)
            | [] ->
                let store' = ChannelStore.appendChannelState true (queued @ [ messageId ]) channelId store

                let sched' =
                    { sched with
                        Channels =
                            Map.add
                                channelId
                                { channel with
                                    ReceiveWaiters = waiting
                                }
                                sched.Channels
                    }
                    |> wakeEventWaiters

                // A selective receiver is not a direct waiter: it is woken to
                // rescan the queue, and the message stays queued (D68).
                let sched'' =
                    match sched'.SelectiveWaiters.TryFind channelId with
                    | Some waiters ->
                        waiters
                        |> List.fold (fun current waiter -> resumeActorForDelivery waiter current) sched'
                    | None -> sched'

                Ok(store', sched'')

/// Handles the D75 reserved system-message class. This runs before any user
/// callback or mailbox queue insertion; the message is never visible to
/// `receive-message`.
and handleSystemMessage
    (mailbox: EntryIdentity)
    (message: Tensor)
    (store: Store)
    (sched: SchedulerState)
    : Result<Store * SchedulerState, EvaluationError> =
    match actorForMailbox mailbox sched with
    | None -> Error(InvalidSystemMessage "system message target is not an actor mailbox")
    | Some(actorId, actor) ->
        let operation = int (Tensor.item message 1)

        if operation = int SystemSuspendOperation then
            if actorId = sched.SelfId then
                Error(InvalidSystemMessage "suspend cannot target the running actor")
            else
                Ok(store, suspendActor actorId sched)
        elif operation = int SystemResumeOperation then
            if actorId = sched.SelfId then
                Error(InvalidSystemMessage "resume cannot target the running actor")
            else
                Ok(store, resumeSuspendedActor actorId store sched)
        elif operation = int SystemCodeChangeOperation then
            if actorId = sched.SelfId then
                Error(InvalidSystemMessage "code-change cannot target the running actor")
            elif not actor.Suspended then
                Error(InvalidSystemMessage "code-change requires a suspended actor")
            elif Tensor.size message < 3 then
                Error(InvalidSystemMessage "code-change requires a quotation id")
            else
                let quotationId = EntryIdentity.ofInt (int (Tensor.item message 2))

                match EvaluatorCode.decode quotationId store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "code-change", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok quotationTerms ->
                    let actor' =
                        { actor with
                            Status = Ready
                            State =
                                { actor.State with
                                    Program = quotationTerms
                                    Location = 0
                                    Position = 1
                                    PendingError = None
                                }
                        }

                    Ok(store, updateActor actor' sched)
        elif operation = int SystemStatusOperation then
            let replyChannel =
                if Tensor.size message >= 3 then
                    let value = int (Tensor.item message 2)

                    if value < 0 then None else Some(EntryIdentity.ofInt value)
                else
                    None

            match replyChannel with
            | None -> Ok(store, sched)
            | Some channel ->
                let queued =
                    match actor.Mailbox with
                    | Some mailbox -> snd (ChannelStore.readChannelState mailbox store)
                    | None -> []

                let report =
                    Tensor.vector
                        [
                            SystemStatusTag
                            actorStatusCode actor.Status
                            (if actor.Suspended then 1.0 else 0.0)
                            double queued.Length
                        ]

                let (replyId, store') = Store.addTensor report store
                deliverMessage channel replyId store' sched
        elif operation = int SystemShutdownOperation then
            if actorId = sched.SelfId then
                Error(InvalidSystemMessage "shutdown cannot target the running actor")
            else
                Ok(shutdownActorAndNotify actorId store sched)
        else
            Error(InvalidSystemMessage("unknown system operation " + string operation))

// A shutdown is not a kill: it keeps the named `ShutdownExit`, terminates the
// target subtree, and still fires the target's links and monitors.
and shutdownActorAndNotify (actorId: EntryIdentity) (store: Store) (sched: SchedulerState) : Store * SchedulerState =
    let rec collect (identities: EntryIdentity list) (accumulated: EntryIdentity list) =
        match identities with
        | [] -> List.rev accumulated
        | identity :: rest ->
            match sched.Actors.TryFind identity with
            | Some actor -> collect (actor.Children @ rest) (identity :: accumulated)
            | None -> collect rest accumulated

    let identities = collect [ actorId ] []

    let links =
        identities
        |> List.map (fun identity ->
            match sched.Actors.TryFind identity with
            | Some actor -> (identity, actor.Links)
            | None -> (identity, []))

    let sched' = terminateSubtree ShutdownExit actorId sched

    links
    |> List.fold
        (fun (currentStore, current) (identity, linked) ->
            let storeAfterLinks, schedAfterLinks =
                propagateLinks ShutdownExit linked currentStore current

            notifyMonitors identity ShutdownExit storeAfterLinks schedAfterLinks)
        (store, sched')

// Fires every monitor targeting the exiting actor: a `DownTag` message carrying
// the reference and reason goes to each live watcher, and the monitor is
// consumed (D67).
and notifyMonitors
    (target: EntryIdentity)
    (reason: ExitReason)
    (store: Store)
    (sched: SchedulerState)
    : Store * SchedulerState =
    let matching =
        sched.Monitors
        |> Map.toList
        |> List.filter (fun (_, monitor) -> monitor.Target = target)

    let schedWithout =
        matching
        |> List.fold
            (fun current (reference, _) ->
                { current with
                    Monitors = Map.remove reference current.Monitors
                })
            sched

    matching
    |> List.fold
        (fun (currentStore, current) (reference, monitor) ->
            match current.Actors.TryFind monitor.Watcher with
            | Some watcher when watcher.Status <> Terminated ->
                match watcher.Mailbox with
                | Some mailbox ->
                    let (messageId, store') =
                        Store.addTensor
                            (Tensor.vector [ DownTag; double reference; exitReasonCode reason ])
                            currentStore

                    match deliverMessage mailbox messageId store' current with
                    | Ok delivered -> delivered
                    | Error _ -> store', current
                | None -> currentStore, current
            | _ -> currentStore, current)
        (store, schedWithout)

// Delivers an abnormal exit to every live linked actor. A non-trapping actor
// gets a pending exit and is made ready; a trapping actor gets an
// `ExitSignalTag` message in its mailbox instead (D66).
and propagateLinks
    (reason: ExitReason)
    (links: EntryIdentity list)
    (store: Store)
    (sched: SchedulerState)
    : Store * SchedulerState =
    let deliver (identity: EntryIdentity) (currentStore: Store, current: SchedulerState) =
        match current.Actors.TryFind identity with
        | Some actor when actor.Status <> Terminated ->
            if actor.TrappingExits then
                match actor.Mailbox with
                | Some mailbox ->
                    let (messageId, store') =
                        Store.addTensor (Tensor.vector [ ExitSignalTag; exitReasonCode reason ]) currentStore

                    match deliverMessage mailbox messageId store' current with
                    | Ok delivered -> delivered
                    | Error _ ->
                        let sched' =
                            updateActor
                                { actor with
                                    Status = Ready
                                    PendingExit = Some reason
                                }
                                current

                        store', sched'
                | None ->
                    let sched' =
                        updateActor
                            { actor with
                                Status = Ready
                                PendingExit = Some reason
                            }
                            current

                    currentStore, sched'
            else
                let sched' =
                    updateActor
                        { actor with
                            Status = Ready
                            PendingExit = Some reason
                        }
                        current

                let sched'' =
                    if actor.Suspended || List.contains identity sched'.ReadyQueue then
                        sched'
                    else
                        { sched' with
                            ReadyQueue = sched'.ReadyQueue @ [ identity ]
                        }

                currentStore, sched''
        | _ -> currentStore, current

    links
    |> List.fold (fun accumulated identity -> deliver identity accumulated) (store, sched)

/// Registers an actor as a selective receiver on a mailbox. A send wakes it
/// without delivering the message, so it rescans the queue (D68).
let registerSelectiveWaiter (mailbox: EntryIdentity) (actorId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    let waiters = sched.SelectiveWaiters.TryFind mailbox |> Option.defaultValue []

    { sched with
        SelectiveWaiters = sched.SelectiveWaiters.Add(mailbox, waiters @ [ actorId ])
    }

/// Removes an actor's selective-receive registration (D68).
let unregisterSelectiveWaiter
    (mailbox: EntryIdentity)
    (actorId: EntryIdentity)
    (sched: SchedulerState)
    : SchedulerState =
    match sched.SelectiveWaiters.TryFind mailbox with
    | None -> sched
    | Some waiters ->
        let remaining = waiters |> List.filter ((<>) actorId)

        if remaining.IsEmpty then
            { sched with
                SelectiveWaiters = Map.remove mailbox sched.SelectiveWaiters
            }
        else
            { sched with
                SelectiveWaiters = sched.SelectiveWaiters.Add(mailbox, remaining)
            }

/// Creates a one-way monitor and returns its reference number (D67).
let monitorActor (watcher: EntryIdentity) (target: EntryIdentity) (sched: SchedulerState) : int * SchedulerState =
    let reference = sched.NextMonitorId

    (reference,
     { sched with
         NextMonitorId = reference + 1
         Monitors = sched.Monitors.Add(reference, { Watcher = watcher; Target = target })
     })

/// Removes a monitor the current actor owns. A forged or foreign reference is
/// a named refusal.
let demonitorActor
    (watcher: EntryIdentity)
    (reference: int)
    (sched: SchedulerState)
    : Result<SchedulerState, EvaluationError> =
    match sched.Monitors.TryFind reference with
    | Some monitor when monitor.Watcher = watcher ->
        Ok
            { sched with
                Monitors = Map.remove reference sched.Monitors
            }
    | _ -> Error(TypeMismatch(ErrorAddress reference, ExpectedMonitor))

/// Kills an actor and its descendants, firing the monitors of every terminated
/// actor (D68). A plain `killActor` cannot notify because it has no store.
let killActorAndNotify (actorId: EntryIdentity) (store: Store) (sched: SchedulerState) : Store * SchedulerState =
    let rec collect (identities: EntryIdentity list) (accumulated: EntryIdentity list) =
        match identities with
        | [] -> List.rev accumulated
        | identity :: rest ->
            match sched.Actors.TryFind identity with
            | Some actor -> collect (actor.Children @ rest) (identity :: accumulated)
            | None -> collect rest accumulated

    let identities = collect [ actorId ] []

    let sched' = killActor actorId sched

    identities
    |> List.fold
        (fun (currentStore, current) identity -> notifyMonitors identity KilledExit currentStore current)
        (store, sched')

/// Registers a name for an actor. A live actor already holding the name is
/// `NameInUse`; a dead holder's name is reusable (D67).
let registerName
    (name: string)
    (actorId: EntryIdentity)
    (sched: SchedulerState)
    : Result<SchedulerState, EvaluationError> =
    match sched.Registry.TryFind name with
    | Some existing when existing <> actorId ->
        match sched.Actors.TryFind existing with
        | Some actor when actor.Status <> Terminated -> Error(ActorError(ActorFault.NameInUse name))
        | _ ->
            Ok
                { sched with
                    Registry = sched.Registry.Add(name, actorId)
                }
    | _ ->
        Ok
            { sched with
                Registry = sched.Registry.Add(name, actorId)
            }

let unregisterName (name: string) (sched: SchedulerState) : Result<SchedulerState, EvaluationError> =
    if sched.Registry.ContainsKey name then
        Ok
            { sched with
                Registry = Map.remove name sched.Registry
            }
    else
        Error(ActorError(ActorFault.UnknownRegisteredName name))

let whereisName (name: string) (sched: SchedulerState) : EntryIdentity option = sched.Registry.TryFind name

/// Links two actors in both directions. A self-link is a no-op, and a repeated
/// link is idempotent (D65).
let linkActors (firstId: EntryIdentity) (secondId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    if firstId = secondId then
        sched
    else
        let linkOne actorId otherId state =
            match state.Actors.TryFind actorId with
            | Some actor when not (List.contains otherId actor.Links) ->
                updateActor
                    { actor with
                        Links = otherId :: actor.Links
                    }
                    state
            | _ -> state

        sched |> linkOne firstId secondId |> linkOne secondId firstId

/// Removes a link in both directions. Unlinking a missing link is a no-op.
let unlinkActors (firstId: EntryIdentity) (secondId: EntryIdentity) (sched: SchedulerState) : SchedulerState =
    let unlinkOne actorId otherId state =
        match state.Actors.TryFind actorId with
        | Some actor ->
            updateActor
                { actor with
                    Links = actor.Links |> List.filter ((<>) otherId)
                }
                state
        | None -> state

    sched |> unlinkOne firstId secondId |> unlinkOne secondId firstId

/// Spawns a new unsupervised actor from a quotation (W5). The child is Ready;
/// it runs when the parent next yields, exhausts its budget, or finishes.
let spawnActor
    (quotationId: EntryIdentity)
    (state: EvaluationState)
    : Result<EntryIdentity * EvaluationState, EvaluationError> =
    match EvaluatorCode.decode quotationId state.Store with
    | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "spawn", ExpectedQuotation))
    | Error error -> Error(CodeError(EvaluatorCode.describeError error))
    | Ok quotationTerms -> Ok(addChild state.SchedulerState.SelfId state None 0 quotationTerms)

/// Spawns a supervised child. The current actor is its supervisor, and its
/// policy supplies the strategy, restart budget, and token deadline (D63).
let superviseActor
    (specification: ActorSpec)
    (deadline: int)
    (state: EvaluationState)
    : Result<EntryIdentity * EvaluationState, EvaluationError> =
    match EvaluatorCode.decode specification.Quotation state.Store with
    | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "supervise", ExpectedQuotation))
    | Error error -> Error(CodeError(EvaluatorCode.describeError error))
    | Ok quotationTerms -> Ok(addChild state.SchedulerState.SelfId state (Some specification) deadline quotationTerms)

/// A restart is allowed by the child's restart type and the way it stopped.
let restartAllowed (restartType: RestartType) (reason: ExitReason) : bool =
    match restartType, reason with
    | Temporary, _ -> false
    | Permanent, _ -> true
    | Transient, (NormalExit | ShutdownExit) -> false
    | Transient, _ -> true

/// Admits one restart against a supervisor's budget. A fixed window resets once
/// `WithinSteps` tokens have passed; `WithinSteps = 0` means no time window.
let private admitRestart
    (parent: Actor)
    (policy: SupervisionPolicy)
    (sched: SchedulerState)
    : Result<Actor * SchedulerState, EvaluationError> =
    let limits = policy.Limits

    let windowElapsed =
        limits.WithinSteps > 0
        && parent.Restarts > 0
        && parent.Consumed - parent.RestartWindowStart > limits.WithinSteps

    let restarts, windowStart =
        if windowElapsed then
            0, parent.Consumed
        else
            parent.Restarts,
            (if parent.Restarts = 0 then
                 parent.Consumed
             else
                 parent.RestartWindowStart)

    if restarts >= limits.MaxRestarts then
        Error(LimitError(LimitFault.SupervisionExceeded((supervisionStrategyName policy.Strategy), restarts)))
    else
        let parent' =
            { parent with
                Restarts = restarts + 1
                RestartWindowStart = windowStart
            }

        Ok(parent', updateActor parent' sched)

/// Shuts down the affected siblings and starts fresh actors for the failed child
/// and every sibling whose restart type is Permanent. One restart admission
/// covers the whole group.
let private restartGroup
    (failedId: EntryIdentity)
    (failedSpecification: ActorSpec)
    (siblings: EntryIdentity list)
    (store: Store)
    (sched: SchedulerState)
    : Result<SchedulerState, EvaluationError> =
    let failed = sched.Actors.[failedId]

    let parentId =
        match failed.Parent with
        | Some parentId -> parentId
        | None -> failedId

    let parent = sched.Actors.[parentId]

    let deadline =
        match parent.Policy with
        | Some policy -> policy.Limits.Deadline
        | None -> 0

    // The failed child is already terminated; shut down the affected siblings.
    let sched0 =
        siblings
        |> List.filter ((<>) failedId)
        |> List.fold (fun current identity -> terminateActorWith ShutdownExit identity current) sched

    let candidates =
        (failedId, failedSpecification)
        :: (siblings
            |> List.choose (fun identity ->
                if identity = failedId then
                    None
                else
                    sched0.Actors.TryFind identity
                    |> Option.bind (fun actor ->
                        actor.Specification
                        |> Option.filter (fun specification -> specification.RestartType = Permanent)
                        |> Option.map (fun specification -> (identity, specification)))))

    candidates
    |> List.fold
        (fun result (_, specification) ->
            result
            |> Result.bind (fun (current, parentState) ->
                match EvaluatorCode.decode specification.Quotation store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "supervise", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok quotationTerms ->
                    let _, parentState' =
                        addChild parentId parentState (Some specification) deadline quotationTerms

                    Ok(parentState'.SchedulerState, parentState')))
        (Ok(
            sched0,
            { parent.State with
                Store = store
                SchedulerState = sched0
            }
        ))
    |> Result.map fst

/// Switches to the next ready actor. With none, the run is over.
let private continueAfter (store: Store) (sched: SchedulerState) (fallback: EvaluationState) : EvaluationState =
    match sched.ReadyQueue with
    | nextId :: rest ->
        let nextState, _ = switchTo store nextId rest sched

        nextState
    | [] ->
        { fallback with
            Store = store
            SchedulerState = sched
            Program = []
        }

/// An abnormal exit is signalled to links; a normal one is not (D65).
let private abnormalExit (reason: ExitReason) : bool =
    match reason with
    | ErrorExit _ -> true
    | TimeoutExit -> true
    | KilledExit -> true
    | NormalExit -> false
    | ShutdownExit -> false

/// Records the current actor's exit, lets its supervisor decide, and runs the
/// next ready actor. An abnormal root exit is delivered as `PendingError`.
let exitCurrent (reason: ExitReason) (state: EvaluationState) : EvaluationState =
    let sched0 = state.SchedulerState
    let currentId = sched0.SelfId
    let current = getCurrentActor sched0

    let sched1 =
        updateActor
            { current with
                State = state
                Consumed = state.Consumed
                Exit = Some reason
                PendingExit = None
            }
            sched0

    let linked = if abnormalExit reason then current.Links else []

    // The strategy decides which children the exit affects. Read it before the
    // failed child leaves its parent's child list.
    let siblings =
        match current.Parent, current.Specification with
        | Some parentId, Some specification when restartAllowed specification.RestartType reason ->
            let parent = sched1.Actors.[parentId]

            match parent.Policy with
            | None -> []
            | Some policy ->
                let children = parent.Children

                match policy.Strategy with
                | OneForOne -> [ currentId ]
                | OneForAll -> children
                | RestForOne -> currentId :: List.takeWhile ((<>) currentId) children
        | _ -> []

    let sched2 = terminateActorWith reason currentId sched1
    let store2, sched2 = propagateLinks reason linked state.Store sched2
    let store2, sched2 = notifyMonitors currentId reason store2 sched2

    match current.Parent with
    | None ->
        match reason with
        | ErrorExit error ->
            { state with
                Store = store2
                SchedulerState = sched2
                Program = []
                PendingError = Some error
            }
        | _ when sched2.ReadyQueue.IsEmpty ->
            { state with
                Store = store2
                SchedulerState = sched2
                Program = []
            }
        | _ -> continueAfter store2 sched2 state
    | Some parentId ->
        match current.Specification with
        | Some specification when restartAllowed specification.RestartType reason ->
            let parent = sched2.Actors.[parentId]

            match parent.Policy with
            | None -> continueAfter store2 sched2 state
            | Some policy ->
                match admitRestart parent policy sched2 with
                | Error error ->
                    // The supervisor's budget is spent. The supervisor exits with
                    // the limit error, but its own supervisor decides what that
                    // exit becomes (D68); the root still surfaces it.
                    let supervisor = sched2.Actors.[parentId]

                    let sched3 =
                        supervisor.Children
                        |> List.fold
                            (fun current child ->
                                if current.Actors.ContainsKey child then
                                    terminateActorWith ShutdownExit child current
                                else
                                    current)
                            sched2

                    let currentSupervisor = sched3.Actors.[parentId]

                    let sched4 =
                        updateActor
                            { currentSupervisor with
                                Status = Ready
                                PendingExit = Some(ErrorExit error)
                            }
                            sched3

                    let sched5 =
                        if List.contains parentId sched4.ReadyQueue then
                            sched4
                        else
                            { sched4 with
                                ReadyQueue = sched4.ReadyQueue @ [ parentId ]
                            }

                    continueAfter store2 sched5 state
                | Ok(_, sched3) ->
                    match restartGroup currentId specification siblings store2 sched3 with
                    | Ok sched4 -> continueAfter store2 sched4 state
                    | Error error ->
                        { state with
                            Store = store2
                            SchedulerState = sched3
                            Program = []
                            PendingError = Some error
                        }
        | _ -> continueAfter store2 sched2 state

/// Ends the current incarnation at its token deadline (D63).
let timeoutCurrent (state: EvaluationState) : EvaluationState = exitCurrent TimeoutExit state

/// True when the current actor is a `supervise`d child. A state whose `SelfId`
/// has no actor record (some tests construct one to simulate a foreign owner)
/// is not supervised, so the old abort-on-error behaviour is preserved.
let isSupervised (sched: SchedulerState) : bool =
    match sched.Actors.TryFind sched.SelfId with
    | Some actor -> actor.Specification.IsSome
    | None -> false

/// Returns a list of all actors with their status for inspection.
let listActors (sched: SchedulerState) : (EntryIdentity * ActorStatus) list =
    sched.Actors
    |> Map.toList
    |> List.map (fun (identity, actor) -> (identity, actor.Status))

/// Gets an actor's state for inspection.
let getActorState (actorId: EntryIdentity) (sched: SchedulerState) : EvaluationState option =
    sched.Actors.TryFind actorId |> Option.map (fun actor -> actor.State)
