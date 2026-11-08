module Tokenizer

open Types

/// Splits the two set sigils out of a word: `[` and `]` always stand alone,
/// and a leading `:` opens a name token (`:player`, `:small`). Quoted text is
/// left untouched, so a colon or bracket inside a string is literal.
let splitSigils (word: string) : string list =
    if word.StartsWith("\"") then
        [ word ]
    else
        let rec loop (remaining: char list) (current: char list) (accumulated: string list) : string list =
            match remaining with
            | [] ->
                let tokens =
                    if current = [] then
                        accumulated
                    else
                        System.String(List.rev current |> Array.ofList) :: accumulated

                List.rev tokens
            | character :: rest when character = '[' || character = ']' ->
                let flushed =
                    if current = [] then
                        accumulated
                    else
                        System.String(List.rev current |> Array.ofList) :: accumulated

                loop rest [] (string character :: flushed)
            | ':' :: rest ->
                let flushed =
                    if current = [] then
                        accumulated
                    else
                        System.String(List.rev current |> Array.ofList) :: accumulated

                loop rest [ ':' ] flushed
            | character :: rest -> loop rest (character :: current) accumulated

        loop (word |> List.ofSeq) [] []

let tokenize (vocabulary: VocabularyEntry list) (sentence: string) : string list =
    let words =
        sentence
            .Replace(".", " . ")
            .Replace(",", " , ")
            .Split([| ' '; '\t'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun word -> word.ToLower())
        |> Array.toList
        |> List.collect splitSigils

    let rec combine (remaining: string list) (accumulated: string list) : string list =
        match remaining with
        | [] -> List.rev accumulated
        | _ ->
            let bestMatch =
                [ 3; 2; 1 ]
                |> List.choose (fun length ->
                    if remaining.Length >= length then
                        let candidate = String.concat " " (remaining |> List.take length)

                        match
                            vocabulary
                            |> List.tryFind (fun entry -> entry.Form = candidate && entry.IsActive)
                        with
                        | Some entry -> Some(length, entry)
                        | None -> None
                    else
                        None)
                |> List.tryHead

            match bestMatch with
            | Some(length, entry) ->
                let rest = remaining |> List.skip length
                combine rest (entry.Token :: accumulated)
            | None -> combine (remaining |> List.tail) (remaining.Head :: accumulated)

    combine words []
