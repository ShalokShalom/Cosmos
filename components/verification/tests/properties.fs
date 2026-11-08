module Properties

// ============================================================
// Property-based verification of Scale's semantical architecture.
//
// Every property below is an executable statement of one row in
// docs/commitments-ledger.typ — the three axioms of the README
// (one thing, one structure, one protocol), the level model,
// the derived semantics (SetAlgebra's `nor` family), and the edge
// (tokenizer/emitter). A property can *falsify* a commitment;
// an example test can only confirm it. When a property fails,
// its recheck seed is printed — re-running with that seed
// reproduces the exact counterexample (decision D5/D6).
//
// Framework: Hedgehog 1.0.0.
// ============================================================

open Types
open ExpressionLogic
open Hedgehog.FSharp

let mutable private passed = 0
let mutable private failed = 0

let private report (name: string) (ok: bool) : unit =
    if ok then
        passed <- passed + 1
        printfn "  PASS: %s" name
    else
        failed <- failed + 1
        printfn "  FAIL: %s" name

/// Runs a property and reports PASS/FAIL. On failure, re-runs the property
/// through `checkBool`, which raises with the shrunk counterexample *and a
/// recheck seed string* — reproduce the failure by passing that string to
/// `Property.recheck` (see docs/decision-records.typ, D6).
let private holds (name: string) (property: Hedgehog.Property<bool>) : unit =
    let result = Property.reportBool property

    match result.Status with
    | Hedgehog.Status.OK -> report name true
    | Hedgehog.Status.GaveUp ->
        // `GaveUp` carries no counterexample and no seed worth rechecking:
        // the generator discarded too many samples. Say so, so a too-narrow
        // generator is diagnosable instead of surfacing as a bare FAIL.
        printfn $"    gave up after {result.Tests} tests ({result.Discards} discarded): the generator"
        printfn "    could not produce enough admissible samples; there is no"
        printfn "    counterexample to shrink and no seed worth rechecking."
        report name false
    | Hedgehog.Status.Failed _ ->
        // Surface the falsifying input and the reproduction seed.
        (try
            Property.checkBool property |> ignore
         with exn ->
             printfn "%s" exn.Message)

        report name false

// ============================================================
// Generators
// ============================================================

let private word: Hedgehog.Gen<string> =
    Gen.string (Range.linear 1 6) (Gen.item [ 'a' .. 'e' ])

let private value: Hedgehog.Gen<RealNumber> =
    Gen.double (Range.linearFrom 0.0 -100.0 100.0)

let private tensor: Hedgehog.Gen<Tensor> =
    Gen.frequency
        [
            (1, Gen.map Tensor.scalar value)
            (2, Gen.map Tensor.vector (Gen.list (Range.linear 1 5) value))
        ]

/// The words a generated program may use. The two console-emitting words,
/// `print` and `showBookmark`, are excluded: their defining effect is stdout,
/// which drowns the test log without adding falsifiable reach — `jump` and
/// `copyBookmark` already exercise bookmark state, and `showBookmark` only
/// prints coordinates and discards its argument.
///
/// The list carries the whole surface, not only the arithmetic core: the
/// scheduler words, the channel and event words, the sort words, unification,
/// dispatch, and the word metaobjects. A totality property that never
/// generates a word cannot say anything about that word, and the surface grew
/// well past the arithmetic core (D14, D15, D19, D20, D21, D26, D29).
///
/// `rollback` is deliberately absent: a program that rolls back and re-runs the
/// same suffix is a loop by construction, and a totality property would hang on
/// it. Its refusal path and its mechanism are pinned by example tests instead.
let private coreWords =
    [
        "add"
        "subtract"
        "multiply"
        "divide"
        "lessThan"
        "greaterThan"
        "equals"
        "duplicate"
        "swap"
        "drop"
        "over"
        "rotate"
        "if"
        "apply"
        "define"
        "bookmark"
        "jump"
        "copyBookmark"
        "history"
        "own"
        "borrow"
        "consume"
        "release"
        "reclaimable"
        "compact"
        "linearity"
        "bind"
        "lookup"
        "unify"
        "resolve"
        "term"
        "typed"
        "set"
        "add-member"
        "and"
        "or"
        "nor"
        "is"
        "spawn"
        "yield"
        "halt"
        "actors"
        "resume"
        "kill"
        "channel"
        "send"
        "receive"
        "receive-matching"
        "close"
        "send-event"
        "receive-event"
        "choose"
        "wrap"
        "guard"
        "with-fallback"
        "sync"
        "seal"
        "predicate-seal"
        "runtime-guard-define"
        "runtime-guard-holds"
        "unname"
        "subset"
        "filter"
        "union"
        "intersect"
        "except"
        "member"
        "same-shape"
        "same-name"
        "sort-of"
        "merge"
        "complement"
        "generic"
        "method"
        "word"
    ]

let private numberToken: Hedgehog.Gen<string> =
    Gen.frequency
        [
            (3, Gen.map (fun (i: int) -> string i) (Gen.int32 (Range.linear -100 100)))
            (1, Gen.map (fun (d: double) -> string d) value)
        ]

/// A token a program may contain: core words, numbers, vocabulary words
/// (active and inactive), quotations, text literals, variables, garbage.
let private programToken (vocabulary: VocabularyEntry list) : Hedgehog.Gen<string> =
    let vocabTokens = vocabulary |> List.map (fun e -> e.Token)

    Gen.frequency
        [
            (4, Gen.item coreWords)
            (3, numberToken)
            (2, Gen.item vocabTokens)
            (1, Gen.item [ "{"; "}" ])
            (1, Gen.map (fun w -> "\"" + w + "\"") word)
            (1, Gen.map (fun w -> "?" + w) word)
            (1, word)
        ]

let private program (vocabulary: VocabularyEntry list) : Hedgehog.Gen<string list> =
    Gen.list (Range.linear 0 12) (programToken vocabulary)

// ============================================================
// Prelude image generators
// ============================================================

let private callGuard: Hedgehog.Gen<LoweredInstructions.CallGuard option> =
    Gen.frequency
        [
            (2, Gen.item [ None ])
            (1,
             Gen.map
                 (fun target ->
                     Some
                         {
                             LoweredInstructions.Target = target
                             LoweredInstructions.Epoch = 1
                         })
                 word)
        ]

let private leafInstruction: Hedgehog.Gen<LoweredInstructions.Instruction> =
    Gen.frequency
        [
            (2, Gen.map LoweredInstructions.LiteralNumber value)
            (2, Gen.map LoweredInstructions.LiteralText word)
            (1, Gen.map (fun slot -> LoweredInstructions.PushSlot slot) (Gen.int32 (Range.linear 0 4)))
            (1, Gen.map (fun slot -> LoweredInstructions.StoreSlot slot) (Gen.int32 (Range.linear 0 4)))
            (1, Gen.map2 (fun name guard -> LoweredInstructions.CallWord(name, guard)) word callGuard)
            (1, Gen.item [ LoweredInstructions.Apply; LoweredInstructions.Fail "generated failure" ])
        ]

/// A code body nests one level: deeper nesting exercises nothing the codec does
/// not already do, and it would only make generation terminate by size (D202).
let private imageInstruction: Hedgehog.Gen<LoweredInstructions.Instruction> =
    Gen.frequency
        [
            (6, leafInstruction)
            (1, Gen.map LoweredInstructions.MakeCode (Gen.list (Range.linear 0 3) leafInstruction))
        ]

let private imageWith
    (unitName: string)
    (instructions: LoweredInstructions.Instruction list)
    : PreludeImage.ImageDocument =
    let export: PreludeImage.ImageExport = { Name = "main"; Unit = unitName }

    let unit: PreludeImage.ImageUnit =
        {
            Name = unitName
            SourceHash = "generated-unit"
            Dependencies = []
            Arity = 0
            Captures = []
            Instructions = instructions
        }

    {
        SchemaVersion = 2
        Form = "image"
        Name = "generated"
        Version = "1"
        SourceHash = "generated-source"
        DependencyPins = Map.empty
        Imports = []
        Exports = [ export ]
        Capabilities = []
        Kinds = []
        Predicates = []
        Primitives = []
        Units = [ unit ]
        Groups = []
    }

let private imageInstructionsRoundTrip: Hedgehog.Property<bool> =
    property {
        let! body = Gen.list (Range.linear 0 8) imageInstruction

        let instructions =
            body
            @ [
                LoweredInstructions.Label 0
                LoweredInstructions.Jump 0
                LoweredInstructions.JumpIfFalse 0
            ]

        let image = imageWith "main" instructions

        match PreludeImage.ofText (PreludeImage.toJson image) with
        | Ok roundTripped -> return roundTripped.Units.Head.Instructions = instructions
        | Error _ -> return false
    }

let private imageArbitraryBytesAreTotal: Hedgehog.Property<bool> =
    property {
        let! values = Gen.list (Range.linear 0 200) (Gen.int32 (Range.linear 0 255))
        let bytes = values |> List.map byte |> Array.ofList

        match PreludeImage.ofBytes bytes with
        | Ok _
        | Error _ -> return true
    }

let private imageUnresolvedJumpIsRefused: Hedgehog.Property<bool> =
    property {
        let! body = Gen.list (Range.linear 0 8) imageInstruction
        let image = imageWith "main" (body @ [ LoweredInstructions.Jump 99 ])

        match PreludeImage.ofText (PreludeImage.toJson image) with
        | Error(PreludeImage.UnresolvedLabel("main", 99)) -> return true
        | _ -> return false
    }

let private imageExportResolutionIsConsistent: Hedgehog.Property<bool> =
    property {
        let! unitName = word
        let image = imageWith unitName []

        let known =
            match PreludeImage.resolveExport image "main" with
            | Ok unit -> unit.Name = unitName
            | Error _ -> false

        let unknown =
            match PreludeImage.resolveExport image "missing" with
            | Error(PreludeImage.UnknownEntry "missing") -> true
            | _ -> false

        return known && unknown
    }

let private imageValidDagRoundTrips: Hedgehog.Property<bool> =
    property {
        let! unitName = word
        let! candidateDependency = word

        let dependencyName =
            if candidateDependency = unitName then
                candidateDependency + "x"
            else
                candidateDependency

        let image =
            let first: PreludeImage.ImageUnit =
                {
                    Name = unitName
                    SourceHash = "generated-unit"
                    Dependencies = [ dependencyName ]
                    Arity = 0
                    Captures = []
                    Instructions = [ LoweredInstructions.LiteralNumber 1.0 ]
                }

            let second: PreludeImage.ImageUnit =
                {
                    Name = dependencyName
                    SourceHash = "generated-dependency"
                    Dependencies = []
                    Arity = 0
                    Captures = []
                    Instructions = [ LoweredInstructions.LiteralNumber 2.0 ]
                }

            { imageWith unitName [ LoweredInstructions.LiteralNumber 1.0 ] with
                Units = [ first; second ]
            }

        match PreludeImage.ofText (PreludeImage.toJson image) with
        | Ok roundTripped -> return roundTripped.Units.Length = 2
        | Error _ -> return false
    }

