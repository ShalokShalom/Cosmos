open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let baseDir = Path.Combine(root, "bases", "scale-cli")
let projectPath = Path.Combine(baseDir, "scale.fsproj")

let compileOrder =
    let document = XDocument.Load projectPath

    [
      for element in document.Descendants() do
          if element.Name.LocalName = "Compile" then
              let includeAttribute =
                  element.Attributes()
                  |> Seq.tryFind (fun attribute -> attribute.Name.LocalName = "Include")

              match includeAttribute with
              | Some attribute when attribute.Value.EndsWith(".fs") -> yield attribute.Value
              | _ -> ()
    ]

let linesOf (path: string) = File.ReadAllLines(Path.Combine(baseDir, path))

let declaringModule (path: string) =
    linesOf path
    |> Array.tryPick (fun line ->
        let moduleMatch = Regex.Match(line, @"^\s*module\s+([A-Za-z0-9_.]+)")

        if moduleMatch.Success then
            Some moduleMatch.Groups.[1].Value
        else
            let namespaceMatch = Regex.Match(line, @"^\s*namespace\s+([A-Za-z0-9_.]+)")

            if namespaceMatch.Success then Some namespaceMatch.Groups.[1].Value else None)

let modules = compileOrder |> List.map (fun path -> path, declaringModule path)

let fileOfModule =
    modules
    |> List.choose (fun (path, name) -> name |> Option.map (fun value -> value, path))
    |> Map.ofList

let opensOf (path: string) =
    linesOf path
    |> Array.choose (fun line ->
        let matched = Regex.Match(line, @"^\s*open\s+([A-Za-z0-9_.]+)")

        if matched.Success then Some matched.Groups.[1].Value else None)
    |> Array.toList

let dependencies =
    compileOrder
    |> List.map (fun path ->
        let dependencyFiles =
            opensOf path
            |> List.choose (fun opened -> fileOfModule |> Map.tryFind opened)
            |> List.filter (fun target -> target <> path)
            |> List.distinct

        path, dependencyFiles)

let index = compileOrder |> List.indexed |> List.map (fun (position, path) -> path, position) |> Map.ofList

let backEdgesAt (position: int) =
    let prefix = compileOrder |> List.take position |> Set.ofList
    let suffix = compileOrder |> List.skip position |> Set.ofList

    [
      for path, targets in dependencies do
          if Set.contains path prefix then
              for target in targets do
                  if Set.contains target suffix then
                      yield path, target
    ]

printfn "module dependency clean cuts (prefix compiles without the suffix):"

let cuts =
    [
      for position in 1 .. compileOrder.Length - 1 do
          let backEdges = backEdgesAt position

          if backEdges.IsEmpty then
              yield position
    ]

for position in cuts do
    printfn "  cut %d: %s | %s" position compileOrder.[position - 1] compileOrder.[position]

printfn ""
printfn "proposed cut after the kernel bridge (evaluator included):"
let bridgePosition = index.["evaluator/kernel-bridge.fs"] + 1

for source, target in backEdgesAt bridgePosition do
    printfn "  back edge: %s -> %s" source target

printfn ""
printfn "first clean cut after the kernel bridge:"

match cuts |> List.tryFind (fun position -> position >= bridgePosition) with
| Some position -> printfn "  %d: %s | %s" position compileOrder.[position - 1] compileOrder.[position]
| None -> printfn "  none; the whole compile order is one cluster"
