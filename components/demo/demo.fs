module Demo

// The demonstration pipeline: a specification grows when it is falsified.
//
//   1. Read the .scale specification (the single editable source of truth).
//   2. Emit the F# property and the SMT obligation.
//   3. Run the *compiled* property through Hedgehog and capture the
//      counterexample (D9: the loop executes the emitter's output).
//   4. Refine: search for the weakest guard that closes the falsified class.
//   5. Append the refinement, re-emit, and confirm the class is closed by
//      running the refined compiled property and discharging the refined
//      obligation with Z3 (D10).

open System
open System.IO
open Types
open Hedgehog.FSharp

/// Writes only when the bytes differ, so a re-emission does not touch the
/// file's mtime. The loop re-emits byte-identically (W12); writing the same
/// bytes anyway would trip mtime-based watchers into a rebuild/test feedback
/// loop while leaving the tree content-clean but never quiet.
let private writeIfChanged (path: string) (contents: string) : unit =
    if not (File.Exists path) || File.ReadAllText path <> contents then
        File.WriteAllText(path, contents)

/// What the demonstration loop did, collected for display and for tests.
type LoopReport =
    {
        Mode: FixedSpecificationAcceptance.Mode
        Specification: string
        ScalePath: string
        InitialFSharpPath: string
        FSharpPath: string
        InitialSmtPath: string
        SmtPath: string
        InitialFalsified: bool
        /// Why the run could not be decided, when it could not. An
        /// inconclusive run is reported as such: it is never a pass.
        Inconclusive: string option
        Diagnostic: Refiner.Diagnostic option
        Refinement: ScaleFile.RefinementGuard option
        RefinementProvenance: Refiner.Provenance option
        Coverage: int option
        Baseline: int
        InitialLattice: TypeLattice.Lattice
        RefinedLattice: TypeLattice.Lattice
        RefinedHolds: bool
        InitialOutcome: Smt.Outcome
        RefinedOutcome: Smt.Outcome
        EmittedInitialFSharp: string
        EmittedFSharp: string
        EmittedInitialSmt: string
        EmittedSmt: string
    }

/// The pristine specification. The demonstration resets the .scale file to
/// this before each run so the showcase is repeatable; in real use the service
/// only ever appends.
let pristineSpecification = "Every player that is alive can jump."

/// The host-bound domain binding (D9 sub-decision 1): the subject type and the
/// verb come from `DemoDomain`, so the emitted property is non-vacuous — it
/// runs the implementation under test. The module name is a parameter: the two
/// document states need two flat, distinct names. The SMT side carries the
/// same implementation body (D10), so the proof is about this code.
let private hostBinding (moduleName: string) (lattice: TypeLattice.Lattice) : Emitter.DomainBinding =
    {
        ModuleName = moduleName
        Vocabulary = []
        SubjectType = Emitter.Fixed "Player"
        VerbName = Emitter.Fixed "canJump"
        FieldName =
            Emitter.Explicit(
                Map.ofList [ "isAlive", "alive"; "alive", "alive" ],
                Emitter.Identity
            )
        FieldAccess =
            Emitter.Explicit(
                Map.ofList
                    [
                        "isAlive", "player.Alive"
                        "alive", "player.Alive"
                        "health", "player.Health"
                    ],
                Emitter.Prefix "player."
            )
        GeneratedRecord = false
        DomainDefault = false
        SourceOfTruth = Some "demo/player.scale"
        Opens = [ "open DemoDomain" ]
        Smt =
            Some
                {
                    Lattice = lattice
                    VerbBody = DemoDomain.canJumpExpression
                }
    }

let private tryFindRepositoryRootFrom (startDirectory: string) : string option =
    let rec walk (directory: string) : string option =
        if File.Exists(Path.Combine(directory, "workspace.edn")) then
            Some directory
        else
            match Directory.GetParent directory with
            | null -> None
            | parent -> walk parent.FullName

    walk startDirectory

let private tryFindRepositoryRoot () : string option =
    tryFindRepositoryRootFrom AppContext.BaseDirectory

/// Resolves the demo content directory. A checkout keeps its content under
/// `components/demo/demo`; a publish payload has the copied `demo` directory
/// beside the binary (D141).
let demoDirectoryFrom (repositoryRoot: string option) (baseDirectory: string) : string =
    match repositoryRoot with
    | Some root -> Path.Combine(root, "components", "demo", "demo")
    | None -> Path.Combine(baseDirectory, "demo")

