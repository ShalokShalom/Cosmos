module EvaluatorDispatch

// ============================================================
// Multiple dispatch: store-resident generic functions and methods (W10, D19).
//
// A generic function is a rank-1 tagged tensor
//
//   [GenericTag; name; arity; policy; method...]
//
// and a method is
//
//   [MethodTag; arity; body; specializer...]
//
// Both live in the store. GenericFunctionTable is an association vector of
// generic-function entries, latest-write-wins by name, so adding a method is a
// new generic-function entry, never a mutation of the old one.
//
// Dispatch is type-first. A typed argument carries its sort name (D15); an
// untyped argument infers the unique most-specific declared sort that contains
// it. When every argument has a known sort, an exact specializer tuple is the
// static fast path: the prepared method table is searched by sort name and the
// sort extensions are not touched. Only when the tuple has no exact method, or
// when a type cannot be inferred, does the dynamic path run: applicability is
// membership (untyped) or extension inclusion (typed), and specificity is
// extension inclusion. Incomparable or extension-equal maximal methods are
// AmbiguousDispatch, not a silent pick.
//
// Words: generic, method. The token fallback also checks this registry before
// the vocabulary, so an store-resident method is callable without an F# case.
// ============================================================

open Types
open EvaluatorCommon
open EvaluatorEnvironment

[<Literal>]
let GenericTag = -4.0

[<Literal>]
let MethodTag = -5.0

/// How a generic function resolves more than one applicable method.
/// `MostSpecific` keeps the maximal methods and refuses a tie; `First` takes
/// definition order; `Unique` requires exactly one applicable method.
type DispatchPolicy =
    | MostSpecific
    | First
    | Unique

/// A generic-function projection. The store entry is the authority.
type GenericFunction =
    {
        Name: EntryIdentity
        Arity: int
        Policy: DispatchPolicy
        Methods: Method list
    }

/// The dispatch-time type of one argument. `UnknownType` is the explicit
/// fallback: no unique declared sort was inferred, so applicability must use
/// membership.
type ArgumentType =
    | KnownType of EvaluatorValueSets.ValueSet
    | ClassType of EntryIdentity
    | UnknownType

/// The classification verdict for a value (D32): the unique most-specific
/// declared sort, no unique class, or a membership question the predicates
/// could not decide.
type ValueVerdict =
    | ValueOf of EvaluatorValueSets.ValueSet
    | ValueUnknown
    | ValueAmbiguous of EvaluatorValueSets.ValueSet list
    | ValueUndecided of reason: string

/// A specializer after resolution: a value set or a cell-system class.
type PreparedSpecializer =
    | ValueSetSpecializer of EvaluatorValueSets.ValueSet
    | ClassSpecializer of EvaluatorCellSystem.ClassDefinition

/// A method with its specializers read from the current declarations and
/// class definitions.
type PreparedMethod =
    {
        Method: Method
        Specializers: PreparedSpecializer list
    }

/// A dispatch verdict. No method and ambiguity are distinct outcomes; an
/// undecidable membership is a named error (`DispatchUndecided`).
type DispatchSelection =
    | Selected of Method
    | SelectedCombination of arounds: Method list * befores: Method list * primaries: Method list * afters: Method list
    | NoMatch
    | Ambiguous of Method list

type private CombinationOrderError =
    | CombinationAmbiguous of Method list
    | CombinationFailed of EvaluationError

let private policyCode (policy: DispatchPolicy) : int =
    match policy with
    | MostSpecific -> 0
    | First -> 1
    | Unique -> 2

let private policyOfCode (code: int) : DispatchPolicy option =
    match code with
    | 0 -> Some MostSpecific
    | 1 -> Some First
    | 2 -> Some Unique
    | _ -> None

// ---- Encoding --------------------------------------------------------------

let private encodeMethod
    (qualifier: MethodQualifier)
    (arity: int)
    (bodyId: EntryIdentity)
    (specializers: EntryIdentity list)
    : Tensor =
    EvaluatorCellSystem.encodeQualifiedMethod qualifier arity bodyId specializers

let private encodeGeneric
    (nameId: EntryIdentity)
    (arity: int)
    (policy: DispatchPolicy)
    (methods: Method list)
    : Tensor =
    Tensor.vector (
        GenericTag
        :: double (EntryIdentity.value nameId)
        :: double arity
        :: double (policyCode policy)
        :: (methods |> List.map (fun method -> double (EntryIdentity.value method.Address)))
    )

let private decodeMethod (entryId: EntryIdentity) (store: Store) : Method option =
    match Store.lookupTensor entryId store with
    | Some tensor ->
        match EvaluatorCellSystem.decodeQualifiedMethod entryId tensor with
        | Some qualified ->
            Some
                {
                    Address = entryId
                    Qualifier = qualified.Qualifier
                    Arity = qualified.Arity
                    Body = qualified.Body
                    Specializers = qualified.Specializers
                }
        | None when
            Tensor.rank tensor = 1
            && Tensor.size tensor >= 3
            && Tensor.item tensor 0 = MethodTag
            ->
            // Entries written before D48 carry no qualifier; they are primary.
            let rawArity = Tensor.item tensor 1

            if rawArity < 0.0 || rawArity <> System.Math.Floor rawArity then
                None
            else
                let arity = int rawArity
                let bodyId = EntryIdentity.ofInt (int (Tensor.item tensor 2))

                let specializers =
                    tensor.Data
                    |> Array.skip 3
                    |> Array.toList
                    |> List.map (fun value -> EntryIdentity.ofInt (int value))

                if List.length specializers <> arity then
                    None
                else
                    Some
                        {
                            Address = entryId
                            Qualifier = Primary
                            Arity = arity
                            Body = bodyId
                            Specializers = specializers
                        }
        | None -> None
    | _ -> None

let private decodeGeneric (entryId: EntryIdentity) (store: Store) : GenericFunction option =
    match Store.lookupTensor entryId store with
    | Some tensor when
        Tensor.rank tensor = 1
        && Tensor.size tensor >= 4
        && Tensor.item tensor 0 = GenericTag
        ->
        let nameId = EntryIdentity.ofInt (int (Tensor.item tensor 1))
        let rawArity = Tensor.item tensor 2
        let rawPolicy = Tensor.item tensor 3

        let validArity = rawArity >= 0.0 && rawArity = System.Math.Floor rawArity

        let validPolicy = rawPolicy = System.Math.Floor rawPolicy

        if not validArity || not validPolicy then
            None
        else
            match policyOfCode (int rawPolicy) with
            | None -> None
            | Some policy ->
                let methodIds =
                    tensor.Data
                    |> Array.skip 4
                    |> Array.toList
                    |> List.map (fun value -> EntryIdentity.ofInt (int value))

                let methods = methodIds |> List.map (fun methodId -> decodeMethod methodId store)

                if List.exists Option.isNone methods then
                    None
                else
                    let decoded = methods |> List.choose (fun method -> method)

                    if decoded |> List.exists (fun method -> method.Arity <> int rawArity) then
                        None
                    else
                        Some
                            {
                                Name = nameId
                                Arity = int rawArity
                                Policy = policy
                                Methods = decoded
                            }
    | _ -> None

