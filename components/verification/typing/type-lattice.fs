module TypeLattice

// The verification-domain type lattice (D25).
//
// A field is a name, a fieldType, an optional finite set of coordinates, and the
// guards that narrow it. A lattice is an ordered list of fields. It is a
// projection of the domain binding (D9), not a second source of truth: the
// `.scale` document remains the one editable specification (D7).
//
// Three readers share it:
//   * Hedgehog reads `coordinatesAfterNarrowing` through the generators.
//   * Z3 reads `fieldTypes` and `constraints` through the SMT encoding.
//   * The refiner reads `candidateGuards` and writes the chosen guard with
//     `applyGuard`, returning the narrowed lattice.
//
// A field with `Coordinates = None` is symbolic: Z3 can reason about it, but a
// finite generator and the exhaustive refiner cannot enumerate it. This is not
// the same as an empty domain, which is refused by `create` and `applyGuard`.

open Types
open Hedgehog.FSharp

type FieldType = DomainExpression.FieldType

/// Checks that a numeric guard's threshold is representable on its field's
/// sort. The emitter and the lattice share this predicate so a threshold the
/// encoded theory cannot state is refused in the same words everywhere.
let validateThreshold (fieldType: FieldType) (threshold: RealNumber) : Result<unit, string> =
    match fieldType with
    | DomainExpression.BooleanType -> Error("The boolean sort has no numeric threshold.")
    | DomainExpression.IntegerType -> DomainExpression.integerValue threshold |> Result.map ignore
    | DomainExpression.NaturalType -> DomainExpression.naturalValue threshold |> Result.map ignore
    | DomainExpression.RealType -> DomainExpression.realValue threshold |> Result.map ignore

/// The three answers a guard can give. `GuardUndecided` carries the reason, so
/// an undecidable guard is never a silent false (D31, D35, D41).
type GuardVerdict =
    | GuardHolds
    | GuardDoesNotHold
    | GuardUndecided of reason: string

/// Resolves a named image-resident guard metaobject against one field value.
/// The evaluator supplies one (D41); the verification pipeline has no image,
/// so a named guard is a named refusal there.
type GuardResolver = string -> ScaleFile.FieldValue -> GuardVerdict

/// The resolver for callers without an image. A named guard stays undecided.
let noGuardResolver: GuardResolver =
    fun name _ ->
        GuardUndecided(
            "the guard metaobject '"
            + name
            + "' needs a runtime guard resolver, and no image is attached"
        )

type Field =
    {
        Name: string
        Type: FieldType
        Coordinates: ScaleFile.FieldValue list option
        Narrowing: ScaleFile.RefinementGuard list
    }

type Lattice = { Fields: Field list }

let private validateCoordinate
    (fieldName: string)
    (fieldType: FieldType)
    (value: ScaleFile.FieldValue)
    : Result<unit, string> =
    match fieldType, value with
    | DomainExpression.BooleanType, ScaleFile.BooleanValue _ -> Ok()
    | DomainExpression.BooleanType, ScaleFile.NumericValue _ ->
        Error("The boolean field '" + fieldName + "' has a numeric coordinate.")
    | (DomainExpression.IntegerType
      | DomainExpression.NaturalType
      | DomainExpression.RealType), ScaleFile.NumericValue number ->
        validateThreshold fieldType number
        |> Result.mapError (fun reason ->
            "The field '"
            + fieldName
            + "' has a coordinate the encoded theory cannot represent: "
            + reason)
    | (DomainExpression.IntegerType
      | DomainExpression.NaturalType
      | DomainExpression.RealType), ScaleFile.BooleanValue _ ->
        Error("The numeric field '" + fieldName + "' has a boolean coordinate.")

/// Builds a finite lattice. Every field has at least one coordinate, every
/// coordinate belongs to its field's fieldType, and the encoded theory can represent
/// every numeric coordinate exactly.
let create (fields: (string * FieldType * ScaleFile.FieldValue list) list) : Result<Lattice, string> =
    let addField
        (seen: Set<string>, accumulated: Field list)
        (name, fieldType, coordinates)
        : Result<Set<string> * Field list, string> =
        let distinct = List.distinct coordinates

        let rec validateAll (remaining: ScaleFile.FieldValue list) : Result<unit, string> =
            match remaining with
            | [] -> Ok()
            | value :: rest ->
                validateCoordinate name fieldType value
                |> Result.bind (fun () -> validateAll rest)

        if System.String.IsNullOrWhiteSpace name then
            Error("A type lattice field must have a name.")
        elif seen.Contains name then
            Error("The type lattice names the field '" + name + "' twice.")
        elif List.isEmpty distinct then
            Error(
                "The field '"
                + name
                + "' has no coordinates; its generator would sample nothing."
            )
        else
            validateAll distinct
            |> Result.map (fun () ->
                seen.Add name,
                {
                    Name = name
                    Type = fieldType
                    Coordinates = Some distinct
                    Narrowing = []
                }
                :: accumulated)

    fields
    |> List.fold
        (fun state field ->
            state
            |> Result.bind (fun (seen, accumulated) -> addField (seen, accumulated) field))
        (Ok(Set.empty, []))
    |> Result.map (fun (_, accumulated) -> { Fields = List.rev accumulated })

