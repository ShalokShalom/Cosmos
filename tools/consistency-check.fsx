// Cross-checks the facts the compiler and the tests cannot see:
//   * the ABI version agrees across the header, the F# literal, the adapter,
//     and the adapter README;
//   * the image schema version agrees between the loader and the adapter;
//   * the pinned suite counts agree with every current-status document;
//   * the decision-record ids are dense and unique, and the specification
//     references the last one;
//   * docs/ remains Typst-only, and every Typst document is linked from the
//     index;
//   * every explicit Compile item names a file that exists.
//
// Run with: dotnet fsi tools/consistency-check.fsx

open System
open System.IO
open System.Text.RegularExpressions

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let read (relativePath: string) : string =
    File.ReadAllText(Path.Combine(repoRoot, relativePath))

let pathExists (relativePath: string) : bool =
    let full = Path.Combine(repoRoot, relativePath)
    File.Exists full || Directory.Exists full

let failures = ResizeArray<string>()
let warnings = ResizeArray<string>()

let fail (message: string) : unit = failures.Add message
let warn (message: string) : unit = warnings.Add message

let matchGroups (pattern: string) (text: string) : string list option =
    let matched = Regex.Match(text, pattern)

    if not matched.Success then
        None
    else
        Some [ for index in 1 .. matched.Groups.Count - 1 -> matched.Groups.[index].Value ]

/// Reads one captured integer, or records why it could not be read.
let requireInt (label: string) (pattern: string) (text: string) : int option =
    match matchGroups pattern text with
    | Some [ value ] ->
        match Int32.TryParse value with
        | true, parsed -> Some parsed
        | false, _ ->
            fail (label + ": '" + value + "' is not an integer")
            None
    | Some values ->
        fail (label + ": expected one capture, found " + string values.Length)
        None
    | None ->
        fail (label + ": the expected statement was not found")
        None

let requireCounts (label: string) (pattern: string) (text: string) : int list option =
    match matchGroups pattern text with
    | Some values ->
        let parsed = values |> List.map Int32.TryParse

        if parsed |> List.exists (fun (ok, _) -> not ok) then
            fail (label + ": a captured count is not an integer")
            None
        else
            Some(parsed |> List.map snd)
    | None ->
        fail (label + ": the expected count statement was not found")
        None

let expectEqual (label: string) (expected: int) (actual: int) : unit =
    if expected <> actual then
        fail (label + ": expected " + string expected + ", found " + string actual)

let expectAllEqual (label: string) (values: int list) : unit =
    match values with
    | [] -> fail (label + ": no values to compare")
    | first :: rest ->
        for value in rest do
            expectEqual label first value

// --- ABI version ------------------------------------------------------------

let header = read "components/kernel/scale_abi.h"

let abiVersion =
    requireInt "scale_abi.h SCALE_ABI_VERSION" "#define SCALE_ABI_VERSION ([0-9]+)" header

let abiSource = read "components/kernel/abi.fs"
let abiSourceVersion = requireInt "abi.fs Version" "let Version = ([0-9]+)" abiSource

let adapterHeader = read "components/adapter-c/scale_adapter.h"

let adapterVersion =
    requireInt
        "scale_adapter.h SCALE_EXPECTED_ABI_VERSION"
        "#define SCALE_EXPECTED_ABI_VERSION ([0-9]+)"
        adapterHeader

let adapterReadme = read "components/adapter-c/README.md"

let adapterReadmeVersion =
    requireInt
        "adapter-c README ABI version"
        "Targets Scale ABI version ([0-9]+)"
        adapterReadme

match abiVersion, abiSourceVersion, adapterVersion, adapterReadmeVersion with
| Some headerValue, Some sourceValue, Some adapterValue, Some readmeValue ->
    expectAllEqual "ABI version" [ headerValue; sourceValue; adapterValue; readmeValue ]
| _ -> ()

// --- Image and term schemas -------------------------------------------------

