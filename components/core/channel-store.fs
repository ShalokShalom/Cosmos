module ChannelStore

// ============================================================
// Channel-specific store encoding. This is Prelude code: the kernel store
// knows only tensors and text.
// ============================================================

open Types

/// The identity entry a channel (or a private actor mailbox) starts with.
let channelIdentityTensor: Tensor = Tensor.vector [ ChannelTag; 1.0 ]

/// Is this tensor a channel's identity entry?
let isChannelIdentity (tensor: Tensor) : bool =
    Tensor.rank tensor = 1
    && Tensor.size tensor = 2
    && Tensor.item tensor 0 = ChannelTag

/// The state entries of one channel, oldest first.
let private stateEntriesFor (channelId: EntryIdentity) (store: Store) : (EntryIdentity * Tensor) list =
    let number = double (EntryIdentity.value channelId)

    store.Lexicon
    |> Map.toList
    |> List.filter (fun (_, tensor) ->
        Tensor.rank tensor = 1
        && Tensor.size tensor >= 3
        && Tensor.item tensor 0 = ChannelStateTag
        && Tensor.item tensor 1 = number)

/// The channel's current state: whether it is open, and its queued messages,
/// oldest first. A channel with no state entry is open and empty.
let readChannelState (channelId: EntryIdentity) (store: Store) : bool * EntryIdentity list =
    match stateEntriesFor channelId store |> List.tryLast with
    | None -> (true, [])
    | Some(_, tensor) ->
        let isOpen = Tensor.item tensor 2 <> 0.0

        let queued =
            tensor.Data
            |> Array.skip 3
            |> Array.toList
            |> List.map (fun value -> EntryIdentity.ofInt (int value))

        (isOpen, queued)

/// Appends a channel's new state. The previous state entry stays in the store;
/// the newest entry is the current one.
let appendChannelState
    (isOpen: bool)
    (queue: EntryIdentity list)
    (channelId: EntryIdentity)
    (store: Store)
    : Store =
    let state =
        Tensor.vector (
            [
                ChannelStateTag
                double (EntryIdentity.value channelId)
                (if isOpen then 1.0 else 0.0)
            ]
            @ (queue |> List.map (fun identity -> double (EntryIdentity.value identity)))
        )

    snd (Store.addTensor state store)