/// Builds a symbolic lattice for a consumer that has only field fieldTypes, such as
/// the decision-table checker. Its fields have no finite coordinates and no
/// narrowing, so it can feed Z3 but not a finite generator or the refiner.
let ofFieldTypes (fields: (string * FieldType) list) : Lattice =
    {
        Fields =
            fields
            |> List.map (fun (name, fieldType) ->
                {
                    Name = name
                    Type = fieldType
                    Coordinates = None
                    Narrowing = []
                })
    }

let fieldNames (lattice: Lattice) : string list =
    lattice.Fields |> List.map (fun field -> field.Name)

let fieldTypes (lattice: Lattice) : (string * FieldType) list =
    lattice.Fields |> List.map (fun field -> field.Name, field.Type)

let findField (name: string) (lattice: Lattice) : Field option =
    lattice.Fields |> List.tryFind (fun field -> field.Name = name)

/// The sort of one field, for a reader that needs only the type and not the
/// coordinates or narrowing.
let tryFieldType (name: string) (lattice: Lattice) : FieldType option =
    findField name lattice |> Option.map (fun field -> field.Type)

let refinementGuardField (guard: ScaleFile.RefinementGuard) : string =
    match guard with
    | ScaleFile.GreaterThan(field, _)
    | ScaleFile.AtLeast(field, _)
    | ScaleFile.LessThan(field, _)
    | ScaleFile.AtMost(field, _)
    | ScaleFile.IsTrue field
    | ScaleFile.IsFalse field
    | ScaleFile.NamedGuard(field, _) -> field

/// A typed guard's field type. A named guard has no static field type: the
/// image-resident quotation decides it, so it is `None` here (D41).
let refinementGuardType (guard: ScaleFile.RefinementGuard) : FieldType option =
    match guard with
    | ScaleFile.GreaterThan _
    | ScaleFile.AtLeast _
    | ScaleFile.LessThan _
    | ScaleFile.AtMost _ -> Some DomainExpression.IntegerType
    | ScaleFile.IsTrue _
    | ScaleFile.IsFalse _ -> Some DomainExpression.BooleanType
    | ScaleFile.NamedGuard _ -> None

let refinementGuardThreshold (guard: ScaleFile.RefinementGuard) : RealNumber option =
    match guard with
    | ScaleFile.GreaterThan(_, value)
    | ScaleFile.AtLeast(_, value)
    | ScaleFile.LessThan(_, value)
    | ScaleFile.AtMost(_, value) -> Some value
    | ScaleFile.IsTrue _
    | ScaleFile.IsFalse _
    | ScaleFile.NamedGuard _ -> None

/// The meaning both the refiner and the runtime guard evaluator use. A named
/// guard asks the resolver, which the evaluator backs with the image-resident
/// guard metaobject; without one the answer is `GuardUndecided`, never a
/// silent false. A mismatched field value does not satisfy a typed guard; the
/// field-type check belongs to `validateGuard`, which refuses the guard before
/// it enters a field.
let refinementGuardHoldsValueWith
    (resolve: GuardResolver)
    (guard: ScaleFile.RefinementGuard)
    (value: ScaleFile.FieldValue)
    : GuardVerdict =
    match guard, value with
    | ScaleFile.GreaterThan(_, threshold), ScaleFile.NumericValue actual ->
        if actual > threshold then GuardHolds else GuardDoesNotHold
    | ScaleFile.AtLeast(_, threshold), ScaleFile.NumericValue actual ->
        if actual >= threshold then GuardHolds else GuardDoesNotHold
    | ScaleFile.LessThan(_, threshold), ScaleFile.NumericValue actual ->
        if actual < threshold then GuardHolds else GuardDoesNotHold
    | ScaleFile.AtMost(_, threshold), ScaleFile.NumericValue actual ->
        if actual <= threshold then GuardHolds else GuardDoesNotHold
    | ScaleFile.IsTrue _, ScaleFile.BooleanValue actual -> if actual then GuardHolds else GuardDoesNotHold
    | ScaleFile.IsFalse _, ScaleFile.BooleanValue actual -> if not actual then GuardHolds else GuardDoesNotHold
    | ScaleFile.NamedGuard(_, name), _ -> resolve name value
    | _ -> GuardDoesNotHold

