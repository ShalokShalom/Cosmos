module Differential

// ============================================================
// D124: a shared corpus that runs each token program through the token
// interpreter and the lowered abstraction, then compares the outcome.
//
// A success case compares the scalar result. A failure case compares the
// failure family, not the prose: the two engines intentionally word their
// diagnostics differently. The corpus is test support, so it is classified
// with the test harness by the line-count report.
// ============================================================

open Types

/// The comparable part of a failure. Both engines classify into the same set,
/// so a corpus case can demand a family without depending on exact wording.
type FailureKind =
    | DivisionByZero
    | Underflow
    | UnknownWord
    | OtherFailure

type Expectation =
    | Value of expected: double
    | Text of expected: string
    | Failure of family: FailureKind

type Case =
    {
        Name: string
        Program: string list
        Expectation: Expectation
    }

/// A second source form for the same behavior. The token program is the
/// interpreter source; the expression is the general-IR source.
type ExpressionCase =
    {
        Name: string
        Expression: ExpressionLogic.Expression
        Program: string list
        Expectation: Expectation
    }

type Outcome =
    | Produced of value: double
    | ProducedText of value: string
    | Failed of family: FailureKind * reason: string

type Disagreement =
    {
        Name: string
        Expected: string
        Interpreter: string
        Lowered: string
        Expression: string
    }

let classifyFailure (reason: string) : FailureKind =
    if reason.Contains "divide" || reason.Contains "division" then
        DivisionByZero
    elif reason.Contains "underflow" || reason.Contains "operand" then
        Underflow
    elif reason.Contains "unknown word" then
        UnknownWord
    else
        OtherFailure

let describeFailure (family: FailureKind) : string =
    match family with
    | DivisionByZero -> "division by zero"
    | Underflow -> "underflow"
    | UnknownWord -> "unknown word"
    | OtherFailure -> "other failure"

let describeExpectation (expectation: Expectation) : string =
    match expectation with
    | Value expected -> "value " + expected.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
    | Text expected -> "text " + expected
    | Failure family -> "failure " + describeFailure family

let describeOutcome (outcome: Outcome) : string =
    match outcome with
    | Produced value -> "value " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
    | ProducedText value -> "text " + value
    | Failed(family, reason) -> "failure " + describeFailure family + " (" + reason + ")"

let topValue (state: EvaluationState) : Outcome =
    match Stack.popResolved state.Store state.Stack with
    | None -> Failed(OtherFailure, "empty stack")
    | Some(tensor, _) ->
        match ScalarRead.readScalar tensor with
        | Ok value -> Produced value
        | Error _ when Tensor.rank tensor = 1 -> ProducedText(ScalarRead.readText tensor)
        | Error reason -> Failed(OtherFailure, reason)

let runInterpreter (seed: EvaluationState) (program: string list) : Outcome =
    match Evaluator.run { seed with Program = EvaluatorProgram.ofTokensOrFail program } with
    | Ok final -> topValue final
    | Error diagnostic ->
        let reason = describeError diagnostic.Error
        Failed(classifyFailure reason, reason)

let runLowered (seed: EvaluationState) (program: string list) : Outcome =
    let instructions = LoweredInstructions.lowerTokens program
    let state = EvaluatorKernelBridge.toKernel { seed with Program = [] }

    match LoweredAbstraction.run EvaluatorKernelBridge.executeWord instructions state with
    | Ok final -> topValue (EvaluatorKernelBridge.fromKernel final)
    | Error(LoweredAbstraction.LoweredFailure reason) -> Failed(classifyFailure reason, reason)
    | Error(LoweredAbstraction.HostFailure reason) -> Failed(classifyFailure reason, reason)

let runExpressionLowered (seed: EvaluationState) (expression: ExpressionLogic.Expression) : Outcome =
    match EvaluatorLowering.lowerExpression expression with
    | Error refusal ->
        let reason = EvaluatorLowering.describeRefusal refusal
        Failed(classifyFailure reason, reason)
    | Ok program ->
        let units =
            program.Units
            |> List.map (fun unit -> unit.Name, unit.Instructions)
            |> Map.ofList

        let state = EvaluatorKernelBridge.toKernel { seed with Program = [] }

        match
            LoweredAbstraction.runProgram
                units
                EvaluatorKernelBridge.executeWord
                program.Instructions
                state
        with
        | Ok final -> topValue (EvaluatorKernelBridge.fromKernel final)
        | Error(LoweredAbstraction.LoweredFailure reason) -> Failed(classifyFailure reason, reason)
        | Error(LoweredAbstraction.HostFailure reason) -> Failed(classifyFailure reason, reason)

