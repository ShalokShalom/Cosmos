module Stack

open Types

/// The stack is control state: a sequence of store references. It is not
/// cell data, so it is allowed host types (the same latitude as the edge).
let empty: EntryIdentity list = []

let push (identity: EntryIdentity) (stack: EntryIdentity list) : EntryIdentity list = identity :: stack

let popIndex (stack: EntryIdentity list) : (EntryIdentity * EntryIdentity list) option =
    match stack with
    | top :: rest -> Some(top, rest)
    | [] -> None

let popResolved (store: Store) (stack: EntryIdentity list) : (Tensor * EntryIdentity list) option =
    match popIndex stack with
    | None -> None
    | Some(identity, remaining) ->
        match Store.lookupTensor identity store with
        | Some resolved -> Some(resolved, remaining)
        | None -> None

let peek (stack: EntryIdentity list) : EntryIdentity option = List.tryHead stack

let depth (stack: EntryIdentity list) : int = List.length stack