// ============================================================
// Axiom 1 — One thing: Tensor is the only semantic type
// ============================================================

/// Level model: `EntryIdentity` (host encoding of an address) projects into a
/// rank-0 tensor and back, injectively. If two references project to the
/// same tensor, they are the same reference.
let private entryIdProjectionIsInjective: Hedgehog.Property<bool> =
    property {
        let! identities = Gen.list (Range.linear 1 20) (Gen.int32 (Range.linear 0 100000))
        let distinct = identities |> List.distinct

        let roundtrips =
            distinct
            |> List.forall (fun i ->
                let identity = EntryIdentity.ofInt i
                let projected = Tensor.scalar (double (EntryIdentity.value identity))

                Tensor.size projected = 1
                && Tensor.rank projected = 0
                && (EntryIdentity.ofInt (int (Tensor.item projected 0)) = identity))

        let injective =
            [
                for i in distinct do
                    for j in distinct do
                        if i <> j then
                            double i <> double j
            ]
            |> List.forall id

        return roundtrips && injective
    }

// ============================================================
// Axiom 2 — One structure: the Store is append-only
// ============================================================

/// Extending an store never disturbs what is already there: every entry
/// readable before a sequence of additions is readable afterwards with the
/// identical tensor, every new identity lies beyond the old frontier (identities
/// are never reused), and, for a store built only by appends, NextIndex equals
/// the number of entries. D53 compaction is a separate sparse successor, covered
/// by its own properties below.
let private imageIsAppendOnly: Hedgehog.Property<bool> =
    property {
        let! tensors = Gen.list (Range.linear 0 15) tensor
        let! texts = Gen.list (Range.linear 0 10) word

        let store0 =
            List.fold (fun img t -> snd (Store.addTensor t img)) Store.empty tensors

        let before = store0.Lexicon |> Map.toList
        let nextIndex0 = store0.NextIndex

        let store1 = List.fold (fun img w -> snd (Store.addText w img)) store0 texts
        let store2 = List.fold (fun img t -> snd (Store.addTensor t img)) store1 tensors

        let stable =
            before
            |> List.forall (fun (identity, t) -> Map.tryFind identity store2.Lexicon = Some t)

        let fresh =
            store2.Lexicon
            |> Map.toList
            |> List.forall (fun (identity, _) ->
                Map.containsKey identity store0.Lexicon
                || EntryIdentity.value identity >= nextIndex0)

        let indexIsCount = store2.NextIndex = store2.Lexicon.Count
        return stable && fresh && indexIsCount
    }

// ============================================================
// Axiom 3 — One protocol: Railway totality and determinism
// ============================================================

/// Railway (E1): running any generated program yields `Ok state` or
/// `Error Diagnostic` — never an exception. Defects are values. A counter-
/// example here would falsify the ledger's "Met" for E1.
let private railwayIsTotal (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! tokens = program vocabulary
        let root, store = VocabularyInImage.addVocabulary vocabulary Store.empty
        let state = Evaluator.initial store root

        let outcome =
            try
                match
                    Evaluator.run
                        { state with
                            Program = EvaluatorProgram.ofTokensOrFail (tokens)
                        }
                with
                | Ok _
                | Error _ -> true
            with exn ->
                printfn "    (exception: %s — tokens: %s)" exn.Message (String.concat " " tokens)
                false

        return outcome
    }

/// The evaluator is a pure function of its state: the same program run
/// twice from the same initial state produces the same final stack (and
/// lexicon) — no hidden state, no ambient randomness.
let private evaluationIsDeterministic (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! tokens = program vocabulary
        let root, store = VocabularyInImage.addVocabulary vocabulary Store.empty
        let state0 = Evaluator.initial store root

        let runIt () =
            Evaluator.run
                { state0 with
                    Program = EvaluatorProgram.ofTokensOrFail (tokens)
                }
            |> Result.map (fun final -> final.Stack, final.Store.Lexicon)

        match runIt (), runIt () with
        | Ok(stack1, lexicon1), Ok(stack2, lexicon2) -> return stack1 = stack2 && lexicon1 = lexicon2
        | Error diagnostic1, Error diagnostic2 -> return diagnostic1 = diagnostic2
        | _ -> return false
    }

/// A well-formed program is not merely total: it succeeds and computes the
/// expected value. This guards against `railwayIsTotal` accepting an error for
/// every input.
let private wellFormedArithmeticSucceeds: Hedgehog.Property<bool> =
    property {
        let! left = Gen.int32 (Range.linear -100 100)
        let! right = Gen.int32 (Range.linear -100 100)
        let! operation = Gen.item [ "add"; "subtract"; "multiply" ]

        let expected =
            match operation with
            | "add" -> left + right
            | "subtract" -> left - right
            | _ -> left * right

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail ([ string left; string right; operation ])
                }
        with
        | Error _ -> return false
        | Ok final ->
            match final.Stack with
            | top :: _ ->
                match Store.lookupTensor top final.Store with
                | Some tensor -> return Tensor.item tensor 0 = double expected
                | None -> return false
            | [] -> return false
    }

// ============================================================
// SetAlgebra — `nor` and its derived family, as set algebra
// ============================================================

/// Helper: build models U, A, B over a small element universe, run
/// `U A B nor` (or `A B and` / `A B or`), then quotationRun membership of every
/// element with `is`. Returns the membership bits, or None on error.
let private setAlgebraOperation
    (op: string)
    (elements: int list)
    (u: int list)
    (a: int list)
    (b: int list)
    : Result<Map<int, bool>, string> =
    let modelProgram name members =
        [
            yield "\"" + name + "\""
            yield "set"
            for m in members do
                yield string m
                yield "\"" + name + "\""
                yield "add-member"
        ]

    let program =
        List.concat
            [
                modelProgram "U" u
                modelProgram "A" a
                modelProgram "B" b
                [ "\"U\""; "\"A\""; "\"B\""; op ]
            ]

    let store = Store.empty

    let vocabularyRoot, store1 =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary store

    let state = Evaluator.initial store1 vocabularyRoot

    match
        Evaluator.run
            { state with
                Program = EvaluatorProgram.ofTokensOrFail (program)
            }
    with
    | Error diagnostic -> Error(describeError diagnostic.Error)
    | Ok final ->
        // top of stack: the result model's name
        match final.Stack with
        | resultNameId :: _ ->
            let resultName =
                match Store.readTextAt resultNameId final.Store with
                | Some name -> name
                | None -> "?"

            let memberships =
                elements
                |> List.map (fun element ->
                    // NB: the model name must be pushed as a *text literal*
                    // (quoted), or it would be read as a word.
                    let quotationRun = [ string element; "\"" + resultName + "\""; "is" ]

                    match
                        Evaluator.run
                            { final with
                                Program = EvaluatorProgram.ofTokensOrFail (quotationRun)
                                Stack = []
                            }
                    with
                    | Ok probeState ->
                        match probeState.Stack with
                        | top :: _ ->
                            match Store.lookupTensor top probeState.Store with
                            | Some t -> element, (Tensor.item t 0 <> 0.0)
                            | None -> element, false
                        | [] -> element, false
                    | Error _ -> element, false)
                |> Map.ofList

            Ok memberships
        | [] -> Error "no result on stack"

/// `U A B nor` = U \ (A ∪ B), elementwise. This is the ledger's Tier-1
/// commitment: `nor` is set complement over an explicit, declared universe.
let private norIsSetComplement: Hedgehog.Property<bool> =
    property {
        let! elements = Gen.list (Range.linear 1 8) (Gen.int32 (Range.linear 0 99))
        let distinct = List.distinct elements

        let bitsOf (bits: int list) =
            [
                for (e, b) in List.zip distinct bits do
                    if b = 1 then
                        e
            ]

        let subsetGenerator =
            Gen.list (Range.constant (List.length distinct) (List.length distinct)) (Gen.int32 (Range.linear 0 1))

        let! uBits = subsetGenerator
        let! aBits = subsetGenerator
        let! bBits = subsetGenerator
        let u, a, b = bitsOf uBits, bitsOf aBits, bitsOf bBits

        match setAlgebraOperation "nor" distinct u a b with
        | Error message ->
            printfn "    (nor property error: %s)" message
            return false
        | Ok actual ->
            let expected =
                distinct
                |> List.map (fun e -> e, (List.contains e u && not (List.contains e a) && not (List.contains e b)))
                |> Map.ofList

            return actual = expected
    }

/// The derived family agrees with set algebra: `A and B` = A ∩ B and
/// `A or B` = A ∪ B. (They share `nor`'s substrate: models as member sets.)
let private andOrAreInterUnion: Hedgehog.Property<bool> =
    property {
        let! elements = Gen.list (Range.linear 1 8) (Gen.int32 (Range.linear 0 99))
        let distinct = List.distinct elements

        let pick () =
            Gen.map
                (fun (bits: int list) ->
                    [
                        for (e, bit) in List.zip distinct bits do
                            if bit = 1 then
                                e
                    ])
                (Gen.list (Range.constant (List.length distinct) (List.length distinct)) (Gen.int32 (Range.linear 0 1)))

        let! u = pick ()
        let! a = pick ()
        let! b = pick ()
        let universe = if List.isEmpty u then distinct else u

        match setAlgebraOperation "and" distinct universe a b, setAlgebraOperation "or" distinct universe a b with
        | Ok andActual, Ok orActual ->
            let expectedAnd =
                distinct
                |> List.map (fun e -> e, List.contains e a && List.contains e b)
                |> Map.ofList

            let expectedOr =
                distinct
                |> List.map (fun e -> e, List.contains e a || List.contains e b)
                |> Map.ofList

            return andActual = expectedAnd && orActual = expectedOr
        | _ -> return false
    }

/// Runs one structural set operation over two literal sets and probes every
/// element with `member`. The expected map is compared by the property.
let structuralSetOperation
    (operation: string)
    (elements: int list)
    (left: int list)
    (right: int list)
    : Result<Map<int, bool>, string> =
    let literal values =
        "[" :: (values |> List.map string) @ [ "]" ]

    // Each quotationRun leaves its boolean below the set, so the set stays reachable:
    // duplicate the set, test the element, then swap the set back on top.
    let probes =
        elements
        |> List.collect (fun element -> [ "duplicate"; string element; "swap"; "member"; "swap" ])

    let program = literal left @ literal right @ [ operation ] @ probes

    let root, store =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    let state = Evaluator.initial store root

    match
        Evaluator.run
            { state with
                Program = EvaluatorProgram.ofTokensOrFail (program)
            }
    with
    | Error diagnostic -> Error(describeError diagnostic.Error)
    | Ok final ->
        let bits =
            final.Stack
            |> List.skip 1 // the set itself, swapped back on top after each quotationRun
            |> List.truncate (List.length elements)
            |> List.rev
            |> List.map (fun identity ->
                match Store.lookupTensor identity final.Store with
                | Some tensor -> Tensor.item tensor 0 <> 0.0
                | None -> false)

        Ok(List.zip elements bits |> Map.ofList)

