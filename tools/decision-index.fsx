open System
open System.IO
open System.Text
open System.Text.RegularExpressions

// docs/process/decision-records.typ is the append-only history. This tool reads its
// headings, proves the ids are dense, and generates a small index. The index is
// committed; --check proves it is current. Reading one decision is a
// doc_get_section call, not a read of the whole file.

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let private readDecisions () =
    let path = Path.Combine(repoRoot, "docs", "process", "decision-records.typ")
    let source = File.ReadAllText path
    let pattern = Regex("(?m)^== D([0-9]+) --- (.+)$")

    let decisions =
        pattern.Matches source
        |> Seq.cast<Match>
        |> Seq.map (fun matched -> (int matched.Groups.[1].Value, matched.Groups.[2].Value.Trim()))
        |> Seq.sortBy fst
        |> Seq.toList

    if decisions.IsEmpty then
        failwith "decision-index: no decision headings were found"

    let ids = decisions |> List.map fst
    let expected = [ 1 .. List.max ids ]

    if ids <> expected then
        failwith "decision-index: the decision ids are not dense from D1"

    decisions

let private displayTitle (value: string) =
    value.Replace("\\'", "'").Replace("\\\\", "\\")

let private rawCell (value: string) =
    "[#raw(\"" + (displayTitle value).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")]"

let private generate (decisions: (int * string) list) =
    let builder = StringBuilder()
    builder.AppendLine("// Generated from docs/process/decision-records.typ. Do not edit.") |> ignore
    builder.AppendLine("= Decision index") |> ignore
    builder.AppendLine() |> ignore
    builder.AppendLine("#strong[Every decision, oldest first.] Read one section on demand") |> ignore
    builder.AppendLine("with `doc_get_section`; do not read `docs/process/decision-records.typ` end to end.") |> ignore
    builder.AppendLine() |> ignore
    builder.AppendLine("#table(") |> ignore
    builder.AppendLine("  columns: (auto, 1fr),") |> ignore
    builder.AppendLine("  align: (left, left),") |> ignore
    builder.AppendLine("  table.header([Decision], [Title]),") |> ignore

    for (identity, title) in decisions do
        builder.AppendLine("  [D" + string identity + "], " + rawCell title + ",") |> ignore

    builder.AppendLine(")") |> ignore
    builder.ToString()

let private firstDifference (expected: string) (actual: string) =
    let expectedLines = expected.Replace("\r\n", "\n").Split('\n')
    let actualLines = actual.Replace("\r\n", "\n").Split('\n')
    let limit = min expectedLines.Length actualLines.Length
    let mutable line = 0
    let mutable found = false

    while not found && line < limit do
        if expectedLines.[line] <> actualLines.[line] then
            found <- true
        else
            line <- line + 1

    if line < limit then
        "line " + string (line + 1) + "\n  expected: " + expectedLines.[line] + "\n  actual:   " + actualLines.[line]
    elif expectedLines.Length <> actualLines.Length then
        "line count " + string expectedLines.Length + " vs " + string actualLines.Length
    else
        "no difference"

let private main (arguments: string[]) =
    let decisions = readDecisions ()
    let generated = generate decisions
    let path = Path.Combine(repoRoot, "docs", "process", "decision-index.typ")
    let write = arguments |> Array.contains "--write"

    if write then
        File.WriteAllText(path, generated)
        printfn "decision-index: wrote docs/process/decision-index.typ (%d decisions)" (List.length decisions)
    elif not (File.Exists path) then
        eprintfn "decision-index: missing docs/process/decision-index.typ"
        exit 1
    else
        let actual = File.ReadAllText path

        if actual.Replace("\r\n", "\n") <> generated.Replace("\r\n", "\n") then
            eprintfn "decision-index: stale docs/process/decision-index.typ"
            eprintfn "%s" (firstDifference generated actual)
            exit 1
        else
            printfn "decision-index: OK (%d decisions)" (List.length decisions)

main fsi.CommandLineArgs.[1..]
