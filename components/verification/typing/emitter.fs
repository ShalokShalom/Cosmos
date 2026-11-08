module Emitter

// The emitter is the single renderer of the pipeline's artifacts (decision
// D9): given a parsed sentence and a *domain binding* supplied as data, it
// renders framework-free F# property predicates (D5) and the SMT obligation
// (D10). There is one renderer, not one per domain.

open Types

/// Looks up an active vocabulary token's Implementation name (falls back to the token).
let fsharpNameOf (vocabulary: VocabularyEntry list) (token: string) : string =
    match vocabulary |> List.tryFind (fun entry -> entry.Token = token && entry.IsActive) with
    | Some entry -> entry.ImplementationName
    | None -> token

/// The SMT side of a domain binding: the verification lattice (field sorts and
/// narrowing) and the verb's implementation body. The body is interpreted for
/// Hedgehog execution and encoded for Z3; there is no second implementation.
type SmtBinding =
    {
        Lattice: TypeLattice.Lattice
        VerbBody: DomainExpression.Expression
    }

/// How a token that is absent from an explicit map is resolved.
type NameFallback =
    | Identity
    | Prefix of prefix: string

/// How a binding maps a source token to a generated name. This is data: no
/// function-typed field is stored in the binding.
type NameMapping =
    | FromVocabulary
    | VocabularyWithPrefix of prefix: string
    | Fixed of name: string
    | Explicit of names: Map<string, string> * fallback: NameFallback

/// What the emitted artifact assumes about the world it compiles into (D9
/// sub-decision 1). Everything that differs between domains lives here, as
/// data: the module name, the subject type, the field-access mapping, the
/// verb's Implementation name, and — for the self-contained case only — the default `true`
/// domain function. Adding a domain means adding a binding value, not a new
/// renderer.
type DomainBinding =
    {
        /// The F# module the artifact declares. Flat and distinct per artifact:
        /// F# will not let one emitted module nest under another's name.
        ModuleName: string
        /// The vocabulary that resolves `FromVocabulary` mappings.
        Vocabulary: VocabularyEntry list
        /// The subject record type's Implementation name, from the parsed subject token.
        SubjectType: NameMapping
        /// The verb's Implementation name, from the parsed verb token.
        VerbName: NameMapping
        /// Names a condition's field in a generated record (self-contained only).
        FieldName: NameMapping
        /// Maps a condition or refinement-field token to the field-access
        /// expression on the subject (for example `player.Alive`).
        FieldAccess: NameMapping
        /// Declare the subject record from the sentence's conditions? False when
        /// the host domain module supplies the type.
        GeneratedRecord: bool
        /// Emit the default `true` domain function for the verb? False when the
        /// host domain module supplies the implementation under test.
        DomainDefault: bool
        /// The editable source of the artifact, recorded in the header. `None`
        /// for a self-contained artifact with no document behind it.
        SourceOfTruth: string option
        /// `open` lines below the module declaration (host modules).
        Opens: string list
        /// The SMT obligation's binding, when this domain emits one.
        Smt: SmtBinding option
    }

let private resolveName (binding: DomainBinding) (mapping: NameMapping) (token: string) : string =
    match mapping with
    | FromVocabulary -> fsharpNameOf binding.Vocabulary token
    | VocabularyWithPrefix prefix -> prefix + fsharpNameOf binding.Vocabulary token
    | Fixed name -> name
    | Explicit(names, fallback) ->
        match Map.tryFind token names with
        | Some name -> name
        | None ->
            match fallback with
            | Identity -> token
            | Prefix prefix -> prefix + token

let subjectTypeOf (binding: DomainBinding) (token: string) : string =
    resolveName binding binding.SubjectType token

let verbNameOf (binding: DomainBinding) (token: string) : string =
    resolveName binding binding.VerbName token

let fieldNameOf (binding: DomainBinding) (token: string) : string =
    resolveName binding binding.FieldName token

let fieldAccessOf (binding: DomainBinding) (token: string) : string =
    resolveName binding binding.FieldAccess token

/// The self-contained binding: the subject record is generated from the
/// vocabulary and the verb's domain function is the documented default `true`
/// (D5). Its output compiles and runs anywhere; its vacuity is the stated
/// placeholder of D5, not a defect. This is the binding behind the pinned
/// golden artifact `generated/sample-properties.fs`.
let selfContained (vocabulary: VocabularyEntry list) : DomainBinding =
    {
        ModuleName = "Scale.Generated.Sample"
        Vocabulary = vocabulary
        SubjectType = FromVocabulary
        VerbName = FromVocabulary
        FieldName = FromVocabulary
        FieldAccess = VocabularyWithPrefix "player."
        GeneratedRecord = true
        DomainDefault = true
        SourceOfTruth = None
        Opens = []
        Smt = None
    }

