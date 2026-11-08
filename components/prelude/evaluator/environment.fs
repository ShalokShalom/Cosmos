module EvaluatorEnvironment

// ============================================================
// Environments: bindings and unification (store-resident, D2, D15).
//
// Bindings are an store-resident association vector addressed by
// `BindingTable`; user words use the same shape under `UserWordTable` (see
// EvaluatorControl.executeDefine).
//
// A term is one of four store entries:
//   * a variable: an interned text entry whose text is `?name`;
//   * an atom: any other store entry;
//   * a compound: a vector [UnificationTermTag; head; argument; ...];
//   * a typed term: a vector [TypedTag; sortName; term].
//
// The tags are negative sentinels. They cannot be sort names: `ValueSet.decode`
// accepts only -1 as the anonymous marker, so a term is never mistaken for a
// sort. Terms need no separate root, and the substitution stays in
// `BindingTable`.
//
// Words: bind, lookup, unify, resolve, term, typed.
// ============================================================

open Types
open EvaluatorCommon

[<Literal>]
let UnificationTermTag = -2.0

[<Literal>]
let TypedTag = -3.0

[<Literal>]
let LambdaTag = -15.0

/// A term view. The view is transient: the store entry is the authority.
type UnificationTerm =
    | Variable of name: string * identity: EntryIdentity
    | Atom of identity: EntryIdentity
    | Compound of head: EntryIdentity * arguments: EntryIdentity list
    | Typed of sortName: EntryIdentity * term: EntryIdentity
    /// A single-parameter binder. `parameter` is usually an atom; `body` is a
    /// term in which that atom is bound. Multi-parameter abstraction is curried.
    | Lambda of parameter: EntryIdentity * body: EntryIdentity

/// The outcome of a structural unification. `NotUnified` is a predicate
/// failure, not a defect; the caller rolls the substitution back to its
/// pre-unification store. `Undecided` is a named refusal for a case outside the
/// pattern fragment: it is never reported as a false (D59).
type Unification =
    | Unified of root: EntryIdentity option
    | NotUnified
    | Undecided of reason: string

/// Reads the `?name` variable sigil out of an interned text value.
let variableNameOfText (text: string) : string option =
    if text.Length >= 2 && text.[0] = '?' then
        Some(text.Substring(1))
    else
        None

// ---- UnificationTerm encoding ---------------------------------------------------------

let encodeCompound (head: EntryIdentity) (arguments: EntryIdentity list) : Tensor =
    Tensor.vector (
        UnificationTermTag
        :: double (EntryIdentity.value head)
        :: (arguments |> List.map (fun identity -> double (EntryIdentity.value identity)))
    )

let encodeTyped (sortName: EntryIdentity) (term: EntryIdentity) : Tensor =
    Tensor.vector
        [
            TypedTag
            double (EntryIdentity.value sortName)
            double (EntryIdentity.value term)
        ]

let encodeLambda (parameter: EntryIdentity) (body: EntryIdentity) : Tensor =
    Tensor.vector
        [
            LambdaTag
            double (EntryIdentity.value parameter)
            double (EntryIdentity.value body)
        ]

let viewUnificationTerm (identity: EntryIdentity) (store: Store) : UnificationTerm =
    match Store.readTextAt identity store with
    | Some text ->
        match variableNameOfText text with
        | Some name -> Variable(name, identity)
        | None -> Atom identity
    | None ->
        match Store.lookupTensor identity store with
        | Some tensor when
            Tensor.rank tensor = 1
            && Tensor.size tensor >= 2
            && Tensor.item tensor 0 = UnificationTermTag
            ->
            let head = EntryIdentity.ofInt (int (Tensor.item tensor 1))

            let arguments =
                tensor.Data
                |> Array.skip 2
                |> Array.toList
                |> List.map (fun value -> EntryIdentity.ofInt (int value))

            Compound(head, arguments)
        | Some tensor when
            Tensor.rank tensor = 1
            && Tensor.size tensor = 3
            && Tensor.item tensor 0 = LambdaTag
            ->
            Lambda(EntryIdentity.ofInt (int (Tensor.item tensor 1)), EntryIdentity.ofInt (int (Tensor.item tensor 2)))
        | Some tensor when
            Tensor.rank tensor = 1
            && Tensor.size tensor = 3
            && Tensor.item tensor 0 = TypedTag
            ->
            Typed(EntryIdentity.ofInt (int (Tensor.item tensor 1)), EntryIdentity.ofInt (int (Tensor.item tensor 2)))
        | _ -> Atom identity

