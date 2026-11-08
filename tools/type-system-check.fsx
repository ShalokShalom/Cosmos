open System
open System.IO
open System.Text

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
    | character when Char.IsDigit character || character = '-' ->
        Number(Double.Parse(readToken text index, Globalization.CultureInfo.InvariantCulture))
    | _ ->
        let token = readToken text index

        match token with
        | "nil" -> Nil
        | "true" -> Bool true
        | "false" -> Bool false
        | other -> Symbol other

and private parseUntil (text: string) (index: int ref) (closing: char) : Edn list =
    skip text index
    let values = ResizeArray<Edn>()
    let mutable finished = false

    while not finished do
        skip text index

        if index.Value >= text.Length then
            failAt text index.Value ("unterminated " + string closing)

        if text.[index.Value] = closing then
            index.Value <- index.Value + 1
            finished <- true
        else
            values.Add(parseValue text index)

    List.ofSeq values

let private asMap (value: Edn) (context: string) =
    match value with
    | Map entries -> entries
    | _ -> failwith (context + " must be a map")

let private tryFind (key: string) (entries: (Edn * Edn) list) =
    entries
    |> List.tryPick (fun (entryKey, entryValue) ->
        match entryKey with
        | Keyword name when name = key -> Some entryValue
        | _ -> None)

let private require (key: string) (entries: (Edn * Edn) list) =
    match tryFind key entries with
    | Some value -> value
    | None -> failwith ("missing required key :" + key)

let private asVector (value: Edn) (context: string) =
    match value with
    | Vector items -> items
    | _ -> failwith (context + " must be a vector")

let private asKeyword (value: Edn) (context: string) =
    match value with
    | Keyword name -> name
    | _ -> failwith (context + " must be a keyword")

let private asString (value: Edn) (context: string) =
    match value with
    | Text text -> text
    | _ -> failwith (context + " must be text")

let private asBool (value: Edn) (context: string) =
    match value with
    | Bool value -> value
    | _ -> failwith (context + " must be a boolean")

let private requireNonEmptyVector key entries =
    let items = asVector (require key entries) (":" + key)

    if List.isEmpty items then
        failwith (":" + key + " must not be empty")

    items

let private propertyIds (entries: (Edn * Edn) list) (key: string) =
    match tryFind key entries with
    | None -> []
    | Some value ->
        asVector value (":" + key)
        |> List.choose (fun item ->
            match item with
            | Map property -> tryFind "id" property |> Option.map (fun id -> asString id ":id")
            | Text id -> Some id
            | _ -> None)

let path = Path.Combine(__SOURCE_DIRECTORY__, "..", "docs", "type-system.edn")
let text = File.ReadAllText path
let index = ref 0
let root = parseValue text index
skip text index

if index.Value <> text.Length then
    failAt text index.Value "trailing input"

let rootEntries = asMap root "root"

let requiredKeys =
    [ "spec/id"
      "title"
      "status"
      "scope"
      "layers"
      "architecture"
      "verification-lattice"
      "row-types"
      "record-types"
      "effect-types"
      "handlers"
      "runtime-sorts"
      "runtime-shapes"
      "static-types"
      "execution-model"
      "properties"
      "formal-verification"
      "test-strategy"
      "assessment"
      "refusals"
      "deferred"
      "evidence" ]

for key in requiredKeys do
    require key rootEntries |> ignore

let layers = requireNonEmptyVector "layers" rootEntries

let layerIds =
    layers
    |> List.map (fun layer ->
        asMap layer "layer" |> fun entries -> asKeyword (require "id" entries) ":id")

if List.distinct layerIds <> layerIds then
    failwith "layer ids must be unique"

let architecture = asMap (require "architecture" rootEntries) ":architecture"

for key in [ "direction"; "non-edges"; "trust-boundaries" ] do
    requireNonEmptyVector key architecture |> ignore

let lattice = asMap (require "verification-lattice" rootEntries) ":verification-lattice"
let latticeData = asMap (require "data" lattice) ":verification-lattice/data"

if not (asBool (require "pure-values" latticeData) ":pure-values") then
    failwith "the verification lattice must be a pure value"

