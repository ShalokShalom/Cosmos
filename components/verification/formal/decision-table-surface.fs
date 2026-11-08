module DecisionTableSurface

// ============================================================
// The Basic English decision-table surface (D74).
//
// A table document is a `table <name>` header, an optional `policy:` line,
// Basic English transition rows, and `end`. Each row's trigger conditions
// become the row guards, its verb becomes the outcome, and its subject becomes
// the table subject. The host still supplies one body per outcome through
// `binding`; this module parses the surface, it does not interpret an outcome.
// ============================================================

open Types

type TableError =
    | MissingTableHeader
    | MissingTableName
    | MissingEnd
    | UnexpectedLine of line: string
    | UnknownPolicy of policy: string
    | DuplicatePolicy
    | EmptyTable
    | RowParseError of reason: string
    | RowNotTransition of line: string
    | RowSubjectMismatch of expected: string * found: string
    | UnsupportedRowCondition of reason: string

type TableDocument =
    {
        Name: string
        Subject: string
        Policy: DecisionTable.HitPolicy
        Rows: DecisionTable.Row<string> list
        Inputs: (string * DomainExpression.FieldType) list
    }

let toTable (document: TableDocument) : DecisionTable.Table<string> =
    {
        Name = document.Name
        Inputs = document.Inputs
        Policy = document.Policy
        Rows = document.Rows
    }

let private guardOfCondition (condition: Condition) : Result<ScaleFile.RefinementGuard list, TableError> =
    match condition with
    | BooleanCondition field -> Ok [ ScaleFile.IsTrue field ]
    | NumericCondition(field, comparison, value) ->
        match comparison with
        | ComparisonGreaterThan -> Ok [ ScaleFile.GreaterThan(field, value) ]
        | ComparisonAtLeast -> Ok [ ScaleFile.AtLeast(field, value) ]
        | ComparisonLessThan -> Ok [ ScaleFile.LessThan(field, value) ]
        | ComparisonAtMost -> Ok [ ScaleFile.AtMost(field, value) ]
        | ComparisonEqualTo ->
            // The guard language has no equality case; equality is the two
            // inequalities together, which stays a conjunction.
            Ok [ ScaleFile.AtLeast(field, value); ScaleFile.AtMost(field, value) ]
    | Disjunction _ -> Error(UnsupportedRowCondition "disjunction")

let private guardType (guard: ScaleFile.RefinementGuard) : DomainExpression.FieldType =
    match guard with
    | ScaleFile.GreaterThan _
    | ScaleFile.AtLeast _
    | ScaleFile.LessThan _
    | ScaleFile.AtMost _ -> DomainExpression.IntegerType
    | ScaleFile.IsTrue _
    | ScaleFile.IsFalse _ -> DomainExpression.BooleanType
    | ScaleFile.NamedGuard _ ->
        // Named guards are not produced from a Basic English row; keep a sort
        // here so a future surface case has a defined type.
        DomainExpression.BooleanType

let private guardField (guard: ScaleFile.RefinementGuard) : string =
    match guard with
    | ScaleFile.GreaterThan(field, _)
    | ScaleFile.AtLeast(field, _)
    | ScaleFile.LessThan(field, _)
    | ScaleFile.AtMost(field, _)
    | ScaleFile.IsTrue field
    | ScaleFile.IsFalse field
    | ScaleFile.NamedGuard(field, _) -> field

let private parsePolicy (text: string) : Result<DecisionTable.HitPolicy, TableError> =
    match text.Trim().ToLowerInvariant() with
    | "unique" -> Ok DecisionTable.Unique
    | "first" -> Ok DecisionTable.First
    | "priority" -> Ok DecisionTable.Priority
    | other -> Error(UnknownPolicy other)

