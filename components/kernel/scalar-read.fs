module ScalarRead

open Types

/// Projects a rank-0 (scalar) tensor to its value. A non-scalar is a named
/// refusal, not its first element (D60); an empty scalar is a caller bug and is
/// reported on the error track.
let readScalar (tensor: Tensor) : Result<RealNumber, string> =
    if Tensor.rank tensor <> 0 then
        Error "A non-scalar tensor cannot be read as a value"
    elif Tensor.size tensor = 0 then
        Error "Empty tensor cannot be read as value"
    else
        Ok(Tensor.item tensor 0)

/// Decodes a text tensor. Total: any 1-D tensor decodes to a string.
let readText (tensor: Tensor) : string =
    System.String(tensor.Data |> Array.map (fun value -> char (int value)))