let imageSource = read "components/kernel/prelude-image.fs"

let imageSchema =
    requireInt
        "prelude-image schema"
        "if document\\.SchemaVersion <> ([0-9]+) then"
        imageSource

let adapterReadmeSchema =
    requireInt "adapter-c README image schema" "image whose schema is not ([0-9]+)" adapterReadme

match imageSchema, adapterReadmeSchema with
| Some imageValue, Some readmeValue -> expectEqual "image schema version" imageValue readmeValue
| _ -> ()

let termSource = read "components/kernel/symbolic-abstraction.fs"

let termSchema = requireInt "symbolic-abstraction schema" "let schemaVersion = ([0-9]+)" termSource

match imageSchema, termSchema with
| Some imageValue, Some termValue ->
    if imageValue <> termValue then
        warn (
            "the image schema is "
            + string imageValue
            + " and the term schema is "
            + string termValue
            + "; confirm that is intentional"
        )
| _ -> ()

// --- Suite counts -----------------------------------------------------------

let testsSource = read "components/verification/tests/tests.fs"
let expectedChecks = requireInt "tests.fs expectedChecks" "let expectedChecks = ([0-9]+)" testsSource

let granularSource = read "components/verification/tests/granular-tests.fs"

let expectedGranular =
    requireInt
        "granular-tests.fs expectedGranularChecks"
        "let expectedGranularChecks = ([0-9]+)"
        granularSource

let snapshotCount =
    requireInt "granular-tests.fs snapshotCount" "let snapshotCount = ([0-9]+)" granularSource

let integrationCount =
    requireInt "granular-tests.fs integrationCount" "let integrationCount = ([0-9]+)" granularSource

match expectedGranular, snapshotCount, integrationCount with
| Some granular, Some snapshots, Some integrations ->
    let writerChecks = granular - snapshots - integrations

    if writerChecks < 0 then
        fail "granular counts: snapshots plus integrations exceed the pinned total"
| _ -> ()

let specification = read "docs/specification.typ"

let specificationExampleCounts =
    requireCounts
        "docs/specification.typ suite counts"
        "The suite at this state is ([0-9]+) example checks \\(([0-9]+) examples, ([0-9]+) properties\\)"
        specification

let specificationGranularCounts =
    requireCounts
        "docs/specification.typ granular counts"
        "([0-9]+) granular\\s+cases \\(([0-9]+) snapshots, ([0-9]+) integrations, ([0-9]+) writer checks\\)"
        specification

let checkExampleCounts (label: string) (counts: int list option) : unit =
    match counts, expectedChecks with
    | Some [ total; examples; properties ], Some pinned ->
        expectEqual (label + " total") pinned total
        expectEqual (label + " examples plus properties") total (examples + properties)
    | Some values, _ -> fail (label + ": expected three counts, found " + string values.Length)
    | None, _ -> ()

let checkGranularCounts (label: string) (counts: int list option) : unit =
    match counts, expectedGranular, snapshotCount, integrationCount with
    | Some [ total; snapshots; integrations; writers ], Some expected, Some expectedSnapshots, Some expectedIntegrations ->
        expectEqual (label + " total") expected total
        expectEqual (label + " snapshots") expectedSnapshots snapshots
        expectEqual (label + " integrations") expectedIntegrations integrations
        expectEqual (label + " writer checks") (expected - expectedSnapshots - expectedIntegrations) writers
    | Some values, _, _, _ ->
        fail (label + ": expected four counts, found " + string values.Length)
    | None, _, _, _ -> ()

checkExampleCounts "docs/specification.typ" specificationExampleCounts
checkGranularCounts "docs/specification.typ" specificationGranularCounts

let testStrategy = read "docs/process/test-strategy.typ"

checkExampleCounts
    "docs/process/test-strategy.typ"
    (requireCounts
        "docs/process/test-strategy.typ suite counts"
        "([0-9]+) checks, ([0-9]+) examples\\s+and ([0-9]+) Hedgehog properties"
        testStrategy)

