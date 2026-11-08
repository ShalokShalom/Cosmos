module IlWriter

// ============================================================
// D134: the JIT-only IL writer over the runtime harness.
//
// One method per LoweredUnit, plus a Run entry method. A MakeClosure
// immediately followed by CallClosure compiles to a direct call to the unit
// method: the captures travel as an EntryIdentity[], EnterUnit pops the
// arguments, prepends the captures, and installs the frame, and ExitUnit pops
// it. A first-class closure (MakeClosure not immediately called) is refused,
// so the interpreter remains the engine for closures that escape.
//
// The project includes this file only when EnableWriter=true (Debug); the AOT
// publish excludes it (D84, D126). It matches Instruction, never Expression.
// ============================================================

open System
open System.Reflection
open System.Reflection.Emit
open Types
open KernelState
open LoweredInstructions
open EvaluatorLowering
open WriterHarness

type WriterRefusal =
    | UnsupportedOpcode of name: string
    | NegativeSlot of slot: int

let describeRefusal (refusal: WriterRefusal) : string =
    match refusal with
    | UnsupportedOpcode name ->
        "the " + name + " opcode is outside the emitted subset; use the interpreter"
    | NegativeSlot slot -> "the slot " + string slot + " is negative"

type EmittedProgram =
    {
        AssemblyBytes: byte[]
        TypeName: string
        MethodName: string
    }

let private opcodeName (instruction: Instruction) : string =
    match instruction with
    | LiteralNumber _ -> "literal-number"
    | LiteralText _ -> "literal-text"
    | PushSlot _ -> "push-slot"
    | StoreSlot _ -> "store-slot"
    | CallWord _ -> "call-word"
    | Apply -> "apply"
    | Duplicate -> "duplicate"
    | Swap -> "swap"
    | Drop -> "drop"
    | Over -> "over"
    | Rotate -> "rotate"
    | PushRoot _ -> "push-root"
    | SetRoot _ -> "set-root"
    | LookupPair _ -> "lookup-pair"
    | AddPair _ -> "add-pair"
    | DefineWord -> "define-word"
    | MakeClosure _ -> "make-closure"
    | CallClosure -> "call-closure"
    | PushCapture _ -> "push-capture"
    | MakeCode _ -> "make-code"
    | TensorRank -> "tensor-rank"
    | TensorSize -> "tensor-size"
    | TensorItem -> "tensor-item"
    | TensorEntry -> "tensor-entry"
    | TextRead -> "text-read"
    | TextEquals -> "text-equals"
    | IsText -> "is-text"
    | Add -> "add"
    | Subtract -> "subtract"
    | Multiply -> "multiply"
    | Divide -> "divide"
    | LessThan -> "less-than"
    | GreaterThan -> "greater-than"
    | Equals -> "equals"
    | Jump _ -> "jump"
    | JumpIfFalse _ -> "jump-if-false"
    | Label _ -> "label"
    | Fail _ -> "fail"

