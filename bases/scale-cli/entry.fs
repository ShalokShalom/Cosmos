module Entry

open Types

/// Unwraps ScalarRead.readScalar for demo printing; an error here is a demo bug.
let value (tensor: Tensor) : RealNumber =
    match ScalarRead.readScalar tensor with
    | Ok v -> v
    | Error e -> failwith e

/// Renders a number without the F# printf machinery, which is not NativeAOT-safe.
let private number (value: RealNumber) : string =
    value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

/// The demonstration showcase: vocabulary, evaluator, persistence, and the
/// refinement loop.
let private runShowcase () : unit =
    // --- Building Store ---
    printfn "=== Building Store ==="
    let store0 = Store.empty

    let (vocabularyRoot, store1) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary store0

    let store2 = Store.setVocabularyTable vocabularyRoot store1

    printfn $"Vocabulary root: {IndexTransformation.toExternal (EntryIdentity.value vocabularyRoot)}"
    printfn $"Lexicon entries: {Lexicon.count store2.Lexicon}"
    printfn ""

    match VocabularyInImage.readVocabulary (Some vocabularyRoot) store2 with
    | Ok vocabulary ->
        printfn $"Vocabulary read: {List.length vocabulary} entries"
        printfn ""
        VocabularyList.showVocabulary vocabulary
        printfn "=== VocabularyList: Explanation ==="
        VocabularyList.explainWord vocabulary "canJump"
        VocabularyList.explainWord vocabulary "canFly"

        printfn "=== Parse Sentence ==="
        let sentence = "Every player that is alive can jump."
        printfn "Sentence: %s" sentence
        let tokens = Tokenizer.tokenize vocabulary sentence
        printfn "Tokens: %s" (String.concat " " tokens)

        match Parser.parse vocabulary tokens with
        | Some parsed ->
            printfn "Pattern: %s" (string parsed.Pattern)
            printfn "Subject: %s" parsed.Subject
            printfn "Conditions: %s" (String.concat ", " (parsed.Conditions |> List.map conditionText))
            printfn "Verb: %s" parsed.Verb
            printfn "F# output:"
            printfn "%s" (Emitter.emit vocabulary parsed)
        | None -> printfn "Pattern not recognized."

        printfn ""
        printfn "=== Diagnostics (P0--P5) ==="
        let malformed = "Every player can jump"
        let malformedTokens = Tokenizer.tokenize vocabulary malformed

        match Violations.parseText vocabulary malformed with
        | Error violation ->
            printfn "%s" (Presentation.render violation)

            match NearestSentence.nearest vocabulary malformedTokens with
            | Some candidate -> printfn "Nearest legal sentence: %s" candidate.Text
            | None -> printfn "Nearest legal sentence: (none within one edit)"
        | Ok _ -> printfn "Unexpectedly accepted: %s" malformed

        let discourseState = Evaluator.initial store2 vocabularyRoot
        let rejected = Discourse.utter [ "add" ] discourseState

        printfn "Discourse: accepted=%b committed=%b" rejected.Accepted rejected.Committed

        rejected.Violations
        |> List.iter (fun violation -> printfn "%s" (Presentation.render violation))

        let committedSentence =
            Violations.parse vocabulary (Tokenizer.tokenize vocabulary "Every player can jump.")

        let candidateSentence =
            Violations.parse vocabulary (Tokenizer.tokenize vocabulary "No player can jump.")

        match committedSentence, candidateSentence with
        | Ok committed, Ok candidate ->
            match Discourse.contradiction [ committed ] candidate with
            | Some violation -> printfn "%s" (Presentation.render violation)
            | None -> printfn "Discourse contradiction: none"
        | _ -> printfn "Discourse contradiction: the demonstration sentence did not parse"

        printfn ""
        printfn "=== Decision table surface ==="

        let tableText =
            [
                "table jump-or-run"
                "when player has health at least 2, every player can jump."
                "end"
            ]
            |> String.concat "\n"

        match DecisionTableSurface.parse vocabulary tableText with
        | Error error -> printfn "Decision table error: %s" (DecisionTableSurface.describeError error)
        | Ok document ->
            let table = DecisionTableSurface.toTable document

            let sink =
                Diagnostics.addSelection
                    table
                    (DecisionTable.select DemoDomain.fieldValue table { Alive = true; Health = 1 })
                    Diagnostics.empty

            Diagnostics.render sink |> List.iter (printfn "%s")

        printfn "=== SMT violation (D62) ==="

        match Smt.createFromLattice DemoDomain.lattice DemoDomain.canJumpExpression with
        | Error reason -> printfn "SMT encoding error: %s" reason
        | Ok encoding ->
            use encoding = encoding

            let falsified =
                Smt.dischargeWithCore
                    encoding
                    {
                        Pattern = Capability
                        Conditions = []
                        Refinements = []
                    }

            let smtSink =
                Diagnostics.addOption (Violations.ofSmtDischarge falsified) Diagnostics.empty

            Diagnostics.render smtSink |> List.iter (printfn "%s")

        printfn ""
    | Error message -> printfn "Error reading vocabulary: %s" message

    // --- Evaluator ---
    printfn "=== Evaluator ==="
    let state0 = Evaluator.initial store2 vocabularyRoot

    let state1 =
        Evaluator.runOrFail
            { state0 with
                Program = EvaluatorProgram.ofTokensOrFail [ "3"; "4"; "add" ]
            }

    match Stack.popResolved state1.Store state1.Stack with
    | Some(result, _) -> printfn $"3 4 add -> {number (value result)}"
    | None -> printfn "Evaluator error"

    let state2 =
        Evaluator.runOrFail
            { state0 with
                Program = EvaluatorProgram.ofTokensOrFail [ "10"; "duplicate"; "multiply" ]
            }

    match Stack.popResolved state2.Store state2.Stack with
    | Some(result, _) -> printfn $"10 duplicate multiply -> {number (value result)}"
    | None -> printfn "Evaluator error"

    let ifProgram1 = [ "1"; "{"; "100"; "}"; "{"; "200"; "}"; "if" ]
    let state3 =
        Evaluator.runOrFail { state0 with Program = EvaluatorProgram.ofTokensOrFail ifProgram1 }

    match Stack.popResolved state3.Store state3.Stack with
    | Some(result, _) -> printfn $"1 (100) (200) if -> {number (value result)}"
    | None -> printfn "Evaluator error"

    let ifProgram2 = [ "0"; "{"; "100"; "}"; "{"; "200"; "}"; "if" ]
    let state4 =
        Evaluator.runOrFail { state0 with Program = EvaluatorProgram.ofTokensOrFail ifProgram2 }

    match Stack.popResolved state4.Store state4.Stack with
    | Some(result, _) -> printfn $"0 (100) (200) if -> {number (value result)}"
    | None -> printfn "Evaluator error"

    // --- Bookmark ---
    printfn ""
    printfn "=== Bookmark ==="
    let bookmarkState0 = Evaluator.initial store2 vocabularyRoot
    let bookmarkProgram = [ "bookmark"; "showBookmark" ]

    let bookmarkState1 =
        Evaluator.runOrFail
            { bookmarkState0 with
                Program = EvaluatorProgram.ofTokensOrFail bookmarkProgram
            }

    // The effect channel: `showBookmark` appended its line to the state; the
    // edge prints it, so the core itself stays pure.
    bookmarkState1.Output |> List.iter (printfn "%s")

    printfn $"After bookmark + showBookmark: {Stack.depth bookmarkState1.Stack} stack depth"

    // --- Unification ---
    printfn ""
    printfn "=== Unification ==="
    let unifyDemo0 = Evaluator.initial store2 vocabularyRoot
    let unifyProgram = [ "10"; "score"; "bind"; "score"; "lookup"; "duplicate"; "add" ]

    let unifyDemo1 =
        Evaluator.runOrFail
            { unifyDemo0 with
                Program = EvaluatorProgram.ofTokensOrFail unifyProgram
            }

    match Stack.popResolved unifyDemo1.Store unifyDemo1.Stack with
    | Some(result, _) -> printfn $"bind 10 to score, lookup, duplicate, add -> {number (value result)}"
    | None -> printfn "Unification demo failed"

    let unifyVarProgram = [ "?x"; "7"; "unify" ]

    let unifyDemo2 =
        Evaluator.runOrFail
            { unifyDemo0 with
                Program = EvaluatorProgram.ofTokensOrFail unifyVarProgram
            }

    match Stack.popResolved unifyDemo2.Store unifyDemo2.Stack with
    | Some(result, _) -> printfn $"unify ?x 7 -> {number (value result)} (x is now bound)"
    | None -> printfn "Unification demo failed"

    // --- SetAlgebra (Models) ---
    printfn ""
    printfn "=== SetAlgebra (Models) ==="
    let setAlgebraDemo0 = Evaluator.initial store2 vocabularyRoot

    let setAlgebraDemoProgram =
        [
            "\"small\""
            "set"
            "1"
            "\"small\""
            "add-member"
            "2"
            "\"small\""
            "add-member"
            "3"
            "\"small\""
            "add-member"
            "2"
            "\"small\""
            "is"
            "99"
            "\"small\""
            "is"
        ]

    let setAlgebraDemoState =
        Evaluator.runOrFail
            { setAlgebraDemo0 with
                Program = EvaluatorProgram.ofTokensOrFail setAlgebraDemoProgram
            }

    match Stack.popResolved setAlgebraDemoState.Store setAlgebraDemoState.Stack with
    | Some(notIn, stack1) ->
        match Stack.popResolved setAlgebraDemoState.Store stack1 with
        | Some(isIn, _) ->
            printfn $"2 in small  -> {number (value isIn)}"
            printfn $"99 in small -> {number (value notIn)}"
        | None -> printfn "SetAlgebra demo failed"
    | None -> printfn "SetAlgebra demo failed"

    // --- Railway (error reporting) ---
    printfn ""
    printfn "=== Railway (error reporting) ==="
    let railwayDemo = Evaluator.initial store2 vocabularyRoot

    for program in [ [ "1"; "0"; "divide" ]; [ "frobnicate" ]; [ "add" ] ] do
        match Evaluator.run { railwayDemo with Program = EvaluatorProgram.ofTokensOrFail program } with
        | Ok _ -> printfn "%s -> (unexpected success)" (String.concat " " program)
        | Error diagnostic ->
            printfn
                "%s -> at token %s: %s"
                (String.concat " " program)
                (string diagnostic.Location)
                (describeError diagnostic.Error)

    // --- Persistence ---
    printfn ""
    printfn "=== Persistence ==="
    // A unique scratch file under the OS temp directory: two concurrent runs
    // must not collide, and nothing is written to a fixed global path.
    let storePath =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lia-store-" + System.Guid.NewGuid().ToString("N") + ".bin"
        )

    Persistence.writeToFile storePath store2
    let fileSize = (System.IO.FileInfo storePath).Length
    printfn $"Store written to {storePath} ({fileSize} bytes)"

    let loadedStore = Persistence.readFromFile storePath
    printfn $"Store loaded: {Lexicon.count loadedStore.Lexicon} lexicon entries"

    match VocabularyInImage.readVocabulary loadedStore.VocabularyTable loadedStore with
    | Ok vocabulary -> printfn $"Vocabulary read from loaded store: {List.length vocabulary} entries"
    | Error message -> printfn "Error reading vocabulary: %s" message

    System.IO.File.Delete storePath

    // --- Refinement loop (the demonstration pipeline) ---
    Demo.report (Demo.runLoop ())