checkGranularCounts
    "docs/process/test-strategy.typ"
    (requireCounts
        "docs/process/test-strategy.typ granular counts"
        "([0-9]+) cases, ([0-9]+)\\s+Verify snapshots, ([0-9]+) integrations, and ([0-9]+) JIT-only writer checks"
        testStrategy)

let criticalReview = read "docs/process/critical-review.typ"

checkExampleCounts
    "docs/process/critical-review.typ"
    (requireCounts
        "docs/process/critical-review.typ suite counts"
        "([0-9]+) example checks \\(([0-9]+) examples and ([0-9]+)\\s+properties\\)"
        criticalReview)

match requireInt "docs/process/critical-review.typ granular total" "([0-9]+) granular cases" criticalReview, expectedGranular with
| Some documented, Some expected -> expectEqual "docs/process/critical-review.typ granular total" expected documented
| _ -> ()

let ledger = read "docs/process/commitments-ledger.typ"

checkExampleCounts
    "docs/process/commitments-ledger.typ"
    (requireCounts
        "docs/process/commitments-ledger.typ suite counts"
        "Suite: \\*([0-9]+) checks\\* \\(([0-9]+) examples and ([0-9]+) properties\\)"
        ledger)

let agents = read "AGENTS.md"

checkExampleCounts
    "AGENTS.md"
    (requireCounts
        "AGENTS.md suite counts"
        "([0-9]+) checks \\(([0-9]+) examples,\\s+([0-9]+) properties\\)"
        agents)

checkGranularCounts
    "AGENTS.md"
    (requireCounts
        "AGENTS.md granular counts"
        "([0-9]+) granular\\s+cases \\(([0-9]+) snapshots,\\s+([0-9]+) integrations,\\s+([0-9]+) writer checks\\)"
        agents)

let justfile = read "justfile"

checkExampleCounts
    "justfile"
    (requireCounts
        "justfile suite counts"
        "Expect ([0-9]+) checks \\(([0-9]+) examples,\\s+([0-9]+) properties\\)"
        justfile)

checkGranularCounts
    "justfile"
    (requireCounts
        "justfile granular counts"
        "([0-9]+) granular\\s+cases \\(([0-9]+) snapshots,\\s+([0-9]+) integrations,\\s+([0-9]+) writer checks\\)"
        justfile)

// --- Decision records -------------------------------------------------------

let decisions = read "docs/process/decision-records.typ"

let decisionIds =
    Regex.Matches(decisions, "(?m)^== D([0-9]+) ")
    |> Seq.map (fun matched -> Int32.Parse matched.Groups.[1].Value)
    |> Seq.toList

if decisionIds.IsEmpty then
    fail "docs/process/decision-records.typ: no decision headings were found"
else
    let highest = List.max decisionIds

    for identity in 1..highest do
        let occurrences = decisionIds |> List.filter ((=) identity) |> List.length

        if occurrences <> 1 then
            fail (
                "docs/process/decision-records.typ: D"
                + string identity
                + " appears "
                + string occurrences
                + " times (expected once)"
            )

    let reference =
        requireInt
            "docs/specification.typ decision range"
            "D1--D([0-9]+)"
            specification

    match reference with
    | Some documented -> expectEqual "docs/specification.typ decision range" highest documented
    | None -> ()

// --- License ---------------------------------------------------------------

for licenseFile in [ "LICENSE"; "LICENSE-docs" ] do
    if not (pathExists licenseFile) then
        fail ("the repository is missing " + licenseFile)

// --- Dispatcher and signature coverage --------------------------------------

let evaluatorSource = read "components/prelude/evaluator/evaluator.fs"
let dispatchStart = evaluatorSource.IndexOf("and executeBuiltinWord")
let dispatchEnd = evaluatorSource.IndexOf("// Main loop", dispatchStart)

