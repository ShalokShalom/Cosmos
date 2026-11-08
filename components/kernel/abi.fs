// UnmanagedCallersOnly is honored by NativeAOT at the IL boundary; the F#
// compiler still reports it as unsupported (FS0202), so the warning is noise
// for this file only.
#nowarn "202"

namespace Scale.Kernel

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Text
open Types
open KernelState

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type HostDispatch =
    delegate of
        nativeint * nativeint * unativeint * nativeint * unativeint * nativeint * unativeint * nativeint * nativeint ->
        int32

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type HostAlloc =
    delegate of unativeint -> nativeint

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type HostFree =
    delegate of nativeint -> unit

module Abi =
    [<Literal>]
    let Version = 4

    [<Literal>]
    let StatusOk = 0

    [<Literal>]
    let StatusError = 1

    [<Literal>]
    let StatusPanic = 2

    [<Literal>]
    let StatusInvalid = 3

    [<Literal>]
    let StatusUnimplemented = 4

    [<Literal>]
    let ImageExport = 0

    [<Literal>]
    let ImageImport = 1

    [<Literal>]
    let ImageReplaceUnit = 2

    [<Literal>]
    let ImagePatchUnit = 3

    /// One independent kernel instance. The callback table and the validated
    /// image belong to this handle, not to the process.
    type private KernelHandle =
        { Callbacks: nativeint
          Image: PreludeImage.ImageDocument option
          State: KernelState<unit> option
          Gate: obj }

    let private handleLock = obj ()

    let private handles =
        System.Collections.Generic.Dictionary<nativeint, KernelHandle>()

    let mutable private nextHandle: nativeint = 0n

    let private findHandle (handle: nativeint) : KernelHandle option =
        if handle = 0n then
            None
        else
            lock handleLock (fun () ->
                match handles.TryGetValue handle with
                | true, current -> Some current
                | false, _ -> None)

    let private createHandle
        (callbacks: nativeint)
        (image: PreludeImage.ImageDocument option)
        : nativeint =
        lock handleLock (fun () ->
            nextHandle <- nextHandle + 1n
            let handle = nextHandle
            handles.[handle] <- { Callbacks = callbacks; Image = image; State = None; Gate = obj () }
            handle)

    let private replaceHandle
        (handle: nativeint)
        (update: KernelHandle -> KernelHandle)
        : bool =
        lock handleLock (fun () ->
            match handles.TryGetValue handle with
            | true, current ->
                handles.[handle] <- update current
                true
            | false, _ -> false)

    let private removeHandle (handle: nativeint) : unit =
        lock handleLock (fun () -> handles.Remove handle |> ignore)

    /// The image a live handle validated at `scale_init`. An unknown or freed
    /// handle has no image.
    let currentImage (handle: nativeint) : PreludeImage.ImageDocument option =
        findHandle handle |> Option.bind (fun current -> current.Image)

    let private clearHandle (handleOut: nativeint) =
        if handleOut <> 0n then
            Marshal.WriteIntPtr(handleOut, IntPtr.Zero)

    /// Installs a callback table and returns an opaque handle with no image.
    /// The C `scale_init` path creates the handle with the validated image;
    /// this managed form is for hosts that link the kernel directly.
    let installCallbacks (callbacks: nativeint) (handleOut: nativeint) : int32 =
        if callbacks = 0n || handleOut = 0n then
            clearHandle handleOut
            StatusInvalid
        else
            let handle = createHandle callbacks None
            Marshal.WriteIntPtr(handleOut, handle)
            StatusOk

    let private callHost
        (callbacks: nativeint)
        (packageName: string)
        (functionName: string)
        (arguments: nativeint)
        (argumentLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        if callbacks = 0n then
            StatusInvalid
        else
            let dispatchPointer = Marshal.ReadIntPtr(callbacks, IntPtr.Size * 3)

            if dispatchPointer = 0n then
                StatusInvalid
            else
                let context = Marshal.ReadIntPtr(callbacks)
                let dispatch = Marshal.GetDelegateForFunctionPointer<HostDispatch>(dispatchPointer)
                let packageBytes = Encoding.UTF8.GetBytes(packageName)
                let functionBytes = Encoding.UTF8.GetBytes(functionName)
                let packagePointer = Marshal.AllocHGlobal packageBytes.Length
                let functionPointer = Marshal.AllocHGlobal functionBytes.Length

                try
                    Marshal.Copy(packageBytes, 0, packagePointer, packageBytes.Length)
                    Marshal.Copy(functionBytes, 0, functionPointer, functionBytes.Length)

                    dispatch.Invoke(
                        context,
                        packagePointer,
                        unativeint packageBytes.Length,
                        functionPointer,
                        unativeint functionBytes.Length,
                        arguments,
                        argumentLength,
                        resultOut,
                        resultLengthOut
                    )
                finally
                    Marshal.FreeHGlobal packagePointer
                    Marshal.FreeHGlobal functionPointer

    /// Validates the prelude image in the kernel. A null prelude with length zero
    /// is allowed and yields no image.
    let private loadImage (prelude: nativeint) (preludeLength: unativeint) : Result<PreludeImage.ImageDocument option, string> =
        if prelude = 0n && preludeLength = 0un then
            Ok None
        elif prelude = 0n || preludeLength = 0un then
            Error "a prelude pointer and length must both be present or both be zero"
        elif preludeLength > unativeint PreludeImage.MaxBytes then
            Error "the prelude image exceeds the size limit"
        else
            let bytes = Array.zeroCreate<byte> (int preludeLength)
            Marshal.Copy(prelude, bytes, 0, bytes.Length)

            match PreludeImage.ofBytes bytes with
            | Ok image -> Ok(Some image)
            | Error error -> Error(PreludeImage.describeError error)

    /// Sends the prelude bytes to the host adapter through `host_dispatch` with
    /// package `scale` and function `prelude`. The kernel has already validated
    /// the image; the adapter only receives it during the transition to
    /// kernel-owned execution.
    let loadPreludeThroughHost
        (callbacks: nativeint)
        (prelude: nativeint)
        (preludeLength: unativeint)
        : int32 =
        if prelude = 0n && preludeLength = 0un then
            StatusOk
        elif prelude = 0n || preludeLength = 0un then
            StatusInvalid
        else
            callHost callbacks "scale" "prelude" prelude preludeLength 0n 0n

    /// Forwards execution to the host adapter through `host_dispatch`, using
    /// the package `scale` and the function `run`. The adapter owns the
    /// prelude; the kernel owns the callback table and the result out-params.
    let runThroughHost
        (handle: nativeint)
        (program: nativeint)
        (programLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current ->
            callHost current.Callbacks "scale" "run" program programLength resultOut resultLengthOut

    let private clearResult (resultOut: nativeint) (resultLengthOut: nativeint) =
        if resultOut <> 0n then
            Marshal.WriteIntPtr(resultOut, IntPtr.Zero)

        if resultLengthOut <> 0n then
            Marshal.WriteIntPtr(resultLengthOut, IntPtr.Zero)

    let private writeResult
        (callbacks: nativeint)
        (bytes: byte[])
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        if bytes.Length = 0 then
            clearResult resultOut resultLengthOut
            StatusOk
        else
            let allocPointer = Marshal.ReadIntPtr(callbacks, IntPtr.Size)

            if allocPointer = 0n then
                StatusError
            else
                let alloc = Marshal.GetDelegateForFunctionPointer<HostAlloc>(allocPointer)
                let buffer = alloc.Invoke(unativeint bytes.Length)

                if buffer = 0n then
                    StatusError
                else
                    Marshal.Copy(bytes, 0, buffer, bytes.Length)
                    Marshal.WriteIntPtr(resultOut, buffer)
                    Marshal.WriteIntPtr(resultLengthOut, nativeint bytes.Length)
                    StatusOk

    /// Resolves an exported image unit by name and runs it with the kernel abstraction.
    /// Image-resident words execute recursively; a word with no unit is an
    /// unresolved primitive and returns an error.
    let private executeCurrent
        (handle: nativeint)
        (current: KernelHandle)
        (program: nativeint)
        (programLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        clearResult resultOut resultLengthOut

        match current.Image with
            | None -> StatusUnimplemented
            | Some image ->
                if program = 0n || programLength = 0un || programLength > unativeint 4096 then
                    StatusInvalid
                else
                    let entryBytes = Array.zeroCreate<byte> (int programLength)
                    Marshal.Copy(program, entryBytes, 0, entryBytes.Length)
                    let entry = Encoding.UTF8.GetString entryBytes

                    let primitive
                        (descriptor: PreludeImage.ImagePrimitive)
                        (operands: Tensor list)
                        : Result<Tensor list, string> =
                        match PrimitiveWire.encodeRequest descriptor.Name operands with
                        | None -> Error "a primitive operand is not finite"
                        | Some request ->
                            let requestPointer = Marshal.AllocHGlobal request.Length
                            let resultOut = Marshal.AllocHGlobal(IntPtr.Size)
                            let resultLengthOut = Marshal.AllocHGlobal(IntPtr.Size)

                            try
                                Marshal.Copy(request, 0, requestPointer, request.Length)
                                Marshal.WriteIntPtr(resultOut, IntPtr.Zero)
                                Marshal.WriteIntPtr(resultLengthOut, IntPtr.Zero)

                                let status =
                                    callHost
                                        current.Callbacks
                                        "scale"
                                        descriptor.Name
                                        requestPointer
                                        (unativeint request.Length)
                                        resultOut
                                        resultLengthOut

                                if status <> StatusOk then
                                    Error("host primitive returned status " + string status)
                                else
                                    let resultPointer = Marshal.ReadIntPtr resultOut
                                    let resultLength = int (Marshal.ReadIntPtr(resultLengthOut).ToInt64())

                                    if resultLength > PreludeImage.MaxBytes then
                                        Error "a primitive response exceeds the size limit"
                                    else
                                        let response =
                                            if resultPointer = 0n || resultLength = 0 then
                                                [||]
                                            else
                                                let bytes = Array.zeroCreate<byte> resultLength
                                                Marshal.Copy(resultPointer, bytes, 0, bytes.Length)
                                                bytes

                                        if resultPointer <> 0n then
                                            let freePointer = Marshal.ReadIntPtr(current.Callbacks, IntPtr.Size * 2)

                                            if freePointer <> 0n then
                                                let free = Marshal.GetDelegateForFunctionPointer<HostFree>(freePointer)
                                                free.Invoke resultPointer

                                        PrimitiveWire.decodeResults descriptor.Results response
                            finally
                                Marshal.FreeHGlobal requestPointer
                                Marshal.FreeHGlobal resultOut
                                Marshal.FreeHGlobal resultLengthOut

                    match
                        PreludeExecution.runEntryWithPrimitive
                            primitive
                            image
                            entry
                            (current.State
                             |> Option.defaultValue (KernelState.create () Store.empty [] []))
                    with
                    | Error error ->
                        let errorBytes = Encoding.UTF8.GetBytes(PreludeExecution.describeError error)
                        writeResult current.Callbacks errorBytes resultOut resultLengthOut |> ignore
                        StatusError
                    | Ok state ->
                        let resultBytes = Encoding.UTF8.GetBytes(PreludeExecution.resultText state)

                        let status =
                            writeResult current.Callbacks resultBytes resultOut resultLengthOut

                        if status = StatusOk then
                            replaceHandle handle (fun current -> { current with State = Some state })
                            |> ignore

                        status

    /// Resolves an exported image unit by name and runs it with the kernel
    /// abstraction under the handle gate.
    let execute
        (handle: nativeint)
        (program: nativeint)
        (programLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current when resultOut = 0n || resultLengthOut = 0n -> StatusInvalid
        | Some current ->
            lock current.Gate (fun () ->
                executeCurrent handle current program programLength resultOut resultLengthOut)

    /// The managed form of `scale_abi_version`, shared with hosts that link the
    /// kernel directly. Managed callers must use this, never the
    /// `UnmanagedCallersOnly` export.
    let versionValue () : int32 = Version

    [<UnmanagedCallersOnly(EntryPoint = "scale_abi_version", CallConvs = [| typeof<CallConvCdecl> |])>]
    let abiVersion () : int32 = versionValue ()

    let private outputPairValid (output: nativeint) (outputLength: nativeint) : bool =
        (output = 0n && outputLength = 0n)
        || (output <> 0n && outputLength <> 0n)

    let private writePreviousImage
        (current: KernelHandle)
        (previous: PreludeImage.ImageDocument option)
        (output: nativeint)
        (outputLength: nativeint)
        : int32 =
        match previous, output, outputLength with
        | Some image, target, length when target <> 0n && length <> 0n ->
            let bytes = Encoding.UTF8.GetBytes(PreludeImage.toJson image)
            writeResult current.Callbacks bytes target length
        | _, 0n, 0n -> StatusOk
        | _ -> StatusInvalid

    /// Imports or exports the current image, or replaces one named unit. Export
    /// serializes the retained image to the host allocator; import validates
    /// replacement bytes before replacing the retained image; unit replacement
    /// parses one unit document and revalidates the whole graph before swapping.
    /// Import and replacement can return the previous image for host rollback.
    let private transferImageCurrent
        (handle: nativeint)
        (current: KernelHandle)
        (direction: int32)
        (input: nativeint)
        (inputLength: unativeint)
        (output: nativeint)
        (outputLength: nativeint)
        : int32 =
        match direction with
            | ImageExport ->
                if output = 0n || outputLength = 0n then
                    StatusInvalid
                else
                    match current.Image with
                    | None -> StatusInvalid
                    | Some image ->
                        let bytes = Encoding.UTF8.GetBytes(PreludeImage.toJson image)
                        writeResult current.Callbacks bytes output outputLength
            | ImageImport ->
                if
                    not (outputPairValid output outputLength)
                    || input = 0n
                    || inputLength = 0un
                    || inputLength > unativeint PreludeImage.MaxBytes
                then
                    StatusInvalid
                else
                    let bytes = Array.zeroCreate<byte> (int inputLength)
                    Marshal.Copy(input, bytes, 0, bytes.Length)

                    match PreludeImage.ofBytes bytes with
                    | Error _ -> StatusInvalid
                    | Ok image ->
                        let previous = current.Image

                        if replaceHandle handle (fun current -> { current with Image = Some image }) then
                            writePreviousImage current previous output outputLength
                        else
                            StatusInvalid
            | ImageReplaceUnit ->
                if
                    not (outputPairValid output outputLength)
                    || input = 0n
                    || inputLength = 0un
                    || inputLength > unativeint PreludeImage.MaxBytes
                then
                    StatusInvalid
                else
                    let bytes = Array.zeroCreate<byte> (int inputLength)
                    Marshal.Copy(input, bytes, 0, bytes.Length)

                    match current.Image, PreludeImage.ofUnitBytes bytes with
                    | Some image, Ok unit ->
                        match PreludeImage.replaceUnit image unit with
                        | Error _ -> StatusInvalid
                        | Ok replacement ->
                            let previous = current.Image

                            if replaceHandle handle (fun current -> { current with Image = Some replacement }) then
                                writePreviousImage current previous output outputLength
                            else
                                StatusInvalid
                    | _ -> StatusInvalid
            | ImagePatchUnit ->
                if
                    not (outputPairValid output outputLength)
                    || input = 0n
                    || inputLength = 0un
                    || inputLength > unativeint PreludeImage.MaxBytes
                then
                    StatusInvalid
                else
                    let bytes = Array.zeroCreate<byte> (int inputLength)
                    Marshal.Copy(input, bytes, 0, bytes.Length)

                    match current.Image, PreludeImage.ofUnitPatchBytes bytes with
                    | Some image, Ok patch ->
                        match PreludeImage.applyUnitPatch image patch with
                        | Error _ -> StatusInvalid
                        | Ok replacement ->
                            let previous = current.Image

                            if replaceHandle handle (fun current -> { current with Image = Some replacement }) then
                                writePreviousImage current previous output outputLength
                            else
                                StatusInvalid
                    | _ -> StatusInvalid
            | _ -> StatusInvalid

    /// Imports, exports, replaces, or patches the image under the handle gate.
    let transferImage
        (handle: nativeint)
        (direction: int32)
        (input: nativeint)
        (inputLength: unativeint)
        (output: nativeint)
        (outputLength: nativeint)
        : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current ->
            lock current.Gate (fun () ->
                transferImageCurrent handle current direction input inputLength output outputLength)

    /// The managed form of `scale_init`, shared with hosts that link the
    /// kernel directly. The exported entry point calls this; managed callers
    /// must use this function, never the `UnmanagedCallersOnly` export.
    let initialize
        (callbacks: nativeint)
        (prelude: nativeint)
        (preludeLength: unativeint)
        (handleOut: nativeint)
        : int32 =
        if callbacks = 0n || handleOut = 0n then
            clearHandle handleOut
            StatusInvalid
        else
            clearHandle handleOut

            match loadImage prelude preludeLength with
            | Error _ -> StatusInvalid
            | Ok image ->
                let handle = createHandle callbacks image

                match image with
                | None ->
                    Marshal.WriteIntPtr(handleOut, handle)
                    StatusOk
                | Some _ ->
                    let loadStatus = loadPreludeThroughHost callbacks prelude preludeLength

                    if loadStatus <> StatusOk then
                        removeHandle handle
                        loadStatus
                    else
                        Marshal.WriteIntPtr(handleOut, handle)
                        StatusOk

    [<UnmanagedCallersOnly(EntryPoint = "scale_init", CallConvs = [| typeof<CallConvCdecl> |])>]
    let init
        (callbacks: nativeint)
        (prelude: nativeint)
        (preludeLength: unativeint)
        (handleOut: nativeint)
        : int32 =
        initialize callbacks prelude preludeLength handleOut

    /// Replaces the callback table for a live handle. The handle itself keeps
    /// its original opaque value; the new table must outlive the handle.
    let replaceCallbacks (handle: nativeint) (callbacks: nativeint) : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current ->
            lock current.Gate (fun () ->
                if
                    callbacks = 0n
                    || not (replaceHandle handle (fun current -> { current with Callbacks = callbacks }))
                then
                    StatusInvalid
                else
                    StatusOk)

    [<UnmanagedCallersOnly(EntryPoint = "scale_set_callbacks", CallConvs = [| typeof<CallConvCdecl> |])>]
    let setCallbacks (handle: nativeint) (callbacks: nativeint) : int32 =
        replaceCallbacks handle callbacks

    [<UnmanagedCallersOnly(EntryPoint = "scale_run", CallConvs = [| typeof<CallConvCdecl> |])>]
    let run
        (handle: nativeint)
        (program: nativeint)
        (programLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        execute handle program programLength resultOut resultLengthOut

    /// The managed form of `scale_verify`: decode a term-program document,
    /// verify it, and write its canonical receipt. A malformed document writes
    /// a diagnostic and returns SCALE_INVALID.
    let verifyTermProgram
        (handle: nativeint)
        (program: nativeint)
        (programLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current when resultOut = 0n || resultLengthOut = 0n -> StatusInvalid
        | Some current ->
            if program = 0n || programLength = 0un || programLength > unativeint TermVerification.MaxBytes then
                StatusInvalid
            else
                clearResult resultOut resultLengthOut
                let programBytes = Array.zeroCreate<byte> (int programLength)
                Marshal.Copy(program, programBytes, 0, programBytes.Length)

                match TermVerification.verifyBytes programBytes with
                | Ok verified ->
                    writeResult current.Callbacks (TermVerification.receiptBytes verified) resultOut resultLengthOut
                | Error error ->
                    let errorBytes = Encoding.UTF8.GetBytes(TermVerification.describeError error)

                    match writeResult current.Callbacks errorBytes resultOut resultLengthOut with
                    | StatusOk -> StatusInvalid
                    | status -> status

    [<UnmanagedCallersOnly(EntryPoint = "scale_verify", CallConvs = [| typeof<CallConvCdecl> |])>]
    let verify
        (handle: nativeint)
        (program: nativeint)
        (programLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        verifyTermProgram handle program programLength resultOut resultLengthOut

    /// The managed form of `scale_transport_send`.
    let forwardTransport
        (handle: nativeint)
        (target: nativeint)
        (targetLength: unativeint)
        (payload: nativeint)
        (payloadLength: unativeint)
        : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current when target = 0n && targetLength > 0un || payload = 0n && payloadLength > 0un ->
            StatusInvalid
        | Some current ->
            let readBytes (pointer: nativeint) (length: unativeint) =
                if pointer = 0n || length = 0un then
                    [||]
                else
                    let bytes = Array.zeroCreate<byte> (int length)
                    Marshal.Copy(pointer, bytes, 0, bytes.Length)
                    bytes

            let targetBytes = readBytes target targetLength
            let payloadBytes = readBytes payload payloadLength
            let stream = new System.IO.MemoryStream()

            (use writer = new System.Text.Json.Utf8JsonWriter(stream)
             writer.WriteStartObject()
             writer.WriteString("target", Encoding.UTF8.GetString targetBytes)
             writer.WriteString("payload", Convert.ToBase64String payloadBytes)
             writer.WriteEndObject()
             writer.Flush())

            let arguments = stream.ToArray()
            let argumentsPointer = Marshal.AllocHGlobal arguments.Length

            try
                Marshal.Copy(arguments, 0, argumentsPointer, arguments.Length)

                callHost
                    current.Callbacks
                    "scale"
                    "transport"
                    argumentsPointer
                    (unativeint arguments.Length)
                    0n
                    0n
            finally
                Marshal.FreeHGlobal argumentsPointer

    [<UnmanagedCallersOnly(EntryPoint = "scale_transport_send", CallConvs = [| typeof<CallConvCdecl> |])>]
    let transportSend
        (handle: nativeint)
        (target: nativeint)
        (targetLength: unativeint)
        (payload: nativeint)
        (payloadLength: unativeint)
        : int32 =
        forwardTransport handle target targetLength payload payloadLength

    /// Acquires a transport resource and returns the host's opaque token result.
    /// This is the token-returning transport path; `scale_transport_send` remains
    /// the ambient send path with no result.
    let forwardTransportAcquire
        (handle: nativeint)
        (target: nativeint)
        (targetLength: unativeint)
        (payload: nativeint)
        (payloadLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current when target = 0n && targetLength > 0un || payload = 0n && payloadLength > 0un ->
            StatusInvalid
        | Some current ->
            let readBytes (pointer: nativeint) (length: unativeint) =
                if pointer = 0n || length = 0un then
                    [||]
                else
                    let bytes = Array.zeroCreate<byte> (int length)
                    Marshal.Copy(pointer, bytes, 0, bytes.Length)
                    bytes

            let targetBytes = readBytes target targetLength
            let payloadBytes = readBytes payload payloadLength
            let stream = new System.IO.MemoryStream()

            (use writer = new System.Text.Json.Utf8JsonWriter(stream)
             writer.WriteStartObject()
             writer.WriteString("target", Encoding.UTF8.GetString targetBytes)
             writer.WriteString("payload", Convert.ToBase64String payloadBytes)
             writer.WriteEndObject()
             writer.Flush())

            let arguments = stream.ToArray()
            let argumentsPointer = Marshal.AllocHGlobal arguments.Length

            try
                Marshal.Copy(arguments, 0, argumentsPointer, arguments.Length)

                callHost
                    current.Callbacks
                    "scale"
                    "transport-acquire"
                    argumentsPointer
                    (unativeint arguments.Length)
                    resultOut
                    resultLengthOut
            finally
                Marshal.FreeHGlobal argumentsPointer

    [<UnmanagedCallersOnly(EntryPoint = "scale_transport_acquire", CallConvs = [| typeof<CallConvCdecl> |])>]
    let transportAcquire
        (handle: nativeint)
        (target: nativeint)
        (targetLength: unativeint)
        (payload: nativeint)
        (payloadLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        forwardTransportAcquire
            handle
            target
            targetLength
            payload
            payloadLength
            resultOut
            resultLengthOut

    let private readHostText (pointer: nativeint) (length: unativeint) : Result<string, int32> =
        if pointer = 0n && length = 0un then
            Ok ""
        elif pointer = 0n || length = 0un then
            Error StatusInvalid
        else
            let bytes = Array.zeroCreate<byte> (int length)
            Marshal.Copy(pointer, bytes, 0, bytes.Length)
            Ok(Encoding.UTF8.GetString bytes)

    /// The managed form of `scale_host_call`: validate the handle and names,
    /// then call the host through `host_dispatch`.
    let callHostFunction
        (handle: nativeint)
        (package: nativeint)
        (packageLength: unativeint)
        (functionName: nativeint)
        (functionLength: unativeint)
        (arguments: nativeint)
        (argumentLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        match findHandle handle with
        | None -> StatusInvalid
        | Some current ->
            match readHostText package packageLength, readHostText functionName functionLength with
            | Ok packageName, Ok functionText ->
                callHost current.Callbacks packageName functionText arguments argumentLength resultOut resultLengthOut
            | Error status, _
            | _, Error status -> status

    [<UnmanagedCallersOnly(EntryPoint = "scale_host_call", CallConvs = [| typeof<CallConvCdecl> |])>]
    let hostCall
        (handle: nativeint)
        (package: nativeint)
        (packageLength: unativeint)
        (functionName: nativeint)
        (functionLength: unativeint)
        (arguments: nativeint)
        (argumentLength: unativeint)
        (resultOut: nativeint)
        (resultLengthOut: nativeint)
        : int32 =
        callHostFunction
            handle
            package
            packageLength
            functionName
            functionLength
            arguments
            argumentLength
            resultOut
            resultLengthOut

    [<UnmanagedCallersOnly(EntryPoint = "scale_image", CallConvs = [| typeof<CallConvCdecl> |])>]
    let image
        (handle: nativeint)
        (direction: int32)
        (input: nativeint)
        (inputLength: unativeint)
        (output: nativeint)
        (outputLength: nativeint)
        : int32 =
        transferImage handle direction input inputLength output outputLength

    /// The managed form of `scale_free`, shared with hosts that link the
    /// kernel directly. The exported entry point calls this.
    let release (handle: nativeint) : unit =
        match findHandle handle with
        | None -> ()
        | Some current -> lock current.Gate (fun () -> removeHandle handle)

    [<UnmanagedCallersOnly(EntryPoint = "scale_free", CallConvs = [| typeof<CallConvCdecl> |])>]
    let free (handle: nativeint) : unit = release handle

// ============================================================
// Potential extensions (not commitments)
// - Chunked image transfer: large Prelude images over bounded ABI buffers.
// - Feature negotiation: per-export capability flags for older hosts.
// - Multiple isolated handles: separate hot-swap domains per actor group.
// - Asynchronous host callbacks: long-running host calls without blocking.
// ============================================================