/// The fixed-spec run: the specification is held fixed, a witness becomes a
/// repair bundle, and the process fails when the target is falsified (or the
/// run is inconclusive). The run mode itself lives in `Cli`, so it is testable.
let private runFixedSpecification () : int =
    let path = Demo.bundlePath ()

    match Demo.runFixedSpecification path with
    | Ok outcome ->
        Demo.reportFixedSpecification outcome

        match outcome with
        | Demo.Holds -> 0
        | Demo.Witnessed _ -> 1
        | Demo.Inconclusive _ -> 1
    | Error reason ->
        printfn "fixed-spec: %s" reason
        2

/// The demonstration vocabulary, derived from the image rather than the seed.
let private demoVocabulary () : VocabularyEntry list =
    let (vocabularyRoot, store0) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    let store = Store.setVocabularyTable vocabularyRoot store0

    match VocabularyInImage.readVocabulary (Some vocabularyRoot) store with
    | Ok vocabulary -> vocabulary
    | Error message -> failwith ("The demonstration vocabulary could not be read: " + message)

let private policyText (policy: DecisionTable.HitPolicy) : string =
    match policy with
    | DecisionTable.Unique -> "unique"
    | DecisionTable.First -> "first"
    | DecisionTable.Priority -> "priority"

/// `--table <path>`: parse and check a Basic English table document.
let private runTable (path: string) : int =
    if path.Trim() = "" then
        printfn "table: a path is required after --table."
        2
    else
        try
            let text = System.IO.File.ReadAllText path
            let vocabulary = demoVocabulary ()

            match DecisionTableSurface.parse vocabulary text with
            | Error error ->
                printfn "table: %s" (DecisionTableSurface.describeError error)
                1
            | Ok document ->
                let table = DecisionTableSurface.toTable document
                let inputs = document.Inputs |> List.map fst |> String.concat ", "

                printfn "Table:   %s" document.Name
                printfn "Subject: %s" document.Subject
                printfn "Policy:  %s" (policyText document.Policy)
                printfn "Rows:    %d" (List.length document.Rows)
                printfn "Inputs:  %s" inputs

                match DecisionTable.check table with
                | Ok DecisionTable.Sound ->
                    printfn "Check:   Sound"
                    0
                | Ok(DecisionTable.Unsound findings) ->
                    printfn "Check:   Unsound (%d finding(s))" (List.length findings)
                    1
                | Ok(DecisionTable.Undecided reason) ->
                    printfn "Check:   Undecided: %s" reason
                    1
                | Error reason ->
                    printfn "Check:   Error: %s" reason
                    1
        with exn ->
            printfn "table: %s" exn.Message
            2

