open System
open System.Diagnostics
open System.IO

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let args = Environment.GetCommandLineArgs() |> Array.skip 1
let project =
    match args |> Array.tryFind (fun value -> value.EndsWith(".fsproj")) with
    | Some value -> Path.GetFullPath(value, root)
    | None -> Path.Combine(root, "projects/scale-tests/scale-tests.fsproj")

let runBuild (label: string) (extra: string list) =
    let binlog = "/tmp/scale-build-" + label + ".binlog"
    let log = "/tmp/scale-build-" + label + ".log"
    let arguments =
        [ "build"
          project
          "-c"
          "Debug"
          "--no-restore"
          "-v:minimal"
          "-clp:PerformanceSummary"
          "-bl:" + binlog ]
        @ extra

    let info =
        ProcessStartInfo(
            "dotnet",
            arguments,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        )

    let started = Stopwatch.StartNew()
    use child = Process.Start(info)
    let output = child.StandardOutput.ReadToEnd()
    let error = child.StandardError.ReadToEnd()
    child.WaitForExit()
    started.Stop()
    File.WriteAllText(log, output + Environment.NewLine + error)

    printfn "%-10s %6d ms  exit=%d  log=%s" label started.ElapsedMilliseconds child.ExitCode log
    printfn "binlog: %s" binlog

let touch (relativePath: string) =
    let path = Path.Combine(root, relativePath)
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow)
    printfn "touched %s" relativePath

printfn "Scale build-time matrix (Debug, no restore)"
printfn "project: %s" project

runBuild "warm" []
runBuild "noop" []

touch "components/demo/demo.fs"
runBuild "leaf" []

touch "components/prelude/evaluator/evaluator.fs"
runBuild "core" []

runBuild "full" [ "-t:Rebuild" ]
