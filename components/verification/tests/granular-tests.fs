module GranularTests

open Expecto
open Types
open Hedgehog
open Hedgehog.FSharp
open Tests
open VerifyTests
open VerifyExpecto
open WriterHarness

do VerifierSettings.DisableRequireUniquePrefix()

/// Expected-first assertion. Expecto's `Expect.equal` takes the actual value
/// first; this wrapper keeps the repository's written order unambiguous.
let expectEqual (expected: 'a) (actual: 'a) (message: string) : unit = Expect.equal actual expected message

/// The granular test suite. Each former example check becomes a `testCase`,
/// so SageFs discovers, filters, and watch-runs them individually. These cases
/// re-run the examples and properties the aggregate gate already covers; only
/// the snapshots and integrations are unique (see `artifactTests`).
/// Example and property cases are covered by `Tests.runAll`; only artifact tests remain here.
/// Verify settings for the emitter artifact (D23). The baseline is copied to
/// the output directory, so lookup does not depend on the current directory.
/// The diff launcher is disabled: a mismatch must fail the run, not block it
/// on an external viewer. The component list and both aggregate suites verify
/// the same baseline, so its prefix is intentionally shared.
let snapshotSettings (fileName: string) =
    let settings = VerifySettings()
    settings.DisableDiff()
    settings.UseFileName(fileName)
    settings.UseDirectory(System.IO.Path.Combine(System.AppContext.BaseDirectory, "snapshots"))
    settings

let private describeNumber (value: double) : string =
    value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

/// A canonical, readable rendering of one lowered instruction for the snapshot.
let rec private describeInstruction (instruction: LoweredInstructions.Instruction) : string =
    match instruction with
    | LoweredInstructions.LiteralNumber value -> "literal-number " + describeNumber value
    | LoweredInstructions.LiteralText value -> "literal-text " + value
    | LoweredInstructions.PushSlot slot -> "push-slot " + string slot
    | LoweredInstructions.StoreSlot slot -> "store-slot " + string slot
    | LoweredInstructions.CallWord(name, None) -> "call-word " + name
    | LoweredInstructions.CallWord(name, Some guard) -> "call-word " + name + " guard-epoch " + string guard.Epoch
    | LoweredInstructions.Apply -> "apply"
    | LoweredInstructions.Duplicate -> "duplicate"
    | LoweredInstructions.Swap -> "swap"
    | LoweredInstructions.Drop -> "drop"
    | LoweredInstructions.Over -> "over"
    | LoweredInstructions.Rotate -> "rotate"
    | LoweredInstructions.PushRoot name -> "push-root " + name
    | LoweredInstructions.SetRoot name -> "set-root " + name
    | LoweredInstructions.LookupPair root -> "lookup-pair " + root
    | LoweredInstructions.AddPair root -> "add-pair " + root
    | LoweredInstructions.DefineWord -> "define-word"
    | LoweredInstructions.MakeClosure(name, captures, arity) ->
        "make-closure "
        + name
        + " captures "
        + String.concat "," (captures |> List.map string)
        + " arity "
        + string arity
    | LoweredInstructions.CallClosure -> "call-closure"
    | LoweredInstructions.PushCapture index -> "push-capture " + string index
    | LoweredInstructions.TensorRank -> "tensor-rank"
    | LoweredInstructions.TensorSize -> "tensor-size"
    | LoweredInstructions.TensorItem -> "tensor-item"
    | LoweredInstructions.TensorEntry -> "tensor-entry"
    | LoweredInstructions.TextRead -> "text-read"
    | LoweredInstructions.TextEquals -> "text-equals"
    | LoweredInstructions.IsText -> "is-text"
    | LoweredInstructions.Add -> "add"
    | LoweredInstructions.Subtract -> "subtract"
    | LoweredInstructions.Multiply -> "multiply"
    | LoweredInstructions.Divide -> "divide"
    | LoweredInstructions.LessThan -> "less-than"
    | LoweredInstructions.GreaterThan -> "greater-than"
    | LoweredInstructions.Equals -> "equals"
    | LoweredInstructions.Jump target -> "jump " + string target
    | LoweredInstructions.JumpIfFalse target -> "jump-if-false " + string target
    | LoweredInstructions.Label label -> "label " + string label
    | LoweredInstructions.Fail reason -> "fail " + reason
    | LoweredInstructions.MakeCode body ->
        "make-code ["
        + (body |> List.map describeInstruction |> String.concat "; ")
        + "]"

