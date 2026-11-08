module NearestSentence

// ============================================================
// Nearest legal sentence, phase P4 (D71).
//
// The fixed specification is the parser and its vocabulary. A candidate is a
// token list within a bounded edit distance; a candidate is accepted when the
// fixed parser accepts it and an optional layer check (the sortal hook) agrees.
// The search stops at the first distance with an accepted candidate, so every
// result is a minimum edit. The search never rewrites the specification.
// ============================================================

open Types
open Violations

type Candidate =
    {
        Tokens: string list
        Text: string
        Distance: int
    }

let private candidate (distance: int) (tokens: string list) : Candidate =
    {
        Tokens = tokens
        Text = String.concat " " tokens
        Distance = distance
    }

/// The one-edit neighbourhood: delete, insert, replace, or swap adjacent
/// tokens. The vocabulary supplies the insert/replace words.
let private edits (words: string list) (tokens: string list) : string list list =
    let n = List.length tokens

    let deletions =
        tokens
        |> List.indexed
        |> List.map (fun (index, _) -> List.removeAt index tokens)

    let insertions =
        [ 0..n ]
        |> List.collect (fun index -> words |> List.map (fun word -> List.insertAt index word tokens))

    let replacements =
        tokens
        |> List.indexed
        |> List.collect (fun (index, token) ->
            words
            |> List.filter ((<>) token)
            |> List.map (fun word -> List.updateAt index word tokens))

    let swaps =
        if n < 2 then
            []
        else
            [ 0 .. n - 2 ]
            |> List.map (fun index ->
                let left = List.item index tokens
                let right = List.item (index + 1) tokens
                tokens |> List.updateAt index right |> List.updateAt (index + 1) left)

    deletions @ insertions @ replacements @ swaps |> List.distinct

let private accepted (vocabulary: VocabularyEntry list) (validate: string list -> bool) (tokens: string list) : bool =
    (Parser.parseDetailed vocabulary tokens |> Result.isOk) && validate tokens

/// Searches the fixed grammar for accepted candidates, nearest first. `validate`
/// is the layer hook: the default accepts every parse; a caller adds a sortal,
/// guard, or binding check. The result is empty when no candidate is within
/// `maxDistance`.
let candidatesWith
    (validate: string list -> bool)
    (maxDistance: int)
    (vocabulary: VocabularyEntry list)
    (tokens: string list)
    : Candidate list =
    let words =
        (vocabulary
         |> List.filter (fun entry -> entry.IsActive)
         |> List.map (fun entry -> entry.Token))
        @ [ "."; "," ]
        |> List.distinct

    let rec loop (distance: int) (frontier: string list list) (seen: Set<string list>) : Candidate list =
        if distance > maxDistance || frontier.IsEmpty then
            []
        else
            let fresh =
                frontier
                |> List.filter (fun candidateTokens -> not (seen.Contains candidateTokens))

            let matches =
                fresh
                |> List.filter (fun candidateTokens -> accepted vocabulary validate candidateTokens)

            if not matches.IsEmpty then
                matches
                |> List.map (candidate distance)
                |> List.sortBy (fun result -> result.Tokens)
            elif distance = maxDistance then
                []
            else
                let next = fresh |> List.collect (edits words) |> List.distinct
                loop (distance + 1) next (seen + Set.ofList fresh)

    if accepted vocabulary validate tokens then
        [ candidate 0 tokens ]
    else
        let first = edits words tokens |> List.distinct
        loop 1 first Set.empty

/// The default search: the fixed parser is the only acceptance check, one edit.
let candidates (vocabulary: VocabularyEntry list) (tokens: string list) : Candidate list =
    candidatesWith (fun _ -> true) 1 vocabulary tokens

/// The best candidate, or `None` when the input is outside the one-edit
/// neighbourhood. An already accepted input returns itself at distance 0, so
/// `nearest` is idempotent.
let nearest (vocabulary: VocabularyEntry list) (tokens: string list) : Candidate option =
    candidates vocabulary tokens |> List.tryHead

/// The same search with a caller-supplied layer check and distance bound. This
/// is the sortal path: a candidate must parse and satisfy the check.
let nearestWith
    (validate: string list -> bool)
    (maxDistance: int)
    (vocabulary: VocabularyEntry list)
    (tokens: string list)
    : Candidate option =
    candidatesWith validate maxDistance vocabulary tokens |> List.tryHead

/// The candidate as diagnostic advice. Advice is not a proposal and cannot be
/// accepted by the fixed-specification gate.
let advice (candidate: Candidate) : Advice =
    {
        Text = candidate.Text
        Span = None
        Distance = Some candidate.Distance
        Source = "nearest sentence"
    }
