module EvaluatorValueSets

// ============================================================
// Sorts: the store-resident (Name, Extension) pair (D12, W3).
//
// A model entry is a vector [nameValue; member1; member2; ...]. A name value
// below zero means "anonymous": a structural set with no nominal seal. The
// models root is an association vector of model entries; latest write wins,
// so `model`, `seal`, and the derived set operations behave as before.
//
// A *declared* sort is a separate entry in the sorts root: the same encoding,
// but its members are the sort's declared domain rather than a particular
// subset. `complement` uses that domain, which is what makes a named sort a
// closed universe instead of the whole lexicon (D3). The domain only grows:
// `set` declares it empty, and `add-member`/`seal` extend it.
//
// Words: [ ] (set literal), seal, predicate-seal, constructor, constructor-seal,
// unname, subset, union, intersect, except, member, same-shape, same-name,
// sort-of, merge, complement.
// ============================================================

open Types
open EvaluatorCommon

/// A sort projection: the nominal name (if sealed), the extension, and — for a
/// predicate sort — the store-resident quotation that decides membership (D31).
type ValueSet =
    {
        Name: EntryIdentity option
        Members: EntryIdentity list
        Predicate: EntryIdentity option
    }

// ---- Encoding --------------------------------------------------------------

/// Encodes a sort constructor: [ConstructorTag; nameId; quotationId]. The
/// quotation decides membership for every sort that names this constructor.
let encodeConstructor (nameId: EntryIdentity) (quotationId: EntryIdentity) : Tensor =
    Tensor.vector
        [
            ConstructorTag
            double (EntryIdentity.value nameId)
            double (EntryIdentity.value quotationId)
        ]

/// Decodes a sort constructor. Anything else is not a constructor.
let decodeConstructor (tensor: Tensor) : (EntryIdentity * EntryIdentity) option =
    if
        Tensor.rank tensor = 1
        && Tensor.size tensor = 3
        && Tensor.item tensor 0 = ConstructorTag
    then
        Some(EntryIdentity.ofInt (int (Tensor.item tensor 1)), EntryIdentity.ofInt (int (Tensor.item tensor 2)))
    else
        None

/// Encodes a finite sort as [nameValue; memberValue; ...] and a predicate sort
/// as [PredicateSortTag; nameValue; quotationId; memberValue; ...].
/// Anonymous sorts use -1.
let encode (sort: ValueSet) : Tensor =
    let nameValue =
        match sort.Name with
        | Some identity -> double (EntryIdentity.value identity)
        | None -> -1.0

    let members =
        sort.Members |> List.map (fun identity -> double (EntryIdentity.value identity))

    match sort.Predicate with
    | None -> Tensor.vector (nameValue :: members)
    | Some quotationId ->
        Tensor.vector (
            [ PredicateSortTag; nameValue; double (EntryIdentity.value quotationId) ]
            @ members
        )

/// Decodes a sort entry. A finite sort is a rank-1 vector with at least a name
/// slot; a predicate sort is `[PredicateSortTag; name; quotation; member…]`.
/// Exactly -1 is the anonymous marker; other negative values are tags (D15,
/// D29, D31) and are not finite sorts.
let decode (tensor: Tensor) : ValueSet option =
    if Tensor.rank tensor = 1 && Tensor.size tensor >= 1 then
        if Tensor.item tensor 0 = PredicateSortTag && Tensor.size tensor >= 3 then
            let rawName = int (Tensor.item tensor 1)

            let name =
                if rawName = -1 then
                    None
                else
                    Some(EntryIdentity.ofInt rawName)

            let members =
                tensor.Data
                |> Array.skip 3
                |> Array.toList
                |> List.map (fun value -> EntryIdentity.ofInt (int value))

            Some
                {
                    Name = name
                    Members = members
                    Predicate = Some(EntryIdentity.ofInt (int (Tensor.item tensor 2)))
                }
        else
            let rawName = int (Tensor.item tensor 0)

            if rawName < -1 then
                None
            else
                let name =
                    if rawName = -1 then
                        None
                    else
                        Some(EntryIdentity.ofInt rawName)

                let members =
                    tensor.Data
                    |> Array.skip 1
                    |> Array.toList
                    |> List.map (fun value -> EntryIdentity.ofInt (int value))

                Some
                    {
                        Name = name
                        Members = members
                        Predicate = None
                    }
    else
        None