let matches (expectation: Expectation) (outcome: Outcome) : bool =
    match expectation, outcome with
    | Value expected, Produced actual -> expected = actual
    | Text expected, ProducedText actual -> expected = actual
    | Failure expected, Failed(actual, _) -> expected = actual
    | _ -> false

let check (seed: EvaluationState) (case: Case) : Disagreement option =
    let interpreted = runInterpreter seed case.Program
    let lowered = runLowered seed case.Program

    if matches case.Expectation interpreted && matches case.Expectation lowered then
        None
    else
        Some
            {
                Name = case.Name
                Expected = describeExpectation case.Expectation
                Interpreter = describeOutcome interpreted
                Lowered = describeOutcome lowered
                Expression = "not run"
            }

let checkExpression (seed: EvaluationState) (case: ExpressionCase) : Disagreement option =
    let interpreted = runInterpreter seed case.Program
    let expression = runExpressionLowered seed case.Expression

    if matches case.Expectation interpreted && matches case.Expectation expression then
        None
    else
        Some
            {
                Name = case.Name
                Expected = describeExpectation case.Expectation
                Interpreter = describeOutcome interpreted
                Lowered = "not run"
                Expression = describeOutcome expression
            }

let describeDisagreement (disagreement: Disagreement) : string =
    disagreement.Name
    + " (expected "
    + disagreement.Expected
    + "; interpreter "
    + disagreement.Interpreter
    + "; lowered "
    + disagreement.Lowered
    + "; expression "
    + disagreement.Expression
    + ")"

let cases : Case list =
    [
        { Name = "add"; Program = [ "2"; "3"; "add" ]; Expectation = Value 5.0 }
        { Name = "subtract"; Program = [ "10"; "4"; "subtract" ]; Expectation = Value 6.0 }
        { Name = "multiply"; Program = [ "3"; "4"; "multiply" ]; Expectation = Value 12.0 }
        { Name = "divide"; Program = [ "8"; "2"; "divide" ]; Expectation = Value 4.0 }
        { Name = "lessThan"; Program = [ "1"; "2"; "lessThan" ]; Expectation = Value 1.0 }
        { Name = "greaterThan"; Program = [ "2"; "1"; "greaterThan" ]; Expectation = Value 1.0 }
        { Name = "equals-true"; Program = [ "5"; "5"; "equals" ]; Expectation = Value 1.0 }
        { Name = "equals-false"; Program = [ "5"; "4"; "equals" ]; Expectation = Value 0.0 }
        { Name = "duplicate"; Program = [ "1"; "duplicate"; "add" ]; Expectation = Value 2.0 }
        { Name = "swap"; Program = [ "1"; "2"; "swap"; "subtract" ]; Expectation = Value 1.0 }
        { Name = "over"; Program = [ "1"; "2"; "over"; "add" ]; Expectation = Value 3.0 }
        { Name = "rotate"; Program = [ "1"; "2"; "3"; "rotate" ]; Expectation = Value 1.0 }
        { Name = "drop"; Program = [ "1"; "2"; "drop" ]; Expectation = Value 1.0 }
        { Name = "chain"; Program = [ "1"; "2"; "add"; "3"; "subtract" ]; Expectation = Value 0.0 }
        { Name = "divide-by-zero"; Program = [ "1"; "0"; "divide" ]; Expectation = Failure DivisionByZero }
        { Name = "add-underflow"; Program = [ "add" ]; Expectation = Failure Underflow }
        { Name = "subtract-underflow"; Program = [ "1"; "subtract" ]; Expectation = Failure Underflow }
        { Name = "unknown-word"; Program = [ "unknown-word" ]; Expectation = Failure UnknownWord }
    ]

let private number (value: double) =
    ExpressionLogic.Literal(ExpressionLogic.ExpressionLiteral.NumberLiteral value)

let private call (name: string) (arguments: ExpressionLogic.Expression list) =
    ExpressionLogic.Call(ExpressionLogic.Word name, arguments)