/// Latest-write-wins scan of the generic-function root.
let findByName (nameId: EntryIdentity) (root: EntryIdentity option) (store: Store) : GenericFunction option =
    EvaluatorValueSets.tableEntries root store
    |> List.fold
        (fun latest entryId ->
            match decodeGeneric entryId store with
            | Some generic when generic.Name = nameId -> Some generic
            | _ -> latest)
        None

// ---- Set-operation protocol (D17 stage 3, D39) -----------------------------

/// The public set words are generic functions; each default method calls the
/// kernel primitive of the same operation. Adding or replacing a method is an
/// image change, not an F# change.
let private setOperations: (string * int * string) list =
    [
        ("member", 1, "set-member")
        ("subset", 2, "set-subset")
        ("union", 2, "set-union")
        ("intersect", 2, "set-intersect")
        ("except", 2, "set-except")
        ("complement", 1, "set-complement")
    ]

let private setTypeName = "set"

/// The synthetic type of a set entry. The declaration is intentionally empty:
/// a set is classified by being a set, not by membership in another extension.
/// This keeps the constructor independent of every operation's method table.
let private setType (valueId: EntryIdentity) (state: EvaluationState) : EvaluatorValueSets.ValueSet option =
    match EvaluatorValueSets.readSet valueId state.SetTable state.Store with
    | None -> None
    | Some _ ->
        Map.tryFind setTypeName state.Store.TextCache
        |> Option.bind (fun nameId -> EvaluatorValueSets.findByName nameId state.DeclarationTable state.Store)

/// Seeds the six set-operation generics, their default methods, and the empty
/// `set` declaration. The roots are execution state (D4); the entries are image
/// entries (D16/D19). `initial` calls this once per fresh state.
let seedSetProtocolMethods (store: Store) : Store * EntryIdentity option * EntryIdentity option =
    let (setNameId, store1) = Store.addText setTypeName store

    let (_, declarationRoot, store2) =
        EvaluatorValueSets.writeSet
            {
                Name = Some setNameId
                Members = []
                Predicate = None
            }
            None
            store1

    let mutable currentStore = store2
    let mutable genericRoot = None

    for (name, arity, body) in setOperations do
        let (nameId, store3) = Store.addText name currentStore

        let (bodyId, store4) =
            match EvaluatorProgram.ofText body with
            | Error error -> invalidOp (EvaluatorProgram.describeError error)
            | Ok bodyTerms ->
                match EvaluatorCode.encode bodyTerms store3 with
                | Ok value -> value
                | Error error -> invalidOp (EvaluatorCode.describeError error)

        let specializers = List.replicate arity setNameId

        let (methodId, store5) =
            Store.addTensor (encodeMethod Primary arity bodyId specializers) store4

        let method =
            {
                Address = methodId
                Qualifier = Primary
                Arity = arity
                Body = bodyId
                Specializers = specializers
            }

        let (genericId, store6) =
            Store.addTensor (encodeGeneric nameId arity MostSpecific [ method ]) store5

        let (newRoot, store7) =
            EvaluatorValueSets.appendToTable genericId genericRoot store6

        currentStore <- store7
        genericRoot <- Some newRoot

    (currentStore, genericRoot, Some declarationRoot)

// ---- Type inference --------------------------------------------------------

/// The current named declarations, latest-write-wins by name. Table order is
/// append order, so a later declaration replaces the earlier one in the map.
let private declarationsByName (state: EvaluationState) : Map<EntryIdentity, EvaluatorValueSets.ValueSet> =
    EvaluatorValueSets.tableEntries state.DeclarationTable state.Store
    |> List.choose (fun entryId -> Store.lookupTensor entryId state.Store |> Option.bind EvaluatorValueSets.decode)
    |> List.fold
        (fun declarations sort ->
            match sort.Name with
            | Some nameId -> Map.add nameId sort declarations
            | None -> declarations)
        Map.empty

/// Every member of `left` (after its own predicate) is a member of `right`,
/// using the one membership verdict. An undecidable predicate stops the
/// comparison: the caller reports it, never a silent false.
let private subsetVerdict
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (left: EvaluatorValueSets.ValueSet)
    (right: EvaluatorValueSets.ValueSet)
    (state: EvaluationState)
    : Result<bool, string> =
    let rec check (remaining: EntryIdentity list) : Result<bool, string> =
        match remaining with
        | [] -> Ok true
        | identity :: rest ->
            match EvaluatorValueSets.memberOf executeWord identity left state with
            | EvaluatorValueSets.NotMember -> check rest
            | EvaluatorValueSets.Undecided reason -> Error reason
            | EvaluatorValueSets.Member ->
                match EvaluatorValueSets.memberOf executeWord identity right state with
                | EvaluatorValueSets.Member -> check rest
                | EvaluatorValueSets.NotMember -> Ok false
                | EvaluatorValueSets.Undecided reason -> Error reason

    check left.Members

