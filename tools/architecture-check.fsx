open System
open System.IO
open System.Text

// The architecture source is EDN. This tool parses the small EDN subset used by
// docs/architecture/architecture.edn, validates it, and generates the Typst views. The
// generated files are committed; --check proves they are current.

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

#load "edn.fsx"
open Edn

let private validateCell (field: string) (value: string) =
    if value.Contains "]" then
        failwithf "EDN %s contains ']' which would close a Typst table cell" field

let private rawCell (value: string) =
    "[#raw(\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")]"

let private textCell (value: string) =
    validateCell "cell" value
    "[" + value + "]"

let private readSource () =
    let path = Path.Combine(repoRoot, "docs", "architecture", "architecture.edn")
    File.ReadAllText path |> parseEdn

let private selfTest () =
    let fixture = "{:a 1 :b [\"x\" :k] :c {:d true}}"

    match parseEdn fixture with
    | Map pairs ->
        if asNumber (get "a" pairs) <> 1.0 then failwith "EDN self-test: number"
        match getVector "b" pairs with
        | [ Text "x"; Keyword "k" ] -> ()
        | _ -> failwith "EDN self-test: vector"
        let nested = get "c" pairs |> asMap
        if get "d" nested <> Bool true then failwith "EDN self-test: nested map"
    | _ -> failwith "EDN self-test: map"

    printfn "EDN parser self-test: OK"

type Node =
    {
        Id: string
        Kind: string
        Title: string
        Code: string
        Role: string
        Doc: string
    }

type Architecture =
    {
        Nodes: Node list
        Edges: (string * string * string * string) list
        Features: (string * string * string * string) list
    }

let private loadArchitecture () : Architecture =
    let root = readSource () |> asMap

    if asNumber (get "schema" root) <> 1.0 then
        failwith "docs/architecture/architecture.edn: unsupported schema"

    let nodes =
        getVector "nodes" root
        |> List.mapi (fun index value ->
            let pairs = asMap value
            let id = getKeyword "id" pairs
            let kind = getKeyword "kind" pairs
            let title = getText "title" pairs
            let code = getText "code" pairs
            let role = getText "role" pairs
            let doc = getText "doc" pairs

            if not (File.Exists(Path.Combine(repoRoot, doc))) then
                failwithf "architecture node %s names a missing doc: %s" id doc

            validateCell ("node " + id) role

            {
                Id = id
                Kind = kind
                Title = title
                Code = code
                Role = role
                Doc = doc
            })

    let nodeIds =
        nodes
        |> List.map (fun node -> node.Id)
        |> Set.ofList

    if Set.count nodeIds <> List.length nodes then
        failwith "architecture nodes have duplicate ids"

    let edges =
        getVector "edges" root
        |> List.map (fun value ->
            let pairs = asMap value
            let fromId = getKeyword "from" pairs
            let toId = getKeyword "to" pairs
            let kind = getKeyword "kind" pairs
            let rationale = getText "rationale" pairs

            if not (nodeIds.Contains fromId) then
                failwithf "architecture edge from unknown node: %s" fromId

            if not (nodeIds.Contains toId) then
                failwithf "architecture edge to unknown node: %s" toId

            validateCell "edge rationale" rationale
            fromId, kind, toId, rationale)

    let spec = get "spec" root |> asMap

    let features =
        getVector "features" spec
        |> List.map (fun value ->
            let pairs = asMap value
            let id = getText "id" pairs
            let title = getText "title" pairs
            let status = getText "status" pairs
            let evidence = getText "evidence" pairs

            validateCell ("feature " + id) title
            validateCell ("feature " + id) status
            validateCell ("feature " + id) evidence
            id, title, status, evidence)

    if features |> List.map (fun (id, _, _, _) -> id) |> List.distinct |> List.length <> List.length features then
        failwith "architecture specification features have duplicate ids"

    { Nodes = nodes
      Edges = edges
      Features = features }

