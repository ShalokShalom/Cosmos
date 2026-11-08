module Smt

// The SMT encoding (D10). The parsed document is rendered as a Z3 formula by
// the same pipeline that renders the F# predicate; the implementation body
// comes from `DomainExpression`, so the proof is about the code under test rather
// than an uninterpreted symbol.

open Types
open Microsoft.Z3

/// A concrete value in a solver model.
type Value =
    | BooleanValue of bool
    | IntegerValue of int
    | RealValue of double

type SolverAssignment = Map<string, Value>

/// The solver verdict. `Proved`/`Falsified` are universal outcomes;
/// `Witnessed`/`NoWitness` are existential ones (D33). `Unknown` and
/// `Unavailable` are reported, never promoted to a pass.
type Outcome =
    | Proved
    | Falsified of SolverAssignment
    | Witnessed of SolverAssignment
    | NoWitness
    | Unknown of reason: string
    | Unavailable of reason: string

/// A verdict with its evidence: the named assumptions Z3 used when the answer
/// is unsatisfiable. A satisfiable answer has no core, and an undecided or
/// unencodable obligation has neither a core nor a model.
type Discharge = { Outcome: Outcome; Core: string list }

/// A Z3 expression tagged with its sort. The encoder returns one of these
/// instead of a bare `Expr`, so nothing downstream has to downcast with `:?>`
/// to recover the sort: the tag *is* the sort, and the compiler enforces it.
type Encoded =
    | EncodedBoolean of BoolExpr
    | EncodedInteger of IntExpr
    | EncodedReal of RealExpr

/// The field world: one constant per declared field, and nothing else.
///
/// A decision table needs only this half of an encoding, because it has no
/// single implementation body (each row selects an outcome). Splitting it out
/// lets a table be checked without inventing a placeholder body.
///
/// The record owns a native Z3 `Context`, so it is `IDisposable`: an owner
/// should `use` it. The finalizer on `Context` means forgetting is bounded
/// rather than fatal, but disposing releases the native memory at once.
type DomainEncoding =
    {
        Context: Context
        Fields: Map<string, Encoded>
        Sorts: Map<string, DomainExpression.FieldType>
        /// The verification lattice this encoding was built from. Its field
        /// sorts are already in `Sorts`; its narrowing guards are additional
        /// preconditions. A sort-only lattice has no finite coordinates and no
        /// narrowing, so legacy callers see exactly the previous encoding.
        Lattice: TypeLattice.Lattice
        /// Sort invariants that are not guards: a Natural field's `x >= 0`.
        SortConstraints: (string * BoolExpr) list
    }

    member this.Dispose() = this.Context.Dispose()

    interface System.IDisposable with
        member this.Dispose() = this.Context.Dispose()

/// The encoded world: the field world plus the implementation body. One
/// encoding serves every property over the same domain.
///
/// `VerbBody` is a `BoolExpr`, so `create` returning an `Encoding` is proof
/// that the body was well-sorted: an ill-sorted body is an `Error` from
/// `create`, not a representable `Encoding`.
type Encoding =
    {
        Domain: DomainEncoding
        VerbBody: BoolExpr
    }

    member this.Context = this.Domain.Context
    member this.Fields = this.Domain.Fields
    member this.Sorts = this.Domain.Sorts

    member this.Dispose() = this.Domain.Dispose()

    interface System.IDisposable with
        member this.Dispose() = this.Domain.Dispose()

/// The underlying Z3 expression of an encoded value, for cases (model
/// evaluation, printing) that are inherently sort-polymorphic.
let private toZ3Expression (encoded: Encoded) : Expr =
    match encoded with
    | EncodedBoolean value -> value :> Expr
    | EncodedInteger value -> value :> Expr
    | EncodedReal value -> value :> Expr

/// A property to discharge. Conditions are already mapped to domain fields;
/// refinements are the guards the document carries.
type Obligation =
    {
        Pattern: SentencePattern
        Conditions: Condition list
        Refinements: ScaleFile.Refinement list
    }

