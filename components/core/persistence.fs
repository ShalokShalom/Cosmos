module Persistence

open Types

/// A malformed or truncated store: a bad shape, a duplicate address, an
/// out-of-range identity, or a stream that ends early is this named failure,
/// not a bug in the core. The reader never returns a partial store and never
/// lets an arbitrary I/O exception escape.
type CorruptStore = CorruptStore of reason: string

exception CorruptImageException of CorruptStore

/// The largest tensor the reader will materialize, in elements. The reader
/// allocates from the file's own counts, so an unchecked count is an
/// allocation attack; the limit is far above any store the core produces.
let maxTensorElements = 10_000_000

/// The largest number of entries the reader will materialize.
let maxEntries = 10_000_000

/// The largest interned text the reader will materialize, in UTF-8 bytes. The
/// length prefix is untrusted, so it is checked before the buffer is allocated.
let maxTextBytes = 1_000_000

/// NativeAOT note: callers build these messages by concatenation, not
/// `sprintf`; the generated printf specialization needs `MakeGenericMethod`,
/// which NativeAOT does not support.
let private fail (reason: string) : 'a =
    raise (CorruptImageException(CorruptStore reason))

/// The number of elements a shape describes, in Int64 so a hostile shape
/// cannot overflow the product.
let private shapeSize (shape: int[]) : int64 =
    Array.fold (fun accumulated dimension -> accumulated * int64 dimension) 1L shape

let private writeTensor (writer: System.IO.BinaryWriter) (tensor: Tensor) : unit =
    writer.Write(tensor.Shape.Length)

    for dimension in tensor.Shape do
        writer.Write(dimension)

    writer.Write(tensor.Data.Length)

    for value in tensor.Data do
        writer.Write(value)

/// Reads a tensor and checks that its shape and data agree, that the counts
/// are not negative, and that the allocation stays inside the reader's limit.
let private readTensor (reader: System.IO.BinaryReader) : Tensor =
    let rank = reader.ReadInt32()

    if rank < 0 then
        fail ("A tensor declares a negative rank: " + string rank + ".")

    let shape = Array.init rank (fun _ -> reader.ReadInt32())

    if shape |> Array.exists (fun dimension -> dimension < 0) then
        fail "A tensor declares a negative dimension."

    let size = reader.ReadInt32()

    if size < 0 then
        fail ("A tensor declares a negative size: " + string size + ".")

    if int64 size > int64 maxTensorElements then
        fail ("A tensor declares " + string size + " elements, beyond the reader's limit.")

    if shapeSize shape <> int64 size then
        fail (
            "A tensor's shape "
            + string shape
            + " does not describe its "
            + string size
            + " elements."
        )

    let data = Array.init size (fun _ -> reader.ReadDouble())
    { Data = data; Shape = shape }

let private writeOptionalId (writer: System.IO.BinaryWriter) (identity: EntryIdentity option) : unit =
    match identity with
    | Some value -> writer.Write(EntryIdentity.value value)
    | None -> writer.Write(-1)

let private readOptionalId (reader: System.IO.BinaryReader) : EntryIdentity option =
    let value = reader.ReadInt32()
    if value < 0 then None else Some(EntryIdentity.ofInt value)

/// Reads the 7-bit encoded byte length that `BinaryWriter.Write(string)` emits.
/// The decoder is local so the bound can be enforced before allocating.
let private readSevenBitInt (reader: System.IO.BinaryReader) : int =
    let mutable result = 0
    let mutable shift = 0
    let mutable more = true

    while more do
        if shift >= 35 then
            fail "A text length prefix is invalid."

        let current = reader.ReadByte()
        result <- result ||| (int (current &&& 0x7Fuy) <<< shift)
        more <- (current &&& 0x80uy) <> 0uy
        shift <- shift + 7

    result

/// Reads one interned text with a checked length. `BinaryReader.ReadString`
/// cannot be used: it allocates from the untrusted prefix before a caller can
/// refuse it.
let private readText (reader: System.IO.BinaryReader) : string =
    let length = readSevenBitInt reader

    if length < 0 || length > maxTextBytes then
        fail ("A text declares " + string length + " bytes, beyond the reader's limit.")

    let bytes = reader.ReadBytes length

    if bytes.Length <> length then
        fail "A text is truncated."

    System.Text.Encoding.UTF8.GetString bytes

let private writeStore (writer: System.IO.BinaryWriter) (store: Store) : unit =
    writeOptionalId writer store.Table
    writeOptionalId writer store.VocabularyTable
    writer.Write(store.NextIndex)
    writer.Write(store.Lexicon.Count)

    for KeyValue(identity, tensor) in store.Lexicon do
        writer.Write(EntryIdentity.value identity)
        writeTensor writer tensor

    writer.Write(store.TextCache.Count)

    for KeyValue(text, identity) in store.TextCache do
        writer.Write(text)
        writer.Write(EntryIdentity.value identity)

