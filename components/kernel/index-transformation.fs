module IndexTransformation

/// Converts an external (1-based, negative-from-end) index to an internal
/// 0-based index. Zero and out-of-range are errors on the error track, not
/// host exceptions.
let toInternal (length: int) (externalIndex: int) : Result<int, string> =
    if externalIndex = 0 then
        Error "Index 0 is invalid. Indices start at 1."
    elif externalIndex > 0 then
        if externalIndex > length then
            Error("Index " + string externalIndex + " exceeds length " + string length)
        else
            Ok(externalIndex - 1)
    else
        let internalIndex = length + externalIndex

        if internalIndex < 0 then
            Error("Index " + string externalIndex + " is too small")
        else
            Ok internalIndex

let toExternal (internalIndex: int) : int = internalIndex + 1