let emit (program: LoweredProgram) : Result<EmittedProgram, WriterRefusal> =
    let runtimeMethod (name: string) : MethodInfo = typeof<WriterRuntime>.GetMethod(name)

    let popType = typeof<EntryIdentity * KernelState<EvaluationState>>
    let popIdentity = popType.GetProperty("Item1").GetGetMethod()
    let popState = popType.GetProperty("Item2").GetGetMethod()

    let numberType = typeof<double * KernelState<EvaluationState>>
    let numberValue = numberType.GetProperty("Item1").GetGetMethod()
    let numberState = numberType.GetProperty("Item2").GetGetMethod()

    let stateType = typeof<KernelState<EvaluationState>>
    let entryType = typeof<EntryIdentity>
    let capturesType = typeof<EntryIdentity[]>
    let assemblyName = AssemblyName("Scale.Generated." + Guid.NewGuid().ToString("N"))

    let builder =
        PersistedAssemblyBuilder(
            assemblyName,
            typeof<obj>.Assembly,
            ([] :> System.Collections.Generic.IEnumerable<CustomAttributeBuilder>)
        )

    let moduleBuilder = builder.DefineDynamicModule(assemblyName.Name)

    let typeBuilder =
        moduleBuilder.DefineType(
            "Scale.Generated.Program",
            TypeAttributes.Public ||| TypeAttributes.Abstract ||| TypeAttributes.Sealed
        )

    let units =
        program.Units
        |> List.mapi (fun index unit ->
            let methodBuilder =
                typeBuilder.DefineMethod(
                    "Unit" + string index,
                    MethodAttributes.Public ||| MethodAttributes.Static,
                    stateType,
                    [| stateType; capturesType |]
                )

            (unit.Name, methodBuilder))
        |> Map.ofList

    let emitMethod
        (methodBuilder: MethodBuilder)
        (body: Instruction list)
        (unitArity: int option)
        : Result<unit, WriterRefusal> =
        let il = methodBuilder.GetILGenerator()
        let state = il.DeclareLocal(stateType)
        let slots = System.Collections.Generic.Dictionary<int, LocalBuilder>()
        let labels = System.Collections.Generic.Dictionary<int, Label>()

        let getSlot (slot: int) : LocalBuilder =
            match slots.TryGetValue slot with
            | true, local -> local
            | _ ->
                let local = il.DeclareLocal(entryType)
                slots.Add(slot, local)
                local

        let getLabel (label: int) : Label =
            match labels.TryGetValue label with
            | true, target -> target
            | _ ->
                let target = il.DefineLabel()
                labels.Add(label, target)
                target

        let emitStateHelper (name: string) =
            il.Emit(OpCodes.Ldloc, state)
            il.Emit(OpCodes.Call, runtimeMethod name)
            il.Emit(OpCodes.Stloc, state)

        let rec emitInstructions (remaining: Instruction list) : Result<unit, WriterRefusal> =
            match remaining with
            | [] -> Ok()
            | MakeClosure(name, captures, _) :: CallClosure :: rest ->
                match units.TryFind name with
                | None -> Error(UnsupportedOpcode "make-closure")
                | Some unitMethod ->
                    il.Emit(OpCodes.Ldloc, state)
                    il.Emit(OpCodes.Ldc_I4, List.length captures)
                    il.Emit(OpCodes.Newarr, entryType)

                    captures
                    |> List.iteri (fun index slot ->
                        il.Emit(OpCodes.Dup)
                        il.Emit(OpCodes.Ldc_I4, index)
                        il.Emit(OpCodes.Ldloc, getSlot slot)
                        il.Emit(OpCodes.Stelem, entryType))

                    il.Emit(OpCodes.Call, unitMethod)
                    il.Emit(OpCodes.Stloc, state)
                    emitInstructions rest
            | MakeClosure _ :: _ -> Error(UnsupportedOpcode "make-closure")
            | CallClosure :: _ -> Error(UnsupportedOpcode "call-closure")
            | PushCapture index :: rest ->
                il.Emit(OpCodes.Ldloc, state)
                il.Emit(OpCodes.Ldc_I4, index)
                il.Emit(OpCodes.Call, runtimeMethod "PushCapture")
                il.Emit(OpCodes.Stloc, state)
                emitInstructions rest
            | LiteralNumber value :: rest ->
                il.Emit(OpCodes.Ldloc, state)
                il.Emit(OpCodes.Ldc_R8, value)
                il.Emit(OpCodes.Call, runtimeMethod "LiteralNumber")
                il.Emit(OpCodes.Stloc, state)
                emitInstructions rest
            | LiteralText value :: rest ->
                il.Emit(OpCodes.Ldloc, state)
                il.Emit(OpCodes.Ldstr, value)
                il.Emit(OpCodes.Call, runtimeMethod "LiteralText")
                il.Emit(OpCodes.Stloc, state)
                emitInstructions rest
            | Add :: rest ->
                emitStateHelper "Add"
                emitInstructions rest
            | Subtract :: rest ->
                emitStateHelper "Subtract"
                emitInstructions rest
            | Multiply :: rest ->
                emitStateHelper "Multiply"
                emitInstructions rest
            | Divide :: rest ->
                emitStateHelper "Divide"
                emitInstructions rest
            | LessThan :: rest ->
                emitStateHelper "LessThan"
                emitInstructions rest
            | GreaterThan :: rest ->
                emitStateHelper "GreaterThan"
                emitInstructions rest
            | Equals :: rest ->
                emitStateHelper "Equals"
                emitInstructions rest
            | Duplicate :: rest ->
                emitStateHelper "Duplicate"
                emitInstructions rest
            | Drop :: rest ->
                emitStateHelper "Drop"
                emitInstructions rest
            | Swap :: rest ->
                emitStateHelper "Swap"
                emitInstructions rest
            | Over :: rest ->
                emitStateHelper "Over"
                emitInstructions rest
            | Rotate :: rest ->
                emitStateHelper "Rotate"
                emitInstructions rest
            | CallWord(name, _) :: rest ->
                il.Emit(OpCodes.Ldloc, state)
                il.Emit(OpCodes.Ldstr, name)
                il.Emit(OpCodes.Call, runtimeMethod "CallWord")
                il.Emit(OpCodes.Stloc, state)
                emitInstructions rest
            | PushSlot slot :: rest ->
                if slot < 0 then
                    Error(NegativeSlot slot)
                else
                    il.Emit(OpCodes.Ldloc, state)
                    il.Emit(OpCodes.Ldloc, getSlot slot)
                    il.Emit(OpCodes.Call, runtimeMethod "Push")
                    il.Emit(OpCodes.Stloc, state)
                    emitInstructions rest
            | StoreSlot slot :: rest ->
                if slot < 0 then
                    Error(NegativeSlot slot)
                else
                    il.Emit(OpCodes.Ldloc, state)
                    il.Emit(OpCodes.Call, runtimeMethod "Pop")
                    il.Emit(OpCodes.Dup)
                    il.Emit(OpCodes.Call, popState)
                    il.Emit(OpCodes.Stloc, state)
                    il.Emit(OpCodes.Call, popIdentity)
                    il.Emit(OpCodes.Stloc, getSlot slot)
                    emitInstructions rest
            | Jump target :: rest ->
                il.Emit(OpCodes.Br, getLabel target)
                emitInstructions rest
            | JumpIfFalse target :: rest ->
                il.Emit(OpCodes.Ldloc, state)
                il.Emit(OpCodes.Call, runtimeMethod "PopNumber")
                il.Emit(OpCodes.Dup)
                il.Emit(OpCodes.Call, numberState)
                il.Emit(OpCodes.Stloc, state)
                il.Emit(OpCodes.Call, numberValue)
                il.Emit(OpCodes.Ldc_R8, 0.0)
                il.Emit(OpCodes.Beq, getLabel target)
                emitInstructions rest
            | Label label :: rest ->
                il.MarkLabel(getLabel label)
                emitInstructions rest
            | Fail reason :: rest ->
                il.Emit(OpCodes.Ldloc, state)
                il.Emit(OpCodes.Ldstr, reason)
                il.Emit(OpCodes.Call, runtimeMethod "Fail")
                il.Emit(OpCodes.Stloc, state)
                emitInstructions rest
            | unsupported :: _ -> Error(UnsupportedOpcode(opcodeName unsupported))

        il.Emit(OpCodes.Ldarg_0)
        il.Emit(OpCodes.Stloc, state)

        match unitArity with
        | Some arity ->
            il.Emit(OpCodes.Ldloc, state)
            il.Emit(OpCodes.Ldc_I4, arity)
            il.Emit(OpCodes.Ldarg_1)
            il.Emit(OpCodes.Call, runtimeMethod "EnterUnit")
            il.Emit(OpCodes.Stloc, state)
        | None -> ()

        match emitInstructions body with
        | Error refusal -> Error refusal
        | Ok() ->
            match unitArity with
            | Some _ ->
                il.Emit(OpCodes.Ldloc, state)
                il.Emit(OpCodes.Call, runtimeMethod "ExitUnit")
                il.Emit(OpCodes.Stloc, state)
            | None -> ()

            il.Emit(OpCodes.Ldloc, state)
            il.Emit(OpCodes.Ret)
            Ok()

    let emitUnits =
        program.Units
        |> List.fold
            (fun (accumulated: Result<unit, WriterRefusal>) unit ->
                match accumulated with
                | Error refusal -> Error refusal
                | Ok() ->
                    match units.TryFind unit.Name with
                    | None -> Error(UnsupportedOpcode "make-closure")
                    | Some methodBuilder -> emitMethod methodBuilder unit.Instructions (Some unit.Arity))
            (Ok())

    match emitUnits with
    | Error refusal -> Error refusal
    | Ok() ->
        let runBuilder =
            typeBuilder.DefineMethod(
                "Run",
                MethodAttributes.Public ||| MethodAttributes.Static,
                stateType,
                [| stateType |]
            )

        match emitMethod runBuilder program.Instructions None with
        | Error refusal -> Error refusal
        | Ok() ->
            typeBuilder.CreateType() |> ignore

            use stream = new System.IO.MemoryStream()
            builder.Save(stream)

            Ok
                {
                    AssemblyBytes = stream.ToArray()
                    TypeName = "Scale.Generated.Program"
                    MethodName = "Run"
                }
