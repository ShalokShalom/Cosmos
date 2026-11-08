module EvaluatorCellSystem

// ============================================================
// The cell system's encoding layer (D44).
//
// This slice owns the tags, the codecs, and the root helpers. It is data only:
// no allocation, no slot access, no dispatch, and no method combination. The
// image is the authority; every decoded record is a transient projection.
//
// The surface is Scale Basic English. The tensor schemas below are internal
// storage and are never user syntax.
// ============================================================

open Types
open EvaluatorCommon

/// How a slot obtains its initial value. The code is a schema value, not a
/// surface word.
type SlotInitialization =
    | Uninitialized
    | Defaulted
    | Required

/// The combination policy a generic function names.
type MethodCombinationPolicy =
    | Standard
    | Append
    | List
    | LastResult

type ClassDefinition =
    {
        Address: EntryIdentity
        Name: EntryIdentity
        Metaclass: EntryIdentity
        Combination: EntryIdentity option
        Superclasses: EntryIdentity list
        Slots: EntryIdentity list
    }

type SlotDefinition =
    {
        Address: EntryIdentity
        Name: EntryIdentity
        ValueSet: EntryIdentity
        Default: EntryIdentity option
        Initialization: SlotInitialization
    }

type InstanceIdentity =
    {
        Address: EntryIdentity
        Class: EntryIdentity
        Number: int
    }

type InstanceState =
    {
        Address: EntryIdentity
        Instance: EntryIdentity
        Bindings: (EntryIdentity * EntryIdentity) list
    }

type Closure =
    {
        Address: EntryIdentity
        Quotation: EntryIdentity
        Environment: EntryIdentity
    }

type EnvironmentFrame =
    {
        Address: EntryIdentity
        Parent: EntryIdentity option
        Bindings: (EntryIdentity * EntryIdentity) list
    }

type MethodCombination =
    {
        Address: EntryIdentity
        Name: EntryIdentity
        Policy: MethodCombinationPolicy
        Qualifiers: MethodQualifier list
    }

type QualifiedMethod =
    {
        Address: EntryIdentity
        Qualifier: MethodQualifier
        Arity: int
        Body: EntryIdentity
        Specializers: EntryIdentity list
    }

type MetaclassDefinition =
    {
        Address: EntryIdentity
        Name: EntryIdentity
        Superclasses: EntryIdentity list
        Slots: EntryIdentity list
    }

type ConditionKind =
    | ErrorCondition
    | WarningCondition

type ConditionDefinition =
    {
        Address: EntryIdentity
        Kind: ConditionKind
        Name: EntryIdentity
        Message: EntryIdentity
    }

type RestartDefinition =
    {
        Address: EntryIdentity
        Name: EntryIdentity
        Handler: EntryIdentity
    }

let private id (identity: EntryIdentity) : RealNumber = double (EntryIdentity.value identity)

let private optionalId (identity: EntryIdentity option) : RealNumber =
    match identity with
    | Some value -> id value
    | None -> -1.0

/// A finite, non-negative integer. Entry identities and counts are small; an
/// out-of-range value is a malformed entry, not a crash.
let private finiteInt (value: RealNumber) : int option =
    if
        System.Double.IsFinite value
        && value >= 0.0
        && value <= float System.Int32.MaxValue
        && value = System.Math.Floor value
    then
        Some(int value)
    else
        None

let private idAt (tensor: Tensor) (index: int) : EntryIdentity option =
    finiteInt (Tensor.item tensor index) |> Option.map EntryIdentity.ofInt

/// `Some None` is an absent optional id; `None` is a malformed value.
let private optionalIdAt (tensor: Tensor) (index: int) : EntryIdentity option option =
    let value = Tensor.item tensor index

    if value < 0.0 then
        Some None
    else
        idAt tensor index |> Option.map Some

let private countAt (tensor: Tensor) (index: int) : int option =
    match finiteInt (Tensor.item tensor index) with
    | Some count when count <= Tensor.size tensor -> Some count
    | _ -> None

let private hasTag (tag: RealNumber) (tensor: Tensor) : bool =
    Tensor.rank tensor = 1 && Tensor.size tensor > 0 && Tensor.item tensor 0 = tag

