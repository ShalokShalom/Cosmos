module FixedSpecificationAcceptance

// Fixed-spec mode (D11): the specification is held fixed, a witness becomes a
// repair bundle, and a patch is accepted only by the four-rule gate. Everything
// here is a pure function over the existing SMT/Refiner results; the gate never
// writes the specification.

open Types
open System.Text.Json

/// The loop's mode is a setting, not an inference.
type Mode =
    | Exploratory
    | FixedSpecification

/// Where the implementation under test lives.
type SourceLocation = { File: string; Line: int }

/// A concrete input and its machine-readable field values.
type Witness<'T> =
    {
        Input: 'T
        Values: (string * ScaleFile.FieldValue) list
    }

/// A condition is a value Repair can carry: its protocol name and rendered
/// message are the same shape as a failure witness.
type ConditionWitness = { Name: string; Message: string }

let conditionWitness (condition: EvaluatorCellSystem.ConditionDefinition) (store: Store) : ConditionWitness =
    {
        Name = Store.readText condition.Name store |> Option.defaultValue ""
        Message = Store.readText condition.Message store |> Option.defaultValue ""
    }

/// The property under repair, with the current (fixed) specification's guards.
type Target =
    {
        Property: string
        Sentence: ParsedSentence
        Conditions: Condition list
        Refinements: ScaleFile.Refinement list
        SpecificationHash: string
        Source: SourceLocation
    }

/// A content identity for one specification. The constructor is private so a
/// proposal cannot name an arbitrary string as its frozen specification.
type SpecificationIdentity = private SpecificationIdentity of string

let private identityOf (hash: string) : SpecificationIdentity = SpecificationIdentity hash
let specificationHash (SpecificationIdentity hash) : string = hash

/// The part of the world a fixed-spec acceptance is allowed to see. A proposal
/// is checked against this identity, and the gate never edits the document.
type FrozenSpecification =
    {
        Target: Target
        Identity: SpecificationIdentity
    }

let freeze (target: Target) : FrozenSpecification =
    {
        Target = target
        Identity = identityOf target.SpecificationHash
    }

/// A proposal over the implementation language. It has no specification field:
/// fixed-spec mode cannot represent a specification edit.
type ImplementationProposal =
    {
        Body: DomainExpression.Expression
        Against: SpecificationIdentity
    }

let propose (frozen: FrozenSpecification) (body: DomainExpression.Expression) : ImplementationProposal =
    {
        Body = body
        Against = frozen.Identity
    }

/// A proposed specification change. It is constructible only with an
/// exploratory capability; fixed-spec mode never has one.
type SpecificationProposal = private SpecificationProposal of ScaleFile.Refinement * SpecificationIdentity

/// The capability that permits a specification proposal. It is produced only by
/// an exploratory mode, and its constructor is private.
type SpecChangeCapability = private SpecChangeCapability of SpecificationIdentity

let specChangeCapability (mode: Mode) (document: ScaleFile.ScaleDocument) : Result<SpecChangeCapability, string> =
    match mode with
    | Exploratory -> Ok(SpecChangeCapability(identityOf (ScaleFile.hash document)))
    | FixedSpecification -> Error "Fixed-spec mode never edits the specification."

let proposeSpecification (capability: SpecChangeCapability) (refinement: ScaleFile.Refinement) : SpecificationProposal =
    let (SpecChangeCapability identity) = capability
    SpecificationProposal(refinement, identity)

let appendSpecification
    (proposal: SpecificationProposal)
    (document: ScaleFile.ScaleDocument)
    : Result<ScaleFile.ScaleDocument, string> =
    let (SpecificationProposal(refinement, identity)) = proposal

    if ScaleFile.hash document <> specificationHash identity then
        Error "The specification hash changed; the proposal was built against a different specification."
    else
        Ok(ScaleFile.append refinement document)