// ---- Roots (association vectors of entry identities) ------------------------------

let tableEntries (root: EntryIdentity option) (store: Store) : EntryIdentity list =
    match root with
    | None -> []
    | Some rootId ->
        match Store.lookupTensor rootId store with
        | Some vector ->
            vector.Data
            |> Array.toList
            |> List.map (fun value -> EntryIdentity.ofInt (int value))
        | None -> []

/// Appends an entry identity to a root vector, returning the new root.
let appendToTable (identity: EntryIdentity) (root: EntryIdentity option) (store: Store) : EntryIdentity * Store =
    let pointer = double (EntryIdentity.value identity)

    let vector =
        match root with
        | Some rootId ->
            match Store.lookupTensor rootId store with
            | Some existing -> Tensor.appendElement pointer existing
            | None -> Tensor.vector [ pointer ]
        | None -> Tensor.vector [ pointer ]

    Store.addTensor vector store

let isRegistered (identity: EntryIdentity) (root: EntryIdentity option) (store: Store) : bool =
    tableEntries root store |> List.contains identity

// ---- Reading ---------------------------------------------------------------

/// Latest-write-wins scan of a root for an entry carrying `nameId`.
let findByName (nameId: EntryIdentity) (root: EntryIdentity option) (store: Store) : ValueSet option =
    tableEntries root store
    |> List.fold
        (fun latest entryId ->
            match Store.lookupTensor entryId store |> Option.bind decode with
            | Some sort when sort.Name = Some nameId -> Some sort
            | _ -> latest)
        None

/// Reads a sort entry by address from a root: a text reference (the surface
/// form `"name"` or `:name`) resolves by latest-write-wins name, and a direct
/// entry identity resolves when it is registered in that root.
let private readFromTable (address: EntryIdentity) (root: EntryIdentity option) (store: Store) : ValueSet option =
    match Store.readTextAt address store with
    | Some _ -> findByName address root store
    | None ->
        if isRegistered address root store then
            Store.lookupTensor address store |> Option.bind decode
        else
            None

/// Reads a model by address (a name reference or a model entry identity) from the
/// models root.
let readSet (address: EntryIdentity) (root: EntryIdentity option) (store: Store) : ValueSet option =
    readFromTable address root store

/// Reads a declared sort by address (a name reference or a declaration identity)
/// from the sorts root.
let readDeclaration (address: EntryIdentity) (root: EntryIdentity option) (store: Store) : ValueSet option =
    readFromTable address root store

// ---- Membership ------------------------------------------------------------

let containsMember (identity: EntryIdentity) (members: EntryIdentity list) (store: Store) : bool =
    match Store.lookupTensor identity store with
    | None -> false
    | Some tensor ->
        members
        |> List.exists (fun memberId ->
            match Store.lookupTensor memberId store with
            | Some other -> tensorsEqual tensor other
            | None -> false)

/// The verdict of a membership question. A predicate sort answers with a
/// verdict; an undecidable predicate is never a silent `false` (D31).
type Membership =
    | Member
    | NotMember
    | Undecided of reason: string

/// Runs the predicate quotation through the shared quotationRun. A scheduler word, an
/// exhausted budget, or an evaluation error is `Undecided`, never `false`:
/// membership may refuse to decide, but it never lies.
let private runPredicate
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (quotationId: EntryIdentity)
    (candidateId: EntryIdentity)
    (state: EvaluationState)
    : Membership =
    match EvaluatorQuotationRun.run executeWord quotationId candidateId state with
    | Error reason -> Undecided reason
    | Ok tensor ->
        match scalarValue "predicate" tensor with
        | Ok value -> if value <> 0.0 then Member else NotMember
        | Error _ -> Undecided "the predicate produced no numeric verdict"