if dispatchStart < 0 || dispatchEnd < 0 then
    fail "components/prelude/evaluator/evaluator.fs: could not locate executeBuiltinWord"
else
    let dispatchRegion = evaluatorSource.Substring(dispatchStart, dispatchEnd - dispatchStart)

    let dispatchedWords =
        Regex.Matches(dispatchRegion, "\\| \"([^\"]+)\" ->")
        |> Seq.map (fun matched -> matched.Groups.[1].Value)
        |> Seq.distinct
        |> Seq.sort
        |> Seq.toList

    let signaturesSource = read "components/prelude/evaluator/signatures.fs"

    let classifiedWords =
        Regex.Matches(signaturesSource, "(?m)^\\s*\\(\"([^\"]+)\",")
        |> Seq.map (fun matched -> matched.Groups.[1].Value)
        |> Seq.toList

    let duplicateClassifications =
        classifiedWords
        |> List.countBy id
        |> List.filter (fun (_, occurrences) -> occurrences > 1)

    for word, occurrences in duplicateClassifications do
        fail ("PreludeSignatures.classification lists '" + word + "' " + string occurrences + " times")

    let dispatchedSet = Set.ofList dispatchedWords
    let classifiedSet = Set.ofList classifiedWords

    for word in Set.difference dispatchedSet classifiedSet do
        fail ("the dispatcher has an unclassified built-in word: " + word)

    for word in Set.difference classifiedSet dispatchedSet do
        fail ("PreludeSignatures.classification names a word the dispatcher does not handle: " + word)

// --- Token lowering and classification --------------------------------------

let loweringSource = read "components/kernel/lowered-instructions.fs"
let loweringStart = loweringSource.IndexOf("let lowerTokens")

if loweringStart < 0 then
    fail "components/kernel/lowered-instructions.fs: could not locate lowerTokens"
else
    let loweringRegion = loweringSource.Substring(loweringStart)

    let loweredWords =
        Regex.Matches(loweringRegion, "\\| \"([^\"]+)\" ->")
        |> Seq.map (fun matched -> matched.Groups.[1].Value)
        |> Seq.distinct
        |> Seq.sort
        |> Seq.toList

    let classifiedWords =
        Regex.Matches(read "components/prelude/evaluator/signatures.fs", "(?m)^\\s*\\(\"([^\"]+)\",")
        |> Seq.map (fun matched -> matched.Groups.[1].Value)
        |> Set.ofSeq

    for word in loweredWords do
        if not (classifiedWords.Contains word) then
            fail ("the token lowerer emits an unclassified word: " + word)

// --- Documentation index and format ----------------------------------------

let docsRoot = Path.Combine(repoRoot, "docs")

