module EvaluatorChannelEvent

// ============================================================
// ChannelEvents-style first-class events (W7).
//
// An event is an store-resident tensor. `sync` selects the first ready branch
// of a choice, commits the channel operations that branch names, and then runs
// the wrappers of the chosen branch. A branch that would block is not an
// error: the actor halts and retries the same event when a channel changes.
//
// Words: send-event, receive-event, choose, wrap, guard, with-fallback, sync.
// ============================================================

open Types
open EvaluatorCommon
open Scheduler
open EvaluatorChannel
open EvaluatorValueSets

/// The event description carried by an store entry.
type ChannelEvent =
    | NeverChannelEvent
    | ChannelSendEvent of channelId: EntryIdentity * messageId: EntryIdentity
    | ChannelReceiveEvent of channelId: EntryIdentity
    | ChannelChooseEvent of eventIds: EntryIdentity list
    | ChannelWrapEvent of eventId: EntryIdentity * quotationId: EntryIdentity
    | ChannelGuardEvent of eventId: EntryIdentity * quotationId: EntryIdentity
    | ChannelWithFallbackEvent of mainEventId: EntryIdentity * fallbackEventId: EntryIdentity

/// One channel operation a synchronized event commits.
type ChannelEventAction =
    | SendAction of channelId: EntryIdentity * messageId: EntryIdentity
    | ReceiveAction of channelId: EntryIdentity

/// The operations and wrappers that a ready event contributes.
type ChannelEventSelection =
    {
        Actions: ChannelEventAction list
        Wrappers: EntryIdentity list
    }

/// The result of asking whether an event can be committed.
type ChannelEventReadiness =
    | ReadyToCommit of ChannelEventSelection
    | WouldBlock
    | CannotCommit of EvaluationError

let private emptySelection: ChannelEventSelection = { Actions = []; Wrappers = [] }

// ---- Store encoding --------------------------------------------------------

let encodeEvent (event: ChannelEvent) : Tensor =
    let channelEventKind =
        match event with
        | NeverChannelEvent -> 0.0
        | ChannelSendEvent _ -> 1.0
        | ChannelReceiveEvent _ -> 2.0
        | ChannelChooseEvent _ -> 3.0
        | ChannelWrapEvent _ -> 4.0
        | ChannelGuardEvent _ -> 5.0
        | ChannelWithFallbackEvent _ -> 6.0

    let reference (eventId: EntryIdentity) : RealNumber = double (EntryIdentity.value eventId)

    let body =
        match event with
        | NeverChannelEvent -> []
        | ChannelSendEvent(channelId, messageId) -> [ reference channelId; reference messageId ]
        | ChannelReceiveEvent channelId -> [ reference channelId ]
        | ChannelChooseEvent eventIds -> eventIds |> List.map reference
        | ChannelWrapEvent(eventId, quotationId) -> [ reference eventId; reference quotationId ]
        | ChannelGuardEvent(eventId, quotationId) -> [ reference eventId; reference quotationId ]
        | ChannelWithFallbackEvent(mainEventId, fallbackEventId) -> [ reference mainEventId; reference fallbackEventId ]

    Tensor.vector (ChannelEventTag :: channelEventKind :: body)

let decodeEvent (tensor: Tensor) : ChannelEvent option =
    let referenceAt index =
        EntryIdentity.ofInt (int (Tensor.item tensor index))

    if
        Tensor.rank tensor <> 1
        || Tensor.size tensor < 2
        || Tensor.item tensor 0 <> ChannelEventTag
    then
        None
    else
        match int (Tensor.item tensor 1) with
        | 0 -> Some NeverChannelEvent
        | 1 when Tensor.size tensor >= 4 -> Some(ChannelSendEvent(referenceAt 2, referenceAt 3))
        | 2 when Tensor.size tensor >= 3 -> Some(ChannelReceiveEvent(referenceAt 2))
        | 3 when Tensor.size tensor >= 3 ->
            Some(
                ChannelChooseEvent(
                    tensor.Data
                    |> Array.skip 2
                    |> Array.toList
                    |> List.map (fun value -> EntryIdentity.ofInt (int value))
                )
            )
        | 4 when Tensor.size tensor >= 4 -> Some(ChannelWrapEvent(referenceAt 2, referenceAt 3))
        | 5 when Tensor.size tensor >= 4 -> Some(ChannelGuardEvent(referenceAt 2, referenceAt 3))
        | 6 when Tensor.size tensor >= 4 -> Some(ChannelWithFallbackEvent(referenceAt 2, referenceAt 3))
        | _ -> None