let private readStore (reader: System.IO.BinaryReader) : Store =
    let root = readOptionalId reader
    let vocabularyRoot = readOptionalId reader
    let nextIndex = reader.ReadInt32()
    let lexiconCount = reader.ReadInt32()

    if nextIndex < 0 then
        fail ("An store declares a negative allocation frontier: " + string nextIndex + ".")

    if lexiconCount < 0 || lexiconCount > maxEntries then
        fail (
            "An store declares "
            + string lexiconCount
            + " entries, beyond the reader's limit."
        )

    if lexiconCount > nextIndex then
        fail (
            "An store declares "
            + string lexiconCount
            + " entries beyond its allocation frontier of "
            + string nextIndex
            + "."
        )

    let lexicon =
        List.init lexiconCount (fun _ ->
            let identity = EntryIdentity.ofInt (reader.ReadInt32())
            let tensor = readTensor reader
            (identity, tensor))
        |> Map.ofList

    // Every entry must own a distinct address below the frontier: a duplicate
    // or an out-of-range identity would let a later append overwrite an entry.
    if lexicon.Count <> lexiconCount then
        fail "An store declares the same entry address twice."

    if
        lexicon
        |> Map.toList
        |> List.exists (fun (identity, _) -> EntryIdentity.value identity >= nextIndex)
    then
        fail "An store holds an entry address beyond its allocation frontier."

    let textCacheCount = reader.ReadInt32()

    if textCacheCount < 0 || textCacheCount > maxEntries then
        fail (
            "An store declares "
            + string textCacheCount
            + " interned texts, beyond the reader's limit."
        )

    let textCache =
        List.init textCacheCount (fun _ ->
            let text = readText reader
            let identity = EntryIdentity.ofInt (reader.ReadInt32())
            (text, identity))
        |> Map.ofList

    if textCache.Count <> textCacheCount then
        fail "An store interns the same text twice."

    if
        textCache
        |> Map.toList
        |> List.exists (fun (_, identity) ->
            EntryIdentity.value identity >= nextIndex
            || not (Map.containsKey identity lexicon))
    then
        fail "An store interns a text that names an address it does not hold."

    {
        Lexicon = lexicon
        Table = root
        VocabularyTable = vocabularyRoot
        NextIndex = nextIndex
        TextCache = textCache
        TextIdentities = textCache |> Map.toList |> List.map snd |> Set.ofList
    }

/// Runs the reader and turns a truncated stream into the named corruption
/// failure. A missing file is still the caller's `FileNotFoundException`; only
/// corruption after opening is named here.
let private readStoreChecked (reader: System.IO.BinaryReader) : Store =
    try
        readStore reader
    with :? System.IO.EndOfStreamException ->
        fail "The store is truncated."

let toBytes (store: Store) : byte[] =
    use stream = new System.IO.MemoryStream()
    use writer = new System.IO.BinaryWriter(stream)
    writeStore writer store
    writer.Flush()
    stream.ToArray()

let fromBytes (bytes: byte[]) : Store =
    use stream = new System.IO.MemoryStream(bytes)
    use reader = new System.IO.BinaryReader(stream)
    readStoreChecked reader

/// Writes the store to a temporary file beside the destination and moves it
/// over the destination only once the bytes are written. A failed or
/// interrupted write therefore leaves the previous store readable, instead of
/// truncating it and destroying the last good snapshot.
let writeToFile (path: string) (store: Store) : unit =
    let fullPath = System.IO.Path.GetFullPath path
    let directory = System.IO.Path.GetDirectoryName fullPath

    let temporary =
        System.IO.Path.Combine(directory, (System.IO.Path.GetFileName fullPath) + ".tmp")

    let writeTo (target: string) : unit =
        use stream = System.IO.File.Create(target)
        use writer = new System.IO.BinaryWriter(stream)
        writeStore writer store
        writer.Flush()

    try
        writeTo temporary
        System.IO.File.Move(temporary, fullPath, true)
    with _ ->
        if System.IO.File.Exists temporary then
            System.IO.File.Delete temporary

        reraise ()

let readFromFile (path: string) : Store =
    use stream = System.IO.File.OpenRead(path)
    use reader = new System.IO.BinaryReader(stream)
    readStoreChecked reader

/// The live actor and every actor snapshot, including terminated actors whose
/// promoted entries must survive.
let private epochsOf (state: EvaluationState) : EpochState list =
    let actorEpochs =
        state.SchedulerState.Actors
        |> Map.toList
        |> List.map (fun (_, actor) -> actor.State.Ownership.Epoch)

    state.Ownership.Epoch :: actorEpochs

/// The image with every unpromoted ephemeral entry removed. The allocation
/// frontier and surviving addresses are unchanged, so the result is a sparse
/// image (D53).
let private durableStore (state: EvaluationState) : Store =
    let durable = Epoch.durableEntries (epochsOf state) state.Store

    { state.Store with
        Lexicon =
            state.Store.Lexicon
            |> Map.filter (fun identity _ -> Set.contains identity durable)
        TextCache =
            state.Store.TextCache
            |> Map.filter (fun _ identity -> Set.contains identity durable)
        TextIdentities =
            state.Store.TextIdentities
            |> Set.filter (fun identity -> Set.contains identity durable)
    }

let toBytesForState (state: EvaluationState) : byte[] =
    toBytes (durableStore state)

let writeToFileForState (path: string) (state: EvaluationState) : unit =
    writeToFile path (durableStore state)
