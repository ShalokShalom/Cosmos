open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

#load "edn.fsx"
open Edn

// Builds the machine-readable specification projection. The normative prose
// remains docs/specification.typ; this file only projects facts that already
// have an authority elsewhere.

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let read (relativePath: string) : string =
    File.ReadAllText(Path.Combine(repoRoot, relativePath))

let requireMatch (label: string) (pattern: string) (text: string) : string =
    let matched = Regex.Match(text, pattern)

    if matched.Success then
        matched.Groups.[1].Value
    else
        failwith (label + ": expected statement was not found")

let requireInt (label: string) (pattern: string) (text: string) : int =
    requireMatch label pattern text |> Int32.Parse

let escape (value: string) : string =
    value.Replace("\\", "\\\\").Replace("\"", "\\\"")

let quoted (value: string) : string = "\"" + escape value + "\""

let writeStrings (builder: StringBuilder) (indent: string) (values: string list) : unit =
    builder.Append("[").Append(String.concat " " (values |> List.map quoted)).Append("]") |> ignore

let architecture = read "docs/architecture/architecture.edn"
let typeSystem = read "docs/types/type-system.edn"
let signatures = read "components/prelude/evaluator/signatures.fs"
let abiHeader = read "components/kernel/scale_abi.h"
let imageSource = read "components/kernel/prelude-image.fs"
let tests = read "components/verification/tests/tests.fs"

let abiVersion = requireInt "ABI version" "#define SCALE_ABI_VERSION ([0-9]+)" abiHeader
let imageSchema = requireInt "image schema" "SchemaVersion <> ([0-9]+)" imageSource
let expectedChecks = requireInt "suite count" "let expectedChecks = ([0-9]+)" tests

let architectureRoot = Edn.parse architecture |> Edn.asMap

let features =
    Edn.get "spec" architectureRoot
    |> Edn.asMap
    |> fun spec -> Edn.get "features" spec |> Edn.asVector
    |> List.map (fun feature ->
        let fields = Edn.asMap feature
        Edn.getText "id" fields, Edn.getText "title" fields, Edn.getText "status" fields)

let dependencies =
    Edn.get "edges" architectureRoot
    |> Edn.asVector
    |> List.map (fun edge ->
        let fields = Edn.asMap edge
        Edn.getKeyword "from" fields, Edn.getKeyword "to" fields)

let typeRoot = Edn.parse typeSystem |> Edn.asMap

let typeLayers =
    Edn.get "layers" typeRoot
    |> Edn.asVector
    |> List.map (fun layer -> Edn.asMap layer |> Edn.getKeyword "id")

let rec private collectPropertyIds (value: Edn) : string list =
    match value with
    | Edn.Text text when Regex.IsMatch(text, "^[LRVSK][0-9]+$") -> [ text ]
    | Edn.Map pairs -> pairs |> List.collect (fun (_, item) -> collectPropertyIds item)
    | Edn.Vector items
    | Edn.List items -> items |> List.collect collectPropertyIds
    | _ -> []

let typeProperties =
    Edn.get "properties" typeRoot |> collectPropertyIds

let wordLines = signatures.Split('\n')

let wordCategory (line: string) : (string * string) option =
    let matched = Regex.Match(line, "^\\s*\\(\"([^\"]+)\",\\s*(notInferred|Expression|stack)")

    if matched.Success then
        let category =
            match matched.Groups.[2].Value with
            | "Expression" -> "expression"
            | "stack" -> "stack"
            | _ -> "not-inferred"

        Some(matched.Groups.[1].Value, category)
    else
        None

let words = wordLines |> Array.choose wordCategory |> Array.toList

let sourceHash =
    [ architecture; typeSystem; signatures; abiHeader; imageSource; tests ]
    |> String.concat "\n"
    |> Encoding.UTF8.GetBytes
    |> SHA256.HashData
    |> Convert.ToHexString
    |> fun value -> value.ToLowerInvariant()