/// The structural words are set algebra, not a parallel implementation: union,
/// intersect, and except agree with ∪, ∩, and \, elementwise.
let private structuralSetOperationsAgree: Hedgehog.Property<bool> =
    property {
        let! elements = Gen.list (Range.linear 1 8) (Gen.int32 (Range.linear 0 99))
        let distinct = List.distinct elements

        let pick () =
            Gen.map
                (fun (bits: int list) ->
                    [
                        for (element, bit) in List.zip distinct bits do
                            if bit = 1 then
                                element
                    ])
                (Gen.list (Range.constant distinct.Length distinct.Length) (Gen.int32 (Range.linear 0 1)))

        let! left = pick ()
        let! right = pick ()

        match
            structuralSetOperation "union" distinct left right,
            structuralSetOperation "intersect" distinct left right,
            structuralSetOperation "except" distinct left right
        with
        | Ok unionActual, Ok intersectActual, Ok exceptActual ->
            let expectedUnion =
                distinct |> List.map (fun e -> e, List.contains e left || List.contains e right)

            let expectedIntersect =
                distinct |> List.map (fun e -> e, List.contains e left && List.contains e right)

            let expectedExcept =
                distinct
                |> List.map (fun e -> e, List.contains e left && not (List.contains e right))

            return
                unionActual = Map.ofList expectedUnion
                && intersectActual = Map.ofList expectedIntersect
                && exceptActual = Map.ofList expectedExcept
        | _ -> return false
    }

// ============================================================
// Environments — store-resident, latest-write-wins (D2)
// ============================================================

/// `bind`/`lookup` round-trip through persistence. The *data* lives in the
/// store and survives write → read; the BindingTable pointer is execution
/// state (D4's latitude — the same as `Location`), so the caller re-threads
/// it across a restore, exactly as tests.fs does. This property pins that
/// contract: restored store + re-threaded root ⇒ lookup resolves.
let private bindingsSurvivePersistence: Hedgehog.Property<bool> =
    property {
        let! name = word
        let! v = Gen.int32 (Range.linear -1000 1000)

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state0 = Evaluator.initial store root
        let bindProgram = [ string v; "\"" + name + "\""; "bind" ]

        match
            Evaluator.run
                { state0 with
                    Program = EvaluatorProgram.ofTokensOrFail (bindProgram)
                }
        with
        | Error _ -> return false
        | Ok bound ->
            let restored = Persistence.fromBytes (Persistence.toBytes bound.Store)

            let state1 =
                { Evaluator.initial restored root with
                    BindingTable = bound.BindingTable
                }

            match
                Evaluator.run
                    { state1 with
                        Program = EvaluatorProgram.ofTokensOrFail ([ "\"" + name + "\""; "lookup" ])
                    }
            with
            | Error _ -> return false
            | Ok looked ->
                match looked.Stack with
                | top :: _ ->
                    match Store.lookupTensor top looked.Store with
                    | Some t -> return Tensor.item t 0 = double v
                    | None -> return false
                | [] -> return false
    }

/// Re-binding a name replaces the earlier binding: `lookup` yields the most
/// recent value (append-only association vectors, latest-write-wins).
let private latestWriteWins: Hedgehog.Property<bool> =
    property {
        let! name = word
        let! first = Gen.int32 (Range.linear -100 100)
        let! second = Gen.int32 (Range.linear -100 100)

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state0 = Evaluator.initial store root

        let program =
            [
                string first
                "\"" + name + "\""
                "bind"
                string second
                "\"" + name + "\""
                "bind"
                "\"" + name + "\""
                "lookup"
            ]

        match
            Evaluator.run
                { state0 with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }
        with
        | Error _ -> return false
        | Ok final ->
            match final.Stack with
            | top :: _ ->
                match Store.lookupTensor top final.Store with
                | Some t -> return Tensor.item t 0 = double second
                | None -> return false
            | [] -> return false
    }

/// Unifying a chain of fresh variables and then binding the tail to a value
/// resolves the head all the way through the store-resident substitution.
let private unificationChainResolves: Hedgehog.Property<bool> =
    property {
        let! chainLength = Gen.int32 (Range.linear 0 8)
        let! value = Gen.int32 (Range.linear -1000 1000)

        let names = List.init (chainLength + 1) (fun i -> "?v" + string i)

        let aliases =
            names
            |> List.pairwise
            |> List.collect (fun (left, right) -> [ left; right; "unify"; "drop" ])

        let program =
            aliases
            @ [ names.[chainLength]; string value; "unify"; "drop"; names.[0]; "resolve" ]

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }
        with
        | Error _ -> return false
        | Ok final ->
            match final.Stack with
            | top :: _ ->
                match Store.lookupTensor top final.Store with
                | Some tensor -> return Tensor.item tensor 0 = double value
                | None -> return false
            | [] -> return false
    }

/// A variable nested arbitrarily deep inside a compound term cannot be bound
/// to that term: the occurs-check rejects the cycle instead of diverging.
let private occursCheckPreventsCycles: Hedgehog.Property<bool> =
    property {
        let! depth = Gen.int32 (Range.linear 1 6)

        let rec termTokens depth =
            if depth = 0 then
                [ "?v" ]
            else
                [ "\"f\"" ] @ termTokens (depth - 1) @ [ "1"; "term" ]

        let program = termTokens depth @ [ "?v"; "unify" ]

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }
        with
        | Error _ -> return false
        | Ok final ->
            match final.Stack with
            | top :: _ ->
                match Store.lookupTensor top final.Store with
                | Some tensor -> return Tensor.item tensor 0 = 0.0
                | None -> return false
            | [] -> return false
    }

/// A unary generic dispatches exactly when the quotationRun is a member of its one
/// specializer sort. Membership is the dynamic fallback; a member also exercises
/// the inferred type and the exact specializer tuple.
let private dispatchSelectsExactlyMembers: Hedgehog.Property<bool> =
    property {
        let! members = Gen.list (Range.linear 0 8) (Gen.int32 (Range.linear -20 20))
        let! quotationRun = Gen.int32 (Range.linear -20 20)
        let distinct = List.distinct members
        let memberTokens = distinct |> List.map string

        let program =
            [
                "["
                yield! memberTokens
                "]"
                ":S"
                "seal"
                "drop"
                "\"f\""
                "1"
                "generic"
                "\"f\""
                "{"
                ":S"
                "}"
                "{"
                "drop"
                "1"
                "}"
                "method"
                string quotationRun
                "f"
            ]

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }
        with
        | Error diagnostic ->
            let expectedNoMethod = not (List.contains quotationRun distinct)

            return
                expectedNoMethod
                && (match diagnostic.Error with
                    | NoApplicableMethod _ -> true
                    | _ -> false)
        | Ok final ->
            // A successful dispatch is only correct when the quotationRun really is a
            // member: an implementation that dispatched a nonmember would
            // otherwise satisfy this property.
            if not (List.contains quotationRun distinct) then
                return false
            else
                match final.Stack with
                | top :: _ ->
                    match Store.lookupTensor top final.Store with
                    | Some tensor -> return Tensor.item tensor 0 = 1.0
                    | None -> return false
                | [] -> return false
    }

/// Replacing a control form with an image-resident code cell does not change
/// the arithmetic it wraps: the custom form evaluates the same operand tokens
/// in the same caller frame (D51).
let private controlReplacementPreservesArithmetic: Hedgehog.Property<bool> =
    property {
        let! left = Gen.int32 (Range.linear -100 100)
        let! right = Gen.int32 (Range.linear -100 100)
        let! operation = Gen.item [ "add"; "subtract"; "multiply" ]

        let expected =
            match operation with
            | "add" -> left + right
            | "subtract" -> left - right
            | _ -> left * right

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root

        let bare =
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail ([ string left; string right; operation ])
                }

        let replaced =
            Evaluator.run
                { state with
                    Program =
                        EvaluatorProgram.ofTokensOrFail (
                            [
                                string left
                                string right
                                "{"
                                operation
                                "}"
                                "\"custom\""
                                "control"
                                "custom"
                            ]
                        )
                }

        let topNumber (outcome: Result<EvaluationState, Diagnostic>) =
            match outcome with
            | Error _ -> None
            | Ok final ->
                match Stack.popResolved final.Store final.Stack with
                | Some(tensor, _) -> Some(Tensor.item tensor 0)
                | None -> None

        return
            topNumber bare = Some(double expected)
            && topNumber replaced = Some(double expected)
    }

/// Rolling back to any recorded step and re-running the restored suffix reaches
/// the same value. The suffix is executed from the exact stack and program the
/// step captured, so a wrong restore changes the result (W8/D29).
let private rollbackRestoresAnyStep: Hedgehog.Property<bool> =
    property {
        let! count = Gen.int32 (Range.linear 1 8)

        let! numbers =
            Gen.list (Range.constant count count) (Gen.int32 (Range.linear -50 50))

        let program =
            match numbers with
            | [] -> []
            | first :: rest -> string first :: (rest |> List.collect (fun number -> [ string number; "add" ]))

        let! index = Gen.int32 (Range.linear 0 (max 0 (List.length program - 1)))

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root

        let topNumber (outcome: Result<EvaluationState, Diagnostic>) =
            match outcome with
            | Error _ -> None
            | Ok final ->
                match Stack.popResolved final.Store final.Stack with
                | Some(tensor, _) -> Some(Tensor.item tensor 0)
                | None -> None

        let executed = "history-on" :: program

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail executed
                }
        with
        | Error _ -> return false
        | Ok final ->
            match EvaluatorHistory.rollbackTo index final with
            | Error _ -> return false
            | Ok restored -> return topNumber (Evaluator.run restored) = topNumber (Ok final)
    }

/// A rejected utterance publishes its violation and restores the observable
/// state. The image is append-only, so the store is not part of the equality.
let private discourseRollbackRestoresState: Hedgehog.Property<bool> =
    property {
        let! depth = Gen.int32 (Range.linear 1 5)
        let program = List.replicate depth "add"

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root
        let outcome = Discourse.utter program state

        return
            not outcome.Accepted
            && Discourse.sameObservable state outcome.State
            && List.length outcome.Violations = 1
    }

/// Repair is idempotent: an accepted sentence is its own zero-edit repair.
let private repairIsIdempotent: Hedgehog.Property<bool> =
    property {
        let! subject = Gen.item [ "player"; "enemy" ]
        let! verb = Gen.item [ "can jump"; "can run" ]
        let sentence = "Every " + subject + " " + verb + "."
        let vocabulary = Vocabulary.gameVocabulary
        let tokens = Tokenizer.tokenize vocabulary sentence

        match NearestSentence.nearest vocabulary tokens with
        | Some candidate -> return candidate.Tokens = tokens && candidate.Distance = 0
        | None -> return false
    }

