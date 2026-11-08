module Tensor

open Types

let create (shape: int[]) (data: RealNumber[]) : Result<Tensor, string> =
    let expectedSize = Array.fold (*) 1 shape

    if data.Length <> expectedSize then
        Error(
            "Shape "
            + string shape
            + " expects "
            + string expectedSize
            + " elements, got "
            + string data.Length
        )
    else
        Ok { Data = Array.copy data; Shape = Array.copy shape }

let scalar (value: RealNumber) : Tensor = { Data = [| value |]; Shape = [||] }

let vector (values: RealNumber list) : Tensor =
    {
        Data = Array.ofList values
        Shape = [| values.Length |]
    }

let empty: Tensor = { Data = [||]; Shape = [| 0 |] }

let rank (tensor: Tensor) : int = tensor.Shape.Length

let size (tensor: Tensor) : int = tensor.Data.Length

let item (tensor: Tensor) (linearIndex: int) : RealNumber = tensor.Data.[linearIndex]

let reshape (newShape: int[]) (tensor: Tensor) : Result<Tensor, string> =
    let expectedSize = Array.fold (*) 1 newShape

    if tensor.Data.Length <> expectedSize then
        Error(
            "Cannot reshape "
            + string tensor.Shape
            + " ("
            + string tensor.Data.Length
            + " elements) to "
            + string newShape
        )
    else
        Ok { Data = Array.copy tensor.Data; Shape = Array.copy newShape }

/// The number of elements a shape describes. Computed in Int64 so a hostile
/// shape cannot overflow the product.
let private shapeSize (shape: int[]) : int64 =
    Array.fold (fun accumulated dimension -> accumulated * int64 dimension) 1L shape

/// A tensor whose shape and data agree. A record can be built by hand, so the
/// invariant is checked rather than assumed.
let private shapeAgreesWithData (tensor: Tensor) : bool =
    shapeSize tensor.Shape = int64 tensor.Data.Length

/// Appends along the leading axis. The result's shape always describes its
/// data: the appended tensor must carry whole rows that share the trailing
/// dimensions, and a tensor whose own shape and data disagree is refused
/// rather than appended to.
let appendAlongAxis (axis: int) (value: Tensor) (tensor: Tensor) : Result<Tensor, string> =
    if axis <> 0 then
        Error "appendAlongAxis currently only supports axis 0"
    elif not (shapeAgreesWithData tensor) then
        Error "appendAlongAxis cannot append to a tensor whose shape and data disagree"
    elif not (shapeAgreesWithData value) then
        Error "appendAlongAxis cannot append a tensor whose shape and data disagree"
    elif rank tensor <= 1 then
        if rank value > 1 then
            Error "appendAlongAxis appends a scalar or a vector to a scalar or a vector"
        else
            let newData = Array.append tensor.Data value.Data

            Ok
                {
                    Data = newData
                    Shape = [| newData.Length |]
                }
    elif tensor.Shape.[0] <= 0 then
        Error "appendAlongAxis cannot append to a tensor with an empty leading dimension"
    elif value.Shape.Length <> tensor.Shape.Length then
        Error "appendAlongAxis requires the appended tensor to have the same rank"
    elif not (Array.forall2 (=) value.Shape.[1..] tensor.Shape.[1..]) then
        Error "appendAlongAxis requires the appended tensor to share the trailing dimensions"
    else
        let rowSize = int64 tensor.Data.Length / int64 tensor.Shape.[0]

        if int64 value.Data.Length % rowSize <> 0L then
            Error "appendAlongAxis requires the appended tensor to hold whole rows"
        else
            let newShape = Array.copy tensor.Shape
            newShape.[0] <- tensor.Shape.[0] + int (int64 value.Data.Length / rowSize)
            let newData = Array.append tensor.Data value.Data

            Ok { Data = newData; Shape = newShape }

let append (value: RealNumber) (tensor: Tensor) : Result<Tensor, string> = appendAlongAxis 0 (scalar value) tensor

/// Concatenates two tensors' data into a rank-1 vector. Total: there is no
/// axis argument and no shape to disagree about, so this cannot fail. Used
/// where the caller holds a genuine continuation vector (environments, models).
let concat (left: Tensor) (right: Tensor) : Tensor =
    let data = Array.append left.Data right.Data

    {
        Data = data
        Shape = [| data.Length |]
    }

/// Appends a scalar onto a tensor, yielding a rank-1 continuation. Total for
/// the same reason as `concat`.
let appendElement (value: RealNumber) (tensor: Tensor) : Tensor = concat tensor (scalar value)

let describe (tensor: Tensor) : string =
    "Tensor(shape=["
    + String.concat ", " (Array.map string tensor.Shape)
    + "], data=["
    + String.concat ", " (Array.map string tensor.Data)
    + "])"