/// A float literal for a Real threshold, or for a numeric field with no
/// lattice. An integer threshold keeps its decimal point so F# types it as
/// `float`; Integer and Natural fields use `numericLiteral` instead.
let private floatLiteral (value: RealNumber) : string =
    let text = value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

    if text.Contains "." || text.Contains "E" || text.Contains "e" then
        text
    else
        text + ".0"

/// The F# carrier for a lattice sort. Real is `float`, Integer and Natural are
/// exact `int`, and Boolean is `bool`. A numeric field with no lattice keeps
/// the historical `float` carrier.
let private fsharpType (fieldType: DomainExpression.FieldType) : string =
    match fieldType with
    | DomainExpression.BooleanType -> "bool"
    | DomainExpression.IntegerType
    | DomainExpression.NaturalType -> "int"
    | DomainExpression.RealType -> "float"

/// The F# literal for a numeric threshold on its field's sort. Integer and
/// Natural render an exact Int32; Real renders a float; no lattice keeps the
/// historical float literal.
let private numericLiteral (fieldType: DomainExpression.FieldType option) (value: RealNumber) : string =
    match fieldType with
    | Some DomainExpression.IntegerType
    | Some DomainExpression.NaturalType ->
        match DomainExpression.integerValue value with
        | Ok number -> string number
        | Error reason -> invalidOp ("The numeric threshold cannot be emitted on an integer field: " + reason)
    | Some DomainExpression.RealType
    | None -> floatLiteral value
    | Some DomainExpression.BooleanType -> invalidOp "A numeric threshold cannot be emitted on a boolean field."

/// The sort of a field in the binding's lattice. A field the lattice does not
/// declare is refused: the F# artifact and the SMT obligation must name the
/// same fields with the same sorts.
let private fieldSortOf (binding: DomainBinding) : string -> DomainExpression.FieldType option =
    match binding.Smt with
    | None -> fun _ -> None
    | Some smt ->
        fun field ->
            match TypeLattice.tryFieldType field smt.Lattice with
            | Some fieldType -> Some fieldType
            | None -> invalidOp ("The domain binding's lattice has no field named '" + field + "'.")

/// Renders one sentence condition as an F# boolean expression over the
/// subject, through the binding's field access. `and` binds tighter than `or`;
/// the disjunction is parenthesized so the enclosing precondition's `&&`
/// cannot change its shape.
let rec private conditionExpression
    (binding: DomainBinding)
    (fieldSort: string -> DomainExpression.FieldType option)
    (condition: Condition)
    : string =
    match condition with
    | BooleanCondition field -> fieldAccessOf binding field
    | NumericCondition(field, comparison, value) ->
        fieldAccessOf binding field
        + " "
        + comparisonText comparison
        + " "
        + numericLiteral (fieldSort (fieldNameOf binding field)) value
    | Disjunction alternatives ->
        let clauses =
            alternatives
            |> List.map (fun clause ->
                "("
                + String.concat " && " (clause |> List.map (conditionExpression binding fieldSort))
                + ")")

        "(" + String.concat " || " clauses + ")"

/// A refinement's numeric threshold must be representable on its field's sort.
/// With a lattice the field's sort decides: Integer/Natural require an Int32
/// integer and Real accepts any finite double. Without a lattice the artifact
/// is plain F#, so any finite double is representable. It is never rounded: a
/// rounded threshold would compile into a predicate the document does not
/// describe.
let private validateGuardThresholds
    (lattice: TypeLattice.Lattice option)
    (refinements: ScaleFile.Refinement list)
    : unit =
    let fieldType (field: string) : DomainExpression.FieldType option =
        match lattice with
        | None -> None
        | Some lattice ->
            match TypeLattice.tryFieldType field lattice with
            | Some fieldType -> Some fieldType
            | None -> invalidOp ("The domain binding's lattice has no field named '" + field + "'.")

    let checkThreshold (guard: ScaleFile.RefinementGuard) (field: string) (value: RealNumber) : unit =
        let checked =
            match fieldType field with
            | Some DomainExpression.BooleanType ->
                Error("The numeric refinement names the boolean field '" + field + "'.")
            | Some sort -> TypeLattice.validateThreshold sort value
            | None -> DomainExpression.realValue value |> Result.map ignore

        match checked with
        | Ok _ -> ()
        | Error reason ->
            invalidOp (
                "The refinement '"
                + ScaleFile.refinementGuardText guard
                + "' cannot be emitted: "
                + reason
            )

    refinements
    |> List.iter (fun refinement ->
        match refinement.Guard with
        | ScaleFile.GreaterThan(field, value)
        | ScaleFile.AtLeast(field, value)
        | ScaleFile.LessThan(field, value)
        | ScaleFile.AtMost(field, value) -> checkThreshold refinement.Guard field value
        | ScaleFile.IsTrue field
        | ScaleFile.IsFalse field ->
            match fieldType field with
            | Some DomainExpression.BooleanType
            | None -> ()
            | Some _ ->
                invalidOp (
                    "The boolean refinement '"
                    + ScaleFile.refinementGuardText refinement.Guard
                    + "' names the non-boolean field '"
                    + field
                    + "'."
                )
        | ScaleFile.NamedGuard(field, name) ->
            invalidOp (
                "The refinement '"
                + field
                + " guard "
                + name
                + "' names an image-resident guard metaobject; the emitter has no F# or SMT rendering for a guard quotation (D41)."
            ))