/// Resolves an event operand. A numeric tensor denotes an event address, which
/// lets a spawned actor name an event without sharing the parent's stack.
let eventOperandId (eventId: EntryIdentity) (store: Store) : EntryIdentity =
    match Store.lookupTensor eventId store with
    | Some tensor when
        Tensor.rank tensor = 1
        && Tensor.size tensor >= 2
        && Tensor.item tensor 0 = ChannelEventTag
        ->
        eventId
    | Some tensor when Tensor.size tensor = 1 -> EntryIdentity.ofInt (int (Tensor.item tensor 0))
    | _ -> eventId

let readEvent (word: string) (eventId: EntryIdentity) (state: EvaluationState) : Result<ChannelEvent, EvaluationError> =
    let resolvedEventId = eventOperandId eventId state.Store

    match Store.lookupTensor resolvedEventId state.Store |> Option.bind decodeEvent with
    | Some event -> Ok event
    | None -> Error(TypeMismatch(ErrorWord word, ExpectedEvent))

let popEventId (word: string) (state: EvaluationState) : Result<EntryIdentity * EntryIdentity list, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow(word, 1, Stack.depth state.Stack))
    | Some(eventId, remaining) -> Ok(eventOperandId eventId state.Store, remaining)

let writeEvent (event: ChannelEvent) (state: EvaluationState) : EntryIdentity * EvaluationState =
    let (eventId, store) = Store.addTensor (encodeEvent event) state.Store

    (eventId, { state with Store = store })

let readEventList
    (word: string)
    (listId: EntryIdentity)
    (state: EvaluationState)
    : Result<EntryIdentity list, EvaluationError> =
    match Store.lookupTensor listId state.Store with
    | None -> Error(TypeMismatch(ErrorWord word, ExpectedEventList))
    | Some tensor ->
        match decodeEvent tensor with
        | Some _ -> Ok [ eventOperandId listId state.Store ]
        | None ->
            match EvaluatorValueSets.decode tensor with
            | Some sort when sort.Predicate.IsSome -> Error(TypeMismatch(ErrorWord word, ExpectedEventList))
            | Some sort -> Ok(sort.Members |> List.map (fun eventId -> eventOperandId eventId state.Store))
            | None -> Error(TypeMismatch(ErrorWord word, ExpectedEventList))

// ---- Guard predicates ------------------------------------------------------

let private guardTokens = [ "spawn"; "yield"; "halt"; "resume"; "kill"; "actors" ]

let private guardHolds
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (word: string)
    (quotationId: EntryIdentity)
    (state: EvaluationState)
    : Result<bool, EvaluationError> =
    match EvaluatorCode.decode quotationId state.Store with
    | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord word, ExpectedQuotation))
    | Error error -> Error(CodeError(EvaluatorCode.describeError error))
    | Ok quotationTerms ->
        let quotationRun =
            { state with
                Program = quotationTerms
                Stack = []
                Output = []
                PendingError = None
                Reductions = DefaultReductions
            }

        let rec runQuotation (current: EvaluationState) : Result<EvaluationState, EvaluationError> =
            match current.Program with
            | [] -> Ok current
            | term :: remaining ->
                let namesGuardWord =
                    match term with
                    | SymbolicAbstraction.Word name -> List.contains name guardTokens
                    | _ -> false

                if namesGuardWord then
                    Error(EventGuardFailed word)
                elif current.Reductions <= 0 then
                    Error(EventGuardBudget word)
                else
                    let beforeToken =
                        { current with
                            Program = remaining
                            Reductions = current.Reductions - 1
                            Location = current.Location + 1
                            Position = current.Position + 1
                        }

                    match EvaluatorProgram.executeTerm executeWord term beforeToken with
                    | Error error -> Error error
                    | Ok afterToken -> runQuotation afterToken

        match runQuotation quotationRun with
        | Error error -> Error error
        | Ok final ->
            match Stack.popResolved final.Store final.Stack with
            | Some(valueTensor, _) ->
                match scalarValue word valueTensor with
                | Error _ -> Error(EventGuardFailed word)
                | Ok value -> Ok(value <> 0.0)
            | None -> Error(EventGuardFailed word)