let expressionCases : ExpressionCase list =
    [
        {
            Name = "expression-add"
            Expression = call "add" [ number 2.0; number 3.0 ]
            Program = [ "2"; "3"; "add" ]
            Expectation = Value 5.0
        }
        {
            Name = "expression-subtract"
            Expression = call "subtract" [ number 10.0; number 4.0 ]
            Program = [ "10"; "4"; "subtract" ]
            Expectation = Value 6.0
        }
        {
            Name = "expression-multiply"
            Expression = call "multiply" [ number 3.0; number 4.0 ]
            Program = [ "3"; "4"; "multiply" ]
            Expectation = Value 12.0
        }
        {
            Name = "expression-divide"
            Expression = call "divide" [ number 8.0; number 2.0 ]
            Program = [ "8"; "2"; "divide" ]
            Expectation = Value 4.0
        }
        {
            Name = "expression-duplicate"
            Expression = call "duplicate" [ number 1.0 ]
            Program = [ "1"; "duplicate" ]
            Expectation = Value 1.0
        }
        {
            Name = "expression-swap"
            Expression = call "swap" [ number 1.0; number 2.0 ]
            Program = [ "1"; "2"; "swap" ]
            Expectation = Value 1.0
        }
        {
            Name = "expression-over"
            Expression = call "over" [ number 1.0; number 2.0 ]
            Program = [ "1"; "2"; "over" ]
            Expectation = Value 1.0
        }
        {
            Name = "expression-let"
            Expression =
                ExpressionLogic.Let(
                    "x",
                    number 7.0,
                    ExpressionLogic.Word "x"
                )
            Program = [ "7"; "\"x\""; "let"; "x" ]
            Expectation = Value 7.0
        }
        {
            Name = "expression-equals"
            Expression = call "equals" [ number 5.0; number 4.0 ]
            Program = [ "5"; "4"; "equals" ]
            Expectation = Value 0.0
        }
        {
            Name = "expression-pipeline"
            Expression =
                ExpressionLogic.Pipeline(
                    number 5.0,
                    call "add" [ number 2.0 ],
                    ExpressionLogic.PipelineStyle.LastArgument
                )
            Program = [ "2"; "5"; "add" ]
            Expectation = Value 7.0
        }
        {
            Name = "expression-block"
            Expression = ExpressionLogic.Block([ number 1.0; number 2.0; call "add" [] ])
            Program = [ "1"; "2"; "add" ]
            Expectation = Value 3.0
        }
        {
            Name = "expression-quotation"
            Expression = ExpressionLogic.Quotation([ number 1.0; number 2.0; call "add" [] ])
            Program = [ "1"; "2"; "add" ]
            Expectation = Value 3.0
        }
    ]

/// W1: how a `Term` constructor relates to the instruction set. The match is
/// exhaustive, so a constructor added to `SymbolicAbstraction.Term` fails to
/// compile here until it is classified.
type TermCounterpart =
    | Instruction of name: string
    | UnitOnly of reason: string

let counterpartOf (term: SymbolicAbstraction.Term) : TermCounterpart =
    match term with
    | SymbolicAbstraction.LiteralNumber _ -> Instruction "LiteralNumber"
    | SymbolicAbstraction.LiteralText _ -> Instruction "LiteralText"
    | SymbolicAbstraction.LiteralAtom _ ->
        UnitOnly "an atom is a tagged text literal; the instruction set has no atom opcode"
    | SymbolicAbstraction.Word _ -> Instruction "CallWord"
    | SymbolicAbstraction.Quote _ ->
        UnitOnly "a quotation body becomes a unit in `LoweredProgram`, not one instruction"
    | SymbolicAbstraction.Branch _ -> Instruction "JumpIfFalse"
    | SymbolicAbstraction.SlotGet _ -> Instruction "PushSlot"
    | SymbolicAbstraction.SlotSet _ -> Instruction "StoreSlot"
    | SymbolicAbstraction.MakeClosure _ -> Instruction "MakeClosure"
    | SymbolicAbstraction.CallClosure -> Instruction "CallClosure"
    | SymbolicAbstraction.Fail _ -> Instruction "Fail"