/// The unique most-specific declared sort containing the value. The verdict is
/// explicit: `ValueUnknown` when no unique class exists, `ValueAmbiguous` when
/// incomparable or extension-equal minima remain, and `ValueUndecided` when a
/// predicate could not decide.
let private inferType
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (declarations: Map<EntryIdentity, EvaluatorValueSets.ValueSet>)
    (valueId: EntryIdentity)
    (state: EvaluationState)
    : ValueVerdict =
    let rec collect
        (remaining: EvaluatorValueSets.ValueSet list)
        (candidates: EvaluatorValueSets.ValueSet list)
        : Result<EvaluatorValueSets.ValueSet list, string> =
        match remaining with
        | [] -> Ok(List.rev candidates)
        | sort :: rest ->
            match EvaluatorValueSets.memberOf executeWord valueId sort state with
            | EvaluatorValueSets.Member -> collect rest (sort :: candidates)
            | EvaluatorValueSets.NotMember -> collect rest candidates
            | EvaluatorValueSets.Undecided reason -> Error reason

    let rec dominatedBy
        (candidate: EvaluatorValueSets.ValueSet)
        (others: EvaluatorValueSets.ValueSet list)
        : Result<bool, string> =
        match others with
        | [] -> Ok false
        | other :: more ->
            if other.Name = candidate.Name then
                dominatedBy candidate more
            else
                match subsetVerdict executeWord other candidate state with
                | Error reason -> Error reason
                | Ok false -> dominatedBy candidate more
                | Ok true ->
                    match subsetVerdict executeWord candidate other state with
                    | Error reason -> Error reason
                    | Ok true -> dominatedBy candidate more
                    | Ok false -> Ok true

    let rec minimal
        (remaining: EvaluatorValueSets.ValueSet list)
        (accumulated: EvaluatorValueSets.ValueSet list)
        : Result<EvaluatorValueSets.ValueSet list, string> =
        match remaining with
        | [] -> Ok(List.rev accumulated)
        | candidate :: rest ->
            match dominatedBy candidate remaining with
            | Error reason -> Error reason
            | Ok true -> minimal rest accumulated
            | Ok false -> minimal rest (candidate :: accumulated)

    match collect (declarations |> Map.toList |> List.map snd) [] with
    | Error reason -> ValueUndecided reason
    | Ok [] -> ValueUnknown
    | Ok containing ->
        match minimal containing [] with
        | Error reason -> ValueUndecided reason
        | Ok [ sort ] -> ValueOf sort
        | Ok [] -> ValueUnknown
        | Ok sorts -> ValueAmbiguous sorts

/// The classification verdict for a value: an explicit `typed` seal wins,
/// otherwise the unique most-specific declared sort is inferred.
let valueVerdictOf
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (valueId: EntryIdentity)
    (state: EvaluationState)
    : ValueVerdict =
    let declarations = declarationsByName state

    match viewUnificationTerm valueId state.Store with
    | Typed(sortName, _) ->
        match Map.tryFind sortName declarations with
        | Some sort -> ValueOf sort
        | None -> ValueUnknown
    | _ -> inferType executeWord declarations valueId state

let private argumentType
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (declarations: Map<EntryIdentity, EvaluatorValueSets.ValueSet>)
    (valueId: EntryIdentity)
    (state: EvaluationState)
    : Result<ArgumentType, string> =
    match
        Store.lookupTensor valueId state.Store
        |> Option.bind (EvaluatorCellSystem.decodeInstance valueId)
    with
    | Some instance -> Ok(ClassType instance.Class)
    | None ->
        match setType valueId state with
        | Some sort -> Ok(KnownType sort)
        | None ->
            match valueVerdictOf executeWord valueId state with
            | ValueOf sort -> Ok(KnownType sort)
            | ValueUnknown
            | ValueAmbiguous _ -> Ok UnknownType
            | ValueUndecided reason -> Error reason

/// The method body sees the underlying value. An explicit type seal is
/// dispatch metadata, not an arithmetic operand, so one wrapper is removed at
/// the call boundary.
let private underlyingValue (valueId: EntryIdentity) (store: Store) : EntryIdentity =
    match viewUnificationTerm valueId store with
    | Typed(_, term) -> term
    | _ -> valueId

// ---- Selection -------------------------------------------------------------

let private readSpecializer
    (specializerId: EntryIdentity)
    (declarations: Map<EntryIdentity, EvaluatorValueSets.ValueSet>)
    (store: Store)
    : Result<EvaluatorValueSets.ValueSet, EvaluationError> =
    match Map.tryFind specializerId declarations with
    | Some sort -> Ok sort
    | None -> Error(Unresolved(UnknownKind.Model, (nameText specializerId store)))

let private readPreparedSpecializer
    (specializerId: EntryIdentity)
    (declarations: Map<EntryIdentity, EvaluatorValueSets.ValueSet>)
    (store: Store)
    : Result<PreparedSpecializer, EvaluationError> =
    match
        Store.lookupTensor specializerId store
        |> Option.bind (EvaluatorCellSystem.decodeClass specializerId)
    with
    | Some classDefinition -> Ok(ClassSpecializer classDefinition)
    | None ->
        readSpecializer specializerId declarations store
        |> Result.map ValueSetSpecializer

let private prepareMethods
    (generic: GenericFunction)
    (declarations: Map<EntryIdentity, EvaluatorValueSets.ValueSet>)
    (store: Store)
    : Result<PreparedMethod list, EvaluationError> =
    let rec prepareOne
        (method: Method)
        (specializerIds: EntryIdentity list)
        (prepared: PreparedSpecializer list)
        : Result<PreparedMethod, EvaluationError> =
        match specializerIds with
        | [] ->
            Ok
                {
                    Method = method
                    Specializers = List.rev prepared
                }
        | specializerId :: rest ->
            match readPreparedSpecializer specializerId declarations store with
            | Error error -> Error error
            | Ok specializer -> prepareOne method rest (specializer :: prepared)

    let rec prepareAll
        (methods: Method list)
        (prepared: PreparedMethod list)
        : Result<PreparedMethod list, EvaluationError> =
        match methods with
        | [] -> Ok(List.rev prepared)
        | method :: rest ->
            match prepareOne method method.Specializers [] with
            | Error error -> Error error
            | Ok preparedMethod -> prepareAll rest (preparedMethod :: prepared)

    prepareAll generic.Methods []

let private matchesSpecializer
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (argumentId: EntryIdentity)
    (argumentType: ArgumentType)
    (specializer: PreparedSpecializer)
    (state: EvaluationState)
    : Result<bool, string> =
    match argumentType, specializer with
    | ClassType classId, ClassSpecializer classDefinition ->
        EvaluatorClassSystem.isSubclass classId classDefinition.Address state.Store
        |> Result.mapError describeError
    | ClassType _, ValueSetSpecializer _ -> Ok false
    | KnownType sort, ValueSetSpecializer valueSet ->
        if sort.Name = valueSet.Name then
            Ok true
        else
            subsetVerdict executeWord sort valueSet state
    | UnknownType, ValueSetSpecializer valueSet ->
        match EvaluatorValueSets.memberOf executeWord argumentId valueSet state with
        | EvaluatorValueSets.Member -> Ok true
        | EvaluatorValueSets.NotMember -> Ok false
        | EvaluatorValueSets.Undecided reason -> Error reason
    | _, ClassSpecializer _ -> Ok false

