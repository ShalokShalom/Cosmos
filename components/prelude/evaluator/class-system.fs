module EvaluatorClassSystem

// ============================================================
// Classes, slots, and instances (D46).
//
// This is the first cell-behavior slice: `class`, `slot`, `create-instance`,
// `slot-value`, and `set-slot-value`. Classes and slots are image entries; an
// instance has a stable identity and appended state. The words are the lab
// surface, not the child-facing Basic English grammar.
//
// Slot definitions are global by name for now: a class references slot
// definitions, and the effective slots are its own slots followed by its
// superclasses' slots. A deterministic class precedence list is D47.
// ============================================================

open Types
open EvaluatorCommon
open EvaluatorCellSystem
open EvaluatorValueSets

let standardMetaclassName = "StandardClass"

let private splitNames (text: string) : string list =
    text.Split([| ',' |])
    |> Array.toList
    |> List.map (fun name -> name.Trim())
    |> List.filter (fun name -> name <> "")

let private ensureStandardMetaclass (store: Store) : EntryIdentity * Store =
    let (nameId, storeWithName) = Store.addText standardMetaclassName store

    match findMetaclassByName nameId storeWithName with
    | Some metaclass -> (metaclass.Address, storeWithName)
    | None -> Store.addTensor (encodeMetaclass nameId [] []) storeWithName

let private readClassAt (classId: EntryIdentity) (state: EvaluationState) : Result<ClassDefinition, EvaluationError> =
    match Store.lookupTensor classId state.Store |> Option.bind (decodeClass classId) with
    | Some classDefinition -> Ok classDefinition
    | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value classId), ExpectedClass))

let private readClassByName
    (nameId: EntryIdentity)
    (state: EvaluationState)
    : Result<ClassDefinition, EvaluationError> =
    match findClass nameId state.ClassTable state.Store with
    | Some classId -> readClassAt classId state
    | None -> Error(Unresolved(UnknownKind.Class, (nameText nameId state.Store)))

let private readInstanceAt
    (instanceId: EntryIdentity)
    (state: EvaluationState)
    : Result<InstanceIdentity, EvaluationError> =
    match
        Store.lookupTensor instanceId state.Store
        |> Option.bind (decodeInstance instanceId)
    with
    | Some instance -> Ok instance
    | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value instanceId), ExpectedInstance))

/// C3 linearization of the superclass graph. The result is most-specific first
/// and includes the class itself. A cycle is `ClassCycle`; a conflicting local
/// order is `InconsistentPrecedence`, never a silent pick.
let classPrecedence (classDefinition: ClassDefinition) (store: Store) : Result<EntryIdentity list, EvaluationError> =
    let rec merge
        (lists: EntryIdentity list list)
        (accumulated: EntryIdentity list)
        : Result<EntryIdentity list, EvaluationError> =
        let remaining = lists |> List.filter (fun list -> not (List.isEmpty list))

        if List.isEmpty remaining then
            Ok(List.rev accumulated)
        else
            let heads = remaining |> List.map List.head |> List.distinct

            let candidate =
                heads
                |> List.tryFind (fun head ->
                    remaining
                    |> List.forall (fun list ->
                        match list with
                        | _ :: tail -> not (List.contains head tail)
                        | [] -> true))

            match candidate with
            | None -> Error(InconsistentPrecedence(nameText classDefinition.Name store))
            | Some head ->
                let next =
                    remaining
                    |> List.map (fun list ->
                        match list with
                        | current :: tail when current = head -> tail
                        | _ -> list)

                merge next (head :: accumulated)

    let rec linearize
        (visited: Set<EntryIdentity>)
        (current: ClassDefinition)
        : Result<EntryIdentity list, EvaluationError> =
        if visited.Contains current.Address then
            Error(ClassCycle(nameText current.Name store))
        else
            let visited = visited.Add current.Address

            let superclasses =
                current.Superclasses
                |> List.map (fun superclassId ->
                    match Store.lookupTensor superclassId store |> Option.bind (decodeClass superclassId) with
                    | Some superclass -> Ok superclass
                    | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value superclassId), ExpectedClass)))

            let rec collect
                (remaining: Result<EntryIdentity list, EvaluationError> list)
                (accumulated: EntryIdentity list list)
                : Result<EntryIdentity list list, EvaluationError> =
                match remaining with
                | [] -> Ok(List.rev accumulated)
                | Ok linearization :: rest -> collect rest (linearization :: accumulated)
                | Error error :: _ -> Error error

            superclasses
            |> List.map (fun superclass -> superclass |> Result.bind (fun value -> linearize visited value))
            |> fun linearizations ->
                collect linearizations []
                |> Result.bind (fun lists ->
                    merge (lists @ [ current.Superclasses ]) []
                    |> Result.map (fun merged -> current.Address :: merged))

    linearize Set.empty classDefinition