/// Exact-text regression for the emitter artifact, complementing D5 rather
/// than replacing it. JIT only: the NativeAOT binary does not run the granular
/// suite (D23).
[<Tests>]
let emitterSnapshotTests =
    testList
        "Emitter (snapshot)"
        [
            testTask "emitted sample matches the committed baseline" {
                let (vocabularyRoot, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let imageWithRoot = Store.setVocabularyTable vocabularyRoot store

                let vocabulary =
                    match VocabularyInImage.readVocabulary (Some vocabularyRoot) imageWithRoot with
                    | Ok entries -> entries
                    | Error message -> failtestf "vocabulary read failed: %s" message

                let tokens = Tokenizer.tokenize vocabulary "Every player that is alive can jump."

                match Parser.parse vocabulary tokens with
                | Some parsed ->
                    do!
                        Verifier
                            .Verify("emitted sample", Emitter.emit vocabulary parsed, snapshotSettings "emitter-sample")
                            .ToTask()
                | None -> failtest "the sample sentence must parse"
            }
            testTask "transition artifact matches the committed baseline" {
                let (vocabularyRoot, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let imageWithRoot = Store.setVocabularyTable vocabularyRoot store

                let vocabulary =
                    match VocabularyInImage.readVocabulary (Some vocabularyRoot) imageWithRoot with
                    | Ok entries -> entries
                    | Error message -> failtestf "vocabulary read failed: %s" message

                match
                    Parser.parse
                        vocabulary
                        (Tokenizer.tokenize vocabulary "When player is alive, every player can jump.")
                with
                | Some parsed ->
                    do!
                        Verifier
                            .Verify(
                                "transition sample",
                                Emitter.emitFSharp
                                    (Emitter.selfContainedNamed "Scale.Generated.Transition" vocabulary)
                                    parsed
                                    ""
                                    [],
                                snapshotSettings "transition-sample"
                            )
                            .ToTask()
                | None -> failtest "the transition sentence must parse"
            }
            testTask "possibility artifact matches the committed baseline" {
                let (vocabularyRoot, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let imageWithRoot = Store.setVocabularyTable vocabularyRoot store

                let vocabulary =
                    match VocabularyInImage.readVocabulary (Some vocabularyRoot) imageWithRoot with
                    | Ok entries -> entries
                    | Error message -> failtestf "vocabulary read failed: %s" message

                match Parser.parse vocabulary (Tokenizer.tokenize vocabulary "Some player that is alive can jump.") with
                | Some parsed ->
                    do!
                        Verifier
                            .Verify(
                                "possibility sample",
                                Emitter.emitFSharp
                                    (Emitter.selfContainedNamed "Scale.Generated.Possibility" vocabulary)
                                    parsed
                                    ""
                                    [],
                                snapshotSettings "possibility-sample"
                            )
                            .ToTask()
                | None -> failtest "the possibility sentence must parse"
            }
            testTask "numeric sample matches the committed baseline" {
                let (vocabularyRoot, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let imageWithRoot = Store.setVocabularyTable vocabularyRoot store

                let vocabulary =
                    match VocabularyInImage.readVocabulary (Some vocabularyRoot) imageWithRoot with
                    | Ok entries -> entries
                    | Error message -> failtestf "vocabulary read failed: %s" message

                let numericParsed: ParsedSentence =
                    {
                        Pattern = Capability
                        Subject = "player"
                        Conditions =
                            [
                                BooleanCondition "isAlive"
                                NumericCondition("health", ComparisonGreaterThan, 0.0)
                            ]
                        Verb = "canJump"
                    }

                do!
                    Verifier
                        .Verify(
                            "numeric sample",
                            Emitter.emitFSharp
                                (Emitter.selfContainedNamed "Scale.Generated.Numeric" vocabulary)
                                numericParsed
                                ""
                                [],
                            snapshotSettings "numeric-sample"
                        )
                        .ToTask()
            }
            testTask "decision-table promotion matches the committed baseline" {
                let table: DecisionTable.Table<string> =
                    {
                        Name = "jump-or-rest"
                        Inputs = DemoDomain.smtFields
                        Policy = DecisionTable.Unique
                        Rows =
                            [
                                {
                                    Guards = [ ScaleFile.AtLeast("health", 2.0) ]
                                    Outcome = "jump"
                                }
                                {
                                    Guards = [ ScaleFile.AtMost("health", 1.0) ]
                                    Outcome = "rest"
                                }
                            ]
                    }

                let tableSubject = "player"
                let tableVerb = "jump"
                let tableBody = fun _ -> DemoDomain.canJumpExpression

                let binding =
                    { Emitter.selfContainedNamed "Scale.Generated.DecisionTable" Vocabulary.gameVocabulary with
                        FieldName = Emitter.Explicit(Map.empty, Emitter.Identity)
                        FieldAccess =
                            Emitter.Explicit(
                                Map.ofList
                                    [
                                        "alive", "player.Alive"
                                        "isAlive", "player.Alive"
                                        "health", "player.Health"
                                    ],
                                Emitter.Prefix "player."
                            )
                        GeneratedRecord = false
                        DomainDefault = false
                        Opens = [ "open DemoDomain" ]
                        Smt =
                            Some
                                {
                                    Lattice = DemoDomain.lattice
                                    VerbBody = DemoDomain.canJumpExpression
                                }
                    }

                do!
                    Verifier
                        .Verify(
                            "decision table",
                            DecisionTableEmitter.emit binding tableSubject tableVerb tableBody table,
                            snapshotSettings "decision-table"
                        )
                        .ToTask()
            }
            testTask "lowered differential corpus matches the committed baseline" {
                let rendered =
                    Differential.cases
                    |> List.map (fun case ->
                        case.Name
                        + "\n"
                        + (LoweredInstructions.lowerTokens case.Program
                           |> List.map describeInstruction
                           |> String.concat "\n"))
                    |> String.concat "\n\n"

                do! Verifier.Verify("lowered corpus", rendered, snapshotSettings "lowered-corpus").ToTask()
            }
            testTask "a prose function value lowers to a stable closure shape" {
                match ProseParser.parseProgram Vocabulary.gameVocabulary "5. logic (x) = add(x 1) close. apply." with
                | Error error -> failtestf "the prose function should parse: %s" (ProseParser.describeError error)
                | Ok expression ->
                    match EvaluatorLowering.lowerExpression expression with
                    | Error refusal ->
                        failtestf "the prose function should lower: %s" (EvaluatorLowering.describeRefusal refusal)
                    | Ok program ->
                        let rendered =
                            (program.Units
                             |> List.map (fun unit ->
                                 unit.Name
                                 + " arity "
                                 + string unit.Arity
                                 + "\n"
                                 + (unit.Instructions |> List.map describeInstruction |> String.concat "\n")))
                            @ (program.Instructions |> List.map describeInstruction)
                            |> String.concat "\n"

                        do!
                            Verifier
                                .Verify("prose function lowering", rendered, snapshotSettings "prose-function-lowering")
                                .ToTask()
            }
            testTask "a prose construction lowers to a compound term" {
                match
                    ProseParser.parseBook Vocabulary.gameVocabulary "shape point with x y.\nlogic main = point(1 2).\n"
                with
                | Error error -> failtestf "the prose construction should parse: %s" (ProseParser.describeError error)
                | Ok document ->
                    match document.Definitions with
                    | [ definition ] ->
                        match EvaluatorLowering.lowerExpression definition.Body with
                        | Error refusal ->
                            failtestf
                                "the prose construction should lower: %s"
                                (EvaluatorLowering.describeRefusal refusal)
                        | Ok program ->
                            let rendered =
                                program.Instructions |> List.map describeInstruction |> String.concat "\n"

                            do!
                                Verifier
                                    .Verify(
                                        "prose construction lowering",
                                        rendered,
                                        snapshotSettings "prose-nominal-constructor"
                                    )
                                    .ToTask()
                    | _ -> failtest "the book should declare one definition"
            }
        ]

/// A fixed-spec bundle with a fixed diagnostic and solver model, so the wire
/// format is deterministic and can be pinned by Verify.
let private fixedSpecBundle () : FixedSpecificationAcceptance.AcceptanceBundle<DemoDomain.Player> =
    let sentence: ParsedSentence =
        {
            Pattern = Capability
            Subject = "player"
            Conditions = [ BooleanCondition "isAlive" ]
            Verb = "canJump"
        }

    let document: ScaleFile.ScaleDocument =
        {
            Specification = "Every player that is alive can jump."
            Refinements = []
        }

    let witness: FixedSpecificationAcceptance.Witness<DemoDomain.Player> =
        {
            Input = { Alive = true; Health = 1 }
            Values = [ "alive", ScaleFile.BooleanValue true; "health", ScaleFile.NumericValue 1.0 ]
        }

    let diagnostic: Refiner.Diagnostic =
        {
            Tests = 4
            Shrinks = 1
            Values = [ "true"; "1" ]
            Seed = "test-seed"
        }

    let model =
        Map.ofList [ "alive", Smt.BooleanValue true; "health", Smt.IntegerValue 1 ]

    FixedSpecificationAcceptance.makeBundle
        1
        "player canJump"
        sentence
        document
        witness
        diagnostic
        (Some model)
        DemoDomain.canJumpExpression
        (DomainExpression.render DemoDomain.canJumpExpression)
        "1/1 witnesses agree"
        { File = "demo/player.scale"; Line = 7 }

/// A numeric-condition bundle, so the wire format pins a numeric witness and a
/// numeric solver model as well as the boolean one.
let private numericSpecBundle () : FixedSpecificationAcceptance.AcceptanceBundle<DemoDomain.Player> =
    let sentence: ParsedSentence =
        {
            Pattern = Capability
            Subject = "player"
            Conditions =
                [
                    BooleanCondition "isAlive"
                    NumericCondition("health", ComparisonGreaterThan, 1.0)
                ]
            Verb = "canJump"
        }

    let document: ScaleFile.ScaleDocument =
        {
            Specification = "Every player that is alive can jump."
            Refinements =
                [
                    {
                        Guard = ScaleFile.AtLeast("health", 2.0)
                        Origin = "solver"
                        Counterexample = "alive=true, health=1"
                    }
                ]
        }

    let witness: FixedSpecificationAcceptance.Witness<DemoDomain.Player> =
        {
            Input = { Alive = true; Health = 1 }
            Values = [ "alive", ScaleFile.BooleanValue true; "health", ScaleFile.NumericValue 1.0 ]
        }

    let diagnostic: Refiner.Diagnostic =
        {
            Tests = 4
            Shrinks = 1
            Values = [ "true"; "1" ]
            Seed = "numeric-seed"
        }

    let model =
        Map.ofList [ "alive", Smt.BooleanValue true; "health", Smt.IntegerValue 1 ]

    FixedSpecificationAcceptance.makeBundle
        1
        "player canJump"
        sentence
        document
        witness
        diagnostic
        (Some model)
        DemoDomain.canJumpExpression
        (DomainExpression.render DemoDomain.canJumpExpression)
        "1/1 witnesses agree"
        { File = "demo/player.scale"; Line = 7 }

/// Snapshot tests for the pipeline's wire formats: the emitted artifact for a
/// subject with no conditions, the repair bundle a generator consumes, the
/// escaped `.scale` document, and their numeric-condition counterparts.
[<Tests>]
let pipelineSnapshotTests =
    testList
        "Pipeline (snapshot)"
        [
            testTask "no-condition artifact matches the committed baseline" {
                let (vocabularyRoot, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let vocabulary =
                    match VocabularyInImage.readVocabulary (Some vocabularyRoot) store with
                    | Ok entries -> entries
                    | Error message -> failtestf "vocabulary read failed: %s" message

                let parsed =
                    match Parser.parse vocabulary (Tokenizer.tokenize vocabulary "Every player can jump.") with
                    | Some sentence -> sentence
                    | None -> failtest "the no-condition sentence must parse"

                do!
                    Verifier
                        .Verify(
                            "no condition sample",
                            Emitter.emitFSharp
                                (Emitter.selfContainedNamed "Scale.Generated.NoCondition" vocabulary)
                                parsed
                                ""
                                [],
                            snapshotSettings "no-condition-sample"
                        )
                        .ToTask()
            }
            testTask "repair bundle wire format matches the committed baseline" {
                do!
                    Verifier
                        .Verify(
                            "repair bundle",
                            FixedSpecificationAcceptance.bundleJson (fixedSpecBundle ()),
                            snapshotSettings "repair-bundle"
                        )
                        .ToTask()
            }
            testTask "escaped document matches the committed baseline" {
                let document: ScaleFile.ScaleDocument =
                    {
                        Specification = "Every player that is alive can jump."
                        Refinements =
                            [
                                {
                                    Guard = ScaleFile.AtLeast("health", 1.0)
                                    Origin = "x\nspecification: No player can jump."
                                    Counterexample = "alive=true, health=1"
                                }
                            ]
                    }

                do!
                    Verifier
                        .Verify("escaped document", ScaleFile.render document, snapshotSettings "scale-document")
                        .ToTask()
            }
            testTask "numeric refinement document matches the committed baseline" {
                let document: ScaleFile.ScaleDocument =
                    {
                        Specification = "Every player that is alive can jump."
                        Refinements =
                            [
                                {
                                    Guard = ScaleFile.AtLeast("health", 2.0)
                                    Origin = "solver"
                                    Counterexample = "alive=true, health=1"
                                }
                            ]
                    }

                do!
                    Verifier
                        .Verify(
                            "numeric refinement document",
                            ScaleFile.render document,
                            snapshotSettings "scale-document-numeric"
                        )
                        .ToTask()
            }
            testTask "numeric repair bundle matches the committed baseline" {
                do!
                    Verifier
                        .Verify(
                            "numeric repair bundle",
                            FixedSpecificationAcceptance.bundleJson (numericSpecBundle ()),
                            snapshotSettings "repair-bundle-numeric"
                        )
                        .ToTask()
            }
        ]

/// Integration tests over the assembled pipeline: the fixed-spec run from the
/// CLI's entry point, and the refinement loop end to end.
[<Tests>]
let pipelineIntegrationTests =
    testList
        "Pipeline (integration)"
        [
            testSequenced (
                testCase "the fixed-spec run writes a bundle and never edits the specification" (fun () ->
                    let bundlePath =
                        System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            "scale-integration-" + System.Guid.NewGuid().ToString("N") + ".json"
                        )

                    let specBefore = ScaleFile.read (Demo.specificationPath ())

                    try
                        match Demo.runFixedSpecification bundlePath with
                        | Ok(Demo.Witnessed path) ->
                            expectEqual bundlePath path "the run reports its bundle path"
                            Expect.isTrue (System.IO.File.Exists path) "the bundle exists"

                            use document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText path)
                            let root = document.RootElement

                            expectEqual 1 (root.GetProperty("version").GetInt32()) "the bundle version"
                            expectEqual "player canJump" (root.GetProperty("property").GetString()) "the property"

                            expectEqual
                                "1/1 witnesses agree"
                                (root.GetProperty("differential").GetString())
                                "the differential"

                            expectEqual
                                (ScaleFile.hash
                                    {
                                        Specification = "Every player that is alive can jump."
                                        Refinements = []
                                    })
                                (root.GetProperty("specHash").GetString())
                                "the bundle pins the fixed specification's hash"
                        | Ok _ -> failtest "the demonstration target should be falsified"
                        | Error reason -> failtestf "the fixed-spec run failed: %s" reason

                        expectEqual
                            specBefore
                            (ScaleFile.read (Demo.specificationPath ()))
                            "the fixed-spec run never edits the specification"
                    finally
                        if System.IO.File.Exists bundlePath then
                            System.IO.File.Delete bundlePath)
            )

            testCase "a specification proposal needs the exploratory capability and the original document" (fun () ->
                let document: ScaleFile.ScaleDocument =
                    {
                        Specification = "Every player can jump."
                        Refinements = []
                    }

                let refinement: ScaleFile.Refinement =
                    {
                        Guard = ScaleFile.GreaterThan("health", 1.0)
                        Origin = "test"
                        Counterexample = "alive=true, health=1"
                    }

                match
                    FixedSpecificationAcceptance.specChangeCapability
                        FixedSpecificationAcceptance.FixedSpecification
                        document
                with
                | Ok _ -> failtest "fixed mode must not grant a specification-change capability"
                | Error message -> Expect.isTrue (message.Contains "never edits") "fixed mode refuses the capability"

                match
                    FixedSpecificationAcceptance.specChangeCapability FixedSpecificationAcceptance.Exploratory document
                with
                | Error message -> failtestf "exploratory mode refused its capability: %s" message
                | Ok capability ->
                    let proposal =
                        FixedSpecificationAcceptance.proposeSpecification capability refinement

                    match FixedSpecificationAcceptance.appendSpecification proposal document with
                    | Ok appended -> expectEqual 1 appended.Refinements.Length "the proposal appends one refinement"
                    | Error message -> failtestf "the exploratory proposal failed: %s" message

                    let changed =
                        { document with
                            Specification = document.Specification + " changed"
                        }

                    match FixedSpecificationAcceptance.appendSpecification proposal changed with
                    | Error message ->
                        Expect.isTrue
                            (message.Contains "different specification")
                            "a changed document refuses the proposal"
                    | Ok _ -> failtest "a proposal must not append to a different document")

            testSequenced (
                testCase "the refinement loop runs the compiled artifacts end to end" (fun () ->
                    let report = Demo.runLoop ()

                    Expect.isTrue report.InitialFalsified "the pristine property is falsified"

                    expectEqual
                        (Some(ScaleFile.GreaterThan("health", 1.0)))
                        report.Refinement
                        "the refiner selects health > 1"

                    Expect.isTrue report.RefinedHolds "the refined property holds"
                    expectEqual Smt.Proved report.RefinedOutcome "the refined obligation is proved"

                    match report.InitialOutcome with
                    | Smt.Falsified _ -> ()
                    | other ->
                        failtestf "the initial obligation should be falsified, got %s" (Smt.describeOutcome other)

                    expectEqual
                        report.Specification
                        "Every player that is alive can jump."
                        "the loop starts from the pristine sentence")
            )

            testSequenced (
                testCase "a prose file applies a function value end to end" (fun () ->
                    let path =
                        System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            "scale-prose-function-" + System.Guid.NewGuid().ToString("N") + ".prose"
                        )

                    System.IO.File.WriteAllText(path, "5. logic (x) = add(x 1) close. apply.")

                    try
                        match ProseShell.runFileWith [] path with
                        | ProseShell.ProseCompleted(ProseShell.ProseNumber value, 0) ->
                            expectEqual 6.0 value "the file applies its function value"
                        | ProseShell.ProseCompleted(value, status) ->
                            failtestf "the prose file completed with %A and status %d" value status
                        | ProseShell.ProseRefused(reason, status) ->
                            failtestf "the prose file was refused with %s and status %d" reason status
                    finally
                        if System.IO.File.Exists path then
                            System.IO.File.Delete path)
            )
        ]

/// Integration tests for the actor model and the shared violation currency.
/// These cross stages: supervision + links + mailboxes, actors + persistence,
/// and the parser + SMT into one `Violation`.
[<Tests>]
let actorIntegrationTests =
    testList
        "Actor and pipeline (integration)"
        [
            testCase "a supervisor traps a link, then messages a replacement" (fun () ->
                let (root, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let state = Evaluator.initial store root

                let program =
                    [
                        "1"
                        "trap-exit"
                        "1"
                        "0"
                        "0"
                        "one-for-one"
                        "{"
                        "missing"
                        "}"
                        "\"temporary\""
                        "supervise"
                        "duplicate"
                        "link"
                        "yield"
                        "receive-message"
                        "exit-reason"
                        "print"
                        "{"
                        "receive-message"
                        "print"
                        "}"
                        "\"temporary\""
                        "supervise"
                        "99"
                        "swap"
                        "send-to"
                        "yield"
                    ]

                match
                    Evaluator.run
                        { state with
                            Program = EvaluatorProgram.ofTokensOrFail (program)
                        }
                with
                | Ok final ->
                    let outputs =
                        final.SchedulerState.Actors
                        |> Map.toList
                        |> List.collect (fun (_, actor) -> actor.State.Output)

                    expectEqual [ "1"; "99" ] outputs "trap, supervise, and mailbox cooperate"
                | Error diagnostic -> failtestf "the actor integration failed: %s" (describeError diagnostic.Error))

            testCase "the actor model, written out" (fun () ->
                printfn "  Scenario: one supervisor and one echo worker."
                printfn "  Root: one-for-one policy, traps exits; spawns and registers the echo worker,"
                printfn "  monitors it, sends 7, then receives the normal-exit Down and its reason."

                let (root, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let state = Evaluator.initial store root

                let program =
                    [
                        // Policy: at most 2 restarts in 10 tokens, no deadline.
                        "2"
                        "10"
                        "0"
                        "one-for-one"
                        // Trap link exits so the failure below becomes a message.
                        "1"
                        "trap-exit"
                        // A = { receive-message print }, supervised as temporary.
                        "{"
                        "receive-message"
                        "print"
                        "}"
                        "\"temporary\""
                        "supervise"
                        // register("echo", A), monitor A, keep the reference.
                        "\"echo\""
                        "register"
                        "\"echo\""
                        "whereis"
                        "monitor"
                        "duplicate"
                        // send 7 to whereis("echo"), then let A run.
                        "\"echo\""
                        "whereis"
                        "7"
                        "swap"
                        "send-to"
                        "yield"
                        // Down message: is it a Down, and what was the reason?
                        "receive-message"
                        "duplicate"
                        "down?"
                        "print"
                        "down-reason"
                        "print"
                        "drop"
                        "drop"
                    ]

                match
                    Evaluator.run
                        { state with
                            Program = EvaluatorProgram.ofTokensOrFail (program)
                        }
                with
                | Error diagnostic -> failtestf "the written scenario failed: %s" (describeError diagnostic.Error)
                | Ok final ->
                    let self = final.SchedulerState.SelfId

                    let transcript =
                        (final.Output
                         |> List.map (fun line -> sprintf "actor %d: %s" (EntryIdentity.value self) line))
                        @ (final.SchedulerState.Actors
                           |> Map.toList
                           |> List.filter (fun (identity, _) -> identity <> self)
                           |> List.collect (fun (identity, actor) ->
                               actor.State.Output
                               |> List.map (fun line -> sprintf "actor %d: %s" (EntryIdentity.value identity) line)))

                    printfn "  Transcript:"
                    transcript |> List.iter (printfn "    %s")

                    expectEqual
                        [ "actor 0: 1"; "actor 0: 0"; "actor 1: 7" ]
                        transcript
                        "the actor transcript is exactly the written scenario"

                    expectEqual false (Map.containsKey "echo" final.SchedulerState.Registry) "the echo name is removed"
                    expectEqual 0 (Map.count final.SchedulerState.Monitors) "the monitor is consumed"
                    expectEqual 1 (Map.count final.SchedulerState.ActorHandles) "only the root handle remains")

            testCase "a mailbox queue survives persistence; handles do not" (fun () ->
                let (root, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let state = Evaluator.initial store root

                let (mailbox, store1, sched) =
                    Scheduler.createMailbox state.Store state.SchedulerState

                let (messageId, store2) = Store.addTensor (Tensor.scalar 7.0) store1

                match Scheduler.deliverMessage mailbox messageId store2 sched with
                | Error error -> failtestf "mailbox delivery failed: %s" (describeError error)
                | Ok(queuedStore, _) ->
                    let restored = Persistence.fromBytes (Persistence.toBytes queuedStore)
                    let isOpen, queued = ChannelStore.readChannelState mailbox restored
                    Expect.isTrue isOpen "a restored mailbox is open"
                    expectEqual [ messageId ] queued "a queued message survives persistence"

                    let reloaded = Evaluator.initial restored root
                    expectEqual 1 (Map.count reloaded.SchedulerState.ActorHandles) "handles are per run")

            testCase "two hosts exchange a number over a Unix socket" (fun () ->
                let path =
                    System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "scale-ipc-" + System.Guid.NewGuid().ToString("N") + ".sock"
                    )

                use ready = new System.Threading.ManualResetEventSlim(false)

                let server =
                    System.Threading.Tasks.Task.Run(fun () -> ActorHost.serveOnce path (fun () -> ready.Set()))

                try
                    if not (ready.Wait(5000)) then
                        failtest "the IPC listener did not become ready"

                    match ActorHost.connect path 41.0 with
                    | Ok value -> expectEqual 42.0 value "the remote actor adds one"
                    | Error error -> failtestf "client: %s" (ActorHost.describeHostError error)

                    match server.Result with
                    | Ok value -> expectEqual 42.0 value "the listener actor returns the same result"
                    | Error error -> failtestf "server: %s" (ActorHost.describeHostError error)

                    let envelope: Wire.Envelope =
                        {
                            MessageId = 7UL
                            Target = 0UL
                            Sender = 0UL
                            Reply = 0UL
                            Operation = Wire.Send
                            Body = Wire.Number 4.5
                        }

                    match Wire.encode envelope with
                    | Error error -> failtestf "wire encode: %s" (Wire.describeError error)
                    | Ok bytes ->
                        match Wire.decode bytes with
                        | Ok decoded ->
                            expectEqual envelope.MessageId decoded.MessageId "the message id round-trips"

                            match decoded.Body with
                            | Wire.Number value -> expectEqual 4.5 value "the numeric body round-trips"
                            | Wire.Text _ -> failtest "the numeric body became text"
                        | Error error -> failtestf "wire decode: %s" (Wire.describeError error)

                        match Wire.decode (Array.truncate 4 bytes) with
                        | Error Wire.TruncatedFrame -> ()
                        | _ -> failtest "a truncated message is refused"

                        bytes.[8] <- 3uy

                        match Wire.decode bytes with
                        | Error Wire.InvalidRootPointer -> ()
                        | _ -> failtest "a non-struct root pointer is refused"
                finally
                    try
                        System.IO.File.Delete path
                    with _ ->
                        ())
            testCase "a worker capability replies and reports its remote exit" (fun () ->
                let path =
                    System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "scale-ipc-worker-" + System.Guid.NewGuid().ToString("N") + ".sock"
                    )

                use ready = new System.Threading.ManualResetEventSlim(false)

                let server =
                    System.Threading.Tasks.Task.Run(fun () -> ActorHost.serveSupervised path (fun () -> ready.Set()))

                try
                    if not (ready.Wait(5000)) then
                        failtest "the supervised listener did not become ready"

                    match ActorHost.connectSupervised path 21.0 with
                    | Ok(reply, down, monitor) ->
                        expectEqual 42.0 reply "the worker capability multiplies by two"
                        expectEqual "down normal" down "the worker exit reaches the client"
                        Expect.isGreaterThan monitor 0 "the monitor has a reference"
                    | Error error -> failtestf "client: %s" (ActorHost.describeHostError error)

                    match server.Result with
                    | Ok(reply, down, monitor) ->
                        expectEqual 42.0 reply "the listener worker returns the same result"
                        expectEqual "down normal" down "the listener reports the normal exit"
                        Expect.isGreaterThan monitor 0 "the listener monitor has a reference"
                    | Error error -> failtestf "server: %s" (ActorHost.describeHostError error)

                    match ActorHost.connectSupervised (path + ".missing") 1.0 with
                    | Error(ActorHost.Unreachable _) -> ()
                    | _ -> failtest "an unreachable instance is a named failure"
                finally
                    try
                        System.IO.File.Delete path
                    with _ ->
                        ())
            testCase "the language sends to a remote handle and receives the reply" (fun () ->
                let path =
                    System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "scale-ipc-language-" + System.Guid.NewGuid().ToString("N") + ".sock"
                    )

                use ready = new System.Threading.ManualResetEventSlim(false)

                let server =
                    System.Threading.Tasks.Task.Run(fun () -> ActorHost.serveSupervised path (fun () -> ready.Set()))

                try
                    if not (ready.Wait(5000)) then
                        failtest "the IPC listener did not become ready"

                    let (root, store0) =
                        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                    let state = Evaluator.initial store0 root

                    let (handleId, store1) =
                        Store.addTensor (Tensor.vector [ RemoteHandleTag; 0.0; 1.0 ]) state.Store

                    let (messageId, store2) = Store.addTensor (Tensor.scalar 21.0) store1

                    let seeded =
                        { state with
                            Store = store2
                            SchedulerState = Scheduler.registerRemoteHandle handleId state.SchedulerState
                            Stack = Stack.push handleId (Stack.push messageId Stack.empty)
                            Program = EvaluatorProgram.ofTokensOrFail ([ "send-to"; "receive-message" ])
                        }

                    match Evaluator.run seeded with
                    | Error diagnostic -> failtestf "the remote send failed: %s" (describeError diagnostic.Error)
                    | Ok final ->
                        match final.SchedulerState.PendingTransport with
                        | [ request ] ->
                            expectEqual 0 request.Instance "the remote instance is recorded"
                            expectEqual 1 request.Capability "the remote capability is recorded"

                            expectEqual
                                (EntryIdentity.value messageId)
                                (EntryIdentity.value request.Message)
                                "the message entry is recorded"

                            expectEqual 0 (EntryIdentity.value request.Sender) "the sender is recorded"

                            match Store.lookupTensor request.Message final.Store with
                            | Some tensor ->
                                match ScalarRead.readScalar tensor with
                                | Ok value -> expectEqual 21.0 value "the host can read the recorded message"
                                | Error reason -> failtestf "the message is not numeric: %s" reason
                            | None -> failtest "the recorded message is not in the store"

                            match LanguageTransport.deliverPending path final with
                            | Error error -> failtestf "transport: %s" (ActorHost.describeHostError error)
                            | Ok(replied, results) ->
                                match results with
                                | [ (reply, down, monitor) ] ->
                                    expectEqual 42.0 reply "the remote worker adds one"
                                    expectEqual "down normal" down "the remote worker reports its exit"
                                    Expect.isGreaterThan monitor 0 "the remote monitor has a reference"
                                | other -> failtestf "expected one transport result, got %d" (List.length other)

                                match Stack.popResolved replied.Store replied.Stack with
                                | Some(tensor, _) ->
                                    match ScalarRead.readScalar tensor with
                                    | Ok value -> expectEqual 42.0 value "the language receives the reply"
                                    | Error reason -> failtestf "the reply is not numeric: %s" reason
                                | None -> failtest "the language left no reply on the stack"

                                expectEqual
                                    0
                                    (List.length replied.SchedulerState.PendingTransport)
                                    "the transport queue is drained"
                        | other -> failtestf "expected one transport request, got %d" (List.length other)

                    match server.Result with
                    | Ok(reply, down, _monitor) ->
                        expectEqual 42.0 reply "the listener returns the same reply"
                        expectEqual "down normal" down "the listener returns the same exit"
                    | Error error -> failtestf "server: %s" (ActorHost.describeHostError error)
                finally
                    try
                        System.IO.File.Delete path
                    with _ ->
                        ())
            testCase "the wire golden vectors decode against the reference" (fun () ->
                let directory =
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "wire-vectors")

                let read name =
                    let path = System.IO.Path.Combine(directory, name + ".bin")

                    if System.IO.File.Exists path then
                        Ok(System.IO.File.ReadAllBytes path)
                    else
                        Error $"missing vector {path}"

                let numeric =
                    [
                        "number", 1UL, 0UL, 0UL, 0UL, Wire.Send, 4.5
                        "monitor", 3UL, 1UL, 0UL, 0UL, Wire.Monitor, 0.0
                        "monitor-ack", 4UL, 0UL, 1UL, 0UL, Wire.MonitorAck, 1.0
                        "reply", 5UL, 0UL, 1UL, 0UL, Wire.Reply, 42.0
                        "defaults", 0UL, 0UL, 0UL, 0UL, Wire.Send, 0.0
                    ]

                for (name, messageId, target, sender, reply, operation, value) in numeric do
                    match read name with
                    | Error reason -> failtest reason
                    | Ok bytes ->
                        match Wire.decode bytes with
                        | Error error -> failtestf "%s: %s" name (Wire.describeError error)
                        | Ok envelope ->
                            expectEqual messageId envelope.MessageId $"{name}: message id"
                            expectEqual target envelope.Target $"{name}: target"
                            expectEqual sender envelope.Sender $"{name}: sender"
                            expectEqual reply envelope.Reply $"{name}: reply"
                            expectEqual operation envelope.Operation $"{name}: operation"

                            match envelope.Body with
                            | Wire.Number number -> expectEqual value number $"{name}: number"
                            | Wire.Text _ -> failtestf "%s: expected a numeric body" name

                            match Wire.encode envelope with
                            | Error error -> failtestf "%s: encode: %s" name (Wire.describeError error)
                            | Ok encoded ->
                                expectEqual bytes encoded $"{name}: the F# encoder matches the reference bytes"

                match read "text" with
                | Error reason -> failtest reason
                | Ok bytes ->
                    match Wire.decode bytes with
                    | Error error -> failtestf "text: %s" (Wire.describeError error)
                    | Ok envelope ->
                        expectEqual 2UL envelope.MessageId "text: message id"
                        expectEqual 1UL envelope.Target "text: target"
                        expectEqual 0UL envelope.Sender "text: sender"
                        expectEqual Wire.Down envelope.Operation "text: operation"

                        match envelope.Body with
                        | Wire.Text value -> expectEqual "down normal" value "text: body"
                        | Wire.Number _ -> failtest "text: expected a text body"

                        match Wire.encode envelope with
                        | Error error -> failtestf "text: encode: %s" (Wire.describeError error)
                        | Ok encoded -> expectEqual bytes encoded "text: the F# encoder matches the reference bytes"

                let expectRefusal name predicate =
                    match read name with
                    | Error reason -> failtest reason
                    | Ok bytes ->
                        match Wire.decode bytes with
                        | Error error when predicate error -> ()
                        | Error error -> failtestf "%s: unexpected %s" name (Wire.describeError error)
                        | Ok _ -> failtestf "%s: expected a refusal" name

                expectRefusal "truncated" (fun error -> error = Wire.TruncatedFrame)
                expectRefusal "bad-root" (fun error -> error = Wire.InvalidRootPointer)

                expectRefusal "bad-operation" (function
                    | Wire.InvalidOperation _ -> true
                    | _ -> false)

                expectRefusal "bad-body-kind" (function
                    | Wire.InvalidBodyKind _ -> true
                    | _ -> false)

                expectRefusal "missing-text" (fun error -> error = Wire.MissingTextPointer)
                expectRefusal "text-no-nul" (fun error -> error = Wire.TextMissingTerminator)
                expectRefusal "text-out-of-bounds" (fun error -> error = Wire.TextOutOfBounds)

                expectRefusal "multi-segment" (function
                    | Wire.SegmentCountUnsupported _ -> true
                    | _ -> false))
            testCase "an unregistered remote handle is not a capability" (fun () ->
                let (root, store0) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let state = Evaluator.initial store0 root

                let (handleId, store1) =
                    Store.addTensor (Tensor.vector [ RemoteHandleTag; 0.0; 1.0 ]) state.Store

                let (messageId, store2) = Store.addTensor (Tensor.scalar 21.0) store1

                let seeded =
                    { state with
                        Store = store2
                        Stack = Stack.push handleId (Stack.push messageId Stack.empty)
                        Program = EvaluatorProgram.ofTokensOrFail ([ "send-to" ])
                    }

                match Evaluator.run seeded with
                | Ok _ -> failtest "an unregistered remote handle must be refused"
                | Error diagnostic ->
                    match diagnostic.Error with
                    | TypeMismatch(_, ExpectedActorHandle) -> ()
                    | other -> failtestf "unexpected refusal: %s" (describeError other))
            testCase "the term portable vectors round-trip canonically" (fun () ->
                let directory =
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "term-vectors")

                let read name =
                    let path = System.IO.Path.Combine(directory, name + ".json")

                    if System.IO.File.Exists path then
                        Ok(System.IO.File.ReadAllBytes path)
                    else
                        Error $"missing term vector {path}"

                for name in [ "number-add"; "quote"; "branch"; "closure"; "fail" ] do
                    match read name with
                    | Error reason -> failtest reason
                    | Ok bytes ->
                        match TermPortable.decode bytes with
                        | Error error -> failtestf "%s: %s" name (TermPortable.describeError error)
                        | Ok program ->
                            match TermPortable.encode program with
                            | Error error -> failtestf "%s: encode: %s" name (TermPortable.describeError error)
                            | Ok encoded -> expectEqual bytes encoded $"{name}: canonical bytes"

                for name in
                    [
                        "bad-schema"
                        "bad-form"
                        "unknown-term"
                        "missing-field"
                        "wrong-type"
                        "negative-slot"
                        "negative-capture"
                        "negative-arity"
                    ] do
                    match read name with
                    | Error reason -> failtest reason
                    | Ok bytes ->
                        match TermPortable.decode bytes with
                        | Error _ -> ()
                        | Ok _ -> failtestf "%s: expected a refusal" name)
            testCase "a parse refusal and an SMT falsification share the Violation shape" (fun () ->
                let (vocabularyRoot, store) =
                    VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

                let imageWithRoot = Store.setVocabularyTable vocabularyRoot store

                let vocabulary =
                    match VocabularyInImage.readVocabulary (Some vocabularyRoot) imageWithRoot with
                    | Ok entries -> entries
                    | Error message -> failtestf "vocabulary read failed: %s" message

                let parsed =
                    Violations.parse vocabulary (Tokenizer.tokenize vocabulary "Never can jump.")

                match parsed with
                | Ok _ -> failtest "the forbidden sentence must not parse"
                | Error violation ->
                    expectEqual Violations.Structural violation.Layer "a grammar refusal is structural"
                    expectEqual Violations.Input violation.Blame "a grammar refusal blames the input"
                    Expect.isSome violation.Suggestion "a grammar refusal carries a suggestion"

                let report = Demo.runLoop ()

                match report.InitialOutcome with
                | Smt.Falsified _ ->
                    match Violations.ofSmtOutcome report.InitialOutcome with
                    | Some violation ->
                        expectEqual Violations.Semantic violation.Layer "an SMT falsification is semantic"
                        expectEqual Violations.Input violation.Blame "an SMT falsification blames the input"
                        Expect.isSome violation.Witness "an SMT falsification carries a model"
                    | None -> failtest "a falsified obligation should be a violation"
                | other -> failtestf "the initial obligation should be falsified, got %s" (Smt.describeOutcome other))
        ]