let private methodApplies
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (arguments: EntryIdentity list)
    (argumentTypes: ArgumentType list)
    (prepared: PreparedMethod)
    (state: EvaluationState)
    : Result<bool, string> =
    let rec loop
        (argumentIds: EntryIdentity list)
        (types: ArgumentType list)
        (specializers: PreparedSpecializer list)
        : Result<bool, string> =
        match argumentIds, types, specializers with
        | argumentId :: restIds, argumentType :: restTypes, specializer :: restSpecializers ->
            match matchesSpecializer executeWord argumentId argumentType specializer state with
            | Error reason -> Error reason
            | Ok false -> Ok false
            | Ok true -> loop restIds restTypes restSpecializers
        | [], [], [] -> Ok true
        | _ -> Ok false

    loop arguments argumentTypes prepared.Specializers

let private specializerSubset
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (left: PreparedSpecializer)
    (right: PreparedSpecializer)
    (state: EvaluationState)
    : Result<bool, string> =
    match left, right with
    | ClassSpecializer leftClass, ClassSpecializer rightClass ->
        EvaluatorClassSystem.isSubclass leftClass.Address rightClass.Address state.Store
        |> Result.mapError describeError
    | ValueSetSpecializer leftSet, ValueSetSpecializer rightSet -> subsetVerdict executeWord leftSet rightSet state
    | _ -> Ok false

let private strictlyMoreSpecific
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (left: PreparedMethod)
    (right: PreparedMethod)
    (state: EvaluationState)
    : Result<bool, string> =
    let rec loop
        (leftSpecializers: PreparedSpecializer list)
        (rightSpecializers: PreparedSpecializer list)
        (allSubset: bool)
        (anyStrict: bool)
        : Result<bool, string> =
        match leftSpecializers, rightSpecializers with
        | left :: leftRest, right :: rightRest ->
            match specializerSubset executeWord left right state with
            | Error reason -> Error reason
            | Ok subset ->
                match specializerSubset executeWord right left state with
                | Error reason -> Error reason
                | Ok reverse ->
                    let strict = subset && not reverse
                    loop leftRest rightRest (allSubset && subset) (anyStrict || strict)
        | [], [] -> Ok(allSubset && anyStrict)
        | _ -> Ok false

    loop left.Specializers right.Specializers true false

/// Selects a method. `argumentTypes` is supplied by the caller so the same
/// inference is used for selection and for the refusal's argument labels.
let selectMethod
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (generic: GenericFunction)
    (arguments: EntryIdentity list)
    (argumentTypes: ArgumentType list)
    (declarations: Map<EntryIdentity, EvaluatorValueSets.ValueSet>)
    (state: EvaluationState)
    : Result<DispatchSelection, EvaluationError> =
    let store = state.Store

    let exactNames =
        argumentTypes
        |> List.map (function
            | KnownType sort -> sort.Name
            | ClassType classId -> Some classId
            | UnknownType -> None)

    // The exact tuple is the static fast path and is definitive under the
    // default policy: a method whose specializers are exactly the argument
    // types is selected before the subtype search. The order-sensitive
    // policies need the full applicable set, so they skip this shortcut.
    let exactMethod =
        if generic.Policy = MostSpecific && List.forall Option.isSome exactNames then
            let key = exactNames |> List.map Option.get
            generic.Methods |> List.tryFind (fun method -> method.Specializers = key)
        else
            None

    match exactMethod with
    | Some method -> Ok(Selected method)
    | None ->
        match prepareMethods generic declarations store with
        | Error error -> Error error
        | Ok prepared ->
            let rec applicableOf
                (remaining: PreparedMethod list)
                (accumulated: PreparedMethod list)
                : Result<PreparedMethod list, EvaluationError> =
                match remaining with
                | [] -> Ok(List.rev accumulated)
                | method :: rest ->
                    match methodApplies executeWord arguments argumentTypes method state with
                    | Error reason -> Error(DispatchUndecided reason)
                    | Ok true -> applicableOf rest (method :: accumulated)
                    | Ok false -> applicableOf rest accumulated

            match applicableOf prepared [] with
            | Error error -> Error error
            | Ok applicable ->
                match generic.Policy with
                | Unique ->
                    match applicable with
                    | [ method ] -> Ok(Selected method.Method)
                    | [] -> Ok NoMatch
                    | methods -> Ok(Ambiguous(methods |> List.map (fun prepared -> prepared.Method)))
                | First ->
                    match applicable with
                    | method :: _ -> Ok(Selected method.Method)
                    | [] -> Ok NoMatch
                | MostSpecific ->
                    let rec maximalOf
                        (remaining: PreparedMethod list)
                        (accumulated: PreparedMethod list)
                        : Result<PreparedMethod list, EvaluationError> =
                        match remaining with
                        | [] -> Ok(List.rev accumulated)
                        | candidate :: rest ->
                            let rec dominatedBy (others: PreparedMethod list) : Result<bool, EvaluationError> =
                                match others with
                                | [] -> Ok false
                                | other :: more ->
                                    if other.Method.Address = candidate.Method.Address then
                                        dominatedBy more
                                    else
                                        match strictlyMoreSpecific executeWord other candidate state with
                                        | Error reason -> Error(DispatchUndecided reason)
                                        | Ok true -> Ok true
                                        | Ok false -> dominatedBy more

                            match dominatedBy applicable with
                            | Error error -> Error error
                            | Ok true -> maximalOf rest accumulated
                            | Ok false -> maximalOf rest (candidate :: accumulated)

                    match maximalOf applicable [] with
                    | Error error -> Error error
                    | Ok [ method ] -> Ok(Selected method.Method)
                    | Ok [] -> Ok NoMatch
                    | Ok methods -> Ok(Ambiguous(methods |> List.map (fun prepared -> prepared.Method)))