let render () : string =
    let builder = StringBuilder()
    builder.AppendLine("{:schema 1") |> ignore
    builder.AppendLine(" :spec/id \"scale-specification\"") |> ignore
    builder.AppendLine(" :authority") |> ignore
    builder.AppendLine(" {:normative \"docs/specification.typ\"") |> ignore
    builder.AppendLine("  :architecture \"docs/architecture/architecture.edn\"") |> ignore
    builder.AppendLine("  :types \"docs/types/type-system.edn\"") |> ignore
    builder.AppendLine("  :decisions \"docs/process/decision-index.typ\"}") |> ignore
    builder.AppendLine(" :generated") |> ignore
    builder.AppendLine(" {:tool \"tools/spec-model.fsx\"") |> ignore
    builder.AppendLine("  :source-hash " + quoted sourceHash + "}") |> ignore
    builder.AppendLine(" :facts") |> ignore
    builder.AppendLine(" {:abi {:version " + string abiVersion + " :image-schema " + string imageSchema + "}") |> ignore
    builder.AppendLine("  :words") |> ignore
    builder.AppendLine("  {:expression") |> ignore
    builder.Append("   ") |> ignore
    writeStrings builder "" (words |> List.filter (fun (_, category) -> category = "expression") |> List.map fst)
    builder.AppendLine() |> ignore
    builder.AppendLine("   :stack") |> ignore
    builder.Append("   ") |> ignore
    writeStrings builder "" (words |> List.filter (fun (_, category) -> category = "stack") |> List.map fst)
    builder.AppendLine() |> ignore
    builder.AppendLine("   :not-inferred") |> ignore
    builder.Append("   ") |> ignore
    writeStrings builder "" (words |> List.filter (fun (_, category) -> category = "not-inferred") |> List.map fst)
    builder.AppendLine() |> ignore
    builder.AppendLine("   :counts {:total " + string words.Length + "}}") |> ignore
    builder.AppendLine("  :types") |> ignore
    builder.AppendLine("  {:layers") |> ignore
    builder.Append("   ") |> ignore
    writeStrings builder "" typeLayers
    builder.AppendLine() |> ignore
    builder.AppendLine("   :properties") |> ignore
    builder.Append("   ") |> ignore
    writeStrings builder "" typeProperties
    builder.AppendLine("}") |> ignore
    builder.AppendLine("  :features") |> ignore
    builder.AppendLine("  [") |> ignore

    for (id, title, status) in features do
        builder.AppendLine("   {:id " + quoted id + " :title " + quoted title + " :status " + quoted status + "}") |> ignore

    builder.AppendLine("  ]") |> ignore
    builder.AppendLine("  :dependencies") |> ignore
    builder.AppendLine("  [") |> ignore

    for (fromNode, toNode) in dependencies do
        builder.AppendLine("   {:from :" + fromNode + " :to :" + toNode + "}") |> ignore

    builder.AppendLine("  ]") |> ignore
    builder.AppendLine("  :tests {:expected-checks " + string expectedChecks + "}}") |> ignore
    builder.AppendLine(" :claims {:authority \"docs/specification.typ\" :generated false}}") |> ignore
    builder.ToString()

let target = Path.Combine(repoRoot, "docs/generated/specification.edn")

let main (arguments: string[]) : int =
    let generated = render ()

    if arguments |> Array.contains "--write" then
        File.WriteAllText(target, generated)
        printfn "spec-model: wrote docs/generated/specification.edn"
        0
    elif arguments |> Array.contains "--check" then
        if not (File.Exists target) then
            printfn "spec-model: docs/generated/specification.edn is missing; run --write"
            1
        elif File.ReadAllText target <> generated then
            printfn "spec-model: docs/generated/specification.edn is stale; run --write"
            1
        else
            printfn "spec-model: OK"
            0
    elif arguments |> Array.contains "--self-test" then
        let first = render ()
        let second = render ()

        if first = second && first.Contains ":abi {:version" then
            printfn "spec-model: self-test OK"
            0
        else
            printfn "spec-model: self-test failed"
            1
    else
        printfn "usage: spec-model.fsx --write | --check | --self-test"
        2

exit (main fsi.CommandLineArgs)
