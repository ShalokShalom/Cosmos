module Epoch

open Types

let durable: EpochId = durableEpoch

let empty: EpochState = emptyEpochState

/// Resumes an epoch at the current shared frontier without attributing the
/// interleaved gap to it.
let resumeAt (frontier: int) (state: EpochState) : EpochState =
    { state with LastFrontier = frontier }

/// Starts a specific incarnation epoch at the current frontier. The actor id
/// keeps epoch identities distinct without a global counter.
let beginAt (identity: EpochId) (frontier: int) (state: EpochState) : EpochState =
    { state with
        Current = identity
        NextEpoch = max state.NextEpoch (identity + 1)
        LastFrontier = frontier
    }

/// Starts a new incarnation epoch at the current frontier.
let beginEpoch (frontier: int) (state: EpochState) : EpochId * EpochState =
    let identity = state.NextEpoch

    (identity,
     { state with
         Current = identity
         NextEpoch = identity + 1
         LastFrontier = frontier
     })

/// Attributes the frontier growth since the last record to the current epoch.
/// A frontier that did not advance adds nothing; the durable epoch records no
/// ranges because it never closes.
let recordAllocation (frontier: int) (state: EpochState) : EpochState =
    if frontier <= state.LastFrontier then
        state
    elif state.Current = durable then
        { state with LastFrontier = frontier }
    else
        let range = { Start = state.LastFrontier; End = frontier }
        let existing = state.Ranges |> Map.tryFind state.Current |> Option.defaultValue []

        { state with
            Ranges = state.Ranges |> Map.add state.Current (range :: existing)
            LastFrontier = frontier
        }

/// Reclassifies an entry as durable. The entry keeps its address.
let promote (identity: EntryIdentity) (state: EpochState) : EpochState =
    { state with Promoted = Set.add identity state.Promoted }

/// Reclassifies several entries as durable.
let promoteAll (identities: EntryIdentity list) (state: EpochState) : EpochState =
    { state with Promoted = Set.union state.Promoted (Set.ofList identities) }

/// Ends an incarnation epoch. The durable epoch never closes.
let closeEpoch (identity: EpochId) (state: EpochState) : EpochState =
    if identity = durable then
        state
    else
        { state with
            Closed = Set.add identity state.Closed
            Current = if state.Current = identity then durable else state.Current
        }

/// The entries an epoch allocated, whether or not they were promoted.
let entriesOf (identity: EpochId) (state: EpochState) : EntryIdentity list =
    state.Ranges
    |> Map.tryFind identity
    |> Option.defaultValue []
    |> List.collect (fun range -> [ range.Start .. range.End - 1 ] |> List.map EntryIdentity.ofInt)

/// The unpromoted entries of every closed epoch: the epoch's reclaim
/// candidates. Reachability from live roots is still the reclaimer's decision.
let closedCandidates (state: EpochState) : EntryIdentity list =
    state.Closed
    |> Set.toList
    |> List.collect (fun identity -> entriesOf identity state)
    |> List.filter (fun identity -> not (Set.contains identity state.Promoted))
    |> List.distinct

/// The epoch that allocated an entry, if it is still attributed to one.
let epochOf (identity: EntryIdentity) (state: EpochState) : EpochId option =
    let index = EntryIdentity.value identity

    state.Ranges
    |> Map.toList
    |> List.tryPick (fun (epoch, ranges) ->
        if ranges |> List.exists (fun range -> index >= range.Start && index < range.End) then
            Some epoch
        else
            None)

/// The durable entries of an image: entries outside every ephemeral range, plus
/// explicitly promoted entries.
let durableEntries (epochs: EpochState list) (store: Store) : Set<EntryIdentity> =
    let ephemeral =
        epochs
        |> List.collect (fun epoch ->
            epoch.Ranges
            |> Map.toList
            |> List.collect (fun (_, ranges) -> ranges))
        |> List.collect (fun range -> [ range.Start .. range.End - 1 ])
        |> List.map EntryIdentity.ofInt
        |> Set.ofList

    let promoted =
        epochs
        |> List.collect (fun epoch -> Set.toList epoch.Promoted)
        |> Set.ofList

    store.Lexicon
    |> Map.toList
    |> List.map fst
    |> Set.ofList
    |> Set.filter (fun identity -> not (Set.contains identity ephemeral) || Set.contains identity promoted)