/// `--diagnose <sentence>`: parse one sentence, publish its violation, and
/// show the nearest legal sentence.
let private runDiagnose (sentence: string) : int =
    if sentence.Trim() = "" then
        printfn "diagnose: a sentence is required after --diagnose."
        2
    else
        let vocabulary = demoVocabulary ()
        let tokens = Tokenizer.tokenize vocabulary sentence

        match Violations.parseText vocabulary sentence with
        | Ok _ ->
            printfn "Accepted: %s" sentence
            0
        | Error violation ->
            let sink = Diagnostics.add violation Diagnostics.empty
            Diagnostics.render sink |> List.iter (printfn "%s")

            match NearestSentence.nearest vocabulary tokens with
            | Some candidate -> printfn "Nearest legal sentence: %s" candidate.Text
            | None -> printfn "Nearest legal sentence: (none within one edit)"

            1

/// The demonstration vocabulary and a fresh evaluator for a prose program.
let private proseContext () : VocabularyEntry list * EvaluationState =
    let (vocabularyRoot, store0) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    let store = Store.setVocabularyTable vocabularyRoot store0

    let vocabulary =
        match VocabularyInImage.readVocabulary (Some vocabularyRoot) store with
        | Ok vocabulary -> vocabulary
        | Error message -> failwith ("The prose vocabulary could not be read: " + message)

    vocabulary, Evaluator.initial store vocabularyRoot