let private idsAt (tensor: Tensor) (start: int) (count: int) : EntryIdentity list option =
    let rec collect (index: int) (remaining: int) (accumulated: EntryIdentity list) : EntryIdentity list option =
        if remaining = 0 then
            Some(List.rev accumulated)
        else
            match idAt tensor index with
            | Some identity -> collect (index + 1) (remaining - 1) (identity :: accumulated)
            | None -> None

    collect start count []

let private pairsAt (tensor: Tensor) (start: int) (count: int) : (EntryIdentity * EntryIdentity) list option =
    let rec collect
        (index: int)
        (remaining: int)
        (accumulated: (EntryIdentity * EntryIdentity) list)
        : (EntryIdentity * EntryIdentity) list option =
        if remaining = 0 then
            Some(List.rev accumulated)
        else
            match idAt tensor index, idAt tensor (index + 1) with
            | Some slot, Some value -> collect (index + 2) (remaining - 1) ((slot, value) :: accumulated)
            | _ -> None

    collect start count []

let private initializationCode (initialization: SlotInitialization) : RealNumber =
    match initialization with
    | Uninitialized -> 0.0
    | Defaulted -> 1.0
    | Required -> 2.0

let private initializationOfCode (value: RealNumber) : SlotInitialization option =
    match int value with
    | 0 -> Some Uninitialized
    | 1 -> Some Defaulted
    | 2 -> Some Required
    | _ -> None

let private qualifierCode (qualifier: MethodQualifier) : RealNumber =
    match qualifier with
    | Primary -> 0.0
    | Before -> 1.0
    | After -> 2.0
    | Around -> 3.0

let private qualifierOfCode (value: RealNumber) : MethodQualifier option =
    match int value with
    | 0 -> Some Primary
    | 1 -> Some Before
    | 2 -> Some After
    | 3 -> Some Around
    | _ -> None

let private policyCode (policy: MethodCombinationPolicy) : RealNumber =
    match policy with
    | Standard -> 0.0
    | Append -> 1.0
    | List -> 2.0
    | LastResult -> 3.0

let private policyOfCode (value: RealNumber) : MethodCombinationPolicy option =
    match int value with
    | 0 -> Some Standard
    | 1 -> Some Append
    | 2 -> Some List
    | 3 -> Some LastResult
    | _ -> None

let encodeClass
    (nameId: EntryIdentity)
    (metaclassId: EntryIdentity)
    (combinationId: EntryIdentity option)
    (superclassIds: EntryIdentity list)
    (slotIds: EntryIdentity list)
    : Tensor =
    Tensor.vector (
        [
            ClassDefinitionTag
            id nameId
            id metaclassId
            optionalId combinationId
            double (List.length superclassIds)
        ]
        @ (superclassIds |> List.map id)
        @ [ double (List.length slotIds) ]
        @ (slotIds |> List.map id)
    )

let decodeClass (address: EntryIdentity) (tensor: Tensor) : ClassDefinition option =
    if not (hasTag ClassDefinitionTag tensor) || Tensor.size tensor < 6 then
        None
    else
        match idAt tensor 1, idAt tensor 2, optionalIdAt tensor 3, countAt tensor 4 with
        | Some name, Some metaclass, Some combination, Some superclassCount ->
            let slotCountIndex = 5 + superclassCount

            if slotCountIndex >= Tensor.size tensor then
                None
            else
                match countAt tensor slotCountIndex with
                | Some slotCount when Tensor.size tensor = slotCountIndex + 1 + slotCount ->
                    match idsAt tensor 5 superclassCount, idsAt tensor (slotCountIndex + 1) slotCount with
                    | Some superclasses, Some slots ->
                        Some
                            {
                                Address = address
                                Name = name
                                Metaclass = metaclass
                                Combination = combination
                                Superclasses = superclasses
                                Slots = slots
                            }
                    | _ -> None
                | _ -> None
        | _ -> None

let encodeSlot
    (nameId: EntryIdentity)
    (valueSetId: EntryIdentity)
    (defaultId: EntryIdentity option)
    (initialization: SlotInitialization)
    : Tensor =
    Tensor.vector
        [
            SlotDefinitionTag
            id nameId
            id valueSetId
            optionalId defaultId
            initializationCode initialization
        ]