let markdownFiles =
    Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)
    |> Seq.append (Directory.EnumerateFiles(docsRoot, "*.txt", SearchOption.AllDirectories))
    |> Seq.map (fun path -> Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
    |> Seq.sort
    |> Seq.toList

for path in markdownFiles do
    fail ("docs/ must stay Typst-only; move the non-canonical note to Notes/: " + path)

let index = read "docs/README.typ"

let linkedPaths =
    Regex.Matches(index, "`([^`]+)`")
    |> Seq.map (fun matched -> matched.Groups.[1].Value)
    |> Seq.filter (fun path ->
        not (path.Contains("<"))
        && [ "docs/"; "components/"; "bases/"; "development/"; "projects/"; "adapters/"; "tools/" ]
           |> List.exists path.StartsWith)
    |> Seq.distinct
    |> Seq.toList

for path in linkedPaths do
    if not (pathExists path) then
        fail ("docs/README.typ links a path that does not exist: " + path)

let typstDocuments =
    Directory.EnumerateFiles(docsRoot, "*.typ", SearchOption.AllDirectories)
    |> Seq.map (fun path -> Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
    |> Seq.filter (fun path -> path <> "docs/README.typ")
    |> Seq.sort
    |> Seq.toList

for path in typstDocuments do
    if not (List.contains path linkedPaths) then
        fail ("docs/README.typ does not index: " + path)

// --- Explicit Compile items -------------------------------------------------

let projectFiles =
    [ "components"; "bases"; "development"; "projects" ]
    |> List.collect (fun root ->
        let directory = Path.Combine(repoRoot, root)

        if Directory.Exists directory then
            Directory.EnumerateFiles(directory, "*.fsproj", SearchOption.AllDirectories)
            |> Seq.filter (fun path ->
                not (
                    Path.GetRelativePath(repoRoot, path)
                        .Replace('\\', '/')
                        .StartsWith("components/nu-engine/")
                ))
            |> Seq.toList
        else
            [])

for projectPath in projectFiles do
    let projectDirectory = Path.GetDirectoryName projectPath
    let projectText = File.ReadAllText projectPath

    for matched in Regex.Matches(projectText, "<Compile Include=\"([^\"]+)\"") do
        let includePath = matched.Groups.[1].Value
        let resolved = Path.GetFullPath(Path.Combine(projectDirectory, includePath))

        if not (File.Exists resolved) then
            fail (
                Path.GetRelativePath(repoRoot, projectPath).Replace('\\', '/')
                + " compiles a file that does not exist: "
                + includePath
            )

// Package versions live in one place. A local Version attribute or a package
// absent from Directory.Packages.props is a drift the compiler will not report
// until two projects disagree.
let centralPackages =
    Regex.Matches(read "Directory.Packages.props", "<PackageVersion Include=\"([^\"]+)\"")
    |> Seq.map (fun matched -> matched.Groups.[1].Value)
    |> Seq.toList

// The vendored engine keeps its own pins; the root central-package policy must
// be disabled inside it or restoring any Nu project would fail with NU1008.
// The engine is absent by design in the sparse Scale-code workspace, so the
// policy is checked only when the Nu project itself is present.
let nuEngineProject = "components/nu-engine/Nu/Nu/Nu.fsproj"
let nuEnginePackagePolicy = "components/nu-engine/Directory.Packages.props"

if pathExists nuEngineProject then
    if not (pathExists nuEnginePackagePolicy) then
        fail ("the vendored Nu engine needs its own package policy: " + nuEnginePackagePolicy)
    elif not ((read nuEnginePackagePolicy).Contains("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>")) then
        fail (nuEnginePackagePolicy + " must set ManagePackageVersionsCentrally to false")

for projectPath in projectFiles do
    let projectText = File.ReadAllText projectPath

    for matched in Regex.Matches(projectText, "<PackageReference Include=\"([^\"]+)\" Version=\"") do
        fail (
            Path.GetRelativePath(repoRoot, projectPath).Replace('\\', '/')
            + " pins "
            + matched.Groups.[1].Value
            + " locally; use Directory.Packages.props"
        )

    for matched in Regex.Matches(projectText, "<PackageReference Include=\"([^\"]+)\"") do
        let packageName = matched.Groups.[1].Value

        if not (List.contains packageName centralPackages) then
            fail (
                Path.GetRelativePath(repoRoot, projectPath).Replace('\\', '/')
                + " uses "
                + packageName
                + " without a Directory.Packages.props entry"
            )

// --- Report -----------------------------------------------------------------

for message in warnings do
    printfn "warning: %s" message

if failures.Count > 0 then
    for message in failures do
        eprintfn "consistency-check: %s" message

    eprintfn "consistency-check: FAIL (%d problem(s))" failures.Count
    exit 1

printfn
    "consistency-check: OK (ABI %s, %s checks, %s granular, %d decisions)"
    (abiVersion |> Option.map string |> Option.defaultValue "?")
    (expectedChecks |> Option.map string |> Option.defaultValue "?")
    (expectedGranular |> Option.map string |> Option.defaultValue "?")
    decisionIds.Length