/// `--prose-typecheck <path>`: parse, infer against the internal primitive
/// registry, and lower without running. A type or lowering failure is reported
/// and returns a non-zero exit code.
let runProseTypecheck (path: string) : int =
    if path.Trim() = "" then
        printfn "prose type: a path is required after --prose-typecheck."
        2
    else
        try
            let text = System.IO.File.ReadAllText path
            let vocabulary, _ = proseContext ()

            match ProseParser.parseLocated vocabulary text with
            | Error error ->
                printfn "prose type: %s" (ProseParser.describeError error)
                1
            | Ok(expression, sourceMap) ->
                match
                    EvaluatorLowering.lowerExpressionWithInference
                        PreludeSignatures.primitiveTable
                        sourceMap
                        expression
                with
                | Error(EvaluatorLowering.InferenceFailed diagnostic) ->
                    printfn "prose type: %s" (TypeRetrieval.describeDiagnostic diagnostic)
                    1
                | Error(EvaluatorLowering.LoweringFailed refusal) ->
                    printfn "prose type: %s" (EvaluatorLowering.describeRefusal refusal)
                    1
                | Ok _ ->
                    printfn "prose type: OK"
                    0
        with error ->
            printfn "prose type: %s" error.Message
            1

/// Whether the prose script completed or was refused, plus its exit status.
type ProseFileOutcome =
    | ProseFileCompleted of status: int
    | ProseFileRefused of status: int