/// Renders a refinement guard as an F# boolean expression over the subject,
/// through the binding's field access. The threshold is rendered in full
/// precision: a guard's value is the document's value, not an approximation.
let private guardExpression
    (binding: DomainBinding)
    (fieldSort: string -> DomainExpression.FieldType option)
    (guard: ScaleFile.RefinementGuard)
    : string =
    let access field = fieldAccessOf binding field

    let number field (value: RealNumber) = numericLiteral (fieldSort field) value

    match guard with
    | ScaleFile.GreaterThan(field, value) -> sprintf "%s > %s" (access field) (number field value)
    | ScaleFile.AtLeast(field, value) -> sprintf "%s >= %s" (access field) (number field value)
    | ScaleFile.LessThan(field, value) -> sprintf "%s < %s" (access field) (number field value)
    | ScaleFile.AtMost(field, value) -> sprintf "%s <= %s" (access field) (number field value)
    | ScaleFile.IsTrue field -> access field
    | ScaleFile.IsFalse field -> "not " + access field
    | ScaleFile.NamedGuard(field, name) ->
        invalidOp (
            "The refinement '"
            + field
            + " guard "
            + name
            + "' names an image-resident guard metaobject; the emitter has no F# rendering for a guard quotation (D41)."
        )

/// The lattice is a projection of the document (D25). If it narrows with a
/// guard the document does not carry, the F# artifact (which reads the
/// document) and the SMT artifact (which reads both) would state different
/// preconditions. Refuse instead of emitting two different specifications.
let private validateLatticeConsistency
    (lattice: TypeLattice.Lattice option)
    (refinements: ScaleFile.Refinement list)
    : unit =
    match lattice with
    | None -> ()
    | Some lattice ->
        let carried =
            refinements
            |> List.map (fun refinement -> ScaleFile.refinementGuardText refinement.Guard)
            |> Set.ofList

        let missing =
            TypeLattice.constraints lattice
            |> List.filter (fun guard -> not (Set.contains (ScaleFile.refinementGuardText guard) carried))

        if not (List.isEmpty missing) then
            invalidOp (
                "The domain binding's lattice narrows with guards the document does not carry: "
                + (missing |> List.map ScaleFile.refinementGuardText |> String.concat ", ")
            )