/// The typed-only form of `refinementGuardHoldsValueWith`. The refiner's
/// candidates are typed guards, so this path never depends on an image; a named
/// guard is undecided instead of being read as false.
let refinementGuardHoldsValue (guard: ScaleFile.RefinementGuard) (value: ScaleFile.FieldValue) : GuardVerdict =
    refinementGuardHoldsValueWith noGuardResolver guard value

let private validateGuardField (field: Field) (guard: ScaleFile.RefinementGuard) : Result<unit, string> =
    if refinementGuardField guard <> field.Name then
        Error(
            "The guard '"
            + ScaleFile.refinementGuardText guard
            + "' names '"
            + refinementGuardField guard
            + "', not '"
            + field.Name
            + "'."
        )
    else
        Ok()

let private guardTypeMatches (fieldType: FieldType) (guardType: FieldType) : bool =
    match fieldType, guardType with
    | DomainExpression.BooleanType, DomainExpression.BooleanType -> true
    | DomainExpression.BooleanType, _
    | _, DomainExpression.BooleanType -> false
    | _ -> true

let private validateTypedGuard (field: Field) (guard: ScaleFile.RefinementGuard) : Result<unit, string> =
    validateGuardField field guard
    |> Result.bind (fun () ->
        let mismatch () =
            Error(
                "The guard '"
                + ScaleFile.refinementGuardText guard
                + "' does not match the field type of the field '"
                + field.Name
                + "'."
            )

        match refinementGuardType guard with
        | None -> Ok()
        | Some guardType when not (guardTypeMatches field.Type guardType) -> mismatch ()
        | Some _ ->
            match refinementGuardThreshold guard with
            | None -> Ok()
            | Some threshold ->
                validateThreshold field.Type threshold
                |> Result.map ignore
                |> Result.mapError (fun reason ->
                    "The guard '"
                    + ScaleFile.refinementGuardText guard
                    + "' is not representable in the encoded theory: "
                    + reason))

/// A typed guard enters the verification lattice; a named guard needs the
/// evaluator's resolver, so the plain lattice refuses it by name.
let private validateGuard (field: Field) (guard: ScaleFile.RefinementGuard) : Result<unit, string> =
    validateGuardField field guard
    |> Result.bind (fun () ->
        match guard with
        | ScaleFile.NamedGuard(_, name) ->
            Error(
                "The guard names the image-resident guard metaobject '"
                + name
                + "'; use the lattice operation with a runtime guard resolver (D41)."
            )
        | _ -> validateTypedGuard field guard)

/// The resolver-aware validation: the field must still match, but a named
/// guard's type and truth are the image's to decide.
let private validateGuardWithResolver (field: Field) (guard: ScaleFile.RefinementGuard) : Result<unit, string> =
    match guard with
    | ScaleFile.NamedGuard _ -> validateGuardField field guard
    | _ -> validateTypedGuard field guard

let private holdsAll
    (resolve: GuardResolver)
    (guards: ScaleFile.RefinementGuard list)
    (value: ScaleFile.FieldValue)
    : GuardVerdict =
    let rec go (remaining: ScaleFile.RefinementGuard list) : GuardVerdict =
        match remaining with
        | [] -> GuardHolds
        | guard :: rest ->
            match refinementGuardHoldsValueWith resolve guard value with
            | GuardHolds -> go rest
            | GuardDoesNotHold -> GuardDoesNotHold
            | GuardUndecided reason -> GuardUndecided reason

    go guards

/// The field's coordinates after every narrowing guard, with named guards
/// resolved through the evaluator. `None` means the field is symbolic;
/// `Some []` means the narrowing emptied a finite field. An undecidable guard
/// is an `Error`, never a silent drop.
let coordinatesAfterNarrowingWithResolver
    (resolve: GuardResolver)
    (field: Field)
    : Result<ScaleFile.FieldValue list option, string> =
    let rec validateAll (remaining: ScaleFile.RefinementGuard list) : Result<unit, string> =
        match remaining with
        | [] -> Ok()
        | guard :: rest ->
            validateGuardWithResolver field guard
            |> Result.bind (fun () -> validateAll rest)

    let rec keep
        (kept: ScaleFile.FieldValue list)
        (remaining: ScaleFile.FieldValue list)
        : Result<ScaleFile.FieldValue list, string> =
        match remaining with
        | [] -> Ok(List.rev kept)
        | value :: rest ->
            match holdsAll resolve field.Narrowing value with
            | GuardHolds -> keep (value :: kept) rest
            | GuardDoesNotHold -> keep kept rest
            | GuardUndecided reason -> Error reason

    validateAll field.Narrowing
    |> Result.bind (fun () ->
        match field.Coordinates with
        | None -> Ok None
        | Some values -> keep [] values |> Result.map Some)