/// The standard method combination (D48): every applicable method is grouped
/// by qualifier, each group is reduced to its maximal methods, and a group with
/// incomparable maximal methods is ambiguous. Around, before, and primary run
/// most-specific-first; after runs least-specific-first. A call needs a primary.
let selectCombination
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (generic: GenericFunction)
    (arguments: EntryIdentity list)
    (argumentTypes: ArgumentType list)
    (declarations: Map<EntryIdentity, EvaluatorValueSets.ValueSet>)
    (state: EvaluationState)
    : Result<DispatchSelection, EvaluationError> =
    let store = state.Store

    let rec applicableOf
        (remaining: PreparedMethod list)
        (accumulated: PreparedMethod list)
        : Result<PreparedMethod list, EvaluationError> =
        match remaining with
        | [] -> Ok(List.rev accumulated)
        | method :: rest ->
            match methodApplies executeWord arguments argumentTypes method state with
            | Error reason -> Error(DispatchUndecided reason)
            | Ok true -> applicableOf rest (method :: accumulated)
            | Ok false -> applicableOf rest accumulated

    let rec maximalOf
        (remaining: PreparedMethod list)
        (accumulated: PreparedMethod list)
        : Result<PreparedMethod list, EvaluationError> =
        match remaining with
        | [] -> Ok(List.rev accumulated)
        | candidate :: rest ->
            let rec dominatedBy (others: PreparedMethod list) : Result<bool, EvaluationError> =
                match others with
                | [] -> Ok false
                | other :: more ->
                    if other.Method.Address = candidate.Method.Address then
                        dominatedBy more
                    else
                        match strictlyMoreSpecific executeWord other candidate state with
                        | Error reason -> Error(DispatchUndecided reason)
                        | Ok true -> Ok true
                        | Ok false -> dominatedBy more

            match dominatedBy accumulated with
            | Error error -> Error error
            | Ok true -> maximalOf rest accumulated
            | Ok false -> maximalOf rest (candidate :: accumulated)

    let rec insertBySpecificity
        (candidate: PreparedMethod)
        (sorted: PreparedMethod list)
        : Result<PreparedMethod list, CombinationOrderError> =
        match sorted with
        | [] -> Ok [ candidate ]
        | head :: tail ->
            match strictlyMoreSpecific executeWord candidate head state with
            | Error reason -> Error(CombinationFailed(DispatchUndecided reason))
            | Ok true -> Ok(candidate :: sorted)
            | Ok false ->
                match strictlyMoreSpecific executeWord head candidate state with
                | Error reason -> Error(CombinationFailed(DispatchUndecided reason))
                | Ok true -> insertBySpecificity candidate tail |> Result.map (fun rest -> head :: rest)
                | Ok false -> Error(CombinationAmbiguous [ candidate.Method; head.Method ])

    let ordered (methods: PreparedMethod list) : Result<Method list, CombinationOrderError> =
        methods
        |> List.fold
            (fun current next -> current |> Result.bind (fun sorted -> insertBySpecificity next sorted))
            (Ok [])
        |> Result.map (List.map (fun prepared -> prepared.Method))

    let exactNames =
        argumentTypes
        |> List.map (function
            | KnownType sort -> sort.Name
            | ClassType classId -> Some classId
            | UnknownType -> None)

    // The exact primary is definitive before the subtype search, as in D19.
    let exactPrimary =
        if generic.Policy = MostSpecific && List.forall Option.isSome exactNames then
            let key = exactNames |> List.map Option.get

            generic.Methods
            |> List.tryFind (fun method -> method.Qualifier = Primary && method.Specializers = key)
        else
            None

    match prepareMethods generic declarations store with
    | Error error -> Error error
    | Ok prepared ->
        match applicableOf prepared [] with
        | Error error -> Error error
        | Ok applicable ->
            let group qualifier =
                applicable
                |> List.filter (fun prepared -> prepared.Method.Qualifier = qualifier)

            let groupOrdered qualifier =
                let maximal = maximalOf (group qualifier) []
                maximal |> Result.mapError CombinationFailed |> Result.bind ordered

            let primaries =
                match exactPrimary with
                | Some method ->
                    // Keep the less specific primaries reachable through
                    // `call-next-method`, but start with the exact one. The
                    // exact method is definitive, so an equal-extension tie
                    // among the others cannot make the call ambiguous.
                    let others =
                        group Primary
                        |> List.filter (fun prepared -> prepared.Method.Address <> method.Address)

                    maximalOf others []
                    |> Result.mapError CombinationFailed
                    |> Result.bind ordered
                    |> Result.map (fun methods -> method :: methods)
                | None -> groupOrdered Primary

            match groupOrdered Around, groupOrdered Before, primaries, groupOrdered After with
            | Error(CombinationFailed error), _, _, _
            | _, Error(CombinationFailed error), _, _
            | _, _, Error(CombinationFailed error), _
            | _, _, _, Error(CombinationFailed error) -> Error error
            | Error(CombinationAmbiguous methods), _, _, _
            | _, Error(CombinationAmbiguous methods), _, _
            | _, _, Error(CombinationAmbiguous methods), _
            | _, _, _, Error(CombinationAmbiguous methods) -> Ok(Ambiguous methods)
            | Ok arounds, Ok befores, Ok primaries, Ok afters ->
                match primaries with
                | [] -> Ok NoMatch
                | _ -> Ok(SelectedCombination(arounds, befores, primaries, List.rev afters))

let private argumentLabel (argumentType: ArgumentType) (store: Store) : string =
    match argumentType with
    | KnownType sort -> EvaluatorValueSets.setLabel sort store
    | ClassType classId ->
        match
            Store.lookupTensor classId store
            |> Option.bind (EvaluatorCellSystem.decodeClass classId)
        with
        | Some classDefinition -> nameText classDefinition.Name store
        | None -> "unknown class"
    | UnknownType -> "unknown"

let private methodLabel (method: Method) (store: Store) : string =
    method.Specializers
    |> List.map (fun specializerId ->
        match
            Store.lookupTensor specializerId store
            |> Option.bind (EvaluatorCellSystem.decodeClass specializerId)
        with
        | Some classDefinition -> nameText classDefinition.Name store
        | None -> nameText specializerId store)
    |> String.concat ", "

/// The internal token a method body carries. It is not a surface word.
let methodReturnToken = "\u0000method-return"

/// Starts one method body. The frame's caller continuation is already the
/// program and stack that follow the call or the `call-next-method` token.
let rec startMethod (frame: MethodCallFrame) (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match EvaluatorCode.decode frame.Method.Body state.Store with
    | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "method", ExpectedQuotation))
    | Error error -> Error(CodeError(EvaluatorCode.describeError error))
    | Ok bodyTerms ->
        let stackWithArguments =
            if frame.ReturnToCaller then
                // `call-next-method` starts the next method with the original
                // arguments; its result returns to the caller's saved stack.
                frame.Arguments
            else
                frame.Arguments
                |> List.fold (fun stack argumentId -> Stack.push argumentId stack) frame.CallerStack

        Ok
            { state with
                Program = bodyTerms @ (SymbolicAbstraction.Word methodReturnToken :: state.Program)
                Stack = stackWithArguments
                MethodFrames = MethodCall frame :: state.MethodFrames
            }

