open System
open System.Diagnostics
open System.IO
open System.Text

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let outputPath = Path.Combine(repoRoot, "docs", "generated", "line-counts.typ")

let runScc (arguments: string list) : string =
    let startInfo = ProcessStartInfo("scc")
    startInfo.WorkingDirectory <- repoRoot
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true

    for argument in arguments do
        startInfo.ArgumentList.Add(argument)

    use sccProcess = Process.Start(startInfo)
    let output = sccProcess.StandardOutput.ReadToEnd()
    let error = sccProcess.StandardError.ReadToEnd()
    sccProcess.WaitForExit()

    if sccProcess.ExitCode <> 0 then
        failwithf "scc failed: %s" error

    output

let commonArguments =
    [
        "--no-cocomo"
        "--exclude-dir"
        "bin,obj,.git,.idea,.fleet,node_modules,nu-engine"
        "--format"
        "csv"
    ]

type FileCount =
    {
        Path: string
        Lines: int
        Code: int
        Complexity: int
    }

type Total = { Lines: int; Code: int; Files: int }

let isHandRolledTestFile (row: FileCount) : bool = Path.GetFileName(row.Path) = "tests.fs"

let isTestHarnessFile (row: FileCount) : bool =
    List.contains
        (Path.GetFileName(row.Path))
        [ "tests.fs"; "granular-tests.fs"; "properties.fs"; "differential.fs"; "test-runner.fs" ]

let isToolRow (row: FileCount) : bool =
    row.Path.StartsWith("tools/", StringComparison.Ordinal)
    || row.Path.StartsWith("components/prelude/writer/", StringComparison.Ordinal)
    || row.Path.StartsWith(".SageFs/", StringComparison.Ordinal)

let isKernelRow (row: FileCount) : bool =
    row.Path.StartsWith("components/kernel/", StringComparison.Ordinal)

let isDemoRow (row: FileCount) : bool =
    let name = Path.GetFileName(row.Path)

    name = "demo.fs"
    || name = "demo-domain.fs"
    || name = "fixed-specification-acceptance.fs"
    || row.Path.StartsWith("components/demo/", StringComparison.Ordinal)

let isEdgeRow (row: FileCount) : bool =
    List.contains
        (Path.GetFileName(row.Path))
        [
            "decision-table.fs"
            "decision-table-emitter.fs"
            "decision-table-surface.fs"
            "smt.fs"
            "type-lattice.fs"
            "refiner.fs"
            "violations.fs"
            "parser.fs"
            "tokenizer.fs"
            "scale-file.fs"
            "domain-expression.fs"
            "nearest-sentence.fs"
            "discourse.fs"
            "diagnostics.fs"
            "presentation.fs"
            "emitter.fs"
            "vocabulary.fs"
            "vocabulary-list.fs"
            "vocabulary-in-image.fs"
            "persistence.fs"
            "reclaim.fs"
            "arena.fs"
        ]

let parseFileRows (csv: string) : FileCount list =
    csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList
    |> List.tail
    |> List.choose (fun line ->
        let fields = line.Trim().Split(',')

        if fields.Length < 8 then
            None
        else
            Some
                {
                    Path = fields.[1]
                    Lines = int fields.[3]
                    Code = int fields.[4]
                    Complexity = int fields.[7]
                })

let sumRows (rows: FileCount list) : Total =
    rows
    |> List.fold
        (fun total row ->
            {
                Lines = total.Lines + row.Lines
                Code = total.Code + row.Code
                Files = total.Files + 1
            })
        { Lines = 0; Code = 0; Files = 0 }

