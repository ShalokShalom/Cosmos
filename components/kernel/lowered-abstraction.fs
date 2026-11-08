module LoweredAbstraction

// ============================================================
// The P1 execution loop for the lowered instruction set.
//
// The abstraction owns local slots, the operand stack, and the pending program. Word
// resolution is the supplied primitive callback, so the abstraction is independent of
// any Prelude state: the callback receives and returns the whole
// `KernelState<'Host>`, and the host value travels with it. Labels are resolved
// by dropping to the matching marker.
// ============================================================

open Types
open KernelState
open LoweredInstructions

type AbstractionError =
    | LoweredFailure of reason: string
    | HostFailure of reason: string

let private runInstruction
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (guardValid: CallGuard -> bool)
    (slots: Map<Slot, EntryIdentity>)
    (state: KernelState<'Host>)
    (instruction: Instruction)
    : Result<Map<Slot, EntryIdentity> * KernelState<'Host>, AbstractionError> =
    let pushTensor (tensor: Tensor) (current: KernelState<'Host>) =
        let (identity, store) = Store.addTensor tensor current.Store
        { current with Store = store; Stack = Stack.push identity current.Stack }

    let popTwo (current: KernelState<'Host>) =
        match Stack.popResolved current.Store current.Stack with
        | Some(right, rest) ->
            match Stack.popResolved current.Store rest with
            | Some(left, rest2) -> Ok(left, right, rest2)
            | None -> Error(LoweredFailure "binary operation has one operand")
        | None -> Error(LoweredFailure "binary operation has no operands")

    let binary name (operation: double -> double -> double) (current: KernelState<'Host>) =
        match popTwo current with
        | Error error -> Error error
        | Ok(left, right, rest) ->
            if Tensor.rank left = 0 && Tensor.rank right = 0 then
                let value = operation (Tensor.item left 0) (Tensor.item right 0)
                Ok(slots, pushTensor (Tensor.scalar value) { current with Stack = rest })
            elif left.Shape = right.Shape then
                let data = Array.map2 operation left.Data right.Data

                Ok(
                    slots,
                    pushTensor { Data = data; Shape = Array.copy left.Shape } { current with Stack = rest }
                )
            else
                Error(LoweredFailure(name + " requires equal shapes or two scalars"))

    match instruction with
    | LiteralNumber value ->
        Ok(slots, pushTensor (Tensor.scalar value) state)
    | LiteralText value ->
        let (identity, store) = Store.addText value state.Store
        Ok(slots, { state with Store = store; Stack = Stack.push identity state.Stack })
    | PushSlot slot ->
        match slots.TryFind slot with
        | Some identity -> Ok(slots, { state with Stack = Stack.push identity state.Stack })
        | None -> Error(LoweredFailure("slot " + string slot + " is not stored"))
    | StoreSlot slot ->
        match Stack.popIndex state.Stack with
        | Some(identity, remaining) -> Ok(slots.Add(slot, identity), { state with Stack = remaining })
        | None -> Error(LoweredFailure("store-slot " + string slot + " has no value"))
    | Duplicate ->
        match Stack.popIndex state.Stack with
        | Some(identity, remaining) ->
            Ok(slots, { state with Stack = Stack.push identity (Stack.push identity remaining) })
        | None -> Error(LoweredFailure "duplicate has no operand")
    | Drop ->
        match Stack.popIndex state.Stack with
        | Some(_, remaining) -> Ok(slots, { state with Stack = remaining })
        | None -> Error(LoweredFailure "drop has no operand")
    | Swap ->
        match Stack.popIndex state.Stack with
        | Some(first, rest) ->
            match Stack.popIndex rest with
            | Some(second, rest2) ->
                Ok(slots, { state with Stack = Stack.push second (Stack.push first rest2) })
            | None -> Error(LoweredFailure "swap needs two operands")
        | None -> Error(LoweredFailure "swap has no operands")
    | Over ->
        match Stack.popIndex state.Stack with
        | Some(first, rest) ->
            match Stack.popIndex rest with
            | Some(second, rest2) ->
                Ok(slots, { state with Stack = Stack.push first (Stack.push second (Stack.push first rest2)) })
            | None -> Error(LoweredFailure "over needs two operands")
        | None -> Error(LoweredFailure "over has no operands")
    | Rotate ->
        match Stack.popIndex state.Stack with
        | Some(first, rest) ->
            match Stack.popIndex rest with
            | Some(second, rest2) ->
                match Stack.popIndex rest2 with
                | Some(third, rest3) ->
                    Ok(slots, { state with Stack = Stack.push third (Stack.push first (Stack.push second rest3)) })
                | None -> Error(LoweredFailure "rotate needs three operands")
            | None -> Error(LoweredFailure "rotate needs three operands")
        | None -> Error(LoweredFailure "rotate needs three operands")
    | PushRoot name ->
        let value =
            match state.Roots.TryFind name with
            | Some(Some root) -> double (EntryIdentity.value root)
            | _ -> -1.0

        Ok(slots, pushTensor (Tensor.scalar value) state)
    | SetRoot name ->
        match Stack.popResolved state.Store state.Stack with
        | Some(tensor, rest) when Tensor.rank tensor = 0 && Tensor.size tensor > 0 ->
            let value = Tensor.item tensor 0

            let root =
                if value = -1.0 then
                    None
                elif value >= 0.0 && value = floor value then
                    Some(EntryIdentity.ofInt (int value))
                else
                    None

            Ok(slots, { state with Stack = rest; Roots = state.Roots.Add(name, root) })
        | _ -> Error(LoweredFailure("set-root " + name + " needs a scalar root"))
    | LookupPair rootName ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure("lookup-pair " + rootName + " has no name"))
        | Some(nameId, rest) ->
            let root = state.Roots.TryFind rootName |> Option.defaultValue None

            match Store.findPair nameId root state.Store with
            | Some value -> Ok(slots, { state with Stack = Stack.push value rest })
            | None -> Ok(slots, pushTensor (Tensor.scalar -1.0) { state with Stack = rest })
    | AddPair rootName ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure("add-pair " + rootName + " has no value"))
        | Some(valueId, rest) ->
            match Stack.popIndex rest with
            | None -> Error(LoweredFailure("add-pair " + rootName + " has no name"))
            | Some(nameId, rest2) ->
                let root = state.Roots.TryFind rootName |> Option.defaultValue None
                let (newRoot, store) = Store.addToEnvironment nameId valueId root state.Store

                Ok(
                    slots,
                    pushTensor
                        (Tensor.scalar (double (EntryIdentity.value newRoot)))
                        { state with
                            Store = store
                            Stack = rest2
                            Roots = state.Roots.Add(rootName, Some newRoot)
                        }
                )
    | DefineWord ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure "define has no body")
        | Some(bodyId, rest) ->
            match Stack.popIndex rest with
            | None -> Error(LoweredFailure "define has no name")
            | Some(nameId, rest2) ->
                match Store.readTextAt nameId state.Store, Store.readTextAt bodyId state.Store with
                | Some name, Some body ->
                    let instructions = lowerTokens (body.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries) |> Array.toList)
                    Ok(slots, { state with Stack = rest2; Words = state.Words.Add(name, instructions) })
                | _ -> Error(LoweredFailure "define needs a name and a body text")
    | MakeClosure(name, captures, arity) ->
        let captured =
            captures
            |> List.map (fun slot ->
                match slots.TryFind slot with
                | Some identity -> Ok identity
                | None -> Error(LoweredFailure("closure capture slot " + string slot + " is not stored")))
            |> List.fold
                (fun accumulated result ->
                    match accumulated, result with
                    | Error error, _ -> Error error
                    | _, Error error -> Error error
                    | Ok identities, Ok identity -> Ok(identity :: identities))
                (Ok [])
            |> Result.map List.rev

        match captured with
        | Error error -> Error error
        | Ok identities ->
            let (nameId, store1) = Store.addText name state.Store

            let data =
                LoweredInstructions.ClosureTag
                :: double (EntryIdentity.value nameId)
                :: double arity
                :: double (List.length identities)
                :: (identities |> List.map (fun identity -> double (EntryIdentity.value identity)))
                |> Array.ofList

            let (closureId, store2) = Store.addTensor { Data = data; Shape = [| data.Length |] } store1
            Ok(slots, { state with Store = store2; Stack = Stack.push closureId state.Stack })
    | CallClosure ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure "call-closure has no closure")
        | Some(closureId, rest) ->
            match Store.lookupTensor closureId state.Store with
            | Some tensor when Tensor.rank tensor = 1 && Tensor.size tensor >= 4 && Tensor.item tensor 0 = LoweredInstructions.ClosureTag ->
                let nameId = EntryIdentity.ofInt (int (Tensor.item tensor 1))
                let arity = int (Tensor.item tensor 2)
                let captureCount = int (Tensor.item tensor 3)

                let captures =
                    tensor.Data
                    |> Array.skip 4
                    |> Array.take captureCount
                    |> Array.toList
                    |> List.map (fun value -> EntryIdentity.ofInt (int value))

                let rec popArguments count remainingStack accumulated =
                    if count = 0 then
                        Some(accumulated, remainingStack)
                    else
                        match Stack.popIndex remainingStack with
                        | Some(identity, remaining) -> popArguments (count - 1) remaining (identity :: accumulated)
                        | None -> None

                match Store.readTextAt nameId state.Store with
                | None -> Error(LoweredFailure "call-closure names a non-text unit")
                | Some name ->
                    match popArguments arity rest [] with
                    | None ->
                        Error(
                            LoweredFailure(
                                "call-closure needs "
                                + string arity
                                + " arguments, "
                                + string (Stack.depth rest)
                                + " available"
                            )
                        )
                    | Some(arguments, remaining) ->
                        let frame = arguments @ captures

                        let withFrame =
                            { state with
                                Stack = remaining
                                CaptureFrames = frame :: state.CaptureFrames
                            }

                        match executeWord name withFrame with
                        | Error error -> Error(HostFailure error)
                        | Ok after ->
                            let frames =
                                match after.CaptureFrames with
                                | _ :: tail -> tail
                                | [] -> []

                            Ok(slots, { after with CaptureFrames = frames })
            | _ -> Error(LoweredFailure "call-closure has a malformed closure")
    | PushCapture index ->
        match state.CaptureFrames with
        | frame :: _ ->
            match List.tryItem index frame with
            | Some identity -> Ok(slots, { state with Stack = Stack.push identity state.Stack })
            | None -> Error(LoweredFailure("closure capture " + string index + " is not present"))
        | [] -> Error(LoweredFailure "push-capture has no closure frame")
    | TensorRank ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure "tensor-rank has no identity")
        | Some(identity, rest) ->
            match Store.lookupTensor identity state.Store with
            | Some tensor -> Ok(slots, pushTensor (Tensor.scalar (double (Tensor.rank tensor))) { state with Stack = rest })
            | None -> Error(LoweredFailure "tensor-rank has no tensor")
    | TensorSize ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure "tensor-size has no identity")
        | Some(identity, rest) ->
            match Store.lookupTensor identity state.Store with
            | Some tensor -> Ok(slots, pushTensor (Tensor.scalar (double (Tensor.size tensor))) { state with Stack = rest })
            | None -> Error(LoweredFailure "tensor-size has no tensor")
    | TensorItem ->
        match Stack.popResolved state.Store state.Stack with
        | None -> Error(LoweredFailure "tensor-item has no index")
        | Some(indexTensor, rest) ->
            match ScalarRead.readScalar indexTensor with
            | Error reason -> Error(LoweredFailure reason)
            | Ok indexValue ->
                let index = int indexValue

                match Stack.popIndex rest with
                | None -> Error(LoweredFailure "tensor-item has no identity")
                | Some(identity, rest2) ->
                    match Store.lookupTensor identity state.Store with
                    | Some tensor when index >= 0 && index < Tensor.size tensor ->
                        Ok(slots, pushTensor (Tensor.scalar (Tensor.item tensor index)) { state with Stack = rest2 })
                    | Some _ -> Error(LoweredFailure "tensor-item index is out of range")
                    | None -> Error(LoweredFailure "tensor-item has no tensor")
    | TensorEntry ->
        match Stack.popResolved state.Store state.Stack with
        | None -> Error(LoweredFailure "tensor-entry has no index")
        | Some(indexTensor, rest) ->
            match ScalarRead.readScalar indexTensor with
            | Error reason -> Error(LoweredFailure reason)
            | Ok indexValue ->
                let index = int indexValue

                match Stack.popIndex rest with
                | None -> Error(LoweredFailure "tensor-entry has no identity")
                | Some(identity, rest2) ->
                    match Store.lookupTensor identity state.Store with
                    | Some tensor when index >= 0 && index < Tensor.size tensor ->
                        let entry = EntryIdentity.ofInt (int (Tensor.item tensor index))
                        Ok(slots, { state with Stack = Stack.push entry rest2 })
                    | Some _ -> Error(LoweredFailure "tensor-entry index is out of range")
                    | None -> Error(LoweredFailure "tensor-entry has no tensor")
    | TextRead ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure "text-read has no identity")
        | Some(identity, rest) ->
            match Store.readTextAt identity state.Store with
            | None -> Error(LoweredFailure "text-read has no text")
            | Some text ->
                let (textId, store) = Store.addText text state.Store
                Ok(slots, { state with Store = store; Stack = Stack.push textId rest })
    | TextEquals ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure "text-equals has no right operand")
        | Some(right, rest) ->
            match Stack.popIndex rest with
            | None -> Error(LoweredFailure "text-equals has no left operand")
            | Some(left, rest2) ->
                match Store.readTextAt left state.Store, Store.readTextAt right state.Store with
                | Some leftText, Some rightText ->
                    Ok(slots, pushTensor (Tensor.scalar (if leftText = rightText then 1.0 else 0.0)) { state with Stack = rest2 })
                | _ -> Error(LoweredFailure "text-equals needs two text operands")
    | IsText ->
        match Stack.popIndex state.Stack with
        | None -> Error(LoweredFailure "is-text has no identity")
        | Some(identity, rest) ->
            let flag =
                match Store.readTextAt identity state.Store with
                | Some _ -> 1.0
                | None -> 0.0

            Ok(slots, pushTensor (Tensor.scalar flag) { state with Stack = rest })
    | Add -> binary "add" (+) state
    | Subtract -> binary "subtract" (-) state
    | Multiply -> binary "multiply" (*) state
    | Divide ->
        match popTwo state with
        | Error error -> Error error
        | Ok(left, right, rest) ->
            let divides value divisor =
                if divisor = 0.0 then
                    Error(LoweredFailure "divide by zero")
                else
                    Ok(value / divisor)

            if Tensor.rank left = 0 && Tensor.rank right = 0 then
                divides (Tensor.item left 0) (Tensor.item right 0)
                |> Result.map (fun value -> slots, pushTensor (Tensor.scalar value) { state with Stack = rest })
            elif left.Shape = right.Shape then
                let values = Array.map2 (fun value divisor -> value, divisor) left.Data right.Data
                let result = values |> Array.map (fun (value, divisor) -> divides value divisor)
                let errors = result |> Array.choose (function Error error -> Some error | Ok _ -> None)

                match errors |> Array.tryHead with
                | Some error -> Error error
                | None ->
                    let data = result |> Array.choose (function Ok value -> Some value | Error _ -> None)
                    Ok(slots, pushTensor { Data = data; Shape = Array.copy left.Shape } { state with Stack = rest })
            else
                Error(LoweredFailure "divide requires equal shapes or two scalars")
    | LessThan ->
        match popTwo state with
        | Error error -> Error error
        | Ok(left, right, rest) ->
            if Tensor.rank left = 0 && Tensor.rank right = 0 then
                let value = if Tensor.item left 0 < Tensor.item right 0 then 1.0 else 0.0
                Ok(slots, pushTensor (Tensor.scalar value) { state with Stack = rest })
            else
                Error(LoweredFailure "less-than requires two scalars")
    | GreaterThan ->
        match popTwo state with
        | Error error -> Error error
        | Ok(left, right, rest) ->
            if Tensor.rank left = 0 && Tensor.rank right = 0 then
                let value = if Tensor.item left 0 > Tensor.item right 0 then 1.0 else 0.0
                Ok(slots, pushTensor (Tensor.scalar value) { state with Stack = rest })
            else
                Error(LoweredFailure "greater-than requires two scalars")
    | Equals ->
        match popTwo state with
        | Error error -> Error error
        | Ok(left, right, rest) ->
            if Tensor.rank left = 0 && Tensor.rank right = 0 then
                let value = if Tensor.item left 0 = Tensor.item right 0 then 1.0 else 0.0
                Ok(slots, pushTensor (Tensor.scalar value) { state with Stack = rest })
            else
                Error(LoweredFailure "equals requires two scalars")
    | CallWord(name, guard) ->
        let target =
            match guard with
            | Some cached when cached.Target <> "" && guardValid cached -> cached.Target
            | _ -> name

        executeWord target state
        |> Result.mapError HostFailure
        |> Result.map (fun next -> slots, next)
    | RecordStep step -> Ok(slots, { state with History = step :: state.History })
    | MakeCode body ->
        let (documentId, storeWithDocument) =
            Store.addText (PreludeImage.toInstructionDocumentJson body) state.Store

        Ok(
            slots,
            pushTensor
                (Tensor.vector
                    [
                        CodeValueTag
                        double (EntryIdentity.value documentId)
                    ])
                { state with Store = storeWithDocument }
        )
    | Apply
    | Jump _
    | JumpIfFalse _
    | Label _ -> Error(LoweredFailure "a control instruction reached the value executor")
    | Fail reason -> Error(LoweredFailure reason)