/// An implementation proposal always carries the frozen identity it was proposed
/// against; the gate can therefore reject a proposal for another specification
/// before any solver work.
let private proposalKeepsFrozenIdentity: Hedgehog.Property<bool> =
    property {
        let! flag = Gen.item [ true; false ]

        let target: FixedSpecificationAcceptance.Target =
            {
                Property = "player canJump"
                Sentence =
                    {
                        Pattern = Capability
                        Subject = "player"
                        Conditions = []
                        Verb = "canJump"
                    }
                Conditions = []
                Refinements = []
                SpecificationHash = "spec-hash"
                Source = { File = "test"; Line = 1 }
            }

        let frozen = FixedSpecificationAcceptance.freeze target
        let body = DomainExpression.BooleanConstant flag
        let proposal = FixedSpecificationAcceptance.propose frozen body

        return proposal.Against = frozen.Identity && proposal.Body = body
    }

// ============================================================
// Edge — the surface is derived, never a second authority
// ============================================================

/// Tokenization respects vocabulary activity: a sentence built from any
/// forms (active or inactive) never tokenizes *into* an inactive token, and
/// every emitted token is either an active vocabulary token or a lowercased
/// word of the input sentence. The edge cannot resurrect a retired word.
let private tokenizerRespectsActivity (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let formAndNoise =
            Gen.item (vocabulary |> List.map (fun e -> e.Form))
            |> Gen.bind (fun form -> Gen.map (fun noise -> form, noise) word)

        let! pairs = Gen.list (Range.linear 0 10) formAndNoise
        let sentence = pairs |> List.collect (fun (f, n) -> [ f; n ]) |> String.concat " "
        let tokens = Tokenizer.tokenize vocabulary sentence

        let activeTokens =
            vocabulary |> List.filter (fun e -> e.IsActive) |> List.map (fun e -> e.Token)

        let inactiveTokens =
            vocabulary
            |> List.filter (fun e -> not e.IsActive)
            |> List.map (fun e -> e.Token)

        let inputWords =
            sentence.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun w -> w.ToLower())
            |> List.ofArray

        let noInactive =
            tokens |> List.forall (fun t -> not (List.contains t inactiveTokens))

        let allAccounted =
            tokens
            |> List.forall (fun t -> List.contains t activeTokens || List.contains t inputWords)

        return noInactive && allAccounted
    }

// ============================================================
// Kernel invariants retained from the first harness
// ============================================================

/// The stack is LIFO (guards the `swap` no-op bug class).
let private stackPreservesPushOrder: Hedgehog.Property<bool> =
    property {
        let! values = Gen.list (Range.linear 0 30) value

        let identities, store =
            (Store.empty, values)
            ||> List.mapFold (fun img v -> Store.addTensor (Tensor.scalar v) img)

        let stack =
            List.fold (fun acc identity -> Stack.push identity acc) Stack.empty identities

        let popped =
            let rec drain remaining acc =
                match Stack.popResolved store remaining with
                | Some(tensor, rest) -> drain rest (tensor.Data.[0] :: acc)
                | None -> acc

            drain stack []

        return popped = values
    }

/// Text interning is injective and round-trips (addText/readText).
let private interningIsInjectiveAndRoundtrips: Hedgehog.Property<bool> =
    property {
        let! words = Gen.list (Range.linear 1 12) word

        let identities, store =
            (Store.empty, words) ||> List.mapFold (fun img w -> Store.addText w img)

        let roundtrips =
            List.forall2 (fun w identity -> Store.readText identity store = Some w) words identities

        let reintern =
            words |> List.map (fun w -> Store.addText w store |> fst) = identities

        let injective =
            [
                for (w1, i1) in List.zip words identities do
                    for (w2, i2) in List.zip words identities do
                        if w1 = w2 then i1 = i2 else i1 <> i2
            ]
            |> List.forall id

        return roundtrips && reintern && injective
    }

/// Persistence round-trips a random lexicon byte-for-byte in meaning.
let private persistenceRoundtrips: Hedgehog.Property<bool> =
    property {
        let! tensors = Gen.list (Range.linear 0 25) tensor
        let store = List.fold (fun img t -> snd (Store.addTensor t img)) Store.empty tensors
        let loaded = Persistence.fromBytes (Persistence.toBytes store)
        return store.Lexicon = loaded.Lexicon && store.NextIndex = loaded.NextIndex
    }

let private reclaimStoreAndRoots () =
    Gen.list (Range.linear 0 12) tensor
    |> Gen.bind (fun tensors ->
        let store = List.fold (fun img t -> snd (Store.addTensor t img)) Store.empty tensors
        let count = List.length tensors

        Gen.list (Range.linear 0 6) (Gen.int32 (Range.linear 0 (max 0 (count - 1))))
        |> Gen.map (fun roots -> store, roots |> List.map EntryIdentity.ofInt))

/// The reclaim report is a partition: every entry is either reachable or a
/// candidate, no candidate is reachable, and a declared root is never a
/// candidate. This is the safety half of D52.
let private reclaimReportIsSound: Hedgehog.Property<bool> =
    property {
        let! store, roots = reclaimStoreAndRoots ()
        let report = Reclaim.report roots store
        let entries = store.Lexicon |> Map.toList |> List.map fst |> Set.ofList

        let partition = Set.union report.Reachable (Set.ofList report.Reclaimable) = entries

        let disjoint =
            report.Reclaimable
            |> List.forall (fun identity -> not (Set.contains identity report.Reachable))

        let rootsStay =
            roots
            |> List.forall (fun identity -> not (List.contains identity report.Reclaimable))

        return partition && disjoint && rootsStay
    }

/// Compaction keeps exactly the reachable entries, preserves their tensors and
/// the allocation frontier, and is idempotent. This is the preservation half of
/// D53.
let private compactionPreservesReachability: Hedgehog.Property<bool> =
    property {
        let! store, roots = reclaimStoreAndRoots ()
        let report = Reclaim.report roots store
        let compacted = Reclaim.compact roots store

        let kept =
            report.Reachable
            |> Set.toList
            |> List.forall (fun identity -> Map.tryFind identity compacted.Lexicon = Map.tryFind identity store.Lexicon)

        let dropped =
            report.Reclaimable
            |> List.forall (fun identity -> not (Map.containsKey identity compacted.Lexicon))

        let sameFrontier = compacted.NextIndex = store.NextIndex
        let idempotent = Reclaim.compact roots compacted = compacted
        return kept && dropped && sameFrontier && idempotent
    }

/// A compacted image can be sparse: its lexicon count is below the allocation
/// frontier. The persistence reader must preserve both the holes and the
/// frontier (D53).
let private sparsePersistenceRoundtrips: Hedgehog.Property<bool> =
    property {
        let! store, roots = reclaimStoreAndRoots ()
        let compacted = Reclaim.compact roots store
        let loaded = Persistence.fromBytes (Persistence.toBytes compacted)
        return compacted.Lexicon = loaded.Lexicon && compacted.NextIndex = loaded.NextIndex
    }

let private ownershipTokens =
    [
        "own"
        "borrow"
        "consume"
        "release"
        "duplicate"
        "swap"
        "drop"
        "over"
        "rotate"
        "add"
        "1"
        "\"text\""
        "?v"
    ]

/// The static ownership checker is total: any token sequence returns a verdict,
/// never an exception. A sparse checker that throws on an unknown word would
/// break the "failures are values" rule.
let private ownershipContractIsTotal: Hedgehog.Property<bool> =
    property {
        let! selected = Gen.list (Range.linear 0 20) (Gen.item ownershipTokens)

        let outcome =
            try
                match OwnershipTypes.checkQuotation (String.concat " " selected) with
                | OwnershipTypes.Linear
                | OwnershipTypes.Nonlinear _
                | OwnershipTypes.Undecided _ -> true
            with _ ->
                false

        return outcome
    }

/// The `linearity` word carries the checker's verdict into the image: the
/// tag, status, and reason path agree for every generated token sequence.
let private linearityWordMatchesChecker: Hedgehog.Property<bool> =
    property {
        let! selected = Gen.list (Range.linear 0 8) (Gen.item ownershipTokens)
        let text = String.concat " " selected

        let root, store =
            VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

        let state = Evaluator.initial store root
        let (quoteId, withQuote) = Store.addText text state.Store

        let expectedStatus =
            match OwnershipTypes.checkQuotation text with
            | OwnershipTypes.Linear -> 0.0
            | OwnershipTypes.Nonlinear _ -> 1.0
            | OwnershipTypes.Undecided _ -> 2.0

        match
            Evaluator.run
                { state with
                    Store = withQuote
                    Stack = [ quoteId ]
                    Program = EvaluatorProgram.ofTokensOrFail ([ "linearity" ])
                }
        with
        | Error _ -> return false
        | Ok final ->
            match Stack.popResolved final.Store final.Stack with
            | Some(tensor, _) -> return Tensor.item tensor 0 = LinearVerdictTag && Tensor.item tensor 1 = expectedStatus
            | None -> return false
    }

/// The emitter is a pure function of (vocabulary, sentence), and its output
/// is shaped: module line present, every condition a bool field.
let private emitterIsDeterministicAndShaped (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let subjects =
            (vocabulary
             |> List.filter (fun e -> e.Category = TypeName && e.IsActive)
             |> List.map (fun e -> e.Token))
            @ [ "value" ]

        let fields =
            (vocabulary
             |> List.filter (fun e -> e.Category = FieldName && e.IsActive)
             |> List.map (fun e -> e.Token))
            @ [ "alive" ]

        let verbs =
            (vocabulary
             |> List.filter (fun e -> e.Category = FunctionName && e.IsActive)
             |> List.map (fun e -> e.Token))
            @ [ "behave" ]

        let numericFields =
            (vocabulary
             |> List.filter (fun e -> e.Category = FieldName && e.IsActive)
             |> List.map (fun e -> e.Token))
            @ [ "health" ]

        let! subject = Gen.item subjects

        let! generatedConditions =
            Gen.list
                (Range.linear 0 3)
                (Gen.choice
                    [
                        Gen.item fields |> Gen.map BooleanCondition
                        Gen.item numericFields
                        |> Gen.map (fun field -> NumericCondition(field, ComparisonGreaterThan, 1.0))
                    ])

        // A field has one sort: the same field may be named twice, but not
        // once as boolean and once as numeric. Keeping the first occurrence
        // matches the emitter's one-field-per-name rule.
        let conditions = generatedConditions |> List.distinctBy conditionField

        let! verb = Gen.item verbs

        let parsed =
            {
                Pattern = Capability
                Subject = subject
                Conditions = conditions
                Verb = verb
            }

        let once = Emitter.emit vocabulary parsed
        let twice = Emitter.emit vocabulary parsed
        let moduleOk = once.Contains "module Scale.Generated.Sample"

        let fsharpCondition c =
            vocabulary
            |> List.tryFind (fun e -> e.Token = c && e.IsActive)
            |> Option.map (fun e -> e.ImplementationName)
            |> Option.defaultValue c

        let fieldsOk =
            conditions
            |> List.forall (fun condition ->
                let field = fsharpCondition (conditionField condition)

                match condition with
                | BooleanCondition _ -> once.Contains(field + ": bool")
                | NumericCondition _ -> once.Contains(field + ": float")
                | Disjunction _ -> false)

        return once = twice && moduleOk && fieldsOk
    }

