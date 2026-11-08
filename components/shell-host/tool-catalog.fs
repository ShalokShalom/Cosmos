module ToolCatalog

// ============================================================
// External-tool glue. The tools remain external applications; this module
// records safe argument shapes and structured output decoders. It never
// executes a tool or constructs a shell string.
// ============================================================

open System
open System.Text.Json

type ToolCapability =
    | ReadFiles
    | SearchFiles
    | ReadText
    | TransformJson
    | SelectInteractive

type ToolOutput =
    | TextOutput
    | NullSeparatedPaths
    | JsonLines
    | JsonOutput

type ToolSpec =
    {
        Name: string
        Executable: string
        Words: string list
        MinimumVersion: string
        Output: ToolOutput
        Capabilities: Set<ToolCapability>
    }

type ToolError =
    | UnknownTool of name: string
    | UnknownWord of word: string
    | DuplicateWord of word: string
    | InvalidOutput of reason: string

type PathBatch = { Paths: string list; Truncated: bool }

type RgMatch =
    {
        Path: string
        LineNumber: int
        Text: string
    }

let catalog : ToolSpec list =
    [
        {
            Name = "fd"
            Executable = "fd"
            Words = [ "files"; "paths" ]
            MinimumVersion = "10"
            Output = NullSeparatedPaths
            Capabilities = Set.ofList [ ReadFiles ]
        }
        {
            Name = "rg"
            Executable = "rg"
            Words = [ "search"; "matches" ]
            MinimumVersion = "15"
            Output = JsonLines
            Capabilities = Set.ofList [ ReadFiles; SearchFiles ]
        }
        {
            Name = "bat"
            Executable = "bat"
            Words = [ "read"; "show" ]
            MinimumVersion = "0.26"
            Output = TextOutput
            Capabilities = Set.ofList [ ReadFiles; ReadText ]
        }
        {
            Name = "eza"
            Executable = "eza"
            Words = [ "list"; "entries" ]
            MinimumVersion = "0.23"
            Output = TextOutput
            Capabilities = Set.ofList [ ReadFiles ]
        }
        {
            Name = "jq"
            Executable = "jq"
            Words = [ "transform"; "json" ]
            MinimumVersion = "1.8"
            Output = JsonOutput
            Capabilities = Set.ofList [ TransformJson ]
        }
        {
            Name = "fzf"
            Executable = "fzf"
            Words = [ "pick"; "choose" ]
            MinimumVersion = "0.74"
            Output = TextOutput
            Capabilities = Set.ofList [ SelectInteractive ]
        }
        {
            Name = "sd"
            Executable = "sd"
            Words = [ "replace"; "substitute" ]
            MinimumVersion = "1"
            Output = TextOutput
            Capabilities = Set.ofList [ ReadFiles; ReadText ]
        }
    ]

let find (name: string) : Result<ToolSpec, ToolError> =
    match catalog |> List.tryFind (fun tool -> tool.Name = name) with
    | Some tool -> Ok tool
    | None -> Error(UnknownTool name)

let findByWord (word: string) : Result<ToolSpec, ToolError> =
    let matches = catalog |> List.filter (fun tool -> tool.Words |> List.contains word)

    match matches with
    | [] -> Error(UnknownWord word)
    | [ tool ] -> Ok tool
    | _ -> Error(DuplicateWord word)

let fdArguments (pattern: string) (roots: string list) (hidden: bool) : string list =
    [
        "--print0"
        if hidden then "--hidden"
        "--"
        pattern
        yield! roots
    ]

let rgArguments (pattern: string) (roots: string list) (hidden: bool) : string list =
    [
        "--json"
        if hidden then "--hidden"
        "--"
        pattern
        yield! roots
    ]

let batArguments (paths: string list) : string list = "--" :: paths

let parseNullSeparatedPaths (maxItems: int) (output: string) : Result<PathBatch, ToolError> =
    if maxItems < 0 then
        Error(InvalidOutput "the path limit cannot be negative")
    else
        let paths =
            output.Split('\u0000', StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList

        Ok
            {
                Paths = paths |> List.truncate maxItems
                Truncated = paths.Length > maxItems
            }

let parseRgJson (maxItems: int) (output: string) : Result<RgMatch list * bool, ToolError> =
    if maxItems < 0 then
        Error(InvalidOutput "the match limit cannot be negative")
    else
        try
            let matches =
                output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                |> Array.choose (fun line ->
                    use document = JsonDocument.Parse line
                    let root = document.RootElement

                    if root.ValueKind <> JsonValueKind.Object then
                        None
                    else
                        match root.TryGetProperty "type" with
                        | true, eventType when eventType.GetString() = "match" ->
                            match root.TryGetProperty "data" with
                            | true, data ->
                                let path =
                                    match data.TryGetProperty "path" with
                                    | true, path ->
                                        match path.TryGetProperty "text" with
                                        | true, text -> text.GetString()
                                        | _ -> ""
                                    | _ -> ""

                                let lineNumber =
                                    match data.TryGetProperty "line_number" with
                                    | true, number -> number.GetInt32()
                                    | _ -> 0

                                let text =
                                    match data.TryGetProperty "lines" with
                                    | true, lines ->
                                        match lines.TryGetProperty "text" with
                                        | true, value -> value.GetString()
                                        | _ -> ""
                                    | _ -> ""

                                if String.IsNullOrEmpty path then
                                    None
                                else
                                    Some
                                        {
                                            Path = path
                                            LineNumber = lineNumber
                                            Text = text
                                        }
                            | _ -> None
                        | _ -> None)
                |> Array.toList

            Ok(matches |> List.truncate maxItems, matches.Length > maxItems)
        with _ ->
            Error(InvalidOutput "rg output is not JSON lines")