/// Whether `subclassId` is `superclassId` or one of its subclasses.
let isSubclass
    (subclassId: EntryIdentity)
    (superclassId: EntryIdentity)
    (store: Store)
    : Result<bool, EvaluationError> =
    match Store.lookupTensor subclassId store |> Option.bind (decodeClass subclassId) with
    | Some classDefinition -> classPrecedence classDefinition store |> Result.map (List.contains superclassId)
    | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value subclassId), ExpectedClass))

/// Whether the value is an instance of the class or one of its subclasses.
let isInstanceOf (instanceId: EntryIdentity) (classId: EntryIdentity) (store: Store) : Result<bool, EvaluationError> =
    match Store.lookupTensor instanceId store |> Option.bind (decodeInstance instanceId) with
    | Some instance -> isSubclass instance.Class classId store
    | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value instanceId), ExpectedInstance))

/// The effective slots follow the class precedence list, so a slot defined by
/// a more specific class wins and a duplicate name appears once. A class that
/// cannot be linearized has no effective slots; the class word refuses such a
/// class before it enters the class root.
let effectiveSlots (classDefinition: ClassDefinition) (store: Store) : SlotDefinition list =
    match classPrecedence classDefinition store with
    | Error _ -> []
    | Ok precedence ->
        precedence
        |> List.collect (fun classId ->
            match Store.lookupTensor classId store |> Option.bind (decodeClass classId) with
            | Some current ->
                current.Slots
                |> List.choose (fun slotId -> Store.lookupTensor slotId store |> Option.bind (decodeSlot slotId))
            | None -> [])
        |> List.distinctBy (fun slot -> slot.Name)

let private findSlot (nameId: EntryIdentity) (classDefinition: ClassDefinition) (store: Store) : SlotDefinition option =
    effectiveSlots classDefinition store
    |> List.tryFind (fun slot -> slot.Name = nameId)

let private readValueSet (slot: SlotDefinition) (state: EvaluationState) : Result<ValueSet, EvaluationError> =
    let fromSets = EvaluatorValueSets.readSet slot.ValueSet state.SetTable state.Store

    let fromDeclarations =
        EvaluatorValueSets.readDeclaration slot.ValueSet state.DeclarationTable state.Store

    match fromSets |> Option.orElse fromDeclarations with
    | Some valueSet -> Ok valueSet
    | None -> Error(Unresolved(UnknownKind.ValueSet, (nameText slot.Name state.Store)))

let private validateValue
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (slot: SlotDefinition)
    (valueId: EntryIdentity)
    (state: EvaluationState)
    : Result<unit, EvaluationError> =
    readValueSet slot state
    |> Result.bind (fun valueSet ->
        match EvaluatorValueSets.memberOf executeWord valueId valueSet state with
        | Member -> Ok()
        | NotMember -> Error(SortError(SortFault.SlotMismatch (nameText slot.Name state.Store)))
        | Undecided reason -> Error(SortError(SortFault.Undecided reason)))

let private bindingsOf (instanceId: EntryIdentity) (state: EvaluationState) : (EntryIdentity * EntryIdentity) list =
    match findInstanceState instanceId state.InstanceStateTable state.Store with
    | None -> []
    | Some stateId ->
        match
            Store.lookupTensor stateId state.Store
            |> Option.bind (decodeInstanceState stateId)
        with
        | Some instanceState -> instanceState.Bindings
        | None -> []