/// Every generated Scale Basic English sentence parses to the canonical
/// pattern, subject, conditions, and verb that generated it (D18).
let private basicEnglishSentencesParse (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let quantifiers =
            vocabulary
            |> List.filter (fun entry ->
                entry.IsActive
                && entry.Category = Quantifier
                && (entry.Token = "every" || entry.Token = "no"))
            |> List.map (fun entry -> entry.Token)

        let subjects =
            vocabulary
            |> List.filter (fun entry -> entry.IsActive && entry.Category = TypeName)
            |> List.map (fun entry -> entry.Token)

        let booleanConditions =
            vocabulary
            |> List.filter (fun entry ->
                entry.IsActive
                && entry.Category = FieldName
                && entry.Form.StartsWith("is ", System.StringComparison.Ordinal))
            |> List.map (fun entry -> [ entry.Token ], BooleanCondition entry.Token)

        let numericFields =
            vocabulary
            |> List.filter (fun entry ->
                entry.IsActive
                && entry.Category = FieldName
                && not (entry.Form.StartsWith("is ", System.StringComparison.Ordinal)))
            |> List.map (fun entry -> entry.Token)

        let comparisons =
            vocabulary
            |> List.filter (fun entry -> entry.IsActive && entry.Category = Comparison)
            |> List.map (fun entry ->
                [ entry.Token ],
                match entry.Token with
                | "greaterThan" -> ComparisonGreaterThan
                | "atLeast" -> ComparisonAtLeast
                | "lessThan" -> ComparisonLessThan
                | "atMost" -> ComparisonAtMost
                | _ -> ComparisonEqualTo)

        let numericConditions =
            [
                for field in numericFields do
                    for (form, comparison) in comparisons do
                        [ "has"; field ] @ form @ [ "0" ], NumericCondition(field, comparison, 0.0)
            ]

        let verbs =
            vocabulary
            |> List.filter (fun entry -> entry.IsActive && entry.Category = FunctionName)
            |> List.map (fun entry -> entry.Token)

        let! quantifier = Gen.item quantifiers
        let! subject = Gen.item subjects

        let! selectedConditions =
            Gen.list (Range.linear 0 3) (Gen.item (booleanConditions @ numericConditions))

        let! verb = Gen.item verbs

        let conditionTokens =
            match selectedConditions with
            | [] -> []
            | (first, _) :: rest -> "that" :: first @ (rest |> List.collect (fun (tokens, _) -> "and" :: tokens))

        let tokens = [ quantifier; subject ] @ conditionTokens @ [ verb; "." ]
        let expectedPattern = if quantifier = "no" then Invariant else Capability
        let expectedConditions = selectedConditions |> List.map snd

        return
            match Parser.parse vocabulary tokens with
            | Some parsed ->
                parsed.Pattern = expectedPattern
                && parsed.Subject = subject
                && parsed.Conditions = expectedConditions
                && parsed.Verb = verb
            | None -> false
    }

/// A toggled vocabulary written into an store reads back identical —
/// the edge's one source of truth is the store, not the seed.
let private vocabularySurvivesImageRoundtrip: Hedgehog.Property<bool> =
    property {
        let! toggles =
            Gen.int32 (Range.linear 0 1)
            |> Gen.list (Range.constant (List.length Vocabulary.gameVocabulary) (List.length Vocabulary.gameVocabulary))
            |> Gen.map (List.map (fun n -> n = 1))

        let seeded =
            Vocabulary.gameVocabulary
            |> List.zip toggles
            |> List.map (fun (active, entry) -> { entry with IsActive = active })

        let root, store = VocabularyInImage.addVocabulary seeded Store.empty

        return
            match VocabularyInImage.readVocabulary (Some root) store with
            | Ok derived -> derived = seeded
            | Error message ->
                printfn "    (readVocabulary error: %s)" message
                false
    }

// ============================================================
// Runner
// ============================================================

/// The tally from a property run. `Failed` counts both falsified properties
/// and `GaveUp`; `Passed + Failed` equals the number of properties run.
type Summary = { Passed: int; Failed: int }

// ---- Actor model properties (D65/D66) --------------------------------------

let private initialEvaluator () : EvaluationState =
    let (root, store) =
        VocabularyInImage.addVocabulary Vocabulary.gameVocabulary Store.empty

    Evaluator.initial store root

let private drainScalars (store: Store) (stack: EntryIdentity list) : RealNumber list =
    let rec loop remaining accumulated =
        match Stack.popResolved store remaining with
        | Some(tensor, rest) -> loop rest (Tensor.item tensor 0 :: accumulated)
        | None -> accumulated

    loop stack []

/// A mailbox is FIFO: N sends followed by N receives yield the sent values in
/// order, for any N and any values.
let private mailboxIsFifo: Hedgehog.Property<bool> =
    property {
        let! messages = Gen.list (Range.linear 0 10) (Gen.int32 (Range.linear -1000 1000))
        let state = initialEvaluator ()

        let program =
            (messages
             |> List.collect (fun number -> [ "self"; string number; "swap"; "send-to" ]))
            @ (messages |> List.map (fun _ -> "receive-message"))

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }
        with
        | Error _ -> return false
        | Ok final -> return drainScalars final.Store final.Stack = (messages |> List.map float)
    }

/// A mailbox queue is image data: any queued list survives a persistence
/// round-trip, in order.
let private mailboxQueueSurvivesPersistence: Hedgehog.Property<bool> =
    property {
        let! messages = Gen.list (Range.linear 0 10) (Gen.int32 (Range.linear -1000 1000))

        let (channelId, store1) =
            Store.addTensor ChannelStore.channelIdentityTensor Store.empty

        let mutable store = store1

        let messageIds =
            messages
            |> List.map (fun number ->
                let (identity, next) = Store.addTensor (Tensor.scalar (float number)) store
                store <- next
                identity)

        let queuedStore = ChannelStore.appendChannelState true messageIds channelId store
        let restored = Persistence.fromBytes (Persistence.toBytes queuedStore)
        let isOpen, queued = ChannelStore.readChannelState channelId restored
        return isOpen && queued = messageIds
    }

/// A number is never a handle: no matter which integer a program pushes, `kill`
/// refuses it unless the scheduler registered that exact entry (D64).
let private numbersAreNotHandles: Hedgehog.Property<bool> =
    property {
        let! number = Gen.int32 (Range.linear -1000 1000)
        let state = initialEvaluator ()

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail ([ string number; "kill" ])
                }
        with
        | Error diagnostic ->
            match diagnostic.Error with
            | TypeMismatch(ErrorAddress _, ExpectedActorHandle) -> return true
            | _ -> return false
        | Ok _ -> return false
    }

/// A supervisor never restarts more than its budget: a permanently failing
/// child ends the run with the exact admitted count.
let private supervisionNeverExceedsBudget: Hedgehog.Property<bool> =
    property {
        let! maxRestarts = Gen.int32 (Range.linear 0 3)
        let state = initialEvaluator ()

        let program =
            [
                string maxRestarts
                "0"
                "0"
                "one-for-one"
                "{"
                "missing"
                "}"
                "\"permanent\""
                "supervise"
                "drop"
            ]
            @ List.replicate (maxRestarts + 3) "yield"

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }
        with
        | Error diagnostic ->
            return diagnostic.Error = LimitError(LimitFault.SupervisionExceeded("one-for-one", maxRestarts))
        | Ok _ -> return false
    }

/// Every actor has its own private handle: N children plus the root produce
/// N+1 distinct handles, all resolving to distinct actors.
let private handlesAreDistinct: Hedgehog.Property<bool> =
    property {
        let! count = Gen.int32 (Range.linear 0 8)
        let state = initialEvaluator ()

        let program =
            List.collect (fun _ -> [ "{"; "halt"; "}"; "spawn"; "drop" ]) [ 1..count ]

        match
            Evaluator.run
                { state with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }
        with
        | Error _ -> return false
        | Ok final ->
            let handles = final.SchedulerState.ActorHandles
            let owners = handles |> Map.toList |> List.map snd

            let liveOwner owner =
                match final.SchedulerState.Actors.TryFind owner with
                | Some actor -> actor.Status <> Terminated
                | None -> false

            let liveOwners = owners |> List.filter liveOwner

            return
                List.length liveOwners = Map.count handles
                && Set.count (Set.ofList owners) = List.length owners
    }

/// P1: the interpreter and the lowered abstraction agree on generated arithmetic.
let private loweringMatchesInterpreter: Hedgehog.Property<bool> =
    property {
        let! left = Gen.int32 (Range.linear -100 100)
        let! right = Gen.int32 (Range.linear -100 100)
        let! operation = Gen.item [ "add"; "subtract"; "multiply" ]
        let program = [ string left; string right; operation ]
        let seed = initialEvaluator ()

        let interpreted =
            Evaluator.run
                { seed with
                    Program = EvaluatorProgram.ofTokensOrFail (program)
                }

        let instructions = LoweredInstructions.lowerTokens program
        let kernelSeed = EvaluatorKernelBridge.toKernel { seed with Program = [] }

        let lowered =
            LoweredAbstraction.run EvaluatorKernelBridge.executeWord instructions kernelSeed

        let topOf (state: EvaluationState) =
            match Stack.popResolved state.Store state.Stack with
            | Some(tensor, _) ->
                match ScalarRead.readScalar tensor with
                | Ok value -> Some value
                | Error _ -> None
            | None -> None

        match interpreted, lowered with
        | Ok interpretedState, Ok loweredState ->
            let loweredHost = EvaluatorKernelBridge.fromKernel loweredState
            return topOf interpretedState = topOf loweredHost
        | _ -> return false
    }

/// P2: the inline and line prose forms parse to the same tree.
let private proseFormsAgree (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! subject = Gen.int32 (Range.linear -20 20)
        let! first = Gen.int32 (Range.linear -20 20)
        let! firstBody = Gen.int32 (Range.linear -100 100)
        let! second = Gen.int32 (Range.linear -20 20)
        let! secondBody = Gen.int32 (Range.linear -100 100)
        let! fallback = Gen.int32 (Range.linear -100 100)

        let inlineText =
            "match "
            + string subject
            + ", when "
            + string first
            + " then "
            + string firstBody
            + ", when "
            + string second
            + " then "
            + string secondBody
            + ", otherwise "
            + string fallback
            + "."

        let lineText =
            "match "
            + string subject
            + ".\nwhen "
            + string first
            + " then "
            + string firstBody
            + ".\nwhen "
            + string second
            + " then "
            + string secondBody
            + ".\notherwise "
            + string fallback
            + "."

        match ProseParser.parseProgram vocabulary inlineText, ProseParser.parseProgram vocabulary lineText with
        | Ok inlineExpression, Ok lineExpression -> return inlineExpression = lineExpression
        | _ -> return false
    }

