module EvaluatorChannel

// ============================================================
// Channel words: channel, send, receive, close.
// W6 / D20 — channels as store-resident communication endpoints.
//
// D27: a channel's *state* is store-resident and append-only.
//
// The identity entry ([ChannelTag; marker]) is written once and never
// touched. The state — whether the channel is open, and the messages queued
// on it — is a separate entry appended on every change
// ([ChannelStateTag; channelNumber; isOpen; messageId…]). The newest state
// entry for a channel is the one with the highest address, so the store stays
// append-only (no entry is ever rewritten) and a persisted store carries the
// queue with it.
//
// The host keeps only the *waiters*: the actors blocked on the channel. Who
// is waiting is control state, like the ready queue, so it stays host-side.
// ============================================================

open Types
open EvaluatorCommon
open Scheduler

// ---- Store encoding --------------------------------------------------------

/// The channel's identity entry. It names the channel and nothing else.
let private identityTensor: Tensor = ChannelStore.channelIdentityTensor

let isChannelIdentity (tensor: Tensor) : bool = ChannelStore.isChannelIdentity tensor

let readState (channelId: EntryIdentity) (store: Store) : bool * EntryIdentity list =
    ChannelStore.readChannelState channelId store

let private appendState = ChannelStore.appendChannelState

// ---- Words -----------------------------------------------------------------

/// Pops a name from the stack and creates a new channel with that name.
/// The channel is created as an store entry and added to the scheduler's channel root.
/// Stack: name -> channelId
let channelWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popResolved "channel" state with
    | Error error -> Error error
    | Ok(nameTensor, remainingStack) ->
        // Create a new channel entry in the store and add it to the channel root.
        let (channelId, store1) = Store.addTensor identityTensor state.Store

        let (newChannelsRoot, store2) =
            match state.SchedulerState.ChannelsRoot with
            | Some rootId ->
                match Store.lookupTensor rootId store1 with
                | Some vector ->
                    Store.addTensor
                        (Tensor.concat vector (Tensor.vector [ double (EntryIdentity.value channelId) ]))
                        store1
                | None -> Store.addTensor (Tensor.vector [ double (EntryIdentity.value channelId) ]) store1
            | None -> Store.addTensor (Tensor.vector [ double (EntryIdentity.value channelId) ]) store1

        // The host-side entry holds the waiters only; the channel's state is
        // in the store.
        let newChannel =
            {
                Identity = channelId
                ReceiveWaiters = []
            }

        let sched =
            { state.SchedulerState with
                ChannelsRoot = Some newChannelsRoot
                Channels = Map.add channelId newChannel state.SchedulerState.Channels
            }

        Ok
            { state with
                Store = store2
                SchedulerState = sched
                Stack = Stack.push channelId remainingStack
            }

/// Helper to read a channel from the store: the identity entry must be there.
let private readChannel (channelId: EntryIdentity) (store: Store) : Result<EntryIdentity, EvaluationError> =
    match Store.lookupTensor channelId store with
    | Some tensor when isChannelIdentity tensor -> Ok channelId
    | _ -> Error(ChannelError(channelId, ChannelFault.ChannelNotFound))

/// The channel's host-side entry: its waiters. A channel restored from
/// persistence has no waiters yet, and its state is read from the store.
let liveChannel (channelId: EntryIdentity) (state: EvaluationState) : Result<Channel, EvaluationError> =
    match state.SchedulerState.Channels.TryFind channelId with
    | Some channel -> Ok channel
    | None ->
        readChannel channelId state.Store
        |> Result.map (fun identity ->
            {
                Identity = identity
                ReceiveWaiters = []
            })

/// Resolves a channel operand. A channel reference denotes its own address; a
/// numeric tensor denotes a channel address, which lets a spawned actor use a
/// channel identity without sharing the parent's stack.
let channelOperandId (identity: EntryIdentity) (store: Store) : EntryIdentity =
    match Store.lookupTensor identity store with
    | Some tensor when isChannelIdentity tensor -> identity
    | Some tensor when Tensor.size tensor = 1 -> EntryIdentity.ofInt (int (Tensor.item tensor 0))
    | _ -> identity

/// Resolves a message target: an actor handle denotes that actor's mailbox, a
/// channel reference or numeric channel id stays a channel (D68). Receiving and
/// closing remain channel-only; only sending is unified.
let messageTargetId (identity: EntryIdentity) (state: EvaluationState) : EntryIdentity =
    match Scheduler.resolveActorHandle identity state.SchedulerState with
    | Some actorId ->
        match state.SchedulerState.Actors.TryFind actorId with
        | Some actor -> actor.Mailbox |> Option.defaultValue identity
        | None -> identity
    | None -> channelOperandId identity state.Store

