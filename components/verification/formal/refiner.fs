module Refiner

// The refinement service. It consumes Hedgehog's diagnostic output - the
// shrunk counterexample and the recheck seed - and synthesizes a candidate
// refinement for the .scale document.

open Types
open Hedgehog.FSharp

/// What Hedgehog reported: the shrunk counterexample and its recheck seed.
type Diagnostic =
    {
        Tests: int
        Shrinks: int
        Values: string list
        Seed: string
    }

/// Whether a property held, was falsified, or could not be decided.
/// `Inconclusive` is not a pass: Hedgehog's `GaveUp` means the generator did
/// not produce enough admissible samples, so the property was neither held
/// nor falsified, and the loop must say so rather than treat it as a pass.
type Falsification =
    | Held
    | Falsified of Diagnostic
    | Inconclusive of reason: string

/// The verdict for a completed Hedgehog run. Exposed separately from `falsify`
/// so the mapping from Hedgehog's status to the loop's verdict is testable
/// without running a generator: `GaveUp` is inconclusive, never a pass.
let verdictOf (status: Hedgehog.Status) (diagnostic: Diagnostic option) : Falsification =
    match status with
    | Hedgehog.Status.OK -> Held
    | Hedgehog.Status.GaveUp -> Inconclusive "the generator gave up before it produced enough admissible samples"
    | Hedgehog.Status.Failed _ ->
        match diagnostic with
        | Some counterexample -> Falsified counterexample
        | None -> Inconclusive "the property failed but Hedgehog reported no counterexample"

/// The journal already holds the shrunk counterexample. Reading it here avoids
/// Hedgehog's failure renderer, whose numeric `printf` specializations do not
/// exist under NativeAOT.
let private journalValues (journal: Hedgehog.Journal) : string list =
    match journal with
    | Hedgehog.Journal renderers ->
        try
            renderers |> Seq.map (fun render -> render ()) |> List.ofSeq
        with _ ->
            []

let private seedText (recheck: Hedgehog.RecheckInfo option) : string =
    match recheck with
    | Some info ->
        try
            Hedgehog.RecheckData.serialize info.Data
        with _ ->
            "?"
    | None -> "?"

/// Runs a property and reports Hedgehog's shrunk counterexample and recheck
/// seed. The report is read from the `Report` value, not by re-running the
/// property and parsing the rendered exception text.
let falsify (property: Hedgehog.Property<bool>) : Falsification =
    let report = Property.reportBool property

    match report.Status with
    | Hedgehog.Status.Failed failure ->
        verdictOf
            report.Status
            (Some
                {
                    Tests = int report.Tests
                    Shrinks = int failure.Shrinks
                    Values = journalValues failure.Journal
                    Seed = seedText failure.RecheckInfo
                })
    | _ -> verdictOf report.Status None

