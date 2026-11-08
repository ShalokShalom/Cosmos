module DomainExpression

// A tiny, verifiable implementation language: one body, two backends (D10).
//
// The interpreter below is what Hedgehog executes; `smt.fs` encodes the same
// value for Z3. Keeping the implementation in one place is what makes the
// differential test meaningful: the two backends are checked against each
// other, not against a hand-written second copy.

open Types

/// The sort of a domain field. Real is the numeric carrier; Integer and
/// Natural are subsets of it. The SMT encoding keeps Integer/Natural on the
/// exact Int sort and Real on Real, choosing QF_LIA, QF_LRA, or QF_LIRA from
/// the sorts present, so existing integer artifacts stay byte-identical.
type FieldType =
    | BooleanType
    | RealType
    | IntegerType
    | NaturalType

/// True for the three numeric sorts. The lattice, the emitter, and the
/// fixed-specification gate share this predicate, so a sort cannot be numeric
/// in one reader and boolean in another.
let isNumeric (fieldType: FieldType) : bool =
    match fieldType with
    | BooleanType -> false
    | RealType
    | IntegerType
    | NaturalType -> true

/// Integer and Natural fields are encoded on the exact Int sort, so their
/// numeric literal must be an integer in Int32 range. The same predicate guards
/// implementation constants, guard thresholds, and the verification lattice's
/// finite coordinates: a value the solver cannot represent must be refused
/// before a generator can sample it.
let integerValue (value: RealNumber) : Result<int, string> =
    if
        System.Double.IsFinite value
        && value = System.Math.Floor value
        && value >= float System.Int32.MinValue
        && value <= float System.Int32.MaxValue
    then
        Ok(int value)
    else
        let rendered =
            value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

        Error($"The threshold {rendered} is not an integer in the encoded theory.")

/// A finite real. Z3's Real sort is exact rational arithmetic; the caller's
/// `double` is rendered in round-trip form for the solver.
let realValue (value: RealNumber) : Result<RealNumber, string> =
    if System.Double.IsFinite value then
        Ok value
    else
        let rendered =
            value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

        Error($"The threshold {rendered} is not a finite real.")

/// A natural: an integer at least zero.
let naturalValue (value: RealNumber) : Result<int, string> =
    integerValue value
    |> Result.bind (fun number ->
        if number >= 0 then
            Ok number
        else
            let rendered =
                value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

            Error($"The threshold {rendered} is not a natural number."))

/// The comparison operators the fragment understands. The names match
/// `ScaleFile.RefinementGuard`, so the two vocabularies read the same.
type Comparison =
    | GreaterThan
    | AtLeast
    | LessThan
    | AtMost
    | Equals

/// The implementation fragment. Deliberately small: fields, constants,
/// comparisons, and boolean structure. Anything outside it is `Unavailable`
/// to the verifier rather than silently approximated.
type Expression =
    | BooleanField of string
    | IntegerField of string
    | BooleanConstant of bool
    | IntegerConstant of int
    | Compare of Comparison * Expression * Expression
    | And of Expression * Expression
    | Or of Expression * Expression
    | Not of Expression

/// A stable, S-expression rendering of the body, for the repair bundle's
/// `implementation` field. This is a deliberate format, not the compiler's
/// `%A` output, so a bundle written today reads the same on a future compiler.
/// It is also close to SMT-LIB, so a reader can compare it with the obligation.
let rec render (expression: Expression) : string =
    let comparisonText =
        function
        | GreaterThan -> ">"
        | AtLeast -> ">="
        | LessThan -> "<"
        | AtMost -> "<="
        | Equals -> "="

    match expression with
    | BooleanField name -> name
    | IntegerField name -> name
    | BooleanConstant value -> if value then "true" else "false"
    | IntegerConstant value -> string value
    | Compare(comparison, left, right) -> "(" + comparisonText comparison + " " + render left + " " + render right + ")"
    | And(left, right) -> "(and " + render left + " " + render right + ")"
    | Or(left, right) -> "(or " + render left + " " + render right + ")"
    | Not inner -> "(not " + render inner + ")"

/// Evaluates an expression against a subject, using the domain's field
/// projection. A sort mismatch is a named `Error`, not a silent `false` and
/// not a raise: the domain binding that shipped an ill-sorted body gets a
/// reason it can report, the same as every other refusal in the core.
let rec interpret
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (subject: 'T)
    (expression: Expression)
    : Result<ScaleFile.FieldValue, string> =
    let boolean (inner: Expression) : Result<bool, string> =
        interpret getField subject inner
        |> Result.bind (function
            | ScaleFile.BooleanValue value -> Ok value
            | ScaleFile.NumericValue _ -> Error "Expected a boolean expression.")

    let numeric (inner: Expression) : Result<RealNumber, string> =
        interpret getField subject inner
        |> Result.bind (function
            | ScaleFile.NumericValue value -> Ok value
            | ScaleFile.BooleanValue _ -> Error "Expected a numeric expression.")

    match expression with
    | BooleanField field ->
        match getField subject field with
        | ScaleFile.BooleanValue value -> Ok(ScaleFile.BooleanValue value)
        | ScaleFile.NumericValue _ -> Error("Expected a boolean field: " + field)
    | IntegerField field ->
        match getField subject field with
        | ScaleFile.NumericValue value -> Ok(ScaleFile.NumericValue value)
        | ScaleFile.BooleanValue _ -> Error("Expected a numeric field: " + field)
    | BooleanConstant value -> Ok(ScaleFile.BooleanValue value)
    | IntegerConstant value -> Ok(ScaleFile.NumericValue(double value))
    | Compare(comparison, left, right) ->
        match numeric left, numeric right with
        | Ok l, Ok r ->
            let result =
                match comparison with
                | GreaterThan -> l > r
                | AtLeast -> l >= r
                | LessThan -> l < r
                | AtMost -> l <= r
                | Equals -> l = r

            Ok(ScaleFile.BooleanValue result)
        | Error reason, _ -> Error reason
        | _, Error reason -> Error reason
    | And(left, right) ->
        match boolean left, boolean right with
        | Ok l, Ok r -> Ok(ScaleFile.BooleanValue(l && r))
        | Error reason, _ -> Error reason
        | _, Error reason -> Error reason
    | Or(left, right) ->
        match boolean left, boolean right with
        | Ok l, Ok r -> Ok(ScaleFile.BooleanValue(l || r))
        | Error reason, _ -> Error reason
        | _, Error reason -> Error reason
    | Not inner -> boolean inner |> Result.map (fun value -> ScaleFile.BooleanValue(not value))

/// Evaluates a boolean expression, reporting a sort mismatch as a named reason.
let interpretBoolean
    (getField: 'T -> string -> ScaleFile.FieldValue)
    (subject: 'T)
    (expression: Expression)
    : Result<bool, string> =
    interpret getField subject expression
    |> Result.bind (function
        | ScaleFile.BooleanValue value -> Ok value
        | ScaleFile.NumericValue _ -> Error "Expected a boolean result.")
