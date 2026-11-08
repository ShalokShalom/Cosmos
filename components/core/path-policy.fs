module PathPolicy

open System
open System.IO

/// Resolves a path through its existing real-path chain, including a symlinked
/// parent when the leaf itself does not exist yet.
let resolveRealPath (path: string) : string =
    let rec resolve (current: string) =
        if File.Exists current || Directory.Exists current then
            let info =
                if Directory.Exists current then
                    DirectoryInfo current :> FileSystemInfo
                else
                    FileInfo current :> FileSystemInfo

            match info.ResolveLinkTarget(true) with
            | null -> Path.GetFullPath current
            | target -> Path.GetFullPath target.FullName
        else
            let parent = Path.GetDirectoryName current

            if String.IsNullOrWhiteSpace parent then
                Path.GetFullPath current
            else
                Path.Combine(resolve parent, Path.GetFileName current)

    try
        resolve (Path.GetFullPath path)
    with _ ->
        Path.GetFullPath path

let relativePath (root: string) (path: string) : string =
    try
        Path.GetRelativePath(Path.GetFullPath root, Path.GetFullPath path)
    with _ ->
        path

let isUnderRoot (root: string) (path: string) : bool =
    let relative = relativePath root path

    relative = "."
    || (not (Path.IsPathRooted relative)
        && relative <> ".."
        && not (relative.StartsWith(".." + string Path.DirectorySeparatorChar, StringComparison.Ordinal))
        && not (relative.StartsWith("../", StringComparison.Ordinal))
        && not (relative.StartsWith("..\\", StringComparison.Ordinal)))

let isHidden (relative: string) : bool =
    relative.Split([| '/'; '\\' |])
    |> Array.exists (fun segment -> segment.StartsWith(".", StringComparison.Ordinal))

let isDirectChild (relative: string) : bool =
    not (relative.Contains("/") || relative.Contains("\\"))