/// Builds the field world from the verification lattice: one constant per
/// declared field, at the lattice's sort. The lattice is kept so the
/// precondition can conjoin its narrowing guards.
let createDomainFromLattice (lattice: TypeLattice.Lattice) : DomainEncoding =
    let context = new Context()
    let fields = TypeLattice.fieldTypes lattice
    let sorts = fields |> Map.ofList

    let fieldEncoded =
        fields
        |> List.map (fun (name, sort) ->
            match sort with
            | DomainExpression.BooleanType -> name, EncodedBoolean(context.MkBoolConst name)
            | DomainExpression.IntegerType
            | DomainExpression.NaturalType -> name, EncodedInteger(context.MkIntConst name)
            | DomainExpression.RealType -> name, EncodedReal(context.MkRealConst name))
        |> Map.ofList

    let sortConstraints =
        fields
        |> List.choose (fun (name, sort) ->
            match sort with
            | DomainExpression.NaturalType ->
                let constant = context.MkIntConst name
                Some(name + " >= 0", context.MkGe(constant, context.MkInt 0))
            | _ -> None)

    {
        Context = context
        Fields = fieldEncoded
        Sorts = sorts
        Lattice = lattice
        SortConstraints = sortConstraints
    }

/// Builds a symbolic lattice for callers that have only field sorts. The
/// resulting encoding has no lattice narrowing, exactly as before D25.
let createDomain (fields: (string * DomainExpression.FieldType) list) : DomainEncoding =
    createDomainFromLattice (TypeLattice.ofFieldTypes fields)

let private isNumericSort (sort: DomainExpression.FieldType) : bool =
    match sort with
    | DomainExpression.BooleanType -> false
    | DomainExpression.RealType
    | DomainExpression.IntegerType
    | DomainExpression.NaturalType -> true

let private booleanField (domain: DomainEncoding) (name: string) : Result<Encoded, string> =
    match domain.Sorts.TryFind name with
    | Some DomainExpression.BooleanType -> Ok domain.Fields.[name]
    | Some sort when isNumericSort sort -> Error("The boolean field names a numeric field: " + name)
    | None -> Error("The domain has no field named '" + name + "'.")

let private arithmeticField (domain: DomainEncoding) (name: string) : Result<Encoded, string> =
    match domain.Sorts.TryFind name with
    | Some sort when isNumericSort sort -> Ok domain.Fields.[name]
    | Some DomainExpression.BooleanType -> Error("The numeric field names a boolean field: " + name)
    | None -> Error("The domain has no field named '" + name + "'.")

let private asBoolean (encoded: Encoded) : Result<BoolExpr, string> =
    match encoded with
    | EncodedBoolean value -> Ok value
    | EncodedInteger _
    | EncodedReal _ -> Error "Expected a boolean subexpression."

let private compareEncoded
    (context: Context)
    (comparison: DomainExpression.Comparison)
    (left: Encoded)
    (right: Encoded)
    : Result<BoolExpr, string> =
    let compareInt (l: IntExpr) (r: IntExpr) =
        match comparison with
        | DomainExpression.GreaterThan -> context.MkGt(l, r)
        | DomainExpression.AtLeast -> context.MkGe(l, r)
        | DomainExpression.LessThan -> context.MkLt(l, r)
        | DomainExpression.AtMost -> context.MkLe(l, r)
        | DomainExpression.Equals -> context.MkEq(l, r)

    let compareReal (l: RealExpr) (r: RealExpr) =
        match comparison with
        | DomainExpression.GreaterThan -> context.MkGt(l, r)
        | DomainExpression.AtLeast -> context.MkGe(l, r)
        | DomainExpression.LessThan -> context.MkLt(l, r)
        | DomainExpression.AtMost -> context.MkLe(l, r)
        | DomainExpression.Equals -> context.MkEq(l, r)

    match left, right with
    | EncodedInteger l, EncodedInteger r -> Ok(compareInt l r)
    | EncodedReal l, EncodedReal r -> Ok(compareReal l r)
    | EncodedInteger l, EncodedReal r -> Ok(compareReal (context.MkInt2Real l) r)
    | EncodedReal l, EncodedInteger r -> Ok(compareReal l (context.MkInt2Real r))
    | _ -> Error "Expected a numeric comparison."