/// The typed-only form of `coordinatesAfterNarrowingWithResolver`. A named
/// guard is refused before it reaches a generator.
let coordinatesAfterNarrowing (field: Field) : Result<ScaleFile.FieldValue list option, string> =
    coordinatesAfterNarrowingWithResolver noGuardResolver field

/// Every guard that narrows the lattice, in field order, with exact duplicates
/// removed. Z3 conjoins these; the document's refinements are deduplicated
/// against them by the same canonical guard text.
let constraints (lattice: Lattice) : ScaleFile.RefinementGuard list =
    lattice.Fields
    |> List.collect (fun field -> field.Narrowing)
    |> List.distinctBy ScaleFile.refinementGuardText

let private narrowedCoordinatesWithResolver
    (resolve: GuardResolver)
    (field: Field)
    : Result<ScaleFile.FieldValue list, string> =
    coordinatesAfterNarrowingWithResolver resolve field
    |> Result.bind (function
        | None ->
            Error(
                "The field '"
                + field.Name
                + "' has no declared coordinates; it cannot feed a finite generator."
            )
        | Some [] ->
            Error(
                "The narrowed field '"
                + field.Name
                + "' is empty; its generator would sample nothing."
            )
        | Some values -> Ok values)

let private narrowedCoordinates (field: Field) : Result<ScaleFile.FieldValue list, string> =
    narrowedCoordinatesWithResolver noGuardResolver field

/// The finite generator for one field. A symbolic or emptied field is an
/// `Error` before a generator is built, so Hedgehog never reports `GaveUp` for
/// an empty domain. The resolver-aware form lets a named guard narrow the field.
let generatorWithResolver (resolve: GuardResolver) (field: Field) : Result<Hedgehog.Gen<ScaleFile.FieldValue>, string> =
    narrowedCoordinatesWithResolver resolve field |> Result.map Gen.item

let generator (field: Field) : Result<Hedgehog.Gen<ScaleFile.FieldValue>, string> =
    generatorWithResolver noGuardResolver field

let generatorForWithResolver
    (resolve: GuardResolver)
    (name: string)
    (lattice: Lattice)
    : Result<Hedgehog.Gen<ScaleFile.FieldValue>, string> =
    match findField name lattice with
    | None -> Error("The type lattice has no field named '" + name + "'.")
    | Some field -> generatorWithResolver resolve field

let generatorFor (name: string) (lattice: Lattice) : Result<Hedgehog.Gen<ScaleFile.FieldValue>, string> =
    generatorForWithResolver noGuardResolver name lattice

let booleanGeneratorForWithResolver
    (resolve: GuardResolver)
    (name: string)
    (lattice: Lattice)
    : Result<Hedgehog.Gen<bool>, string> =
    match findField name lattice with
    | None -> Error("The type lattice has no field named '" + name + "'.")
    | Some field when field.Type <> DomainExpression.BooleanType -> Error("The field '" + name + "' is not boolean.")
    | Some field ->
        narrowedCoordinatesWithResolver resolve field
        |> Result.bind (fun values ->
            let booleans =
                values
                |> List.choose (function
                    | ScaleFile.BooleanValue value -> Some value
                    | ScaleFile.NumericValue _ -> None)

            if List.length booleans = List.length values then
                Ok(Gen.item booleans)
            else
                Error("The field '" + name + "' has a coordinate that is not boolean."))

let booleanGeneratorFor (name: string) (lattice: Lattice) : Result<Hedgehog.Gen<bool>, string> =
    booleanGeneratorForWithResolver noGuardResolver name lattice

let numericGeneratorForWithResolver
    (resolve: GuardResolver)
    (name: string)
    (lattice: Lattice)
    : Result<Hedgehog.Gen<RealNumber>, string> =
    match findField name lattice with
    | None -> Error("The type lattice has no field named '" + name + "'.")
    | Some field when not (DomainExpression.isNumeric field.Type) ->
        Error("The field '" + name + "' is not numeric.")
    | Some field ->
        narrowedCoordinatesWithResolver resolve field
        |> Result.bind (fun values ->
            let numbers =
                values
                |> List.choose (function
                    | ScaleFile.NumericValue value -> Some value
                    | ScaleFile.BooleanValue _ -> None)

            if List.length numbers = List.length values then
                Ok(Gen.item numbers)
            else
                Error("The field '" + name + "' has a coordinate that is not numeric."))

let numericGeneratorFor (name: string) (lattice: Lattice) : Result<Hedgehog.Gen<RealNumber>, string> =
    numericGeneratorForWithResolver noGuardResolver name lattice

