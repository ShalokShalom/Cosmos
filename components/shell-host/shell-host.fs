module ShellHost

// ============================================================
// The shell effect boundary (D183). This component owns every OS process
// operation. The kernel and the Prelude hold no process code: the Prelude
// offers a host-word token to an attached `HostWordAdapter`, and this module
// implements that adapter on top of `System.Diagnostics.Process`.
// ============================================================

open Types

/// The process effect boundary. `Run` forwards standard output and standard
/// error; `Capture` forwards standard error and returns standard output.
type ShellPolicy =
    | Unrestricted
    | Restricted of programs: Set<string> * writeRoots: string list * inheritEnvironment: bool

type ShellProcess =
    abstract Run: program: string * arguments: string list -> Result<int, string>
    abstract Capture: program: string * arguments: string list -> Result<int * string, string>
    abstract Stream: program: string * arguments: string list * input: string -> Result<int * string, string>
    abstract CheckWritePath: path: string -> Result<unit, string>

let checkProgram (policy: ShellPolicy) (program: string) : Result<unit, string> =
    match policy with
    | Unrestricted -> Ok()
    | Restricted(programs, _, _) when programs.Contains program -> Ok()
    | Restricted _ -> Error("the shell policy does not allow program: " + program)

let checkWritePath (policy: ShellPolicy) (path: string) : Result<unit, string> =
    match policy with
    | Unrestricted -> Ok()
    | Restricted(_, roots, _) ->
        let fullPath = PathPolicy.resolveRealPath path

        let allowed =
            roots
            |> List.exists (fun root ->
                PathPolicy.isUnderRoot (PathPolicy.resolveRealPath root) fullPath)

        if allowed then Ok() else Error("the shell policy does not allow writing: " + path)

/// The real process host. It starts one program with an explicit argument list,
/// inherits the environment, working directory, and standard input, and never
/// routes through a shell.
type ProcessShell(policy: ShellPolicy, timeoutMilliseconds: int option) =
    new(policy: ShellPolicy) = ProcessShell(policy, None)
    new() = ProcessShell(Unrestricted, None)

    static member private Start
        (policy: ShellPolicy)
        (program: string)
        (arguments: string list)
        : Result<System.Diagnostics.Process, string> =
        match checkProgram policy program with
        | Error reason -> Error reason
        | Ok() ->
            try
                let startInfo = System.Diagnostics.ProcessStartInfo(program)
                startInfo.UseShellExecute <- false
                startInfo.RedirectStandardOutput <- true
                startInfo.RedirectStandardError <- true
                startInfo.RedirectStandardInput <- true

                match policy with
                | Restricted(_, _, false) -> startInfo.Environment.Clear()
                | _ -> ()

                for argument in arguments do
                    startInfo.ArgumentList.Add argument

                let process = new System.Diagnostics.Process()
                process.StartInfo <- startInfo

                if process.Start() then
                    Ok process
                else
                    process.Dispose()
                    Error("the process did not start: " + program)
            with error ->
                Error("the process did not start: " + program + ": " + error.Message)

    static member private Finish(timeoutMilliseconds: int option, process: System.Diagnostics.Process) : Result<int * string, string> =
        try
            process.StandardInput.Close()
            let standardOutput = process.StandardOutput.ReadToEndAsync()
            let standardError = process.StandardError.ReadToEndAsync()

            let exited =
                match timeoutMilliseconds with
                | Some milliseconds -> process.WaitForExit milliseconds
                | None ->
                    process.WaitForExit()
                    true

            if not exited then
                try
                    process.Kill true
                with _ ->
                    ()

                Error "the process timed out"
            else
                let output = standardOutput.Result
                let errors = standardError.Result

                if errors <> "" then
                    System.Console.Error.Write errors

                Ok(process.ExitCode, output)
        with error ->
            Error("the process failed: " + error.Message)

    interface ShellProcess with
        member _.Run(program, arguments) =
            match ProcessShell.Start policy program arguments with
            | Error reason -> Error reason
            | Ok process ->
                use process = process

                match ProcessShell.Finish(timeoutMilliseconds, process) with
                | Error reason -> Error reason
                | Ok(status, output) ->
                    if output <> "" then
                        System.Console.Out.Write output

                    Ok status

        member _.Capture(program, arguments) =
            match ProcessShell.Start policy program arguments with
            | Error reason -> Error reason
            | Ok process ->
                use process = process
                ProcessShell.Finish(timeoutMilliseconds, process)

        member _.Stream(program, arguments, input) =
            match ProcessShell.Start policy program arguments with
            | Error reason -> Error reason
            | Ok process ->
                use process = process

                try
                    process.StandardInput.Write input
                    ProcessShell.Finish(timeoutMilliseconds, process)
                with error ->
                    Error("the process failed: " + error.Message)

        member _.CheckWritePath(path) = checkWritePath policy path

