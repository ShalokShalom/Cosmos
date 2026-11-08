module DecisionTable

// Decision models, prepared but optional (D13).
//
// A decision table is an ordered list of rows; a row is a conjunction of guards
// and an outcome. The core already had both halves: `ScaleFile.RefinementGuard` with
// `Refiner.evaluateRefinementGuard` answers a guard at runtime, and `Smt.refinementGuardToBooleanExpression`
// encodes it for the solver. Nothing joined them into a table. This module is
// that join.
//
// Nothing here is wired into the parser, the vocabulary, the emitter, or the
// demo. A domain opts in by constructing a `Table` and calling `check` and
// `select`; a domain that never does is unaffected. That is what "optional"
// means here: the setup exists, and the default path does not touch it.
//
// Two shapes were possible (see docs/decision-models.typ):
//   1. N guarded rows plus a completeness obligation  <- this module
//   2. a conditional verb body, which would need `Expression.Ite`
// Shape 1 was chosen because it reuses the guards and the solver that already
// exist, and keeps the implementation fragment small (D10).

open Types
open Microsoft.Z3

/// How a table resolves more than one matching row.
type HitPolicy =
    /// Exactly one row may match an input. `check` proves disjointness.
    | Unique
    /// The first matching row wins. Overlap is legal and resolved by order.
    | First
    /// The matching row with the most guards wins. Ties keep table order.
    /// This is structural specificity; `Refiner`'s coverage ranking is the
    /// solver-backed version used for candidate synthesis.
    | Priority

/// One row: a conjunction of guards, and the outcome it selects.
type Row<'outcome> =
    {
        Guards: ScaleFile.RefinementGuard list
        Outcome: 'outcome
    }

/// A table over a named domain. `Rows` is ordered, and the order is significant
/// for `First` and for `Priority` ties.
type Table<'outcome> =
    {
        Name: string
        Inputs: (string * DomainExpression.FieldType) list
        Policy: HitPolicy
        Rows: Row<'outcome> list
    }

/// The pure answer for one subject. `Ambiguous` only arises for a `Unique`
/// table that was never checked, or was checked and found unsound.
type Selection<'outcome> =
    | Selected of index: int * row: Row<'outcome>
    | NoMatch
    | Ambiguous of indices: int list

/// Why a table is not sound, with the input that proves it.
type Finding =
    /// No row's guards hold for this input.
    | UncoveredInput of witness: Smt.SolverAssignment
    /// Two rows hold for this input, which `Unique` forbids.
    | OverlappingRows of first: int * second: int * witness: Smt.SolverAssignment

/// The structural verdict. `Undecided` is reported, never promoted to `Sound`.
type Verdict =
    | Sound
    | Unsound of Finding list
    | Undecided of reason: string

