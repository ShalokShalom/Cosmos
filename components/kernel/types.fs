namespace Types

[<AutoOpen>]
module Core =
    /// A distinct reference into the store lexicon. Host encoding of an address,
    /// not a host integer: it projects to a rank-0 tensor.
    [<Struct>]
    type EntryIdentity = EntryIdentity of int

    module EntryIdentity =
        /// Unwrap to the underlying integer, for structural boundaries only
        /// (Shape, IndexTransformation, persistence).
        let value (EntryIdentity i) : int = i
        let ofInt (i: int) : EntryIdentity = EntryIdentity i

    /// The only element type of the cell value domain. A scalar is a rank-0 tensor.
    type RealNumber = double

    /// A tensor is a multidimensional array.
    type Tensor = { Data: RealNumber[]; Shape: int[] }

    /// An opaque host-issued token. The constructor is private; only the host
    /// resource table can mint one. Language code has no constructor, accessor,
    /// or dereference operation for a token.
    [<Struct>]
    type HostToken = private HostToken of uint64

    module HostToken =
        let create (identifier: uint64) : HostToken = HostToken identifier
        let value (HostToken identifier) : uint64 = identifier

    /// The lexicon maps store references to tensors.
    type Lexicon = Map<EntryIdentity, Tensor>

    /// The default reduction budget.
    [<Literal>]
    let DefaultReductions = 2000

    /// The store holds the lexicon and metadata.
    type Store =
        {
            Lexicon: Lexicon
            Table: EntryIdentity option
            VocabularyTable: EntryIdentity option
            NextIndex: int
            TextCache: Map<string, EntryIdentity>
            /// The identities registered as text. An inverse index for
            /// `readTextAt`, so "is this entry text?" stays logarithmic instead
            /// of scanning the whole `TextCache`.
            TextIdentities: Set<EntryIdentity>
        }