// ---- Selecting a branch ----------------------------------------------------

let rec private selectEvent
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (event: ChannelEvent)
    (state: EvaluationState)
    : ChannelEventReadiness =
    match event with
    | NeverChannelEvent -> WouldBlock
    | ChannelSendEvent(channelId, messageId) ->
        match liveChannel channelId state with
        | Error error -> CannotCommit error
        | Ok _ ->
            let isOpen, _ = readState channelId state.Store

            if not isOpen then
                CannotCommit(ChannelError(channelId, ChannelFault.ChannelClosed))
            else
                ReadyToCommit
                    { emptySelection with
                        Actions = [ SendAction(channelId, messageId) ]
                    }
    | ChannelReceiveEvent channelId ->
        match liveChannel channelId state with
        | Error error -> CannotCommit error
        | Ok _ ->
            let isOpen, queued = readState channelId state.Store

            if not isOpen then
                CannotCommit(ChannelError(channelId, ChannelFault.ChannelClosed))
            elif List.isEmpty queued then
                WouldBlock
            else
                ReadyToCommit
                    { emptySelection with
                        Actions = [ ReceiveAction channelId ]
                    }
    | ChannelGuardEvent(eventId, quotationId) ->
        match guardHolds executeWord "guard" quotationId state with
        | Error error -> CannotCommit error
        | Ok false -> WouldBlock
        | Ok true ->
            match readEvent "guard" eventId state with
            | Error error -> CannotCommit error
            | Ok innerEvent -> selectEvent executeWord innerEvent state
    | ChannelWrapEvent(eventId, quotationId) ->
        match readEvent "wrap" eventId state with
        | Error error -> CannotCommit error
        | Ok innerEvent ->
            match selectEvent executeWord innerEvent state with
            | ReadyToCommit selection ->
                ReadyToCommit
                    { selection with
                        Wrappers = selection.Wrappers @ [ quotationId ]
                    }
            | other -> other
    | ChannelWithFallbackEvent(mainEventId, _) ->
        match readEvent "with-fallback" mainEventId state with
        | Error error -> CannotCommit error
        | Ok mainEvent -> selectEvent executeWord mainEvent state
    | ChannelChooseEvent eventIds -> selectBranches executeWord eventIds state

and private selectBranches
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (eventIds: EntryIdentity list)
    (state: EvaluationState)
    : ChannelEventReadiness =
    let rec findReady
        (seen: EntryIdentity list)
        (remaining: EntryIdentity list)
        (firstError: EvaluationError option)
        : ChannelEventReadiness =
        match remaining with
        | [] ->
            match firstError with
            | Some error -> CannotCommit error
            | None -> WouldBlock
        | eventId :: rest ->
            match readEvent "choose" eventId state with
            | Error error -> findReady (seen @ [ eventId ]) rest (firstError |> Option.orElse (Some error))
            | Ok event ->
                match selectEvent executeWord event state with
                | ReadyToCommit selection ->
                    match collectNacks executeWord (seen @ rest) state with
                    | Error error -> CannotCommit error
                    | Ok fallbackSelection ->
                        ReadyToCommit
                            {
                                Actions = selection.Actions @ fallbackSelection.Actions
                                Wrappers = selection.Wrappers @ fallbackSelection.Wrappers
                            }
                | WouldBlock -> findReady (seen @ [ eventId ]) rest firstError
                | CannotCommit error -> findReady (seen @ [ eventId ]) rest (firstError |> Option.orElse (Some error))

    findReady [] eventIds None

