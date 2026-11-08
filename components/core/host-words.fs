module HostWords

/// The host-word token convention (D183, D184). A host word lowers to a token
/// that carries its argument count, so the adapter pops exactly the word's
/// arguments and nothing already on the stack. `$` is not a prose name
/// character, so a program cannot spell these tokens by accident.
let private shellPrefix = "$shell."
let private bookPrefix = "$book."

let isShellWord (name: string) : bool =
    name = "run" || name = "capture" || name = "feed" || name = "save"
let isBookWord (name: string) : bool =
    name = "cast" || name = "field"

let encode (name: string) (arity: int) : string = shellPrefix + name + "#" + string arity

let encodeBook (name: string) (arity: int) : string = bookPrefix + name + "#" + string arity

let private tryDecodeWith (prefix: string) (token: string) : (string * int) option =
    if not (token.StartsWith prefix) then
        None
    else
        match token.IndexOf('#', prefix.Length) with
        | -1 -> None
        | index ->
            let name = token.Substring(prefix.Length, index - prefix.Length)

            match System.Int32.TryParse(token.Substring(index + 1)) with
            | true, arity when arity >= 0 -> Some(name, arity)
            | _ -> None

let tryDecode (token: string) : (string * int) option = tryDecodeWith shellPrefix token
let tryDecodeBook (token: string) : (string * int) option = tryDecodeWith bookPrefix token

let isHostToken (token: string) : bool =
    tryDecode token |> Option.isSome || tryDecodeBook token |> Option.isSome