let render () : string =
    let fsharpRows =
        runScc (commonArguments @ [ "--by-file"; "--include-ext"; "fs,fsx"; "." ])
        |> parseFileRows

    let fsharpTotal = sumRows fsharpRows

    let testRow = fsharpRows |> List.tryFind isHandRolledTestFile

    let moduleRows =
        fsharpRows
        |> List.filter (fun row -> not (isHandRolledTestFile row) && not (isDemoRow row))
        |> List.sortBy (fun row -> -row.Code, row.Path)

    let moduleTotal = sumRows moduleRows

    let kernelTotal = fsharpRows |> List.filter isKernelRow |> sumRows

    let demoTotal = fsharpRows |> List.filter isDemoRow |> sumRows
    let edgeTotal = fsharpRows |> List.filter isEdgeRow |> sumRows

    let preludeTotal =
        fsharpRows
        |> List.filter (fun row ->
            not (isKernelRow row)
            && not (isTestHarnessFile row)
            && not (isToolRow row)
            && not (isDemoRow row)
            && not (isEdgeRow row))
        |> sumRows

    let testHarnessTotal = fsharpRows |> List.filter isTestHarnessFile |> sumRows
    let toolTotal = fsharpRows |> List.filter isToolRow |> sumRows

    let documentationRows =
        runScc (commonArguments @ [ "--by-file"; "--include-ext"; "typ,md"; "." ])
        |> parseFileRows
        // The generated table must not count itself: a first write would change
        // the next reading, so `just line-counts` would need two passes.
        |> List.filter (fun row -> Path.GetFullPath(Path.Combine(repoRoot, row.Path)) <> outputPath)

    let documentationTotal = sumRows documentationRows

    let builder = StringBuilder()
    builder.AppendLine("== Line counts (scc)") |> ignore
    builder.AppendLine() |> ignore

    builder.AppendLine(
        "#strong[Generated by `just line-counts`; do not edit by hand.] `tests.fs` is excluded from the per-module table and reported separately."
    )
    |> ignore

    builder.AppendLine() |> ignore

    builder.AppendLine(
        $"#strong[Implementation (F\\#):] {fsharpTotal.Files} files, {fsharpTotal.Lines} lines, {fsharpTotal.Code} code."
    )
    |> ignore

    match testRow with
    | Some row -> builder.AppendLine($"`tests.fs`: {row.Lines} lines, {row.Code} code.") |> ignore
    | None -> builder.AppendLine("`tests.fs` was not found.") |> ignore

    builder.AppendLine(
        $"#strong[Kernel:] {kernelTotal.Files} files, {kernelTotal.Lines} lines, {kernelTotal.Code} code."
    )
    |> ignore

    builder.AppendLine(
        $"#strong[Prelude core (excluding tests, tools, demo/acceptance, and edge/verification):] {preludeTotal.Files} files, {preludeTotal.Lines} lines, {preludeTotal.Code} code."
    )
    |> ignore

    builder.AppendLine(
        $"#strong[Edge and verification (not part of the initial port):] {edgeTotal.Files} files, {edgeTotal.Lines} lines, {edgeTotal.Code} code."
    )
    |> ignore

    builder.AppendLine(
        $"#strong[Demo and acceptance (not counted as Prelude code):] {demoTotal.Files} files, {demoTotal.Lines} lines, {demoTotal.Code} code."
    )
    |> ignore

    builder.AppendLine(
        $"#strong[Test harness (tests, granular, properties):] {testHarnessTotal.Files} files, {testHarnessTotal.Lines} lines, {testHarnessTotal.Code} code."
    )
    |> ignore

    builder.AppendLine(
        $"#strong[Tools and local scripts:] {toolTotal.Files} files, {toolTotal.Lines} lines, {toolTotal.Code} code."
    )
    |> ignore

    builder.AppendLine(
        "The categories partition the implementation total: kernel, Prelude core, edge/verification, demo/acceptance, test harness, and tools."
    )
    |> ignore

    builder.AppendLine(
        $"The table below excludes `tests.fs` and demo/acceptance: {moduleTotal.Files} files, {moduleTotal.Lines} lines, {moduleTotal.Code} code."
    )
    |> ignore

    builder.AppendLine() |> ignore

    builder.AppendLine(
        $"#strong[Documentation:] {documentationTotal.Files} files, {documentationTotal.Lines} lines, {documentationTotal.Code} code."
    )
    |> ignore

    builder.AppendLine() |> ignore
    builder.AppendLine("#table(") |> ignore
    builder.AppendLine("  columns: (2fr, auto, auto, auto),") |> ignore
    builder.AppendLine("  align: (left, right, right, right),") |> ignore

    builder.AppendLine("  table.header([Module], [Lines], [Code], [Complexity]),")
    |> ignore

    let tableRows =
        moduleRows
        |> List.map (fun row -> $"  [`{row.Path}`], [{row.Lines}], [{row.Code}], [{row.Complexity}]")

    builder.AppendLine(String.Join(",\n", tableRows)) |> ignore
    builder.AppendLine(")") |> ignore
    builder.ToString()

let content = render ()

if Environment.GetCommandLineArgs() |> Array.contains "--check" then
    if File.Exists outputPath && File.ReadAllText(outputPath) = content then
        printfn "line counts are current"
    else
        eprintfn "line counts are stale; run `just line-counts`"
        exit 1
else
    File.WriteAllText(outputPath, content, UTF8Encoding(false))
    printfn "wrote %s" outputPath
