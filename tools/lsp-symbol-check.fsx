// Asserts that the F# language server really loads the Scale projects.
//
// A health request is not evidence: a server with an empty LoadedProjects set
// answers health checks and still refuses every symbol request. This script
// speaks LSP directly, asks for document symbols in a real component file, and
// fails with a named reason when the workspace did not load.
//
// It reproduces the client configuration recorded in Notes/lsp-workspace-plan.md:
// the workspace peek must exclude the vendored Nu tree, or it picks
// components/nu-engine/Nu.slnx and the Nu build poisons the load.
//
// The server runs its own MSBuild graph load; that is this check's build, not an
// agent build, and it does not replace SageFs.
//
// Run with: dotnet fsi tools/lsp-symbol-check.fsx [--timeout-seconds N]

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let timeoutSeconds =
    let arguments = fsi.CommandLineArgs |> Array.skip 1

    arguments
    |> Array.tryFindIndex (fun argument -> argument = "--timeout-seconds")
    |> Option.bind (fun index -> arguments |> Array.tryItem (index + 1))
    |> Option.bind (fun value ->
        match Int32.TryParse value with
        | true, parsed -> Some parsed
        | false, _ -> None)
    |> Option.defaultValue 240

let targetFile =
    Path.Combine(repoRoot, "components/prelude/evaluator/lowering.fs")

let expectedSymbol = "EvaluatorLowering"

let initializeOptions =
    """
    {
      "AutomaticWorkspaceInit": true,
      "WorkspaceModePeekDeepLevel": 4,
      "ExcludeProjectDirectories": ["nu-engine"]
    }
    """

let fileUri (path: string) : string = Uri(path).AbsoluteUri

type Frame =
    | Payload of string
    | StreamEnded