// ---- Resolution ------------------------------------------------------------

/// Follows a variable's substitution until it reaches a non-variable or an
/// unbound variable. `visited` makes a legacy cyclic binding terminate.
let rec walk
    (identity: EntryIdentity)
    (visited: Set<EntryIdentity>)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity * Store =
    if visited.Contains identity then
        (identity, store)
    else
        match viewUnificationTerm identity store with
        | Variable(name, _) ->
            let (nameId, store1) = Store.addText name store

            match findPair nameId root store1 with
            | Some bound -> walk bound (visited.Add identity) root store1
            | None -> (identity, store1)
        | _ -> (identity, store)

/// Zonks a term: resolves variables everywhere, rebuilding compounds only when
/// a child changed. The rebuilt entries live in the store like any other term.
let rec resolveUnificationTerm
    (identity: EntryIdentity)
    (visited: Set<EntryIdentity>)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity * Store =
    if visited.Contains identity then
        (identity, store)
    else
        let (walked, store1) = walk identity visited root store

        if walked <> identity then
            resolveUnificationTerm walked (visited.Add identity) root store1
        else
            match viewUnificationTerm identity store1 with
            | Variable _
            | Atom _ -> (identity, store1)
            | Compound(head, arguments) ->
                let visited1 = visited.Add identity
                let (head1, store2) = resolveUnificationTerm head visited1 root store1

                let rec resolveArguments arguments store =
                    match arguments with
                    | [] -> ([], store)
                    | argument :: rest ->
                        let (argument1, store3) = resolveUnificationTerm argument visited1 root store
                        let (rest1, store4) = resolveArguments rest store3
                        (argument1 :: rest1, store4)

                let (arguments1, store3) = resolveArguments arguments store2

                if head1 = head && arguments1 = arguments then
                    (identity, store3)
                else
                    Store.addTensor (encodeCompound head1 arguments1) store3
            | Typed(sortName, inner) ->
                let (inner1, store2) =
                    resolveUnificationTerm inner (visited.Add identity) root store1

                if inner1 = inner then
                    (identity, store2)
                else
                    Store.addTensor (encodeTyped sortName inner1) store2
            | Lambda(parameter, body) ->
                let (body1, store2) = resolveUnificationTerm body (visited.Add identity) root store1

                if body1 = body then
                    (identity, store2)
                else
                    Store.addTensor (encodeLambda parameter body1) store2

// ---- Unification -----------------------------------------------------------

/// Occurs-check: does `variable` occur anywhere in `term`, after resolution?
let rec occursIn
    (variable: EntryIdentity)
    (term: EntryIdentity)
    (visited: Set<EntryIdentity>)
    (root: EntryIdentity option)
    (store: Store)
    : bool * Store =
    if visited.Contains term then
        (false, store)
    else
        let (walked, store1) = walk term visited root store

        if walked = variable then
            (true, store1)
        else
            let visited1 = visited.Add term

            match viewUnificationTerm walked store1 with
            | Variable _
            | Atom _ -> (false, store1)
            | Compound(head, arguments) ->
                let (headOccurs, store2) = occursIn variable head visited1 root store1

                if headOccurs then
                    (true, store2)
                else
                    let rec checkArguments arguments store =
                        match arguments with
                        | [] -> (false, store)
                        | argument :: rest ->
                            let (found, store3) = occursIn variable argument visited1 root store

                            if found then (true, store3) else checkArguments rest store3

                    checkArguments arguments store2
            | Typed(_, inner) -> occursIn variable inner visited1 root store1
            | Lambda(_, body) -> occursIn variable body visited1 root store1

