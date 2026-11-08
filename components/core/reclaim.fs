module Reclaim

open Types

type EntryLinearity =
    | Root
    | Unreferenced
    | Linear
    | Shared

type ReclaimReport =
    {
        Reachable: Set<EntryIdentity>
        Linearity: Map<EntryIdentity, EntryLinearity>
        Reclaimable: EntryIdentity list
    }

let possibleReferences (store: Store) (tensor: Tensor) : EntryIdentity list =
    tensor.Data
    |> Array.choose (fun value ->
        if value >= 0.0 && value < double store.NextIndex && value = floor value then
            Some(EntryIdentity.ofInt (int value))
        else
            None)
    |> Array.distinct
    |> Array.toList

let report (roots: EntryIdentity list) (store: Store) : ReclaimReport =
    let textIdentities =
        store.TextCache |> Map.toList |> List.map snd |> Set.ofList

    let entryReferences identity tensor =
        if Set.contains identity textIdentities then
            []
        else
            possibleReferences store tensor

    let rec visit (pending: EntryIdentity list) (seen: Set<EntryIdentity>) : Set<EntryIdentity> =
        match pending with
        | [] -> seen
        | identity :: rest ->
            if Set.contains identity seen then
                visit rest seen
            else
                match Map.tryFind identity store.Lexicon with
                | None -> visit rest seen
                | Some tensor -> visit (entryReferences identity tensor @ rest) (Set.add identity seen)

    let textRoots = store.TextCache |> Map.toList |> List.map snd

    let roots =
        roots
        @ textRoots
        @ (match store.Table with
           | Some identity -> [ identity ]
           | None -> [])
        @ (match store.VocabularyTable with
           | Some identity -> [ identity ]
           | None -> [])

    let rootSet = Set.ofList roots
    let reachable = visit roots Set.empty

    let incoming =
        reachable
        |> Set.toList
        |> List.collect (fun identity ->
            match Map.tryFind identity store.Lexicon with
            | None -> []
            | Some tensor -> entryReferences identity tensor)
        |> List.countBy id
        |> Map.ofList

    let linearity =
        store.Lexicon
        |> Map.toList
        |> List.map fst
        |> List.map (fun identity ->
            let classification =
                if Set.contains identity rootSet then
                    Root
                else
                    match Map.tryFind identity incoming with
                    | Some 1 -> Linear
                    | Some _ -> Shared
                    | None -> Unreferenced

            (identity, classification))
        |> Map.ofList

    let reclaimable =
        linearity
        |> Map.toList
        |> List.filter (fun (_, classification) -> classification = Unreferenced)
        |> List.map fst

    {
        Reachable = reachable
        Linearity = linearity
        Reclaimable = reclaimable
    }

/// The roots owned by one execution state: its stack, its tables, its frames,
/// its handlers, and its ownership records. This is the per-actor half; the
/// scheduler-wide roots are added once by `rootsOf`.
let private stateRoots (state: EvaluationState) : EntryIdentity list =
    let methodRoots (method: Method) : EntryIdentity list =
        method.Address :: method.Body :: method.Specializers

    let rec combinationRoots (core: CombinationFrame) : EntryIdentity list =
        core.CallerStack
        @ core.Arguments
        @ (core.Befores @ core.Primaries @ core.Afters |> List.collect methodRoots)
        @ (match core.PrimaryStack with
           | Some identities -> identities
           | None -> [])

    and methodCallRoots (call: MethodCallFrame) : EntryIdentity list =
        methodRoots call.Method
        @ call.CallerStack
        @ call.Arguments
        @ (call.NextMethods |> List.collect methodRoots)
        @ (match call.Core with
           | Some core -> combinationRoots core
           | None -> [])

    let frameRoots (frame: MethodFrame) : EntryIdentity list =
        match frame with
        | MethodCall call -> methodCallRoots call
        | CombinationCall core -> combinationRoots core

    let handlerRoots (handler: HandlerFrame) : EntryIdentity list = handler.Handler :: handler.CallerStack

    let optionalRoots =
        [
            state.UserWordTable
            state.BindingTable
            state.SetTable
            state.DeclarationTable
            state.GenericFunctionTable
            state.WordTable
            state.HistoryTable
            state.ClassTable
            state.InstanceStateTable
            state.CurrentFrame
            state.SchedulerState.ChannelsRoot
        ]
        |> List.choose id

    let ownershipRoots =
        (state.Ownership.Owners
         |> Map.toList
         |> List.collect (fun (value, owner) -> [ value; owner ]))
        @ Set.toList state.Ownership.Consumed

    (state.VocabularyTable :: state.Stack)
    @ optionalRoots
    @ List.choose id state.CallFrames
    @ (state.MethodFrames |> List.collect frameRoots)
    @ (state.Handlers |> List.collect handlerRoots)
    @ ownershipRoots

