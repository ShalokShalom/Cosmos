module Lexicon

open Types

let empty: Lexicon = Map.empty

let add (identity: EntryIdentity) (tensor: Tensor) (lexicon: Lexicon) : Lexicon = Map.add identity tensor lexicon

let lookup (identity: EntryIdentity) (lexicon: Lexicon) : Tensor option = Map.tryFind identity lexicon

let contains (identity: EntryIdentity) (lexicon: Lexicon) : bool = Map.containsKey identity lexicon

let count (lexicon: Lexicon) : int = Map.count lexicon

let describe (lexicon: Lexicon) : string =
    let entries =
        lexicon
        |> Map.toList
        |> List.map (fun (identity, tensor) ->
            "  ["
            + string (IndexTransformation.toExternal (EntryIdentity.value identity))
            + "] "
            + Tensor.describe tensor)

    "Lexicon(" + string (count lexicon) + " entries)\n" + String.concat "\n" entries