/// Pinned nature counts for the artifact tests. They are the only checks in
/// this suite that the example suite does not already cover.
let snapshotCount = 13
let integrationCount = 14

/// The JIT-only artifact tests: the snapshots and the end-to-end
/// integrations. The example and property cases above are re-runs of the
/// example suite, so the aggregate gate does not run them twice.
let artifactTests =
    testList
        "Scale.Artifacts"
        [
            emitterSnapshotTests
            pipelineSnapshotTests
            pipelineIntegrationTests
            actorIntegrationTests
        ]

/// Runs the tests without printing the runner's name: the summary is by kind,
/// not by runner. Failure detail is forwarded, filtered of the runner's banner.
let private runCaptured (tests: Test) : int * string =
    let originalOut = System.Console.Out
    let originalError = System.Console.Error
    use captured = new System.IO.StringWriter()
    System.Console.SetOut captured
    System.Console.SetError captured

    let code =
        try
            runTestsWithCLIArgs [] [||] tests
        finally
            System.Console.SetOut originalOut
            System.Console.SetError originalError

    code, captured.ToString()

let private printFailureDetail (text: string) : unit =
    text.Split('\n')
    |> Array.filter (fun line ->
        not (
            line.Contains("EXPECTO", System.StringComparison.Ordinal)
            || line.Contains("Expecto", System.StringComparison.Ordinal)
        ))
    |> Array.iter System.Console.Out.WriteLine

