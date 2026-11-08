module EvaluatorProgram

// ============================================================
// The P3b token-to-term boundary.
//
// The runtime holds `Program: Term list` in P3b. This module is the one place
// that turns the edge's tokens into core terms, executes the two active term
// shapes (a number pushes a scalar tensor, a word dispatches), and projects the
// terms back for the text-based control words, the token-vector history, and
// the kernel bridge. P3c replaces quotation text with tagged term documents;
// P3d removes the token text path from execution. `toKernelTokens` remains only
// for surface clause grammars and the kernel bridge.
// ============================================================

open System
open System.Globalization
open Types
open SymbolicAbstraction
open EvaluatorCommon

type Error =
    | NonFiniteNumber of token: string

let termName (term: Term) : string =
    match term with
    | LiteralNumber _ -> "number"
    | LiteralText _ -> "text"
    | LiteralAtom _ -> "atom"
    | Word _ -> "word"
    | Quote _ -> "quotation"
    | Branch _ -> "branch"
    | SlotGet _ -> "slot-get"
    | SlotSet _ -> "slot-set"
    | MakeClosure _ -> "make-closure"
    | CallClosure -> "call-closure"
    | Fail _ -> "fail"

let describeError (error: Error) : string =
    match error with
    | NonFiniteNumber token -> "term boundary: non-finite number " + token

let parseToken (token: string) : Result<Term, Error> =
    let mutable value = 0.0

    if Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, &value) then
        if Double.IsFinite value then
            Ok(LiteralNumber value)
        else
            Error(NonFiniteNumber token)
    else
        Ok(Word token)

/// The numeric reading both boundaries share: a finite number, a refusal for a
/// non-finite one, and `None` for a word.
let private numberToken (token: string) : Result<double option, Error> =
    let mutable value = 0.0

    if Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, &value) then
        if Double.IsFinite value then Ok(Some value) else Error(NonFiniteNumber token)
    else
        Ok None

/// One token as one instruction (D202): a finite number is a literal, every other
/// token is a late-bound word call. These two shapes are the whole token surface.
let parseInstruction (token: string) : Result<LoweredInstructions.Instruction, Error> =
    numberToken token
    |> Result.map (fun parsed ->
        match parsed with
        | Some value -> LoweredInstructions.LiteralNumber value
        | None -> LoweredInstructions.CallWord(token, None))

/// Maps edge tokens to the instruction encoding. It is the counterpart of
/// `ofTokens`, and it is what the interpreter's program becomes (W1b).
let ofTokensAsInstructions
    (tokens: string list)
    : Result<LoweredInstructions.Instruction list, Error> =
    tokens
    |> List.fold
        (fun state token ->
            match state with
            | Error error -> Error error
            | Ok instructions ->
                parseInstruction token
                |> Result.map (fun instruction -> instruction :: instructions))
        (Ok [])
    |> Result.map List.rev

/// Maps edge tokens to core terms. A finite numeric token is a literal; every
/// other token is a word. Quotation grouping is P3b/P3c work.
let ofTokens (tokens: string list) : Result<Term list, Error> =
    tokens
    |> List.fold
        (fun state token ->
            match state with
            | Error error -> Error error
            | Ok terms -> parseToken token |> Result.map (fun term -> term :: terms))
        (Ok [])
    |> Result.map List.rev

/// The external-text edge. The parser splits text into tokens and then uses the
/// same token boundary as every other entry.
let ofText (text: string) : Result<Term list, Error> = ofTokens (splitText text)

/// The total construction form for a program whose tokens are known to be
/// executable. A non-finite number is a programmer invariant failure at a
/// construction site, never a silently dropped program.
let ofTokensOrFail (tokens: string list) : Term list =
    match ofTokens tokens with
    | Ok terms -> terms
    | Error error -> invalidOp (describeError error)

/// The projection used by a surface token grammar (pattern clauses and method
/// signatures) and the kernel bridge. It is not a stored quotation form.
let rec toKernelTokens (terms: Term list) : string list =
    terms
    |> List.collect (fun term ->
        match term with
        | LiteralNumber value -> [ value.ToString("R", CultureInfo.InvariantCulture) ]
        | Word name -> [ name ]
        | Quote body -> "{" :: toKernelTokens body @ [ "}" ]
        | other -> invalidOp ("term projection: cannot project a " + termName other + " term to a token"))

/// Executes one active term. P3b executes only the two shapes its constructors
/// produce: a number pushes a scalar tensor without reparsing text, and a word
/// dispatches. Any other term in the active program is an invariant violation;
/// P3c adds `Quote` execution.
let executeTerm
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (term: Term)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match term with
    | LiteralNumber value ->
        let (identity, newStore) = Store.addTensor (Tensor.scalar value) state.Store

        Ok
            { state with
                Store = newStore
                Stack = Stack.push identity state.Stack
            }
    | Word name -> executeWord name state
    | other -> invalidOp ("term execution: cannot execute a " + termName other + " term")

/// Executes one instruction of the token surface: a literal pushes a scalar, a
/// word call dispatches, and `MakeCode` stores its body as a code value. It is
/// the instruction counterpart of `executeTerm` (D202), and a test pins that the
/// two boundaries read a token the same way.
let executeInstruction
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (instruction: LoweredInstructions.Instruction)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match instruction with
    | LoweredInstructions.LiteralNumber value ->
        let (identity, newStore) = Store.addTensor (Tensor.scalar value) state.Store

        Ok
            { state with
                Store = newStore
                Stack = Stack.push identity state.Stack
            }
    | LoweredInstructions.CallWord(name, None) -> executeWord name state
    | LoweredInstructions.MakeCode body ->
        let (identity, newStore) = EvaluatorCode.encodeInstructions body state.Store

        Ok
            { state with
                Store = newStore
                Stack = Stack.push identity state.Stack
            }
    | _ -> invalidOp "instruction execution: this instruction is not on the token surface"