/// Reads one LSP frame: headers terminated by a blank line, then a body of the
/// declared byte length.
let readFrame (stream: Stream) : Frame =
    let headers = StringBuilder()
    let mutable finished = false
    let mutable ended = false

    while not finished && not ended do
        let value = stream.ReadByte()

        if value < 0 then
            ended <- true
        else
            headers.Append(char value) |> ignore
            let text = headers.ToString()

            if text.EndsWith("\r\n\r\n", StringComparison.Ordinal) then
                finished <- true

    if ended then
        StreamEnded
    else
        let declaredLength =
            headers
                .ToString()
                .Split([| "\r\n" |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.tryPick (fun line ->
                if line.StartsWith("Content-Length:", StringComparison.Ordinal) then
                    match Int32.TryParse(line.Substring("Content-Length:".Length).Trim()) with
                    | true, parsed -> Some parsed
                    | false, _ -> None
                else
                    None)

        match declaredLength with
        | None -> StreamEnded
        | Some length ->
            let buffer = Array.zeroCreate<byte> length
            let mutable read = 0

            while read < length do
                let got = stream.Read(buffer, read, length - read)

                if got <= 0 then
                    read <- length
                else
                    read <- read + got

            Payload(Encoding.UTF8.GetString buffer)

let writeFrame (stream: Stream) (json: string) : unit =
    let body = Encoding.UTF8.GetBytes json
    let header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n")
    stream.Write(header, 0, header.Length)
    stream.Write(body, 0, body.Length)
    stream.Flush()

/// The client configuration under test, in the form the LSP expects.
let startServer (stateDirectory: string) : Process =
    let info =
        ProcessStartInfo(
            FileName = "fsautocomplete",
            Arguments = $"--log-level Error --state-directory \"{stateDirectory}\"",
            WorkingDirectory = repoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        )

    Process.Start info

let run () : int =
    if not (File.Exists targetFile) then
        eprintfn "lsp-symbol-check: the target file is missing: %s" targetFile
        1
    else
        let stateDirectory =
            Path.Combine(Path.GetTempPath(), "scale-lsp-symbol-check")

        if Directory.Exists stateDirectory then
            Directory.Delete(stateDirectory, true)

        Directory.CreateDirectory stateDirectory |> ignore

        use server = startServer stateDirectory

        let inbox = ConcurrentQueue<string>()
        let cancellation = new CancellationTokenSource()

        // Drain stdout on a background task so the server never blocks on a full
        // pipe, and so a notification storm cannot starve the response we want.
        let reader =
            Task.Run(fun () ->
                let stream = server.StandardOutput.BaseStream
                let mutable running = true

                while running && not cancellation.IsCancellationRequested do
                    match readFrame stream with
                    | Payload payload -> inbox.Enqueue payload
                    | StreamEnded -> running <- false)

        let serverErrors = ConcurrentQueue<string>()

        let errorReader =
            Task.Run(fun () ->
                let mutable line = server.StandardError.ReadLine()

                while not (isNull line) do
                    serverErrors.Enqueue line
                    line <- server.StandardError.ReadLine())

        let send (json: string) = writeFrame server.StandardInput.BaseStream json

        /// Waits for the response whose id matches, or returns None on deadline.
        let awaitResponse (id: int) (deadline: DateTime) : JsonElement option =
            let mutable result = None

            while result.IsNone && DateTime.UtcNow < deadline do
                match inbox.TryDequeue() with
                | true, payload ->
                    let document = JsonDocument.Parse payload
                    let root = document.RootElement
                    let mutable idProperty = JsonElement()
                    let mutable resultProperty = JsonElement()

                    if root.TryGetProperty("id", &idProperty) then
                        let responseId = idProperty.GetInt32()

                        if responseId = id && root.TryGetProperty("result", &resultProperty) then
                            result <- Some(root.Clone())
                | false, _ -> Thread.Sleep 100

            result

        let deadline = DateTime.UtcNow.AddSeconds(float timeoutSeconds)

        send (
            JsonSerializer.Serialize(
                {| jsonrpc = "2.0"
                   id = 1
                   method = "initialize"
                   ``params`` =
                    {| processId = null
                       rootUri = fileUri repoRoot
                       capabilities = {| workspace = {| workspaceFolders = true |} |}
                       workspaceFolders =
                        [| {| uri = fileUri repoRoot; name = "Scale" |} |]
                       initializationOptions = JsonDocument.Parse(initializeOptions).RootElement |} |}
            )
        )

        match awaitResponse 1 deadline with
        | None ->
            eprintfn
                "lsp-symbol-check: the server did not answer initialize within %d seconds. Errors: %s"
                timeoutSeconds
                (String.Join(" | ", serverErrors))
            1
        | Some _ ->
            send (JsonSerializer.Serialize {| jsonrpc = "2.0"; method = "initialized"; ``params`` = {| |} |})

            let mutable attempt = 0
            let mutable found: string list = []
            let mutable lastReason = "no symbol response arrived"

            while found.IsEmpty && DateTime.UtcNow < deadline do
                attempt <- attempt + 1
                let requestId = 100 + attempt

                send (
                    JsonSerializer.Serialize(
                        {| jsonrpc = "2.0"
                           id = requestId
                           method = "textDocument/documentSymbol"
                           ``params`` = {| textDocument = {| uri = fileUri targetFile |} |} |}
                    )
                )

                match awaitResponse requestId (min deadline (DateTime.UtcNow.AddSeconds 20.0)) with
                | None -> lastReason <- "no response to the document symbol request"
                | Some response ->
                    let mutable symbols = JsonElement()

                    if response.TryGetProperty("result", &symbols) && symbols.ValueKind = JsonValueKind.Array then
                        if symbols.GetArrayLength() > 0 then
                            found <-
                                symbols.EnumerateArray()
                                |> Seq.choose (fun symbol ->
                                    let mutable name = Unchecked.defaultof<JsonElement>

                                    if symbol.TryGetProperty("name", &name) then
                                        Some(name.GetString())
                                    else
                                        None)
                                |> Seq.truncate 20
                                |> List.ofSeq
                        else
                            lastReason <- "the server returned no symbols for the file"

                    let mutable errorProperty = Unchecked.defaultof<JsonElement>

                    if response.TryGetProperty("error", &errorProperty) then
                        lastReason <- errorProperty.ToString()

            cancellation.Cancel()

            try
                server.Kill true
            with _ ->
                ()

            reader.Wait 2000 |> ignore
            errorReader.Wait 2000 |> ignore

            if found.IsEmpty then
                eprintfn
                    "lsp-symbol-check: FAIL, %s did not load. Last reason: %s. Errors: %s"
                    targetFile
                    lastReason
                    (String.Join(" | ", serverErrors))
                eprintfn "lsp-symbol-check: fix the client's workspace options, not the repository."
                1
            elif not (found |> List.contains expectedSymbol) then
                eprintfn
                    "lsp-symbol-check: FAIL, symbols arrived but %s is not among them: %A"
                    expectedSymbol
                    found
                1
            else
                printfn "lsp-symbol-check: PASS, %s returned %d symbols including %s" targetFile found.Length expectedSymbol
                0

let exitCode = run ()

if exitCode <> 0 then
    exit exitCode