/// Counts the leaves of the test tree, so the reported total does not depend
/// on the runner printing its own summary.
let rec private countTests (test: Test) : int =
    match test with
    | Test.TestList(tests, _) -> List.sumBy countTests tests
    | Test.TestLabel(_, inner, _) -> countTests inner
    | Test.Sequenced(_, inner) -> countTests inner
    | Test.TestCase _ -> 1

let runArtifacts () : int =
    let code, text = runCaptured artifactTests

    if code <> 0 then
        printFailureDetail text

    code

[<Tests>]
let aggregateSuite =
    testList
        "Scale.Aggregate"
        [
            testCase "example suite" (fun () ->
                Tests.passed <- 0
                Tests.failed <- 0
                Tests.runAll ()

                if Tests.failed > 0 then
                    failwith $"{Tests.failed} of {Tests.passed + Tests.failed} checks failed")
        ]

#if ENABLE_WRITER
let writerSeed () : KernelState.KernelState<EvaluationState> =
    let (vocabularyRoot, store) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    let state =
        Evaluator.initial (Store.setVocabularyTable vocabularyRoot store) vocabularyRoot

    EvaluatorKernelBridge.toKernel { state with Program = [] }

let seedProgram (instructions: LoweredInstructions.Instruction list) : EvaluatorLowering.LoweredProgram =
    {
        Units = []
        Instructions = instructions
    }