let decodeSlot (address: EntryIdentity) (tensor: Tensor) : SlotDefinition option =
    if not (hasTag SlotDefinitionTag tensor) || Tensor.size tensor <> 5 then
        None
    else
        match idAt tensor 1, idAt tensor 2, optionalIdAt tensor 3, initializationOfCode (Tensor.item tensor 4) with
        | Some name, Some valueSet, Some defaultId, Some initialization ->
            Some
                {
                    Address = address
                    Name = name
                    ValueSet = valueSet
                    Default = defaultId
                    Initialization = initialization
                }
        | _ -> None

let encodeInstance (classId: EntryIdentity) (number: int) : Tensor =
    Tensor.vector [ InstanceTag; id classId; double number ]

let decodeInstance (address: EntryIdentity) (tensor: Tensor) : InstanceIdentity option =
    if not (hasTag InstanceTag tensor) || Tensor.size tensor <> 3 then
        None
    else
        match idAt tensor 1, finiteInt (Tensor.item tensor 2) with
        | Some classId, Some number ->
            Some
                {
                    Address = address
                    Class = classId
                    Number = number
                }
        | _ -> None

let encodeInstanceState (instanceId: EntryIdentity) (bindings: (EntryIdentity * EntryIdentity) list) : Tensor =
    Tensor.vector (
        [ InstanceStateTag; id instanceId; double (List.length bindings) ]
        @ (bindings |> List.collect (fun (slot, value) -> [ id slot; id value ]))
    )

let decodeInstanceState (address: EntryIdentity) (tensor: Tensor) : InstanceState option =
    if not (hasTag InstanceStateTag tensor) || Tensor.size tensor < 3 then
        None
    else
        match idAt tensor 1, countAt tensor 2 with
        | Some instance, Some count when Tensor.size tensor = 3 + 2 * count ->
            match pairsAt tensor 3 count with
            | Some bindings ->
                Some
                    {
                        Address = address
                        Instance = instance
                        Bindings = bindings
                    }
            | None -> None
        | _ -> None

let encodeClosure (quotationId: EntryIdentity) (environmentId: EntryIdentity) : Tensor =
    Tensor.vector [ ClosureTag; id quotationId; id environmentId ]

let decodeClosure (address: EntryIdentity) (tensor: Tensor) : Closure option =
    if not (hasTag ClosureTag tensor) || Tensor.size tensor <> 3 then
        None
    else
        match idAt tensor 1, idAt tensor 2 with
        | Some quotation, Some environment ->
            Some
                {
                    Address = address
                    Quotation = quotation
                    Environment = environment
                }
        | _ -> None

let encodeEnvironment (parentId: EntryIdentity option) (bindings: (EntryIdentity * EntryIdentity) list) : Tensor =
    Tensor.vector (
        [ EnvironmentFrameTag; optionalId parentId; double (List.length bindings) ]
        @ (bindings |> List.collect (fun (name, value) -> [ id name; id value ]))
    )

let decodeEnvironment (address: EntryIdentity) (tensor: Tensor) : EnvironmentFrame option =
    if not (hasTag EnvironmentFrameTag tensor) || Tensor.size tensor < 3 then
        None
    else
        match optionalIdAt tensor 1, countAt tensor 2 with
        | Some parent, Some count when Tensor.size tensor = 3 + 2 * count ->
            match pairsAt tensor 3 count with
            | Some bindings ->
                Some
                    {
                        Address = address
                        Parent = parent
                        Bindings = bindings
                    }
            | None -> None
        | _ -> None

let encodeMethodCombination
    (nameId: EntryIdentity)
    (policy: MethodCombinationPolicy)
    (qualifiers: MethodQualifier list)
    : Tensor =
    Tensor.vector (
        [
            MethodCombinationTag
            id nameId
            policyCode policy
            double (List.length qualifiers)
        ]
        @ (qualifiers |> List.map qualifierCode)
    )