/// Evaluates a guard against a subject, using a field projection. The guard's
/// meaning is the lattice's meaning; the runtime evaluator and the synthesizer
/// do not keep two copies of it.
let evaluateRefinementGuard
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (guard: ScaleFile.RefinementGuard)
    (subject: 'T)
    : bool =
    match TypeLattice.refinementGuardHoldsValue guard (getField subject (TypeLattice.refinementGuardField guard)) with
    | TypeLattice.GuardHolds -> true
    | TypeLattice.GuardDoesNotHold -> false
    | TypeLattice.GuardUndecided reason ->
        invalidOp (
            "The refinement guard '"
            + ScaleFile.refinementGuardText guard
            + "' cannot be decided without a runtime guard resolver: "
            + reason
        )

/// The evidence behind a proved refinement: the solver version and the model
/// of the unrefined falsification (the `pre ∧ ¬post` witness).
type Provenance =
    {
        Solver: string
        Witness: Smt.SolverAssignment option
    }

/// A candidate guard and how many finite inputs it keeps in scope. `0` means
/// the domain is symbolic, so coverage was not measurable; the guard was
/// chosen by the witness-guided search instead (D56).
type Candidate =
    {
        Guard: ScaleFile.RefinementGuard
        Coverage: int
        Provenance: Provenance
    }

/// The chosen candidate and the lattice after the guard was written into it.
/// Returning the lattice is how refinement becomes a write to the shared
/// coordinate system rather than a value that lives only in the document.
type RefinementResult =
    {
        Candidate: Candidate
        Lattice: TypeLattice.Lattice
    }

/// The proved provenClosure oracle. `Proves` must answer `true` only when
/// `pre ∧ guard ∧ ¬post` is unsatisfiable; `demo.fs` additionally requires
/// `pre ∧ guard` to be satisfiable before returning true. The refiner never
/// scans the universe for provenClosure. `Witness` is the model of the unrefined
/// property, kept as provenance rather than used to decide.
type ProvenClosure =
    {
        Proves: ScaleFile.RefinementGuard -> bool
        Solver: string
        Witness: Smt.SolverAssignment option
    }

/// A solver value projected back into the lattice's field-value type. The
/// solver's integers are the encoded theory's Int32 values, so the conversion
/// is exact.
let private scaleFileValue (value: Smt.Value) : ScaleFile.FieldValue =
    match value with
    | Smt.BooleanValue flag -> ScaleFile.BooleanValue flag
    | Smt.IntegerValue number -> ScaleFile.NumericValue(double number)
    | Smt.RealValue number -> ScaleFile.NumericValue number

let private witnessValues (witness: Smt.SolverAssignment) : Map<string, ScaleFile.FieldValue> =
    witness |> Map.map (fun _ value -> scaleFileValue value)

/// Widens a seed guard outward until the oracle proves it, then bisects the
/// last failing and first proving thresholds to return the weakest proved
/// guard (D56). A boolean seed is proved or rejected as it stands. The search
/// is bounded at 2^30, so a divergent oracle cannot loop. Integer and Natural
/// thresholds are quantized to integers; a Real threshold keeps the bisection's
/// fractional precision. Natural thresholds never go below zero, so the search
/// cannot propose a guard that empties the sort's domain.
let private tighten
    (quantize: RealNumber -> RealNumber)
    (tolerance: RealNumber)
    (clamp: RealNumber -> RealNumber)
    (proves: ScaleFile.RefinementGuard -> bool)
    (seed: ScaleFile.RefinementGuard)
    : ScaleFile.RefinementGuard option =
    let search (build: RealNumber -> ScaleFile.RefinementGuard) (start: RealNumber) (direction: RealNumber) =
        if proves (build start) then
            Some(build start)
        else
            let rec expand (failed: RealNumber) (distance: RealNumber) : (RealNumber * RealNumber) option =
                if distance > 1073741824.0 then
                    None
                else
                    let candidate = clamp (start + direction * distance)

                    if candidate = failed then
                        None
                    elif proves (build candidate) then
                        Some(failed, candidate)
                    else
                        expand candidate (distance * 2.0)

            match expand start 1.0 with
            | None -> None
            | Some(failed, proved) ->
                let rec bisect (low: RealNumber) (high: RealNumber) : RealNumber =
                    if abs (high - low) <= tolerance then
                        high
                    else
                        let middle = clamp (quantize ((low + high) / 2.0))

                        if middle <= low || middle >= high then
                            high
                        elif proves (build middle) then
                            bisect low middle
                        else
                            bisect middle high

                Some(build (bisect failed proved))

    match seed with
    | ScaleFile.GreaterThan(field, threshold) -> search (fun value -> ScaleFile.GreaterThan(field, value)) threshold 1.0
    | ScaleFile.LessThan(field, threshold) -> search (fun value -> ScaleFile.LessThan(field, value)) threshold -1.0
    | _ -> if proves seed then Some seed else None

/// The witness-guided refinement for a symbolic lattice: no finite universe
/// exists, so coverage is not measurable and the first proved guard in field
/// order wins (`Coverage = 0` records that fact). The guard is still proved by
/// the same `pre ∧ guard ∧ ¬post` oracle as the finite path, never by sampling.
let private refineSymbolic
    (lattice: TypeLattice.Lattice)
    (currentRefinements: ScaleFile.RefinementGuard list)
    (provenClosure: ProvenClosure)
    : Result<RefinementResult option, string> =
    TypeLattice.withGuards currentRefinements lattice
    |> Result.bind (fun narrowed ->
        match provenClosure.Witness with
        | None ->
            Error(
                "The domain is symbolic and the solver reported no counterexample; the refiner cannot propose a guard."
            )
        | Some witness ->
            TypeLattice.candidateGuardsFromWitness (witnessValues witness) narrowed
            |> Result.bind (fun seeds ->
                let sorts = TypeLattice.fieldTypes narrowed |> Map.ofList

                let searchFor
                    (field: string)
                    : (RealNumber -> RealNumber) * RealNumber * (RealNumber -> RealNumber) =
                    match Map.tryFind field sorts with
                    | Some DomainExpression.RealType -> id, 1e-9, id
                    | Some DomainExpression.NaturalType -> floor, 1.0, (fun value -> max 0.0 value)
                    | _ -> floor, 1.0, id

                let viable =
                    seeds
                    |> List.choose (fun seed ->
                        let quantize, tolerance, clamp =
                            searchFor (TypeLattice.refinementGuardField seed)

                        tighten quantize tolerance clamp provenClosure.Proves seed)

                match viable with
                | [] -> Ok None
                | chosen :: _ ->
                    TypeLattice.applyGuard chosen narrowed
                    |> Result.map (fun withGuard ->
                        Some
                            {
                                Candidate =
                                    {
                                        Guard = chosen
                                        Coverage = 0
                                        Provenance =
                                            {
                                                Solver = provenClosure.Solver
                                                Witness = provenClosure.Witness
                                            }
                                    }
                                Lattice = withGuard
                            })))

/// The refinement service's core. Given the verification lattice, the guards
/// the document already carries, a finite projection, the specification's
/// precondition, and a proved provenClosure oracle, it finds the weakest single-field
/// guard that closes the falsified class.
///
/// The lattice is the only source of candidate guards and of the finite
/// universe; `currentRefinements` are folded in with `withGuards` before the
/// universe is built, so coverage and the returned lattice describe the current
/// document rather than a stale pristine region. `make` projects a member into
/// the host type. ProvenClosure is `pre ∧ guard ∧ ¬post` unsatisfiable, answered by
/// the oracle (Z3); the universe is used only to reject guards with no witness
/// in the demo and to rank the weakest guard by how many inputs it keeps in
/// scope. Ties are broken by the candidate enumeration order. The chosen guard
/// is written back into the lattice with `applyGuard`, and the result carries
/// that narrowed lattice. The provenance carries the solver version and the
/// model of the unrefined falsification.
let private refineFinite
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (make: Map<string, ScaleFile.FieldValue> -> 'T)
    (lattice: TypeLattice.Lattice)
    (currentRefinements: ScaleFile.RefinementGuard list)
    (specificationPrecondition: 'T -> bool)
    (provenClosure: ProvenClosure)
    : Result<RefinementResult option, string> =
    TypeLattice.withGuards currentRefinements lattice
    |> Result.bind (fun lattice ->
        TypeLattice.universe lattice make
        |> Result.bind (fun universe ->
            TypeLattice.candidateGuards lattice
            |> Result.bind (fun candidates ->
                let coverage (guard: ScaleFile.RefinementGuard) : int =
                    universe
                    |> List.filter (fun subject ->
                        specificationPrecondition subject
                        && evaluateRefinementGuard getField guard subject)
                    |> List.length

                let viable =
                    candidates
                    |> List.filter provenClosure.Proves
                    |> List.filter (fun guard -> coverage guard > 0)

                match viable with
                | [] -> Ok None
                | _ ->
                    let best = viable |> List.map coverage |> List.max
                    let chosen = viable |> List.find (fun guard -> coverage guard = best)

                    TypeLattice.applyGuard chosen lattice
                    |> Result.map (fun narrowed ->
                        Some
                            {
                                Candidate =
                                    {
                                        Guard = chosen
                                        Coverage = best
                                        Provenance =
                                            {
                                                Solver = provenClosure.Solver
                                                Witness = provenClosure.Witness
                                            }
                                    }
                                Lattice = narrowed
                            }))))

/// The refinement service's core. A finite lattice uses the exhaustive universe
/// and the coverage ranking (D25); a symbolic lattice uses the solver's
/// counterexample and witness-guided threshold search (D56). Both choose a guard
/// only when `pre ∧ guard ∧ ¬post` is proved.
let refine
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (make: Map<string, ScaleFile.FieldValue> -> 'T)
    (lattice: TypeLattice.Lattice)
    (currentRefinements: ScaleFile.RefinementGuard list)
    (specificationPrecondition: 'T -> bool)
    (provenClosure: ProvenClosure)
    : Result<RefinementResult option, string> =
    if TypeLattice.isFinite lattice then
        refineFinite getField make lattice currentRefinements specificationPrecondition provenClosure
    else
        refineSymbolic lattice currentRefinements provenClosure

// ============================================================
// Potential extensions (not commitments)
// - OwnershipTypes probe: connect runtime ownership to static verification.
// - Typed-term verification: semantic checks beyond structural validation.
// - Incremental proof cache: reuse obligations across hot-swap units.
// - Counterexample minimization: shorter witnesses for symbolic lattices.
// ============================================================