let lowerProgram (expression: ExpressionLogic.Expression) : Result<EvaluatorLowering.LoweredProgram, string> =
    match EvaluatorLowering.lowerExpression expression with
    | Ok program -> Ok program
    | Result.Error refusal -> Result.Error(EvaluatorLowering.describeRefusal refusal)

let number (value: double) =
    ExpressionLogic.Literal(ExpressionLogic.ExpressionLiteral.NumberLiteral value)

let word (name: string) = ExpressionLogic.Word name

let invokeEmitted (program: EvaluatorLowering.LoweredProgram) : Result<double, string> =
    match IlWriter.emit program with
    | Error refusal -> Result.Error(IlWriter.describeRefusal refusal)
    | Ok emitted ->
        let assembly = System.Reflection.Assembly.Load(emitted.AssemblyBytes)
        let generated = assembly.GetType(emitted.TypeName)
        let methodInfo = generated.GetMethod(emitted.MethodName)
        let seed = writerSeed ()

        try
            match methodInfo.Invoke(null, [| box seed |]) with
            | :? KernelState.KernelState<EvaluationState> as result ->
                let host = EvaluatorKernelBridge.fromKernel result

                match Stack.popResolved host.Store host.Stack with
                | Some(tensor, _) -> Ok(value tensor)
                | None -> Result.Error "the emitted method left no value"
            | other -> Result.Error("unexpected emitted result: " + string other)
        with
        | :? System.Reflection.TargetInvocationException as ex ->
            match ex.InnerException with
            | :? AbstractionFault as fault -> Result.Error fault.Reason
            | inner -> Result.Error inner.Message
        | ex -> Result.Error ex.Message