/// One sample per constructor, with the classification of that sample.
let termLedger : (string * TermCounterpart) list =
    let classify (name: string) (term: SymbolicAbstraction.Term) = name, counterpartOf term

    [
        classify "LiteralNumber" (SymbolicAbstraction.LiteralNumber 1.0)
        classify "LiteralText" (SymbolicAbstraction.LiteralText "text")
        classify "LiteralAtom" (SymbolicAbstraction.LiteralAtom "red")
        classify "Word" (SymbolicAbstraction.Word "add")
        classify "Quote" (SymbolicAbstraction.Quote [])
        classify
            "Branch"
            (SymbolicAbstraction.Branch(
                SymbolicAbstraction.Word "condition",
                SymbolicAbstraction.Word "then",
                SymbolicAbstraction.Word "otherwise"
            ))
        classify "SlotGet" (SymbolicAbstraction.SlotGet 0)
        classify "SlotSet" (SymbolicAbstraction.SlotSet 0)
        classify "MakeClosure" (SymbolicAbstraction.MakeClosure("closure", [], 0))
        classify "CallClosure" SymbolicAbstraction.CallClosure
        classify "Fail" (SymbolicAbstraction.Fail "reason")
    ]

/// The instruction opcodes the ledger may name. A rename in the instruction set
/// has to be answered here instead of drifting.
let instructionNames =
    set
        [
            "LiteralNumber"
            "LiteralText"
            "CallWord"
            "JumpIfFalse"
            "PushSlot"
            "StoreSlot"
            "MakeClosure"
            "CallClosure"
            "Fail"
        ]

let describeCounterpart (counterpart: TermCounterpart) =
    match counterpart with
    | Instruction name -> "instruction " + name
    | UnitOnly reason -> "unit-only: " + reason

/// W1: the constructor names of the semantic form, in declaration order. A
/// constructor added to `Expression` and not named here is caught by the
/// coverage test, because `namesIn` refuses to compile until it handles it.
let constructorNames =
    [
        "Literal"
        "Word"
        "Call"
        "Partial"
        "Hole"
        "Quotation"
        "Block"
        "Pipeline"
        "ReversePipeline"
        "Let"
        "Lambda"
        "If"
        "PatternMatch"
        "DeclareKind"
        "DeclarePredicate"
        "Condition"
        "Restart"
        "Generic"
        "Message"
        "Fail"
    ]

/// Every constructor name in an expression, including its children. Cheap on
/// purpose: one exhaustive match, no visitor machinery.
let rec namesIn (expression: ExpressionLogic.Expression) : string list =
    let children expressions = expressions |> List.collect namesIn

    match expression with
    | ExpressionLogic.Literal _ -> [ "Literal" ]
    | ExpressionLogic.Word _ -> [ "Word" ]
    | ExpressionLogic.Call(target, arguments) -> "Call" :: namesIn target @ children arguments
    | ExpressionLogic.Partial(target, arguments) -> "Partial" :: namesIn target @ children arguments
    | ExpressionLogic.Hole -> [ "Hole" ]
    | ExpressionLogic.Quotation body -> "Quotation" :: children body
    | ExpressionLogic.Block body -> "Block" :: children body
    | ExpressionLogic.Pipeline(value, stage, _) -> "Pipeline" :: namesIn value @ namesIn stage
    | ExpressionLogic.ReversePipeline(target, value) -> "ReversePipeline" :: namesIn target @ namesIn value
    | ExpressionLogic.Let(_, value, body) -> "Let" :: namesIn value @ namesIn body
    | ExpressionLogic.Lambda(_, body) -> "Lambda" :: namesIn body
    | ExpressionLogic.If(condition, thenBranch, elseBranch) ->
        "If" :: namesIn condition @ namesIn thenBranch @ namesIn elseBranch
    | ExpressionLogic.PatternMatch(subject, clauses) ->
        "PatternMatch"
        :: namesIn subject
        @ (clauses |> List.collect (fun clause -> children clause.Body))
    | ExpressionLogic.DeclareKind _ -> [ "DeclareKind" ]
    | ExpressionLogic.DeclarePredicate _ -> [ "DeclarePredicate" ]
    | ExpressionLogic.Condition(_, message) -> "Condition" :: namesIn message
    | ExpressionLogic.Restart(_, handler) -> "Restart" :: namesIn handler
    | ExpressionLogic.Generic(_, _, methods) -> "Generic" :: children methods
    | ExpressionLogic.Message(_, fields) -> "Message" :: children fields
    | ExpressionLogic.Fail _ -> [ "Fail" ]

/// W1: an expression-level row with no token oracle. It proves the lowering
/// produces the documented value; agreement with a second engine is impossible
/// because these shapes have no token spelling.
type LoweringCase =
    {
        Name: string
        Expression: ExpressionLogic.Expression
        Expectation: Expectation
    }

