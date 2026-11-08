module Wire

// Protocol-specific binary framing. Shared JSON validation lives in WireCore;
// actor framing and operation semantics stay here.

open System
open System.Buffers.Binary
open System.IO
open System.Text

/// The first wire subset: one segment, one root struct, a text pointer, and a
/// numeric body. Every failure is named; the decoder never repairs a pointer.
type WireError =
    | MessageTooLarge of bytes: int
    | TruncatedFrame
    | SegmentCountUnsupported of count: int
    | SegmentSizeMismatch of declared: int * actual: int
    | InvalidRootPointer
    | StructOutOfBounds
    | DataSectionTooSmall of words: int
    | PointerSectionTooSmall of words: int
    | InvalidOperation of operation: uint64
    | InvalidBodyKind of kind: uint64
    | UnexpectedTextPointer
    | MissingTextPointer
    | InvalidTextPointer
    | TextOutOfBounds
    | TextMissingTerminator
    | InvalidUtf8

let describeError (error: WireError) : string =
    match error with
    | MessageTooLarge bytes -> $"wire: message is {bytes} bytes, over the limit"
    | TruncatedFrame -> "wire: truncated frame"
    | SegmentCountUnsupported count -> $"wire: {count} segments are not supported yet"
    | SegmentSizeMismatch(declared, actual) -> $"wire: segment declares {declared} bytes but the frame has {actual}"
    | InvalidRootPointer -> "wire: the root pointer is not a struct pointer"
    | StructOutOfBounds -> "wire: the struct is outside the segment"
    | DataSectionTooSmall words -> $"wire: the data section has {words} words, expected at least 7"
    | PointerSectionTooSmall words -> $"wire: the pointer section has {words} words, expected at least 1"
    | InvalidOperation operation -> $"wire: operation {operation} is not defined"
    | InvalidBodyKind kind -> $"wire: body kind {kind} is not defined"
    | UnexpectedTextPointer -> "wire: a numeric body carries a text pointer"
    | MissingTextPointer -> "wire: a text body has no text pointer"
    | InvalidTextPointer -> "wire: the text pointer is not a byte list"
    | TextOutOfBounds -> "wire: the text is outside the segment"
    | TextMissingTerminator -> "wire: the text has no NUL terminator"
    | InvalidUtf8 -> "wire: the text is not valid UTF-8"

/// The operation an envelope carries. A send is answered by a reply; a monitor
/// is acknowledged and later followed by a down message.
type Operation =
    | Send
    | Reply
    | Monitor
    | MonitorAck
    | Down

let operationCode (operation: Operation) : uint64 =
    match operation with
    | Send -> 0UL
    | Reply -> 1UL
    | Monitor -> 2UL
    | MonitorAck -> 3UL
    | Down -> 4UL

let operationOfCode (code: uint64) : Result<Operation, WireError> =
    match code with
    | 0UL -> Ok Send
    | 1UL -> Ok Reply
    | 2UL -> Ok Monitor
    | 3UL -> Ok MonitorAck
    | 4UL -> Ok Down
    | other -> Error(InvalidOperation other)

/// A numeric body or a text body. The actor host accepts numeric sends and
/// sends text only for a down reason.
type Body =
    | Number of double
    | Text of string

/// The actor envelope. Capability indices are resolved by the host; the wire
/// carries indices, never host objects.
type Envelope =
    {
        MessageId: uint64
        Target: uint64
        Sender: uint64
        Reply: uint64
        Operation: Operation
        Body: Body
    }

let maxMessageBytes = 1 <<< 20

let private readWord (bytes: byte[]) (offset: int) : uint64 =
    BinaryPrimitives.ReadUInt64LittleEndian(ReadOnlySpan<byte>(bytes, offset, 8))

let private writeWord (bytes: byte[]) (offset: int) (value: uint64) : unit =
    BinaryPrimitives.WriteUInt64LittleEndian(Span<byte>(bytes, offset, 8), value)

