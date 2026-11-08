open System
open System.IO

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let extensions =
    [
        "kernel", "components/kernel/abi.fs"
        "core", "components/core/runtime-types.fs"
        "prelude", "components/prelude/evaluator/evaluator.fs"
        "ipc", "components/ipc/actor-host.fs"
        "verification", "components/verification/formal/refiner.fs"
        "demo", "components/demo/demo.fs"
        "cli", "components/cli/cli.fs"
        "shell-host", "components/shell-host/shell-host.fs"
        "book-host", "components/book-host/book-runner.fs"
        "nu-host", "components/nu-host/nu-host.fs"
        "adapter-c", "components/adapter-c/scale_adapter.c"
    ]

let marker = "Potential extensions (not commitments)"
let mutable checkedCount = 0

for componentName, relativePath in extensions do
    let path = Path.Combine(repoRoot, relativePath)

    if not (File.Exists path) then
        failwithf "component-extensions-check: %s is missing %s" componentName relativePath

    let text = File.ReadAllText path
    let markerIndex = text.LastIndexOf(marker, StringComparison.Ordinal)

    if markerIndex < 0 then
        failwithf "component-extensions-check: %s has no extension block in %s" componentName relativePath

    if markerIndex < text.Length - 5000 then
        failwithf "component-extensions-check: %s has its extension block away from the bottom of %s" componentName relativePath

    let extensionLines =
        text.Substring(markerIndex).Split('\n')
        |> Array.filter (fun line ->
            let trimmed = line.Trim()
            (trimmed.StartsWith("// -", StringComparison.Ordinal)
             || trimmed.StartsWith("* -", StringComparison.Ordinal)))

    if extensionLines.Length < 2 then
        failwithf "component-extensions-check: %s needs at least two potential extensions" componentName

    checkedCount <- checkedCount + 1

printfn "component-extensions-check: OK (%d components)" checkedCount