and private collectNacks
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (eventIds: EntryIdentity list)
    (state: EvaluationState)
    : Result<ChannelEventSelection, EvaluationError> =
    eventIds
    |> List.fold
        (fun accumulated eventId ->
            match accumulated with
            | Error error -> Error error
            | Ok selection ->
                match readEvent "with-fallback" eventId state with
                | Error error -> Error error
                | Ok event ->
                    match collectNacksFromEvent executeWord event state with
                    | Error error -> Error error
                    | Ok nextSelection ->
                        Ok
                            {
                                Actions = selection.Actions @ nextSelection.Actions
                                Wrappers = selection.Wrappers @ nextSelection.Wrappers
                            })
        (Ok emptySelection)

and private collectNacksFromEvent
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (event: ChannelEvent)
    (state: EvaluationState)
    : Result<ChannelEventSelection, EvaluationError> =
    match event with
    | ChannelWithFallbackEvent(_, fallbackEventId) ->
        match readEvent "with-fallback" fallbackEventId state with
        | Error error -> Error error
        | Ok fallbackEvent ->
            match selectEvent executeWord fallbackEvent state with
            | ReadyToCommit selection -> Ok selection
            | WouldBlock -> Ok emptySelection
            | CannotCommit error -> Error error
    | ChannelChooseEvent eventIds -> collectNacks executeWord eventIds state
    | ChannelWrapEvent(eventId, _) ->
        match readEvent "wrap" eventId state with
        | Error error -> Error error
        | Ok innerEvent -> collectNacksFromEvent executeWord innerEvent state
    | ChannelGuardEvent(eventId, _) ->
        match readEvent "guard" eventId state with
        | Error error -> Error error
        | Ok innerEvent -> collectNacksFromEvent executeWord innerEvent state
    | NeverChannelEvent
    | ChannelSendEvent _
    | ChannelReceiveEvent _ -> Ok emptySelection

// ---- Committing a selection ------------------------------------------------

let private commitAction
    (action: ChannelEventAction)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match action with
    | SendAction(channelId, messageId) ->
        let originalStack = state.Stack

        match
            sendWord
                { state with
                    Stack = [ messageId; channelId ]
                }
        with
        | Error error -> Error error
        | Ok afterSend -> Ok { afterSend with Stack = originalStack }
    | ReceiveAction channelId ->
        match receiveWord { state with Stack = [ channelId ] } with
        | Error error -> Error error
        | Ok afterReceive ->
            Ok
                { afterReceive with
                    Stack = afterReceive.Stack @ state.Stack
                }

let rec private commitActions
    (actions: ChannelEventAction list)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match actions with
    | [] -> Ok state
    | action :: remaining ->
        match commitAction action state with
        | Error error -> Error error
        | Ok afterAction -> commitActions remaining afterAction

let private commitSelection
    (selection: ChannelEventSelection)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match commitActions selection.Actions state with
    | Error error -> Error error
    | Ok afterActions ->
        let wrapperTerms =
            selection.Wrappers
            |> List.fold
                (fun accumulated quotationId ->
                    match accumulated with
                    | Error error -> Error error
                    | Ok termsSoFar ->
                        match EvaluatorCode.decode quotationId afterActions.Store with
                        | Ok quotationTerms -> Ok(termsSoFar @ quotationTerms)
                        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "wrap", ExpectedQuotation))
                        | Error error -> Error(CodeError(EvaluatorCode.describeError error)))
                (Ok [])

        match wrapperTerms with
        | Error error -> Error error
        | Ok terms -> Ok { afterActions with Program = terms @ afterActions.Program }

// ---- Words -----------------------------------------------------------------

/// Pops a channel and a message and pushes a send event.
/// Stack: channelId message -> eventId
let sendEventWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("send-event", 2, Stack.depth state.Stack))
    | Some(messageId, afterMessage) ->
        match Stack.popIndex afterMessage with
        | None -> Error(StackUnderflow("send-event", 2, Stack.depth state.Stack))
        | Some(target, remaining) ->
            let channelId = messageTargetId target state

            let (eventId, afterEvent) =
                writeEvent (ChannelSendEvent(channelId, messageId)) state

            Ok
                { afterEvent with
                    Stack = Stack.push eventId remaining
                    Ownership =
                        { afterEvent.Ownership with
                            Epoch = Epoch.promoteAll [ messageId ] afterEvent.Ownership.Epoch
                        }
                }

