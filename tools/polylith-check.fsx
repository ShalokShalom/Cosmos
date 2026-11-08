open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

type Manifest =
    { Components: string list
      Bases: string list
      Projects: string list
      Development: string list
      External: string list
      Legacy: string list }

let manifestPath = Path.Combine(repoRoot, "workspace.edn")
let manifestText = File.ReadAllText manifestPath

let readVector (key: string) =
    let pattern = "(?s):" + Regex.Escape key + @"\s*\[(.*?)\]"
    let matched = Regex.Match(manifestText, pattern)

    if not matched.Success then
        failwithf "workspace.edn has no :%s vector" key

    [ for item in Regex.Matches(matched.Groups.[1].Value, "\"([^\"]+)\"") -> item.Groups.[1].Value ]

let manifest =
    { Components = readVector "components"
      Bases = readVector "bases"
      Projects = readVector "projects"
      Development = readVector "development"
      External = readVector "external"
      Legacy = readVector "legacy" }

let failures = ResizeArray<string>()

let fail (message: string) = failures.Add message

let relative (path: string) =
    Path.GetRelativePath(repoRoot, path).Replace('\\', '/')

let directoryOf (kind: string) (name: string) =
    match kind with
    | "component" -> Path.Combine(repoRoot, "components", name)
    | "base" -> Path.Combine(repoRoot, "bases", name)
    | "project" -> Path.Combine(repoRoot, "projects", name)
    | "development" -> Path.Combine(repoRoot, "development", name)
    | "external" -> Path.Combine(repoRoot, "components", name)
    | other -> failwithf "unknown node kind %s" other

let checkDirectory (kind: string) (name: string) =
    let path = directoryOf kind name

    if not (Directory.Exists path) then
        fail (sprintf "%s '%s' has no directory at %s" kind name (relative path))

let checkManifest () =
    manifest.Components |> List.iter (checkDirectory "component")
    manifest.Bases |> List.iter (checkDirectory "base")
    manifest.Projects |> List.iter (checkDirectory "project")
    manifest.Development |> List.iter (checkDirectory "development")
    manifest.External |> List.iter (checkDirectory "external")

    manifest.Legacy
    |> List.iter (fun path ->
        if not (File.Exists(Path.Combine(repoRoot, path))) then
            fail (sprintf "legacy entry '%s' does not exist" path))

let projectFiles (kind: string) (name: string) =
    let path = directoryOf kind name

    if Directory.Exists path then
        Directory.EnumerateFiles(path, "*.fsproj", SearchOption.AllDirectories)
        |> Seq.toList
    else
        []

let isExternal (name: string) = List.contains name manifest.External

let allProjects =
    [
      for name in manifest.Components do
          if not (isExternal name) then
              yield! projectFiles "component" name

      for name in manifest.Bases do
          yield! projectFiles "base" name

      for name in manifest.Projects do
          yield! projectFiles "project" name

      for name in manifest.Development do
          yield! projectFiles "development" name
    ]

let nodeOfProject (path: string) =
    let parts = (relative path).Split('/')

    if parts.Length < 3 then
        None
    else
        let kind =
            match parts.[0] with
            | "components" -> Some "component"
            | "bases" -> Some "base"
            | "projects" -> Some "project"
            | "development" -> Some "development"
            | _ -> None

        kind |> Option.map (fun nodeKind -> nodeKind, parts.[1])

let references (projectPath: string) =
    let document = XDocument.Load projectPath
    let directory = Path.GetDirectoryName projectPath

    [
      for element in document.Descendants() do
          if element.Name.LocalName = "ProjectReference" then
              let includeAttribute =
                  element.Attributes()
                  |> Seq.tryFind (fun attribute -> attribute.Name.LocalName = "Include")

              match includeAttribute with
              | Some attribute -> yield Path.GetFullPath(Path.Combine(directory, attribute.Value))
              | None -> ()
    ]

let allowed (sourceKind: string) (sourceName: string) (targetKind: string) (targetName: string) =
    if sourceKind = "component" && isExternal targetName then
        sourceName = "nu-host"
    else
        match sourceKind, targetKind with
        | "component", "component" -> true
        | "base", ("component" | "development") -> true
        | "project", ("component" | "base" | "development") -> true
        | "development", "component" -> true
        | _ -> false

let graph = ResizeArray<(string * string) * (string * string)>()

let checkReferences () =
    for projectPath in allProjects do
        match nodeOfProject projectPath with
        | None -> ()
        | Some(sourceKind, sourceName) ->
            for targetPath in references projectPath do
                match nodeOfProject targetPath with
                | None -> ()
                | Some(targetKind, targetName) ->
                    if not (allowed sourceKind sourceName targetKind targetName) then
                        fail (
                            sprintf
                                "%s '%s' references %s '%s' (forbidden by the Polylith boundary)"
                                sourceKind
                                sourceName
                                targetKind
                                targetName
                        )

                    graph.Add((sourceKind, sourceName), (targetKind, targetName))

let adjacency =
    graph
    |> Seq.groupBy fst
    |> Seq.map (fun (source, edges) -> source, edges |> Seq.map snd |> Seq.toList)
    |> Map.ofSeq

let rec visit (path: Set<string * string>) (node: string * string) =
    if Set.contains node path then
        fail (sprintf "dependency cycle through %s '%s'" (fst node) (snd node))
        false
    else
        let next = adjacency |> Map.tryFind node |> Option.defaultValue []
        next |> List.forall (visit (Set.add node path))

let checkCycles () =
    graph
    |> Seq.collect (fun (source, target) -> [ source; target ])
    |> Seq.distinct
    |> Seq.toList
    |> List.iter (fun node -> visit Set.empty node |> ignore)

let checkInterfaces () =
    for name in manifest.Components do
        if not (isExternal name) then
            let directory = directoryOf "component" name

            if Directory.Exists directory then
                if not (File.Exists(Path.Combine(directory, "README.md"))) then
                    fail (sprintf "component '%s' has no README.md" name)

                let hasProject =
                    Directory.EnumerateFiles(directory, "*.fsproj", SearchOption.AllDirectories)
                    |> Seq.isEmpty
                    |> not

                let hasHeader =
                    Directory.EnumerateFiles(directory, "*.h", SearchOption.AllDirectories)
                    |> Seq.isEmpty
                    |> not

                if not (hasProject || hasHeader) then
                    fail (sprintf "component '%s' has neither a .fsproj nor a public header" name)

checkManifest ()
checkInterfaces ()
checkReferences ()
checkCycles ()

if failures.Count > 0 then
    for failure in failures do
        eprintfn "poly-check: %s" failure

    eprintfn "poly-check: FAIL (%d problem(s))" failures.Count
    exit 1

printfn
    "poly-check: OK (%d components, %d bases, %d projects, %d development, %d external, %d legacy)"
    manifest.Components.Length
    manifest.Bases.Length
    manifest.Projects.Length
    manifest.Development.Length
    manifest.External.Length
    manifest.Legacy.Length