/// The demo content directory: the source tree in a checkout, the copied
/// `demo` directory beside the binary in a publish payload.
let demoDirectory () : string =
    demoDirectoryFrom (tryFindRepositoryRoot ()) AppContext.BaseDirectory

/// The demonstration specification's path, resolved from the content root.
let specificationPath () : string =
    Path.Combine(demoDirectory (), "player.scale")

let private repositoryRoot () : string =
    tryFindRepositoryRoot () |> Option.defaultValue AppContext.BaseDirectory

/// The default repair-bundle path, resolved from the repository root.
let bundlePath () : string =
    Path.Combine(repositoryRoot (), "artifacts", "repair-bundle.json")

let private deriveVocabulary () : VocabularyEntry list =
    let root, store =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    match VocabularyInImage.readVocabulary (Some root) store with
    | Ok vocabulary -> vocabulary
    | Error message -> invalidOp ("The seed vocabulary could not be read back from the store: " + message)

/// Maps a specification condition token onto the domain field that names it,
/// so the refiner's exhaustive search sees the same precondition the sentence
/// states. This is a *refiner input* — like the generators — not a runtime
/// check: the runtime check is the compiled predicate (D9).
let private domainFieldFor (vocabulary: VocabularyEntry list) (conditionToken: string) : string =
    let wanted = Emitter.fsharpNameOf vocabulary conditionToken

    match
        DemoDomain.fields
        |> List.tryFind (fun field -> Emitter.fsharpNameOf vocabulary field = wanted)
    with
    | Some field -> field
    | None -> conditionToken

/// The specification's precondition: the sentence's conditions, mapped onto
/// domain fields. A numeric condition compares the player's (integer-valued)
/// health against the threshold; the demonstration domain's health is a
/// `RealNumber`, and the threshold is an integer, so the comparison is exact.
let private preconditionOf (vocabulary: VocabularyEntry list) (parsed: ParsedSentence) : DemoDomain.Player -> bool =
    let domainField = domainFieldFor vocabulary

    fun player ->
        parsed.Conditions
        |> List.forall (fun condition ->
            let field = domainField (conditionField condition)

            match condition, DemoDomain.fieldValue player field with
            | BooleanCondition _, ScaleFile.BooleanValue value -> value
            | NumericCondition(_, comparison, threshold), ScaleFile.NumericValue value ->
                match comparison with
                | ComparisonGreaterThan -> value > threshold
                | ComparisonAtLeast -> value >= threshold
                | ComparisonLessThan -> value < threshold
                | ComparisonAtMost -> value <= threshold
                | ComparisonEqualTo -> value = threshold
            | _ -> false)

/// The thin harness (D9 sub-decision 3): the generators are derived from the
/// same lattice the verifier and refiner use; the property body calls the
/// *compiled* `: bool` predicate. This is exactly the D5 contract — a
/// framework-free predicate adopted by a harness.
let private harness (lattice: TypeLattice.Lattice) (predicate: DemoDomain.Player -> bool) : Hedgehog.Property<bool> =
    let aliveGenerator =
        TypeLattice.booleanGeneratorFor "alive" lattice
        |> Result.defaultWith (fun reason ->
            failwith ("The demonstration lattice has no usable 'alive' generator: " + reason))

    let healthGenerator =
        TypeLattice.numericGeneratorFor "health" lattice
        |> Result.defaultWith (fun reason ->
            failwith ("The demonstration lattice has no usable 'health' generator: " + reason))

    property {
        let! alive = aliveGenerator
        let! health = healthGenerator

        let health =
            DomainExpression.integerValue health
            |> Result.defaultWith (fun reason ->
                failwith ("The demonstration lattice's 'health' generator is not an integer: " + reason))

        let player: DemoDomain.Player = { Alive = alive; Health = health }
        return predicate player
    }

