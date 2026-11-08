module EvaluatorCode

// ============================================================
// Quotation code values (P3c).
//
// A quotation is `[QuoteTag; documentId]`. The document id names a text entry
// holding the canonical `term-program` document for the body. The value is code,
// not token text; `decode` returns the term list and refuses a non-code tensor.
// ============================================================

open System.Text
open Types
open SymbolicAbstraction
open TermPortable

type Error =
    | NotCode of address: EntryIdentity
    | InvalidDocument of TermPortable.Error
    | EncodeFailed of TermPortable.Error
    | InvalidInstructionDocument of PreludeImage.ImageError

let describeError (error: Error) : string =
    match error with
    | NotCode address -> "code: value " + string (EntryIdentity.value address) + " is not a quotation"
    | InvalidDocument documentError -> "code: " + TermPortable.describeError documentError
    | EncodeFailed documentError -> "code: " + TermPortable.describeError documentError
    | InvalidInstructionDocument imageError -> "code: " + PreludeImage.describeError imageError

let document (terms: Term list) : TermProgram =
    {
        SchemaVersion = schemaVersion
        Form = form
        Terms = terms
    }

/// Encodes a term list to a code value and its canonical term document.
let encode (terms: Term list) (store: Store) : Result<EntryIdentity * Store, Error> =
    match TermPortable.encode (document terms) with
    | Error error -> Error(EncodeFailed error)
    | Ok bytes ->
        let text = Encoding.UTF8.GetString bytes
        let (documentId, storeWithDocument) = Store.addText text store

        let (codeId, storeWithCode) =
            Store.addTensor
                (Tensor.vector [ QuoteTag; double (EntryIdentity.value documentId) ])
                storeWithDocument

        Ok(codeId, storeWithCode)

/// Decodes a code value. A plain text tensor is not a code value in P3c; only
/// the tagged pair is accepted.
let decode (identity: EntryIdentity) (store: Store) : Result<Term list, Error> =
    match Store.lookupTensor identity store with
    | Some tensor when
        Tensor.rank tensor = 1
        && Tensor.size tensor = 2
        && Tensor.item tensor 0 = QuoteTag
        ->
        let documentId = EntryIdentity.ofInt (int (Tensor.item tensor 1))

        match Store.readTextAt documentId store with
        | None -> Error(NotCode identity)
        | Some text ->
            match TermPortable.decode (Encoding.UTF8.GetBytes text) with
            | Ok program -> Ok program.Terms
            | Error error -> Error(InvalidDocument error)
    | _ -> Error(NotCode identity)

/// Encodes an instruction program as a code value carrying an instruction
/// document (D202, W1a). The value keeps the same `[QuoteTag; documentId]` shape
/// as a term document, so a reader distinguishes them by the document's form.
let encodeInstructions
    (instructions: LoweredInstructions.Instruction list)
    (store: Store)
    : EntryIdentity * Store =
    let text = PreludeImage.toInstructionDocumentJson instructions
    let (documentId, storeWithDocument) = Store.addText text store

    Store.addTensor
        (Tensor.vector [ QuoteTag; double (EntryIdentity.value documentId) ])
        storeWithDocument

/// Decodes a code value whose document is an instruction program. A term
/// document is refused by name rather than misread as instructions.
let decodeInstructions
    (identity: EntryIdentity)
    (store: Store)
    : Result<LoweredInstructions.Instruction list, Error> =
    match Store.lookupTensor identity store with
    | Some tensor when
        Tensor.rank tensor = 1
        && Tensor.size tensor = 2
        && Tensor.item tensor 0 = QuoteTag
        ->
        let documentId = EntryIdentity.ofInt (int (Tensor.item tensor 1))

        match Store.readTextAt documentId store with
        | None -> Error(NotCode identity)
        | Some text ->
            match PreludeImage.fromInstructionDocumentJson text with
            | Ok instructions -> Ok instructions
            | Error error -> Error(InvalidInstructionDocument error)
    | _ -> Error(NotCode identity)
