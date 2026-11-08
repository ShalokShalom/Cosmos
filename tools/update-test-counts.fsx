// Run the example suite, count the granular cases, then update every current
// test-count reference. Counting the granular suite is instant because the
// test tree is static; `just check` still runs the executable checks.
// Historical counts ("suite 275 at W2.1") are deliberately left alone.
//
// Usage:
//   dotnet fsi tools/update-test-counts.fsx --write
//   dotnet fsi tools/update-test-counts.fsx --check

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

type Mode =
    | Write
    | Check

type Counts =
    {
        Total: int
        Examples: int
        Properties: int
        Granular: int
    }

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let mode =
    match fsi.CommandLineArgs |> Array.skip 1 with
    | [| "--write" |] -> Write
    | [| "--check" |] -> Check
    | _ -> failwith "usage: dotnet fsi tools/update-test-counts.fsx --write|--check"

if mode = Write && Environment.GetEnvironmentVariable "SCALE_RELEASE" <> "1" then
    failwith "test counts are release artifacts; run them only through 'just release'"

let run (arguments: string list) =
    let psi = ProcessStartInfo("dotnet")
    arguments |> List.iter psi.ArgumentList.Add
    psi.WorkingDirectory <- root
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    use proc = Process.Start(psi)
    let standardOutput = proc.StandardOutput.ReadToEnd()
    let standardError = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    (proc.ExitCode, standardOutput, standardError)

let stripAnsi (text: string) =
    Regex.Replace(text, "\u001b\\[[0-9;]*[A-Za-z]", "")

let matchOne (pattern: string) (text: string) (what: string) =
    let m = Regex.Match(text, pattern)

    if not m.Success then
        failwithf "could not read %s from the test output" what

    m.Groups.[1].Value |> int

let handExit, handOutputRaw, handError =
    run [ "run"; "--project"; "projects/scale-tests/scale-tests.fsproj"; "--"; "--tests" ]
let handOutput = stripAnsi handOutputRaw

let handPassed = matchOne @"Tests passed: (\d+)" handOutput "the example pass count"

let reportedFailures =
    matchOne @"Tests failed: (\d+)" handOutput "the example failure count"

// `expectedChecks` is the pinned total. When it drifts, the suite reports one
// extra failure ("FAIL: expected X checks, ran Y") but every real check still
// passes. Treat that line as the count update's signal, not as a test failure,
// so a stale pin can be repaired by this script (the bootstrap case).
let countMismatch = Regex.IsMatch(handOutput, @"FAIL: expected \d+ checks, ran \d+")

let handFailed =
    if countMismatch then
        max 0 (reportedFailures - 1)
    else
        reportedFailures

let handTotal = handPassed + handFailed

let properties =
    matchOne @"Properties:\s+(\d+)" handOutput "the property count"

let examples = handTotal - properties

if examples <= 0 then
    failwithf "the example count is not positive: %d - %d" handTotal properties

if handFailed <> 0 then
    failwithf "the example suite has %d failure(s):\n%s%s" handFailed handOutput handError

if handExit <> 0 then
    printfn "note: the example suite exited %d before the count update (expected when expectedChecks is stale)" handExit

let granularExit, granularOutputRaw, granularError =
    run [ "run"; "--project"; "projects/scale-tests/scale-tests.fsproj"; "--"; "--granular-count" ]

let granularOutput = stripAnsi granularOutputRaw

let granularTotal =
    matchOne @"Granular tests: (\d+)" granularOutput "the granular total"

if granularExit <> 0 then
    failwithf "the granular suite failed:\n%s%s" granularOutput granularError

let granularTests =
    File.ReadAllText(Path.Combine(root, "components", "verification", "tests", "granular-tests.fs"))

let snapshotCount =
    matchOne @"let snapshotCount = (\d+)" granularTests "the snapshot count"

let integrationCount =
    matchOne @"let integrationCount = (\d+)" granularTests "the integration count"

let counts =
    {
        Total = handTotal
        Examples = examples
        Properties = properties
        Granular = granularTotal
    }

// The nature total counts every kind once: the example checks and properties
// plus the artifact tests the aggregate gate adds.
let natureTotal =
    counts.Examples + counts.Properties + snapshotCount + integrationCount

printfn
    "counts: %d examples, %d properties, %d snapshots, %d integrations (%d total); %d granular tests"
    counts.Examples
    counts.Properties
    snapshotCount
    integrationCount
    natureTotal
    counts.Granular

let mutable texts = Map.empty<string, string>

