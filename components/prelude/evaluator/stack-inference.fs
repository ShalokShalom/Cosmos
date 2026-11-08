module StackInference

open System
open System.Globalization
open TypeRetrieval
open PreludeSignatures

type StackError =
    | UnknownWord of name: string
    | NotInferredWord of name: string * reason: string
    | StackUnderflow of name: string * needed: int * available: int
    | StackTypeMismatch of name: string * expected: Type * actual: Type

type StackResult =
    {
        Stack: Type list
        Effects: EffectSet
        NextVariable: int
    }

let describeError (error: StackError) : string =
    match error with
    | UnknownWord name -> "unknown word: " + name
    | NotInferredWord(name, reason) -> name + ": " + reason
    | StackUnderflow(name, needed, available) ->
        name + " needs " + string needed + " operands, " + string available + " available"
    | StackTypeMismatch(name, expected, actual) ->
        name + " expects " + describeType expected + ", found " + describeType actual

let private takeTop (count: int) (stack: Type list) : (Type list * Type list) option =
    let topFirst = List.rev stack

    if List.length topFirst < count then
        None
    else
        Some(List.take count topFirst, List.skip count topFirst |> List.rev)

let private instantiate
    (signature: StackSignature)
    (nextVariable: int)
    : Type list * Type list * EffectSet * Substitution * int =
    let highest =
        signature.Quantified |> List.fold max -1

    let nextVariable = max nextVariable (highest + 1)

    let substitution, nextVariable =
        signature.Quantified
        |> List.fold
            (fun (current, next) variable ->
                Map.add variable (Variable next) current, next + 1)
            (Map.empty, nextVariable)

    let apply value = applyType substitution value
    signature.Pops |> List.map apply,
    signature.Pushes |> List.map apply,
    signature.Effects,
    substitution,
    nextVariable

let private stackSignatureOfWord
    (signature: WordSignature)
    : StackSignature =
    {
        Quantified = signature.Quantified
        Pops = List.rev signature.Parameters
        Pushes =
            if signature.Result = Primitive "unit" then
                []
            else
                [ signature.Result ]
        Effects = signature.Effects
    }

let private applyWord
    (name: string)
    (signature: StackSignature)
    (stack: Type list)
    (effects: EffectSet)
    (nextVariable: int)
    : Result<Type list * EffectSet * int, StackError> =
    let pops, pushes, wordEffects, substitution, nextVariable =
        instantiate signature nextVariable

    let rec pop (remaining: Type list) (currentStack: Type list) (current: Substitution) =
        match remaining with
        | [] -> Ok(currentStack, current)
        | expected :: rest ->
            match takeTop 1 currentStack with
            | None ->
                Error(
                    StackUnderflow(
                        name,
                        List.length signature.Pops,
                        List.length currentStack
                    )
                )
            | Some([ actual ], restStack) ->
                let expected = applyType current expected
                let actual = applyType current actual

                match unify expected actual current with
                | Error _ -> Error(StackTypeMismatch(name, expected, actual))
                | Ok next -> pop rest restStack next

    pop pops stack substitution
    |> Result.map (fun (remaining, substitution) ->
        let pushed = pushes |> List.map (applyType substitution)
        let combinedEffects, _ = unionEffects emptyInferenceState effects wordEffects
        remaining @ pushed, combinedEffects, nextVariable)

let check (program: string list) : Result<StackResult, StackError> =
    let rec loop
        (remaining: string list)
        (stack: Type list)
        (effects: EffectSet)
        (nextVariable: int)
        =
        match remaining with
        | [] ->
            Ok
                {
                    Stack = stack
                    Effects = effects
                    NextVariable = nextVariable
                }
        | token :: rest ->
            match Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, _ -> loop rest (stack @ [ Primitive "number" ]) effects nextVariable
            | false, _ ->
                match PreludeSignatures.classification |> List.tryFind (fun (name, _) -> name = token) with
                | None -> Error(UnknownWord token)
                | Some(_, NotInferred reason) -> Error(NotInferredWord(token, reason))
                | Some(_, Stack signature) ->
                    applyWord token signature stack effects nextVariable
                    |> Result.bind (fun (nextStack, nextEffects, next) -> loop rest nextStack nextEffects next)
                | Some(_, Expression signatures) ->
                    match signatures with
                    | [] -> Error(UnknownWord token)
                    | signature :: _ ->
                        applyWord token (stackSignatureOfWord signature) stack effects nextVariable
                        |> Result.bind (fun (nextStack, nextEffects, next) -> loop rest nextStack nextEffects next)

    loop program [] emptyEffects 0