let private printProseValue (value: ProseShell.ProseValue) : unit =
    match value with
    | ProseShell.ProseText text -> System.Console.Out.Write text
    | ProseShell.ProseNumber value -> printfn "%s" (number value)
    | ProseShell.ProseEmpty -> ()

/// `--prose <path>`: read, parse, lower, and run a prose program with the
/// process host attached. The final value is printed and the last command
/// status is the exit code; a parse, lowering, or run failure names its reason
/// and returns a non-zero exit code.
let runProseFile (path: string) : ProseFileOutcome =
    if path.Trim() = "" then
        printfn "prose: a path is required after --prose."
        ProseFileRefused 2
    else
        let process = ShellHost.ProcessShell() :> ShellHost.ShellProcess

        let adapters: HostWordAdapter list =
            [ BookHost.wordAdapter; ShellHost.wordAdapter process ]

        match ProseShell.runFileWith adapters path with
        | ProseShell.ProseCompleted(value, status) ->
            printProseValue value
            ProseFileCompleted status
        | ProseShell.ProseRefused(reason, status) ->
            printfn "prose: %s" reason
            ProseFileRefused status

let private bookState () : EvaluationState =
    let _, state = proseContext ()
    let process = ShellHost.ProcessShell() :> ShellHost.ShellProcess

    { state with
        HostWords = [ BookHost.wordAdapter; ShellHost.wordAdapter process ]
        Capabilities = Set.ofList [ "book"; "shell" ]
        BookBaseDirectory = Some(System.IO.Directory.GetCurrentDirectory()) }

/// `--check <path>`: load, package, and lower a book without running it.
let runBookCheck (path: string) : int =
    if path.Trim() = "" then
        printfn "book check: a path is required after --check."
        2
    else
        match BookRunner.check (bookState ()) path with
        | Ok () ->
            printfn "book check: OK"
            0
        | Error reason ->
            printfn "book check: %s" reason
            1

/// `--book <path>`: classify a book and run `main` when it is a spell.
let runBook (path: string) : ProseFileOutcome =
    if path.Trim() = "" then
        printfn "book: a path is required after --book."
        ProseFileRefused 2
    else
        let state = bookState ()

        match BookRunner.load state path with
        | Error reason ->
            printfn "book: %s" reason
            ProseFileRefused 1
        | Ok book ->
            let kind =
                match BookRunner.classify book with
                | BookRunner.Book -> "book"
                | BookRunner.Spell -> "spell"

            printfn "Book:  %s" book.Package.Name
            printfn "Kind:  %s" kind
            printfn "Words: %s" (String.concat ", " (BookRunner.exports book))

            match BookRunner.classify book with
            | BookRunner.Book -> ProseFileCompleted 0
            | BookRunner.Spell ->
                match BookRunner.runWord state book "main" [] with
                | Error reason ->
                    printfn "book: %s" reason
                    ProseFileRefused 1
                | Ok final ->
                    printProseValue (ProseShell.readFinalValue final)
                    ProseFileCompleted (final.LastCommandStatus |> Option.defaultValue 0)

/// `--cast <path> [word] [args...]`: call a book word, or a spell's `main`.
let runCast (path: string) (word: string) (arguments: string list) : ProseFileOutcome =
    if path.Trim() = "" then
        printfn "cast: a path is required after --cast."
        ProseFileRefused 2
    else
        let state = bookState ()
        let state, argumentIds = BookRunner.withArguments state arguments
        let target = if word.Trim() = "" then None else Some word

        match BookRunner.cast state path target argumentIds with
        | Error reason ->
            printfn "cast: %s" reason
            ProseFileRefused 1
        | Ok final ->
            printProseValue (ProseShell.readFinalValue final)
            ProseFileCompleted (final.LastCommandStatus |> Option.defaultValue 0)

let private parseWireNumber (text: string) : Result<double, string> =
    let mutable value = 0.0

    if
        System.Double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            &value
        )
    then
        Ok value
    else
        Error $"wire: '{text}' is not a number"