let load (file: string) =
    match texts.TryFind file with
    | Some text -> text
    | None ->
        let text = File.ReadAllText(Path.Combine(root, file))
        texts <- texts.Add(file, text)
        text

let store (file: string) (text: string) = texts <- texts.Add(file, text)

let replace (file: string) (pattern: string) (expected: int) (build: Match -> string) =
    let text = load file
    let matches = Regex.Matches(text, pattern)

    if matches.Count <> expected then
        failwithf "%s: pattern matched %d time(s), expected %d: %s" file matches.Count expected pattern

    let updated = Regex.Replace(text, pattern, MatchEvaluator(build))
    store file updated

let replaceInSection
    (file: string)
    (startMarker: string)
    (endMarker: string)
    (pattern: string)
    (expected: int)
    (build: Match -> string)
    =
    let text = load file
    let start = text.IndexOf(startMarker, StringComparison.Ordinal)

    if start < 0 then
        failwithf "%s: missing section %s" file startMarker

    let bodyStart = start + startMarker.Length
    let stop = text.IndexOf(endMarker, bodyStart, StringComparison.Ordinal)

    if stop < 0 then
        failwithf "%s: missing section end %s" file endMarker

    let section = text.Substring(bodyStart, stop - bodyStart)
    let matches = Regex.Matches(section, pattern)

    if matches.Count <> expected then
        failwithf "%s: section pattern matched %d time(s), expected %d: %s" file matches.Count expected pattern

    let updated = Regex.Replace(section, pattern, MatchEvaluator(build))
    store file (text.Substring(0, bodyStart) + updated + text.Substring(stop))

// The pinned source of truth in the suite itself.
replace "components/verification/tests/tests.fs" @"let expectedChecks = (\d+)" 1 (fun _ -> sprintf "let expectedChecks = %d" counts.Total)

// The gate comment.
replace "justfile" @"Expect \d+ checks \(\d+ examples, \d+ properties\), \d+ snapshots, \d+ integrations, and \d+ granular cases \(\d+ snapshots, \d+ integrations, \d+ writer checks\)\." 1 (fun _ ->
    sprintf
        "Expect %d checks (%d examples, %d properties), %d snapshots, %d integrations, and %d granular cases (%d snapshots, %d integrations, %d writer checks)."
        counts.Total
        counts.Examples
        counts.Properties
        snapshotCount
        integrationCount
        counts.Granular
        snapshotCount
        integrationCount
        (counts.Granular - snapshotCount - integrationCount))

// The Typst overview.
replace "README.typ" @"counts them\s+together \((\d+) checks\)" 1 (fun _ ->
    sprintf "counts them together (%d checks)" natureTotal)

replace
    "README.typ"
    @"`just check` runs \d+ examples, \d+ properties,\s+\d+ snapshots, and \d+ integrations \((\d+) checks\)"
    2
    (fun _ ->
        sprintf
            "`just check` runs %d examples, %d properties, %d snapshots, and %d integrations (%d checks)"
            counts.Examples
            counts.Properties
            snapshotCount
            integrationCount
            natureTotal)

// `docs/process/agent-setup.typ` is now a pointer to `AGENTS.md`; it carries no counts.

replace
    "README.typ"
    @"`just check`\s+runs \d+ examples, \d+ properties, \d+ snapshots, and \d+ integrations \(\d+\s+checks\); the granular suite has \d+ tests\."
    1
    (fun _ ->
        sprintf
            "`just check` runs %d examples, %d properties, %d snapshots, and %d integrations (%d checks); the granular suite has %d tests."
            counts.Examples
            counts.Properties
            snapshotCount
            integrationCount
            natureTotal
            counts.Granular)

// The .NET MCP guide.
replace "docs/process/dotnet-mcp.typ" @"example acceptance suite \((\d+) checks\)" 1 (fun _ ->
    sprintf "example acceptance suite (%d checks)" counts.Total)

replace "docs/process/dotnet-mcp.typ" @"ALL TESTS PASSED \((\d+)\)" 1 (fun _ -> sprintf "ALL TESTS PASSED (%d)" counts.Total)

let changed =
    texts
    |> Map.toList
    |> List.filter (fun (file, text) -> text <> File.ReadAllText(Path.Combine(root, file)))

match mode with
| Check ->
    if changed.IsEmpty then
        printfn "documented counts match the suites"
    else
        for (file, _) in changed do
            printfn "stale counts: %s" file

        exit 1
| Write ->
    for (file, text) in changed do
        File.WriteAllText(Path.Combine(root, file), text)
        printfn "updated %s" file
