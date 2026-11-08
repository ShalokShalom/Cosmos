module ScaleFile

// The .scale document: one specification sentence, plus machine-appended
// refinements. The file is the single editable source of truth; the
// refinement service appends to it, and nothing else edits it.

open Types

/// A field value in the domain under test.
type FieldValue =
    | BooleanValue of bool
    | NumericValue of RealNumber

/// A guard: a single-field condition synthesized by the refinement service.
/// Deliberately tiny, so a game developer can read it and both Hedgehog and
/// Z3 can check it.
type RefinementGuard =
    | GreaterThan of field: string * value: RealNumber
    | AtLeast of field: string * value: RealNumber
    | LessThan of field: string * value: RealNumber
    | AtMost of field: string * value: RealNumber
    | IsTrue of field: string
    | IsFalse of field: string
    /// A document refinement that names an image-resident guard metaobject
    /// (D41). The guard's quotation decides the field value at runtime; a
    /// quotation has no SMT encoding, so the verification backends refuse it
    /// by name rather than approximate it.
    | NamedGuard of field: string * name: string

/// A refinement: a guard plus where it came from.
type Refinement =
    {
        Guard: RefinementGuard
        Origin: string
        Counterexample: string
    }

/// The document: the specification sentence and the refinements beneath the fold.
type ScaleDocument =
    {
        Specification: string
        Refinements: Refinement list
    }

/// Escapes a free-text field for the line-oriented .scale format. The format
/// reads one directive per line, so a newline inside a field would be read
/// back as another directive: a refinement's origin could inject a
/// `specification:` line and silently replace the specification. Escaping keeps
/// every field on one line, and `unescapeText` restores it exactly, so a
/// document still round-trips through render and parse.
let private escapeText (text: string) : string =
    let builder = System.Text.StringBuilder()

    for character in text do
        match character with
        | '\\' -> builder.Append("\\\\") |> ignore
        | '\n' -> builder.Append("\\n") |> ignore
        | '\r' -> builder.Append("\\r") |> ignore
        | other -> builder.Append(other) |> ignore

    builder.ToString()

/// Restores a field escaped by `escapeText`. An unknown escape is kept as
/// written, so the text is never lost by a decode.
let private unescapeText (text: string) : string =
    let builder = System.Text.StringBuilder()
    let mutable index = 0

    while index < text.Length do
        let character = text.[index]

        if character = '\\' && index + 1 < text.Length then
            match text.[index + 1] with
            | 'n' ->
                builder.Append('\n') |> ignore
                index <- index + 2
            | 'r' ->
                builder.Append('\r') |> ignore
                index <- index + 2
            | '\\' ->
                builder.Append('\\') |> ignore
                index <- index + 2
            | _ ->
                builder.Append(character) |> ignore
                index <- index + 1
        else
            builder.Append(character) |> ignore
            index <- index + 1

    builder.ToString()

/// Renders a guard as it appears in the .scale file.
let refinementGuardText (guard: RefinementGuard) : string =
    let number (value: RealNumber) =
        value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)

    match guard with
    | GreaterThan(field, value) -> $"{field} > {number value}"
    | AtLeast(field, value) -> $"{field} >= {number value}"
    | LessThan(field, value) -> $"{field} < {number value}"
    | AtMost(field, value) -> $"{field} <= {number value}"
    | IsTrue field -> $"{field} is true"
    | IsFalse field -> $"{field} is false"
    | NamedGuard(field, name) -> $"{field} guard {name}"

let private parseNumber (text: string) : RealNumber option =
    match System.Double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture) with
    | true, value -> Some value
    | false, _ -> None

/// Parses a guard from its .scale text, for example "health > 1" or
/// "alive is false".
let parseRefinementGuard (text: string) : RefinementGuard option =
    let parts =
        text.Trim().Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)

    match parts with
    | [| field; "is"; "true" |] -> Some(IsTrue field)
    | [| field; "is"; "false" |] -> Some(IsFalse field)
    | [| field; "guard"; name |] -> Some(NamedGuard(field, name))
    | [| field; ">"; value |] -> parseNumber value |> Option.map (fun v -> GreaterThan(field, v))
    | [| field; ">="; value |] -> parseNumber value |> Option.map (fun v -> AtLeast(field, v))
    | [| field; "<"; value |] -> parseNumber value |> Option.map (fun v -> LessThan(field, v))
    | [| field; "<="; value |] -> parseNumber value |> Option.map (fun v -> AtMost(field, v))
    | _ -> None

