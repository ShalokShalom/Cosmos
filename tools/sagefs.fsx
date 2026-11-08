// Drive the shared SageFs daemon over its MCP HTTP endpoint from a shell.
//
// The daemon is the compiler, the runner and the test harness for this
// repository (AGENTS.md, "Alpha build policy"). A client whose transport cannot
// speak MCP uses this script; it never runs MSBuild directly.
//
//   dotnet fsi tools/sagefs.fsx status
//   dotnet fsi tools/sagefs.fsx sessions
//   dotnet fsi tools/sagefs.fsx eval path/to/snippet.fsx
//   dotnet fsi tools/sagefs.fsx eval -            # read the snippet from stdin
//   dotnet fsi tools/sagefs.fsx check path/to/snippet.fsx
//   dotnet fsi tools/sagefs.fsx list-tests [pattern]
//   dotnet fsi tools/sagefs.fsx tests [pattern] [--wait 45]
//   dotnet fsi tools/sagefs.fsx reset [--rebuild]

open System
open System.IO
open System.Net.Http
open System.Text
open System.Text.Json

let repositoryRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let port =
    match Environment.GetEnvironmentVariable "SAGEFS_MCP_PORT" with
    | null
    | "" -> 37749
    | value -> int value

let endpoint = sprintf "http://127.0.0.1:%d/" port
let client = new HttpClient()
client.Timeout <- TimeSpan.FromSeconds 300.0
let session = ref (None: string option)

let unreachable (error: exn) =
    failwith (sprintf "sagefs: cannot reach %s (%s); run `just sage-ensure`" endpoint error.Message)

let selectJson (requestId: int) (text: string) =
    let candidates =
        if text.Contains "data: " then
            text.Split '\n'
            |> Array.filter (fun line -> line.StartsWith "data: ")
            |> Array.map (fun line -> line.Substring 6)
        else
            [| text |]

    let parsed =
        candidates
        |> Array.rev
        |> Array.choose (fun candidate ->
            try
                Some(JsonDocument.Parse(candidate.Trim()))
            with _ -> None)

    let matchesRequest (document: JsonDocument) =
        match document.RootElement.TryGetProperty "id" with
        | true, value when value.ValueKind = JsonValueKind.Number -> value.GetInt32() = requestId
        | true, value when value.ValueKind = JsonValueKind.String -> value.GetString() = string requestId
        | _ -> false

    match parsed |> Array.tryFind matchesRequest |> Option.orElse (Array.tryHead parsed) with
    | Some document -> document
    | None ->
        failwith (
            sprintf "sagefs: no JSON in the response: %s" (text.Substring(0, min 300 text.Length))
        )

let connect () =
    let initialize =
        """{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"sagefs-fsx","version":"1"}}}"""

    let request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream") |> ignore
    request.Content <- new StringContent(initialize, Encoding.UTF8, "application/json")

    let response =
        try
            client.Send request
        with error -> unreachable error

    match response.Headers.TryGetValues "Mcp-Session-Id" with
    | true, values -> session.Value <- Some(Seq.head values)
    | _ -> ()

    let notified = new HttpRequestMessage(HttpMethod.Post, endpoint)
    notified.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream") |> ignore
    notified.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18") |> ignore

    match session.Value with
    | Some id -> notified.Headers.TryAddWithoutValidation("Mcp-Session-Id", id) |> ignore
    | None -> ()

    notified.Content <-
        new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json")

    client.Send notified |> ignore

let call (body: string) =
    let request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream") |> ignore
    request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18") |> ignore

    match session.Value with
    | Some id -> request.Headers.TryAddWithoutValidation("Mcp-Session-Id", id) |> ignore
    | None -> ()

    request.Content <- new StringContent(body, Encoding.UTF8, "application/json")

    let response =
        try
            client.Send request
        with error -> unreachable error

    let text = response.Content.ReadAsStringAsync().Result

    if int response.StatusCode >= 400 then
        failwith (
            sprintf "sagefs: HTTP %d: %s" (int response.StatusCode) (text.Substring(0, min 400 text.Length))
        )

    text

let requestCounter = ref 0

let callTool (name: string) (arguments: string) =
    requestCounter.Value <- requestCounter.Value + 1

    let body =
        sprintf
            "{\"jsonrpc\":\"2.0\",\"id\":%d,\"method\":\"tools/call\",\"params\":{\"name\":%s,\"arguments\":%s}}"
            requestCounter.Value
            (JsonSerializer.Serialize name)
            arguments

    let document = selectJson requestCounter.Value (call body)
    let root = document.RootElement

    match root.TryGetProperty "error" with
    | true, error ->
        let message =
            match error.TryGetProperty "message" with
            | true, value -> value.GetString()
            | _ -> error.GetRawText()

        failwith ("sagefs: " + message)
    | _ -> ()

    let result = root.GetProperty "result"

    let isError =
        match result.TryGetProperty "isError" with
        | true, value -> value.ValueKind = JsonValueKind.True
        | _ -> false

    let text =
        result.GetProperty("content").EnumerateArray()
        |> Seq.choose (fun item ->
            match item.TryGetProperty "text" with
            | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
            | _ -> None)
        |> String.concat "\n"

    isError, text