let private generateTable (architecture: Architecture) =
    let builder = StringBuilder()
    builder.AppendLine("// Generated from docs/architecture/architecture.edn. Do not edit.") |> ignore
    builder.AppendLine("#table(") |> ignore
    builder.AppendLine("  columns: (auto, auto, 1fr, auto),") |> ignore
    builder.AppendLine("  align: (left, left, left, left),") |> ignore
    builder.AppendLine("  table.header([Canonical], [Code], [Role], [Documentation]),") |> ignore

    for node in architecture.Nodes do
        builder.AppendLine("  " + rawCell node.Title + ", " + rawCell node.Code + ", " + textCell node.Role + ", " + rawCell node.Doc + ",") |> ignore

    builder.AppendLine(")") |> ignore
    builder.ToString()

let private generateGraph (architecture: Architecture) =
    let builder = StringBuilder()
    builder.AppendLine("// Generated from docs/architecture/architecture.edn. Do not edit.") |> ignore
    builder.AppendLine("#table(") |> ignore
    builder.AppendLine("  columns: (auto, auto, auto, 1fr),") |> ignore
    builder.AppendLine("  align: (left, left, left, left),") |> ignore
    builder.AppendLine("  table.header([From], [Kind], [To], [Rationale]),") |> ignore

    for (fromId, kind, toId, rationale) in architecture.Edges do
        builder.AppendLine("  " + rawCell fromId + ", " + rawCell kind + ", " + rawCell toId + ", " + textCell rationale + ",") |> ignore

    builder.AppendLine(")") |> ignore
    builder.AppendLine() |> ignore
    builder.AppendLine("```text") |> ignore

    for (fromId, kind, toId, _) in architecture.Edges do
        builder.AppendLine(fromId + " -> " + toId + " : " + kind) |> ignore

    builder.AppendLine("```") |> ignore
    builder.ToString()

let private generateStatus (architecture: Architecture) =
    let builder = StringBuilder()
    builder.AppendLine("// Generated from docs/architecture/architecture.edn. Do not edit.") |> ignore
    builder.AppendLine("#table(") |> ignore
    builder.AppendLine("  columns: (3fr, 1.4fr, 3fr),") |> ignore
    builder.AppendLine("  align: (left, left, left),") |> ignore
    builder.AppendLine("  table.header([Feature], [Status], [Evidence / note]),") |> ignore

    for (_, title, status, evidence) in architecture.Features do
        builder.AppendLine("  " + textCell title + ", " + textCell status + ", " + textCell evidence + ",") |> ignore

    builder.AppendLine(")") |> ignore
    builder.ToString()

let private generatedFiles (architecture: Architecture) =
    [
        "docs/generated/architecture-table.typ", generateTable architecture
        "docs/generated/architecture-graph.typ", generateGraph architecture
        "docs/generated/specification-status.typ", generateStatus architecture
    ]

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
    selfTest ()
    let architecture = loadArchitecture ()
    let files = generatedFiles architecture
    let write = arguments |> Array.contains "--write"

    if write then
        for (path, content) in files do
            let fullPath = Path.Combine(repoRoot, path)
            File.WriteAllText(fullPath, content)
            printfn "wrote %s" path

        printfn "architecture-check: wrote %d view(s)" (List.length files)
    else
        let mutable failed = false

        for (path, content) in files do
            let fullPath = Path.Combine(repoRoot, path)

            if not (File.Exists fullPath) then
                eprintfn "architecture-check: missing generated view %s" path
                failed <- true
            else
                let actual = File.ReadAllText fullPath

                if actual.Replace("\r\n", "\n") <> content.Replace("\r\n", "\n") then
                    eprintfn "architecture-check: stale generated view %s" path
                    eprintfn "%s" (firstDifference content actual)
                    failed <- true

        if failed then
            eprintfn "architecture-check: FAIL"
            exit 1
        else
            printfn "architecture-check: OK (%d nodes, %d edges, %d features)" (List.length architecture.Nodes) (List.length architecture.Edges) (List.length architecture.Features)

main fsi.CommandLineArgs.[1..]