/// Encodes one implementation expression against a field world. The encoder
/// carries each subexpression's sort in `Encoded`, so there is no downcast and
/// an ill-sorted expression cannot produce a well-sorted value: it is a named
/// `Error` instead.
let rec private encodeExpression
    (domain: DomainEncoding)
    (expression: DomainExpression.Expression)
    : Result<Encoded, string> =
    let context = domain.Context

    match expression with
    | DomainExpression.BooleanField name -> booleanField domain name
    | DomainExpression.IntegerField name -> arithmeticField domain name
    | DomainExpression.BooleanConstant value -> Ok(EncodedBoolean(context.MkBool value))
    | DomainExpression.IntegerConstant value -> Ok(EncodedInteger(context.MkInt value))
    | DomainExpression.Compare(comparison, left, right) ->
        encodeExpression domain left
        |> Result.bind (fun l ->
            encodeExpression domain right
            |> Result.bind (fun r ->
                compareEncoded context comparison l r
                |> Result.map EncodedBoolean))
    | DomainExpression.And(left, right) ->
        encodeExpression domain left
        |> Result.bind (fun l ->
            encodeExpression domain right
            |> Result.bind (fun r ->
                match asBoolean l, asBoolean r with
                | Ok lb, Ok rb -> Ok(EncodedBoolean(context.MkAnd(lb, rb)))
                | Error reason, _ -> Error reason
                | _, Error reason -> Error reason))
    | DomainExpression.Or(left, right) ->
        encodeExpression domain left
        |> Result.bind (fun l ->
            encodeExpression domain right
            |> Result.bind (fun r ->
                match asBoolean l, asBoolean r with
                | Ok lb, Ok rb -> Ok(EncodedBoolean(context.MkOr(lb, rb)))
                | Error reason, _ -> Error reason
                | _, Error reason -> Error reason))
    | DomainExpression.Not inner ->
        encodeExpression domain inner
        |> Result.bind (fun value ->
            match asBoolean value with
            | Ok b -> Ok(EncodedBoolean(context.MkNot b))
            | Error reason -> Error reason)

/// Builds the encoding from the domain's field sorts and the verb's body.
/// The body is the *implementation under test*, not an uninterpreted symbol.
///
/// `VerbBody` being a `BoolExpr` is a proof, not a hope: an ill-sorted body is
/// an `Error` from `create`, and the field world is disposed on that path.
let createFromLattice (lattice: TypeLattice.Lattice) (body: DomainExpression.Expression) : Result<Encoding, string> =
    let domain = createDomainFromLattice lattice

    match encodeExpression domain body with
    | Error reason ->
        domain.Dispose()
        Error reason
    | Ok(EncodedBoolean verbBody) -> Ok { Domain = domain; VerbBody = verbBody }
    | Ok(EncodedInteger _)
    | Ok(EncodedReal _) ->
        domain.Dispose()
        Error "The domain's implementation body must be a boolean expression."

/// Builds the encoding from a sort-only schema. Kept for callers that have no
/// lattice; they get a symbolic lattice with no narrowing.
let create
    (fields: (string * DomainExpression.FieldType) list)
    (body: DomainExpression.Expression)
    : Result<Encoding, string> =
    createFromLattice (TypeLattice.ofFieldTypes fields) body


let private arithmetic (domain: DomainEncoding) (field: string) : Result<Encoded, string> =
    match domain.Sorts.TryFind field, domain.Fields.TryFind field with
    | Some sort, Some value when isNumericSort sort -> Ok value
    | Some DomainExpression.BooleanType, _ -> Error("The numeric guard names a boolean field: " + field)
    | None, _ -> Error("The domain has no field named '" + field + "'.")
    | _ -> Error("The field '" + field + "' is not a numeric constant.")