/// A sort's narrowing slot holds either a raw predicate quotation (D31) or a
/// named constructor metaobject (D36). Resolve a constructor to its quotation.
let private narrowingQuotation (narrowingId: EntryIdentity) (store: Store) : EntryIdentity =
    match Store.lookupTensor narrowingId store |> Option.bind decodeConstructor with
    | Some(_, quotationId) -> quotationId
    | None -> narrowingId

/// The membership verdict for a value. A finite sort is its extension; a
/// predicate or constructor sort is its base extension narrowed by a quotation.
let memberOf
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (identity: EntryIdentity)
    (sort: ValueSet)
    (state: EvaluationState)
    : Membership =
    if not (containsMember identity sort.Members state.Store) then
        NotMember
    else
        match sort.Predicate with
        | None -> Member
        | Some narrowingId -> runPredicate executeWord (narrowingQuotation narrowingId state.Store) identity state

// ---- Writing ---------------------------------------------------------------

let writeSet (sort: ValueSet) (root: EntryIdentity option) (store: Store) : EntryIdentity * EntryIdentity * Store =
    let (entryId, store1) = Store.addTensor (encode sort) store
    let (newRoot, store2) = appendToTable entryId root store1
    (entryId, newRoot, store2)

/// Declares a sort with these members unless the name already has a
/// declaration; an existing declaration is left untouched. `model` uses this so
/// re-declaring a name does not clear a domain that `admit` or `seal` grew.
let ensureDeclaration
    (nameId: EntryIdentity)
    (members: EntryIdentity list)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity option * Store =
    match findByName nameId root store with
    | Some _ -> (root, store)
    | None ->
        let (_, newRoot, newStore) =
            writeSet
                {
                    Name = Some nameId
                    Members = members
                    Predicate = None
                }
                root
                store

        (Some newRoot, newStore)

/// Extends a sort's declared domain. The domain only grows, so a later
/// subset sealed under the same name cannot shrink the universe. A predicate
/// sort keeps its predicate: the extension is the base, the quotation narrows.
let extendDeclaration
    (nameId: EntryIdentity)
    (members: EntryIdentity list)
    (predicate: EntryIdentity option)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity option * Store =
    let existing =
        match findByName nameId root store with
        | Some sort -> sort.Members
        | None -> []

    let allMembers =
        existing
        @ (members
           |> List.filter (fun identity -> not (containsMember identity existing store)))

    let (_, newRoot, newStore) =
        writeSet
            {
                Name = Some nameId
                Members = allMembers
                Predicate = predicate
            }
            root
            store

    (Some newRoot, newStore)

let setsCount (root: EntryIdentity option) (store: Store) : int = tableEntries root store |> List.length

// ---- Set operations over member identities ---------------------------------------

/// Deduplicates an identity list by structural tensor equality, keeping first sight.
let distinctMembers (identities: EntryIdentity list) (store: Store) : EntryIdentity list =
    identities
    |> List.fold
        (fun accumulated identity ->
            if containsMember identity accumulated store then
                accumulated
            else
                accumulated @ [ identity ])
        []

let unionMembers (left: EntryIdentity list) (right: EntryIdentity list) (store: Store) : EntryIdentity list =
    left
    @ (right |> List.filter (fun identity -> not (containsMember identity left store)))

let intersectMembers (left: EntryIdentity list) (right: EntryIdentity list) (store: Store) : EntryIdentity list =
    left |> List.filter (fun identity -> containsMember identity right store)

let exceptMembers (left: EntryIdentity list) (right: EntryIdentity list) (store: Store) : EntryIdentity list =
    left |> List.filter (fun identity -> not (containsMember identity right store))

let subsetOf (left: EntryIdentity list) (right: EntryIdentity list) (store: Store) : bool =
    left |> List.forall (fun identity -> containsMember identity right store)

let sameShape (left: EntryIdentity list) (right: EntryIdentity list) (store: Store) : bool =
    subsetOf left right store && subsetOf right left store

// ---- Stack plumbing --------------------------------------------------------

let setLabel (sort: ValueSet) (store: Store) : string =
    match sort.Name with
    | Some nameId -> nameText nameId store
    | None -> "anonymous"

let pushBool (state: EvaluationState) (value: bool) (remainingStack: EntryIdentity list) : EvaluationState =
    let (identity, newStore) =
        Store.addTensor (Tensor.scalar (if value then 1.0 else 0.0)) state.Store

    { state with
        Store = newStore
        Stack = Stack.push identity remainingStack
    }

