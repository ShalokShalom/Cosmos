module PreludeExecution

// ============================================================
// Step 3 execution wiring: resolve an exported unit and run it with the
// kernel abstraction. A word that names another image unit is executed recursively
// with the same machine state; a word with no unit is an unresolved primitive
// and is reported rather than silently ignored.
// ============================================================

open System
open Types
open KernelState
open LoweredInstructions
open LoweredAbstraction
open PreludeImage

type ExecutionError =
    | ImageError of ImageError
    | AbstractionError of AbstractionError
    | UnresolvedWord of word: string
    | CallDepthExceeded of limit: int
    | UnitArityMismatch of unit: string * expected: int * available: int
    | UnitCaptureMissing of unit: string * slot: int
    | EntryUnitArity of unit: string * arity: int
    | PrimitiveStackUnderflow of word: string * needed: int * available: int
    | PrimitiveProtocolFailure of word: string * reason: string
    | PrimitiveResultMismatch of word: string * expected: int * actual: int

[<Literal>]
let MaxCallDepth = 256

let describeError (error: ExecutionError) : string =
    match error with
    | ImageError imageError -> PreludeImage.describeError imageError
    | AbstractionError(LoweredFailure reason) -> "abstraction: " + reason
    | AbstractionError(HostFailure reason) -> "host: " + reason
    | UnresolvedWord word -> "unresolved word: " + word
    | CallDepthExceeded limit -> "call depth exceeded: " + string limit
    | UnitArityMismatch(unit, expected, available) ->
        "unit "
        + unit
        + " needs "
        + string expected
        + " arguments, "
        + string available
        + " available"
    | UnitCaptureMissing(unit, slot) -> "unit " + unit + " needs capture slot " + string slot + " from its caller"
    | EntryUnitArity(unit, arity) ->
        "entry unit "
        + unit
        + " has arity "
        + string arity
        + "; an entry must have arity 0"
    | PrimitiveStackUnderflow(word, needed, available) ->
        word
        + ": primitive needs "
        + string needed
        + " operands, "
        + string available
        + " available"
    | PrimitiveProtocolFailure(word, reason) -> word + ": primitive protocol failed: " + reason
    | PrimitiveResultMismatch(word, expected, actual) ->
        word
        + ": primitive returned "
        + string actual
        + " results; expected "
        + string expected

let private groupOf (image: ImageDocument) (unitName: string) : ImageUnitGroup option =
    image.Groups
    |> List.tryFind (fun group -> group.Units |> List.contains unitName)

let private findUnitInGroup (image: ImageDocument) (group: ImageUnitGroup option) (name: string) : ImageUnit option =
    match group with
    | Some current when current.Units |> List.contains name -> findUnit image name
    | _ -> findUnit image name

