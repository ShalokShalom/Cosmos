module DemoDomain

// The record under test. In the real pipeline this module is the engine's
// code; Scale never owns it, it only specifies it.

open Types

/// The record under test: the stand-in for a game object. Health is an
/// Integer field in the lattice, so the host record stores it as `int`.
type Player = { Alive: bool; Health: int }

/// The health values the domain exposes: the integers from minus ten to ten.
let healthValues: RealNumber list = [ for health in -10 .. 10 -> double health ]

/// The one description of the verification domain (D25). The SMT encoding,
/// the Hedgehog generators, and the refiner all read this value; the `.scale`
/// document remains the editable source of the specification.
let lattice: TypeLattice.Lattice =
    match
        TypeLattice.create
            [
                "alive", DomainExpression.BooleanType, [ ScaleFile.BooleanValue true; ScaleFile.BooleanValue false ]
                "health", DomainExpression.IntegerType, healthValues |> List.map ScaleFile.NumericValue
            ]
    with
    | Ok lattice -> lattice
    | Error reason -> failwith ("The demonstration domain's type lattice is invalid: " + reason)

/// Projects one member of the finite lattice into the host record. The lattice
/// is static and validated above, so a missing or ill-sorted field is an
/// authoring bug at this boundary, not a runtime input error.
let makePlayer (values: Map<string, ScaleFile.FieldValue>) : Player =
    let alive =
        match values.TryFind "alive" with
        | Some(ScaleFile.BooleanValue value) -> value
        | Some(ScaleFile.NumericValue _) -> failwith "The demonstration lattice's 'alive' field is not boolean."
        | None -> failwith "The demonstration lattice has no field named 'alive'."

    let health =
        match values.TryFind "health" with
        | Some(ScaleFile.NumericValue value) ->
            match DomainExpression.integerValue value with
            | Ok number -> number
            | Error reason -> failwith ("The demonstration lattice's 'health' field is not an integer: " + reason)
        | Some(ScaleFile.BooleanValue _) -> failwith "The demonstration lattice's 'health' field is not numeric."
        | None -> failwith "The demonstration lattice has no field named 'health'."

    { Alive = alive; Health = health }

/// Every input the domain can produce. Kept finite and small on purpose, so
/// the refinement service can validate candidate guards exhaustively.
let universe: Player list =
    TypeLattice.universe lattice makePlayer
    |> Result.defaultWith (fun reason -> failwith ("The demonstration domain's finite universe is invalid: " + reason))

/// The fields the domain exposes to guards.
let fields: string list = TypeLattice.fieldNames lattice

/// Projects a field, named by a vocabulary token, out of a player.
let fieldValue (player: Player) (field: string) : ScaleFile.FieldValue =
    match field with
    | "alive"
    | "isAlive" -> ScaleFile.BooleanValue player.Alive
    | "health" -> ScaleFile.NumericValue(double player.Health)
    | other -> invalidArg "field" ("The demonstration domain has no field named '" + other + "'.")

/// The implementation under test, as one body with two backends (D10): the
/// interpreter runs under Hedgehog, and `Smt.createFromLattice` encodes the
/// same value for Z3. The specification says every player that is alive can
/// jump; this implementation silently also requires health to be above one —
/// exactly the kind of hidden requirement that the refinement loop exists to
/// surface.
let canJumpExpression: DomainExpression.Expression =
    DomainExpression.Compare(
        DomainExpression.GreaterThan,
        DomainExpression.IntegerField "health",
        DomainExpression.IntegerConstant 1
    )

/// The executable implementation: the interpreter over the one body. An
/// ill-sorted body is a demo authoring bug, so it fails loudly at this edge
/// rather than silently becoming `false`.
let canJump (player: Player) : bool =
    DomainExpression.interpretBoolean fieldValue player canJumpExpression
    |> Result.defaultWith (fun reason -> failwith ("The demonstration domain's body is ill-sorted: " + reason))

/// The sorts the SMT encoding uses for the domain's fields.
let smtFields: (string * DomainExpression.FieldType) list =
    TypeLattice.fieldTypes lattice