/// Runs the full loop once and returns a structured report.
let runLoop () : LoopReport =
    let demoRoot = demoDirectory ()
    let generatedDirectory = Path.Combine(demoRoot, "generated")
    Directory.CreateDirectory generatedDirectory |> ignore
    let scalePath = Path.Combine(demoRoot, "player.scale")

    let initialFSharpPath =
        Path.Combine(generatedDirectory, "PlayerSpecificationPristine.fs")

    let fsharpPath = Path.Combine(generatedDirectory, "PlayerSpecificationRefined.fs")

    let initialSmtPath =
        Path.Combine(generatedDirectory, "PlayerSpecificationPristine.smt2")

    let smtPath = Path.Combine(generatedDirectory, "PlayerSpecificationRefined.smt2")

    // 1. Reset the .scale file to the pristine specification.
    ScaleFile.write
        scalePath
        {
            Specification = pristineSpecification
            Refinements = []
        }

    // 2. Derive the vocabulary from the store.
    let vocabulary = deriveVocabulary ()

    // 3. Tokenize and parse the specification sentence.
    let parsed =
        let tokens = Tokenizer.tokenize vocabulary pristineSpecification

        match Parser.parse vocabulary tokens with
        | Some sentence -> sentence
        | None -> invalidOp ("The specification sentence did not parse: " + pristineSpecification)

    // 4. Read the document, emit the artifacts, and run the compiled property.
    let document =
        match ScaleFile.read scalePath with
        | Ok loaded -> loaded
        | Error message -> invalidOp ("The .scale file could not be read: " + message)

    // The document is the editable specification; the lattice is its current
    // region. Folding the document's guards here keeps the harness, the
    // baseline, and the refiner on the same inputs when the loop is re-entered
    // with a non-pristine document.
    let currentLattice =
        match
            TypeLattice.withGuards
                (document.Refinements |> List.map (fun refinement -> refinement.Guard))
                DemoDomain.lattice
        with
        | Ok lattice -> lattice
        | Error reason -> invalidOp ("The document's refinements do not narrow the demonstration lattice: " + reason)

    let initialBinding =
        hostBinding "Scale.Generated.PlayerSpecificationPristine" currentLattice

    let initialFSharp =
        Emitter.emitFSharp initialBinding parsed document.Specification document.Refinements

    let initialSmt =
        Emitter.emitSmt initialBinding parsed document.Specification document.Refinements

    writeIfChanged initialFSharpPath initialFSharp
    writeIfChanged initialSmtPath initialSmt

    let precondition = preconditionOf vocabulary parsed

    let baseline =
        match TypeLattice.universe currentLattice DemoDomain.makePlayer with
        | Ok universe -> universe |> List.filter precondition |> List.length
        | Error reason -> invalidOp ("The current lattice cannot be enumerated: " + reason)

    // One encoding serves both document states: the implementation body does
    // not change when the specification grows.
    use encoding =
        match Smt.create DemoDomain.smtFields DemoDomain.canJumpExpression with
        | Ok encoding -> encoding
        | Error reason -> invalidOp ("The demonstration domain's body is ill-sorted: " + reason)

    let obligationOf (refinements: ScaleFile.Refinement list) : Smt.Obligation =
        {
            Pattern = parsed.Pattern
            Conditions = parsed.Conditions |> List.map (mapConditionField (Emitter.fieldNameOf initialBinding))
            Refinements = refinements
        }

    let initialOutcome = Smt.discharge encoding (obligationOf document.Refinements)

    let smtWitness =
        match initialOutcome with
        | Smt.Falsified model -> Some model
        | _ -> None

    // The falsification runs the *initial* compiled predicate over the initial
    // lattice's coordinates.
    match Refiner.falsify (harness currentLattice Scale.Generated.PlayerSpecificationPristine.``player canJump``) with
    | Refiner.Held ->
        writeIfChanged fsharpPath initialFSharp
        writeIfChanged smtPath initialSmt

        {
            Mode = FixedSpecificationAcceptance.Exploratory
            Specification = pristineSpecification
            ScalePath = scalePath
            InitialFSharpPath = initialFSharpPath
            FSharpPath = fsharpPath
            InitialSmtPath = initialSmtPath
            SmtPath = smtPath
            InitialFalsified = false
            Inconclusive = None
            Diagnostic = None
            Refinement = None
            RefinementProvenance = None
            Coverage = None
            Baseline = baseline
            InitialLattice = currentLattice
            RefinedLattice = currentLattice
            RefinedHolds = true
            InitialOutcome = initialOutcome
            RefinedOutcome = initialOutcome
            EmittedInitialFSharp = initialFSharp
            EmittedFSharp = initialFSharp
            EmittedInitialSmt = initialSmt
            EmittedSmt = initialSmt
        }
    | Refiner.Inconclusive reason ->
        // The property was neither held nor falsified: the generator could
        // not produce enough admissible samples. The loop reports that, and
        // never counts it as a pass.
        writeIfChanged fsharpPath initialFSharp
        writeIfChanged smtPath initialSmt

        {
            Mode = FixedSpecificationAcceptance.Exploratory
            Specification = pristineSpecification
            ScalePath = scalePath
            InitialFSharpPath = initialFSharpPath
            FSharpPath = fsharpPath
            InitialSmtPath = initialSmtPath
            SmtPath = smtPath
            InitialFalsified = false
            Inconclusive = Some reason
            Diagnostic = None
            Refinement = None
            RefinementProvenance = None
            Coverage = None
            Baseline = baseline
            InitialLattice = currentLattice
            RefinedLattice = currentLattice
            RefinedHolds = false
            InitialOutcome = initialOutcome
            RefinedOutcome = initialOutcome
            EmittedInitialFSharp = initialFSharp
            EmittedFSharp = initialFSharp
            EmittedInitialSmt = initialSmt
            EmittedSmt = initialSmt
        }
    | Refiner.Falsified diagnostic ->
        // 5. Refine: provenClosure is the proved SMT query `pre ∧ guard ∧ ¬post`,
        // not a scan of the universe. The universe only proposes thresholds
        // and ranks the weakest guard by coverage.
        let provenClosure: Refiner.ProvenClosure =
            {
                Proves =
                    fun guard ->
                        let obligation =
                            obligationOf (
                                document.Refinements
                                @ [
                                    {
                                        Guard = guard
                                        Origin = "refiner"
                                        Counterexample = ""
                                    }
                                ]
                            )

                        match Smt.discharge encoding obligation with
                        | Smt.Proved ->
                            match Smt.satisfiablePre encoding obligation with
                            | Ok true -> true
                            | _ -> false
                        | _ -> false
                Solver = Smt.solverVersion
                Witness = smtWitness
            }

        let refinement =
            match
                Refiner.refine
                    DemoDomain.fieldValue
                    DemoDomain.makePlayer
                    DemoDomain.lattice
                    (document.Refinements |> List.map (fun refinement -> refinement.Guard))
                    precondition
                    provenClosure
            with
            | Ok refinement -> refinement
            | Error reason -> invalidOp ("The refiner could not narrow the demonstration lattice: " + reason)

        let refinedLattice =
            refinement
            |> Option.map (fun result -> result.Lattice)
            |> Option.defaultValue currentLattice

        let refinedBinding =
            hostBinding "Scale.Generated.PlayerSpecificationRefined" refinedLattice

        // 6. Append the refinement and re-emit. The persisted provenance is
        // the solver version and the model that Z3 returned for the
        // unrefined obligation.
        let refinedDocument =
            match refinement with
            | Some result ->
                let candidate = result.Candidate

                let refinementRecord: ScaleFile.Refinement =
                    {
                        Guard = candidate.Guard
                        Origin = "z3 " + candidate.Provenance.Solver
                        Counterexample =
                            match candidate.Provenance.Witness with
                            | Some model -> Smt.describeModel model
                            | None -> String.concat ", " diagnostic.Values
                    }

                let capability =
                    match
                        FixedSpecificationAcceptance.specChangeCapability
                            FixedSpecificationAcceptance.Exploratory
                            document
                    with
                    | Ok value -> value
                    | Error reason -> invalidOp reason

                let proposal =
                    FixedSpecificationAcceptance.proposeSpecification capability refinementRecord

                match FixedSpecificationAcceptance.appendSpecification proposal document with
                | Ok appended -> appended
                | Error reason -> invalidOp reason
            | None -> document

        ScaleFile.write scalePath refinedDocument

        let finalFSharp =
            Emitter.emitFSharp refinedBinding parsed refinedDocument.Specification refinedDocument.Refinements

        let finalSmt =
            Emitter.emitSmt refinedBinding parsed refinedDocument.Specification refinedDocument.Refinements

        writeIfChanged fsharpPath finalFSharp
        writeIfChanged smtPath finalSmt

        // 7. Re-run the *refined* compiled predicate over the narrowed lattice:
        // it should now hold. The SMT obligation says whether it holds for
        // every input in the theory.
        let refinedHolds =
            match
                (Property.reportBool (
                    harness refinedLattice Scale.Generated.PlayerSpecificationRefined.``player canJump``
                ))
                    .Status
            with
            | Hedgehog.Status.OK -> true
            | _ -> false

        use refinedEncoding =
            match Smt.createFromLattice refinedLattice DemoDomain.canJumpExpression with
            | Ok encoding -> encoding
            | Error reason -> invalidOp ("The refined lattice's body is ill-sorted: " + reason)

        let refinedOutcome =
            Smt.discharge refinedEncoding (obligationOf refinedDocument.Refinements)

        {
            Mode = FixedSpecificationAcceptance.Exploratory
            Specification = pristineSpecification
            ScalePath = scalePath
            InitialFSharpPath = initialFSharpPath
            FSharpPath = fsharpPath
            InitialSmtPath = initialSmtPath
            SmtPath = smtPath
            InitialFalsified = true
            Inconclusive = None
            Diagnostic = Some diagnostic
            Refinement = refinement |> Option.map (fun result -> result.Candidate.Guard)
            RefinementProvenance = refinement |> Option.map (fun result -> result.Candidate.Provenance)
            Coverage = refinement |> Option.map (fun result -> result.Candidate.Coverage)
            Baseline = baseline
            InitialLattice = currentLattice
            RefinedLattice = refinedLattice
            RefinedHolds = refinedHolds
            InitialOutcome = initialOutcome
            RefinedOutcome = refinedOutcome
            EmittedInitialFSharp = initialFSharp
            EmittedFSharp = finalFSharp
            EmittedInitialSmt = initialSmt
            EmittedSmt = finalSmt
        }