/// Pops one set. A named address that names no model stays `UnknownModel`, so
/// the existing diagnostic does not change; a direct identity that is not a model is
/// `NotASet`.
let popValueSet
    (word: string)
    (stack: EntryIdentity list)
    (state: EvaluationState)
    : Result<ValueSet * EntryIdentity list, EvaluationError> =
    match Stack.popIndex stack with
    | None -> Error(StackUnderflow(word, 1, Stack.depth stack))
    | Some(address, remainingStack) ->
        match readSet address state.SetTable state.Store with
        | Some sort -> Ok(sort, remainingStack)
        | None ->
            match Store.readTextAt address state.Store with
            | Some text -> Error(Unresolved(UnknownKind.Model, text))
            | None -> Error(TypeMismatch(ErrorWord word, ExpectedSet))

let popTwoValueSets
    (word: string)
    (state: EvaluationState)
    : Result<ValueSet * ValueSet * EntryIdentity list, EvaluationError> =
    match popValueSet word state.Stack state with
    | Error error -> Error error
    | Ok(second, afterSecond) ->
        match popValueSet word afterSecond state with
        | Error error -> Error error
        | Ok(first, afterFirst) -> Ok(first, second, afterFirst)

let pushSet (sort: ValueSet) (state: EvaluationState) (remainingStack: EntryIdentity list) : EvaluationState =
    let (entryId, newRoot, newStore) = writeSet sort state.SetTable state.Store

    { state with
        Store = newStore
        SetTable = Some newRoot
        Stack = Stack.push entryId remainingStack
    }

// ---- Literal sets ----------------------------------------------------------

let rec collectSet
    (depth: int)
    (terms: SymbolicAbstraction.Term list)
    (accumulated: SymbolicAbstraction.Term list)
    : Result<SymbolicAbstraction.Term list * SymbolicAbstraction.Term list, EvaluationError> =
    match terms with
    | [] -> Error UnclosedSet
    | SymbolicAbstraction.Word "[" :: rest -> collectSet (depth + 1) rest (SymbolicAbstraction.Word "[" :: accumulated)
    | SymbolicAbstraction.Word "]" :: rest ->
        if depth = 0 then
            Ok(List.rev accumulated, rest)
        else
            collectSet (depth - 1) rest (SymbolicAbstraction.Word "]" :: accumulated)
    | term :: rest -> collectSet depth rest (term :: accumulated)

/// The words a set literal may not contain are the same actor words a
/// quotation run refuses (`EvaluatorQuotationRun.actorWords`, D69). They change
/// the actor or block it, and a literal runs its body on a fresh stack inside
/// the current actor: a `halt` would leave the actor halted while the literal
/// carried on, and a `receive` would switch actors mid-literal. A literal is an
/// expression over values, so it refuses them by name instead of corrupting the
/// actor.
/// `[ ... ]` evaluates the enclosed program on a fresh stack and captures the
/// resulting values as a structural (anonymous) set. It is an extension
/// literal, not a comprehension: `[ 1 2 add ]` is {3}.
let literalWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match collectSet 0 state.Program [] with
    | Error error -> Error error
    | Ok(body, afterSet) ->
        match
            body
            |> List.tryPick (fun term ->
                match term with
                | SymbolicAbstraction.Word name when List.contains name EvaluatorQuotationRun.actorWords -> Some name
                | _ -> None)
        with
        | Some word -> Error(SetLiteralSchedulerWord word)
        | None ->
            let innerStart =
                { state with
                    Program = body
                    Stack = []
                }

            let rec runInner (inner: EvaluationState) : Result<EvaluationState, EvaluationError> =
                match inner.Program with
                | [] -> Ok inner
                | term :: remaining ->
                    if inner.Reductions <= 0 then
                        // W5: the budget is a fairness bound, not a failure. The
                        // literal runs synchronously on a fresh stack, so there
                        // is no peer to yield to; continue with a fresh quantum.
                        runInner
                            { inner with
                                Reductions = DefaultReductions
                            }
                    else
                        let preState =
                            { inner with
                                Program = remaining
                                Reductions = inner.Reductions - 1
                                Location = inner.Location + 1
                                Position = inner.Position + 1
                            }

                        match EvaluatorProgram.executeTerm executeWord term preState with
                        | Error error -> Error error
                        | Ok postState -> runInner postState

            match runInner innerStart with
            | Error error -> Error error
            | Ok inner ->
                let members = distinctMembers (List.rev inner.Stack) inner.Store

                let (entryId, newRoot, newStore) =
                    writeSet
                        {
                            Name = None
                            Members = members
                            Predicate = None
                        }
                        inner.SetTable
                        inner.Store

                Ok
                    { inner with
                        Store = newStore
                        SetTable = Some newRoot
                        Stack = Stack.push entryId state.Stack
                        Program = afterSet
                    }