let private initialBindings (classDefinition: ClassDefinition) (store: Store) : (EntryIdentity * EntryIdentity) list =
    effectiveSlots classDefinition store
    |> List.choose (fun slot ->
        match slot.Initialization, slot.Default with
        | Defaulted, Some value -> Some(slot.Address, value)
        | _ -> None)
    |> List.distinctBy fst

/// `valueSetName "slotName" slot` declares a slot definition. The value set is
/// resolved by name when the slot is used, so a later set redefinition is
/// visible (latest-write-wins).
let slotWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("slot", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "slot", ExpectedText))
        | Some _ ->
            match Stack.popIndex afterName with
            | None -> Error(StackUnderflow("slot", 2, Stack.depth state.Stack))
            | Some(valueSetId, afterValueSet) ->
                match Store.readTextAt valueSetId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "slot", ExpectedText))
                | Some valueSetName ->
                    match
                        EvaluatorValueSets.readSet valueSetId state.SetTable state.Store
                        |> Option.orElse (
                            EvaluatorValueSets.readDeclaration valueSetId state.DeclarationTable state.Store
                        )
                    with
                    | None -> Error(Unresolved(UnknownKind.ValueSet, valueSetName))
                    | Some _ ->
                        let (slotId, store) =
                            Store.addTensor (encodeSlot nameId valueSetId None Uninitialized) state.Store

                        Ok
                            { state with
                                Store = store
                                Stack = Stack.push slotId afterValueSet
                            }

/// `"ClassName" "superclassNames" "slotNames" class` declares a class. The
/// names are comma-separated; an empty text means none. The class root is
/// latest-write-wins, so a redefinition is a new class entry.
let classWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("class", 3, Stack.depth state.Stack))
    | Some(slotListId, afterSlotList) ->
        match Store.readTextAt slotListId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "class", ExpectedText))
        | Some slotListText ->
            match Stack.popIndex afterSlotList with
            | None -> Error(StackUnderflow("class", 3, Stack.depth state.Stack))
            | Some(superclassListId, afterSuperclassList) ->
                match Store.readTextAt superclassListId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "class", ExpectedText))
                | Some superclassListText ->
                    match Stack.popIndex afterSuperclassList with
                    | None -> Error(StackUnderflow("class", 3, Stack.depth state.Stack))
                    | Some(nameId, afterName) ->
                        match Store.readTextAt nameId state.Store with
                        | None -> Error(TypeMismatch(ErrorWord "class", ExpectedText))
                        | Some _ ->
                            let resolveSuperclass (name: string) : Result<EntryIdentity, EvaluationError> =
                                let (superNameId, storeWithName) = Store.addText name state.Store

                                match findClass superNameId state.ClassTable storeWithName with
                                | Some classId -> Ok classId
                                | None -> Error(Unresolved(UnknownKind.Class, name))

                            let resolveSlot (name: string) : Result<EntryIdentity, EvaluationError> =
                                let (slotNameId, storeWithName) = Store.addText name state.Store

                                match findSlotByName slotNameId storeWithName with
                                | Some slot -> Ok slot.Address
                                | None -> Error(Unresolved(UnknownKind.Slot, name))

                            let superclasses =
                                splitNames superclassListText
                                |> List.map resolveSuperclass
                                |> List.fold
                                    (fun current next ->
                                        current
                                        |> Result.bind (fun values ->
                                            next |> Result.map (fun value -> value :: values)))
                                    (Ok [])
                                |> Result.map List.rev

                            let slots =
                                splitNames slotListText
                                |> List.map resolveSlot
                                |> List.fold
                                    (fun current next ->
                                        current
                                        |> Result.bind (fun values ->
                                            next |> Result.map (fun value -> value :: values)))
                                    (Ok [])
                                |> Result.map List.rev

                            superclasses
                            |> Result.bind (fun superclassIds ->
                                slots
                                |> Result.bind (fun slotIds ->
                                    let (metaclassId, storeWithMetaclass) = ensureStandardMetaclass state.Store

                                    let (classId, storeWithClass) =
                                        Store.addTensor
                                            (encodeClass nameId metaclassId None superclassIds slotIds)
                                            storeWithMetaclass

                                    let classDefinition =
                                        {
                                            Address = classId
                                            Name = nameId
                                            Metaclass = metaclassId
                                            Combination = None
                                            Superclasses = superclassIds
                                            Slots = slotIds
                                        }

                                    match classPrecedence classDefinition storeWithClass with
                                    | Error error -> Error error
                                    | Ok _ ->
                                        let (newRoot, storeWithRoot) =
                                            addClass nameId classId state.ClassTable storeWithClass

                                        Ok
                                            { state with
                                                Store = storeWithRoot
                                                ClassTable = Some newRoot
                                                Stack = Stack.push classId afterName
                                            }))