/// The contract between the loop and a generator, versioned from the first
/// commit. It is data, not prose.
type AcceptanceBundle<'T> =
    {
        Version: int
        Property: string
        Sentence: ParsedSentence
        SpecificationHash: string
        Witness: Witness<'T>
        Diagnostic: Refiner.Diagnostic
        Z3Model: Smt.SolverAssignment option
        Implementation: DomainExpression.Expression
        EncodedImplementation: string
        Differential: string
        Source: SourceLocation
    }

type Verdict =
    | Accepted
    | Rejected of reason: string

/// A previously observed property outcome. `Expected` is pinned by the
/// append-only corpus, never re-derived from the patch.
type CorpusEntry<'T> =
    {
        Witness: Witness<'T>
        Pattern: SentencePattern
        Conditions: Condition list
        Refinements: ScaleFile.Refinement list
        Expected: bool
    }

let private conditionHolds
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (conditions: Condition list)
    (subject: 'T)
    : bool =
    let rec holds (condition: Condition) : bool =
        match condition with
        | BooleanCondition field ->
            match getField subject field with
            | ScaleFile.BooleanValue value -> value
            | ScaleFile.NumericValue _ -> false
        | NumericCondition(field, comparison, threshold) ->
            match getField subject field with
            | ScaleFile.BooleanValue _ -> false
            | ScaleFile.NumericValue value ->
                match comparison with
                | ComparisonGreaterThan -> value > threshold
                | ComparisonAtLeast -> value >= threshold
                | ComparisonLessThan -> value < threshold
                | ComparisonAtMost -> value <= threshold
                | ComparisonEqualTo -> value = threshold
        | Disjunction alternatives -> alternatives |> List.exists (fun clause -> clause |> List.forall holds)

    conditions |> List.forall holds

/// The property's truth at one input, using the interpreter that the compiled
/// predicate runs. `Invariant` negates the verb, matching `Smt.postcondition`.
/// The gate checks `sortOf` before it calls this, so the body is well-sorted
/// here; the `defaultValue` is unreachable, not a swallowed error.
let private propertyHolds
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (pattern: SentencePattern)
    (conditions: Condition list)
    (refinements: ScaleFile.Refinement list)
    (body: DomainExpression.Expression)
    (subject: 'T)
    : bool =
    let pre =
        conditionHolds getField conditions subject
        && (refinements
            |> List.forall (fun refinement -> Refiner.evaluateRefinementGuard getField refinement.Guard subject))

    let verdict =
        let holds =
            DomainExpression.interpretBoolean getField subject body
            |> Result.defaultValue false

        match pattern with
        | Invariant -> not pre || not holds
        | Possibility -> pre && holds
        | Capability
        | Transition -> not pre || holds

    verdict

/// Type-checks a patch body against the encoded domain before any solver runs.
/// A generator can return a value of the wrong sort; that is a rejected patch,
/// not an exception from the encoding or the interpreter.
let private sortOf
    (fields: (string * DomainExpression.FieldType) list)
    (expr: DomainExpression.Expression)
    : Result<DomainExpression.FieldType, string> =
    let fieldSort name =
        match fields |> List.tryFind (fun (field, _) -> field = name) with
        | Some(_, sort) -> Ok sort
        | None -> Error("The domain has no field named '" + name + "'.")

    let rec go expr =
        match expr with
        | DomainExpression.BooleanField name ->
            fieldSort name
            |> Result.bind (fun sort ->
                match sort with
                | DomainExpression.BooleanType -> Ok DomainExpression.BooleanType
                | _ -> Error("The boolean field names a numeric field: " + name))
        | DomainExpression.IntegerField name ->
            fieldSort name
            |> Result.bind (fun sort ->
                if DomainExpression.isNumeric sort then
                    Ok sort
                else
                    Error("The numeric field names a boolean field: " + name))
        | DomainExpression.BooleanConstant _ -> Ok DomainExpression.BooleanType
        | DomainExpression.IntegerConstant _ -> Ok DomainExpression.IntegerType
        | DomainExpression.Compare(_, left, right) ->
            match go left, go right with
            | Ok leftSort, Ok rightSort
                when DomainExpression.isNumeric leftSort && DomainExpression.isNumeric rightSort ->
                Ok DomainExpression.BooleanType
            | Error reason, _ -> Error reason
            | _, Error reason -> Error reason
            | _ -> Error "A comparison needs two numeric operands."
        | DomainExpression.And(left, right)
        | DomainExpression.Or(left, right) ->
            match go left, go right with
            | Ok DomainExpression.BooleanType, Ok DomainExpression.BooleanType -> Ok DomainExpression.BooleanType
            | Error reason, _ -> Error reason
            | _, Error reason -> Error reason
            | _ -> Error "A boolean connective needs two boolean operands."
        | DomainExpression.Not inner ->
            go inner
            |> Result.bind (fun sort ->
                match sort with
                | DomainExpression.BooleanType -> Ok DomainExpression.BooleanType
                | _ -> Error "`Not` needs a boolean operand.")

    go expr

/// The differential result recorded in a bundle. It is informational: the gate
/// recomputes it from the patch and never trusts this string.
let differentialResult
    (fields: (string * DomainExpression.FieldType) list)
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (body: DomainExpression.Expression)
    (witnesses: Witness<'T> list)
    : string =
    match Smt.create fields body with
    | Error reason -> "0/0 witnesses agree (the body is ill-sorted: " + reason + ")"
    | Ok encoding ->
        use encoding = encoding

        let checks =
            witnesses
            |> List.map (fun witness ->
                match Smt.evaluateBody encoding witness.Values with
                | Ok encoded ->
                    encoded =
                        (DomainExpression.interpretBoolean getField witness.Input body
                         |> Result.defaultValue false)
                | Error _ -> false)

        let agreed = checks |> List.filter id |> List.length
        $"{agreed}/{checks.Length} witnesses agree"

let makeBundle
    (version: int)
    (property: string)
    (sentence: ParsedSentence)
    (document: ScaleFile.ScaleDocument)
    (witness: Witness<'T>)
    (diagnostic: Refiner.Diagnostic)
    (z3Model: Smt.SolverAssignment option)
    (body: DomainExpression.Expression)
    (encodedImplementation: string)
    (differential: string)
    (source: SourceLocation)
    : AcceptanceBundle<'T> =
    {
        Version = version
        Property = property
        Sentence = sentence
        SpecificationHash = ScaleFile.hash document
        Witness = witness
        Diagnostic = diagnostic
        Z3Model = z3Model
        Implementation = body
        EncodedImplementation = encodedImplementation
        Differential = differential
        Source = source
    }

/// The four-rule gate. The specification identity is checked before solver
/// work; the target proof is the primary acceptance; the corpus catches
/// regressions the target cannot see; the differential check refuses a proposal
/// the encoding cannot faithfully see. A specification edit is not part of the
/// proposal type.
let accept
    (fields: (string * DomainExpression.FieldType) list)
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (frozen: FrozenSpecification)
    (corpus: CorpusEntry<'T> list)
    (proposal: ImplementationProposal)
    : Verdict =
    let target = frozen.Target

    if proposal.Against <> frozen.Identity then
        Rejected "The proposal was built against a different specification."
    else
        match sortOf fields proposal.Body with
        | Error reason -> Rejected("The proposal body is not well-sorted: " + reason)
        | Ok DomainExpression.IntegerType
        | Ok DomainExpression.NaturalType
        | Ok DomainExpression.RealType -> Rejected "The proposal body must be a boolean expression."
        | Ok DomainExpression.BooleanType ->
            match Smt.create fields proposal.Body with
            | Error reason -> Rejected("The proposal body cannot be encoded: " + reason)
            | Ok encoding ->
                use encoding = encoding

                let obligation: Smt.Obligation =
                    {
                        Pattern = target.Sentence.Pattern
                        Conditions = target.Conditions
                        Refinements = target.Refinements
                    }

                match Smt.discharge encoding obligation with
                | Smt.Proved ->
                    let regressions =
                        corpus
                        |> List.filter (fun entry -> entry.Expected)
                        |> List.filter (fun entry ->
                            not (
                                propertyHolds
                                    getField
                                    entry.Pattern
                                    entry.Conditions
                                    entry.Refinements
                                    proposal.Body
                                    entry.Witness.Input
                            ))

                    if not (List.isEmpty regressions) then
                        Rejected($"{regressions.Length} previously passing witness(es) regressed.")
                    else
                        let disagreements =
                            corpus
                            |> List.filter (fun entry ->
                                match Smt.evaluateBody encoding entry.Witness.Values with
                                | Ok encoded ->
                                    encoded
                                    <> (DomainExpression.interpretBoolean getField entry.Witness.Input proposal.Body
                                        |> Result.defaultValue false)
                                | Error _ -> true)

                        if not (List.isEmpty disagreements) then
                            Rejected(
                                $"The encoding disagrees with the compiled predicate on {disagreements.Length} witness(es)."
                            )
                        else
                            Accepted
                | Smt.Falsified model -> Rejected("The target property is still falsified: " + Smt.describeModel model)
                | Smt.Witnessed _ ->
                    Rejected "Fixed-spec acceptance is for universal targets; a possibility has no acceptance rule."
                | Smt.NoWitness -> Rejected "The possibility target has no witness in the encoded theory."
                | Smt.Unknown reason -> Rejected("The target property is undecided (Unknown): " + reason)
                | Smt.Unavailable reason -> Rejected("The target property cannot be encoded (Unavailable): " + reason)

// ---------------------------------------------------------------------------
// Persistence: the bundle and the append-only witness corpus are JSON.
//
// The names below are the wire contract, not domain types: they are how the
// format is spelled on disk. `witnesses/player.json` is checked in, and the
// tests parse the bundle back by these exact names. The writer is explicit
// because System.Text.Json's reflection and source-generation paths are not
// NativeAOT-compatible for F# records; `Utf8JsonWriter` is.
// ---------------------------------------------------------------------------

let private patternText (pattern: SentencePattern) : string =
    match pattern with
    | Capability -> "Capability"
    | Invariant -> "Invariant"
    | Transition -> "Transition"
    | Possibility -> "Possibility"

let private fieldValueText (value: ScaleFile.FieldValue) : string =
    match value with
    | ScaleFile.BooleanValue flag -> if flag then "true" else "false"
    | ScaleFile.NumericValue number -> number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

let private modelValueText (value: Smt.Value) : string =
    match value with
    | Smt.BooleanValue flag -> if flag then "true" else "false"
    | Smt.IntegerValue number -> string number
    | Smt.RealValue number -> number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

let private writeJson (write: Utf8JsonWriter -> unit) : string =
    use stream = new System.IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
     write writer
     writer.Flush())

    System.Text.Encoding.UTF8.GetString(stream.ToArray())

let private writeTextArray (writer: Utf8JsonWriter) (name: string) (values: string list) : unit =
    writer.WriteStartArray name

    for value in values do
        writer.WriteStringValue value

    writer.WriteEndArray()

let private writeValueArray (writer: Utf8JsonWriter) (name: string) (values: (string * string) list) : unit =
    writer.WriteStartArray name

    for (valueName, value) in values do
        writer.WriteStartObject()
        writer.WriteString("name", valueName)
        writer.WriteString("value", value)
        writer.WriteEndObject()

    writer.WriteEndArray()

let bundleJson (bundle: AcceptanceBundle<'T>) : string =
    let witnessValues =
        bundle.Witness.Values
        |> List.map (fun (name, value) -> name, fieldValueText value)

    let z3Values =
        match bundle.Z3Model with
        | Some model ->
            model
            |> Map.toList
            |> List.map (fun (name, value) -> name, modelValueText value)
        | None -> []

    writeJson (fun writer ->
        writer.WriteStartObject()
        writer.WriteNumber("version", bundle.Version)
        writer.WriteString("property", bundle.Property)
        writer.WriteStartObject("sentence")
        writer.WriteString("pattern", patternText bundle.Sentence.Pattern)
        writer.WriteString("subject", bundle.Sentence.Subject)
        writeTextArray writer "conditions" (bundle.Sentence.Conditions |> List.map conditionText)
        writer.WriteString("verb", bundle.Sentence.Verb)
        writer.WriteEndObject()
        writer.WriteString("specHash", bundle.SpecificationHash)
        writer.WriteStartObject("hedgehog")
        writer.WriteNumber("tests", bundle.Diagnostic.Tests)
        writer.WriteNumber("shrinks", bundle.Diagnostic.Shrinks)
        writeTextArray writer "values" bundle.Diagnostic.Values
        writer.WriteString("seed", bundle.Diagnostic.Seed)
        writer.WriteEndObject()
        writeValueArray writer "witness" witnessValues
        writeValueArray writer "z3Model" z3Values
        writer.WriteString("implementation", DomainExpression.render bundle.Implementation)
        writer.WriteString("encodedImplementation", bundle.EncodedImplementation)
        writer.WriteString("differential", bundle.Differential)
        writer.WriteStartObject("source")
        writer.WriteString("file", bundle.Source.File)
        writer.WriteNumber("line", bundle.Source.Line)
        writer.WriteEndObject()
        writer.WriteEndObject())

let writeBundle (path: string) (bundle: AcceptanceBundle<'T>) : unit =
    System.IO.File.WriteAllText(path, bundleJson bundle)

let corpusJson (entries: CorpusEntry<'T> list) : string =
    writeJson (fun writer ->
        writer.WriteStartObject()
        writer.WriteNumber("version", 1)
        writer.WriteStartArray("entries")

        for entry in entries do
            writer.WriteStartObject()
            writer.WriteString("pattern", patternText entry.Pattern)
            writeTextArray writer "conditions" (entry.Conditions |> List.map conditionText)
            writer.WriteBoolean("expected", entry.Expected)

            entry.Witness.Values
            |> List.map (fun (name, value) -> name, fieldValueText value)
            |> writeValueArray writer "values"

            writer.WriteStartArray("refinements")

            for refinement in entry.Refinements do
                writer.WriteStartObject()
                writer.WriteString("guard", ScaleFile.refinementGuardText refinement.Guard)
                writer.WriteString("origin", refinement.Origin)
                writer.WriteString("counterexample", refinement.Counterexample)
                writer.WriteEndObject()

            writer.WriteEndArray()
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject())

let writeCorpus (path: string) (entries: CorpusEntry<'T> list) : unit =
    System.IO.File.WriteAllText(path, corpusJson entries)

let private parseFieldValue (element: JsonElement) : Result<ScaleFile.FieldValue, string> =
    match element.ValueKind with
    | JsonValueKind.True -> Ok(ScaleFile.BooleanValue true)
    | JsonValueKind.False -> Ok(ScaleFile.BooleanValue false)
    | JsonValueKind.Number ->
        try
            Ok(ScaleFile.NumericValue(element.GetDouble()))
        with ex ->
            Error ex.Message
    | JsonValueKind.String ->
        let text = element.GetString()

        match System.Boolean.TryParse text with
        | true, flag -> Ok(ScaleFile.BooleanValue flag)
        | false, _ ->
            match System.Double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture) with
            | true, number -> Ok(ScaleFile.NumericValue number)
            | false, _ -> Error("A field value is neither a boolean nor a number: " + text)
    | _ -> Error "A field value must be a boolean or a number."

let private parsePattern (text: string) : Result<SentencePattern, string> =
    match text with
    | "Capability" -> Ok Capability
    | "Invariant" -> Ok Invariant
    | "Transition" -> Ok Transition
    | "Possibility" -> Error "A possibility entry is not a universal corpus entry."
    | other -> Error("Unknown sentence pattern: " + other)

/// Collapses a list of results into a result of list, first reason wins.
let private toResult (results: Result<'T, string> list) : Result<'T list, string> =
    let rec go acc remaining =
        match remaining with
        | [] -> Ok(List.rev acc)
        | Ok value :: rest -> go (value :: acc) rest
        | Error reason :: _ -> Error reason

    go [] results

/// Accepts both corpus encodings the format has used: a field-name object and
/// the bundle's `[{ name, value }]` array. The checked-in corpus uses the
/// object form because it is easier to edit; the writer emits the array form.
let private parseValues (element: JsonElement) : Result<Map<string, ScaleFile.FieldValue>, string> =
    let collect (pairs: (string * Result<ScaleFile.FieldValue, string>) seq) =
        pairs
        |> Seq.toList
        |> List.fold
            (fun acc (name, result) ->
                match acc, result with
                | Error reason, _ -> Error reason
                | Ok _, Error reason -> Error(name + ": " + reason)
                | Ok map, Ok value -> Ok(Map.add name value map))
            (Ok Map.empty)

    match element.ValueKind with
    | JsonValueKind.Object ->
        collect (
            element.EnumerateObject()
            |> Seq.map (fun property -> property.Name, parseFieldValue property.Value)
        )
    | JsonValueKind.Array ->
        collect (
            element.EnumerateArray()
            |> Seq.map (fun item -> item.GetProperty("name").GetString(), parseFieldValue (item.GetProperty("value")))
        )
    | _ -> Error "Field values must be an object or an array of { name, value } records."

/// Reads a checked-in, append-only corpus. `decode` turns the field map back
/// into the domain's record; it is the only domain-specific step.
let readCorpus (decode: Map<string, ScaleFile.FieldValue> -> 'T) (path: string) : Result<CorpusEntry<'T> list, string> =
    try
        use document = JsonDocument.Parse(System.IO.File.ReadAllText path)
        let root = document.RootElement
        let version = root.GetProperty("version").GetInt32()

        if version <> 1 then
            Error($"Unsupported witness corpus version: {version}")
        else
            let mutable failure: string option = None

            let parseEntry (element: JsonElement) : CorpusEntry<'T> option =
                if failure.IsSome then
                    None
                else
                    try
                        let pattern = parsePattern (element.GetProperty("pattern").GetString())

                        let conditions =
                            element.GetProperty("conditions").EnumerateArray()
                            |> Seq.map (fun item -> parseConditionText (item.GetString()))
                            |> Seq.toList

                        let expected = element.GetProperty("expected").GetBoolean()

                        let refinements =
                            element.GetProperty("refinements").EnumerateArray()
                            |> Seq.map (fun item ->
                                let refinementGuardText = item.GetProperty("guard").GetString()

                                match ScaleFile.parseRefinementGuard refinementGuardText with
                                | Some guard ->
                                    Ok(
                                        {
                                            Guard = guard
                                            Origin = item.GetProperty("origin").GetString()
                                            Counterexample = item.GetProperty("counterexample").GetString()
                                        }
                                        : ScaleFile.Refinement
                                    )
                                | None -> Error("Cannot parse the corpus guard: " + refinementGuardText))
                            |> Seq.toList

                        match
                            pattern,
                            parseValues (element.GetProperty("values")),
                            toResult refinements,
                            toResult conditions
                        with
                        | Error reason, _, _, _ ->
                            failure <- Some reason
                            None
                        | _, Error reason, _, _ ->
                            failure <- Some reason
                            None
                        | _, _, Error reason, _ ->
                            failure <- Some reason
                            None
                        | _, _, _, Error reason ->
                            failure <- Some reason
                            None
                        | Ok parsedPattern, Ok values, Ok refinements, Ok parsedConditions ->
                            Some
                                {
                                    Witness =
                                        {
                                            Input = decode values
                                            Values = Map.toList values
                                        }
                                    Pattern = parsedPattern
                                    Conditions = parsedConditions
                                    Refinements = refinements
                                    Expected = expected
                                }
                    with ex ->
                        failure <- Some ex.Message
                        None

            let entries =
                root.GetProperty("entries").EnumerateArray()
                |> Seq.choose parseEntry
                |> Seq.toList

            match failure with
            | Some reason -> Error reason
            | None -> Ok entries
    with ex ->
        Error ex.Message