/// Runs the next step of the core: before methods, then the primary chain, then
/// after methods. The core frame is the head of `MethodFrames`.
and continueCore (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match state.MethodFrames with
    | CombinationCall frame :: rest ->
        match frame.Befores with
        | before :: befores ->
            let frame = { frame with Befores = befores }

            startMethod
                {
                    Method = before
                    Kind = BeforeCall
                    CallerProgram = frame.CallerProgram
                    CallerStack = frame.CallerStack
                    Arguments = frame.Arguments
                    NextMethods = []
                    ReturnToCaller = false
                    Core = None
                }
                { state with
                    MethodFrames = CombinationCall frame :: rest
                }
        | [] ->
            match frame.Primaries with
            | primary :: primaries ->
                // The core runs only the most specific primary. The remaining
                // primaries are reached through `call-next-method`.
                let frame = { frame with Primaries = [] }

                startMethod
                    {
                        Method = primary
                        Kind = PrimaryCall
                        CallerProgram = frame.CallerProgram
                        CallerStack = frame.CallerStack
                        Arguments = frame.Arguments
                        NextMethods = primaries
                        ReturnToCaller = false
                        Core = None
                    }
                    { state with
                        MethodFrames = CombinationCall frame :: rest
                    }
            | [] ->
                match frame.Afters with
                | after :: afters ->
                    let frame = { frame with Afters = afters }

                    startMethod
                        {
                            Method = after
                            Kind = AfterCall
                            CallerProgram = frame.CallerProgram
                            CallerStack = frame.CallerStack
                            Arguments = frame.Arguments
                            NextMethods = []
                            ReturnToCaller = false
                            Core = None
                        }
                        { state with
                            MethodFrames = CombinationCall frame :: rest
                        }
                | [] ->
                    match frame.PrimaryStack with
                    | Some primaryStack ->
                        Ok
                            { state with
                                Program = frame.CallerProgram
                                Stack = primaryStack
                                MethodFrames = rest
                            }
                    | None -> Error(EmptyValue "method")
    | _ -> Error NoNextMethod

/// Starts the core with a caller continuation. The core is pushed below the
/// around method that asked for it, so finishing the core resumes that method.
and startCore (frame: CombinationFrame) (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    continueCore
        { state with
            MethodFrames = CombinationCall frame :: state.MethodFrames
        }

/// A method body has finished. Around and primary methods return their top
/// value; before and after methods run for effect only. A primary or around
/// invoked by `call-next-method` resumes that caller; a primary invoked by the
/// core updates the core's result.
and methodReturnWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match state.MethodFrames with
    | MethodCall frame :: rest ->
        match frame.Kind with
        | AroundCall ->
            if frame.ReturnToCaller then
                match Stack.peek state.Stack with
                | None -> Error(EmptyValue "method")
                | Some result ->
                    Ok
                        { state with
                            Program = frame.CallerProgram
                            Stack = Stack.push result frame.CallerStack
                            MethodFrames = rest
                        }
            else
                Ok
                    { state with
                        Program = frame.CallerProgram
                        MethodFrames = rest
                    }
        | PrimaryCall ->
            if frame.ReturnToCaller then
                match Stack.peek state.Stack with
                | None -> Error(EmptyValue "method")
                | Some result ->
                    Ok
                        { state with
                            Program = frame.CallerProgram
                            Stack = Stack.push result frame.CallerStack
                            MethodFrames = rest
                        }
            else
                match rest with
                | CombinationCall core :: rest2 ->
                    let core =
                        { core with
                            PrimaryStack = Some state.Stack
                        }

                    continueCore
                        { state with
                            MethodFrames = CombinationCall core :: rest2
                        }
                | _ -> Error NoNextMethod
        | BeforeCall
        | AfterCall ->
            match rest with
            | CombinationCall core :: rest2 ->
                continueCore
                    { state with
                        Stack = core.CallerStack
                        MethodFrames = CombinationCall core :: rest2
                    }
            | _ -> Error NoNextMethod
    | _ -> Error NoNextMethod

/// `call-next-method` resumes the next method in the current segment. Around
/// falls through to the core; primary falls through to the next primary; before
/// and after have no next method.
let callNextMethodWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match state.MethodFrames with
    | MethodCall frame :: rest ->
        match frame.Kind with
        | AroundCall ->
            match frame.NextMethods with
            | next :: later ->
                let nextFrame =
                    { frame with
                        Method = next
                        NextMethods = later
                        CallerProgram = state.Program
                        CallerStack = state.Stack
                        ReturnToCaller = true
                    }

                startMethod nextFrame state
            | [] ->
                match frame.Core with
                | Some core ->
                    let core =
                        { core with
                            CallerProgram = state.Program
                            CallerStack = state.Stack
                            Arguments = frame.Arguments
                            PrimaryStack = None
                        }

                    startCore core state
                | None -> Error NoNextMethod
        | PrimaryCall ->
            match frame.NextMethods with
            | next :: later ->
                let nextFrame =
                    { frame with
                        Method = next
                        NextMethods = later
                        CallerProgram = state.Program
                        CallerStack = state.Stack
                        ReturnToCaller = true
                    }

                startMethod nextFrame state
            | [] -> Error NoNextMethod
        | BeforeCall
        | AfterCall -> Error NoNextMethod
    | _ -> Error NoNextMethod

let private pushBool (remaining: EntryIdentity list) (value: bool) (state: EvaluationState) : EvaluationState =
    let (identity, store) =
        Store.addTensor (Tensor.scalar (if value then 1.0 else 0.0)) state.Store

    { state with
        Store = store
        Stack = Stack.push identity remaining
    }

/// `next-method-p` pushes 1 when the current method has a next method. An around
/// method always has the core below it, so it answers true.
let nextMethodPWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    let answer =
        match state.MethodFrames with
        | MethodCall frame :: _ ->
            match frame.Kind with
            | AroundCall -> true
            | PrimaryCall -> not (List.isEmpty frame.NextMethods)
            | BeforeCall
            | AfterCall -> false
        | _ -> false

    Ok(pushBool state.Stack answer state)

/// Dispatches a generic function named by `token` over the top `arity`
/// operands. The standard combination runs around methods, then all before
/// methods, the primary chain, and all after methods. Method bodies receive the
/// underlying values, with their type seals removed.
let invoke
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (token: string)
    (generic: GenericFunction)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    if Stack.depth state.Stack < generic.Arity then
        Error(StackUnderflow(token, generic.Arity, Stack.depth state.Stack))
    else
        let arguments = state.Stack |> List.take generic.Arity |> List.rev

        let remaining = state.Stack |> List.skip generic.Arity
        let declarations = declarationsByName state

        let rec collectTypes
            (remainingArguments: EntryIdentity list)
            (accumulated: ArgumentType list)
            : Result<ArgumentType list, EvaluationError> =
            match remainingArguments with
            | [] -> Ok(List.rev accumulated)
            | argumentId :: rest ->
                match argumentType executeWord declarations argumentId state with
                | Ok argumentType -> collectTypes rest (argumentType :: accumulated)
                | Error reason -> Error(DispatchUndecided reason)

        match collectTypes arguments [] with
        | Error error -> Error error
        | Ok argumentTypes ->
            match selectCombination executeWord generic arguments argumentTypes declarations state with
            | Error error -> Error error
            | Ok NoMatch ->
                let labels =
                    argumentTypes
                    |> List.map (fun argumentType -> argumentLabel argumentType state.Store)

                Error(NoApplicableMethod(token, labels))
            | Ok(Ambiguous methods) ->
                let candidates = methods |> List.map (fun method -> methodLabel method state.Store)
                Error(AmbiguousDispatch(token, candidates))
            | Ok(SelectedCombination(arounds, befores, primaries, afters)) ->
                let methodArguments =
                    arguments |> List.map (fun argumentId -> underlyingValue argumentId state.Store)

                let core =
                    {
                        CallerProgram = state.Program
                        CallerStack = remaining
                        Arguments = methodArguments
                        Befores = befores
                        Primaries = primaries
                        Afters = afters
                        PrimaryStack = None
                    }

                let baseState = { state with Stack = remaining }

                match arounds with
                | [] -> startCore core baseState
                | first :: later ->
                    startMethod
                        {
                            Method = first
                            Kind = AroundCall
                            CallerProgram = state.Program
                            CallerStack = remaining
                            Arguments = methodArguments
                            NextMethods = later
                            ReturnToCaller = false
                            Core = Some core
                        }
                        baseState
            | Ok(Selected method) ->
                match EvaluatorCode.decode method.Body state.Store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord token, ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok bodyTerms ->
                    let restoredStack =
                        arguments
                        |> List.map (fun argumentId -> underlyingValue argumentId state.Store)
                        |> List.fold (fun stack valueId -> Stack.push valueId stack) remaining

                    Ok
                        { state with
                            Stack = restoredStack
                            Program = bodyTerms @ state.Program
                        }

// ---- Words -----------------------------------------------------------------

/// The tag-identified kind of an store entry (D32). A finite sort carries no
/// tag of its own — it is identified by its root — so it is not an entry kind
/// here; `class-of` answers the value class for untagged entries.
type EntryKind =
    | WordEntry
    | GenericFunctionEntry
    | MethodEntry
    | ChannelEntry
    | ChannelStateEntry
    | ChannelEventEntry
    | HistoryStepEntry
    | PredicateSortEntry
    | RuntimeGuardEntry
    | ConstructorEntry

let entryKind (entryId: EntryIdentity) (store: Store) : EntryKind option =
    match Store.lookupTensor entryId store with
    | Some tensor when Tensor.rank tensor = 1 && Tensor.size tensor >= 2 ->
        let tag = Tensor.item tensor 0
        let size = Tensor.size tensor

        if tag = WordTag && size >= 6 then
            Some WordEntry
        elif tag = GenericTag && size >= 4 then
            Some GenericFunctionEntry
        elif tag = MethodTag && size >= 3 then
            Some MethodEntry
        elif tag = ChannelTag && size = 2 then
            Some ChannelEntry
        elif tag = ChannelStateTag && size >= 3 then
            Some ChannelStateEntry
        elif tag = ChannelEventTag then
            Some ChannelEventEntry
        elif tag = HistoryStepTag && size = 8 then
            Some HistoryStepEntry
        elif tag = PredicateSortTag && size >= 3 then
            Some PredicateSortEntry
        elif tag = RuntimeGuardTag && size = 3 then
            Some RuntimeGuardEntry
        elif tag = ConstructorTag && size = 3 then
            Some ConstructorEntry
        else
            None
    | _ -> None

/// `class-of` pops a value and pushes the class verdict (D32):
/// `[ValueTag; status; payload…]`. An undecidable predicate is status 3 with
/// the reason, never a silent class.
let valueOfWord
    (executeWord: string -> EvaluationState -> Result<EvaluationState, EvaluationError>)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("value-of", 1, Stack.depth state.Stack))
    | Some(valueId, remaining) ->
        match Store.lookupTensor valueId state.Store with
        | None -> Error(UnknownIndex(EntryIdentity.value valueId))
        | Some _ ->
            let store, verdict =
                match entryKind valueId state.Store with
                | Some kind ->
                    let code =
                        match kind with
                        | WordEntry -> 1.0
                        | GenericFunctionEntry -> 2.0
                        | MethodEntry -> 3.0
                        | ChannelEntry -> 4.0
                        | ChannelStateEntry -> 5.0
                        | ChannelEventEntry -> 6.0
                        | HistoryStepEntry -> 7.0
                        | PredicateSortEntry -> 8.0
                        | RuntimeGuardEntry -> 9.0
                        | ConstructorEntry -> 10.0

                    state.Store, Tensor.vector [ ValueTag; 4.0; code ]
                | None ->
                    match valueVerdictOf executeWord valueId state with
                    | ValueOf sort ->
                        match sort.Name with
                        | Some nameId ->
                            state.Store, Tensor.vector [ ValueTag; 1.0; double (EntryIdentity.value nameId) ]
                        | None -> state.Store, Tensor.vector [ ValueTag; 0.0 ]
                    | ValueUnknown -> state.Store, Tensor.vector [ ValueTag; 0.0 ]
                    | ValueAmbiguous sorts ->
                        match sorts |> List.choose (fun sort -> sort.Name) with
                        | [] -> state.Store, Tensor.vector [ ValueTag; 0.0 ]
                        | names ->
                            let identities =
                                names |> List.map (fun identity -> double (EntryIdentity.value identity))

                            state.Store, Tensor.vector (ValueTag :: 2.0 :: identities)
                    | ValueUndecided reason ->
                        let (reasonId, store1) = Store.addText reason state.Store

                        store1, Tensor.vector [ ValueTag; 3.0; double (EntryIdentity.value reasonId) ]

            let (verdictId, finalImage) = Store.addTensor verdict store

            Ok
                { state with
                    Store = finalImage
                    Stack = Stack.push verdictId remaining
                }

let genericWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("generic", 2, Stack.depth state.Stack))
    | Some(arityId, afterArity) ->
        match Store.lookupTensor arityId state.Store with
        | None -> Error(UnknownIndex(EntryIdentity.value arityId))
        | Some arityTensor ->
            match scalarValue "generic" arityTensor with
            | Error error -> Error error
            | Ok rawArity ->
                let integral =
                    System.Double.IsFinite rawArity
                    && rawArity >= 0.0
                    && rawArity = System.Math.Floor rawArity

                if not integral then
                    Error(ArityError(ArityFault.InvalidGeneric("generic", rawArity)))
                else
                    let arity = int rawArity

                    match Stack.popIndex afterArity with
                    | None -> Error(StackUnderflow("generic", 2, Stack.depth state.Stack))
                    | Some(nameId, remainingStack) ->
                        match Store.readTextAt nameId state.Store with
                        | None -> Error(TypeMismatch(ErrorWord "generic", ExpectedText))
                        | Some name ->
                            match findByName nameId state.GenericFunctionTable state.Store with
                            | Some existing when existing.Arity = arity -> Ok { state with Stack = remainingStack }
                            | Some existing -> Error(ArityError(ArityFault.GenericMismatch(name, existing.Arity, arity)))
                            | None ->
                                let (entryId, store1) =
                                    Store.addTensor (encodeGeneric nameId arity MostSpecific []) state.Store

                                let (newRoot, store2) =
                                    EvaluatorValueSets.appendToTable entryId state.GenericFunctionTable store1

                                Ok
                                    { state with
                                        Store = store2
                                        GenericFunctionTable = Some newRoot
                                        Stack = remainingStack
                                    }