let private popArguments
    (word: string)
    (arity: int)
    (state: EvaluationState)
    : Result<string list * EntryIdentity list, EvaluationError> =
    let rec loop
        (remainingCount: int)
        (remainingStack: EntryIdentity list)
        (accumulated: string list)
        : Result<string list * EntryIdentity list, EvaluationError> =
        if remainingCount = 0 then
            Ok(accumulated, remainingStack)
        else
            match Stack.popIndex remainingStack with
            | None -> Error(StackUnderflow(word, arity, Stack.depth remainingStack))
            | Some(identity, rest) ->
                match Store.readTextAt identity state.Store with
                | Some text -> loop (remainingCount - 1) rest (text :: accumulated)
                | None -> Error(HostFailure("the " + word + " arguments must be text"))

    loop arity state.Stack []

let private executeCommand
    (process: ShellProcess)
    (capture: bool)
    (name: string)
    (arity: int)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match popArguments name arity state with
    | Error error -> Error error
    | Ok(values, remaining) ->
        match values with
        | [] -> Error(HostFailure(name + ": a program is required"))
        | program :: arguments ->
            let executed =
                if capture then
                    process.Capture(program, arguments)
                    |> Result.map (fun (status, output) -> status, Some output)
                else
                    process.Run(program, arguments) |> Result.map (fun status -> status, None)

            match executed with
            | Error reason -> Error(HostFailure reason)
            | Ok(status, captured) ->
                match captured with
                | Some output ->
                    let (identity, store) = Store.addText output state.Store

                    Ok
                        { state with
                            Store = store
                            Stack = Stack.push identity remaining
                            LastCommandStatus = Some status
                        }
                | None ->
                    let (identity, store) = Store.addTensor (Tensor.scalar (double status)) state.Store

                    Ok
                        { state with
                            Store = store
                            Stack = Stack.push identity remaining
                            LastCommandStatus = Some status
                        }

let private popInputText
    (word: string)
    (remaining: EntryIdentity list)
    (state: EvaluationState)
    : Result<string * EntryIdentity list, EvaluationError> =
    match Stack.popIndex remaining with
    | None -> Error(HostFailure(word + " needs text on the stack"))
    | Some(inputId, rest) ->
        match Store.readTextAt inputId state.Store with
        | Some text -> Ok(text, rest)
        | None -> Error(HostFailure(word + " input must be text"))

let private executeFeed
    (process: ShellProcess)
    (arity: int)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match popArguments "feed" arity state with
    | Error error -> Error error
    | Ok(values, remaining) ->
        match values with
        | [] -> Error(HostFailure("feed: a program is required"))
        | program :: arguments ->
            match popInputText "feed" remaining state with
            | Error error -> Error error
            | Ok(input, rest) ->
                match process.Stream(program, arguments, input) with
                | Error reason -> Error(HostFailure reason)
                | Ok(status, output) ->
                    let (identity, store) = Store.addText output state.Store

                    Ok
                        { state with
                            Store = store
                            Stack = Stack.push identity rest
                            LastCommandStatus = Some status
                        }

let private executeSave
    (process: ShellProcess)
    (arity: int)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match popArguments "save" arity state with
    | Error error -> Error error
    | Ok(values, remaining) ->
        match values with
        | [ path ] ->
            match popInputText "save" remaining state with
            | Error error -> Error error
            | Ok(input, rest) ->
                let resolved =
                    if System.IO.Path.IsPathRooted path then
                        path
                    else
                        let directory =
                            state.BookBaseDirectory
                            |> Option.defaultValue (System.IO.Directory.GetCurrentDirectory())

                        System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, path))

                match process.CheckWritePath resolved with
                | Error reason -> Error(HostFailure reason)
                | Ok() ->
                    try
                        System.IO.File.WriteAllText(resolved, input)

                        Ok
                            { state with
                                Stack = Stack.push (Store.addText input state.Store |> fst) rest
                                LastCommandStatus = Some 0
                            }
                    with error ->
                        Error(HostFailure("save: " + error.Message))
        | _ -> Error(HostFailure("save: a path is required"))

/// The host-word adapter for `run`, `capture`, `feed`, and `save`.
let wordAdapter (process: ShellProcess) : HostWordAdapter =
    { new HostWordAdapter with
        member _.Capability = "shell"
        member _.TryExecute(token, state) =
            match HostWords.tryDecode token with
            | Some("run", arity) -> Some(executeCommand process false "run" arity state)
            | Some("capture", arity) -> Some(executeCommand process true "capture" arity state)
            | Some("feed", arity) -> Some(executeFeed process arity state)
            | Some("save", arity) -> Some(executeSave process arity state)
            | Some(name, _) -> Some(Error(HostFailure("unknown host word: " + name)))
            | None -> None }

// ============================================================
// Potential extensions (not commitments)
// - Streaming stdout/stderr and pipelines: long-running commands.
// - Network and sandbox policy: constrain host effects beyond paths.
// - Process-tree cancellation and deadlines: reliable script cleanup.
// - PTY support: interactive terminal tools under an approved capability.
// ============================================================