/// Renders the F# property module for a parsed sentence under a domain binding.
/// The output is framework-free (D5): an implication and a `: bool` predicate.
/// Any refinements the document carries are baked into the precondition — the
/// grew-specification is visible in the artifact's source text, not an input.
let emitFSharp
    (binding: DomainBinding)
    (parsed: ParsedSentence)
    (specification: string)
    (refinements: ScaleFile.Refinement list)
    : string =
    validateLatticeConsistency (binding.Smt |> Option.map (fun smt -> smt.Lattice)) refinements
    validateGuardThresholds (binding.Smt |> Option.map (fun smt -> smt.Lattice)) refinements

    let subjectType = subjectTypeOf binding parsed.Subject
    let verbName = verbNameOf binding parsed.Verb
    let fieldSort = fieldSortOf binding

    // The subject type carries every field the artifact names: the sentence's
    // conditions and the refinements' guards, deduplicated by field. A
    // duplicated condition, or a refinement that names a field the sentence
    // does not, would otherwise produce a record F# cannot compile.
    let recordFields =
        let conditionFields =
            parsed.Conditions
            |> List.collect conditionAtoms
            |> List.map (fun condition ->
                let fieldType =
                    match condition with
                    | BooleanCondition field ->
                        match fieldSort (fieldNameOf binding field) with
                        | Some DomainExpression.BooleanType
                        | None -> "bool"
                        | Some _ ->
                            invalidOp (
                                "The boolean condition names the non-boolean field '"
                                + fieldNameOf binding field
                                + "'."
                            )
                    | NumericCondition(field, _, _) ->
                        match fieldSort (fieldNameOf binding field) with
                        | Some DomainExpression.BooleanType ->
                            invalidOp (
                                "The numeric condition names the boolean field '"
                                + fieldNameOf binding field
                                + "'."
                            )
                        | Some fieldType -> fsharpType fieldType
                        | None -> "float"
                    | Disjunction _ -> invalidOp "conditionAtoms must return atomic conditions."

                conditionField condition, fieldType)

        let refinementFields =
            refinements
            |> List.map (fun refinement ->
                let fieldType =
                    match refinement.Guard with
                    | ScaleFile.IsTrue field
                    | ScaleFile.IsFalse field ->
                        match fieldSort field with
                        | Some DomainExpression.BooleanType
                        | None -> "bool"
                        | Some _ ->
                            invalidOp (
                                "The boolean refinement names the non-boolean field '"
                                + field
                                + "'."
                            )
                    | ScaleFile.GreaterThan(field, _)
                    | ScaleFile.AtLeast(field, _)
                    | ScaleFile.LessThan(field, _)
                    | ScaleFile.AtMost(field, _) ->
                        match fieldSort field with
                        | Some DomainExpression.BooleanType ->
                            invalidOp ("The numeric refinement names the boolean field '" + field + "'.")
                        | Some fieldType -> fsharpType fieldType
                        | None -> "float"
                    | ScaleFile.NamedGuard(field, name) ->
                        invalidOp (
                            "The refinement '"
                            + field
                            + " guard "
                            + name
                            + "' names an image-resident guard metaobject; the emitter cannot declare its field type (D41)."
                        )

                TypeLattice.refinementGuardField refinement.Guard, fieldType)

        let allFields = conditionFields @ refinementFields

        // One field has one sort. A sentence that names the same field as both
        // a boolean and a numeric condition is ill-sorted, and emitting it
        // would produce a record F# cannot compile: refuse instead.
        let conflicts =
            allFields
            |> List.groupBy fst
            |> List.filter (fun (_, entries) -> entries |> List.map snd |> List.distinct |> List.length > 1)

        if not (List.isEmpty conflicts) then
            let names = conflicts |> List.map fst |> String.concat ", "

            invalidOp ("The subject declares one field with two sorts: " + names)

        allFields
        |> List.distinctBy fst
        |> List.map (fun (field, fieldType) -> fieldNameOf binding field + ": " + fieldType)

    let lines = System.Collections.Generic.List<string>()

    // header
    lines.Add("// Generated by Scale — do not edit by hand.")
    lines.Add("// Pattern: " + string parsed.Pattern)

    lines.Add(
        "// Subject: "
        + parsed.Subject
        + ", Conditions: "
        + String.concat ", " (parsed.Conditions |> List.map conditionText)
        + ", Verb: "
        + parsed.Verb
    )
    // The pattern reaches the emitted code: an Invariant asserts that the verb
    // never holds, so its postcondition is negated. Kept out of the Capability
    // path so the pinned golden output is unchanged.
    if parsed.Pattern = Invariant then
        lines.Add("// An Invariant asserts the verb never holds: the postcondition is negated.")

    if parsed.Pattern = Transition then
        lines.Add("// A Transition is event-triggered: when the trigger holds, the effect must hold.")

    match binding.SourceOfTruth with
    | Some source ->
        lines.Add("// Source of truth: " + source)
        lines.Add("// specification: " + specification)
    | None -> ()

    lines.Add("module " + binding.ModuleName)
    lines.Add("")

    // host modules the artifact binds to
    for line in binding.Opens do
        lines.Add(line)

    if not (List.isEmpty binding.Opens) then
        lines.Add("")

    // implication
    lines.Add("/// Standard implication for property shapes. A property-test harness")
    lines.Add("/// (Hedgehog/FsCheck) generates inputs and runs the predicates below.")
    lines.Add("let (==>) precondition postcondition = if precondition then postcondition else true")
    lines.Add("")

    // generated subject record (self-contained only)
    if binding.GeneratedRecord then
        if List.isEmpty recordFields then
            // A sentence with no conditions still needs a subject type, and a
            // record with no fields is not valid F#: the subject is a
            // one-case union used only as a marker.
            lines.Add("/// Subject type for the sentence, generated from the vocabulary.")
            lines.Add("type " + subjectType + " = " + subjectType)
        else
            lines.Add("/// Record type for the sentence's subject, generated from the vocabulary.")
            lines.Add("type " + subjectType + " =")
            lines.Add("    { " + String.concat "; " recordFields + " }")

        lines.Add("")

    // domain default (self-contained only)
    if binding.DomainDefault then
        lines.Add("/// Domain default — pending a host domain module.")
        lines.Add("let " + verbName + " (_: " + subjectType + ") : bool = true")
        lines.Add("")

    // property
    let bindingPrefix =
        match parsed.Pattern with
        | Possibility -> "some "
        | Transition -> "when "
        | _ -> ""

    let propertyComment =
        match parsed.Pattern with
        | Possibility ->
            "/// Witness predicate over "
            + subjectType
            + ": a harness searches for a subject satisfying the conditions and the verb."
        | Transition ->
            "/// Transition predicate over "
            + subjectType
            + ": when the trigger holds, the effect must hold."
        | _ -> "/// Property predicate over " + subjectType + "."

    lines.Add(propertyComment)

    lines.Add(
        "let ``"
        + bindingPrefix
        + parsed.Subject
        + " "
        + verbName
        + "`` (player: "
        + subjectType
        + ") : bool ="
    )

    let preconditions =
        (parsed.Conditions |> List.map (conditionExpression binding fieldSort))
        @ (refinements
           |> List.map (fun refinement -> guardExpression binding fieldSort refinement.Guard))
    // `==>` binds tighter than `&&`, so a multi-clause precondition must be
    // parenthesized. A single clause is left bare so the pinned golden output
    // is unchanged.
    let lhs =
        match preconditions with
        | [] -> "true"
        | [ single ] -> single
        | multiple -> "(" + String.concat " && " multiple + ")"

    let postcondition =
        match parsed.Pattern with
        | Invariant -> "not (" + verbName + " player)"
        | _ -> verbName + " player"

    let body =
        match parsed.Pattern with
        | Possibility ->
            // A witness predicate: the conditions and the verb hold together.
            let conjuncts =
                (if List.isEmpty preconditions then [] else [ lhs ]) @ [ postcondition ]

            String.concat " && " conjuncts
        | _ -> lhs + " ==> " + postcondition

    lines.Add("    " + body)

    String.concat "\n" lines

