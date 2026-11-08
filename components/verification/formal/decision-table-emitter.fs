module DecisionTableEmitter

// ============================================================
// Promoting decision tables (D73): one emitted property per row.
//
// The emitter takes one body per outcome, not one body for the table. Each row
// becomes its own module and property, with the row's guards as the
// precondition and `rowN` as the body the host domain supplies. The table-wide
// completeness proof stays with `DecisionTable.check`; this module renders the
// row obligations, it does not replace the completeness check.
// ============================================================

open Types
open DecisionTable

let private refinement (table: Table<'outcome>) (guard: ScaleFile.RefinementGuard) : ScaleFile.Refinement =
    {
        Guard = guard
        Origin = "decision table " + table.Name
        Counterexample = ""
    }

/// Renders one framework-free property module per row. The caller supplies one
/// body per outcome through `tableBinding.Body`; the emitted artifact assumes
/// the host module (in `binding.Opens`) supplies `row0`, `row1`, and so on.
/// Re-emitting the same table is byte-identical.
let emit
    (binding: Emitter.DomainBinding)
    (subject: string)
    (verb: string)
    (body: 'outcome -> DomainExpression.Expression)
    (table: Table<'outcome>)
    : string =
    table.Rows
    |> List.mapi (fun index row ->
        let rowBody = body row.Outcome

        let rowBinding =
            { binding with
                ModuleName = binding.ModuleName + "Row" + string index
                VerbName = Emitter.Fixed("row" + string index)
                GeneratedRecord = false
                DomainDefault = false
                Smt = binding.Smt |> Option.map (fun smt -> { smt with VerbBody = rowBody })
            }

        let sentence: ParsedSentence =
            {
                Pattern = Capability
                Subject = subject
                Conditions = []
                Verb = verb
            }

        let refinements = row.Guards |> List.map (refinement table)

        Emitter.emitFSharp rowBinding sentence ("decision table " + table.Name) refinements)
    |> String.concat "\n\n"
