module TestRunner

// The test deployable's entry logic. The product base no longer references the
// development library: this module, the `scale-tests` project, and the JIT-only
// suites are the test artifact's whole surface.

open Cli

/// Prints the example/granular summary and returns the process exit code.
let reportSummary (granularExitCode: int option) : int =
    printfn ""
    printfn "=== Done ==="
    printfn ""

    match granularExitCode with
    | Some code when code <> 0 ->
        printfn ">>> GRANULAR TESTS FAILED <<<"
        printfn ""
        1
    | Some _ ->
        printfn ">>> ALL GRANULAR TESTS PASSED <<<"
        printfn ""
        0
    | None ->
        let total = Tests.passed + Tests.failed
        printfn $"  Examples:    {Tests.examples}"
        printfn $"  Properties:  {Tests.properties}"
        printfn $"  Total:       {total}"
        printfn ""

        if Tests.failed = 0 then
            printfn $">>> ALL TESTS PASSED ({total}) <<<"
        else
            printfn $">>> {Tests.failed} TEST(S) FAILED <<<"

        printfn ""
        if Tests.failed = 0 then 0 else 1

/// The nature-based summary across every kind of check. Snapshots and
/// integrations are the only artifact tests; the rest are the examples and
/// properties (the granular cases re-run those).
let reportAllTests (artifactExitCode: int) : int =
    printfn ""
    printfn "=== Done ==="
    printfn ""

    let total =
        Tests.passed
        + Tests.failed
        + GranularTests.snapshotCount
        + GranularTests.integrationCount

    printfn $"  Examples:    {Tests.examples}"
    printfn $"  Properties:  {Tests.properties}"
    printfn $"  Snapshots:   {GranularTests.snapshotCount}"
    printfn $"  Integration: {GranularTests.integrationCount}"
    printfn $"  Total:       {total}"
    printfn ""

    if Tests.failed = 0 && artifactExitCode = 0 then
        printfn $">>> ALL TESTS PASSED ({total}) <<<"
        printfn ""
        0
    else
        printfn ">>> TESTS FAILED <<<"
        printfn ""
        1

/// Runs the test artifact's argument modes. Product modes are refused by name:
/// the product binary owns them, and a test run should not silently do nothing.
let run (arguments: string[]) : int =
    match Cli.parseArguments arguments with
    | AllTests ->
        Tests.runAll()
        let artifactExitCode = GranularTests.runArtifacts()
        reportAllTests artifactExitCode
    | GranularCount ->
        printfn $"Granular tests: {GranularTests.granularTestCount}"
        0
    | GranularSuite ->
        let exitCode = GranularTests.runAll()
        reportSummary (Some exitCode)
    | ExampleSuite ->
        Tests.runAll()
        reportSummary None
    | BenchmarkRun ->
        Benchmarks.run()
        0
    | Showcase
    | FixedSpecificationRun
    | TableRun _
    | DiagnoseRun _
    | ProseFile _
    | ProseTypecheck _
    | BookRun _
    | CastRun _
    | WireListen _
    | WireConnect _ ->
        printfn "scale-tests: this artifact runs the test suite; use the scale product for other modes."
        2
