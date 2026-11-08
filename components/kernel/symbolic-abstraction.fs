module SymbolicAbstraction

// ============================================================
// Shared semantic term profile. Action, Query, and Logic all lower into this
// term family; it is not a per-surface format.
// ============================================================

/// The core executable term shape. P1 pins the shape and its portable
/// document; it does not execute or type a term yet.
type Term =
    | LiteralNumber of value: double
    | LiteralText of value: string
    | LiteralAtom of value: string
    | Word of name: string
    | Quote of body: Term list
    | Branch of condition: Term * thenTerm: Term * elseTerm: Term
    | SlotGet of slot: int
    | SlotSet of slot: int
    | MakeClosure of name: string * captures: int list * arity: int
    | CallClosure
    | Fail of reason: string

/// One canonical term program document.
type TermProgram =
    {
        SchemaVersion: int
        Form: string
        Terms: Term list
    }

let schemaVersion = 1
let form = "term-program"
