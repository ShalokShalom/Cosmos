module OwnershipTypes

/// How many times a value may be used. `ExactlyOnce` is linear,
/// `AtMostOnce` is affine, and `AnyNumberOfTimes` is unrestricted.
type UsageMode =
    | ExactlyOnce
    | AtMostOnce
    | AnyNumberOfTimes

type OwnershipType =
    | Unowned
    | Owned
    | Consumed
    | NonReference

type LinearVerdict =
    | Linear
    | Nonlinear of reason: string
    | Undecided of reason: string

/// One step of a linear trace. The ownership words are steps; a literal push
/// is a step; any other word is opaque and makes the trace undecided.
type LinearStep =
    | Own
    | Borrow
    | Consume
    | Release
    | Duplicate
    | Over
    | Swap
    | Drop
    | Rotate
    | PushReference
    | PushNonReference
    | Opaque of word: string

/// What one step does to the symbolic stack.
type StepOutcome =
    | Advanced of OwnershipType list
    | Refused of reason: string
    | Unmodelled of reason: string

let private advance (mode: UsageMode) (step: LinearStep) (stack: OwnershipType list) : StepOutcome =
    let unrestricted = mode = AnyNumberOfTimes

    match step with
    | Own ->
        match stack with
        | NonReference :: _ -> Refused "own requires a reference"
        | Consumed :: _ -> Refused "own after consume"
        | _ :: tail -> Advanced(Owned :: tail)
        | [] -> Refused "own: stack underflow"
    | Borrow ->
        match stack with
        | Owned :: tail -> Advanced(Owned :: tail)
        | Consumed :: _ -> Refused "borrow after consume"
        | Unowned :: tail when unrestricted -> Advanced(Unowned :: tail)
        | Unowned :: _ -> Refused "borrow without own"
        | NonReference :: _ -> Refused "borrow requires a reference"
        | [] -> Refused "borrow: stack underflow"
    | Consume ->
        match stack with
        | Owned :: tail -> Advanced(Consumed :: tail)
        | Consumed :: _ -> Refused "a second consume"
        | Unowned :: _ -> Refused "consume without own"
        | NonReference :: _ -> Refused "consume requires a reference"
        | [] -> Refused "consume: stack underflow"
    | Release ->
        match stack with
        | Owned :: tail -> Advanced(Unowned :: tail)
        | Consumed :: tail -> Advanced(Consumed :: tail)
        | Unowned :: _ -> Refused "release without own"
        | NonReference :: _ -> Refused "release requires a reference"
        | [] -> Refused "release: stack underflow"
    | Duplicate ->
        match stack with
        | Consumed :: _ -> Refused "duplicate after consume"
        | NonReference :: _ -> Advanced(NonReference :: stack)
        | top :: _ when unrestricted -> Advanced(top :: stack)
        | _ :: _ -> Refused "duplicate aliases a reference"
        | [] -> Refused "duplicate: stack underflow"
    | Over ->
        match stack with
        | _ :: Consumed :: _ -> Refused "over after consume"
        | _ :: NonReference :: _ -> Advanced(NonReference :: stack)
        | _ :: second :: _ when unrestricted -> Advanced(second :: stack)
        | _ :: _ :: _ -> Refused "over aliases a reference"
        | _ -> Refused "over: stack underflow"
    | Swap ->
        match stack with
        | first :: second :: tail -> Advanced(second :: first :: tail)
        | _ -> Refused "swap: stack underflow"
    | Drop ->
        match stack with
        | Owned :: _ when mode = ExactlyOnce -> Refused "an owned reference is dropped without release or consume"
        | _ :: tail -> Advanced tail
        | [] -> Refused "drop: stack underflow"
    | Rotate ->
        match stack with
        | first :: second :: third :: tail -> Advanced(third :: first :: second :: tail)
        | _ -> Refused "rotate: stack underflow"
    | PushReference -> Advanced(Unowned :: stack)
    | PushNonReference -> Advanced(NonReference :: stack)
    | Opaque word when word.StartsWith("?") ->
        Unmodelled("the static contract does not model the variable '" + word + "'")
    | Opaque word -> Unmodelled("the static contract does not model the word '" + word + "'")

/// Checks one trace under one usage mode. The end-of-trace rule is the mode's
/// obligation: a linear trace must not leave an owned reference open, while an
/// affine or unrestricted trace may drop it.
let checkTrace (mode: UsageMode) (trace: LinearStep list) : LinearVerdict =
    let rec walk (remaining: LinearStep list) (stack: OwnershipType list) : LinearVerdict =
        match remaining with
        | [] ->
            match mode with
            | ExactlyOnce when List.contains Owned stack ->
                Nonlinear "an owned reference is left open at the end of the trace"
            | _ -> Linear
        | step :: rest ->
            match advance mode step stack with
            | Advanced next -> walk rest next
            | Refused reason -> Nonlinear reason
            | Unmodelled reason -> Undecided reason

    walk trace [ Unowned ]

let stepOfToken (token: string) : LinearStep =
    match token with
    | "own" -> Own
    | "borrow" -> Borrow
    | "consume" -> Consume
    | "release" -> Release
    | "duplicate" -> Duplicate
    | "over" -> Over
    | "swap" -> Swap
    | "drop" -> Drop
    | "rotate" -> Rotate
    | _ when token.StartsWith("\"") -> PushReference
    | _ when token.StartsWith(":") -> PushReference
    | _ ->
        match System.Double.TryParse token with
        | true, _ -> PushNonReference
        | false, _ -> Opaque token

let traceOfQuotation (text: string) : LinearStep list =
    text.Split([| ' '; '\t'; '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList
    |> List.map stepOfToken

/// The default contract is linear: the quotation's subject is a linear
/// resource, so an owned reference left open is a leak.
let checkQuotation (text: string) : LinearVerdict =
    checkTrace ExactlyOnce (traceOfQuotation text)

/// A linear type is a named set of traces under one usage mode. The name is the
/// nominal projection and the language is the extension; this is the third
/// projection of the sort record (D12). Union, intersect, and `nor` do not
/// apply, because linear composition is sequential, not set algebra.
type LinearType =
    private
        {
            Name: string option
            Usage: UsageMode
            Language: Set<LinearStep list>
        }

let nameOf (linearType: LinearType) : string option = linearType.Name

let modeOf (linearType: LinearType) : UsageMode = linearType.Usage

let languageOf (linearType: LinearType) : Set<LinearStep list> = linearType.Language

/// The language holds only the traces the checker proves linear under the mode.
let createLinearType (name: string option) (mode: UsageMode) (traces: LinearStep list list) : LinearType =
    {
        Name = name
        Usage = mode
        Language =
            traces
            |> List.filter (fun trace -> checkTrace mode trace = Linear)
            |> Set.ofList
    }

let linearTypeOfQuotation (mode: UsageMode) (text: string) : LinearType =
    createLinearType None mode [ traceOfQuotation text ]

let accepts (linearType: LinearType) (trace: LinearStep list) : bool = Set.contains trace linearType.Language
