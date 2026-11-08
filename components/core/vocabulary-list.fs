module VocabularyList

open Types

let showVocabulary (vocabulary: VocabularyEntry list) : unit =
    printfn "+-----------------------------------------+"
    printfn "| Vocabulary (available ingredients)             |"
    printfn "+-----------------------------------------+"
    let categories = vocabulary |> List.groupBy (fun entry -> entry.Category)

    for (category, entries) in categories do
        printfn "| %s" (string category)

        for entry in entries do
            let status = if entry.IsActive then "+" else "o"
            printfn "|   %s %-30s |" status entry.Form

    printfn "+-----------------------------------------+"
    printfn ""

let explainWord (vocabulary: VocabularyEntry list) (token: string) : unit =
    match
        vocabulary
        |> List.rev
        |> List.tryFind (fun entry -> entry.Token = token || entry.Form = token)
    with
    | Some entry ->
        printfn "Word:      %s" entry.Token
        printfn "Form:      %s" entry.Form
        printfn "Implementation name:   %s" entry.ImplementationName
        printfn "Category:  %s" (string entry.Category)
        printfn "Status:    %s" (if entry.IsActive then "active" else "inactive")
    | None -> printfn "Unknown word: %s" token

    printfn ""