/// P0: construct a declared shape, then destructure it through a match with the
/// same arity. The chosen field must come back in declared order.
let private proseConstructorRoundTrips (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! valueCount = Gen.int32 (Range.linear 1 4)
        let count = int valueCount
        let! values = Gen.list (Range.constant count count) (Gen.int32 (Range.linear 0 99))
        let! chosen = Gen.int32 (Range.linear 0 (count - 1))
        let chosenIndex = int chosen

        let fieldNames = [ 0 .. count - 1 ] |> List.map (fun index -> "f" + string index)

        let patternNames = [ 0 .. count - 1 ] |> List.map (fun index -> "x" + string index)

        let text =
            "shape point with "
            + String.concat " " fieldNames
            + ".\nlogic pick p = match p, when point("
            + String.concat " " patternNames
            + ") then "
            + List.item chosenIndex patternNames
            + ", otherwise 999 close.\nlogic main = pick(point("
            + (values |> List.map string |> String.concat " ")
            + ")).\n"

        let path =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "scale-constructor-property-" + System.Guid.NewGuid().ToString("N") + ".book"
            )

        System.IO.File.WriteAllText(path, text)

        let outcome =
            match ProseParser.parseBook vocabulary text with
            | Error _ -> false
            | Ok _ ->
                match BookRunner.cast (initialEvaluator ()) path (Some "main") [] with
                | Error _ -> false
                | Ok final ->
                    match Stack.popResolved final.Store final.Stack with
                    | Some(tensor, _) -> Tensor.item tensor 0 = float (List.item chosenIndex values)
                    | None -> false

        System.IO.File.Delete path
        return outcome
    }

/// P0: a dyadic decimal literal parses to its exact binary64 value.
let private proseRealLiteralRoundTrips (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! whole = Gen.int32 (Range.linear 0 999)
        let! eighths = Gen.int32 (Range.linear 0 7)
        let! negative = Gen.int32 (Range.linear 0 1)

        let fraction = (int eighths * 125).ToString("000")
        let text = (if negative = 1 then "-" else "") + string whole + "." + fraction + "."

        let expected =
            let magnitude = float whole + float eighths / 8.0
            if negative = 1 then -magnitude else magnitude

        let parsed =
            match ProseParser.parseBook vocabulary ("logic main = " + text) with
            | Ok document ->
                match document.Definitions with
                | [ definition ] ->
                    match definition.Body with
                    | ExpressionLogic.Literal(ExpressionLogic.NumberLiteral value) -> value = expected
                    | _ -> false
                | _ -> false
            | Error _ -> false

        return parsed
    }

/// P0: nested local bindings evaluate in order and feed the body.
let private proseLocalBindingRoundTrips (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! a = Gen.int32 (Range.linear -50 50)
        let! b = Gen.int32 (Range.linear -50 50)

        let text =
            "logic main = let x = " + string a + " in let y = " + string b + " in add(x y)."

        let path =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "scale-local-binding-property-" + System.Guid.NewGuid().ToString("N") + ".book"
            )

        System.IO.File.WriteAllText(path, text)

        let outcome =
            match ProseParser.parseBook vocabulary text with
            | Error _ -> false
            | Ok _ ->
                match BookRunner.cast (initialEvaluator ()) path (Some "main") [] with
                | Error _ -> false
                | Ok final ->
                    match Stack.popResolved final.Store final.Stack with
                    | Some(tensor, _) -> Tensor.item tensor 0 = float a + float b
                    | None -> false

        System.IO.File.Delete path
        return outcome
    }

/// P0: a generated argument shape selects the matching method.
let private proseDispatchSelectsByShape (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! choice = Gen.int32 (Range.linear 0 1)

        let construction, expected =
            if choice = 0 then "point(1 2)", 1.0 else "circle(1)", 2.0

        let text =
            "generic area with 1.\nshape point with x y.\nshape circle with r.\nmethod area(p : point) = 1.\nmethod area(p : circle) = 2.\nlogic main = area("
            + construction
            + ").\n"

        let path =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "scale-dispatch-property-" + System.Guid.NewGuid().ToString("N") + ".book"
            )

        System.IO.File.WriteAllText(path, text)

        let outcome =
            match ProseParser.parseBook vocabulary text with
            | Error _ -> false
            | Ok _ ->
                match BookRunner.cast (initialEvaluator ()) path (Some "main") [] with
                | Error _ -> false
                | Ok final ->
                    match Stack.popResolved final.Store final.Stack with
                    | Some(tensor, _) -> Tensor.item tensor 0 = expected
                    | None -> false

        System.IO.File.Delete path
        return outcome
    }

/// P3: a generated function value applies its parameter through the lowered
/// path. This is the parser, lowering, and abstraction in one property.
let private proseFunctionValue (vocabulary: VocabularyEntry list) : Hedgehog.Property<bool> =
    property {
        let! argument = Gen.int32 (Range.linear -50 50)
        let! offset = Gen.int32 (Range.linear -50 50)

        let text =
            string argument + ". logic (x) = add(x " + string offset + ") close. apply."

        match ProseParser.parseProgram vocabulary text with
        | Error _ -> return false
        | Ok expression ->
            match EvaluatorLowering.lowerExpression expression with
            | Error _ -> return false
            | Ok program ->
                let units =
                    program.Units
                    |> List.map (fun unit -> unit.Name, unit.Instructions)
                    |> Map.ofList

                let state = initialEvaluator ()

                match
                    LoweredAbstraction.runProgram
                        units
                        EvaluatorKernelBridge.executeWord
                        program.Instructions
                        (EvaluatorKernelBridge.toKernel state)
                with
                | Ok final ->
                    match Stack.popResolved final.Store final.Stack with
                    | Some(tensor, _) ->
                        match ScalarRead.readScalar tensor with
                        | Ok value -> return value = float (argument + offset)
                        | Error _ -> return false
                    | None -> return false
                | Error _ -> return false
    }

/// A checked lattice constructor produces a value that validate accepts.
let private latticeCreateValidates : Hedgehog.Property<bool> =
    property {
        let! x = Gen.int32 (Range.linear -100 100)

        match
            TypeLattice.create
                [ "x", DomainExpression.IntegerType, [ ScaleFile.NumericValue(float x) ] ]
        with
        | Ok lattice -> return TypeLattice.validate lattice = Ok()
        | Error _ -> return false
    }

/// A lattice data document round-trips through the canonical reader.
let private latticeJsonRoundTrips : Hedgehog.Property<bool> =
    property {
        let! x = Gen.int32 (Range.linear -100 100)

        let lattice: TypeLattice.Lattice =
            {
                Fields =
                    [
                        {
                            Name = "x"
                            Type = DomainExpression.IntegerType
                            Coordinates = Some [ ScaleFile.NumericValue(float x) ]
                            Narrowing = []
                        }
                    ]
            }

        match TypeLattice.ofJson (TypeLattice.toJson lattice) with
        | Ok roundTripped -> return TypeLattice.fieldNames roundTripped = [ "x" ]
        | Error _ -> return false
    }

/// A row unifies with itself without substitution.
let private rowUnificationIsReflexive : Hedgehog.Property<bool> =
    property {
        let! labels =
            Gen.list (Range.linear 0 4) (Gen.item [ "a"; "b"; "c"; "d" ])
            |> Gen.map List.distinct

        match RowTypes.ofList labels with
        | Error _ -> return false
        | Ok row ->
            match RowTypes.unify "row" row row RowTypes.emptySubstitution 0 with
            | Ok(substitution, _) -> return Map.isEmpty substitution
            | Error _ -> return false
    }

/// An open row absorbs the other side's extra labels.
let private rowAbsorptionBindsExtraLabels : Hedgehog.Property<bool> =
    property {
        let! extra =
            Gen.list (Range.linear 0 3) (Gen.item [ "b"; "c"; "d" ])
            |> Gen.map List.distinct

        let openRow =
            RowTypes.Extend("a", RowTypes.Variable(RowTypes.RowVariable 0))

        match RowTypes.ofList ("a" :: extra) with
        | Error _ -> return false
        | Ok closedRow ->
            match RowTypes.unify "row" openRow closedRow RowTypes.emptySubstitution 1 with
            | Ok(substitution, _) ->
                match Map.tryFind (RowTypes.RowVariable 0) substitution with
                | Some bound -> return Set.ofList (RowTypes.labelsOf bound) = Set.ofList extra
                | None -> return false
            | Error _ -> return false
    }

/// A closed set unifies with itself without substitution.
let private setUnificationIsReflexive : Hedgehog.Property<bool> =
    property {
        let! labels =
            Gen.list (Range.linear 0 4) (Gen.item [ "a"; "b"; "c"; "d" ])
            |> Gen.map Set.ofList

        match LabelSets.unify "set" (LabelSets.closed labels) (LabelSets.closed labels) Map.empty 0 with
        | Ok(substitution, _) -> return Map.isEmpty substitution
        | Error _ -> return false
    }

/// An open set absorbs the other side's extra labels.
let private setAbsorptionBindsExtraLabels : Hedgehog.Property<bool> =
    property {
        let! extra =
            Gen.list (Range.linear 0 3) (Gen.item [ "b"; "c"; "d" ])
            |> Gen.map Set.ofList

        let openSet =
            LabelSets.Open(Set.singleton "a", LabelSets.SetVariable 0)

        match LabelSets.unify "set" openSet (LabelSets.closed (Set.add "a" extra)) Map.empty 1 with
        | Ok(substitution, _) ->
            match Map.tryFind (LabelSets.SetVariable 0) substitution with
            | Some bound -> return LabelSets.known bound = extra
            | None -> return false
        | Error _ -> return false
    }

/// The kind dispatcher refuses a row/set pair.
let private kindDispatcherSeparatesRowsAndSets : Hedgehog.Property<bool> =
    property {
        let! _ = Gen.constant ()

        match
            Unification.unifyKinded
                (Unification.RowTerm RowTypes.Empty)
                (Unification.SetTerm(LabelSets.Closed(Set.singleton "a")))
                Unification.emptyState
        with
        | Error(Unification.KindMismatch _) -> return true
        | _ -> return false
    }

/// A scheme quantifies type, row, and set variables together.
let private schemeQuantifiesAllThreeKinds : Hedgehog.Property<bool> =
    property {
        let! _ = Gen.constant ()

        let term =
            Unification.Record(
                RowTypes.Extend(
                    {
                        Name = "field"
                        Type =
                            Unification.Function(
                                [],
                                Unification.Primitive "number",
                                LabelSets.Open(Set.empty, LabelSets.SetVariable 0)
                            )
                    },
                    RowTypes.Variable(RowTypes.RowVariable 0)
                )
            )

        let scheme = Unification.generalize Set.empty Set.empty term
        return scheme.QuantifiedRows = [ RowTypes.RowVariable 0 ]
               && scheme.QuantifiedSets = [ LabelSets.SetVariable 0 ]
    }

/// A type variable never binds a row or set term.
let private kindTypeVariableStaysInItsKind : Hedgehog.Property<bool> =
    property {
        let! _ = Gen.constant ()
        let typeVariable = Unification.TypeTerm(Unification.Variable 0)
        let row = Unification.RowTerm RowTypes.Empty
        let set = Unification.SetTerm(LabelSets.Closed(Set.singleton "a"))

        let refused left right =
            match Unification.unifyKinded left right Unification.emptyState with
            | Error(Unification.KindMismatch _) -> true
            | _ -> false

        return refused typeVariable row && refused typeVariable set
    }