let private candidateGuardsFor (field: Field) : Result<ScaleFile.RefinementGuard list, string> =
    match field.Coordinates with
    | None ->
        Error(
            "The field '"
            + field.Name
            + "' has no declared coordinates; the refiner cannot propose guards for it."
        )
    | Some coordinates ->
        let rec collect (remaining: ScaleFile.FieldValue list) : Result<ScaleFile.RefinementGuard list, string> =
            match remaining with
            | [] -> Ok []
            | value :: rest ->
                let guards =
                    match field.Type, value with
                    | DomainExpression.BooleanType, ScaleFile.BooleanValue true -> Ok [ ScaleFile.IsTrue field.Name ]
                    | DomainExpression.BooleanType, ScaleFile.BooleanValue false -> Ok [ ScaleFile.IsFalse field.Name ]
                    | (DomainExpression.IntegerType
                      | DomainExpression.NaturalType
                      | DomainExpression.RealType), ScaleFile.NumericValue threshold ->
                        validateThreshold field.Type threshold
                        |> Result.map (fun () ->
                            [
                                ScaleFile.GreaterThan(field.Name, threshold)
                                ScaleFile.AtLeast(field.Name, threshold)
                                ScaleFile.LessThan(field.Name, threshold)
                                ScaleFile.AtMost(field.Name, threshold)
                            ])
                    | _ ->
                        Error(
                            "The field '"
                            + field.Name
                            + "' has a coordinate that does not match its fieldType."
                        )

                guards
                |> Result.bind (fun head -> collect rest |> Result.map (fun tail -> head @ tail))

        collect (List.distinct coordinates)

/// Every guard the refiner may propose: each distinct coordinate, in every
/// relation its fieldType supports. The refiner still proves provenClosure with Z3 and
/// ranks the survivors by coverage; this function only supplies the candidates.
let candidateGuards (lattice: Lattice) : Result<ScaleFile.RefinementGuard list, string> =
    let rec collect
        (remaining: Field list)
        (accumulated: ScaleFile.RefinementGuard list)
        : Result<ScaleFile.RefinementGuard list, string> =
        match remaining with
        | [] -> Ok accumulated
        | field :: rest ->
            candidateGuardsFor field
            |> Result.bind (fun guards -> collect rest (accumulated @ guards))

    collect lattice.Fields []
    |> Result.map (List.distinctBy ScaleFile.refinementGuardText)

/// True when every field declares finite coordinates, so the exhaustive
/// universe and the coverage ranking are available. A symbolic lattice can
/// still feed Z3, but the refiner must use a counterexample instead of a
/// universe (D56).
let isFinite (lattice: Lattice) : bool =
    lattice.Fields |> List.forall (fun field -> field.Coordinates.IsSome)

/// The weakest guards that exclude a counterexample's value: one pair per
/// numeric field (above and below) and one guard per boolean field. A symbolic
/// field has no declared coordinates, so the refiner cannot enumerate
/// thresholds; it starts at the witness's value and widens outward, proving
/// each step with Z3 (D56). A field the counterexample omits is an Error, never
/// a silently skipped field.
let candidateGuardsFromWitness
    (witness: Map<string, ScaleFile.FieldValue>)
    (lattice: Lattice)
    : Result<ScaleFile.RefinementGuard list, string> =
    let guardsFor (field: Field) : Result<ScaleFile.RefinementGuard list, string> =
        match witness.TryFind field.Name with
        | None ->
            Error(
                "The counterexample has no value for the field '"
                + field.Name
                + "'; the refiner cannot propose a guard."
            )
        | Some value ->
            match field.Type, value with
            | DomainExpression.BooleanType, ScaleFile.BooleanValue true -> Ok [ ScaleFile.IsFalse field.Name ]
            | DomainExpression.BooleanType, ScaleFile.BooleanValue false -> Ok [ ScaleFile.IsTrue field.Name ]
            | (DomainExpression.IntegerType
              | DomainExpression.NaturalType
              | DomainExpression.RealType), ScaleFile.NumericValue number ->
                validateThreshold field.Type number
                |> Result.map (fun () ->
                    [
                        ScaleFile.GreaterThan(field.Name, number)
                        ScaleFile.LessThan(field.Name, number)
                    ])
                |> Result.mapError (fun reason ->
                    "The counterexample's value for '"
                    + field.Name
                    + "' is not representable in the encoded theory: "
                    + reason)
            | _ ->
                Error(
                    "The counterexample's value for '"
                    + field.Name
                    + "' does not match its fieldType."
                )

    lattice.Fields
    |> List.fold
        (fun accumulated field ->
            accumulated
            |> Result.bind (fun guards -> guardsFor field |> Result.map (fun head -> guards @ head)))
        (Ok [])

let rec private assignmentsOfWithResolver
    (resolve: GuardResolver)
    (fields: Field list)
    : Result<Map<string, ScaleFile.FieldValue> list, string> =
    match fields with
    | [] -> Ok [ Map.empty ]
    | field :: rest ->
        narrowedCoordinatesWithResolver resolve field
        |> Result.bind (fun values ->
            assignmentsOfWithResolver resolve rest
            |> Result.map (fun tails ->
                [
                    for value in values do
                        for tail in tails do
                            yield tail.Add(field.Name, value)
                ]))