/// `"ClassName" create-instance` creates a stable instance identity and an
/// appended initial state. A class is resolved by name from the class root.
let createInstanceWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("create-instance", 1, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "create-instance", ExpectedText))
        | Some _ ->
            match findClass nameId state.ClassTable state.Store with
            | None -> Error(Unresolved(UnknownKind.Class, (nameText nameId state.Store)))
            | Some classId ->
                match readClassAt classId state with
                | Error error -> Error error
                | Ok classDefinition ->
                    let number = state.Store.NextIndex

                    let (instanceId, storeWithInstance) =
                        Store.addTensor (encodeInstance classId number) state.Store

                    let bindings = initialBindings classDefinition storeWithInstance

                    let (stateId, storeWithState) =
                        Store.addTensor (encodeInstanceState instanceId bindings) storeWithInstance

                    let (newRoot, storeWithRoot) =
                        addInstanceState instanceId stateId state.InstanceStateTable storeWithState

                    Ok
                        { state with
                            Store = storeWithRoot
                            InstanceStateTable = Some newRoot
                            Stack = Stack.push instanceId afterName
                        }

/// `instance "slotName" slot-value` reads the latest state. A missing slot and
/// an unbound slot are distinct named refusals.
let slotValueWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("slot-value", 2, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "slot-value", ExpectedText))
        | Some slotName ->
            match Stack.popIndex afterName with
            | None -> Error(StackUnderflow("slot-value", 2, Stack.depth state.Stack))
            | Some(instanceId, remainingStack) ->
                readInstanceAt instanceId state
                |> Result.bind (fun instance ->
                    readClassAt instance.Class state
                    |> Result.bind (fun classDefinition ->
                        match findSlot nameId classDefinition state.Store with
                        | None -> Error(MissingSlot slotName)
                        | Some slot ->
                            match
                                findInstanceState instanceId state.InstanceStateTable state.Store
                                |> Option.bind (fun stateId ->
                                    Store.lookupTensor stateId state.Store
                                    |> Option.bind (decodeInstanceState stateId))
                            with
                            | None -> Error(UnboundSlot slotName)
                            | Some instanceState ->
                                match
                                    instanceState.Bindings
                                    |> List.tryFind (fun (slotId, _) -> slotId = slot.Address)
                                with
                                | None -> Error(UnboundSlot slotName)
                                | Some(_, valueId) ->
                                    Ok
                                        { state with
                                            Stack = Stack.push valueId remainingStack
                                        }))

