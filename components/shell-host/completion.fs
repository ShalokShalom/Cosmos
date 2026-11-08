module Completion

// ============================================================
// Shell completion protocol. Providers receive structured requests and return
// candidates; they never edit the line or execute as Scale code. The first
// slice is pure request construction and output parsing.
// ============================================================

open System
open System.Text.Json

type CompletionRequest =
    {
        Line: string
        Cursor: int
        Command: string list
        Cwd: string
    }

type CompletionItem =
    {
        Value: string
        Description: string option
        Span: (int * int) option
    }

type CompletionBatch =
    {
        Items: CompletionItem list
        Truncated: bool
    }

type CompletionError =
    | InvalidRequest of reason: string
    | InvalidOutput of reason: string

type CompletionCommand =
    {
        FileName: string
        Arguments: string list
    }

let validate (request: CompletionRequest) : Result<unit, CompletionError> =
    if request.Cursor < 0 || request.Cursor > request.Line.Length then
        Error(InvalidRequest "the cursor is outside the line")
    elif String.IsNullOrWhiteSpace request.Cwd then
        Error(InvalidRequest "the working directory is required")
    else
        Ok()

let private escapeFishSingleQuoted (text: string) : string =
    text.Replace("'", "\\'")

let fishCommand (request: CompletionRequest) : Result<CompletionCommand, CompletionError> =
    validate request
    |> Result.map (fun () ->
        {
            FileName = "fish"
            Arguments =
                [
                    "--command"
                    "complete '--do-complete=" + escapeFishSingleQuoted request.Line + "'"
                ]
        })

let carapaceCommand (request: CompletionRequest) : Result<CompletionCommand, CompletionError> =
    match request.Command with
    | [] -> Error(InvalidRequest "the command word list is empty")
    | command :: _ ->
        validate request
        |> Result.map (fun () ->
            {
                FileName = "carapace"
                Arguments = command :: "nushell" :: request.Command
            })

let parseFishOutput (maxItems: int) (output: string) : Result<CompletionBatch, CompletionError> =
    let lines =
        output.Replace("\r\n", "\n").Split('\n')
        |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))

    let parsed =
        lines
        |> Array.choose (fun line ->
            let parts = line.Split('\t', 2)

            if parts.Length = 0 || String.IsNullOrWhiteSpace parts.[0] then
                None
            else
                Some
                    {
                        Value = parts.[0]
                        Description =
                            if parts.Length = 2 && not (String.IsNullOrWhiteSpace parts.[1]) then
                                Some parts.[1]
                            else
                                None
                        Span = None
                    })
        |> Array.toList

    if maxItems < 0 then
        Error(InvalidOutput "the candidate limit cannot be negative")
    else
        Ok
            {
                Items = parsed |> List.truncate maxItems
                Truncated = parsed.Length > maxItems
            }

let parseCarapaceOutput (maxItems: int) (output: string) : Result<CompletionBatch, CompletionError> =
    try
        use document = JsonDocument.Parse output

        if document.RootElement.ValueKind <> JsonValueKind.Array then
            Error(InvalidOutput "carapace output must be an array")
        else
            let parsed =
                document.RootElement.EnumerateArray()
                |> Seq.choose (fun element ->
                    if element.ValueKind <> JsonValueKind.Object then
                        None
                    else
                        match element.TryGetProperty "value" with
                        | true, value when value.ValueKind = JsonValueKind.String ->
                            let description =
                                match element.TryGetProperty "description" with
                                | true, description when description.ValueKind = JsonValueKind.String ->
                                    Some(description.GetString())
                                | _ -> None

                            Some
                                {
                                    Value = value.GetString()
                                    Description = description
                                    Span = None
                                }
                        | _ -> None)
                |> Seq.toList

            if maxItems < 0 then
                Error(InvalidOutput "the candidate limit cannot be negative")
            else
                Ok
                    {
                        Items = parsed |> List.truncate maxItems
                        Truncated = parsed.Length > maxItems
                    }
    with _ ->
        Error(InvalidOutput "carapace output is not valid JSON")
