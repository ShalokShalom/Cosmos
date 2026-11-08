module HostTokenWire

// Protocol fields stay here; shared JSON validation is WireCore.

open System.IO
open System.Text.Json
open Types

let encode (token: HostToken) : byte[] =
    use stream = new MemoryStream()

    (use writer = new Utf8JsonWriter(stream)
     writer.WriteStartObject()
     writer.WriteNumber("token", HostToken.value token)
     writer.WriteEndObject()
     writer.Flush())

    stream.ToArray()

let decode (bytes: byte[]) : Result<HostToken, string> =
    try
        use document = JsonDocument.Parse(bytes)
        let root = document.RootElement

        match WireCore.requiredUInt64 root "token" with
        | Ok value -> Ok(HostToken.create value)
        | Error _ -> Error "a token result must carry an unsigned integer token"
    with _ ->
        Error "a token result must be a JSON object"
