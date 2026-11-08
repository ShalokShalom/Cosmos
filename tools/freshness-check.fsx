// Detects the failure mode that let a broken commit look green: the compiled
// DLLs are older than the source files, so the REPL and the tests are running a
// build that is not what the repository says.
//
// This compares each project's source files with the DLL the last Debug build
// produced. A missing DLL is reported as "not built" (some components, such as
// nu-host, are not part of the current product build); a source newer than the
// DLL is a stale failure.
//
// Run with: dotnet fsi tools/freshness-check.fsx

open System
open System.IO

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let relative (path: string) : string =
    Path.GetRelativePath(repoRoot, path).Replace('\\', '/')

let isBuildOutput (path: string) : bool =
    let parts = path.Replace('\\', '/').Split('/')
    parts |> Array.exists (fun part -> part = "bin" || part = "obj")

let projectFiles =
    [ "components"; "bases"; "development"; "projects" ]
    |> List.collect (fun root ->
        let directory = Path.Combine(repoRoot, root)

        if Directory.Exists directory then
            Directory.EnumerateFiles(directory, "*.fsproj", SearchOption.AllDirectories)
            |> Seq.filter (fun path ->
                not ((relative path).StartsWith("components/nu-engine/")))
            |> Seq.toList
        else
            [])

let sourceFiles (projectDirectory: string) : string list =
    Directory.EnumerateFiles(projectDirectory, "*.fs", SearchOption.AllDirectories)
    |> Seq.append (Directory.EnumerateFiles(projectDirectory, "*.fsproj", SearchOption.AllDirectories))
    |> Seq.filter (fun path -> not (isBuildOutput path))
    |> Seq.toList

let newestSource (projectDirectory: string) : DateTime option =
    sourceFiles projectDirectory
    |> List.map (fun path -> File.GetLastWriteTimeUtc path)
    |> function
        | [] -> None
        | times -> Some(List.max times)

let builtAssembly (projectDirectory: string) (projectName: string) : string option =
    [ Path.Combine(projectDirectory, "bin", "Debug", "net10.0", projectName + ".dll")
      Path.Combine(projectDirectory, "bin", "Release", "net10.0", projectName + ".dll") ]
    |> List.tryFind File.Exists

let stale = ResizeArray<string>()
let unbuilt = ResizeArray<string>()
let fresh = ResizeArray<string>()

for projectPath in projectFiles do
    let projectDirectory = Path.GetDirectoryName projectPath
    let projectName = Path.GetFileNameWithoutExtension projectPath

    match builtAssembly projectDirectory projectName, newestSource projectDirectory with
    | Some assembly, Some sourceTime ->
        let buildTime = File.GetLastWriteTimeUtc assembly

        if sourceTime > buildTime then
            stale.Add(
                relative projectPath
                + " (source "
                + sourceTime.ToString("yyyy-MM-dd HH:mm:ss")
                + "Z, build "
                + buildTime.ToString("yyyy-MM-dd HH:mm:ss")
                + "Z)"
            )
        else
            fresh.Add(relative projectPath)
    | Some _, None -> fresh.Add(relative projectPath)
    | None, _ -> unbuilt.Add(relative projectPath)

for path in unbuilt do
    printfn "not built: %s" path

for path in fresh do
    printfn "fresh:     %s" path

if stale.Count > 0 then
    for path in stale do
        eprintfn "STALE:     %s" path

    eprintfn ""
    eprintfn "freshness-check: FAIL (%d stale project(s)); rebuild before trusting tests" stale.Count
    exit 1

printfn ""
printfn "freshness-check: OK (%d fresh, %d not built)" fresh.Count unbuilt.Count
