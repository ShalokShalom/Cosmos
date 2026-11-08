module ExpressionLogic

// ============================================================
// One semantic expression IR, with three documented profiles:
//   Common: literals, words, calls, quotations, blocks, failure
//   Action: pipelines and imperative execution forms
//   Question: pattern declarations and question clauses
//   Logic: definitions, lambdas, control, generics, messages
// The profile comments are a reading guide. `profilesOf` is the executable
// classification, and the tests keep it exhaustive.
// ============================================================

open Types
open PatternLogic

/// A profile of the one semantic IR. A node may belong to several profiles.
type ExpressionProfile =
    | CommonProfile
    | ActionProfile
    | QuestionProfile
    | LogicProfile

/// A literal in the general expression IR.
type ExpressionLiteral =
    | NumberLiteral of value: RealNumber
    | TextLiteral of value: string
    | AtomLiteral of value: string

/// Which argument position a pipeline inserts. The F\# port uses the last
/// argument; the Elixir style is recorded for a later surface.
type PipelineStyle =
    | LastArgument
    | FirstArgument

/// One clause of a general pattern match. The body is an expression list; the
/// pattern and guard are the same nodes the pattern sub-language owns.
type ExpressionClause =
    {
        Pattern: Pattern
        Guard: PatternGuard option
        Body: Expression list
    }

/// The general expression IR. It carries code, not values: values remain
/// tensors and text entries in the image.
and Expression =
    // --- Common semantic core ---
    | Literal of ExpressionLiteral
    | Word of name: string
    | Call of target: Expression * arguments: Expression list
    | Partial of target: Expression * arguments: Expression list
    | Hole
    | Quotation of body: Expression list
    | Block of body: Expression list
    // --- Action profile ---
    | Pipeline of value: Expression * stage: Expression * style: PipelineStyle
    | ReversePipeline of target: Expression * value: Expression
    // --- Action and Logic profiles ---
    | Let of name: string * value: Expression * body: Expression
    | Lambda of parameters: string list * body: Expression
    | If of condition: Expression * thenBranch: Expression * elseBranch: Expression
    // --- Shared by all three profiles; patterns are the Question core ---
    | PatternMatch of subject: Expression * clauses: ExpressionClause list
    // --- Question profile ---
    | DeclareKind of PatternKind
    | DeclarePredicate of name: string * version: int
    // --- Runtime control shared by Action and Logic ---
    | Condition of name: string * message: Expression
    | Restart of name: string * handler: Expression
    // --- Logic profile ---
    | Generic of name: string * arity: int * methods: Expression list
    | Message of kind: string * fields: Expression list
    | Fail of reason: string

/// The profiles a node belongs to. Question has no private expression constructors;
/// it is the common core plus `PatternMatch` and the pattern declarations.
let profilesOf (expression: Expression) : Set<ExpressionProfile> =
    match expression with
    | Literal _
    | Word _
    | Call _
    | Partial _
    | Hole
    | Quotation _
    | Block _
    | Fail _ -> Set.ofList [ CommonProfile; ActionProfile; QuestionProfile; LogicProfile ]
    | Pipeline _
    | ReversePipeline _ -> Set.singleton ActionProfile
    | Let _
    | Lambda _
    | If _
    | Condition _
    | Restart _ -> Set.ofList [ ActionProfile; LogicProfile ]
    | PatternMatch _ -> Set.ofList [ ActionProfile; QuestionProfile; LogicProfile ]
    | DeclareKind _
    | DeclarePredicate _ -> Set.singleton QuestionProfile
    | Generic _
    | Message _ -> Set.singleton LogicProfile

/// Lowers a pipeline to a call without changing any other node. The value is
/// evaluated by the normal expression lowering before the stage arguments.
let rec desugarPipeline (expression: Expression) : Expression =
    let desugar = desugarPipeline

    match expression with
    | Pipeline(value, stage, style) ->
        let value = desugar value
        let stage = desugar stage

        match stage with
        | Call(target, arguments) ->
            match style with
            | LastArgument -> Call(target, arguments @ [ value ])
            | FirstArgument -> Call(target, value :: arguments)
        | other -> Call(other, [ value ])
    | Literal _
    | Word _
    | Hole -> expression
    | Call(target, arguments) -> Call(desugar target, arguments |> List.map desugar)
    | Partial(target, arguments) -> Partial(desugar target, arguments |> List.map desugar)
    | ReversePipeline(target, value) ->
        let target = desugar target
        let value = desugar value

        match target with
        | Call(callee, arguments) -> Call(callee, arguments @ [ value ])
        | Partial(callee, arguments) ->
            let rec fill (remaining: Expression list) (accumulated: Expression list) =
                match remaining with
                | [] -> Partial(callee, arguments @ [ value ])
                | Hole :: rest -> Call(callee, List.rev accumulated @ [ value ] @ rest)
                | argument :: rest -> fill rest (argument :: accumulated)

            fill arguments []
        | _ -> Call(target, [ value ])
    | Quotation body -> Quotation(body |> List.map desugar)
    | Block body -> Block(body |> List.map desugar)
    | Let(name, value, body) -> Let(name, desugar value, desugar body)
    | Lambda(parameters, body) -> Lambda(parameters, desugar body)
    | If(condition, thenBranch, elseBranch) -> If(desugar condition, desugar thenBranch, desugar elseBranch)
    | PatternMatch(subject, clauses) ->
        PatternMatch(
            desugar subject,
            clauses
            |> List.map (fun clause ->
                { clause with
                    Body = clause.Body |> List.map desugar
                })
        )
    | DeclareKind _
    | DeclarePredicate _ -> expression
    | Condition(name, message) -> Condition(name, desugar message)
    | Restart(name, handler) -> Restart(name, desugar handler)
    | Generic(name, arity, methods) -> Generic(name, arity, methods |> List.map desugar)
    | Message(kind, fields) -> Message(kind, fields |> List.map desugar)
    | Fail _ -> expression

/// The path to one node in an expression tree. The root is `[]`; a child
/// appends its position, following the constructor's field order.
type ExpressionPath = int list

/// A half-open character range in the source text. Parsing and inference share
/// this type, so inference diagnostics can point back at the parser's ranges
/// without the parser depending on the inference module.
type SourceRange = { StartOffset: int; EndOffset: int }

/// Source ranges keyed by expression path. The map is metadata; it is never
/// part of the portable expression document.
type ExpressionSourceMap = Map<ExpressionPath, SourceRange>

/// A closed, versioned expression document.
type ExpressionDocument =
    {
        SchemaVersion: int
        Form: string
        Kinds: PatternKind list
        Predicates: (string * int) list
        Root: Expression
    }