/// A record field may hold an effectful function without turning its row into
/// an effect set.
let private fieldEffectBoundary : Hedgehog.Property<bool> =
    property {
        let! label = Gen.item [ "f"; "g"; "h" ]

        let field: Unification.RecordField =
            {
                Name = label
                Type =
                    Unification.Function(
                        [],
                        Unification.Primitive "number",
                        LabelSets.Open(Set.empty, LabelSets.SetVariable 0)
                    )
            }

        let openRecord =
            Unification.Record(
                RowTypes.Extend(field, RowTypes.Variable(RowTypes.RowVariable 0))
            )

        let closedField =
            {
                field with
                    Type =
                        Unification.Function(
                            [],
                            Unification.Primitive "number",
                            LabelSets.Closed(Set.singleton "io")
                        )
            }

        let closedRecord = Unification.Record(RowTypes.Extend(closedField, RowTypes.Empty))

        match Unification.unifyType openRecord closedRecord Unification.emptyState with
        | Ok state ->
            return not state.RecordRows.IsEmpty && not state.Sets.IsEmpty
        | Error _ -> return false
    }

/// Field access and effect propagation compose through one expression.
let private fieldAndEffect : Hedgehog.Property<bool> =
    property {
        let! effect = Gen.item [ "io"; "net"; "db" ]

        let recordType =
            TypeRetrieval.Record(
                RowTypes.Extend(
                    {
                        Name = "f"
                        Type =
                            TypeRetrieval.Function(
                                [],
                                TypeRetrieval.Primitive "number",
                                TypeRetrieval.closedEffects (Set.singleton effect)
                            )
                    },
                    RowTypes.Empty
                )
            )

        let signature: TypeRetrieval.WordSignature =
            {
                Quantified = []
                QuantifiedEffects = []
                QuantifiedRows = []
                Parameters = []
                Result = recordType
                Effects = TypeRetrieval.emptyEffects
            }

        let environment = Map.ofList [ "record", [ TypeRetrieval.wordScheme signature ] ]

        let expression =
            Call(
                Call(
                    Word "field",
                    [ Literal(TextLiteral "f"); Call(Word "record", []) ]
                ),
                []
            )

        match TypeRetrieval.infer [] TypeRetrieval.emptyInferenceState environment expression with
        | Ok(inferred, _) ->
            return LabelSets.known inferred.Effects = Set.singleton effect
        | Error _ -> return false
    }

/// The host adapter and the reference model agree on the closed effect fragment.
let private hostReferenceDifferential : Hedgehog.Property<bool> =
    property {
        let! left =
            Gen.list (Range.linear 0 5) (Gen.item [ "io"; "net"; "db" ])
            |> Gen.map Set.ofList

        let! right =
            Gen.list (Range.linear 0 5) (Gen.item [ "io"; "net"; "db" ])
            |> Gen.map Set.ofList

        let reference =
            Unification.unifyType
                (Unification.Function([], Unification.Primitive "number", LabelSets.Closed left))
                (Unification.Function([], Unification.Primitive "number", LabelSets.Closed right))
                Unification.emptyState
            |> Result.isOk

        let host =
            TypeRetrieval.unifyWith
                (TypeRetrieval.Function([], TypeRetrieval.Primitive "number", TypeRetrieval.closedEffects left))
                (TypeRetrieval.Function([], TypeRetrieval.Primitive "number", TypeRetrieval.closedEffects right))
                TypeRetrieval.emptyUnification
            |> Result.isOk

        return reference = host
    }

/// Closed subtyping is transitive on records and effect sets.
let private subtypeTransitive : Hedgehog.Property<bool> =
    property {
        let! labels =
            Gen.list (Range.linear 0 5) (Gen.item [ "io"; "net"; "db" ])
            |> Gen.map Set.ofList

        let field name : TypeRetrieval.RecordField =
            { Name = name; Type = TypeRetrieval.Primitive "number" }

        let record names =
            TypeRetrieval.Record(
                List.foldBack (fun name rest -> RowTypes.Extend(field name, rest)) names RowTypes.Empty
            )

        let small = record [ "x" ]
        let medium = record [ "x"; "y" ]
        let large = record [ "x"; "y"; "z" ]
        let closed = LabelSets.Closed labels

        let recordTransitive =
            TypeRetrieval.subtype large medium
            && TypeRetrieval.subtype medium small
            && TypeRetrieval.subtype large small

        let effectTransitive =
            TypeRetrieval.effectSubset (LabelSets.Closed Set.empty) closed
            && TypeRetrieval.effectSubset closed closed
            && TypeRetrieval.effectSubset (LabelSets.Closed Set.empty) closed

        return recordTransitive && effectTransitive
    }

/// A row variable cannot absorb a label it lacks.
let private rowLacksRefuses : Hedgehog.Property<bool> =
    property {
        let! labels =
            Gen.list (Range.linear 1 5) (Gen.item [ "a"; "b"; "c"; "d" ])
            |> Gen.map Set.ofList

        let lacked = Set.minElement labels
        let variable = RowTypes.RowVariable 0

        match
            RowTypes.unifyWithLacks
                "row"
                (RowTypes.Variable variable)
                (RowTypes.Extend(lacked, RowTypes.Empty))
                RowTypes.emptySubstitution
                (Map.ofList [ variable, labels ])
                1
        with
        | Error(RowTypes.LacksViolation _) -> return true
        | _ -> return false
    }

/// Effect subtraction is set difference on a closed label set.
let private labelSetSubtractionIsSetDifference : Hedgehog.Property<bool> =
    property {
        let! left =
            Gen.list (Range.linear 0 6) (Gen.int32 (Range.linear 0 9))
            |> Gen.map Set.ofList

        let! right =
            Gen.list (Range.linear 0 6) (Gen.int32 (Range.linear 0 9))
            |> Gen.map Set.ofList

        match LabelSets.subtractClosed right (LabelSets.closed left) with
        | Ok result -> return LabelSets.known result = Set.difference left right
        | Error _ -> return false
    }

/// A declarative IO plan never escapes its installed directory scope.
let private declarativePlanStaysInScope : Hedgehog.Property<bool> =
    property {
        let! suffix = Gen.string (Range.linear 1 5) (Gen.item [ 'a' .. 'e' ])
        let path = "photos/" + suffix + ".png"

        let installText =
            "install photos {\n"
            + "  resource files : directory at \"photos\" recursive true follow-links false hidden false\n"
            + "  resource trash : trash at \"photos/.trash\"\n"
            + "  resource vision : vision at \"aesthetic-v1\" threshold 0.85\n"
            + "  default delete trash\n"
            + "}"

        match DeclarativeIo.parse installText with
        | Error _ -> return false
        | Ok installation ->
            let evidence: DeclarativeIo.VerdictEvidence =
                {
                    ModelId = "aesthetic-v1"
                    ModelVersion = "1.0"
                    Score = 0.9
                    ContentHash = path
                }

            let candidate: DeclarativeIo.Candidate =
                {
                    Path = path
                    Hash = path
                    Kind = DeclarativeIo.RegularFile
                    Verdict = DeclarativeIo.NotPretty
                    Evidence = evidence
                }

            let intent =
                DeclarativeIo.Remove(
                    DeclarativeIo.CapabilityName.create "files",
                    None,
                    DeclarativeIo.NotPrettyPredicate
                )

            match DeclarativeIo.planRemove installation intent [ candidate ] with
            | Ok plan ->
                return
                    plan.Items
                    |> List.forall (fun item ->
                        item.Path.StartsWith("photos/", System.StringComparison.Ordinal))
            | Error _ -> return false
    }

/// Open effect subtraction records a lacks constraint and refuses a later
/// binding that contains the removed label.
let private setSubtractOpenLacks : Hedgehog.Property<bool> =
    property {
        let! labels =
            Gen.list (Range.linear 1 5) (Gen.item [ 1; 2; 3; 4 ])
            |> Gen.map Set.ofList

        let removed = Set.minElement labels
        let variable = LabelSets.SetVariable 0

        match
            LabelSets.subtractOpen
                (Set.singleton removed)
                (LabelSets.Open(labels, variable))
                LabelSets.emptyLacks
        with
        | Ok(set, lacks) ->
            let lacksOk =
                Map.tryFind variable lacks = Some(Set.singleton removed)

            let refused =
                match
                    LabelSets.unifyWithLacks
                        "set"
                        (LabelSets.Open(Set.empty, variable))
                        (LabelSets.closed (Set.singleton removed))
                        Map.empty
                        lacks
                        1
                with
                | Error(LabelSets.LacksViolation _) -> true
                | _ -> false

            return lacksOk && refused && LabelSets.known set = Set.remove removed labels
        | Error _ -> return false
    }

/// The verification lattice is a pure value: product composition is a
/// validating operation over generated coordinates.
let private latticeProductComposes : Hedgehog.Property<bool> =
    property {
        let! x = Gen.int32 (Range.linear -100 100)
        let! y = Gen.int32 (Range.linear -100 100)

        let left: TypeLattice.Lattice =
            {
                Fields =
                    [
                        {
                            Name = "x"
                            Type = DomainExpression.IntegerType
                            Coordinates = Some [ ScaleFile.NumericValue(float x) ]
                            Narrowing = []
                        }
                    ]
            }

        let right: TypeLattice.Lattice =
            {
                Fields =
                    [
                        {
                            Name = "y"
                            Type = DomainExpression.IntegerType
                            Coordinates = Some [ ScaleFile.NumericValue(float y) ]
                            Narrowing = []
                        }
                    ]
            }

        match TypeLattice.product left right with
        | Ok combined -> return TypeLattice.fieldNames combined = [ "x"; "y" ]
        | Error _ -> return false
    }

/// Runs all properties and returns the per-property tally. Called from
/// Tests.runAll once the derived vocabulary is available. The caller folds
/// both counts into the suite totals, so a single property regression is
/// visible in the summary instead of collapsing to one generic failure.
/// The standalone instruction document keeps the image's opcode spelling, so a
/// program survives a code value and a history step unchanged (D202, W1a).
let private instructionDocumentRoundTrips: Hedgehog.Property<bool> =
    property {
        let! body = Gen.list (Range.linear 0 8) imageInstruction
        let text = PreludeImage.toInstructionDocumentJson body
        return PreludeImage.fromInstructionDocumentJson text = Ok body
    }