let private currentFrame (state: KernelState<'Host>) : EntryIdentity list =
    state.CaptureFrames |> List.tryHead |> Option.defaultValue []

let private resolveCaptures (unit: ImageUnit) (frame: EntryIdentity list) : Result<EntryIdentity list, ExecutionError> =
    let rec loop slots accumulated =
        match slots with
        | [] -> Ok(List.rev accumulated)
        | slot :: rest ->
            match List.tryItem slot frame with
            | Some identity -> loop rest (identity :: accumulated)
            | None -> Error(UnitCaptureMissing(unit.Name, slot))

    loop unit.Captures []

let private popArguments (count: int) (stack: EntryIdentity list) : (EntryIdentity list * EntryIdentity list) option =
    let rec loop remaining current accumulated =
        if remaining = 0 then
            Some(accumulated, current)
        else
            match Stack.popIndex current with
            | Some(identity, rest) -> loop (remaining - 1) rest (identity :: accumulated)
            | None -> None

    loop count stack []

let private runUnit
    (image: ImageDocument)
    (primitive: ImagePrimitive -> Tensor list -> Result<Tensor list, string>)
    (executeHost: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (runNested: int -> ImageUnit -> KernelState<'Host> -> Result<KernelState<'Host>, ExecutionError>)
    (depth: int)
    (unit: ImageUnit)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, ExecutionError> =
    let rec popOperands (store: Store) (remaining: int) (stack: EntryIdentity list) (operands: Tensor list) =
        if remaining = 0 then
            Ok(List.rev operands, stack)
        else
            match Stack.popResolved store stack with
            | Some(tensor, rest) -> popOperands store (remaining - 1) rest (tensor :: operands)
            | None -> Error(List.rev operands |> List.length)

    let callUnit (target: ImageUnit) (current: KernelState<'Host>) =
        if depth >= MaxCallDepth then
            Error(describeError (CallDepthExceeded MaxCallDepth))
        else
            match resolveCaptures target (currentFrame current) with
            | Error error -> Error(describeError error)
            | Ok captures ->
                match popArguments target.Arity current.Stack with
                | None -> Error(describeError (UnitArityMismatch(target.Name, target.Arity, Stack.depth current.Stack)))
                | Some(arguments, remaining) ->
                    let frame = arguments @ captures

                    let withFrame =
                        { current with
                            Stack = remaining
                            CaptureFrames = frame :: current.CaptureFrames
                        }

                    match runNested (depth + 1) target withFrame with
                    | Error error -> Error(describeError error)
                    | Ok after ->
                        let frames =
                            match after.CaptureFrames with
                            | _ :: tail -> tail
                            | [] -> []

                        Ok { after with CaptureFrames = frames }

    let callClosureTarget name current =
        match findUnit image name with
        | None -> Error("unresolved closure unit: " + name)
        | Some target ->
            if depth >= MaxCallDepth then
                Error(describeError (CallDepthExceeded MaxCallDepth))
            else
                match runNested (depth + 1) target current with
                | Ok next -> Ok next
                | Error error -> Error(describeError error)

    let callWord word current =
        match current.Words.TryFind word with
        | Some instructions ->
            let dynamicUnit =
                {
                    Name = word
                    SourceHash = "dynamic"
                    Dependencies = []
                    Arity = 0
                    Captures = []
                    Instructions = instructions
                }

            callUnit dynamicUnit current
        | None ->
            match findUnitInGroup image (groupOf image unit.Name) word with
            | None ->
                match image.Primitives |> List.tryFind (fun descriptor -> descriptor.Name = word) with
                | None -> executeHost word current
                | Some descriptor ->
                    match popOperands current.Store descriptor.Arity current.Stack [] with
                    | Error _ ->
                        Error(
                            describeError (PrimitiveStackUnderflow(word, descriptor.Arity, List.length current.Stack))
                        )
                    | Ok(operands, rest) ->
                        match primitive descriptor operands with
                        | Error reason -> Error(describeError (PrimitiveProtocolFailure(word, reason)))
                        | Ok results when results.Length = descriptor.Results ->
                            let (store, stack) =
                                results
                                |> List.fold
                                    (fun (store, stack) tensor ->
                                        let (identity, nextStore) = Store.addTensor tensor store
                                        (nextStore, Stack.push identity stack))
                                    (current.Store, rest)

                            Ok
                                { current with
                                    Store = store
                                    Stack = stack
                                }
                        | Ok results ->
                            Error(describeError (PrimitiveResultMismatch(word, descriptor.Results, results.Length)))
            | Some nested -> callUnit nested current

    LoweredAbstraction.runWithClosure callWord callClosureTarget unit.Instructions state
    |> Result.mapError AbstractionError

let runEntryWithPrimitiveAndHost
    (primitive: ImagePrimitive -> Tensor list -> Result<Tensor list, string>)
    (executeHost: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (image: ImageDocument)
    (entry: string)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, ExecutionError> =
    let rec runNested depth unit current =
        runUnit image primitive executeHost runNested depth unit current

    match resolveExport image entry with
    | Error error -> Error(ImageError error)
    | Ok unit when unit.Arity <> 0 -> Error(EntryUnitArity(unit.Name, unit.Arity))
    | Ok unit ->
        match resolveCaptures unit [] with
        | Error error -> Error error
        | Ok _ ->
            let withFrame =
                { state with
                    CaptureFrames = [] :: state.CaptureFrames
                }

            match runNested 0 unit withFrame with
            | Error error -> Error error
            | Ok after ->
                let frames =
                    match after.CaptureFrames with
                    | _ :: tail -> tail
                    | [] -> []

                Ok { after with CaptureFrames = frames }

let runEntryWithPrimitive
    (primitive: ImagePrimitive -> Tensor list -> Result<Tensor list, string>)
    (image: ImageDocument)
    (entry: string)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, ExecutionError> =
    runEntryWithPrimitiveAndHost primitive (fun word _ -> Error("unresolved word: " + word)) image entry state

let runEntry
    (image: ImageDocument)
    (entry: string)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, ExecutionError> =
    runEntryWithPrimitive (fun descriptor _ -> Error(describeError (UnresolvedWord descriptor.Name))) image entry state

/// The textual result of a run: the top scalar, or the empty string when the
/// stack is empty. Text values are decoded through the store's text cache.
let resultText (state: KernelState<'Host>) : string =
    match Stack.popResolved state.Store state.Stack with
    | None -> ""
    | Some(tensor, _) when Tensor.rank tensor = 0 && Tensor.size tensor > 0 ->
        (Tensor.item tensor 0).ToString("G", System.Globalization.CultureInfo.InvariantCulture)
    | Some(_, _) -> "value"
