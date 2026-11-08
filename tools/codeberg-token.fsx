open System
open System.Diagnostics
open System.IO

// One-token Codeberg helper. The token is kept in the desktop keyring, never in
// Git config, a remote URL, a shell argument, or the repository.
let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let service = "codeberg"
let account = "ShalokShalom"
let remote = "polylith"
let branch = "polylith-main"

let run (file: string) (arguments: string list) (input: string option) =
    let start = ProcessStartInfo(file)
    start.WorkingDirectory <- repoRoot
    start.UseShellExecute <- false
    start.RedirectStandardInput <- true
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true

    for argument in arguments do
        start.ArgumentList.Add argument

    use child = Process.Start start

    match input with
    | Some value ->
        child.StandardInput.Write value
        child.StandardInput.Close()
    | None -> child.StandardInput.Close()

    let output = child.StandardOutput.ReadToEnd()
    let error = child.StandardError.ReadToEnd()
    child.WaitForExit()
    child.ExitCode, output, error

let sanitizeToken (value: string) =
    value
    |> Seq.filter (fun character -> not (Char.IsWhiteSpace character))
    |> Seq.toArray
    |> String

let clipboardToken () =
    let code, output, error = run "xsel" [ "--clipboard"; "--output" ] None

    if code <> 0 then
        failwith ("could not read the clipboard: " + error.Trim())

    sanitizeToken output

let keyringLookup () =
    let code, output, error = run "secret-tool" [ "lookup"; "service"; service; "username"; account ] None

    if code = 0 then Some(sanitizeToken output)
    elif String.IsNullOrWhiteSpace error then None
    else None

let keyringClear () =
    run "secret-tool" [ "clear"; "service"; service; "username"; account ] None |> ignore

let keyringStore (token: string) =
    if String.IsNullOrWhiteSpace token then
        failwith "the Codeberg token is empty"

    keyringClear ()
    let code, _, error =
        run
            "secret-tool"
            [ "store"; "--label"; "Codeberg token"; "service"; service; "username"; account ]
            (Some(token + "\n"))

    if code <> 0 then
        failwith ("could not store the token in the keyring: " + error.Trim())

let approve (token: string) =
    let input =
        "protocol=https\nhost=codeberg.org\nusername="
        + account
        + "\npassword="
        + token
        + "\n\n"

    let code, _, error =
        run
            "git"
            [ "-c"; "credential.helper=cache --timeout=60"; "credential"; "approve" ]
            (Some input)

    if code <> 0 then
        failwith ("could not approve the credential: " + error.Trim())

let push (token: string) =
    approve token
    let code, output, error =
        run
            "git"
            [ "-c"; "credential.helper=cache --timeout=60"; "push"; "--force"; remote; branch + ":main" ]
            None

    run "git" [ "credential-cache"; "exit" ] None |> ignore

    if code <> 0 then
        failwith ("push failed: " + error.Trim())

    printfn "%s" (output.Trim())

let showStatus () =
    match keyringLookup () with
    | Some _ -> printfn "Codeberg token: stored"
    | None -> printfn "Codeberg token: not stored"

match fsi.CommandLineArgs |> Array.skip 1 with
| [| "store" |] ->
    keyringStore (clipboardToken ())
    printfn "Codeberg token stored in the keyring"
| [| "status" |] -> showStatus ()
| [| "push" |] ->
    match keyringLookup () with
    | Some token -> push token
    | None -> failwith "no Codeberg token in the keyring; run `store` first"
| [| "clear" |] ->
    keyringClear ()
    printfn "Codeberg token removed from the keyring"
| _ ->
    printfn "usage: dotnet fsi tools/codeberg-token.fsx store|status|push|clear"