let decodeMethodCombination (address: EntryIdentity) (tensor: Tensor) : MethodCombination option =
    if not (hasTag MethodCombinationTag tensor) || Tensor.size tensor < 4 then
        None
    else
        match idAt tensor 1, policyOfCode (Tensor.item tensor 2), countAt tensor 3 with
        | Some name, Some policy, Some count when Tensor.size tensor = 4 + count ->
            let rec collect
                (index: int)
                (remaining: int)
                (accumulated: MethodQualifier list)
                : MethodQualifier list option =
                if remaining = 0 then
                    Some(List.rev accumulated)
                else
                    match qualifierOfCode (Tensor.item tensor index) with
                    | Some qualifier -> collect (index + 1) (remaining - 1) (qualifier :: accumulated)
                    | None -> None

            match collect 4 count [] with
            | Some qualifiers ->
                Some
                    {
                        Address = address
                        Name = name
                        Policy = policy
                        Qualifiers = qualifiers
                    }
            | None -> None
        | _ -> None

let encodeQualifiedMethod
    (qualifier: MethodQualifier)
    (arity: int)
    (bodyId: EntryIdentity)
    (specializerIds: EntryIdentity list)
    : Tensor =
    Tensor.vector (
        [
            QualifiedMethodTag
            qualifierCode qualifier
            double arity
            id bodyId
            double (List.length specializerIds)
        ]
        @ (specializerIds |> List.map id)
    )

let decodeQualifiedMethod (address: EntryIdentity) (tensor: Tensor) : QualifiedMethod option =
    if not (hasTag QualifiedMethodTag tensor) || Tensor.size tensor < 5 then
        None
    else
        match
            qualifierOfCode (Tensor.item tensor 1), finiteInt (Tensor.item tensor 2), idAt tensor 3, countAt tensor 4
        with
        | Some qualifier, Some arity, Some body, Some count when Tensor.size tensor = 5 + count ->
            match idsAt tensor 5 count with
            | Some specializers ->
                Some
                    {
                        Address = address
                        Qualifier = qualifier
                        Arity = arity
                        Body = body
                        Specializers = specializers
                    }
            | None -> None
        | _ -> None

let private conditionKindCode (kind: ConditionKind) : RealNumber =
    match kind with
    | ErrorCondition -> 0.0
    | WarningCondition -> 1.0

let private conditionKindOfCode (value: RealNumber) : ConditionKind option =
    match int value with
    | 0 -> Some ErrorCondition
    | 1 -> Some WarningCondition
    | _ -> None

let encodeCondition (kind: ConditionKind) (nameId: EntryIdentity) (messageId: EntryIdentity) : Tensor =
    Tensor.vector [ ConditionTag; conditionKindCode kind; id nameId; id messageId ]

let decodeCondition (address: EntryIdentity) (tensor: Tensor) : ConditionDefinition option =
    if not (hasTag ConditionTag tensor) || Tensor.size tensor <> 4 then
        None
    else
        match conditionKindOfCode (Tensor.item tensor 1), idAt tensor 2, idAt tensor 3 with
        | Some kind, Some name, Some message ->
            Some
                {
                    Address = address
                    Kind = kind
                    Name = name
                    Message = message
                }
        | _ -> None

let encodeRestart (nameId: EntryIdentity) (handlerId: EntryIdentity) : Tensor =
    Tensor.vector [ RestartTag; id nameId; id handlerId ]

let decodeRestart (address: EntryIdentity) (tensor: Tensor) : RestartDefinition option =
    if not (hasTag RestartTag tensor) || Tensor.size tensor <> 3 then
        None
    else
        match idAt tensor 1, idAt tensor 2 with
        | Some name, Some handler ->
            Some
                {
                    Address = address
                    Name = name
                    Handler = handler
                }
        | _ -> None

let encodeMetaclass (nameId: EntryIdentity) (superclassIds: EntryIdentity list) (slotIds: EntryIdentity list) : Tensor =
    Tensor.vector (
        [ MetaclassTag; id nameId; double (List.length superclassIds) ]
        @ (superclassIds |> List.map id)
        @ [ double (List.length slotIds) ]
        @ (slotIds |> List.map id)
    )