let composition = asMap (require "composition" lattice) ":composition"
let product = asMap (require "product" composition) ":product"
let sum = asMap (require "sum" composition) ":sum"

if asKeyword (require "status" product) ":product/status" <> "implemented" then
    failwith "product composition must be implemented"

if asKeyword (require "status" sum) ":sum/status" <> "data-only" then
    failwith "sum composition must be data-only"

let properties = asMap (require "properties" rootEntries) ":properties"
let ids =
    propertyIds properties "verification-lattice"
    @ propertyIds properties "row-types"
    @ propertyIds properties "static-types"
    @ propertyIds properties "runtime-sorts"
    @ propertyIds properties "runtime-shapes"

if List.distinct ids <> ids then
    failwith "property ids must be unique"

let requiredIds =
    [ "L1"; "L2"; "L3"; "L4"; "L5"; "L6"; "L7"; "L8"; "L9"; "L10"; "L11"
      "R1"; "R2"; "R3"; "R4"
      "S1"; "S2"; "S3"; "S4"; "S5"; "S6"; "S7"; "S8"
      "V1"; "V2"; "V3"; "V4"; "V5"
      "K1"; "K2"; "K3"; "K4"; "K5" ]

for id in requiredIds do
    if not (List.contains id ids) then
        failwith ("missing property " + id)

asString (require "ownership-convention" properties) ":properties/ownership-convention"
|> ignore

let current =
    asMap (require "current" (asMap (require "test-strategy" rootEntries) ":test-strategy")) ":test-strategy/current"

let specPropertyCount = require "spec-properties" current

let specPropertyCountValue =
    match specPropertyCount with
    | Number value -> int value
    | _ -> failwith "spec-properties must be a number"

if specPropertyCountValue <> ids.Length then
    failwith "spec-properties does not match the number of property ids"

let assessment = asMap (require "assessment" rootEntries) ":assessment"
asString (require "scope" assessment) ":assessment/scope" |> ignore

for key in [ "feasibility"; "realisticness"; "practicability" ] do
    asMap (require key assessment) (":assessment/" + key) |> ignore

let formal = asMap (require "formal-verification" rootEntries) ":formal-verification"
let structural = asMap (require "structural" formal) ":formal-verification/structural"
asString (require "parser" structural) ":formal-verification/structural/parser" |> ignore

let execution = asMap (require "execution-model" rootEntries) ":execution-model"

for key in [ "effects"; "composition"; "resources"; "state"; "deployment" ] do
    asMap (require key execution) (":execution-model/" + key) |> ignore

let effects = asMap (require "effects" execution) ":execution-model/effects"

if asKeyword (require "effect-rows" effects) ":execution-model/effects/effect-rows" <> "open-label-sets" then
    failwith "effect rows must use the open-label-set representation"

let subtraction = asMap (require "subtraction" effects) ":execution-model/effects/subtraction"

if asKeyword (require "closed" subtraction) ":execution-model/effects/subtraction/closed" <> "implemented" then
    failwith "closed effect subtraction must be implemented"

if asKeyword (require "open" subtraction) ":execution-model/effects/subtraction/open" <> "implemented" then
    failwith "open effect subtraction must be implemented"

let executionComposition = asMap (require "composition" execution) ":execution-model/composition"
asString (require "cycles" executionComposition) ":execution-model/composition/cycles" |> ignore
asString (require "dynamic-loading" executionComposition) ":execution-model/composition/dynamic-loading" |> ignore

let resources = asMap (require "resources" execution) ":execution-model/resources"
let linearity = asMap (require "linearity" resources) ":execution-model/resources/linearity"

if asKeyword (require "status" linearity) ":execution-model/resources/linearity/status" <> "implemented" then
    failwith "the linearity trace checker must be implemented"

let state = asMap (require "state" execution) ":execution-model/state"
asString (require "model" state) ":execution-model/state/model" |> ignore

let deployment = asMap (require "deployment" execution) ":execution-model/deployment"
asString (require "self-modification" deployment) ":execution-model/deployment/self-modification" |> ignore

if text.Contains "Dafny" then
    failwith "retired Dafny references are not allowed"

printfn "type-system-check: OK (%d layers, %d properties)" layerIds.Length ids.Length