/// Renders the SMT-LIB obligation for a document under a domain binding. The
/// formula is `pre ∧ ¬post` over the implementation body, so a `Proved` answer
/// is about the code under test (D10). A binding with no SMT side raises.
let emitSmt
    (binding: DomainBinding)
    (parsed: ParsedSentence)
    (specification: string)
    (refinements: ScaleFile.Refinement list)
    : string =
    match binding.Smt with
    | None -> invalidOp "This domain binding does not emit an SMT obligation."
    | Some smt ->
        validateLatticeConsistency (Some smt.Lattice) refinements
        validateGuardThresholds (Some smt.Lattice) refinements

        use encoding =
            match Smt.createFromLattice smt.Lattice smt.VerbBody with
            | Ok encoding -> encoding
            | Error reason -> invalidOp ("The domain binding's body is ill-sorted: " + reason)

        let obligation =
            {
                Smt.Pattern = parsed.Pattern
                Smt.Conditions = parsed.Conditions |> List.map (mapConditionField (fieldNameOf binding))
                Smt.Refinements = refinements
            }

        match Smt.obligationText encoding obligation with
        | Error message -> invalidOp ("The SMT obligation could not be encoded: " + message)
        | Ok text ->
            let lines = System.Collections.Generic.List<string>()
            lines.Add("; Generated by Scale — do not edit by hand.")
            lines.Add("; Pattern: " + string parsed.Pattern)

            lines.Add(
                "; Subject: "
                + parsed.Subject
                + ", Conditions: "
                + String.concat ", " (parsed.Conditions |> List.map conditionText)
                + ", Verb: "
                + parsed.Verb
            )

            match binding.SourceOfTruth with
            | Some source ->
                lines.Add("; Source of truth: " + source)
                lines.Add("; specification: " + specification)
            | None -> ()

            lines.Add("")
            lines.Add(text)
            String.concat "\n" lines

/// The self-contained binding under a caller-chosen flat module name, for a
/// second pinned artifact (for example the numeric-condition sample).
let selfContainedNamed (moduleName: string) (vocabulary: VocabularyEntry list) : DomainBinding =
    { selfContained vocabulary with
        ModuleName = moduleName
    }

/// The self-contained rendering: the pinned golden sample, with no refinements
/// and no document behind it. Kept as the single entry point the W1 tests and
/// the emitter-purity property use.
let emit (vocabulary: VocabularyEntry list) (parsed: ParsedSentence) : string =
    emitFSharp (selfContained vocabulary) parsed "" []
