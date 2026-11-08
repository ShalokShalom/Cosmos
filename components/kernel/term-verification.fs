module TermVerification

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open SymbolicAbstraction

/// A path from the whole term list into one term. An empty path is the term
/// list itself; `[0; 1]` is the second term inside the first term's quotation
/// or branch.
type Path = int list

/// A structural refusal from the kernel verifier. `Document` preserves a
/// portable-reader or canonicalization refusal; the other cases carry the term
/// path that failed.
type Error =
    | Document of TermPortable.Error
    | NonFiniteNumber of Path
    | NegativeSlot of Path * int
    | NegativeCapture of Path * int
    | NegativeArity of Path * int
    | EmptyWordName of Path
    | EmptyClosureName of Path

/// The receipt is opaque: the record representation is private, so only
/// `verify` can construct one.
type VerifiedTermProgram =
    private
        { Program: TermProgram
          Digest: string }

[<Literal>]
let MaxBytes = 1048576

[<Literal>]
let ReceiptSchemaVersion = 1

[<Literal>]
let ReceiptForm = "term-receipt"

let program (verified: VerifiedTermProgram) : TermProgram = verified.Program

let digest (verified: VerifiedTermProgram) : string = verified.Digest

let describePath (path: Path) : string =
    "terms"
    + (path
       |> List.map (fun index -> "[" + string index + "]")
       |> String.concat "")

let describeError (error: Error) : string =
    match error with
    | Document documentError -> TermPortable.describeError documentError
    | NonFiniteNumber path -> "term verification: non-finite number at " + describePath path
    | NegativeSlot(path, slot) ->
        "term verification: negative slot " + string slot + " at " + describePath path
    | NegativeCapture(path, capture) ->
        "term verification: negative capture " + string capture + " at " + describePath path
    | NegativeArity(path, arity) ->
        "term verification: negative arity " + string arity + " at " + describePath path
    | EmptyWordName path -> "term verification: empty word name at " + describePath path
    | EmptyClosureName path -> "term verification: empty closure name at " + describePath path

let rec verifyTerm (path: Path) (term: Term) : Result<unit, Error> =
    match term with
    | LiteralNumber value when not (Double.IsFinite value) -> Error(NonFiniteNumber path)
    | LiteralNumber _ -> Ok()
    | LiteralText _ -> Ok()
    | LiteralAtom _ -> Ok()
    | Word name when String.IsNullOrWhiteSpace name -> Error(EmptyWordName path)
    | Word _ -> Ok()
    | Quote terms -> verifyTerms path terms
    | Branch(condition, thenTerm, elseTerm) -> verifyTerms path [ condition; thenTerm; elseTerm ]
    | SlotGet slot
    | SlotSet slot when slot < 0 -> Error(NegativeSlot(path, slot))
    | SlotGet _
    | SlotSet _ -> Ok()
    | MakeClosure(name, captures, arity) ->
        if String.IsNullOrWhiteSpace name then
            Error(EmptyClosureName path)
        else
            match captures |> List.tryFind (fun capture -> capture < 0) with
            | Some capture -> Error(NegativeCapture(path, capture))
            | None when arity < 0 -> Error(NegativeArity(path, arity))
            | None -> Ok()
    | CallClosure -> Ok()
    | Fail _ -> Ok()

and verifyTerms (path: Path) (terms: Term list) : Result<unit, Error> =
    terms
    |> List.indexed
    |> List.fold
        (fun state (index, term) ->
            match state with
            | Error error -> Error error
            | Ok() -> verifyTerm (path @ [ index ]) term)
        (Ok())

/// Re-checks the structural invariants and returns a receipt over the canonical
/// bytes. Stack effects are not checked; P4 adds the signature table.
let verify (program: TermProgram) : Result<VerifiedTermProgram, Error> =
    match verifyTerms [] program.Terms with
    | Error error -> Error error
    | Ok() ->
        match TermPortable.encode program with
        | Error error -> Error(Document error)
        | Ok canonical ->
            let fingerprint =
                SHA256.HashData canonical
                |> Convert.ToHexString
                |> fun text -> text.ToLowerInvariant()

            Ok
                { Program = program
                  Digest = fingerprint }

/// Decodes a term-program document and verifies it. The digest is over the
/// canonical bytes, so two spellings with the same terms produce one receipt.
let verifyBytes (bytes: byte[]) : Result<VerifiedTermProgram, Error> =
    match TermPortable.decode bytes with
    | Error error -> Error(Document error)
    | Ok program -> verify program

/// The canonical receipt document. The digest binds the exact program bytes;
/// the receipt does not carry a host object.
let receiptBytes (verified: VerifiedTermProgram) : byte[] =
    use stream = new MemoryStream()

    (use writer = new Utf8JsonWriter(stream)
     writer.WriteStartObject()
     writer.WriteNumber("schema", ReceiptSchemaVersion)
     writer.WriteString("form", ReceiptForm)
     writer.WriteString("digest", verified.Digest)
     writer.WriteEndObject()
     writer.Flush())

    stream.ToArray()