/// `instance value "slotName" set-slot-value` validates the value against the
/// slot's value set and appends a new state entry. The old state stays in the
/// image; the root points at the newest one. The word leaves the value.
let setSlotValueWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("set-slot-value", 3, Stack.depth state.Stack))
    | Some(nameId, afterName) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "set-slot-value", ExpectedText))
        | Some slotName ->
            match Stack.popIndex afterName with
            | None -> Error(StackUnderflow("set-slot-value", 3, Stack.depth state.Stack))
            | Some(valueId, afterValue) ->
                match Stack.popIndex afterValue with
                | None -> Error(StackUnderflow("set-slot-value", 3, Stack.depth state.Stack))
                | Some(instanceId, remainingStack) ->
                    readInstanceAt instanceId state
                    |> Result.bind (fun instance ->
                        readClassAt instance.Class state
                        |> Result.bind (fun classDefinition ->
                            match findSlot nameId classDefinition state.Store with
                            | None -> Error(MissingSlot slotName)
                            | Some slot ->
                                validateValue executeWord slot valueId state
                                |> Result.bind (fun () ->
                                    let bindings =
                                        bindingsOf instance.Address state
                                        |> List.filter (fun (slotId, _) -> slotId <> slot.Address)
                                        |> fun current -> current @ [ (slot.Address, valueId) ]

                                    let (stateId, storeWithState) =
                                        Store.addTensor (encodeInstanceState instance.Address bindings) state.Store

                                    let (newRoot, storeWithRoot) =
                                        addInstanceState instanceId stateId state.InstanceStateTable storeWithState

                                    Ok
                                        { state with
                                            Store = storeWithRoot
                                            InstanceStateTable = Some newRoot
                                            Stack = Stack.push valueId remainingStack
                                        })))

let private readMetaclassAt
    (metaclassId: EntryIdentity)
    (state: EvaluationState)
    : Result<MetaclassDefinition, EvaluationError> =
    match
        Store.lookupTensor metaclassId state.Store
        |> Option.bind (decodeMetaclass metaclassId)
    with
    | Some metaclass -> Ok metaclass
    | None -> Error(Unresolved(UnknownKind.Metaclass, (nameText metaclassId state.Store)))

/// `"Name" "superclassNames" "slotNames" metaclass` declares a metaclass. The
/// superclass and slot names are comma-separated; an empty text means none.
let metaclassWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("metaclass", 3, Stack.depth state.Stack))
    | Some(slotListId, afterSlotList) ->
        match Store.readTextAt slotListId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "metaclass", ExpectedText))
        | Some slotListText ->
            match Stack.popIndex afterSlotList with
            | None -> Error(StackUnderflow("metaclass", 3, Stack.depth state.Stack))
            | Some(superclassListId, afterSuperclassList) ->
                match Store.readTextAt superclassListId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "metaclass", ExpectedText))
                | Some superclassListText ->
                    match Stack.popIndex afterSuperclassList with
                    | None -> Error(StackUnderflow("metaclass", 3, Stack.depth state.Stack))
                    | Some(nameId, remainingStack) ->
                        match Store.readTextAt nameId state.Store with
                        | None -> Error(TypeMismatch(ErrorWord "metaclass", ExpectedText))
                        | Some _ ->
                            let resolveSuperclass (name: string) : Result<EntryIdentity, EvaluationError> =
                                let (superNameId, storeWithName) = Store.addText name state.Store

                                match findMetaclassByName superNameId storeWithName with
                                | Some metaclass -> Ok metaclass.Address
                                | None -> Error(Unresolved(UnknownKind.Metaclass, name))

                            let resolveSlot (name: string) : Result<EntryIdentity, EvaluationError> =
                                let (slotNameId, storeWithName) = Store.addText name state.Store

                                match findSlotByName slotNameId storeWithName with
                                | Some slot -> Ok slot.Address
                                | None -> Error(Unresolved(UnknownKind.Slot, name))

                            let superclasses =
                                splitNames superclassListText
                                |> List.map resolveSuperclass
                                |> List.fold
                                    (fun current next ->
                                        current
                                        |> Result.bind (fun values ->
                                            next |> Result.map (fun value -> value :: values)))
                                    (Ok [])
                                |> Result.map List.rev

                            let slots =
                                splitNames slotListText
                                |> List.map resolveSlot
                                |> List.fold
                                    (fun current next ->
                                        current
                                        |> Result.bind (fun values ->
                                            next |> Result.map (fun value -> value :: values)))
                                    (Ok [])
                                |> Result.map List.rev

                            superclasses
                            |> Result.bind (fun superclassIds ->
                                slots
                                |> Result.map (fun slotIds ->
                                    let (metaclassId, storeWithMetaclass) =
                                        Store.addTensor (encodeMetaclass nameId superclassIds slotIds) state.Store

                                    { state with
                                        Store = storeWithMetaclass
                                        Stack = Stack.push metaclassId remainingStack
                                    }))