// ---- Words -----------------------------------------------------------------

let sealWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("seal", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "seal", ExpectedText))
        | Some _ ->
            match popValueSet "seal" afterName state with
            | Error error -> Error error
            | Ok(sort, remainingStack) ->
                let sealedSort =
                    {
                        Name = Some nameId
                        Members = sort.Members
                        Predicate = sort.Predicate
                    }

                let (entryId, newRoot, store1) = writeSet sealedSort state.SetTable state.Store

                let (newDeclarationsRoot, store2) =
                    extendDeclaration nameId sort.Members sort.Predicate state.DeclarationTable store1

                Ok
                    { state with
                        Store = store2
                        SetTable = Some newRoot
                        DeclarationTable = newDeclarationsRoot
                        Stack = Stack.push entryId remainingStack
                    }

/// Names a sorted extension by a predicate quotation (D31): the base extension
/// is the operand sort, and the quotation decides the narrowing. Membership
/// answers Member/NotMember/Undecided; enumeration operations refuse it.
let predicateSealWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("predicate-seal", 3, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "predicate-seal", ExpectedText))
        | Some _ ->
            match Stack.popIndex afterName with
            | None -> Error(StackUnderflow("predicate-seal", 3, Stack.depth state.Stack))
            | Some(quotationId, afterQuotation) ->
                match EvaluatorCode.decode quotationId state.Store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "predicate-seal", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok _ ->
                    match popValueSet "predicate-seal" afterQuotation state with
                    | Error error -> Error error
                    | Ok(baseSort, remainingStack) ->
                        let sort =
                            {
                                Name = Some nameId
                                Members = baseSort.Members
                                Predicate = Some quotationId
                            }

                        let (entryId, newRoot, store1) = writeSet sort state.SetTable state.Store

                        let (newDeclarationsRoot, store2) =
                            extendDeclaration nameId sort.Members sort.Predicate state.DeclarationTable store1

                        Ok
                            { state with
                                Store = store2
                                SetTable = Some newRoot
                                DeclarationTable = newDeclarationsRoot
                                Stack = Stack.push entryId remainingStack
                            }

/// `"name" { predicate } constructor` declares a named, store-resident sort
/// constructor. The quotation is the implementation: constructing a sort with
/// this constructor does not add an F# case (D36).
let constructorWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("constructor", 2, Stack.depth state.Stack))
    | Some(quotationId, afterQuotation) ->
        match EvaluatorCode.decode quotationId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "constructor", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            match Stack.popIndex afterQuotation with
            | None -> Error(StackUnderflow("constructor", 2, Stack.depth state.Stack))
            | Some(nameId, remaining) ->
                match Store.readTextAt nameId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "constructor", ExpectedText))
                | Some _ ->
                    let (identity, store) =
                        Store.addTensor (encodeConstructor nameId quotationId) state.Store

                    Ok
                        { state with
                            Store = store
                            Stack = Stack.push identity remaining
                        }