/// Pops a channel operand.
let popChannelId (word: string) (state: EvaluationState) : Result<EntryIdentity * EntryIdentity list, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow(word, 1, Stack.depth state.Stack))
    | Some(identity, remaining) -> Ok(channelOperandId identity state.Store, remaining)

/// The waiters that can still receive: a terminated actor cannot, and a
/// waiter that has been terminated must never swallow a message.
let private liveWaiters (channel: Channel) (sched: SchedulerState) : EntryIdentity list =
    channel.ReceiveWaiters
    |> List.filter (fun waiterId ->
        match sched.Actors.TryFind waiterId with
        | Some actor -> actor.Status <> Terminated
        | None -> false)

/// Pops a channel ID and a message tensor from the stack, sends the message.
/// If the channel has waiting receivers, delivers immediately; otherwise queues.
/// Stack: channelId message -> (void)
let sendWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("send", 2, Stack.depth state.Stack))
    | Some(messageId, afterMessage) ->
        match Stack.popIndex afterMessage with
        | None -> Error(StackUnderflow("send", 2, Stack.depth state.Stack))
        | Some(channelId, afterChannel) ->
            let channelId = messageTargetId channelId state

            match Scheduler.deliverMessage channelId messageId state.Store state.SchedulerState with
            | Error error -> Error error
            | Ok(store, sched) ->
                Ok
                    { state with
                        Store = store
                        SchedulerState = sched
                        Stack = afterChannel
                        Ownership =
                            { state.Ownership with
                                Epoch = Epoch.promoteAll [ messageId ] state.Ownership.Epoch
                            }
                    }

/// Pops a channel ID from the stack, receives a message.
/// If the channel has queued messages, dequeues one; otherwise blocks the actor.
/// Stack: channelId -> message
let receiveWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popChannelId "receive" state with
    | Error error -> Error error
    | Ok(channelId, remainingStack) ->
        match liveChannel channelId state with
        | Error error -> Error error
        | Ok channel ->
            let isOpen, queued = readState channelId state.Store

            if not isOpen then
                Error(ChannelError(channelId, ChannelFault.ChannelClosed))
            else
                match queued with
                | messageId :: restMessages ->
                    // The queue shrinks by one, which is a new state entry.
                    let newStore = appendState true restMessages channelId state.Store

                    let sched =
                        { state.SchedulerState with
                            Channels = Map.add channelId channel state.SchedulerState.Channels
                        }

                    Ok
                        { state with
                            Store = newStore
                            SchedulerState = sched
                            Stack = Stack.push messageId remainingStack
                        }
                | [] ->
                    let currentId = state.SchedulerState.SelfId

                    let newChannel =
                        { channel with
                            ReceiveWaiters = channel.ReceiveWaiters @ [ currentId ]
                        }

                    let sched =
                        { state.SchedulerState with
                            Channels = Map.add channelId newChannel state.SchedulerState.Channels
                        }

                    // Halt the current actor with a channel wait indication.
                    // `haltCurrent` may switch to the next ready actor; the
                    // waiting actor's stack is already in the state it stores.
                    let waitingState =
                        { state with
                            SchedulerState = sched
                            Stack = remainingStack
                        }

                    Ok(
                        Scheduler.haltCurrent
                            (Some(ChannelError(channelId, ChannelFault.ReceiveFailed ("waiting for message"))))
                            waitingState
                    )

/// Pops a channel ID from the stack, closes the channel.
/// Any waiting receivers are resumed with `ChannelClosed`.
/// Stack: channelId -> (void)
let closeWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popChannelId "close" state with
    | Error error -> Error error
    | Ok(channelId, remainingStack) ->
        match liveChannel channelId state with
        | Error error -> Error error
        | Ok channel ->
            let isOpen, queued = readState channelId state.Store

            if not isOpen then
                Error(ChannelError(channelId, ChannelFault.ChannelClosed))
            else
                // Closing appends a state entry; the identity entry and the
                // earlier state entries are never rewritten.
                let newStore = appendState false queued channelId state.Store

                let closedChannel =
                    {
                        Identity = channelId
                        ReceiveWaiters = []
                    }

                let sched1 =
                    { state.SchedulerState with
                        Channels = Map.add channelId closedChannel state.SchedulerState.Channels
                    }

                let unblockWaiters (waiters: EntryIdentity list) (sched: SchedulerState) : SchedulerState =
                    waiters
                    |> List.fold
                        (fun s waiterId -> Scheduler.resumeActorWithError (ChannelError(channelId, ChannelFault.ChannelClosed)) waiterId s)
                        sched

                let sched2 = unblockWaiters channel.ReceiveWaiters sched1
                let sched3 = Scheduler.wakeEventWaiters sched2

                Ok
                    { state with
                        Store = newStore
                        SchedulerState = sched3
                        Stack = remainingStack
                    }
