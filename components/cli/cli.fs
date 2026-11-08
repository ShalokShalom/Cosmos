module Cli

/// Which part of the process to run. The suite modes are handled by the
/// JIT-only `scale-tests` artifact; the product binary handles the rest and
/// refuses the suite modes by name. `--fixed-spec` runs the fixed-spec gate
/// against the demonstration specification, writes a repair bundle when the
/// specification is falsified, and returns its exit code: 0 when the fixed
/// specification holds, 1 when it is falsified or the run is inconclusive, 2
/// when the run cannot start.
type RunMode =
    | ExampleSuite
    | GranularSuite
    | GranularCount
    | BenchmarkRun
    | AllTests
    | FixedSpecificationRun
    | TableRun of path: string
    | DiagnoseRun of sentence: string
    | ProseFile of path: string
    | ProseTypecheck of path: string
    | BookCheck of path: string
    | BookRun of path: string
    | CastRun of path: string * word: string * arguments: string list
    | WireListen of path: string
    | WireConnect of path: string * message: string
    | Showcase

let private valueAfter (flag: string) (arguments: string[]) : string =
    match arguments |> Array.tryFindIndex ((=) flag) with
    | Some index when index + 1 < arguments.Length -> arguments.[index + 1]
    | _ -> ""

let private valuesAfter (flag: string) (count: int) (arguments: string[]) : string list =
    match arguments |> Array.tryFindIndex ((=) flag) with
    | Some index ->
        [ for offset in 1 .. count do
              if index + offset < arguments.Length then
                  yield arguments.[index + offset] ]
    | None -> []

let private argumentsAfter (flag: string) (arguments: string[]) : string list =
    match arguments |> Array.tryFindIndex ((=) flag) with
    | Some index -> [ for offset in index + 1 .. arguments.Length - 1 -> arguments.[offset] ]
    | None -> []

/// Parses the command line. `--fixed-spec` is explicit and wins over the suite
/// flags; `--table <path>` parses and checks a table document; `--diagnose
/// <sentence>` runs the parser diagnostic, repair and presentation path;
/// `--prose <path>` reads and runs a prose program; `--wire-listen <path>` and
/// `--wire-connect <path> <number>` run the first actor IPC slice; `--all-tests`
/// runs every kind once and prints the nature-based summary; `--granular-count`
/// prints the number of granular cases without running them; `--granular-tests`
/// runs the fine-grained development suite; `--tests` runs only the example
/// suite; anything else (or nothing) runs the product showcase.
let parseArguments (arguments: string[]) : RunMode =
    if arguments |> Array.contains "--fixed-spec" then
        FixedSpecificationRun
    elif arguments |> Array.contains "--table" then
        TableRun(valueAfter "--table" arguments)
    elif arguments |> Array.contains "--diagnose" then
        DiagnoseRun(valueAfter "--diagnose" arguments)
    elif arguments |> Array.contains "--prose-typecheck" then
        ProseTypecheck(valueAfter "--prose-typecheck" arguments)
    elif arguments |> Array.contains "--check" then
        BookCheck(valueAfter "--check" arguments)
    elif arguments |> Array.contains "--prose" then
        ProseFile(valueAfter "--prose" arguments)
    elif arguments |> Array.contains "--book" then
        BookRun(valueAfter "--book" arguments)
    elif arguments |> Array.contains "--cast" then
        match valuesAfter "--cast" 2 arguments with
        | [ path; word ] -> CastRun(path, word, argumentsAfter "--cast" arguments |> List.skip 2)
        | [ path ] -> CastRun(path, "", [])
        | _ -> CastRun("", "", [])
    elif arguments |> Array.contains "--wire-listen" then
        WireListen(valueAfter "--wire-listen" arguments)
    elif arguments |> Array.contains "--wire-connect" then
        match valuesAfter "--wire-connect" 2 arguments with
        | [ path; message ] -> WireConnect(path, message)
        | _ -> WireConnect("", "")
    elif arguments |> Array.contains "--benchmark" then
        BenchmarkRun
    elif arguments |> Array.contains "--all-tests" then
        AllTests
    elif arguments |> Array.contains "--granular-count" then
        GranularCount
    elif arguments |> Array.contains "--granular-tests" then
        GranularSuite
    elif arguments |> Array.contains "--tests" then
        ExampleSuite
    else
        Showcase

// ============================================================
// Potential extensions (not commitments)
// - Unified `.scale` mode selection: choose Action, Query, or Logic explicitly.
// - Watch mode: rerun the selected surface after source changes.
// - Structured JSON output: automate results in CI and editors.
// - Multi-file package build: compile a complete book tree.
// - Shell completion and generated help: lower CLI entry friction.
// ============================================================