let loweringCases : LoweringCase list =
    [
        {
            Name = "lowering-lambda-applied"
            Expression =
                ExpressionLogic.Call(
                    ExpressionLogic.Lambda([ "x" ], ExpressionLogic.Word "x"),
                    [ number 5.0 ]
                )
            Expectation = Value 5.0
        }
        {
            Name = "lowering-if-true-branch"
            Expression =
                ExpressionLogic.If(call "lessThan" [ number 1.0; number 2.0 ], number 10.0, number 20.0)
            Expectation = Value 10.0
        }
        {
            Name = "lowering-partial-application"
            Expression =
                ExpressionLogic.Let(
                    "partial",
                    ExpressionLogic.Partial(
                        ExpressionLogic.Word "subtract",
                        [ ExpressionLogic.Hole; number 10.0 ]
                    ),
                    ExpressionLogic.Call(ExpressionLogic.Word "partial", [ number 3.0 ])
                )
            Expectation = Value -7.0
        }
        {
            Name = "lowering-reverse-pipeline"
            Expression = ExpressionLogic.ReversePipeline(call "add" [ number 2.0 ], number 5.0)
            Expectation = Value 7.0
        }
        {
            Name = "lowering-pattern-match"
            Expression =
                ExpressionLogic.PatternMatch(
                    number 5.0,
                    [
                        {
                            Pattern = PatternLogic.Wildcard
                            Guard = None
                            Body = [ number 7.0 ]
                        }
                    ]
                )
            Expectation = Value 7.0
        }
        {
            Name = "lowering-fail"
            Expression = ExpressionLogic.Fail "deliberate"
            Expectation = Failure OtherFailure
        }
        {
            Name = "lowering-message"
            Expression = ExpressionLogic.Message("kind", [])
            Expectation = Text "kind"
        }
    ]

let checkLowering (seed: EvaluationState) (case: LoweringCase) : Disagreement option =
    let lowered = runExpressionLowered seed case.Expression

    if matches case.Expectation lowered then
        None
    else
        Some
            {
                Name = case.Name
                Expected = describeExpectation case.Expectation
                Interpreter = "(no token oracle)"
                Lowered = describeOutcome lowered
                Expression = describeOutcome lowered
            }

/// The constructors the expression corpus actually reaches.
let coveredConstructors () =
    (expressionCases |> List.collect (fun case -> namesIn case.Expression))
    @ (loweringCases |> List.collect (fun case -> namesIn case.Expression))
    |> Set.ofList

/// W1: the constructors the lowering refuses by design. `Constructor` is the
/// `Expression` case name the coverage ledger uses; `Node` is the name the
/// refusal carries. A decline is a first-class outcome: the interpreter handles
/// these shapes, the lowered subset says so instead of failing at run time.
type RefusalCase =
    {
        Constructor: string
        Node: string
        Expression: ExpressionLogic.Expression
    }

let kindSample : PatternLogic.PatternKind = { Name = "kind"; Arity = 0 }

let refusalCases : RefusalCase list =
    [
        {
            Constructor = "Hole"
            Node = "hole"
            Expression = ExpressionLogic.Hole
        }
        {
            Constructor = "DeclareKind"
            Node = "declare-kind"
            Expression = ExpressionLogic.DeclareKind kindSample
        }
        {
            Constructor = "DeclarePredicate"
            Node = "declare-predicate"
            Expression = ExpressionLogic.DeclarePredicate("predicate", 1)
        }
        {
            Constructor = "Condition"
            Node = "condition"
            Expression = ExpressionLogic.Condition("condition", number 1.0)
        }
        {
            Constructor = "Restart"
            Node = "restart"
            Expression = ExpressionLogic.Restart("restart", number 1.0)
        }
        {
            Constructor = "Generic"
            Node = "generic"
            Expression = ExpressionLogic.Generic("generic", 0, [])
        }
    ]

/// The refusal a constructor produces, or `None` when it lowers.
let refusalName (expression: ExpressionLogic.Expression) =
    match EvaluatorLowering.lowerExpression expression with
    | Error refusal -> Some(EvaluatorLowering.describeRefusal refusal)
    | Ok _ -> None

// Extension inventory for this module:
// - a corpus row per constructor, so `coveredConstructors` climbs toward
//   `constructorNames` and the uncovered count reaches zero;
// - a `Term`-side corpus once an `Expression -> Term` view exists, so the
//   ledger's `UnitOnly` entries can shrink;
// - a generated coverage table under `docs/generated/` so the gap is readable
//   without running tests;
// - the same two functions for `PatternLogic` and `PackageLogic` when their
//   dialects grow rows.
