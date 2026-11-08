open System
open System.Diagnostics

let runGit (arguments: string) =
    let info =
        ProcessStartInfo(
            "git",
            arguments,
            WorkingDirectory = "/home/shalok/Projects/Scale",
            RedirectStandardOutput = true,
            UseShellExecute = false
        )

    use child = Process.Start(info)
    let output = child.StandardOutput.ReadToEnd()
    child.WaitForExit()
    output.Split('\n', StringSplitOptions.RemoveEmptyEntries)

let changed =
    runGit "status --porcelain"
    |> Array.map (fun line -> line.Substring(3).Trim())
    |> Array.filter (fun path -> path.Length > 0)

let isProjectFile (path: string) = path.EndsWith(".fsproj")
let isFSharpSource (path: string) = path.EndsWith(".fs")
let isBuildInput (path: string) = isProjectFile path || isFSharpSource path

let verdict =
    if changed.Length = 0 then "NoRebuild"
    elif changed |> Array.exists isBuildInput then "FullRebuild"
    else "NoRebuild"

printfn "rebuild plan: %s" verdict

for path in changed do
    printfn "  changed: %s" path