/// The row the table selects for a subject, with its index. Pure: it never
/// calls the solver. `NoMatch` means no row matches, which `check` rules out for
/// a sound table.
let select
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (table: Table<'outcome>)
    (subject: 'T)
    : Selection<'outcome> =
    let matching =
        table.Rows
        |> List.indexed
        |> List.filter (fun (_, row) ->
            row.Guards
            |> List.forall (fun guard -> Refiner.evaluateRefinementGuard getField guard subject))

    match matching with
    | [] -> NoMatch
    | [ single ] -> Selected single
    | many ->
        match table.Policy with
        | Unique -> Ambiguous(List.map fst many)
        | First -> Selected(List.head many)
        | Priority ->
            many
            |> List.sortBy (fun (index, row) -> -(List.length row.Guards), index)
            |> List.head
            |> Selected

/// A table selection as one diagnostic value. `Selected` is not a failure;
/// `NoMatch` is a coverage gap of the table, and `Ambiguous` is a table-design
/// fault. This adapter lets a caller report the table; it does not wire the
/// table into the CLI or the diagnostic sink.
let explain (table: Table<'outcome>) (selection: Selection<'outcome>) : Violations.Violation option =
    let make (rule: string) (expected: string) (given: string) (blame: Violations.Blame) : Violations.Violation =
        {
            Layer = Violations.Sortal
            Rule = rule
            Span =
                {
                    Token = table.Name
                    Start = None
                    End = None
                }
            Expected = Some expected
            Given = Some given
            Blame = blame
            Witness = None
            Suggestion = None
        }

    match selection with
    | Selected _ -> None
    | NoMatch -> Some(make "DecisionTableNoMatch" "a matching row" "no row" Violations.CoverageGap)
    | Ambiguous indices ->
        let given = indices |> List.map string |> String.concat ", "
        Some(make "DecisionTableAmbiguous" "one matching row" given Violations.Input)

/// One row as an SMT obligation: the row's guards are the refinements of a
/// capability whose body is the outcome's implementation. Discharging
/// `guards ∧ ¬body` proves the row on its own inputs.
let obligationFor (table: Table<'outcome>) (row: Row<'outcome>) : Smt.Obligation =
    {
        Pattern = Capability
        Conditions = []
        Refinements =
            row.Guards
            |> List.map (fun guard ->
                {
                    ScaleFile.Guard = guard
                    Origin = "decision table " + table.Name
                    Counterexample = ""
                })
    }

/// Every row as an obligation, in table order.
let obligations (table: Table<'outcome>) : Smt.Obligation list =
    table.Rows |> List.map (obligationFor table)

/// A conjunction of guards as a Z3 boolean expression. An empty conjunction is
/// `true`, which is the correct reading of a row with no guards.
let private conjunction
    (domain: Smt.DomainEncoding)
    (guards: ScaleFile.RefinementGuard list)
    : Result<BoolExpr, string> =
    guards
    |> List.map (Smt.refinementGuardToBooleanExpression domain)
    |> List.fold
        (fun state next ->
            state
            |> Result.bind (fun expressions -> next |> Result.map (fun expression -> expression :: expressions)))
        (Ok [])
    |> Result.map (fun expressions ->
        let context = domain.Context

        match expressions with
        | [] -> context.MkTrue()
        | _ -> context.MkAnd(List.toArray expressions))

let private disjunction (domain: Smt.DomainEncoding) (formulas: BoolExpr list) : BoolExpr =
    let context = domain.Context

    match formulas with
    | [] -> context.MkFalse()
    | _ -> context.MkOr(List.toArray formulas)

let private pairs (count: int) : (int * int) list =
    [
        for first in 0 .. count - 2 do
            for second in first + 1 .. count - 1 do
                yield first, second
    ]

let private checkWith (domain: Smt.DomainEncoding) (table: Table<'outcome>) : Result<Verdict, string> =
    let context = domain.Context

    let encodedRows =
        table.Rows
        |> List.mapi (fun index row ->
            conjunction domain row.Guards
            |> Result.mapError (fun reason -> $"Row {index}: {reason}"))

    match
        encodedRows
        |> List.tryPick (function
            | Error reason -> Some reason
            | Ok _ -> None)
    with
    | Some reason -> Error reason
    | None ->
        let expressions =
            encodedRows
            |> List.choose (function
                | Ok expression -> Some expression
                | Error _ -> None)

        let coverage = disjunction domain expressions

        let coverageFinding =
            match Smt.satisfiable domain (context.MkNot coverage) with
            | Smt.Satisfiable witness -> Ok(Some(UncoveredInput witness))
            | Smt.Unsatisfiable -> Ok None
            | Smt.Undecided reason -> Error reason

        match coverageFinding with
        | Error reason -> Ok(Undecided reason)
        | Ok coverageFinding ->
            let overlapFindings =
                match table.Policy with
                | First
                | Priority -> Ok []
                | Unique ->
                    pairs (List.length expressions)
                    |> List.fold
                        (fun state (first, second) ->
                            state
                            |> Result.bind (fun findings ->
                                let overlap = context.MkAnd(expressions.[first], expressions.[second])

                                match Smt.satisfiable domain overlap with
                                | Smt.Satisfiable witness -> Ok(OverlappingRows(first, second, witness) :: findings)
                                | Smt.Unsatisfiable -> Ok findings
                                | Smt.Undecided reason -> Error reason))
                        (Ok [])

            match overlapFindings with
            | Error reason -> Ok(Undecided reason)
            | Ok overlaps ->
                let findings = (coverageFinding |> Option.toList) @ List.rev overlaps

                if List.isEmpty findings then
                    Ok Sound
                else
                    Ok(Unsound findings)

/// Checks a table against its hit policy:
///
///   * every input must select a row (no gaps), for every policy;
///   * `Unique` tables must also have pairwise disjoint rows, so exactly one
///     row matches any input.
///
/// An `Error` means the table could not be encoded (an unknown field, or a
/// threshold outside the integer theory). `Unsound` carries a concrete witness
/// for each defect; `Undecided` means the solver gave up, and is never read as
/// `Sound`.
let check (table: Table<'outcome>) : Result<Verdict, string> =
    let domain = Smt.createDomain table.Inputs

    try
        checkWith domain table
    finally
        domain.Dispose()
