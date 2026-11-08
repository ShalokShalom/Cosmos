module Diagnostics

// ============================================================
// The common diagnostic sink (P0--P5).
//
// A sink is an ordered list of violations. Callers add parser, semantic, SMT,
// discourse or decision-table violations; the presentation layer renders them.
// The sink is pure: `add` returns a new sink, so it can be threaded through a
// run without hidden state.
// ============================================================

open Violations

type Sink = { Violations: Violation list }

let empty: Sink = { Violations = [] }

let add (violation: Violation) (sink: Sink) : Sink =
    {
        Violations = violation :: sink.Violations
    }

let addOption (violation: Violation option) (sink: Sink) : Sink =
    match violation with
    | Some value -> add value sink
    | None -> sink

/// Adds a table selection failure through `DecisionTable.explain`. A selected
/// row is not a failure and leaves the sink unchanged.
let addSelection
    (table: DecisionTable.Table<'outcome>)
    (selection: DecisionTable.Selection<'outcome>)
    (sink: Sink)
    : Sink =
    DecisionTable.explain table selection
    |> fun violation -> addOption violation sink

let count (sink: Sink) : int = List.length sink.Violations

/// Oldest first, which is the order the stages reported.
let violations (sink: Sink) : Violation list = List.rev sink.Violations

let render (sink: Sink) : string list =
    violations sink |> List.map Presentation.render

let renderTechnical (sink: Sink) : string list =
    violations sink |> List.map Presentation.renderTechnical