let private dropTo (label: LabelId) (instructions: Instruction list) : Result<Instruction list, AbstractionError> =
    let rec loop (remaining: Instruction list) =
        match remaining with
        | [] -> Error(LoweredFailure("jump target " + string label + " is not present"))
        | Label found :: rest when found = label -> Ok rest
        | _ :: rest -> loop rest

    loop instructions

let runWithGuardAndClosure
    (guardValid: CallGuard -> bool)
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (executeClosure: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (instructions: Instruction list)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, AbstractionError> =
    let rec loop
        (slots: Map<Slot, EntryIdentity>)
        (current: KernelState<'Host>)
        (remaining: Instruction list)
        : Result<KernelState<'Host>, AbstractionError> =
        match remaining with
        | [] -> Ok current
        | Label _ :: rest -> loop slots current rest
        | Jump target :: _ ->
            dropTo target remaining
            |> Result.bind (fun next -> loop slots current next)
        | JumpIfFalse target :: rest ->
            match Stack.popResolved current.Store current.Stack with
            | Some(tensor, remainingStack) when Tensor.rank tensor = 0 && Tensor.size tensor > 0 ->
                let condition = Tensor.item tensor 0
                let afterPop = { current with Stack = remainingStack }

                if condition = 0.0 then
                    dropTo target remaining |> Result.bind (fun next -> loop slots afterPop next)
                else
                    loop slots afterPop rest
            | _ -> Error(LoweredFailure "jump-if-false has no scalar condition")
        | Apply :: rest ->
            match Stack.popIndex current.Stack with
            | None -> Error(LoweredFailure "apply has no target")
            | Some(target, remainingStack) ->
                let withTarget = { current with Stack = Stack.push target remainingStack }

                match Store.lookupTensor target current.Store with
                | Some tensor when
                    Tensor.rank tensor = 1
                    && Tensor.size tensor >= 4
                    && Tensor.item tensor 0 = LoweredInstructions.ClosureTag
                    ->
                    match runInstruction executeClosure guardValid slots withTarget CallClosure with
                    | Error error -> Error error
                    | Ok(nextSlots, nextState) -> loop nextSlots nextState rest
                | _ ->
                    match executeWord "apply" withTarget with
                    | Error error -> Error(HostFailure error)
                    | Ok applied ->
                        let loweredBody = lowerTokens applied.Program

                        loop slots { applied with Program = []; Stack = applied.Stack } (loweredBody @ rest)
        | CallClosure :: rest ->
            match runInstruction executeClosure guardValid slots current CallClosure with
            | Error error -> Error error
            | Ok(nextSlots, nextState) -> loop nextSlots nextState rest
        | instruction :: rest ->
            match runInstruction executeWord guardValid slots current instruction with
            | Error error -> Error error
            | Ok(nextSlots, nextState) -> loop nextSlots nextState rest

    loop Map.empty state instructions

/// The call-vs-closure split: a word call goes through the word resolver, while
/// a closure invocation runs the already-framed unit without another arity frame.
let runWithClosure
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (executeClosure: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (instructions: Instruction list)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, AbstractionError> =
    runWithGuardAndClosure (fun _ -> false) executeWord executeClosure instructions state

let runWithGuard
    (guardValid: CallGuard -> bool)
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (instructions: Instruction list)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, AbstractionError> =
    runWithGuardAndClosure guardValid executeWord executeWord instructions state

/// Execute one value instruction against an empty slot map. The writer runtime
/// uses this so the interpreter and emitted code share one semantics (D128).
let step
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (instruction: Instruction)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, AbstractionError> =
    runInstruction executeWord (fun _ -> false) Map.empty state instruction
    |> Result.map snd

/// The default abstraction treats every cache guard as stale, so a cached target is
/// never used without an explicit guard policy.
let run
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (instructions: Instruction list)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, AbstractionError> =
    runWithGuard (fun _ -> false) executeWord instructions state

/// Resume at a recorded position: skip the instructions before `start` and run
/// the rest. A position outside the program is a named refusal (D201, superseding D120).
let runFrom
    (start: int)
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (instructions: Instruction list)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, AbstractionError> =
    if start < 0 || start > List.length instructions then
        Error(LoweredFailure("resume position " + string start + " is outside the program"))
    else
        run executeWord (List.skip start instructions) state

/// The positions a run recorded, oldest first.
let recordedSteps (state: KernelState<'Host>) : int list = List.rev state.History

[<Literal>]
let MaxUnitDepth = 256

/// Run a program that carries named units (lowered lambdas). A word that names a
/// unit runs that unit's instructions in the same machine; any other word goes
/// to the supplied callback. Nested unit calls share the depth bound.
let runProgram
    (units: Map<string, Instruction list>)
    (executeWord: string -> KernelState<'Host> -> Result<KernelState<'Host>, string>)
    (instructions: Instruction list)
    (state: KernelState<'Host>)
    : Result<KernelState<'Host>, AbstractionError> =
    let rec execute depth token current =
        match units.TryFind token with
        | None -> executeWord token current
        | Some unitInstructions ->
            if depth >= MaxUnitDepth then
                Error("call depth exceeded: " + string MaxUnitDepth)
            else
                match runWithGuard (fun _ -> false) (execute (depth + 1)) unitInstructions current with
                | Ok next -> Ok next
                | Error(LoweredFailure reason) -> Error reason
                | Error(HostFailure reason) -> Error reason

    runWithGuard (fun _ -> false) (execute 0) instructions state