let workingDirectoryArgument =
    let encoded = JsonSerializer.Serialize repositoryRoot
    sprintf "\"working_directory\":%s" encoded

let emit (isError: bool) (text: string) =
    printfn "%s" text
    if isError then exit 1

let readSnippet (path: string) =
    if path = "-" then Console.In.ReadToEnd()
    elif File.Exists path then File.ReadAllText path
    else failwith ("sagefs: no such file: " + path)

let at (index: int) (arguments: string array) =
    if arguments.Length > index then Some arguments.[index] else None

let flagValue (name: string) (arguments: string array) =
    arguments
    |> Array.tryFindIndex ((=) name)
    |> Option.bind (fun index -> at (index + 1) arguments)

let isFlag (value: string) = value.StartsWith "--"

connect ()

let arguments = fsi.CommandLineArgs |> Array.skip 1

match arguments with
| [| "sessions" |] ->
    let isError, text = callTool "list_sessions" "{}"
    emit isError text
| [| "status" |] ->
    let isError, text = callTool "get_session_status" (sprintf "{%s}" workingDirectoryArgument)
    emit isError text
| [| "eval"; path |] ->
    let code = readSnippet path
    let filePath = if path = "-" then "" else Path.GetFullPath path

    let arguments =
        sprintf
            "{\"agentName\":\"crush\",\"code\":%s,\"file_path\":%s,%s}"
            (JsonSerializer.Serialize code)
            (JsonSerializer.Serialize filePath)
            workingDirectoryArgument

    let isError, text = callTool "send_fsharp_code" arguments
    emit isError text
| [| "check"; path |] ->
    let code = readSnippet path
    let arguments = sprintf "{\"code\":%s,%s}" (JsonSerializer.Serialize code) workingDirectoryArgument
    let isError, text = callTool "check_fsharp_code" arguments
    emit isError text
| [| "list-tests" |] ->
    let isError, text = callTool "list_tests" "{\"pattern\":\"\"}"
    emit isError text
| [| "list-tests"; pattern |] ->
    let isError, text = callTool "list_tests" (sprintf "{\"pattern\":%s}" (JsonSerializer.Serialize pattern))
    emit isError text
| [| "tests" |]
| [| "tests"; _ |]
| [| "tests"; _; _ |]
| [| "tests"; _; _; _ |] ->
    let pattern = at 1 arguments |> Option.filter (isFlag >> not) |> Option.defaultValue ""
    let wait = flagValue "--wait" arguments |> Option.defaultValue "30"

    let arguments =
        sprintf
            "{\"pattern\":%s,\"wait_seconds\":%s,%s}"
            (JsonSerializer.Serialize pattern)
            wait
            workingDirectoryArgument

    let isError, text = callTool "run_tests" arguments
    emit isError text
| [| "reset" |]
| [| "reset"; "--rebuild" |] ->
    let rebuild = arguments |> Array.contains "--rebuild"
    let arguments = sprintf "{\"rebuild\":%b,%s}" rebuild workingDirectoryArgument
    let isError, text = callTool "hard_reset_fsi_session" arguments
    emit isError text
| [| "suite" |] ->
    let code =
        """let suiteReport () =
    let writer = new System.IO.StringWriter()
    let original = System.Console.Out
    System.Console.SetOut writer
    try Tests.runAll () finally System.Console.SetOut original
    writer.ToString()

let report = suiteReport ()
System.IO.File.WriteAllText("/tmp/scale-suite.txt", report)
let lines = report.Split [| '\n' |]
let failures = lines |> Array.filter (fun line -> line.Contains "FAIL")
let summary = lines |> Array.filter (fun line -> line.StartsWith "Tests ") |> Array.truncate 3
printfn "suite lines: %d; failure lines: %d" lines.Length failures.Length
for line in failures |> Array.truncate 20 do printfn "%s" line
for line in summary do printfn "%s" line"""

    let arguments =
        sprintf
            "{\"agentName\":\"crush\",\"code\":%s,%s}"
            (JsonSerializer.Serialize code)
            workingDirectoryArgument

    let isError, text = callTool "send_fsharp_code" arguments
    emit isError text
| _ ->
    eprintfn "usage: dotnet fsi tools/sagefs.fsx status|sessions|eval FILE|check FILE|list-tests [PATTERN]|tests [PATTERN] [--wait N]|suite|reset [--rebuild]"
    exit 2