/// `--wire-listen <path>`: create a worker capability, accept a monitor and a
/// numeric message, run the worker, and report the reply and the remote exit.
let private runWireListen (path: string) : int =
    if path.Trim() = "" then
        printfn "wire: a socket path is required after --wire-listen."
        2
    else
        match ActorHost.serveSupervised path (fun () -> printfn "wire: listening on %s" path) with
        | Ok(reply, down, monitor) ->
            printfn "wire: reply %s" (number reply)
            printfn "wire: %s" down
            printfn "wire: monitor %d" monitor
            0
        | Error error ->
            printfn "%s" (ActorHost.describeHostError error)
            1

/// `--wire-connect <path> <number>`: monitor capability 1, send one number,
/// print the reply, and print the worker's remote exit.
let private runWireConnect (path: string) (message: string) : int =
    if path.Trim() = "" then
        printfn "wire: a socket path and a number are required after --wire-connect."
        2
    else
        match parseWireNumber message with
        | Error reason ->
            printfn "%s" reason
            2
        | Ok value ->
            match ActorHost.connectSupervised path value with
            | Ok(reply, down, monitor) ->
                printfn "wire: reply %s" (number reply)
                printfn "wire: %s" down
                printfn "wire: monitor %d" monitor
                0
            | Error error ->
                printfn "%s" (ActorHost.describeHostError error)
                1

[<EntryPoint>]
let main arguments =
    printfn "=== Scale: Complete Prototype ==="
    printfn ""

    match Cli.parseArguments arguments with
    | Cli.AllTests
    | Cli.GranularCount
    | Cli.GranularSuite
    | Cli.BenchmarkRun
    | Cli.ExampleSuite ->
        printfn "scale: test modes live in the scale-tests artifact; use the product modes or run scale-tests."
        2
    | Cli.FixedSpecificationRun ->
        let exitCode = runFixedSpecification ()
        printfn ""
        printfn ">>> FIXED-SPEC %s <<<" (if exitCode = 0 then "HOLDS" else "REJECTED")
        exitCode
    | Cli.TableRun path ->
        let exitCode = runTable path
        printfn ""
        printfn ">>> TABLE %s <<<" (if exitCode = 0 then "SOUND" else "REJECTED")
        exitCode
    | Cli.DiagnoseRun sentence -> runDiagnose sentence
    | Cli.ProseFile path ->
        match runProseFile path with
        | ProseFileCompleted status ->
            printfn ""
            printfn ">>> PROSE OK <<<"
            status
        | ProseFileRefused status ->
            printfn ""
            printfn ">>> PROSE REJECTED <<<"
            status
    | Cli.ProseTypecheck path ->
        let exitCode = runProseTypecheck path
        printfn ""
        printfn ">>> PROSE TYPECHECK %s <<<" (if exitCode = 0 then "OK" else "REJECTED")
        exitCode
    | Cli.BookCheck path ->
        let exitCode = runBookCheck path
        printfn ""
        printfn ">>> BOOK CHECK %s <<<" (if exitCode = 0 then "OK" else "REJECTED")
        exitCode
    | Cli.BookRun path ->
        match runBook path with
        | ProseFileCompleted status ->
            printfn ""
            printfn ">>> BOOK OK <<<"
            status
        | ProseFileRefused status ->
            printfn ""
            printfn ">>> BOOK REJECTED <<<"
            status
    | Cli.CastRun(path, word, arguments) ->
        match runCast path word arguments with
        | ProseFileCompleted status ->
            printfn ""
            printfn ">>> CAST OK <<<"
            status
        | ProseFileRefused status ->
            printfn ""
            printfn ">>> CAST REJECTED <<<"
            status
    | Cli.WireListen path ->
        let exitCode = runWireListen path
        printfn ""
        printfn ">>> WIRE LISTEN %s <<<" (if exitCode = 0 then "OK" else "FAILED")
        exitCode
    | Cli.WireConnect(path, message) ->
        let exitCode = runWireConnect path message
        printfn ""
        printfn ">>> WIRE CONNECT %s <<<" (if exitCode = 0 then "OK" else "FAILED")
        exitCode
    | Cli.Showcase ->
        runShowcase ()
        0