let private specializerNameOfToken (token: string) : string option =
    if token.Length > 1 && token.StartsWith(":") then
        Some(token.Substring(1))
    elif isTextLiteral token then
        Some(literalBody token)
    else
        None

/// `name signature body method`: the signature is a quotation whose tokens are
/// `:Name` or text references, so repeated specializers keep their order and
/// count. The body is a second quotation. `before-method`, `after-method`, and
/// `around-method` use the same shape with a qualifier.
let private qualifiedMethodWord
    (qualifier: MethodQualifier)
    (state: EvaluationState)
    : Result<EvaluationState, EvaluationError> =
    match Stack.popIndex state.Stack with
    | None -> Error(StackUnderflow("method", 3, Stack.depth state.Stack))
    | Some(bodyId, afterBody) ->
        match EvaluatorCode.decode bodyId state.Store with
        | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "method", ExpectedQuotation))
        | Error error -> Error(CodeError(EvaluatorCode.describeError error))
        | Ok _ ->
            match Stack.popIndex afterBody with
            | None -> Error(StackUnderflow("method", 3, Stack.depth state.Stack))
            | Some(signatureId, afterSignature) ->
                match EvaluatorCode.decode signatureId state.Store with
                | Error(EvaluatorCode.NotCode _) -> Error(TypeMismatch(ErrorWord "method", ExpectedQuotation))
                | Error error -> Error(CodeError(EvaluatorCode.describeError error))
                | Ok signatureTerms ->
                    let signatureText = EvaluatorProgram.toKernelTokens signatureTerms |> String.concat " "
                    match Stack.popIndex afterSignature with
                    | None -> Error(StackUnderflow("method", 3, Stack.depth state.Stack))
                    | Some(nameId, remainingStack) ->
                        match Store.readTextAt nameId state.Store with
                        | None -> Error(TypeMismatch(ErrorWord "method", ExpectedText))
                        | Some name ->
                            match findByName nameId state.GenericFunctionTable state.Store with
                            | None -> Error(Unresolved(UnknownKind.Generic, name))
                            | Some generic ->
                                let signatureTokens = splitText signatureText

                                if List.length signatureTokens <> generic.Arity then
                                    Error(ArityError(ArityFault.MethodMismatch(name, generic.Arity, (List.length signatureTokens))))
                                else
                                    let rec resolve
                                        (tokens: string list)
                                        (resolved: EntryIdentity list)
                                        (store: Store)
                                        : Result<EntryIdentity list * Store, EvaluationError> =
                                        match tokens with
                                        | [] -> Ok(List.rev resolved, store)
                                        | token :: rest ->
                                            match specializerNameOfToken token with
                                            | None -> Error(TypeMismatch(ErrorWord "method", ExpectedText))
                                            | Some specializerName ->
                                                let (specializerId, store1) = Store.addText specializerName store

                                                let resolvedId =
                                                    match
                                                        EvaluatorCellSystem.findClass
                                                            specializerId
                                                            state.ClassTable
                                                            store1
                                                    with
                                                    | Some classId -> classId
                                                    | None -> specializerId

                                                resolve rest (resolvedId :: resolved) store1

                                    match resolve signatureTokens [] state.Store with
                                    | Error error -> Error error
                                    | Ok(specializers, store1) ->
                                        let rec validate
                                            (identities: EntryIdentity list)
                                            : Result<unit, EvaluationError> =
                                            match identities with
                                            | [] -> Ok()
                                            | specializerId :: rest ->
                                                let isClass =
                                                    Store.lookupTensor specializerId store1
                                                    |> Option.bind (EvaluatorCellSystem.decodeClass specializerId)
                                                    |> Option.isSome

                                                if isClass then
                                                    validate rest
                                                else
                                                    match
                                                        EvaluatorValueSets.readDeclaration
                                                            specializerId
                                                            state.DeclarationTable
                                                            store1
                                                    with
                                                    | Some _ -> validate rest
                                                    | None -> Error(Unresolved(UnknownKind.Model, (nameText specializerId store1)))

                                        match validate specializers with
                                        | Error error -> Error error
                                        | Ok() ->
                                            let (methodId, store2) =
                                                Store.addTensor
                                                    (encodeMethod qualifier generic.Arity bodyId specializers)
                                                    store1

                                            let keptMethods =
                                                generic.Methods
                                                |> List.filter (fun method ->
                                                    method.Qualifier <> qualifier
                                                    || method.Specializers <> specializers)

                                            let methods =
                                                keptMethods
                                                @ [
                                                    {
                                                        Address = methodId
                                                        Qualifier = qualifier
                                                        Arity = generic.Arity
                                                        Body = bodyId
                                                        Specializers = specializers
                                                    }
                                                ]

                                            let (genericId, store3) =
                                                Store.addTensor
                                                    (encodeGeneric generic.Name generic.Arity generic.Policy methods)
                                                    store2

                                            let (newRoot, store4) =
                                                EvaluatorValueSets.appendToTable
                                                    genericId
                                                    state.GenericFunctionTable
                                                    store3

                                            Ok
                                                { state with
                                                    Store = store4
                                                    GenericFunctionTable = Some newRoot
                                                    Stack = remainingStack
                                                }

let methodWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> = qualifiedMethodWord Primary state

let beforeMethodWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    qualifiedMethodWord Before state

let afterMethodWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    qualifiedMethodWord After state

let aroundMethodWord (state: EvaluationState) : Result<EvaluationState, EvaluationError> =
    qualifiedMethodWord Around state