/// `"ClassName" "MetaclassName" use-metaclass` re-declares the class with the
/// named metaclass, keeping its superclasses and slots. The class root is
/// latest-write-wins, so the old entry stays in the image.
let useMetaclassWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("use-metaclass", 2, Stack.depth state.Stack))
    | Some(metaclassNameId, afterMetaclassName) ->
        match Store.readTextAt metaclassNameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "use-metaclass", ExpectedText))
        | Some metaclassName ->
            match Stack.popIndex afterMetaclassName with
            | None -> Error(StackUnderflow("use-metaclass", 2, Stack.depth state.Stack))
            | Some(classNameId, remainingStack) ->
                match Store.readTextAt classNameId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "use-metaclass", ExpectedText))
                | Some _ ->
                    (match findClass classNameId state.ClassTable state.Store with
                     | Some classId -> Ok classId
                     | None -> Error(Unresolved(UnknownKind.Class, (nameText classNameId state.Store))))
                    |> Result.bind (fun classId ->
                        readClassAt classId state
                        |> Result.bind (fun classDefinition ->
                            let metaclassResult =
                                if metaclassName = standardMetaclassName then
                                    let (standardId, storeWithStandard) = ensureStandardMetaclass state.Store
                                    Ok(standardId, storeWithStandard)
                                else
                                    match findMetaclassByName metaclassNameId state.Store with
                                    | Some metaclass -> Ok(metaclass.Address, state.Store)
                                    | None -> Error(Unresolved(UnknownKind.Metaclass, metaclassName))

                            metaclassResult
                            |> Result.bind (fun (metaclassId, storeWithMetaclass) ->
                                let (newClassId, storeWithClass) =
                                    Store.addTensor
                                        (encodeClass
                                            classDefinition.Name
                                            metaclassId
                                            classDefinition.Combination
                                            classDefinition.Superclasses
                                            classDefinition.Slots)
                                        storeWithMetaclass

                                let newDefinition =
                                    { classDefinition with
                                        Address = newClassId
                                        Metaclass = metaclassId
                                    }

                                match classPrecedence newDefinition storeWithClass with
                                | Error error -> Error error
                                | Ok _ ->
                                    let (newRoot, storeWithRoot) =
                                        addClass classDefinition.Name newClassId state.ClassTable storeWithClass

                                    Ok
                                        { state with
                                            Store = storeWithRoot
                                            ClassTable = Some newRoot
                                            Stack = Stack.push newClassId remainingStack
                                        })))

/// `value cell-class` answers the cell query (D49): instance to its class,
/// class to its metaclass, metaclass to the root metaclass, and the root to
/// itself. The answer is always a name text.
let cellClassWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("cell-class", 1, Stack.depth state.Stack))
    | Some(valueId, remainingStack) ->
        let pushName (nameId: EntryIdentity) : Result<EvaluationState, EvaluationError> =
            Ok
                { state with
                    Stack = Stack.push nameId remainingStack
                }

        let metaclassNameOfClass (classDefinition: ClassDefinition) : Result<EvaluationState, EvaluationError> =
            readMetaclassAt classDefinition.Metaclass state
            |> Result.bind (fun metaclass -> pushName metaclass.Name)

        let rootNameOfMetaclass (_metaclass: MetaclassDefinition) : Result<EvaluationState, EvaluationError> =
            let (standardNameId, storeWithName) =
                Store.addText standardMetaclassName state.Store

            let (_standardId, storeWithStandard) = ensureStandardMetaclass storeWithName

            Ok
                { state with
                    Store = storeWithStandard
                    Stack = Stack.push standardNameId remainingStack
                }

        match Store.lookupTensor valueId state.Store with
        | Some tensor ->
            match decodeInstance valueId tensor with
            | Some instance ->
                readClassAt instance.Class state
                |> Result.bind (fun classDefinition -> pushName classDefinition.Name)
            | None ->
                match decodeClass valueId tensor with
                | Some classDefinition -> metaclassNameOfClass classDefinition
                | None ->
                    match decodeMetaclass valueId tensor with
                    | Some metaclass -> rootNameOfMetaclass metaclass
                    | None ->
                        match Store.readTextAt valueId state.Store with
                        | Some name ->
                            match findClass valueId state.ClassTable state.Store with
                            | Some classId -> readClassAt classId state |> Result.bind metaclassNameOfClass
                            | None ->
                                match findMetaclassByName valueId state.Store with
                                | Some metaclass -> rootNameOfMetaclass metaclass
                                | None -> Error(Unresolved(UnknownKind.Class, name))
                        | None -> Error(TypeMismatch(ErrorAddress (EntryIdentity.value valueId), ExpectedCell))
        | None -> Error(UnknownIndex(EntryIdentity.value valueId))