/// D134: one method per unit, and a direct lambda call compiles to a call to
/// that method. The AOT publish sets EnableWriter=false, so this list and the
/// writer itself are absent from the native binary.
[<Tests>]
let writerTests =
    testList
        "IL writer (JIT)"
        [
            testCase "arithmetic" (fun () ->
                match
                    invokeEmitted (
                        seedProgram
                            [
                                LoweredInstructions.LiteralNumber 2.0
                                LoweredInstructions.LiteralNumber 3.0
                                LoweredInstructions.Add
                            ]
                    )
                with
                | Ok value -> expectEqual 5.0 value "the emitted arithmetic returns 5"
                | Error reason -> failtestf "%s" reason)

            testCase "branch" (fun () ->
                let program =
                    [
                        LoweredInstructions.LiteralNumber 1.0
                        LoweredInstructions.JumpIfFalse 0
                        LoweredInstructions.LiteralNumber 10.0
                        LoweredInstructions.Jump 1
                        LoweredInstructions.Label 0
                        LoweredInstructions.LiteralNumber 20.0
                        LoweredInstructions.Label 1
                    ]

                match invokeEmitted (seedProgram program) with
                | Ok value -> expectEqual 10.0 value "the emitted branch takes the true arm"
                | Error reason -> failtestf "%s" reason)

            testCase "stored slot" (fun () ->
                match
                    invokeEmitted (
                        seedProgram
                            [
                                LoweredInstructions.LiteralNumber 7.0
                                LoweredInstructions.StoreSlot 0
                                LoweredInstructions.PushSlot 0
                            ]
                    )
                with
                | Ok value -> expectEqual 7.0 value "the emitted slot round-trips a value"
                | Error reason -> failtestf "%s" reason)

            testCase "call word through the harness" (fun () ->
                match
                    invokeEmitted (
                        seedProgram
                            [
                                LoweredInstructions.LiteralNumber 2.0
                                LoweredInstructions.LiteralNumber 3.0
                                LoweredInstructions.CallWord("add", None)
                            ]
                    )
                with
                | Ok value -> expectEqual 5.0 value "the emitted dispatch agrees with the interpreter"
                | Error reason -> failtestf "%s" reason)

            testCase "fail" (fun () ->
                match invokeEmitted (seedProgram [ LoweredInstructions.Fail "boom" ]) with
                | Error "boom" -> ()
                | Error reason -> failtestf "unexpected failure: %s" reason
                | Ok value -> failtestf "a failed program must not produce %f" value)

            testCase "unsupported opcode" (fun () ->
                match IlWriter.emit (seedProgram [ LoweredInstructions.MakeClosure("unit", [], 0) ]) with
                | Error(IlWriter.UnsupportedOpcode "make-closure") -> ()
                | Error refusal -> failtestf "unexpected refusal: %s" (IlWriter.describeRefusal refusal)
                | Ok _ -> failtest "a first-class closure must be outside the emitted subset")

            testCase "lambda parameter" (fun () ->
                match
                    lowerProgram (ExpressionLogic.Call(ExpressionLogic.Lambda([ "a" ], word "a"), [ number 5.0 ]))
                with
                | Error reason -> failtestf "%s" reason
                | Ok program ->
                    match invokeEmitted program with
                    | Ok value -> expectEqual 5.0 value "the emitted lambda binds its parameter"
                    | Error reason -> failtestf "%s" reason)

            testCase "lambda capture" (fun () ->
                match
                    lowerProgram (
                        ExpressionLogic.Let(
                            "x",
                            number 7.0,
                            ExpressionLogic.Call(ExpressionLogic.Lambda([], word "x"), [])
                        )
                    )
                with
                | Error reason -> failtestf "%s" reason
                | Ok program ->
                    match invokeEmitted program with
                    | Ok value -> expectEqual 7.0 value "the emitted lambda reads its captured value"
                    | Error reason -> failtestf "%s" reason)

            testCase "nested unit" (fun () ->
                match
                    lowerProgram (
                        ExpressionLogic.Call(
                            ExpressionLogic.Lambda(
                                [],
                                ExpressionLogic.Call(ExpressionLogic.Lambda([], number 5.0), [])
                            ),
                            []
                        )
                    )
                with
                | Error reason -> failtestf "%s" reason
                | Ok program ->
                    match invokeEmitted program with
                    | Ok value -> expectEqual 5.0 value "an emitted unit calls another emitted unit"
                    | Error reason -> failtestf "%s" reason)
        ]
#endif

/// The granular suite. `aggregateSuite` is deliberately absent: the example
/// suite already runs those checks, and including it here would execute them
/// twice. The NativeAOT binary must not call this (D23).
let suite: Test =
    testList
        "Scale.Granular"
        [
            emitterSnapshotTests
            pipelineSnapshotTests
            pipelineIntegrationTests
            actorIntegrationTests
#if ENABLE_WRITER
            writerTests
#endif
        ]

/// Pinned so a skipped or added granular case cannot silently change the
/// release-gate total. Update this and the documented counts together.
let expectedGranularChecks = 36

/// The number of granular cases, without running them.
let granularTestCount: int = countTests suite

/// Runs the granular suite. A count drift is a failure before any case runs,
/// so the gate cannot report green on a suite that silently lost checks.
let runAll () : int =
    if granularTestCount <> expectedGranularChecks then
        printfn $"  FAIL: expected {expectedGranularChecks} granular checks, found {granularTestCount}"

        1
    else
        let code, text = runCaptured suite

        if code <> 0 then
            printFailureDetail text

        printfn $"Granular tests: {granularTestCount}"
        code