/// Parses a .scale document. An unrecognized line is an error: the file is
/// machine-appended on one side only, so drift means something is wrong.
let parse (text: string) : Result<ScaleDocument, string> =
    let lines = text.Replace("\r\n", "\n").Split('\n') |> Array.toList
    let mutable specification: string option = None
    let mutable refinements: Refinement list = []
    let mutable error: string option = None
    let mutable guard: RefinementGuard option = None
    let mutable origin: string = ""
    let mutable counterexample: string = ""

    let commit () =
        match guard with
        | Some current ->
            refinements <-
                {
                    Guard = current
                    Origin = origin
                    Counterexample = counterexample
                }
                :: refinements
        | None -> ()

        guard <- None
        origin <- ""
        counterexample <- ""

    for line in lines do
        if error.IsNone then
            let trimmed = line.Trim()

            if trimmed = "" || trimmed.StartsWith "#" || trimmed.StartsWith "---" then
                ()
            elif trimmed.StartsWith "specification:" then
                commit ()
                specification <- Some(trimmed.Substring("specification:".Length).Trim() |> unescapeText)
            elif trimmed.StartsWith "refinement:" then
                commit ()
                let body = trimmed.Substring("refinement:".Length).Trim() |> unescapeText

                match parseRefinementGuard body with
                | Some parsed -> guard <- Some parsed
                | None -> error <- Some("Cannot parse the refinement: " + body)
            elif trimmed.StartsWith "origin:" then
                origin <- trimmed.Substring("origin:".Length).Trim() |> unescapeText
            elif trimmed.StartsWith "counterexample:" then
                counterexample <- trimmed.Substring("counterexample:".Length).Trim() |> unescapeText
            else
                error <- Some("Unrecognized line in the .scale file: " + line)

    commit ()

    match error with
    | Some message -> Error message
    | None ->
        match specification with
        | Some sentence ->
            Ok
                {
                    Specification = sentence
                    Refinements = List.rev refinements
                }
        | None -> Error "The .scale file has no 'specification:' line."

/// Renders the document back to .scale text.
let render (document: ScaleDocument) : string =
    let builder = System.Text.StringBuilder()

    builder.AppendLine("# Scale specification - the single editable source of truth.")
    |> ignore

    builder.AppendLine("#") |> ignore

    builder.AppendLine("# Edit the specification sentence below to change the specification.")
    |> ignore

    builder.AppendLine("# The refinement service appends refinements beneath the fold,")
    |> ignore

    builder.AppendLine("# each with the counterexample that revealed it.") |> ignore
    builder.AppendLine() |> ignore

    builder.AppendLine("specification: " + escapeText document.Specification)
    |> ignore

    if not (List.isEmpty document.Refinements) then
        builder.AppendLine() |> ignore

        builder.AppendLine("--- refinements (written by the refinement service) ---")
        |> ignore

        for refinement in document.Refinements do
            builder.AppendLine() |> ignore

            builder.AppendLine("refinement: " + escapeText (refinementGuardText refinement.Guard))
            |> ignore

            builder.AppendLine("  origin: " + escapeText refinement.Origin) |> ignore

            builder.AppendLine("  counterexample: " + escapeText refinement.Counterexample)
            |> ignore

    builder.ToString()

let read (path: string) : Result<ScaleDocument, string> =
    System.IO.File.ReadAllText path |> parse

let write (path: string) (document: ScaleDocument) : unit =
    let contents = render document

    if not (System.IO.File.Exists path) || System.IO.File.ReadAllText path <> contents then
        System.IO.File.WriteAllText(path, contents)

/// Appends a refinement (the refinement service's only write).
let append (refinement: Refinement) (document: ScaleDocument) : ScaleDocument =
    { document with
        Refinements = document.Refinements @ [ refinement ]
    }

/// A stable hash of the document's canonical rendering. The repair bundle pins
/// it: a patch accepted against one specification cannot be applied to another.
let hash (document: ScaleDocument) : string =
    use sha = System.Security.Cryptography.SHA256.Create()
    let bytes = System.Text.Encoding.UTF8.GetBytes(render document)

    sha.ComputeHash(bytes)
    |> Array.map (fun byte -> byte.ToString("x2"))
    |> String.concat ""
