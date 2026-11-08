module LoweredInstructions

// ============================================================
// Shared lowering profile. There is one instruction target for Action, Query,
// and Logic; surface-specific decisions are made before this module.
// ============================================================

// ============================================================
// The small lowered instruction set for P1.
//
// This is the opcode seam the SRM writer will fold over: literals, local slots,
// branches, late-bound word calls with an optional cache guard, apply, and a
// named failure. No language construct is matched here; lowering has already
// made every decision.
// ============================================================

type Slot = int
type LabelId = int

[<Literal>]
let ClosureTag = -7.0

/// The tag of a code value: `[CodeValueTag; documentId]`. It lives in the kernel
/// because the kernel now makes code values itself (`MakeCode`, D202).
[<Literal>]
let CodeValueTag = -34.0

/// A cached call target and the epoch that validates it. An empty target means
/// the call is not cached and falls back to the dispatcher.
type CallGuard =
    {
        Target: string
        Epoch: int
    }

/// How a lowered artifact relates to the interpreter's event stream. A program
/// that contains a `RecordStep` reports `HistoryRecorded` and can replay the
/// positions it recorded; a program without one reports `HistoryExcluded` and
/// pays nothing. Recording is the emitter's choice (D201, superseding D120), and
/// `scopeOf` answers from the program itself. A record is a position only, so it
/// is a resume point rather than a rollback point.
type HistoryScope =
    | HistoryExcluded
    | HistoryRecorded

let historyScope : HistoryScope = HistoryExcluded

type Instruction =
    | LiteralNumber of value: double
    | LiteralText of value: string
    | PushSlot of slot: Slot
    | StoreSlot of slot: Slot
    | CallWord of name: string * guard: CallGuard option
    | Apply
    | Duplicate
    | Swap
    | Drop
    | Over
    | Rotate
    | PushRoot of name: string
    | SetRoot of name: string
    | LookupPair of root: string
    | AddPair of root: string
    | DefineWord
    | MakeClosure of name: string * captures: Slot list * arity: int
    | CallClosure
    | PushCapture of index: int
    /// Makes a code value from its body (D202). The body is the only nested
    /// instruction list in the set: it is the instruction counterpart of the
    /// interpreter's `Quote` term, and its document uses the image's own opcode
    /// spelling.
    | MakeCode of body: Instruction list
    | TensorRank
    | TensorSize
    | TensorItem
    | TensorEntry
    | TextRead
    | TextEquals
    | IsText
    | Add
    | Subtract
    | Multiply
    | Divide
    | LessThan
    | GreaterThan
    | Equals
    | Jump of target: LabelId
    | JumpIfFalse of target: LabelId
    | Label of label: LabelId
    /// Records an executed position in the state's history (D201, superseding D120). It has no
    /// stack effect, so a program with records produces the same value as one
    /// without, and `LoweredAbstraction.runFrom` can resume at a recorded
    /// position.
    | RecordStep of step: int
    | Fail of reason: string

/// The scope a program actually has: a program that contains a step record can
/// replay its history, one that does not cannot (D201, superseding D120).
let scopeOf (instructions: Instruction list) : HistoryScope =
    if instructions |> List.exists (function RecordStep _ -> true | _ -> false) then
        HistoryRecorded
    else
        HistoryExcluded

/// The token-lowering slice: numbers become literals and every other token is
/// a late-bound word call. Expression lowering lives in the Prelude.
let lowerTokens (tokens: string list) : Instruction list =
    tokens
    |> List.map (fun token ->
        match token with
        | "duplicate" -> Duplicate
        | "swap" -> Swap
        | "drop" -> Drop
        | "over" -> Over
        | "rotate" -> Rotate
        | "add" -> Add
        | "subtract" -> Subtract
        | "multiply" -> Multiply
        | "divide" -> Divide
        | "lessThan" -> LessThan
        | "greaterThan" -> GreaterThan
        | "equals" -> Equals
        | _ ->
            match System.Double.TryParse token with
            | true, value -> LiteralNumber value
            | false, _ -> CallWord(token, None))