/// Pops a channel and pushes a receive event.
/// Stack: channelId -> eventId
let receiveEventWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popChannelId "receive-event" state with
    | Error error -> Error error
    | Ok(channelId, remaining) ->
        let (eventId, afterEvent) = writeEvent (ChannelReceiveEvent channelId) state

        Ok
            { afterEvent with
                Stack = Stack.push eventId remaining
            }

/// Pops an event list and pushes a choice event.
/// Stack: eventList -> eventId
let chooseWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("choose", 1, Stack.depth state.Stack))
    | Some(listId, remaining) ->
        match readEventList "choose" listId state with
        | Error error -> Error error
        | Ok [] -> Error(EmptyEventChoice "choose")
        | Ok eventIds ->
            let (eventId, afterEvent) = writeEvent (ChannelChooseEvent eventIds) state

            Ok
                { afterEvent with
                    Stack = Stack.push eventId remaining
                }

/// Pops an event and a quotation and pushes a wrapped event.
/// Stack: eventId quotationId -> eventId
let wrapWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("wrap", 2, Stack.depth state.Stack))
    | Some(quotationId, afterQuotation) ->
        match EvaluatorCode.decode quotationId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "wrap", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            match popEventId "wrap" { state with Stack = afterQuotation } with
            | Error error -> Error error
            | Ok(eventId, remaining) ->
                let (wrappedEventId, afterEvent) =
                    writeEvent (ChannelWrapEvent(eventId, quotationId)) state

                Ok
                    { afterEvent with
                        Stack = Stack.push wrappedEventId remaining
                    }

/// Pops an event and a predicate quotation and pushes a guarded event.
/// Stack: eventId quotationId -> eventId
let guardWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("guard", 2, Stack.depth state.Stack))
    | Some(quotationId, afterQuotation) ->
        match EvaluatorCode.decode quotationId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "guard", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            match popEventId "guard" { state with Stack = afterQuotation } with
            | Error error -> Error error
            | Ok(eventId, remaining) ->
                let (guardedEventId, afterEvent) =
                    writeEvent (ChannelGuardEvent(eventId, quotationId)) state

                Ok
                    { afterEvent with
                        Stack = Stack.push guardedEventId remaining
                    }

/// Pops a main event and a fallback event and pushes the combined event.
/// Stack: mainEventId fallbackEventId -> eventId
let withFallbackWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popEventId "with-fallback" state with
    | Error error -> Error error
    | Ok(fallbackEventId, afterNack) ->
        match popEventId "with-fallback" { state with Stack = afterNack } with
        | Error error -> Error error
        | Ok(mainEventId, remaining) ->
            let (withFallbackEventId, afterEvent) =
                writeEvent (ChannelWithFallbackEvent(mainEventId, fallbackEventId)) state

            Ok
                { afterEvent with
                    Stack = Stack.push withFallbackEventId remaining
                }

/// Pops an event and synchronizes it. A blocked event is registered as a sync
/// waiter and halts the current actor; it is retried after a channel changes.
/// Stack: eventId -> (the selected event's result)
let syncWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match popEventId "sync" state with
    | Error error -> Error error
    | Ok(eventId, remaining) ->
        let promotedState =
            { state with
                Stack = remaining
                Ownership =
                    { state.Ownership with
                        Epoch = Epoch.promoteAll [ eventId ] state.Ownership.Epoch
                    }
            }

        match readEvent "sync" eventId promotedState with
        | Error error -> Error error
        | Ok event ->
            match selectEvent executeWord event promotedState with
            | ReadyToCommit selection -> commitSelection selection promotedState
            | CannotCommit error -> Error error
            | WouldBlock ->
                let schedulerState =
                    registerEventWaiter state.SchedulerState.SelfId eventId state.SchedulerState

                Ok(
                    haltCurrent
                        None
                        { promotedState with
                            SchedulerState = schedulerState
                        }
                )
