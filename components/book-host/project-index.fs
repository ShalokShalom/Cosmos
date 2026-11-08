module ProjectIndex

open BookRunner

/// A catalog entry names a library, a project, or a scroll. A catalog is a
/// workspace-wide lookup, never a global build order.
type CatalogKind =
    | Library
    | Project
    | Scroll

type CatalogEntry =
    {
        Name: string
        Kind: CatalogKind
        Path: string
        Version: string
        Capabilities: string list
    }

type Catalog =
    {
        Entries: CatalogEntry list
    }

type CatalogError =
    | DuplicateCatalogEntry of string

let describeCatalogError (error: CatalogError) : string =
    match error with
    | DuplicateCatalogEntry name -> "the catalog contains " + name + " twice"

let empty: Catalog = { Entries = [] }

let entries (catalog: Catalog) : CatalogEntry list =
    catalog.Entries

let tryFind (name: string) (catalog: Catalog) : CatalogEntry option =
    catalog.Entries |> List.tryFind (fun entry -> entry.Name = name)

let add (entry: CatalogEntry) (catalog: Catalog) : Result<Catalog, CatalogError> =
    if catalog.Entries |> List.exists (fun candidate -> candidate.Name = entry.Name) then
        Error(DuplicateCatalogEntry entry.Name)
    else
        Ok { Entries = catalog.Entries @ [ entry ] }

/// Derives the catalog entry for a loaded book. A spell is a project; a book
/// with no index is a scroll; an indexed book without `main` is a library.
let ofBook (book: LoadedBook) : CatalogEntry =
    let kind =
        match classify book with
        | Spell -> Project
        | Book when book.Opened.IsEmpty -> Scroll
        | Book -> Library

    {
        Name = book.Package.Name
        Kind = kind
        Path = book.Path
        Version = book.Package.Version
        Capabilities = book.Package.Capabilities
    }

/// The ordered directory of a library's scrolls and books. It is the same
/// ordering rule as a book index, but it has no root and no `main`. Its entries
/// are the library's `include` declarations.
type Directory =
    {
        Name: string
        Entries: IndexEntry list
    }

let directory
    (name: string)
    (indexEntries': IndexEntry list)
    : Result<Directory, IndexError> =
    indexEntries indexEntries'
    |> Result.map (fun index -> { Name = name; Entries = index.Entries })
