module Store

open Types

let empty: Store =
    {
        Lexicon = Lexicon.empty
        Table = None
        VocabularyTable = None
        NextIndex = 0
        TextCache = Map.empty
        TextIdentities = Set.empty
    }

let addTensor (tensor: Tensor) (store: Store) : EntryIdentity * Store =
    let newId = EntryIdentity.ofInt store.NextIndex
    let newLexicon = Lexicon.add newId tensor store.Lexicon

    (newId,
     { store with
         Lexicon = newLexicon
         NextIndex = store.NextIndex + 1
     })

let addText (text: string) (store: Store) : EntryIdentity * Store =
    match Map.tryFind text store.TextCache with
    | Some existingId -> (existingId, store)
    | None ->
        let data = text.ToCharArray() |> Array.map (fun character -> double (int character))

        let tensor =
            {
                Data = data
                Shape = [| data.Length |]
            }

        let (identity, newStore) = addTensor tensor store

        (identity,
         { newStore with
             TextCache = Map.add text identity newStore.TextCache
             TextIdentities = Set.add identity newStore.TextIdentities
         })

let readText (identity: EntryIdentity) (store: Store) : string option =
    match Lexicon.lookup identity store.Lexicon with
    | Some tensor -> Some(System.String(tensor.Data |> Array.map (fun value -> char (int value))))
    | None -> None

/// Reads text only when the identity was registered as text. Use this when you
/// need "is this actually a string?" rather than a loose decode of any 1-D
/// tensor: it disambiguates text from numeric tensors. The identity check is a
/// set lookup, not a scan of the cache.
let readTextAt (identity: EntryIdentity) (store: Store) : string option =
    if Set.contains identity store.TextIdentities then
        readText identity store
    else
        None

let setTable (identity: EntryIdentity) (store: Store) : Store = { store with Table = Some identity }

let setVocabularyTable (identity: EntryIdentity) (store: Store) : Store =
    { store with
        VocabularyTable = Some identity
    }

/// Scans a pairs vector [name; value; ...] for a name, latest write wins.
let findPair (nameId: EntryIdentity) (root: EntryIdentity option) (store: Store) : EntryIdentity option =
    match root with
    | None -> None
    | Some rootId ->
        match Lexicon.lookup rootId store.Lexicon with
        | Some vector ->
            let rec loop (identities: RealNumber list) (latest: EntryIdentity option) : EntryIdentity option =
                match identities with
                | nameValue :: valueValue :: rest ->
                    let candidateName = EntryIdentity.ofInt (int nameValue)
                    let candidateValue = EntryIdentity.ofInt (int valueValue)

                    loop
                        rest
                        (if candidateName = nameId then
                             Some candidateValue
                         else
                             latest)
                | _ -> latest

            loop (vector.Data |> Array.toList) None
        | None -> None

/// Appends a [name; value] pair, returning the new association-vector root.
let addToEnvironment
    (nameId: EntryIdentity)
    (valueId: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity * Store =
    let pair =
        Tensor.vector [ double (EntryIdentity.value nameId); double (EntryIdentity.value valueId) ]

    match root with
    | Some rootId ->
        match Lexicon.lookup rootId store.Lexicon with
        | Some vector -> addTensor (Tensor.concat vector pair) store
        | None -> addTensor pair store
    | None -> addTensor pair store

let lookupTensor (identity: EntryIdentity) (store: Store) : Tensor option = Lexicon.lookup identity store.Lexicon

let describe (store: Store) : string =
    let root =
        match store.Table with
        | Some identity -> EntryIdentity.value identity
        | None -> -1

    let vocabularyRoot =
        match store.VocabularyTable with
        | Some identity -> EntryIdentity.value identity
        | None -> -1

    "Store(root="
    + string (IndexTransformation.toExternal root)
    + ", vocabularyRoot="
    + string (IndexTransformation.toExternal vocabularyRoot)
    + ", nextIndex="
    + string store.NextIndex
    + ")\n"
    + Lexicon.describe store.Lexicon