/// Every live root: the current execution state, each non-terminated actor's
/// suspended state, and the scheduler's channels and waiters. A terminated
/// actor is dead, so its stack does not keep an entry alive.
let rootsOf (state: EvaluationState) : EntryIdentity list =
    let scheduler = state.SchedulerState

    let channelRoots =
        scheduler.Channels
        |> Map.toList
        |> List.collect (fun (identity, channel) -> identity :: channel.Identity :: channel.ReceiveWaiters)

    let schedulerRoots =
        (scheduler.SelfId :: scheduler.ReadyQueue)
        @ channelRoots
        @ (scheduler.EventWaiters
           |> Map.toList
           |> List.collect (fun (actor, event) -> [ actor; event ]))

    let liveActorRoots =
        scheduler.Actors
        |> Map.toList
        |> List.filter (fun (_, actor) -> actor.Status <> Terminated)
        |> List.collect (fun (_, actor) -> stateRoots actor.State)

    stateRoots state @ liveActorRoots @ schedulerRoots

let reportForState (state: EvaluationState) : ReclaimReport = report (rootsOf state) state.Store

let compact (roots: EntryIdentity list) (store: Store) : Store =
    let retained = (report roots store).Reachable

    { store with
        Lexicon = store.Lexicon |> Map.filter (fun identity _ -> Set.contains identity retained)
        TextCache = store.TextCache |> Map.filter (fun _ identity -> Set.contains identity retained)
        TextIdentities = store.TextIdentities |> Set.filter (fun identity -> Set.contains identity retained)
    }

let compactForState (state: EvaluationState) : Store = compact (rootsOf state) state.Store

/// The epoch state of the live actor and every actor snapshot. A terminated
/// actor still contributes its closed epochs.
let private epochStates (state: EvaluationState) : EpochState list =
    let actorStates =
        state.SchedulerState.Actors
        |> Map.toList
        |> List.map (fun (_, actor) -> actor.State.Ownership.Epoch)

    state.Ownership.Epoch :: actorStates

/// The unpromoted entries of every closed epoch, across the live actor and all
/// snapshots.
let closedCandidatesForState (state: EvaluationState) : Set<EntryIdentity> =
    epochStates state
    |> List.collect Epoch.closedCandidates
    |> Set.ofList

/// Physically removes the closed-epoch candidates that no live root reaches.
/// A promoted entry is never a candidate, and a live root keeps its entry. The
/// allocation frontier and surviving addresses are unchanged.
let compactClosedEpochs (state: EvaluationState) : Store =
    let candidates =
        closedCandidatesForState state
        |> Set.filter (fun identity -> Map.containsKey identity state.Store.Lexicon)

    if Set.isEmpty candidates then
        state.Store
    else
        let reclaimable =
            reportForState state
            |> fun report -> Set.ofList report.Reclaimable

        let removable = Set.intersect candidates reclaimable

        { state.Store with
            Lexicon =
                state.Store.Lexicon
                |> Map.filter (fun identity _ -> not (Set.contains identity removable))
            TextCache =
                state.Store.TextCache
                |> Map.filter (fun _ identity -> not (Set.contains identity removable))
            TextIdentities =
                state.Store.TextIdentities
                |> Set.filter (fun identity -> not (Set.contains identity removable))
        }
