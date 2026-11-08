module Edn

open System
open System.Globalization
open System.Text

// The small EDN subset used by the documentation model. The parser is shared by
// the architecture generator and the specification projection so both read the
// same structured inputs.

type Edn =
    | Nil
    | Bool of bool
    | Number of float
    | Text of string
    | Keyword of string
    | Symbol of string
    | Vector of Edn list
    | Map of (Edn * Edn) list
    | List of Edn list

let private stopChars =
    set [ ' '; '\t'; '\r'; '\n'; ','; '['; ']'; '{'; '}'; '('; ')'; '"'; ';' ]

let private isStop (character: char) = stopChars.Contains character

let private failAt (text: string) (index: int) (message: string) =
    let line = text.Substring(0, min index text.Length).Split('\n').Length
    failwithf "EDN %s at line %d (offset %d)" message line index

let private skip (text: string) (index: int ref) =
    let mutable moving = true

    while moving && index.Value < text.Length do
        let character = text.[index.Value]

        if Char.IsWhiteSpace character || character = ',' then
            index.Value <- index.Value + 1
        elif character = ';' then
            while index.Value < text.Length && text.[index.Value] <> '\n' do
                index.Value <- index.Value + 1
        else
            moving <- false

let private readToken (text: string) (index: int ref) =
    let start = index.Value

    while index.Value < text.Length && not (isStop text.[index.Value]) do
        index.Value <- index.Value + 1

    if start = index.Value then
        failAt text start "expected a token"

    text.Substring(start, index.Value - start)

let private readString (text: string) (index: int ref) =
    index.Value <- index.Value + 1
    let builder = StringBuilder()
    let mutable finished = false

    while not finished do
        if index.Value >= text.Length then
            failAt text index.Value "unterminated string"

        match text.[index.Value] with
        | '"' ->
            index.Value <- index.Value + 1
            finished <- true
        | '\\' when index.Value + 1 < text.Length ->
            let escaped = text.[index.Value + 1]
            index.Value <- index.Value + 2

            match escaped with
            | '"' -> builder.Append('"') |> ignore
            | '\\' -> builder.Append('\\') |> ignore
            | 'n' -> builder.Append('\n') |> ignore
            | 'r' -> builder.Append('\r') |> ignore
            | 't' -> builder.Append('\t') |> ignore
            | 'b' -> builder.Append('\b') |> ignore
            | 'f' -> builder.Append('\f') |> ignore
            | 'u' when index.Value + 4 <= text.Length ->
                let hex = text.Substring(index.Value, 4)
                index.Value <- index.Value + 4

                match Int32.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) with
                | true, code -> builder.Append(char code) |> ignore
                | _ -> failAt text index.Value ("invalid unicode escape " + hex)
            | other -> builder.Append(other) |> ignore
        | character ->
            builder.Append(character) |> ignore
            index.Value <- index.Value + 1

    builder.ToString()

let rec private parseValue (text: string) (index: int ref) : Edn =
    skip text index

    if index.Value >= text.Length then
        failAt text index.Value "unexpected end of input"

    match text.[index.Value] with
    | '[' ->
        index.Value <- index.Value + 1
        Vector(parseUntil text index ']')
    | '{' ->
        index.Value <- index.Value + 1
        let values = parseUntil text index '}'

        let rec pair remaining accumulated =
            match remaining with
            | [] -> List.rev accumulated
            | key :: value :: rest -> pair rest ((key, value) :: accumulated)
            | _ -> failAt text index.Value "map has an odd number of forms"

        Map(pair values [])
    | '(' ->
        index.Value <- index.Value + 1
        List(parseUntil text index ')')
    | '"' -> Text(readString text index)
    | ':' ->
        index.Value <- index.Value + 1
        Keyword(readToken text index)
    | _ ->
        let token = readToken text index

        match token with
        | "nil" -> Nil
        | "true" -> Bool true
        | "false" -> Bool false
        | _ ->
            match Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, value -> Number value
            | false, _ -> Symbol token

and private parseUntil (text: string) (index: int ref) (closing: char) : Edn list =
    skip text index

    if index.Value >= text.Length then
        failAt text index.Value ("expected " + string closing)

    if text.[index.Value] = closing then
        index.Value <- index.Value + 1
        []
    else
        let value = parseValue text index
        value :: parseUntil text index closing

let parse (text: string) : Edn =
    let index = ref 0
    let value = parseValue text index
    skip text index

    if index.Value <> text.Length then
        failAt text index.Value "trailing forms"

    value

let parseEdn = parse

let asMap (value: Edn) =
    match value with
    | Map pairs -> pairs
    | _ -> failwith "expected a map"

let asVector (value: Edn) =
    match value with
    | Vector items -> items
    | _ -> failwith "expected a vector"

let asText (value: Edn) =
    match value with
    | Text text -> text
    | _ -> failwith "expected a string"

let asKeyword (value: Edn) =
    match value with
    | Keyword name -> name
    | _ -> failwith "expected a keyword"

let asNumber (value: Edn) =
    match value with
    | Number number -> number
    | _ -> failwith "expected a number"

let tryGet (key: string) (pairs: (Edn * Edn) list) =
    pairs
    |> List.tryFind (fun (candidate, _) -> candidate = Keyword key)
    |> Option.map snd

let get (key: string) (pairs: (Edn * Edn) list) =
    match tryGet key pairs with
    | Some value -> value
    | None -> failwith ("missing EDN key :" + key)

let getText key pairs = get key pairs |> asText
let getKeyword key pairs = get key pairs |> asKeyword
let getVector key pairs = get key pairs |> asVector