/// `sort constructor "name" constructor-seal` builds a named sort whose base
/// extension is the operand sort and whose membership is decided by the named
/// constructor. A non-constructor reference is refused by name.
let constructorSealWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("constructor-seal", 3, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "constructor-seal", ExpectedText))
        | Some _ ->
            match Stack.popIndex afterName with
            | None -> Error(StackUnderflow("constructor-seal", 3, Stack.depth state.Stack))
            | Some(constructorId, afterConstructor) ->
                match Store.lookupTensor constructorId state.Store |> Option.bind decodeConstructor with
                | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value constructorId), ExpectedConstructor))
                | Some _ ->
                    match popValueSet "constructor-seal" afterConstructor state with
                    | Error error -> Error error
                    | Ok(baseSort, remainingStack) ->
                        let sort =
                            {
                                Name = Some nameId
                                Members = baseSort.Members
                                Predicate = Some constructorId
                            }

                        let (entryId, newRoot, store1) = writeSet sort state.SetTable state.Store

                        let (newDeclarationsRoot, store2) =
                            extendDeclaration nameId sort.Members (Some constructorId) state.DeclarationTable store1

                        Ok
                            { state with
                                Store = store2
                                SetTable = Some newRoot
                                DeclarationTable = newDeclarationsRoot
                                Stack = Stack.push entryId remainingStack
                            }

let unnameWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popValueSet "unname" state.Stack state with
    | Error error -> Error error
    | Ok(sort, remainingStack) ->
        Ok(
            pushSet
                {
                    Name = None
                    Members = sort.Members
                    Predicate = sort.Predicate
                }
                state
                remainingStack
        )

/// A predicate sort's extension cannot be enumerated, so an operation that
/// needs the whole extension refuses by name instead of returning a half-set.
let refuseEnumeration (word: string) (sorts: ValueSet list) : Result<unit, EvaluationError> =
    if sorts |> List.exists (fun sort -> sort.Predicate.IsSome) then
        Error(SortError(SortFault.Undecided (word + " cannot enumerate a predicate sort's extension")))
    else
        Ok()

let subsetWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match popTwoValueSets "subset" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        let rec check (remaining: EntryIdentity list) : Result<bool, EvaluationError> =
            match remaining with
            | [] -> Ok true
            | identity :: rest ->
                match memberOf executeWord identity first state with
                | NotMember -> check rest
                | Undecided reason -> Error(SortError(SortFault.Undecided reason))
                | Member ->
                    match memberOf executeWord identity second state with
                    | Member -> check rest
                    | NotMember -> Ok false
                    | Undecided reason -> Error(SortError(SortFault.Undecided reason))

        check first.Members
        |> Result.map (fun result -> pushBool state result remainingStack)

/// `sort { predicate } filter` narrows a sort to the members the predicate
/// accepts (D34). The result is an anonymous structural sort; an undecidable
/// predicate refuses by name instead of silently dropping the member.
let filterWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("filter", 2, Stack.depth state.Stack))
    | Some(quotationId, afterQuotation) ->
        match EvaluatorCode.decode quotationId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "filter", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            match popValueSet "filter" afterQuotation state with
            | Error error -> Error error
            | Ok(sort, remainingStack) ->
                let rec keep
                    (remaining: EntryIdentity list)
                    (accumulated: EntryIdentity list)
                    : Result<EntryIdentity list, EvaluationError> =
                    match remaining with
                    | [] -> Ok(List.rev accumulated)
                    | identity :: rest ->
                        match memberOf executeWord identity sort state with
                        | NotMember -> keep rest accumulated
                        | Undecided reason -> Error(SortError(SortFault.Undecided reason))
                        | Member ->
                            match runPredicate executeWord quotationId identity state with
                            | Member -> keep rest (identity :: accumulated)
                            | NotMember -> keep rest accumulated
                            | Undecided reason -> Error(SortError(SortFault.Undecided reason))

                keep sort.Members []
                |> Result.map (fun members ->
                    pushSet
                        {
                            Name = None
                            Members = members
                            Predicate = None
                        }
                        state
                        remainingStack)

/// Intersection and union return structural sets: the name is a nominal seal,
/// and combining two extensions has no canonical seal of its own.
let unionWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoValueSets "union" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        refuseEnumeration "union" [ first; second ]
        |> Result.map (fun () ->
            let members = unionMembers first.Members second.Members state.Store

            pushSet
                {
                    Name = None
                    Members = members
                    Predicate = None
                }
                state
                remainingStack)

let intersectWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoValueSets "intersect" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        refuseEnumeration "intersect" [ first; second ]
        |> Result.map (fun () ->
            let members = intersectMembers first.Members second.Members state.Store

            pushSet
                {
                    Name = None
                    Members = members
                    Predicate = None
                }
                state
                remainingStack)

let exceptWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoValueSets "except" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        refuseEnumeration "except" [ first; second ]
        |> Result.map (fun () ->
            let members = exceptMembers first.Members second.Members state.Store

            pushSet
                {
                    Name = None
                    Members = members
                    Predicate = None
                }
                state
                remainingStack)

let memberWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("member", 2, Stack.depth state.Stack))
    | Some(setAddress, afterSet) ->
        match Stack.popIndex afterSet with
        | None -> Error(StackUnderflow("member", 2, Stack.depth state.Stack))
        | Some(valueId, afterValue) ->
            match readSet setAddress state.SetTable state.Store with
            | Some sort ->
                match memberOf executeWord valueId sort state with
                | Member -> Ok(pushBool state true afterValue)
                | NotMember -> Ok(pushBool state false afterValue)
                | Undecided reason -> Error(SortError(SortFault.Undecided reason))
            | None ->
                match Store.readTextAt setAddress state.Store with
                | Some text -> Error(Unresolved(UnknownKind.Model, text))
                | None -> Error(TypeMismatch(ErrorWord "member", ExpectedSet))

let sameShapeWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoValueSets "same-shape" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        refuseEnumeration "same-shape" [ first; second ]
        |> Result.map (fun () -> pushBool state (sameShape first.Members second.Members state.Store) remainingStack)

let sameNameWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoValueSets "same-name" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) -> Ok(pushBool state (first.Name = second.Name) remainingStack)

/// `A B sort-of` reads B as a sort reference and asks whether A carries it.
let sortOfWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("sort-of", 2, Stack.depth state.Stack))
    | Some(referenceAddress, afterReference) ->
        match Stack.popIndex afterReference with
        | None -> Error(StackUnderflow("sort-of", 2, Stack.depth state.Stack))
        | Some(valueAddress, afterValue) ->
            let referenceName =
                match readSet referenceAddress state.SetTable state.Store with
                | Some sort -> sort.Name
                | None -> None

            let valueName =
                match readSet valueAddress state.SetTable state.Store with
                | Some sort -> sort.Name
                | None -> None

            match referenceName, valueName with
            | Some expected, Some actual -> Ok(pushBool state (expected = actual) afterValue)
            | _ -> Ok(pushBool state false afterValue)

/// `A B merge` is the sort-preserving union: the operands must carry the same
/// seal. `union` is the widening version, so the refusal names that next move.
let mergeWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popTwoValueSets "merge" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        match refuseEnumeration "merge" [ first; second ] with
        | Error error -> Error error
        | Ok() ->
            if first.Name <> second.Name then
                Error(SortError(SortFault.Mismatch((setLabel first state.Store), (setLabel second state.Store))))
            else
                let members = unionMembers first.Members second.Members state.Store

                Ok(
                    pushSet
                        {
                            Name = first.Name
                            Members = members
                            Predicate = None
                        }
                        state
                        remainingStack
                )

/// `A complement` defaults the universe to A's declared sort. A structural set
/// has no closed universe, so that is a named refusal rather than the whole
/// lexicon (the defect D3 fixed).
let complementWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match popValueSet "complement" state.Stack state with
    | Error error -> Error error
    | Ok(sort, remainingStack) ->
        match sort.Name with
        | None -> Error(SortError(SortFault.NotClosed ("anonymous set")))
        | Some nameId ->
            match readDeclaration nameId state.DeclarationTable state.Store with
            | None -> Error(SortError(SortFault.NotClosed (nameText nameId state.Store)))
            | Some declaration ->
                match refuseEnumeration "complement" [ sort; declaration ] with
                | Error error -> Error error
                | Ok() ->
                    let members = exceptMembers declaration.Members sort.Members state.Store

                    Ok(
                        pushSet
                            {
                                Name = sort.Name
                                Members = members
                                Predicate = None
                            }
                            state
                            remainingStack
                    )