let private numericConstant (domain: DomainEncoding) (field: string) (value: RealNumber) : Result<Encoded, string> =
    match domain.Sorts.TryFind field with
    | Some DomainExpression.IntegerType ->
        DomainExpression.integerValue value
        |> Result.map (fun number -> EncodedInteger(domain.Context.MkInt number))
    | Some DomainExpression.NaturalType ->
        DomainExpression.naturalValue value
        |> Result.map (fun number -> EncodedInteger(domain.Context.MkInt number))
    | Some DomainExpression.RealType ->
        DomainExpression.realValue value
        |> Result.map (fun number ->
            EncodedReal(domain.Context.MkReal(number.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))
    | Some DomainExpression.BooleanType -> Error("The numeric guard names a boolean field: " + field)
    | None -> Error("The domain has no field named '" + field + "'.")

let private booleanConstant (domain: DomainEncoding) (field: string) : Result<BoolExpr, string> =
    match domain.Sorts.TryFind field, domain.Fields.TryFind field with
    | Some DomainExpression.BooleanType, Some(EncodedBoolean value) -> Ok value
    | Some sort, _ when isNumericSort sort -> Error("The boolean guard names a numeric field: " + field)
    | None, _ -> Error("The domain has no field named '" + field + "'.")
    | _ -> Error("The field '" + field + "' is not a boolean constant.")

let private sequenceResults (results: Result<'a, string> list) : Result<'a list, string> =
    results
    |> List.fold
        (fun accumulated result ->
            accumulated
            |> Result.bind (fun values -> result |> Result.map (fun value -> value :: values)))
        (Ok [])
    |> Result.map List.rev

/// Encodes one sentence condition as a Z3 boolean expression. A numeric
/// condition reuses the guard encoder's sort-aware constant, so an integer
/// threshold on an Int field and a finite real threshold on a Real field are
/// refused or accepted in the same words wherever they enter the theory.
let rec conditionToBooleanExpression (domain: DomainEncoding) (condition: Condition) : Result<BoolExpr, string> =
    let context = domain.Context

    let compare (comparison: DomainExpression.Comparison) (field: string) (value: RealNumber) =
        arithmetic domain field
        |> Result.bind (fun left ->
            numericConstant domain field value
            |> Result.bind (fun right -> compareEncoded context comparison left right))

    match condition with
    | BooleanCondition field -> booleanConstant domain field
    | NumericCondition(field, comparison, value) ->
        match comparison with
        | ComparisonGreaterThan -> compare DomainExpression.GreaterThan field value
        | ComparisonAtLeast -> compare DomainExpression.AtLeast field value
        | ComparisonLessThan -> compare DomainExpression.LessThan field value
        | ComparisonAtMost -> compare DomainExpression.AtMost field value
        | ComparisonEqualTo -> compare DomainExpression.Equals field value
    | Disjunction alternatives ->
        alternatives
        |> List.map (fun clause -> clause |> List.map (conditionToBooleanExpression domain) |> sequenceResults)
        |> sequenceResults
        |> Result.map (fun clauses ->
            clauses
            |> List.map (fun expressions -> context.MkAnd(Array.ofList expressions))
            |> Array.ofList
            |> context.MkOr)

/// Encodes one guard as a Z3 boolean expression. Public because the decision
/// table checker builds completeness and disjointness formulas from the same
/// guards the runtime evaluates; there is one encoding, not a second one.
let refinementGuardToBooleanExpression
    (domain: DomainEncoding)
    (guard: ScaleFile.RefinementGuard)
    : Result<BoolExpr, string> =
    let context = domain.Context

    let compare (comparison: DomainExpression.Comparison) (field: string) (value: RealNumber) =
        arithmetic domain field
        |> Result.bind (fun left ->
            numericConstant domain field value
            |> Result.bind (fun right -> compareEncoded context comparison left right))

    match guard with
    | ScaleFile.GreaterThan(field, value) -> compare DomainExpression.GreaterThan field value
    | ScaleFile.AtLeast(field, value) -> compare DomainExpression.AtLeast field value
    | ScaleFile.LessThan(field, value) -> compare DomainExpression.LessThan field value
    | ScaleFile.AtMost(field, value) -> compare DomainExpression.AtMost field value
    | ScaleFile.IsTrue field -> booleanConstant domain field
    | ScaleFile.IsFalse field -> booleanConstant domain field |> Result.map context.MkNot
    | ScaleFile.NamedGuard(field, name) ->
        Error(
            "The guard '"
            + field
            + " guard "
            + name
            + "' names an image-resident guard metaobject, whose quotation has no SMT encoding (D41)."
        )

/// The precondition as named atoms. The names are human-readable descriptions
/// used to explain an unsat core; they never enter the encoded formula.
let private preconditionAtoms (encoding: Encoding) (obligation: Obligation) : Result<(string * BoolExpr) list, string> =
    let conditions =
        obligation.Conditions
        |> List.mapi (fun index condition ->
            conditionToBooleanExpression encoding.Domain condition
            |> Result.map (fun atom -> "condition " + string (index + 1), atom))

    let refinements =
        obligation.Refinements
        |> List.map (fun refinement ->
            refinementGuardToBooleanExpression encoding.Domain refinement.Guard
            |> Result.map (fun atom -> "refinement " + ScaleFile.refinementGuardText refinement.Guard, atom))

    let latticeConstraints =
        encoding.Domain.Lattice
        |> TypeLattice.constraints
        |> List.filter (fun guard ->
            obligation.Refinements
            |> List.exists (fun refinement ->
                ScaleFile.refinementGuardText refinement.Guard = ScaleFile.refinementGuardText guard)
            |> not)
        |> List.map (fun guard ->
            refinementGuardToBooleanExpression encoding.Domain guard
            |> Result.map (fun atom -> "lattice guard " + ScaleFile.refinementGuardText guard, atom))

    let sortConstraints =
        encoding.Domain.SortConstraints
        |> List.map (fun (description, atom) -> Ok(description, atom))

    let all = conditions @ refinements @ latticeConstraints @ sortConstraints

    match
        all
        |> List.tryPick (function
            | Error message -> Some message
            | Ok _ -> None)
    with
    | Some message -> Error message
    | None ->
        Ok(
            all
            |> List.choose (function
                | Ok atom -> Some atom
                | Error _ -> None)
        )

/// The precondition: the sentence's conditions, the document's guards, and the
/// verification lattice's own narrowing. A guard the document already carries
/// is not conjoined twice: the canonical `.scale` text is the identity.
let private precondition (encoding: Encoding) (obligation: Obligation) : Result<BoolExpr, string> =
    preconditionAtoms encoding obligation
    |> Result.map (fun atoms ->
        let context = encoding.Context

        if List.isEmpty atoms then
            context.MkTrue()
        else
            let expressions = atoms |> List.map snd |> List.toArray
            context.MkAnd(expressions))

let private postcondition (encoding: Encoding) (obligation: Obligation) : BoolExpr =
    match obligation.Pattern with
    | Invariant -> encoding.Context.MkNot encoding.VerbBody
    | _ -> encoding.VerbBody

/// The obligation. A universal property is `pre ∧ ¬post` (unsatisfiable iff it
/// holds); a possibility is `pre ∧ post` (satisfiable iff a witness exists).
let encode (encoding: Encoding) (obligation: Obligation) : Result<BoolExpr, string> =
    precondition encoding obligation
    |> Result.map (fun pre ->
        match obligation.Pattern with
        | Possibility -> encoding.Context.MkAnd([| pre; postcondition encoding obligation |])
        | _ -> encoding.Context.MkAnd([| pre; encoding.Context.MkNot(postcondition encoding obligation) |]))

/// Reads a model back as domain values, not as Z3 expressions, so tests,
/// repair bundles, and decision-table witnesses can consume it without holding
/// the context. A model value the domain cannot represent is an `Error`: a
/// counterexample that does not describe the satisfying assignment is worse
/// than no counterexample.
let private modelOf (domain: DomainEncoding) (z3Model: Microsoft.Z3.Model) : Result<SolverAssignment, string> =
    let rec collect
        (remaining: (string * Encoded) list)
        (accumulated: SolverAssignment)
        : Result<SolverAssignment, string> =
        match remaining with
        | [] -> Ok accumulated
        | (name, encoded) :: rest ->
            let value = z3Model.Eval(toZ3Expression encoded, true)

            match domain.Sorts.[name] with
            | DomainExpression.BooleanType ->
                if value.IsTrue then
                    collect rest (accumulated.Add(name, BooleanValue true))
                elif value.IsFalse then
                    collect rest (accumulated.Add(name, BooleanValue false))
                else
                    collect rest accumulated
            | DomainExpression.IntegerType
            | DomainExpression.NaturalType ->
                match value with
                | :? IntNum as number ->
                    let text = number.ToString()

                    match
                        System.Numerics.BigInteger.TryParse(
                            text,
                            System.Globalization.NumberStyles.AllowLeadingSign,
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    with
                    | true, value ->
                        let largest = System.Numerics.BigInteger(System.Int32.MaxValue)
                        let smallest = System.Numerics.BigInteger(System.Int32.MinValue)

                        if value > largest || value < smallest then
                            Error(
                                "The solver's model for '"
                                + name
                                + "' is "
                                + text
                                + ", which is outside the Int32 range the domain represents."
                            )
                        else
                            collect rest (accumulated.Add(name, IntegerValue(int value)))
                    | _ -> collect rest accumulated
                | _ -> collect rest accumulated
            | DomainExpression.RealType ->
                match value with
                | :? RatNum as number ->
                    let numerator = System.Numerics.BigInteger.Parse(number.Numerator.ToString())
                    let denominator = System.Numerics.BigInteger.Parse(number.Denominator.ToString())
                    let real = float numerator / float denominator
                    collect rest (accumulated.Add(name, RealValue real))
                | :? IntNum as number ->
                    match
                        System.Double.TryParse(
                            number.ToString(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    with
                    | true, real -> collect rest (accumulated.Add(name, RealValue real))
                    | _ -> collect rest accumulated
                | _ -> collect rest accumulated

    collect (domain.Fields |> Map.toList) Map.empty

/// The three-way answer to "is this formula satisfiable?", with a model when it
/// is. `Undecided` is reported, never treated as `Unsatisfiable`.
type Satisfaction =
    | Satisfiable of SolverAssignment
    | Unsatisfiable
    | Undecided of reason: string

/// Asks the solver whether a formula is satisfiable. This is the decision-table
/// checker's primitive: completeness is "the negation is unsatisfiable", and
/// disjointness is "the conjunction is unsatisfiable". It is deliberately
/// separate from `discharge`, which is about the obligation `pre ∧ ¬post`.
let satisfiable (domain: DomainEncoding) (formula: BoolExpr) : Satisfaction =
    use solver = domain.Context.MkSolver()
    solver.Add(formula)

    match solver.Check() with
    | Status.SATISFIABLE ->
        match modelOf domain solver.Model with
        | Ok model -> Satisfiable model
        | Error reason -> Undecided reason
    | Status.UNSATISFIABLE -> Unsatisfiable
    | Status.UNKNOWN -> Undecided solver.ReasonUnknown
    | _ -> Undecided "The solver returned an unexpected status."

/// The plain discharge: encode, assert, and read the verdict. This is the
/// production path, and the model selection it uses is the one the pinned demo
/// output depends on.
let private solve (encoding: Encoding) (obligation: Obligation) : Outcome =
    match encode encoding obligation with
    | Error reason -> Unavailable reason
    | Ok formula ->
        use solver = encoding.Context.MkSolver()
        solver.Add(formula)

        match solver.Check() with
        | Status.UNSATISFIABLE ->
            match obligation.Pattern with
            | Possibility -> NoWitness
            | _ -> Proved
        | Status.SATISFIABLE ->
            match modelOf encoding.Domain solver.Model with
            | Ok model ->
                match obligation.Pattern with
                | Possibility -> Witnessed model
                | _ -> Falsified model
            | Error reason -> Unavailable reason
        | Status.UNKNOWN -> Unknown solver.ReasonUnknown
        | _ -> Unknown "The solver returned an unexpected status."

/// Re-checks the unsatisfiable obligation with labelled assertions, only to name
/// the assumptions Z3 used. `None` means the tracking query did not reproduce
/// the unsat verdict: the core is missing, but the plain verdict still stands.
let private namedCore (encoding: Encoding) (obligation: Obligation) : string list option =
    match preconditionAtoms encoding obligation with
    | Error _ -> None
    | Ok preconditionAtoms ->
        let context = encoding.Context

        let postconditionAtom, postconditionDescription =
            match obligation.Pattern with
            | Possibility -> postcondition encoding obligation, "postcondition"
            | _ -> context.MkNot(postcondition encoding obligation), "negated postcondition"

        let atoms = preconditionAtoms @ [ postconditionDescription, postconditionAtom ]
        use solver = context.MkSolver()

        atoms
        |> List.iteri (fun index (_, atom) ->
            solver.AssertAndTrack(atom, context.MkBoolConst("__scale_assumption_" + string index)))

        match solver.Check() with
        | Status.UNSATISFIABLE ->
            let used =
                solver.UnsatCore
                |> Array.map (fun expression -> expression.FuncDecl.Name.ToString())
                |> Set.ofArray

            atoms
            |> List.indexed
            |> List.choose (fun (index, (description, _)) ->
                if used.Contains("__scale_assumption_" + string index) then
                    Some description
                else
                    None)
            |> Some
        | _ -> None

/// Discharges the obligation and, when the answer is unsatisfiable, returns the
/// named assumptions Z3 used. The verdict comes from `solve`, so this function
/// and `discharge` cannot disagree; the core pass adds evidence, never a second
/// verdict.
let dischargeWithCore (encoding: Encoding) (obligation: Obligation) : Discharge =
    let outcome = solve encoding obligation

    let core =
        match outcome with
        | Proved
        | NoWitness -> namedCore encoding obligation |> Option.defaultValue []
        | _ -> []

    { Outcome = outcome; Core = core }

/// Discharges the obligation. For a universal property `Proved` is an `unsat`
/// answer and `Falsified` carries a counterexample; for a possibility
/// `Witnessed` carries a witness and `NoWitness` is the `unsat` answer. The
/// other cases are honest failures to decide.
let discharge (encoding: Encoding) (obligation: Obligation) : Outcome = solve encoding obligation

/// Evaluates the encoded implementation body at a **complete** concrete
/// assignment. The assignment must name every field in the encoding exactly
/// once, and each value must match its field's sort: a partial, duplicated, or
/// ill-sorted assignment is `Error`, not a guess. A boolean value on a numeric
/// field would otherwise reach Z3 as a sort mismatch — an exception, not a
/// verdict.
let evaluateBody (encoding: Encoding) (values: (string * ScaleFile.FieldValue) list) : Result<bool, string> =
    let context = encoding.Context
    let supplied = values |> List.map fst |> Set.ofList

    let missing =
        encoding.Fields
        |> Map.toList
        |> List.map fst
        |> List.filter (fun name -> not (supplied.Contains name))

    let duplicated =
        values
        |> List.map fst
        |> List.countBy id
        |> List.exists (fun (_, count) -> count > 1)

    if not (List.isEmpty missing) then
        Error("The assignment is missing: " + String.concat ", " missing)
    elif duplicated then
        Error "The assignment names the same field more than once."
    else
        use solver = context.MkSolver()
        let mutable failure: string option = None

        for (name, value) in values do
            if failure.IsNone then
                match encoding.Fields.TryFind name with
                | None -> failure <- Some("The domain has no field named '" + name + "'.")
                | Some encoded ->
                    let constant = toZ3Expression encoded

                    match value with
                    | ScaleFile.BooleanValue flag ->
                        match encoding.Sorts.[name] with
                        | DomainExpression.BooleanType ->
                            solver.Add(context.MkEq(constant, context.MkBool flag :> Expr))
                        | sort when isNumericSort sort ->
                            failure <- Some("The field '" + name + "' is numeric; it cannot take the boolean value.")
                    | ScaleFile.NumericValue number ->
                        match encoding.Sorts.[name] with
                        | DomainExpression.IntegerType ->
                            match DomainExpression.integerValue number with
                            | Ok n -> solver.Add(context.MkEq(constant, context.MkInt n :> Expr))
                            | Error reason -> failure <- Some reason
                        | DomainExpression.NaturalType ->
                            match DomainExpression.naturalValue number with
                            | Ok n -> solver.Add(context.MkEq(constant, context.MkInt n :> Expr))
                            | Error reason -> failure <- Some reason
                        | DomainExpression.RealType ->
                            match DomainExpression.realValue number with
                            | Ok n ->
                                let literal =
                                    context.MkReal(n.ToString("R", System.Globalization.CultureInfo.InvariantCulture))

                                solver.Add(context.MkEq(constant, literal :> Expr))
                            | Error reason -> failure <- Some reason
                        | DomainExpression.BooleanType ->
                            failure <- Some("The field '" + name + "' is boolean; it cannot take the numeric value.")

        match failure with
        | Some reason -> Error reason
        | None ->
            solver.Add(encoding.VerbBody)

            match solver.Check() with
            | Status.SATISFIABLE -> Ok true
            | Status.UNSATISFIABLE -> Ok false
            | Status.UNKNOWN -> Error("The solver could not evaluate the body: " + solver.ReasonUnknown)
            | _ -> Error "The solver returned an unexpected status."

/// The SMT-LIB text is Z3's printer's output over the same AST, not a
/// hand-written second rendering.
let obligationText (encoding: Encoding) (obligation: Obligation) : Result<string, string> =
    encode encoding obligation
    |> Result.map (fun formula ->
        let hasReal =
            encoding.Sorts
            |> Map.exists (fun _ sort -> sort = DomainExpression.RealType)

        let hasInteger =
            encoding.Sorts
            |> Map.exists (fun _ sort ->
                sort = DomainExpression.IntegerType || sort = DomainExpression.NaturalType)

        let logic =
            if hasReal && hasInteger then "QF_LIRA"
            elif hasReal then "QF_LRA"
            else "QF_LIA"

        let builder = System.Text.StringBuilder()
        builder.AppendLine("(set-logic " + logic + ")") |> ignore

        for (name, sort) in encoding.Sorts |> Map.toList do
            let sortText =
                match sort with
                | DomainExpression.BooleanType -> "Bool"
                | DomainExpression.RealType -> "Real"
                | DomainExpression.IntegerType
                | DomainExpression.NaturalType -> "Int"

            builder.AppendLine(sprintf "(declare-fun %s () %s)" name sortText) |> ignore

        builder.AppendLine("(assert " + formula.ToString() + ")") |> ignore
        builder.AppendLine("(check-sat)") |> ignore
        builder.AppendLine("(get-model)") |> ignore
        builder.ToString())

/// Is the precondition satisfiable? `Ok false` means the property is vacuous:
/// no input satisfies it. An undecided query is an `Error`, never a `false` —
/// "the solver could not decide" is not "the property is vacuous".
let satisfiablePre (encoding: Encoding) (obligation: Obligation) : Result<bool, string> =
    precondition encoding obligation
    |> Result.bind (fun pre ->
        use solver = encoding.Context.MkSolver()
        solver.Add(pre)

        match solver.Check() with
        | Status.SATISFIABLE -> Ok true
        | Status.UNSATISFIABLE -> Ok false
        | Status.UNKNOWN ->
            Error(
                "The solver could not decide whether the precondition is satisfiable: "
                + solver.ReasonUnknown
            )
        | _ -> Error "The solver returned an unexpected status.")

/// Is the postcondition true independently of the input? `Ok true` means the
/// property is suspicious: it says nothing about the implementation. An
/// undecided query is an `Error`, never a verdict.
let tautologicalPost (encoding: Encoding) (obligation: Obligation) : Result<bool, string> =
    use solver = encoding.Context.MkSolver()
    solver.Add(encoding.Context.MkNot(postcondition encoding obligation))

    match solver.Check() with
    | Status.UNSATISFIABLE -> Ok true
    | Status.SATISFIABLE -> Ok false
    | Status.UNKNOWN ->
        Error(
            "The solver could not decide whether the postcondition is tautological: "
            + solver.ReasonUnknown
        )
    | _ -> Error "The solver returned an unexpected status."

/// The native solver's version, recorded in refinement provenance. This is the
/// loaded library's version (not the NuGet wrapper's package version).
let solverVersion: string = Microsoft.Z3.Version.FullVersion

/// A stable rendering of a model's assignments, e.g. `alive=true, health=1`.
let describeModel (model: SolverAssignment) : string =
    let valueText value =
        match value with
        | BooleanValue flag -> if flag then "true" else "false"
        | IntegerValue number -> string number
        | RealValue number -> number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

    model
    |> Map.toList
    |> List.map (fun (name, value) -> name + "=" + valueText value)
    |> String.concat ", "

/// Converts a solver model to the domain's field-value vocabulary.
let fieldValues (model: SolverAssignment) : (string * ScaleFile.FieldValue) list =
    model
    |> Map.toList
    |> List.map (fun (name, value) ->
        match value with
        | BooleanValue flag -> name, ScaleFile.BooleanValue flag
        | IntegerValue number -> name, ScaleFile.NumericValue(double number)
        | RealValue number -> name, ScaleFile.NumericValue number)

/// A stable, human-readable rendering of an outcome.
let describeOutcome (outcome: Outcome) : string =
    match outcome with
    | Proved -> "Proved"
    | Falsified model -> "Falsified (" + describeModel model + ")"
    | Witnessed model -> "Witnessed (" + describeModel model + ")"
    | NoWitness -> "No witness"
    | Unknown reason -> "Unknown (" + reason + ")"
    | Unavailable reason -> "Unavailable (" + reason + ")"
