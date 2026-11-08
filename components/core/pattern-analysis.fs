module PatternAnalysis

// ============================================================
// Best-effort analyses over the runtime pattern IR.
//
// The analysis is deliberately conservative. It can prove reachability and
// redundancy through structural subsumption, and it can prove exhaustiveness
// for a closed subject kind whose constructors are each covered by a clause
// with catch-all children. Everything else is `Undecided`, never a silent
// "not exhaustive" claim.
// ============================================================

open PatternLogic

/// The host metadata an analysis needs for a subject kind. A closed kind lists
/// its constructors; an open kind cannot be proved exhaustive.
type KindMetadata =
    {
        Name: string
        Arity: int
        Closed: bool
        Constructors: string list
    }

type Exhaustiveness =
    | Exhaustive
    | NotExhaustive
    | Undecided of reason: string

type PatternAnalysis =
    {
        Unreachable: int list
        Exhaustiveness: Exhaustiveness
    }

let rec private catchAll (pattern: Pattern) : bool =
    match pattern with
    | Wildcard -> true
    | Variable(_, Fresh)
    | Variable(_, Shadowing) -> true
    | TypedPattern(_, child) -> catchAll child
    | _ -> false

let rec private subsumes (earlier: Pattern) (later: Pattern) : bool =
    if catchAll earlier then
        true
    else
        match earlier, later with
        | Variable(name1, _), Variable(name2, _) when name1 = name2 -> true
        | NumberLiteral first, NumberLiteral second -> first = second
        | TextLiteral first, TextLiteral second -> first = second
        | AtomLiteral first, AtomLiteral second -> first = second
        | TypedPattern(sort1, child1), TypedPattern(sort2, child2) ->
            sort1 = sort2 && subsumes child1 child2
        | Constructor(name1, children1), Constructor(name2, children2) ->
            name1 = name2
            && List.length children1 = List.length children2
            && List.forall2 subsumes children1 children2
        | _ -> false

let private unreachableClauses (clauses: PatternClause list) : int list =
    let rec loop (index: int) (covering: Pattern list) (unreachable: int list) (remaining: PatternClause list) =
        match remaining with
        | [] -> List.rev unreachable
        | clause :: rest ->
            let covered = covering |> List.exists (fun earlier -> subsumes earlier clause.Pattern)
            let nextUnreachable = if covered then index :: unreachable else unreachable

            let nextCovering =
                if covered || clause.Guard.IsSome then
                    covering
                else
                    covering @ [ clause.Pattern ]

            loop (index + 1) nextCovering nextUnreachable rest

    loop 0 [] [] clauses

let private topLevelCoverage
    (subjectKind: KindMetadata option)
    (clauses: PatternClause list)
    : Exhaustiveness =
    let unguarded = clauses |> List.filter (fun clause -> clause.Guard.IsNone)

    if unguarded |> List.exists (fun clause -> catchAll clause.Pattern) then
        Exhaustive
    else
        match subjectKind with
        | None -> Undecided "the subject kind has no metadata"
        | Some kind when not kind.Closed -> Undecided("the subject kind " + kind.Name + " is open")
        | Some kind ->
            let covered =
                unguarded
                |> List.choose (fun clause ->
                    match clause.Pattern with
                    | Constructor(name, children) when List.forall catchAll children -> Some name
                    | _ -> None)
                |> Set.ofList

            let missing = kind.Constructors |> List.filter (fun name -> not (covered.Contains name))

            if missing.IsEmpty then
                Exhaustive
            else
                NotExhaustive

/// Analyses clause reachability and top-level exhaustiveness. Guards do not
/// cover later clauses; a clause subsumed by an earlier unguarded clause is
/// unreachable.
let analyze (subjectKind: KindMetadata option) (clauses: PatternClause list) : PatternAnalysis =
    {
        Unreachable = unreachableClauses clauses
        Exhaustiveness = topLevelCoverage subjectKind clauses
    }