/// Binds an unbound variable to a term. The variable identity is canonicalised
/// through `walk` first, so aliases all check the same representative.
let bindVariable
    (variable: EntryIdentity)
    (term: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : Unification * Store =
    let (canonical, store1) = walk variable Set.empty root store

    match viewUnificationTerm canonical store1 with
    | Variable(name, _) ->
        let (occurs, store2) = occursIn canonical term Set.empty root store1

        if occurs then
            (NotUnified, store2)
        else
            let (nameId, store3) = Store.addText name store2
            let (newRoot, store4) = addToEnvironment nameId term root store3
            (Unified(Some newRoot), store4)
    | _ -> (NotUnified, store1)

/// Does `atom` occur anywhere in `term`, following bindings? Used as a
/// conservative capture check before substitution.
let rec containsAtom
    (atom: EntryIdentity)
    (term: EntryIdentity)
    (visited: Set<EntryIdentity>)
    (root: EntryIdentity option)
    (store: Store)
    : bool * Store =
    if visited.Contains term then
        (false, store)
    else
        let (walked, store1) = walk term visited root store

        if walked = atom then
            (true, store1)
        else
            let visited1 = visited.Add term

            match viewUnificationTerm walked store1 with
            | Variable _
            | Atom _ -> (false, store1)
            | Compound(head, arguments) ->
                let (headFound, store2) = containsAtom atom head visited1 root store1

                if headFound then
                    (true, store2)
                else
                    let rec check arguments store =
                        match arguments with
                        | [] -> (false, store)
                        | argument :: rest ->
                            let (found, store3) = containsAtom atom argument visited1 root store

                            if found then (true, store3) else check rest store3

                    check arguments store2
            | Typed(_, inner)
            | Lambda(_, inner) -> containsAtom atom inner visited1 root store1

/// Capture-avoiding substitution for beta-reduction. A binder that shadows the
/// parameter stops the walk; a binder that would capture the replacement is a
/// named refusal, never an unsound rename.
let rec substitute
    (parameter: EntryIdentity)
    (replacement: EntryIdentity)
    (term: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : Result<EntryIdentity * Store, string> =
    let (walked, store1) = walk term Set.empty root store

    if walked = parameter then
        Ok(replacement, store1)
    else
        match viewUnificationTerm walked store1 with
        | Variable _
        | Atom _ -> Ok(walked, store1)
        | Compound(head, arguments) ->
            substitute parameter replacement head root store1
            |> Result.bind (fun (head1, store2) ->
                let rec go arguments store =
                    match arguments with
                    | [] -> Ok([], store)
                    | argument :: rest ->
                        substitute parameter replacement argument root store
                        |> Result.bind (fun (argument1, store3) ->
                            go rest store3 |> Result.map (fun (rest1, store4) -> argument1 :: rest1, store4))

                go arguments store2
                |> Result.bind (fun (arguments1, store3) ->
                    if head1 = head && arguments1 = arguments then
                        Ok(walked, store3)
                    else
                        let (identity, store4) = Store.addTensor (encodeCompound head1 arguments1) store3
                        Ok(identity, store4)))
        | Typed(sortName, inner) ->
            substitute parameter replacement inner root store1
            |> Result.bind (fun (inner1, store2) ->
                if inner1 = inner then
                    Ok(walked, store2)
                else
                    let (identity, store3) = Store.addTensor (encodeTyped sortName inner1) store2
                    Ok(identity, store3))
        | Lambda(parameter2, body) ->
            if parameter2 = parameter then
                Ok(walked, store1)
            else
                let (captures, store2) = containsAtom parameter2 replacement Set.empty root store1

                if captures then
                    Error("the substitution would capture the bound parameter")
                else
                    substitute parameter replacement body root store2
                    |> Result.bind (fun (body1, store3) ->
                        if body1 = body then
                            Ok(walked, store3)
                        else
                            let (identity, store4) = Store.addTensor (encodeLambda parameter2 body1) store3
                            Ok(identity, store4))

/// Beta-normalizes a term. A lambda applied to an argument substitutes and
/// retries; a capture is a named refusal.
let rec betaNormalize
    (term: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : Result<EntryIdentity * Store, string> =
    let (walked, store1) = walk term Set.empty root store

    match viewUnificationTerm walked store1 with
    | Compound(head, arguments) ->
        betaNormalize head root store1
        |> Result.bind (fun (head1, store2) ->
            let rec go arguments store =
                match arguments with
                | [] -> Ok([], store)
                | argument :: rest ->
                    betaNormalize argument root store
                    |> Result.bind (fun (argument1, store3) ->
                        go rest store3 |> Result.map (fun (rest1, store4) -> argument1 :: rest1, store4))

            go arguments store2
            |> Result.bind (fun (arguments1, store3) ->
                match viewUnificationTerm head1 store3, arguments1 with
                | Lambda(parameter, body), argument :: rest ->
                    substitute parameter argument body root store3
                    |> Result.bind (fun (body1, store4) ->
                        let (applied, store5) =
                            if List.isEmpty rest then
                                (body1, store4)
                            else
                                Store.addTensor (encodeCompound body1 rest) store4

                        betaNormalize applied root store5)
                | _ ->
                    if head1 = head && arguments1 = arguments then
                        Ok(walked, store3)
                    else
                        let (identity, store4) = Store.addTensor (encodeCompound head1 arguments1) store3
                        Ok(identity, store4)))
    | Lambda(parameter, body) ->
        betaNormalize body root store1
        |> Result.bind (fun (body1, store2) ->
            if body1 = body then
                Ok(walked, store2)
            else
                let (identity, store3) = Store.addTensor (encodeLambda parameter body1) store2
                Ok(identity, store3))
    | _ -> Ok(walked, store1)

let private tryFlex (viewed: UnificationTerm) (store: Store) : (EntryIdentity * EntryIdentity list) option =
    match viewed with
    | Compound(head, arguments) when not (List.isEmpty arguments) ->
        match viewUnificationTerm head store with
        | Variable(_, variable) -> Some(variable, arguments)
        | _ -> None
    | _ -> None

/// Binds a function variable by a Miller pattern: the spine must be distinct
/// atoms, and the variable must not occur in the body. The binding is the
/// curried abstraction of the body over the spine.
let private bindPattern
    (variable: EntryIdentity)
    (arguments: EntryIdentity list)
    (body: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : Unification * Store =
    let atomsOnly =
        arguments
        |> List.forall (fun argument ->
            match viewUnificationTerm argument store with
            | Atom _ -> true
            | _ -> false)

    let distinct = List.distinct arguments |> List.length = List.length arguments

    if not (atomsOnly && distinct) then
        (Undecided "the flex application is not a Miller pattern", store)
    else
        let rec abstractOver arguments body store =
            match arguments with
            | [] -> (body, store)
            | parameter :: rest ->
                let (inner, store1) = abstractOver rest body store
                Store.addTensor (encodeLambda parameter inner) store1

        let (lambda, store1) = abstractOver arguments body store
        bindVariable variable lambda root store1

/// Higher-order pattern unification with a first-order fallback. A flex term is
/// a variable applied to arguments; a Miller-pattern flex binds the variable to
/// an abstraction of the rigid side. Lambda terms compare by alpha-renaming a
/// parameter. Non-pattern flex spines and flex-flex pairs with different spines
/// are `Undecided`, never a silent false (D59).
let rec unifyTerms
    (left: EntryIdentity)
    (right: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : Unification * Store =
    match betaNormalize left root store with
    | Error reason -> (Undecided reason, store)
    | Ok(leftNormalized, store1) ->
        match betaNormalize right root store1 with
        | Error reason -> (Undecided reason, store1)
        | Ok(rightNormalized, store2) ->
            if leftNormalized = rightNormalized then
                (Unified root, store2)
            else
                let leftView = viewUnificationTerm leftNormalized store2
                let rightView = viewUnificationTerm rightNormalized store2

                match tryFlex leftView store2, tryFlex rightView store2 with
                | Some(leftVariable, leftArguments), Some(rightVariable, rightArguments) ->
                    if leftArguments = rightArguments then
                        if EntryIdentity.value leftVariable < EntryIdentity.value rightVariable then
                            bindVariable rightVariable leftVariable root store2
                        else
                            bindVariable leftVariable rightVariable root store2
                    else
                        (Undecided "flex-flex unification with different spines", store2)
                | Some(variable, arguments), None -> bindPattern variable arguments rightNormalized root store2
                | None, Some(variable, arguments) -> bindPattern variable arguments leftNormalized root store2
                | None, None ->
                    match leftView, rightView with
                    | Variable _, Variable _ ->
                        if EntryIdentity.value leftNormalized < EntryIdentity.value rightNormalized then
                            bindVariable rightNormalized leftNormalized root store2
                        else
                            bindVariable leftNormalized rightNormalized root store2
                    | Variable _, _ -> bindVariable leftNormalized rightNormalized root store2
                    | _, Variable _ -> bindVariable rightNormalized leftNormalized root store2
                    | Typed(firstName, firstTerm), Typed(secondName, secondTerm) ->
                        if firstName <> secondName then
                            (NotUnified, store2)
                        else
                            unifyTerms firstTerm secondTerm root store2
                    | Lambda(firstParameter, firstBody), Lambda(secondParameter, secondBody) ->
                        if firstParameter = secondParameter then
                            unifyTerms firstBody secondBody root store2
                        else
                            match substitute secondParameter firstParameter secondBody root store2 with
                            | Error reason -> (Undecided reason, store2)
                            | Ok(secondBody1, store3) -> unifyTerms firstBody secondBody1 root store3
                    | Lambda _, _
                    | _, Lambda _ -> (Undecided "a function value cannot be unified with a non-function", store2)
                    | Typed _, _
                    | _, Typed _ -> (NotUnified, store2)
                    | Compound(firstHead, firstArguments), Compound(secondHead, secondArguments) ->
                        if List.length firstArguments <> List.length secondArguments then
                            (NotUnified, store2)
                        else
                            unifyLists (firstHead :: firstArguments) (secondHead :: secondArguments) root store2
                    | Atom firstId, Atom secondId ->
                        match Store.lookupTensor firstId store2, Store.lookupTensor secondId store2 with
                        | Some firstTensor, Some secondTensor ->
                            ((if tensorsEqual firstTensor secondTensor then
                                  Unified root
                              else
                                  NotUnified),
                             store2)
                        | _ -> (NotUnified, store2)
                    | _ -> (NotUnified, store2)

and unifyLists
    (left: EntryIdentity list)
    (right: EntryIdentity list)
    (root: EntryIdentity option)
    (store: Store)
    : Unification * Store =
    match left, right with
    | [], [] -> (Unified root, store)
    | leftId :: leftRest, rightId :: rightRest ->
        match unifyTerms leftId rightId root store with
        | NotUnified, store1 -> (NotUnified, store1)
        | Undecided reason, store1 -> (Undecided reason, store1)
        | Unified root1, store1 -> unifyLists leftRest rightRest root1 store1
    | _ -> (NotUnified, store)

// ---- Words -----------------------------------------------------------------

let bindWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("bind", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Stack.popIndex afterName with
        | None -> Error(StackUnderflow("bind", 2, Stack.depth state.Stack))
        | Some(valueId, afterValue) ->
            let (newRoot, newStore) =
                addToEnvironment nameId valueId state.BindingTable state.Store

            Ok
                { state with
                    Store = newStore
                    BindingTable = Some newRoot
                    Stack = afterValue
                }

let lookupWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("lookup", 1, Stack.depth state.Stack))
    | Some(nameId, remainingStack) ->
        let binding =
            match EvaluatorCellSystem.lookupInFrame nameId state.CurrentFrame state.Store with
            | Some valueId -> Some valueId
            | None -> findPair nameId state.BindingTable state.Store

        match binding with
        | Some valueId ->
            let (resolved, store) =
                resolveUnificationTerm valueId Set.empty state.BindingTable state.Store

            Ok
                { state with
                    Store = store
                    Stack = Stack.push resolved remainingStack
                }
        | None -> Error(Unresolved(UnknownKind.Binding, (nameText nameId state.Store)))

let unifyWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("unify", 2, Stack.depth state.Stack))
    | Some(secondId, afterSecond) ->
        match Stack.popIndex afterSecond with
        | None -> Error(StackUnderflow("unify", 2, Stack.depth state.Stack))
        | Some(firstId, afterFirst) ->
            let pushBool (value: bool) (root: EntryIdentity option) (store: Store) =
                let (identity, newStore) =
                    Store.addTensor (Tensor.scalar (if value then 1.0 else 0.0)) store

                Ok
                    { state with
                        Store = newStore
                        Stack = Stack.push identity afterFirst
                        BindingTable = root
                    }

            match unifyTerms firstId secondId state.BindingTable state.Store with
            | Unified root, store -> pushBool true root store
            // A failed unification is a predicate failure, not a partial
            // binding: discard the speculative root and store.
            | NotUnified, _ -> pushBool false state.BindingTable state.Store
            | Undecided reason, _ -> Error(UnificationUndecided reason)

/// The three-way unification verdict as a tagged tensor:
/// `[UnificationVerdictTag; status; reasonId]`; status 0 unified, 1 not
/// unified, 2 undecided; reasonId is 0 unless status is 2.
let unifyVerdictWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("unify-verdict", 2, Stack.depth state.Stack))
    | Some(secondId, afterSecond) ->
        match Stack.popIndex afterSecond with
        | None -> Error(StackUnderflow("unify-verdict", 2, Stack.depth state.Stack))
        | Some(firstId, afterFirst) ->
            let (status, reasonId, root, store) =
                match unifyTerms firstId secondId state.BindingTable state.Store with
                | Unified root, store -> (0.0, 0.0, root, store)
                | NotUnified, _ -> (1.0, 0.0, state.BindingTable, state.Store)
                | Undecided reason, _ ->
                    let (reasonEntry, store) = Store.addText reason state.Store
                    (2.0, double (EntryIdentity.value reasonEntry), state.BindingTable, store)

            let (identity, store1) =
                Store.addTensor (Tensor.vector [ UnificationVerdictTag; status; reasonId ]) store

            Ok
                { state with
                    Store = store1
                    Stack = Stack.push identity afterFirst
                    BindingTable = root
                }

let resolveWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("resolve", 1, Stack.depth state.Stack))
    | Some(identity, remainingStack) ->
        let (resolved, store) =
            resolveUnificationTerm identity Set.empty state.BindingTable state.Store

        Ok
            { state with
                Store = store
                Stack = Stack.push resolved remainingStack
            }

let lambdaTermWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("lambda-term", 2, Stack.depth state.Stack))
    | Some(parameterId, afterParameter) ->
        match Stack.popIndex afterParameter with
        | None -> Error(StackUnderflow("lambda-term", 2, Stack.depth state.Stack))
        | Some(bodyId, remainingStack) ->
            let (parameter, store1) = walk parameterId Set.empty state.BindingTable state.Store
            let (body, store2) = walk bodyId Set.empty state.BindingTable store1
            let (identity, store3) = Store.addTensor (encodeLambda parameter body) store2

            Ok
                { state with
                    Store = store3
                    Stack = Stack.push identity remainingStack
                }

let reduceWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("reduce", 1, Stack.depth state.Stack))
    | Some(identity, remainingStack) ->
        match betaNormalize identity state.BindingTable state.Store with
        | Error reason -> Error(UnificationUndecided reason)
        | Ok(reduced, store) ->
            Ok
                { state with
                    Store = store
                    Stack = Stack.push reduced remainingStack
                }

let termWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("term", 2, Stack.depth state.Stack))
    | Some(arityId, afterArity) ->
        match Store.lookupTensor arityId state.Store with
        | None -> Error(UnknownIndex(EntryIdentity.value arityId))
        | Some arityTensor ->
            match scalarValue "term" arityTensor with
            | Error error -> Error error
            | Ok rawArity ->
                // The arity is bounded twice over: it must be an integer the
                // stack can possibly hold, and the comparison is done in Int64
                // so an extreme arity cannot overflow the check itself.
                let integral =
                    System.Double.IsFinite rawArity
                    && rawArity >= 0.0
                    && rawArity <= float System.Int32.MaxValue
                    && rawArity = System.Math.Floor rawArity

                if not integral then
                    Error(ArityError(ArityFault.InvalidTerm("term", rawArity)))
                else
                    let arity = int64 rawArity
                    let depth = int64 (Stack.depth afterArity)

                    if arity + 1L > depth then
                        Error(StackUnderflow("term", int (arity + 2L), Stack.depth state.Stack))
                    else
                        let rec popArguments count stack arguments =
                            if count = 0 then
                                (List.rev arguments, stack)
                            else
                                match Stack.popIndex stack with
                                | Some(identity, rest) -> popArguments (count - 1) rest (identity :: arguments)
                                | None -> (List.rev arguments, stack)

                        let (arguments, afterArguments) = popArguments (int arity) afterArity []

                        match Stack.popIndex afterArguments with
                        | None -> Error(StackUnderflow("term", int (arity + 2L), Stack.depth state.Stack))
                        | Some(headId, remainingStack) ->
                            let (identity, store) =
                                Store.addTensor (encodeCompound headId arguments) state.Store

                            Ok
                                { state with
                                    Store = store
                                    Stack = Stack.push identity remainingStack
                                }

let typedWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("typed", 2, Stack.depth state.Stack))
    | Some(sortId, afterSort) ->
        match Store.readTextAt sortId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "typed", ExpectedText))
        | Some sortName ->
            match EvaluatorValueSets.readDeclaration sortId state.DeclarationTable state.Store with
            | None -> Error(Unresolved(UnknownKind.Model, sortName))
            | Some _ ->
                match Stack.popIndex afterSort with
                | None -> Error(StackUnderflow("typed", 2, Stack.depth state.Stack))
                | Some(termId, remainingStack) ->
                    let (term, store1) = walk termId Set.empty state.BindingTable state.Store

                    match viewUnificationTerm term store1 with
                    | Typed(existingName, _) when existingName = sortId ->
                        Ok
                            { state with
                                Store = store1
                                Stack = Stack.push term remainingStack
                            }
                    | Typed(existingName, _) -> Error(SortError(SortFault.Mismatch((nameText existingName store1), sortName)))
                    | _ ->
                        let (identity, store2) = Store.addTensor (encodeTyped sortId term) store1

                        Ok
                            { state with
                                Store = store2
                                Stack = Stack.push identity remainingStack
                            }