let private signedOffset (pointer: uint64) : int =
    let raw = int32 ((pointer >>> 2) &&& 0x3FFFFFFFUL)

    if raw >= (1 <<< 29) then raw - (1 <<< 30) else raw

let private structPointer (offsetWords: int) (dataWords: int) (pointerWords: int) : uint64 =
    uint64 (offsetWords &&& 0x3FFFFFFF) <<< 2
    ||| (uint64 dataWords <<< 32)
    ||| (uint64 pointerWords <<< 48)

let private listPointer (offsetWords: int) (elementSize: int) (count: int) : uint64 =
    1UL
    ||| (uint64 (offsetWords &&& 0x3FFFFFFF) <<< 2)
    ||| (uint64 elementSize <<< 32)
    ||| (uint64 count <<< 35)

/// Encodes one Cap'n Proto message: a segment table, a root struct, seven data
/// words, one pointer word, and an optional text.
let encode (envelope: Envelope) : Result<byte[], WireError> =
    let bodyKind, bodyNumber, text =
        match envelope.Body with
        | Number value -> 0UL, BitConverter.DoubleToUInt64Bits value, None
        | Text value -> 1UL, 0UL, Some value

    let textBytes = text |> Option.map Encoding.UTF8.GetBytes
    let textLength = textBytes |> Option.map (fun bytes -> bytes.Length + 1) |> Option.defaultValue 0
    let textWords = (textLength + 7) / 8
    let totalWords = 9 + textWords
    let totalBytes = 8 + totalWords * 8

    if totalBytes > maxMessageBytes then
        Error(MessageTooLarge totalBytes)
    else
        let bytes = Array.zeroCreate totalBytes
        BinaryPrimitives.WriteUInt32LittleEndian(Span<byte>(bytes, 0, 4), 0u)
        BinaryPrimitives.WriteUInt32LittleEndian(Span<byte>(bytes, 4, 4), uint32 totalWords)
        writeWord bytes 8 (structPointer 0 7 1)
        writeWord bytes 16 envelope.MessageId
        writeWord bytes 24 envelope.Target
        writeWord bytes 32 envelope.Sender
        writeWord bytes 40 envelope.Reply
        writeWord bytes 48 (operationCode envelope.Operation)
        writeWord bytes 56 bodyKind
        writeWord bytes 64 bodyNumber

        match textBytes with
        | Some text ->
            writeWord bytes 72 (listPointer 0 2 textLength)
            Array.blit text 0 bytes 80 text.Length
            bytes.[80 + text.Length] <- 0uy
        | None -> ()

        Ok bytes

