module VocabularyInImage

open Types

let private categoryToNumber (category: WordCategory) : RealNumber =
    match category with
    | TypeName -> 0.0
    | FieldName -> 1.0
    | FunctionName -> 2.0
    | Quantifier -> 3.0
    | Connective -> 4.0
    | Conditional -> 5.0
    | Negation -> 6.0
    | Pronoun -> 7.0
    | Copula -> 8.0
    | Comparison -> 9.0
    | Article -> 10.0

let private numberToCategory (number: RealNumber) : Result<WordCategory, string> =
    match int number with
    | 0 -> Ok TypeName
    | 1 -> Ok FieldName
    | 2 -> Ok FunctionName
    | 3 -> Ok Quantifier
    | 4 -> Ok Connective
    | 5 -> Ok Conditional
    | 6 -> Ok Negation
    | 7 -> Ok Pronoun
    | 8 -> Ok Copula
    | 9 -> Ok Comparison
    | 10 -> Ok Article
    | other -> Error("Unknown category: " + string other)

let addEntry (entry: VocabularyEntry) (store: Store) : EntryIdentity * Store =
    let (tokenId, store1) = Store.addText entry.Token store
    let (formId, store2) = Store.addText entry.Form store1
    let (implementationNameId, store3) = Store.addText entry.ImplementationName store2

    let (categoryId, store4) =
        Store.addTensor (Tensor.scalar (categoryToNumber entry.Category)) store3

    let (activeId, store5) =
        Store.addTensor (Tensor.scalar (if entry.IsActive then 1.0 else 0.0)) store4

    let entryTensor =
        Tensor.vector
            [
                double (EntryIdentity.value tokenId)
                double (EntryIdentity.value formId)
                double (EntryIdentity.value implementationNameId)
                double (EntryIdentity.value categoryId)
                double (EntryIdentity.value activeId)
            ]

    Store.addTensor entryTensor store5

let addVocabulary (entries: VocabularyEntry list) (store: Store) : EntryIdentity * Store =
    let rec loop
        (remaining: VocabularyEntry list)
        (accumulated: EntryIdentity list)
        (currentImage: Store)
        : EntryIdentity list * Store =
        match remaining with
        | [] -> (List.rev accumulated, currentImage)
        | entry :: rest ->
            let (entryId, newStore) = addEntry entry currentImage
            loop rest (entryId :: accumulated) newStore

    let (entryIds, finalImage) = loop entries [] store

    let vocabularyTensor =
        Tensor.vector (entryIds |> List.map (fun identity -> double (EntryIdentity.value identity)))

    Store.addTensor vocabularyTensor finalImage

let readEntry (entryId: EntryIdentity) (store: Store) : Result<VocabularyEntry, string> =
    match Store.lookupTensor entryId store with
    | None -> Error("Entry index " + string (EntryIdentity.value entryId) + " not found")
    | Some entryTensor ->
        if Tensor.size entryTensor < 5 then
            Error("Entry tensor too small at index " + string (EntryIdentity.value entryId))
        else
            let tokenId = EntryIdentity.ofInt (int (Tensor.item entryTensor 0))
            let formId = EntryIdentity.ofInt (int (Tensor.item entryTensor 1))
            let implementationNameId = EntryIdentity.ofInt (int (Tensor.item entryTensor 2))
            let categoryId = EntryIdentity.ofInt (int (Tensor.item entryTensor 3))
            let activeId = EntryIdentity.ofInt (int (Tensor.item entryTensor 4))

            match
                Store.readText tokenId store,
                Store.readText formId store,
                Store.readText implementationNameId store,
                Store.lookupTensor categoryId store,
                Store.lookupTensor activeId store
            with
            | Some token, Some form, Some fsharp, Some categoryTensor, Some activeTensor ->
                numberToCategory (Tensor.item categoryTensor 0)
                |> Result.map (fun category ->
                    {
                        Token = token
                        Form = form
                        ImplementationName = fsharp
                        Category = category
                        IsActive = (Tensor.item activeTensor 0) <> 0.0
                    })
            | _ ->
                Error(
                    "Failed to read entry components at index "
                    + string (EntryIdentity.value entryId)
                )

let readVocabulary (vocabularyRootId: EntryIdentity option) (store: Store) : Result<VocabularyEntry list, string> =
    match vocabularyRootId with
    | None -> Error "No vocabulary root set"
    | Some rootId ->
        match Store.lookupTensor rootId store with
        | None -> Error("Vocabulary root " + string (EntryIdentity.value rootId) + " not found")
        | Some vocabularyTensor ->
            let rec loop
                (identities: RealNumber list)
                (accumulated: VocabularyEntry list)
                : Result<VocabularyEntry list, string> =
                match identities with
                | [] -> Ok(List.rev accumulated)
                | identity :: rest ->
                    match readEntry (EntryIdentity.ofInt (int identity)) store with
                    | Ok entry -> loop rest (entry :: accumulated)
                    | Error message -> Error message

            loop (vocabularyTensor.Data |> Array.toList) []

let lookupByToken (token: string) (vocabularyRootId: EntryIdentity) (store: Store) : VocabularyEntry option =
    match Store.lookupTensor vocabularyRootId store with
    | None -> None
    | Some vocabularyTensor ->
        vocabularyTensor.Data
        |> Array.toList
        |> List.rev
        |> List.tryPick (fun identity ->
            match readEntry (EntryIdentity.ofInt (int identity)) store with
            | Ok entry when entry.Token = token -> Some entry
            | _ -> None)