let private assignmentsOf (fields: Field list) : Result<Map<string, ScaleFile.FieldValue> list, string> =
    assignmentsOfWithResolver noGuardResolver fields

/// The finite members of the lattice, as field-value maps in field order.
let assignments (lattice: Lattice) : Result<Map<string, ScaleFile.FieldValue> list, string> =
    assignmentsOf lattice.Fields

/// The finite members with named guards resolved by the evaluator.
let assignmentsWithResolver
    (resolve: GuardResolver)
    (lattice: Lattice)
    : Result<Map<string, ScaleFile.FieldValue> list, string> =
    assignmentsOfWithResolver resolve lattice.Fields

/// The finite members projected into a host type. The projection is supplied
/// by the domain binding; it never decides membership.
let universe (lattice: Lattice) (make: Map<string, ScaleFile.FieldValue> -> 'T) : Result<'T list, string> =
    assignments lattice |> Result.map (List.map make)

/// The finite members projected into a host type, with named guards resolved.
let universeWithResolver
    (resolve: GuardResolver)
    (lattice: Lattice)
    (make: Map<string, ScaleFile.FieldValue> -> 'T)
    : Result<'T list, string> =
    assignmentsWithResolver resolve lattice |> Result.map (List.map make)

/// Applies a guard to the field it names, with named guards resolved through
/// the evaluator. A finite field is refused when the guard empties it; a
/// symbolic field keeps the guard as a constraint because emptiness is not
/// decidable there. Re-applying the same guard is idempotent.
let applyGuardWithResolver
    (resolve: GuardResolver)
    (guard: ScaleFile.RefinementGuard)
    (lattice: Lattice)
    : Result<Lattice, string> =
    match findField (refinementGuardField guard) lattice with
    | None -> Error("The type lattice has no field named '" + refinementGuardField guard + "'.")
    | Some field ->
        validateGuardWithResolver field guard
        |> Result.bind (fun () ->
            let narrowing =
                if List.contains guard field.Narrowing then
                    field.Narrowing
                else
                    field.Narrowing @ [ guard ]

            let updated = { field with Narrowing = narrowing }

            coordinatesAfterNarrowingWithResolver resolve updated
            |> Result.bind (function
                | Some [] ->
                    Error(
                        "The guard '"
                        + ScaleFile.refinementGuardText guard
                        + "' empties the field '"
                        + field.Name
                        + "'."
                    )
                | _ ->
                    Ok
                        {
                            Fields =
                                lattice.Fields
                                |> List.map (fun current -> if current.Name = field.Name then updated else current)
                        }))

/// Applies a typed guard. A named guard is refused: it needs the evaluator's
/// resolver, so `applyGuardWithResolver` is the door for it (D41).
let applyGuard (guard: ScaleFile.RefinementGuard) (lattice: Lattice) : Result<Lattice, string> =
    match findField (refinementGuardField guard) lattice with
    | None -> Error("The type lattice has no field named '" + refinementGuardField guard + "'.")
    | Some field ->
        validateGuard field guard
        |> Result.bind (fun () -> applyGuardWithResolver noGuardResolver guard lattice)

/// Folds the document's existing refinements into the lattice. Callers that
/// re-enter the loop with a non-pristine document build the current region this
/// way, so the refiner's coverage and the returned lattice describe the inputs
/// the document already admits.
let withGuardsWithResolver
    (resolve: GuardResolver)
    (guards: ScaleFile.RefinementGuard list)
    (lattice: Lattice)
    : Result<Lattice, string> =
    guards
    |> List.fold (fun current guard -> current |> Result.bind (applyGuardWithResolver resolve guard)) (Ok lattice)

/// Folds typed document refinements into the lattice. A named guard is refused
/// here; the resolver-aware form is the door for it (D41).
let withGuards (guards: ScaleFile.RefinementGuard list) (lattice: Lattice) : Result<Lattice, string> =
    guards
    |> List.fold (fun current guard -> current |> Result.bind (applyGuard guard)) (Ok lattice)

/// Validates a pure lattice value. Direct record construction is allowed for
/// inspection and transformation; this is the check that makes such a value
/// admissible to the readers.
let validateField (field: Field) : Result<unit, string> =
    let validateCoordinates () =
        match field.Coordinates with
        | None -> Ok()
        | Some [] ->
            Error(
                "The field '"
                + field.Name
                + "' has no coordinates; its generator would sample nothing."
            )
        | Some values ->
            values
            |> List.fold
                (fun result value ->
                    result
                    |> Result.bind (fun () -> validateCoordinate field.Name field.Type value))
                (Ok())

    let validateNarrowing () =
        field.Narrowing
        |> List.fold
            (fun result guard ->
                result
                |> Result.bind (fun () ->
                    validateGuardWithResolver field guard))
            (Ok())

    if System.String.IsNullOrWhiteSpace field.Name then
        Error("A type lattice field must have a name.")
    else
        validateCoordinates () |> Result.bind (fun () -> validateNarrowing ())

let validate (lattice: Lattice) : Result<unit, string> =
    let rec loop (seen: Set<string>) (remaining: Field list) =
        match remaining with
        | [] -> Ok()
        | field :: rest ->
            if seen.Contains field.Name then
                Error("The type lattice names the field '" + field.Name + "' twice.")
            else
                validateField field
                |> Result.bind (fun () -> loop (seen.Add field.Name) rest)

    loop Set.empty lattice.Fields

/// Product composition is a pure value operation: concatenate fields and
/// validate the result. Duplicate field names are refused by `validate`.
let product (left: Lattice) (right: Lattice) : Result<Lattice, string> =
    let combined = { Fields = left.Fields @ right.Fields }
    validate combined |> Result.map (fun () -> combined)

let private fieldTypeText (fieldType: FieldType) : string =
    match fieldType with
    | DomainExpression.BooleanType -> "boolean"
    | DomainExpression.IntegerType -> "integer"
    | DomainExpression.NaturalType -> "natural"
    | DomainExpression.RealType -> "real"

let private fieldTypeOfText (text: string) : Result<FieldType, string> =
    match text with
    | "boolean" -> Ok DomainExpression.BooleanType
    | "integer" -> Ok DomainExpression.IntegerType
    | "natural" -> Ok DomainExpression.NaturalType
    | "real" -> Ok DomainExpression.RealType
    | other -> Error("unknown field type: " + other)

let private writeFieldValue (writer: System.Text.Json.Utf8JsonWriter) (value: ScaleFile.FieldValue) =
    match value with
    | ScaleFile.BooleanValue boolean -> writer.WriteBooleanValue boolean
    | ScaleFile.NumericValue number -> writer.WriteNumberValue number

let private writeGuard
    (writer: System.Text.Json.Utf8JsonWriter)
    (guard: ScaleFile.RefinementGuard)
    =
    writer.WriteStartObject()

    match guard with
    | ScaleFile.GreaterThan(field, value) ->
        writer.WriteString("kind", "greater-than")
        writer.WriteString("field", field)
        writer.WriteNumber("value", value)
    | ScaleFile.AtLeast(field, value) ->
        writer.WriteString("kind", "at-least")
        writer.WriteString("field", field)
        writer.WriteNumber("value", value)
    | ScaleFile.LessThan(field, value) ->
        writer.WriteString("kind", "less-than")
        writer.WriteString("field", field)
        writer.WriteNumber("value", value)
    | ScaleFile.AtMost(field, value) ->
        writer.WriteString("kind", "at-most")
        writer.WriteString("field", field)
        writer.WriteNumber("value", value)
    | ScaleFile.IsTrue field ->
        writer.WriteString("kind", "is-true")
        writer.WriteString("field", field)
    | ScaleFile.IsFalse field ->
        writer.WriteString("kind", "is-false")
        writer.WriteString("field", field)
    | ScaleFile.NamedGuard(field, name) ->
        writer.WriteString("kind", "named")
        writer.WriteString("field", field)
        writer.WriteString("name", name)

    writer.WriteEndObject()

/// A canonical data document for a pure lattice value. The schema is versioned
/// so a reader can refuse an incompatible document instead of guessing.
let toJson (lattice: Lattice) : string =
    use stream = new System.IO.MemoryStream()
    use writer = new System.Text.Json.Utf8JsonWriter(stream)

    writer.WriteStartObject()
    writer.WriteNumber("schema", 1)
    writer.WriteStartArray("fields")

    for field in lattice.Fields do
        writer.WriteStartObject()
        writer.WriteString("name", field.Name)
        writer.WriteString("type", fieldTypeText field.Type)

        match field.Coordinates with
        | None -> writer.WriteNull("coordinates")
        | Some values ->
            writer.WriteStartArray("coordinates")

            for value in values do
                writeFieldValue writer value

            writer.WriteEndArray()

        writer.WriteStartArray("narrowing")

        for guard in field.Narrowing do
            writeGuard writer guard

        writer.WriteEndArray()
        writer.WriteEndObject()

    writer.WriteEndArray()
    writer.WriteEndObject()
    writer.Flush()

    System.Text.Encoding.UTF8.GetString(stream.ToArray())

let private requiredProperty (element: System.Text.Json.JsonElement) (name: string) =
    match element.TryGetProperty name with
    | true, value -> Ok value
    | false, _ -> Error("missing lattice property: " + name)

let private readFieldValue (element: System.Text.Json.JsonElement) : Result<ScaleFile.FieldValue, string> =
    match element.ValueKind with
    | System.Text.Json.JsonValueKind.True -> Ok(ScaleFile.BooleanValue true)
    | System.Text.Json.JsonValueKind.False -> Ok(ScaleFile.BooleanValue false)
    | System.Text.Json.JsonValueKind.Number -> element.GetDouble() |> ScaleFile.NumericValue |> Ok
    | _ -> Error("a lattice coordinate must be a number or boolean")

let private readGuard (element: System.Text.Json.JsonElement) : Result<ScaleFile.RefinementGuard, string> =
    requiredProperty element "kind"
    |> Result.bind (fun kind ->
        requiredProperty element "field"
        |> Result.bind (fun field ->
            let fieldName = field.GetString()

            match kind.GetString() with
            | "greater-than" ->
                requiredProperty element "value"
                |> Result.map (fun value -> ScaleFile.GreaterThan(fieldName, value.GetDouble()))
            | "at-least" ->
                requiredProperty element "value"
                |> Result.map (fun value -> ScaleFile.AtLeast(fieldName, value.GetDouble()))
            | "less-than" ->
                requiredProperty element "value"
                |> Result.map (fun value -> ScaleFile.LessThan(fieldName, value.GetDouble()))
            | "at-most" ->
                requiredProperty element "value"
                |> Result.map (fun value -> ScaleFile.AtMost(fieldName, value.GetDouble()))
            | "is-true" -> Ok(ScaleFile.IsTrue fieldName)
            | "is-false" -> Ok(ScaleFile.IsFalse fieldName)
            | "named" ->
                requiredProperty element "name"
                |> Result.map (fun name -> ScaleFile.NamedGuard(fieldName, name.GetString()))
            | other -> Error("unknown lattice guard kind: " + other)))

/// Reads a canonical lattice document and validates it before returning.
let ofJson (text: string) : Result<Lattice, string> =
    try
        use document = System.Text.Json.JsonDocument.Parse text
        let root = document.RootElement

        requiredProperty root "schema"
        |> Result.bind (fun schema ->
            if schema.GetInt32() <> 1 then
                Error("unsupported lattice schema: " + string (schema.GetInt32()))
            else
                requiredProperty root "fields"
                |> Result.bind (fun fields ->
                    let rec readFields (remaining: System.Text.Json.JsonElement list) accumulated =
                        match remaining with
                        | [] -> Ok(List.rev accumulated)
                        | field :: rest ->
                            requiredProperty field "name"
                            |> Result.bind (fun name ->
                                requiredProperty field "type"
                                |> Result.bind (fun fieldType ->
                                    fieldTypeOfText (fieldType.GetString())
                                    |> Result.bind (fun fieldType ->
                                        requiredProperty field "coordinates"
                                        |> Result.bind (fun coordinates ->
                                            let readCoordinates () =
                                                match coordinates.ValueKind with
                                                | System.Text.Json.JsonValueKind.Null -> Ok None
                                                | System.Text.Json.JsonValueKind.Array ->
                                                    coordinates.EnumerateArray()
                                                    |> Seq.toList
                                                    |> List.fold
                                                        (fun result value ->
                                                            result
                                                            |> Result.bind (fun current ->
                                                                readFieldValue value
                                                                |> Result.map (fun value -> current @ [ value ])))
                                                        (Ok [])
                                                    |> Result.map Some
                                                | _ -> Error("coordinates must be null or an array")

                                            readCoordinates ()
                                            |> Result.bind (fun coordinates ->
                                                requiredProperty field "narrowing"
                                                |> Result.bind (fun narrowing ->
                                                    narrowing.EnumerateArray()
                                                    |> Seq.toList
                                                    |> List.fold
                                                        (fun result guard ->
                                                            result
                                                            |> Result.bind (fun current ->
                                                                readGuard guard
                                                                |> Result.map (fun guard -> current @ [ guard ])))
                                                        (Ok [])
                                                    |> Result.bind (fun narrowing ->
                                                        readFields
                                                            rest
                                                            ({ Name = name.GetString()
                                                               Type = fieldType
                                                               Coordinates = coordinates
                                                               Narrowing = narrowing }
                                                             :: accumulated))))))))

                    readFields (fields.EnumerateArray() |> Seq.toList) []
                    |> Result.bind (fun fields ->
                        let lattice = { Fields = fields }
                        validate lattice |> Result.map (fun () -> lattice))))
    with
    | :? System.Text.Json.JsonException as error -> Error("invalid lattice JSON: " + error.Message)
    | :? System.InvalidOperationException as error -> Error("invalid lattice JSON: " + error.Message)