/// The fixed-spec run's outcome. `Holds` is the only success: a witness is a
/// defect report against a specification the developer holds fixed, and an
/// inconclusive run is never a pass.
type FixedSpecificationOutcome =
    | Holds
    | Witnessed of bundlePath: string
    | Inconclusive of reason: string

/// Runs the demonstration target in fixed-spec mode. The specification is held
/// fixed: this neither edits `demo/player.scale` nor re-emits the generated
/// artifacts, and it never appends a refinement. The compiled pristine
/// predicate is the property under test; a witness becomes a repair bundle at
/// `bundlePath`, and the outcome names what happened.
///
/// This is the CLI's reporting path, not an acceptance gate: accepting a patch
/// against the bundle stays `FixedSpecificationAcceptance.accept`, which is exercised in-process.
let runFixedSpecification (bundlePath: string) : Result<FixedSpecificationOutcome, string> =
    let vocabulary = deriveVocabulary ()

    let document: ScaleFile.ScaleDocument =
        {
            Specification = pristineSpecification
            Refinements = []
        }

    match Parser.parse vocabulary (Tokenizer.tokenize vocabulary pristineSpecification) with
    | None -> Error("The fixed specification did not parse: " + pristineSpecification)
    | Some parsed ->
        match TypeLattice.withGuards [] DemoDomain.lattice with
        | Error reason -> Error("The demonstration lattice is not usable: " + reason)
        | Ok lattice ->
            let binding = hostBinding "Scale.Generated.PlayerSpecificationPristine" lattice

            let obligation: Smt.Obligation =
                {
                    Pattern = parsed.Pattern
                    Conditions = parsed.Conditions |> List.map (mapConditionField (Emitter.fieldNameOf binding))
                    Refinements = []
                }

            match Smt.create DemoDomain.smtFields DemoDomain.canJumpExpression with
            | Error reason -> Error("The demonstration body is ill-sorted: " + reason)
            | Ok encoding ->
                use encoding = encoding

                match
                    Refiner.falsify (harness lattice Scale.Generated.PlayerSpecificationPristine.``player canJump``)
                with
                | Refiner.Held -> Ok Holds
                | Refiner.Inconclusive reason -> Ok(Inconclusive reason)
                | Refiner.Falsified diagnostic ->
                    match Smt.discharge encoding obligation with
                    | Smt.Falsified model ->
                        let values = Smt.fieldValues model

                        let witness: FixedSpecificationAcceptance.Witness<DemoDomain.Player> =
                            {
                                Input = DemoDomain.makePlayer (Map.ofList values)
                                Values = values
                            }

                        let differential =
                            FixedSpecificationAcceptance.differentialResult
                                DemoDomain.smtFields
                                DemoDomain.fieldValue
                                DemoDomain.canJumpExpression
                                [ witness ]

                        let bundle =
                            FixedSpecificationAcceptance.makeBundle
                                1
                                "player canJump"
                                parsed
                                document
                                witness
                                diagnostic
                                (Some model)
                                DemoDomain.canJumpExpression
                                (DomainExpression.render DemoDomain.canJumpExpression)
                                differential
                                { File = "demo/player.scale"; Line = 7 }

                        let directory =
                            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath bundlePath)

                        if not (System.IO.Directory.Exists directory) then
                            System.IO.Directory.CreateDirectory directory |> ignore

                        FixedSpecificationAcceptance.writeBundle bundlePath bundle
                        Ok(Witnessed bundlePath)
                    | Smt.Proved ->
                        Error
                            "The solver proved the fixed target while Hedgehog falsified it; the two backends disagree."
                    | Smt.Witnessed _ ->
                        Error
                            "The solver witnessed a universal target while the fixed-spec run expected a proof; the two backends disagree."
                    | Smt.NoWitness ->
                        Error "The solver found no witness for a universal target; the two backends disagree."
                    | Smt.Unknown reason -> Ok(Inconclusive("the solver could not decide the fixed target: " + reason))
                    | Smt.Unavailable reason -> Error("The fixed target cannot be encoded: " + reason)

