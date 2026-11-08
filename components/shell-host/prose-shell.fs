module ProseShell

// ============================================================
// The non-interactive prose shell runner (D183). It reads a `.prose` script,
// parses and lowers it, attaches the process host, and returns the final value
// and the last command status. The base only prints the outcome and exits.
// ============================================================

open Types
open ShellHost

/// The script's final value, rendered by the base.
type ProseValue =
    | ProseNumber of value: double
    | ProseText of value: string
    | ProseEmpty

/// The script's outcome. A refusal carries the exit status it earns.
type ProseOutcome =
    | ProseCompleted of value: ProseValue * status: int
    | ProseRefused of reason: string * status: int

let private context () : VocabularyEntry list * EvaluationState =
    let (vocabularyRoot, store0) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    let store = Store.setVocabularyTable vocabularyRoot store0

    let vocabulary =
        match VocabularyInImage.readVocabulary (Some vocabularyRoot) store with
        | Ok vocabulary -> vocabulary
        | Error message -> failwith ("The prose vocabulary could not be read: " + message)

    vocabulary, Evaluator.initial store vocabularyRoot

let private finalValue (state: EvaluationState) : ProseValue =
    match Stack.popIndex state.Stack with
    | None -> ProseEmpty
    | Some(identity, _) ->
        match Store.readTextAt identity state.Store with
        | Some text -> ProseText text
        | None ->
            match Store.lookupTensor identity state.Store with
            | Some tensor ->
                match ScalarRead.readScalar tensor with
                | Ok number -> ProseNumber number
                | Error _ -> ProseEmpty
            | None -> ProseEmpty

/// The final value of a completed script, for the CLI's book and spell modes.
let readFinalValue (state: EvaluationState) : ProseValue = finalValue state

/// Runs one `.prose` text with the given host-word adapters and book base.
let runTextWith
    (adapters: HostWordAdapter list)
    (baseDirectory: string option)
    (text: string)
    : ProseOutcome =
    let vocabulary, state = context ()

    let state =
        { state with
            HostWords = adapters
            Capabilities = adapters |> List.map (fun adapter -> adapter.Capability) |> Set.ofList
            BookBaseDirectory = baseDirectory }

    match ProseParser.parseProgram vocabulary text with
    | Error error -> ProseRefused(ProseParser.describeError error, 1)
    | Ok expression ->
        match EvaluatorLowering.lowerExpression expression with
        | Error refusal -> ProseRefused(EvaluatorLowering.describeRefusal refusal, 1)
        | Ok program ->
            let units =
                program.Units
                |> List.map (fun unit -> unit.Name, unit.Instructions)
                |> Map.ofList

            match
                LoweredAbstraction.runProgram
                    units
                    EvaluatorKernelBridge.executeWord
                    program.Instructions
                    (EvaluatorKernelBridge.toKernel state)
            with
            | Error(LoweredAbstraction.LoweredFailure reason)
            | Error(LoweredAbstraction.HostFailure reason) -> ProseRefused(reason, 1)
            | Ok final ->
                let host = EvaluatorKernelBridge.fromKernel final
                let status = host.LastCommandStatus |> Option.defaultValue 0
                ProseCompleted(finalValue host, status)

/// Runs one `.prose` text with the process host only.
let runText (process: ShellProcess) (text: string) : ProseOutcome =
    runTextWith [ wordAdapter process ] None text

/// Runs one `.prose` file with the given host-word adapters. The file's
/// directory is the base for relative book paths.
let runFileWith (adapters: HostWordAdapter list) (path: string) : ProseOutcome =
    try
        let fullPath = System.IO.Path.GetFullPath path
        let baseDirectory = System.IO.Path.GetDirectoryName fullPath

        System.IO.File.ReadAllText fullPath
        |> runTextWith adapters (Some baseDirectory)
    with error ->
        ProseRefused(error.Message, 2)

/// Runs one `.prose` file with the process host only.
let runFile (process: ShellProcess) (path: string) : ProseOutcome =
    runFileWith [ wordAdapter process ] path