let private parseRow
    (vocabulary: VocabularyEntry list)
    (line: string)
    : Result<DecisionTable.Row<string> * string, TableError> =
    let tokens = Tokenizer.tokenize vocabulary line

    match Parser.parseDetailed vocabulary tokens |> Result.bind Parser.toParsedSentence with
    | Error error -> Error(RowParseError(Parser.describeError error))
    | Ok parsed ->
        if parsed.Pattern <> Transition then
            Error(RowNotTransition line)
        else
            let guards =
                parsed.Conditions
                |> List.fold
                    (fun state condition ->
                        state
                        |> Result.bind (fun accumulated ->
                            guardOfCondition condition |> Result.map (fun next -> accumulated @ next)))
                    (Ok [])

            match guards with
            | Error error -> Error error
            | Ok value ->
                Ok(
                    {
                        Guards = value
                        Outcome = parsed.Verb
                    },
                    parsed.Subject
                )

/// Parses one table document. Blank lines are ignored; `policy:` is optional and
/// defaults to `Unique`. The subject is the first row's subject, and every row
/// must name the same subject. A row that is not a transition, a disjunctive
/// row, an unknown policy, or a missing `end` is a named refusal.
let parse (vocabulary: VocabularyEntry list) (text: string) : Result<TableDocument, TableError> =
    let lines =
        text.Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (fun line -> line <> "")
        |> List.ofArray

    match lines with
    | [] -> Error MissingTableHeader
    | first :: rest ->
        if not (first.StartsWith("table ", System.StringComparison.Ordinal)) then
            Error MissingTableHeader
        else
            let name = first.Substring("table ".Length).Trim()

            if name = "" then
                Error MissingTableName
            else
                let rec loop
                    (policy: DecisionTable.HitPolicy option)
                    (rows: DecisionTable.Row<string> list)
                    (subject: string option)
                    (remaining: string list)
                    : Result<TableDocument, TableError> =
                    match remaining with
                    | [] -> Error MissingEnd
                    | line :: rest ->
                        if line = "end" then
                            if not rest.IsEmpty then
                                Error(UnexpectedLine(List.head rest))
                            elif rows.IsEmpty then
                                Error EmptyTable
                            else
                                let inputs =
                                    rows
                                    |> List.collect (fun row -> row.Guards)
                                    |> List.map (fun guard -> guardField guard, guardType guard)
                                    |> List.distinctBy fst

                                Ok
                                    {
                                        Name = name
                                        Subject = subject |> Option.defaultValue ""
                                        Policy = policy |> Option.defaultValue DecisionTable.Unique
                                        Rows = List.rev rows
                                        Inputs = inputs
                                    }
                        elif line.StartsWith("policy:", System.StringComparison.Ordinal) then
                            if policy.IsSome then
                                Error DuplicatePolicy
                            else
                                match parsePolicy (line.Substring("policy:".Length)) with
                                | Error error -> Error error
                                | Ok value -> loop (Some value) rows subject rest
                        else
                            match parseRow vocabulary line with
                            | Error error -> Error error
                            | Ok(row, rowSubject) ->
                                match subject with
                                | Some expected when expected <> rowSubject ->
                                    Error(RowSubjectMismatch(expected, rowSubject))
                                | _ -> loop policy (row :: rows) (Some rowSubject) rest

                loop None [] None rest

let describeError (error: TableError) : string =
    match error with
    | MissingTableHeader -> "A table must start with 'table <name>'."
    | MissingTableName -> "The table header has no name."
    | MissingEnd -> "The table is missing 'end'."
    | UnexpectedLine line -> "Unexpected line after 'end': " + line
    | UnknownPolicy policy -> "Unknown table policy '" + policy + "'; use unique, first, or priority."
    | DuplicatePolicy -> "The table declares more than one policy."
    | EmptyTable -> "The table has no rows."
    | RowParseError reason -> "A table row is not a Basic English sentence: " + reason
    | RowNotTransition line -> "A table row must be a transition: " + line
    | RowSubjectMismatch(expected, found) -> "A table row subject is '" + found + "', expected '" + expected + "'."
    | UnsupportedRowCondition reason -> "A table row uses an unsupported condition: " + reason + "."