/// The token boundary reads a token the same way in both encodings, which is what
/// lets the interpreter's program move onto the instruction set (D202, W1b).
let private tokenBoundaryAgrees: Hedgehog.Property<bool> =
    property {
        let! tokens =
            Gen.list
                (Range.linear 0 6)
                (Gen.frequency
                    [
                        (2, Gen.map string (Gen.int32 (Range.linear -5 5)))
                        (2, word)
                    ])

        let shapesAgree
            (terms: SymbolicAbstraction.Term list)
            (instructions: LoweredInstructions.Instruction list)
            =
            List.length terms = List.length instructions
            && List.forall2
                (fun term instruction ->
                    match term, instruction with
                    | SymbolicAbstraction.LiteralNumber termValue, LoweredInstructions.LiteralNumber instructionValue ->
                        termValue = instructionValue
                    | SymbolicAbstraction.Word termName, LoweredInstructions.CallWord(instructionName, None) ->
                        termName = instructionName
                    | _ -> false)
                terms
                instructions

        match EvaluatorProgram.ofTokens tokens, EvaluatorProgram.ofTokensAsInstructions tokens with
        | Ok terms, Ok instructions -> return shapesAgree terms instructions
        | _ -> return false
    }

let runAll (vocabulary: VocabularyEntry list) : Summary =
    passed <- 0
    failed <- 0
    printfn "Properties (Hedgehog):"
    printfn "  — one thing —"
    holds "EntryIdentity projects injectively into rank-0 tensors (level model)" entryIdProjectionIsInjective
    printfn "  — one structure —"
    holds "the store is append-only (existing entries never change)" imageIsAppendOnly
    holds "text interning is injective and round-trips" interningIsInjectiveAndRoundtrips
    holds "persistence round-trips a random lexicon" persistenceRoundtrips
    holds "reclaim partitions every entry into reachable or candidate" reclaimReportIsSound
    holds "compaction keeps exactly the reachable entries" compactionPreservesReachability
    holds "a sparse image round-trips with its frontier" sparsePersistenceRoundtrips
    holds "the ownership checker is total" ownershipContractIsTotal
    holds "the linearity word carries the checker verdict" linearityWordMatchesChecker
    holds "control replacement preserves arithmetic" controlReplacementPreservesArithmetic
    holds "rollback restores any recorded step" rollbackRestoresAnyStep
    holds "discourse rollback restores the state after an input fault" discourseRollbackRestoresState
    holds "repair is idempotent on an accepted sentence" repairIsIdempotent
    holds "an implementation proposal keeps its frozen identity" proposalKeepsFrozenIdentity
    printfn "  — one protocol —"
    holds "railway: running any generated program yields a value, never an exception" (railwayIsTotal vocabulary)
    holds "evaluation is deterministic (same program, same result)" (evaluationIsDeterministic vocabulary)
    holds "well-formed arithmetic programs succeed with the expected value" wellFormedArithmeticSucceeds
    holds "the interpreter and the lowered abstraction agree on generated arithmetic" loweringMatchesInterpreter
    printfn "  — semantics —"
    holds "nor is set complement over an explicit universe" norIsSetComplement
    holds "and/or agree with intersection and union" andOrAreInterUnion
    holds "union/intersect/except agree with set algebra" structuralSetOperationsAgree
    holds "bindings survive persistence (data in store, root re-threaded)" bindingsSurvivePersistence
    holds "latest write wins for re-bound names" latestWriteWins
    holds "unification resolves a variable chain to its value" unificationChainResolves
    holds "occurs-check prevents cyclic terms" occursCheckPreventsCycles
    holds "dispatch selects exactly the declared sort members" dispatchSelectsExactlyMembers
    holds "lattice product composition is a validating pure operation" latticeProductComposes
    holds "a checked lattice constructor validates" latticeCreateValidates
    holds "a lattice data document round-trips" latticeJsonRoundTrips
    holds "a row unifies with itself" rowUnificationIsReflexive
    holds "an open row absorbs extra labels" rowAbsorptionBindsExtraLabels
    holds "a row variable cannot absorb a label it lacks" rowLacksRefuses
    holds "a closed set unifies with itself" setUnificationIsReflexive
    holds "an open set absorbs extra labels" setAbsorptionBindsExtraLabels
    holds "the kind dispatcher separates rows and sets" kindDispatcherSeparatesRowsAndSets
    holds "a scheme quantifies type, row, and set variables" schemeQuantifiesAllThreeKinds
    holds "a type variable stays in its kind" kindTypeVariableStaysInItsKind
    holds "a record field can hold an effectful function" fieldEffectBoundary
    holds "field access and effect propagation compose" fieldAndEffect
    holds "host and reference agree on closed effects" hostReferenceDifferential
    holds "closed subtyping is transitive" subtypeTransitive
    holds "effect subtraction is set difference on closed label sets" labelSetSubtractionIsSetDifference
    holds "open effect subtraction records a lacks constraint" setSubtractOpenLacks
    holds "a declarative plan stays inside its installed scope" declarativePlanStaysInScope
    printfn "  — edge —"
    holds "tokenizer never resurrects inactive words" (tokenizerRespectsActivity vocabulary)

    holds
        "generated Scale Basic English sentences parse to their canonical form"
        (basicEnglishSentencesParse vocabulary)

    holds "the inline and line prose forms parse to the same tree" (proseFormsAgree vocabulary)

    holds "a generated function value applies its parameter" (proseFunctionValue vocabulary)

    holds "a constructed shape destructures its fields in declared order" (proseConstructorRoundTrips vocabulary)

    holds "a dyadic decimal literal parses to its exact binary64 value" (proseRealLiteralRoundTrips vocabulary)

    holds "nested local bindings evaluate in order and feed the body" (proseLocalBindingRoundTrips vocabulary)

    holds "a generated argument shape selects the matching method" (proseDispatchSelectsByShape vocabulary)

    holds "emitter is deterministic and shaped" (emitterIsDeterministicAndShaped vocabulary)
    holds "vocabulary survives store round-trip with activity toggles" vocabularySurvivesImageRoundtrip
    holds "stack preserves push order (LIFO)" stackPreservesPushOrder
    holds "a mailbox delivers messages in FIFO order" mailboxIsFifo
    holds "a mailbox queue survives persistence" mailboxQueueSurvivesPersistence
    holds "a number is never an actor handle" numbersAreNotHandles
    holds "supervision never exceeds its restart budget" supervisionNeverExceedsBudget
    holds "every actor has a distinct private handle" handlesAreDistinct
    printfn "  — kernel image —"
    holds "image instructions round-trip through JSON" imageInstructionsRoundTrip
    holds "an instruction document round-trips through its own codec" instructionDocumentRoundTrips
    holds "the token boundary reads tokens the same way in both encodings" tokenBoundaryAgrees
    holds "arbitrary image bytes never throw" imageArbitraryBytesAreTotal
    holds "an unresolved image jump is refused" imageUnresolvedJumpIsRefused
    holds "image export resolution is consistent" imageExportResolutionIsConsistent
    holds "a valid image dependency DAG round-trips" imageValidDagRoundTrips
    { Passed = passed; Failed = failed }

/// Exposes the property suite as (name, property) pairs so hosts like the granular runner
/// can discover and run each Hedgehog property individually instead of as
/// one opaque `runAll` block.


let propertyTests (vocabulary: VocabularyEntry list) : (string * Hedgehog.Property<bool>) list =
    [
        ("EntryIdentity projects injectively into rank-0 tensors (level model)", entryIdProjectionIsInjective)
        ("the store is append-only (existing entries never change)", imageIsAppendOnly)
        ("text interning is injective and round-trips", interningIsInjectiveAndRoundtrips)
        ("persistence round-trips a random lexicon", persistenceRoundtrips)
        ("reclaim partitions every entry into reachable or candidate", reclaimReportIsSound)
        ("compaction keeps exactly the reachable entries", compactionPreservesReachability)
        ("a sparse image round-trips with its frontier", sparsePersistenceRoundtrips)
        ("the ownership checker is total", ownershipContractIsTotal)
        ("the linearity word carries the checker verdict", linearityWordMatchesChecker)
        ("control replacement preserves arithmetic", controlReplacementPreservesArithmetic)
        ("rollback restores any recorded step", rollbackRestoresAnyStep)
        ("discourse rollback restores the state after an input fault", discourseRollbackRestoresState)
        ("repair is idempotent on an accepted sentence", repairIsIdempotent)
        ("an implementation proposal keeps its frozen identity", proposalKeepsFrozenIdentity)
        ("railway: running any generated program yields a value, never an exception", railwayIsTotal vocabulary)
        ("evaluation is deterministic (same program, same result)", evaluationIsDeterministic vocabulary)
        ("well-formed arithmetic programs succeed with the expected value", wellFormedArithmeticSucceeds)
        ("nor is set complement over an explicit universe", norIsSetComplement)
        ("and/or agree with intersection and union", andOrAreInterUnion)
        ("union/intersect/except agree with set algebra", structuralSetOperationsAgree)
        ("bindings survive persistence (data in store, root re-threaded)", bindingsSurvivePersistence)
        ("latest write wins for re-bound names", latestWriteWins)
        ("unification resolves a variable chain to its value", unificationChainResolves)
        ("occurs-check prevents cyclic terms", occursCheckPreventsCycles)
        ("dispatch selects exactly the declared sort members", dispatchSelectsExactlyMembers)
        ("tokenizer never resurrects inactive words", tokenizerRespectsActivity vocabulary)
        ("generated Scale Basic English sentences parse to their canonical form", basicEnglishSentencesParse vocabulary)
        ("the interpreter and the lowered abstraction agree on generated arithmetic", loweringMatchesInterpreter)
        ("the inline and line prose forms parse to the same tree", proseFormsAgree vocabulary)
        ("a generated function value applies its parameter", proseFunctionValue vocabulary)
        ("a constructed shape destructures its fields in declared order", proseConstructorRoundTrips vocabulary)
        ("a dyadic decimal literal parses to its exact binary64 value", proseRealLiteralRoundTrips vocabulary)
        ("nested local bindings evaluate in order and feed the body", proseLocalBindingRoundTrips vocabulary)
        ("a generated argument shape selects the matching method", proseDispatchSelectsByShape vocabulary)
        ("emitter is deterministic and shaped", emitterIsDeterministicAndShaped vocabulary)
        ("vocabulary survives store round-trip with activity toggles", vocabularySurvivesImageRoundtrip)
        ("stack preserves push order (LIFO)", stackPreservesPushOrder)
        ("a mailbox delivers messages in FIFO order", mailboxIsFifo)
        ("a mailbox queue survives persistence", mailboxQueueSurvivesPersistence)
        ("a number is never an actor handle", numbersAreNotHandles)
        ("supervision never exceeds its restart budget", supervisionNeverExceedsBudget)
        ("every actor has a distinct private handle", handlesAreDistinct)
        ("image instructions round-trip through JSON", imageInstructionsRoundTrip)
        ("an instruction document round-trips through its own codec", instructionDocumentRoundTrips)
        ("the token boundary reads tokens the same way in both encodings", tokenBoundaryAgrees)
        ("arbitrary image bytes never throw", imageArbitraryBytesAreTotal)
        ("an unresolved image jump is refused", imageUnresolvedJumpIsRefused)
        ("image export resolution is consistent", imageExportResolutionIsConsistent)
        ("a valid image dependency DAG round-trips", imageValidDagRoundTrips)
    ]