let private classByName (nameId: EntryIdentity) (state: EvaluationState) : Result<EntryIdentity, EvaluationError> =
    match findClass nameId state.ClassTable state.Store with
    | Some classId -> Ok classId
    | None -> Error(Unresolved(UnknownKind.Class, (nameText nameId state.Store)))

let private pushBool (remaining: EntryIdentity list) (value: bool) (state: EvaluationState) : EvaluationState =
    let (identity, store) =
        Store.addTensor (Tensor.scalar (if value then 1.0 else 0.0)) state.Store

    { state with
        Store = store
        Stack = Stack.push identity remaining
    }

/// `"ClassName" class-precedence` pushes the class names in C3 order, most
/// specific first.
let classPrecedenceWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("class-precedence", 1, Stack.depth state.Stack))
    | Some(nameId, remainingStack) ->
        match Store.readTextAt nameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "class-precedence", ExpectedText))
        | Some _ ->
            classByName nameId state
            |> Result.bind (fun classId ->
                readClassAt classId state
                |> Result.bind (fun classDefinition ->
                    classPrecedence classDefinition state.Store
                    |> Result.bind (fun precedence ->
                        let names =
                            precedence
                            |> List.map (fun entryId ->
                                match
                                    Store.lookupTensor entryId state.Store |> Option.bind (decodeClass entryId)
                                with
                                | Some classDefinition -> double (EntryIdentity.value classDefinition.Name)
                                | None -> double (EntryIdentity.value entryId))

                        let (vectorId, store) = Store.addTensor (Tensor.vector names) state.Store

                        Ok
                            { state with
                                Store = store
                                Stack = Stack.push vectorId remainingStack
                            })))

/// `"Child" "Parent" subclass-of` pushes 1 when Child is Parent or a subclass.
let subclassOfWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("subclass-of", 2, Stack.depth state.Stack))
    | Some(superclassNameId, afterSuperclass) ->
        match Store.readTextAt superclassNameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "subclass-of", ExpectedText))
        | Some _ ->
            match Stack.popIndex afterSuperclass with
            | None -> Error(StackUnderflow("subclass-of", 2, Stack.depth state.Stack))
            | Some(subclassNameId, remainingStack) ->
                match Store.readTextAt subclassNameId state.Store with
                | None -> Error(TypeMismatch(ErrorWord "subclass-of", ExpectedText))
                | Some _ ->
                    classByName subclassNameId state
                    |> Result.bind (fun subclassId ->
                        classByName superclassNameId state
                        |> Result.bind (fun superclassId ->
                            isSubclass subclassId superclassId state.Store
                            |> Result.map (fun answer -> pushBool remainingStack answer state)))

/// `instance "ClassName" instance-of` pushes 1 when the value is an instance of
/// the class or one of its subclasses.
let instanceOfWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("instance-of", 2, Stack.depth state.Stack))
    | Some(classNameId, afterClass) ->
        match Store.readTextAt classNameId state.Store with
        | None -> Error(TypeMismatch(ErrorWord "instance-of", ExpectedText))
        | Some _ ->
            match Stack.popIndex afterClass with
            | None -> Error(StackUnderflow("instance-of", 2, Stack.depth state.Stack))
            | Some(instanceId, remainingStack) ->
                classByName classNameId state
                |> Result.bind (fun classId ->
                    isInstanceOf instanceId classId state.Store
                    |> Result.map (fun answer -> pushBool remainingStack answer state))