/// Decodes one message. Only a single-segment struct with the documented fields
/// is accepted; every other shape is a named refusal.
let decode (bytes: byte[]) : Result<Envelope, WireError> =
    if bytes.Length < 8 then
        Error TruncatedFrame
    else
        let segmentCount = int (BinaryPrimitives.ReadUInt32LittleEndian(Span<byte>(bytes, 0, 4))) + 1

        if segmentCount <> 1 then
            Error(SegmentCountUnsupported segmentCount)
        else
            let words = int (BinaryPrimitives.ReadUInt32LittleEndian(Span<byte>(bytes, 4, 4)))
            let expected = 8 + words * 8

            if bytes.Length <> expected then
                Error(SegmentSizeMismatch(expected, bytes.Length))
            elif words < 9 then
                Error TruncatedFrame
            else
                let root = readWord bytes 8

                if (root &&& 3UL) <> 0UL then
                    Error InvalidRootPointer
                else
                    let offset = signedOffset root
                    let dataWords = int ((root >>> 32) &&& 0xFFFFUL)
                    let pointerWords = int ((root >>> 48) &&& 0xFFFFUL)
                    let structWord = 1 + offset

                    if dataWords < 7 then
                        Error(DataSectionTooSmall dataWords)
                    elif pointerWords < 1 then
                        Error(PointerSectionTooSmall pointerWords)
                    elif structWord < 0 || structWord + dataWords + pointerWords > words then
                        Error StructOutOfBounds
                    else
                        let structByte = 8 + structWord * 8
                        let messageId = readWord bytes structByte
                        let target = readWord bytes (structByte + 8)
                        let sender = readWord bytes (structByte + 16)
                        let reply = readWord bytes (structByte + 24)
                        let operationCode = readWord bytes (structByte + 32)
                        let bodyKind = readWord bytes (structByte + 40)
                        let bodyNumber = readWord bytes (structByte + 48)
                        let textPointer = readWord bytes (structByte + dataWords * 8)

                        match operationOfCode operationCode with
                        | Error error -> Error error
                        | Ok operation ->
                            let envelope body =
                                Ok
                                    {
                                        MessageId = messageId
                                        Target = target
                                        Sender = sender
                                        Reply = reply
                                        Operation = operation
                                        Body = body
                                    }

                            if bodyKind = 0UL then
                                if textPointer <> 0UL then
                                    Error UnexpectedTextPointer
                                else
                                    envelope (Number(BitConverter.UInt64BitsToDouble bodyNumber))
                            elif bodyKind = 1UL then
                                if textPointer = 0UL then
                                    Error MissingTextPointer
                                elif (textPointer &&& 3UL) <> 1UL then
                                    Error InvalidTextPointer
                                else
                                    let textOffset = signedOffset textPointer
                                    let elementSize = int ((textPointer >>> 32) &&& 0x7UL)
                                    let count = int ((textPointer >>> 35) &&& 0x1FFFFFFFUL)
                                    let textWord = structWord + dataWords + 1 + textOffset

                                    if elementSize <> 2 then
                                        Error InvalidTextPointer
                                    elif count <= 0 then
                                        Error TextMissingTerminator
                                    elif textWord < 0 || 8 + textWord * 8 + count > 8 + words * 8 then
                                        Error TextOutOfBounds
                                    else
                                        let textByte = 8 + textWord * 8

                                        if bytes.[textByte + count - 1] <> 0uy then
                                            Error TextMissingTerminator
                                        else
                                            try
                                                let value =
                                                    UTF8Encoding(false, true).GetString(bytes, textByte, count - 1)

                                                envelope (Text value)
                                            with :? DecoderFallbackException ->
                                                Error InvalidUtf8
                            else
                                Error(InvalidBodyKind bodyKind)

let private readExactly (stream: Stream) (buffer: byte[]) (offset: int) (count: int) : Result<unit, WireError> =
    let mutable position = offset

    while position < offset + count do
        let read = stream.Read(buffer, position, offset + count - position)

        if read <= 0 then
            position <- offset + count + 1
        else
            position <- position + read

    if position = offset + count then Ok() else Error TruncatedFrame

/// Reads the segment table, then the segment, then decodes the envelope.
let read (stream: Stream) : Result<Envelope, WireError> =
    let header = Array.zeroCreate 8

    match readExactly stream header 0 8 with
    | Error error -> Error error
    | Ok() ->
        let segmentCount = int (BinaryPrimitives.ReadUInt32LittleEndian(Span<byte>(header, 0, 4))) + 1

        if segmentCount <> 1 then
            Error(SegmentCountUnsupported segmentCount)
        else
            let words = int (BinaryPrimitives.ReadUInt32LittleEndian(Span<byte>(header, 4, 4)))
            let total = 8 + words * 8

            if total > maxMessageBytes then
                Error(MessageTooLarge total)
            else
                let message = Array.zeroCreate total
                Array.blit header 0 message 0 8

                match readExactly stream message 8 (total - 8) with
                | Error error -> Error error
                | Ok() -> decode message

/// Writes one segment-table-framed message and flushes the stream.
let write (stream: Stream) (envelope: Envelope) : Result<unit, WireError> =
    match encode envelope with
    | Error error -> Error error
    | Ok bytes ->
        stream.Write(bytes, 0, bytes.Length)
        stream.Flush()
        Ok()
