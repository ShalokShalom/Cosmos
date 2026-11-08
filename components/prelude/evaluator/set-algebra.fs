module EvaluatorSetAlgebra

// ============================================================
// SetAlgebra: store-resident models and set algebra (decision D2, Tier 1).
//
// A model is a subset of a sort (D12, W3): an entry [nameValue; member...] in
// the models root, where the name is the sort seal. `and` and `or` are
// intersection and union, and `nor` is the set complement `U \ (A ∪ B)`;
// `properties.fs` pins those laws. The sort registry itself lives in
// `EvaluatorValueSets`, which also owns the structural set words.
//
// Words: set, add-member, and, or, nor, is.
// ============================================================

open Types
open EvaluatorCommon

/// Writes a derived model under a fresh name and declares its domain, then
/// pushes the name reference (the shape the existing surface already returns).
let derivedSet
    (prefix: string)
    (members: EntryIdentity list)
    (state: EvaluationState)
    (remainingStack: EntryIdentity list)
    : Result<EvaluationState, EvaluationError> =
    let newName =
        prefix + "_" + string (EvaluatorValueSets.setsCount state.SetTable state.Store)

    let (nameId, store1) = Store.addText newName state.Store

    let (_, newRoot, store2) =
        EvaluatorValueSets.writeSet
            {
                Name = Some nameId
                Members = members
                Predicate = None
            }
            state.SetTable
            store1

    let (newDeclarationsRoot, store3) =
        EvaluatorValueSets.extendDeclaration nameId members None state.DeclarationTable store2

    Ok
        { state with
            Store = store3
            SetTable = Some newRoot
            DeclarationTable = newDeclarationsRoot
            Stack = Stack.push nameId remainingStack
        }

let setWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("set", 1, Stack.depth state.Stack))
    | Some(nameId, remainingStack) ->
        let (newDeclarationsRoot, store1) =
            EvaluatorValueSets.ensureDeclaration nameId [] state.DeclarationTable state.Store

        let (_, newRoot, store2) =
            EvaluatorValueSets.writeSet
                {
                    Name = Some nameId
                    Members = []
                    Predicate = None
                }
                state.SetTable
                store1

        Ok
            { state with
                Store = store2
                SetTable = Some newRoot
                DeclarationTable = newDeclarationsRoot
                Stack = remainingStack
            }

let addMemberWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("add-member", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Stack.popIndex afterName with
        | None -> Error(StackUnderflow("add-member", 2, Stack.depth state.Stack))
        | Some(valueId, afterValue) ->
            match EvaluatorValueSets.readSet nameId state.SetTable state.Store with
            | None -> Error(Unresolved(UnknownKind.Model, (nameText nameId state.Store)))
            | Some sort when sort.Predicate.IsSome ->
                Error(SortError(SortFault.Undecided ("add-member cannot extend a predicate sort; the predicate decides membership")))
            | Some sort ->
                let newMembers =
                    if EvaluatorValueSets.containsMember valueId sort.Members state.Store then
                        sort.Members
                    else
                        valueId :: sort.Members

                let (_, newRoot, store1) =
                    EvaluatorValueSets.writeSet
                        {
                            Name = sort.Name
                            Members = newMembers
                            Predicate = None
                        }
                        state.SetTable
                        state.Store

                let (newDeclarationsRoot, store2) =
                    match sort.Name with
                    | Some sortName ->
                        EvaluatorValueSets.extendDeclaration sortName [ valueId ] None state.DeclarationTable store1
                    | None -> (state.DeclarationTable, store1)

                Ok
                    { state with
                        Store = store2
                        SetTable = Some newRoot
                        DeclarationTable = newDeclarationsRoot
                        Stack = afterValue
                    }

let andWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match EvaluatorValueSets.popTwoValueSets "and" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        match EvaluatorValueSets.refuseEnumeration "and" [ first; second ] with
        | Error error -> Error error
        | Ok() ->
            let members =
                EvaluatorValueSets.intersectMembers first.Members second.Members state.Store

            derivedSet "and" members state remainingStack

let orWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match EvaluatorValueSets.popTwoValueSets "or" state with
    | Error error -> Error error
    | Ok(first, second, remainingStack) ->
        match EvaluatorValueSets.refuseEnumeration "or" [ first; second ] with
        | Error error -> Error error
        | Ok() ->
            let members =
                EvaluatorValueSets.unionMembers first.Members second.Members state.Store

            derivedSet "or" members state remainingStack

/// `nor` over an explicit universe: `U A B nor` yields `U \ (A ∪ B)`.
let norWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match EvaluatorValueSets.popValueSet "nor" state.Stack state with
    | Error error -> Error error
    | Ok(second, afterSecond) ->
        match EvaluatorValueSets.popValueSet "nor" afterSecond state with
        | Error error -> Error error
        | Ok(first, afterFirst) ->
            match EvaluatorValueSets.popValueSet "nor" afterFirst state with
            | Error error -> Error error
            | Ok(universe, afterUniverse) ->
                match EvaluatorValueSets.refuseEnumeration "nor" [ universe; first; second ] with
                | Error error -> Error error
                | Ok() ->
                    let union = EvaluatorValueSets.unionMembers first.Members second.Members state.Store
                    let complement = EvaluatorValueSets.exceptMembers universe.Members union state.Store
                    derivedSet "nor" complement state afterUniverse

let isWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("is", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Stack.popIndex afterName with
        | None -> Error(StackUnderflow("is", 2, Stack.depth state.Stack))
        | Some(valueId, afterValue) ->
            match EvaluatorValueSets.readSet nameId state.SetTable state.Store with
            | None -> Error(Unresolved(UnknownKind.Model, (nameText nameId state.Store)))
            | Some sort ->
                match EvaluatorValueSets.memberOf executeWord valueId sort state with
                | EvaluatorValueSets.Member -> Ok(EvaluatorValueSets.pushBool state true afterValue)
                | EvaluatorValueSets.NotMember -> Ok(EvaluatorValueSets.pushBool state false afterValue)
                | EvaluatorValueSets.Undecided reason -> Error(SortError(SortFault.Undecided reason))