/// Prints the fixed-spec outcome. `Holds` is the only success; a witness names
/// the bundle a generator consumes, and an inconclusive run says so.
let reportFixedSpecification (outcome: FixedSpecificationOutcome) : unit =
    printfn ""
    printfn "=== Scale fixed-spec gate ==="

    match outcome with
    | Holds -> printfn "the fixed specification holds; there is nothing to repair."
    | Witnessed bundlePath ->
        printfn "the fixed specification is falsified; the specification was not edited."
        printfn "repair bundle: %s" bundlePath
    | Inconclusive reason -> printfn "the run was inconclusive (%s); it is not a pass." reason

/// Prints the loop's story.
let report (loopReport: LoopReport) : unit =
    printfn ""
    printfn "=== Scale -> Hedgehog -> Z3 (the refinement loop) ==="

    printfn
        "mode: %s"
        (match loopReport.Mode with
         | FixedSpecificationAcceptance.Exploratory -> "exploratory"
         | FixedSpecificationAcceptance.FixedSpecification -> "fixed-spec")

    printfn "specification: %s" loopReport.Specification
    printfn "emitted: %s" loopReport.InitialFSharpPath
    printfn "         %s" loopReport.FSharpPath
    printfn "         %s" loopReport.InitialSmtPath
    printfn "         %s" loopReport.SmtPath

    if not loopReport.InitialFalsified then
        match loopReport.Inconclusive with
        | Some reason -> printfn "Hedgehog: the run was inconclusive (%s); it is not a pass." reason
        | None -> printfn "Hedgehog: the specification held; there is nothing to refine."
    else
        match loopReport.Diagnostic with
        | Some diagnostic ->
            printfn $"Hedgehog: falsified after {diagnostic.Tests} tests and {diagnostic.Shrinks} shrinks"
            printfn "          smallest counterexample: %s" (String.concat ", " diagnostic.Values)
            printfn "          recheck seed: %s" diagnostic.Seed
        | None -> ()

        match loopReport.Refinement with
        | Some guard ->
            printfn "refiner: the weakest guard that closes the class is: %s" (ScaleFile.refinementGuardText guard)
        | None -> printfn "refiner: no single-field guard closed the class."

        match loopReport.Coverage with
        | Some coverage ->
            printfn $"coverage: {coverage} of the specification's {loopReport.Baseline} inputs remain in scope"
        | None -> ()

        match loopReport.RefinementProvenance with
        | Some provenance ->
            printfn "provenance: Z3 %s" provenance.Solver

            match provenance.Witness with
            | Some model -> printfn "            witness model: %s" (Smt.describeModel model)
            | None -> ()
        | None -> ()

        if loopReport.RefinedHolds then
            printfn "Hedgehog: the refined property holds; that bug class is closed."
        else
            printfn "Hedgehog: the refined property still fails."

    printfn "Z3: initial obligation: %s" (Smt.describeOutcome loopReport.InitialOutcome)
    printfn "Z3: refined obligation: %s" (Smt.describeOutcome loopReport.RefinedOutcome)
    printfn ""

// ============================================================
// Potential extensions (not commitments)
// - Query and actor showcase scenarios: demonstrate the other two surfaces.
// - Failure and restart demonstrations: teach recovery without exceptions.
// - Interactive tutorial mode: guided onboarding for new users.
// - Benchmark showcase: compare interpreter and lowered execution publicly.
// ============================================================