let decodeMetaclass (address: EntryIdentity) (tensor: Tensor) : MetaclassDefinition option =
    if not (hasTag MetaclassTag tensor) || Tensor.size tensor < 4 then
        None
    else
        match idAt tensor 1, countAt tensor 2 with
        | Some name, Some superclassCount ->
            let slotCountIndex = 3 + superclassCount

            if slotCountIndex >= Tensor.size tensor then
                None
            else
                match countAt tensor slotCountIndex with
                | Some slotCount when Tensor.size tensor = slotCountIndex + 1 + slotCount ->
                    match idsAt tensor 3 superclassCount, idsAt tensor (slotCountIndex + 1) slotCount with
                    | Some superclasses, Some slots ->
                        Some
                            {
                                Address = address
                                Name = name
                                Superclasses = superclasses
                                Slots = slots
                            }
                    | _ -> None
                | _ -> None
        | _ -> None

/// A depth limit turns a malformed cyclic frame chain into a miss, never an
/// infinite walk. Frames are image data; a cycle is possible only if a caller
/// builds one by hand.
let frameLookupLimit = 1024

/// An empty lexical frame.
let emptyFrame () : Tensor = encodeEnvironment None []

/// Makes sure a closure has an environment. A closure created with no current
/// frame captures an empty frame, not a host sentinel.
let ensureFrame (frame: EntryIdentity option) (store: Store) : EntryIdentity * Store =
    match frame with
    | Some identity -> (identity, store)
    | None -> Store.addTensor (emptyFrame ()) store

/// A new child frame with one binding; the parent is the current frame.
let addFrameBinding
    (nameId: EntryIdentity)
    (valueId: EntryIdentity)
    (parent: EntryIdentity option)
    (store: Store)
    : EntryIdentity * Store =
    Store.addTensor (encodeEnvironment parent [ (nameId, valueId) ]) store

/// Looks a name up in the frame chain. The caller falls back to the global
/// binding table when this returns `None`.
let lookupInFrame (nameId: EntryIdentity) (frame: EntryIdentity option) (store: Store) : EntryIdentity option =
    let rec go (remaining: int) (current: EntryIdentity) : EntryIdentity option =
        if remaining = 0 then
            None
        else
            match Store.lookupTensor current store |> Option.bind (decodeEnvironment current) with
            | None -> None
            | Some environment ->
                match environment.Bindings |> List.tryFind (fun (name, _) -> name = nameId) with
                | Some(_, valueId) -> Some valueId
                | None ->
                    match environment.Parent with
                    | Some parent -> go (remaining - 1) parent
                    | None -> None

    match frame with
    | Some current -> go frameLookupLimit current
    | None -> None

/// The environment a closure carries.
let closureEnvironment (closureId: EntryIdentity) (store: Store) : EntryIdentity option =
    Store.lookupTensor closureId store
    |> Option.bind (decodeClosure closureId)
    |> Option.map (fun closure -> closure.Environment)

/// The latest slot definition with this name. Slots have no root, so this
/// scans the append-only image; the latest declaration wins.
let findSlotByName (nameId: EntryIdentity) (store: Store) : SlotDefinition option =
    Seq.init store.NextIndex (fun index -> EntryIdentity.ofInt index)
    |> Seq.choose (fun identity -> Store.lookupTensor identity store |> Option.bind (decodeSlot identity))
    |> Seq.filter (fun slot -> slot.Name = nameId)
    |> Seq.tryLast

/// The latest metaclass definition with this name.
let findMetaclassByName (nameId: EntryIdentity) (store: Store) : MetaclassDefinition option =
    Seq.init store.NextIndex (fun index -> EntryIdentity.ofInt index)
    |> Seq.choose (fun identity -> Store.lookupTensor identity store |> Option.bind (decodeMetaclass identity))
    |> Seq.filter (fun metaclass -> metaclass.Name = nameId)
    |> Seq.tryLast

/// The class root is an association vector [nameId; classId; …], latest-write-wins.
let addClass
    (nameId: EntryIdentity)
    (classId: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity * Store =
    addToEnvironment nameId classId root store

let findClass (nameId: EntryIdentity) (root: EntryIdentity option) (store: Store) : EntryIdentity option =
    findPair nameId root store

/// The instance-state root is an association vector [instanceId; stateId; …].
let addInstanceState
    (instanceId: EntryIdentity)
    (stateId: EntryIdentity)
    (root: EntryIdentity option)
    (store: Store)
    : EntryIdentity * Store =
    addToEnvironment instanceId stateId root store

let findInstanceState (instanceId: EntryIdentity) (root: EntryIdentity option) (store: Store) : EntryIdentity option =
    findPair instanceId root store
