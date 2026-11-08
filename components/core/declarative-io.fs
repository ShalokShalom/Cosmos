module DeclarativeIo

open System
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text

type CapabilityName = private CapabilityName of string

module CapabilityName =
    let create (value: string) : CapabilityName = CapabilityName value
    let value (CapabilityName value) : string = value

type DirectoryCapability =
    {
        Path: string
        Recursive: bool option
        FollowLinks: bool option
        Hidden: bool option
    }

type Capability =
    | Directory of DirectoryCapability
    | Trash of path: string
    | Unlink of path: string
    | Vision of model: string * threshold: float

type FileKind =
    | RegularFile
    | DirectoryEntry
    | SymbolicLink
    | Other

type Verdict =
    | Pretty
    | NotPretty
    | NotApplicable of reason: string
    | Undecided of reason: string

type VerdictEvidence =
    {
        ModelId: string
        ModelVersion: string
        Score: float
        ContentHash: string
    }

type Candidate =
    {
        Path: string
        Hash: string
        Kind: FileKind
        Verdict: Verdict
        Evidence: VerdictEvidence
    }

type AllPolicy =
    {
        Recursive: bool
        FollowLinks: bool
        Hidden: bool
    }

type UndecidedPolicy =
    | SkipUndecided
    | RefuseUndecided

type ConfirmationPolicy =
    | ConfirmationRequired
    | ConfirmationNotRequired

type ConfirmationReceipt = private ConfirmationReceipt of string

module ConfirmationReceipt =
    let create (value: string) : ConfirmationReceipt = ConfirmationReceipt value
    let value (ConfirmationReceipt value) : string = value

type Policy =
    {
        Delete: CapabilityName option
        All: AllPolicy
        Undecided: UndecidedPolicy
        Confirmation: ConfirmationPolicy
    }

type InstanceType =
    | Container
    | VirtualMachine

type WorldSpec =
    {
        Image: string
        InstanceType: InstanceType
        Cpus: int
        MemoryMiB: int
        DiskGiB: int
        Network: string
    }

type Installation =
    {
        Name: string
        Capabilities: Map<CapabilityName, Capability>
        Policy: Policy
        World: WorldSpec option
    }

type Predicate =
    | NotPrettyPredicate

type Intent =
    | Remove of scope: CapabilityName * action: CapabilityName option * predicate: Predicate

type PlanAction =
    | MoveToTrash of capability: CapabilityName
    | UnlinkFile of capability: CapabilityName
    | Skip of reason: string

type PlanItem =
    {
        Path: string
        Hash: string
        Verdict: Verdict
        Evidence: VerdictEvidence
        Action: PlanAction
    }

type Plan =
    {
        Installation: string
        Scope: string
        Items: PlanItem list
        Confirmation: ConfirmationPolicy
        Undecided: int
        NotApplicable: int
    }

type PlanError =
    | UnknownCapability of CapabilityName
    | WrongCapabilityKind of CapabilityName * expected: string * actual: string
    | MissingDeleteCapability
    | UndecidedVerdict of path: string
    | InvalidVerdictEvidence of path: string
    | MissingVisionCapability
    | MissingWorldImage
    | InvalidWorldValue of value: string
    | InvalidThreshold of float
    | EmptyInstallationName
    | DuplicateCapability of string
    | UnknownResourceKind of string
    | InvalidBoolean of string
    | InvalidNumber of string
    | InvalidLine of int
    | MissingInstallBlock
    | UnclosedInstallBlock
    | MissingAttribute of string
    | UnknownAttribute of string
    | InvalidPolicy of string

let defaultAllPolicy: AllPolicy =
    {
        Recursive = true
        FollowLinks = false
        Hidden = false
    }

let defaultPolicy: Policy =
    {
        Delete = None
        All = defaultAllPolicy
        Undecided = SkipUndecided
        Confirmation = ConfirmationRequired
    }

let defaultWorld: WorldSpec =
    {
        Image = ""
        InstanceType = VirtualMachine
        Cpus = 2
        MemoryMiB = 2048
        DiskGiB = 20
        Network = "default"
    }

let kindName (capability: Capability) : string =
    match capability with
    | Directory _ -> "directory"
    | Trash _ -> "trash"
    | Unlink _ -> "unlink"
    | Vision _ -> "vision"

let private tokenize (line: string) : Result<string list, string> =
    let tokens = ResizeArray<string>()
    let current = StringBuilder()
    let mutable index = 0
    let mutable quoted = false
    let mutable escaped = false
    let mutable finished = false

    let flush () =
        if current.Length > 0 then
            tokens.Add(current.ToString())
            current.Clear() |> ignore

    while not finished && index < line.Length do
        let character = line.[index]

        if escaped then
            current.Append(character) |> ignore
            escaped <- false
            index <- index + 1
        elif character = '\\' && quoted then
            escaped <- true
            index <- index + 1
        elif character = '"' then
            quoted <- not quoted
            index <- index + 1
        elif character = '#' && not quoted then
            flush ()
            finished <- true
        elif Char.IsWhiteSpace character && not quoted then
            flush ()
            index <- index + 1
        else
            current.Append(character) |> ignore
            index <- index + 1

    if quoted then
        Error "an unterminated quoted string"
    else
        flush ()
        Ok(List.ofSeq tokens)

let private parseBoolean (value: string) : Result<bool, PlanError> =
    match value with
    | "true" -> Ok true
    | "false" -> Ok false
    | _ -> Error(InvalidBoolean value)

let private parseThreshold (value: string) : Result<float, PlanError> =
    match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, number when number >= 0.0 && number <= 1.0 -> Ok number
    | true, number -> Error(InvalidThreshold number)
    | false, _ -> Error(InvalidNumber value)

let private parsePositiveInt (value: string) : Result<int, PlanError> =
    match Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
    | true, number when number > 0 -> Ok number
    | _ -> Error(InvalidWorldValue value)

let private parseDirectoryAttributes
    (attributes: string list)
    : Result<AllPolicy, PlanError> =
    let rec loop remaining current =
        match remaining with
        | [] -> Ok current
        | "recursive" :: value :: rest ->
            parseBoolean value
            |> Result.bind (fun parsed -> loop rest { current with Recursive = parsed })
        | "follow-links" :: value :: rest ->
            parseBoolean value
            |> Result.bind (fun parsed -> loop rest { current with FollowLinks = parsed })
        | "hidden" :: value :: rest ->
            parseBoolean value
            |> Result.bind (fun parsed -> loop rest { current with Hidden = parsed })
        | attribute :: _ -> Error(UnknownAttribute attribute)

    loop attributes defaultAllPolicy

let private parseDirectoryOverrides
    (path: string)
    (attributes: string list)
    : Result<DirectoryCapability, PlanError> =
    let rec loop remaining recursive followLinks hidden =
        match remaining with
        | [] ->
            Ok
                {
                    Path = path
                    Recursive = recursive
                    FollowLinks = followLinks
                    Hidden = hidden
                }
        | "recursive" :: value :: rest ->
            parseBoolean value
            |> Result.bind (fun parsed -> loop rest (Some parsed) followLinks hidden)
        | "follow-links" :: value :: rest ->
            parseBoolean value
            |> Result.bind (fun parsed -> loop rest recursive (Some parsed) hidden)
        | "hidden" :: value :: rest ->
            parseBoolean value
            |> Result.bind (fun parsed -> loop rest recursive followLinks (Some parsed))
        | attribute :: _ -> Error(UnknownAttribute attribute)

    loop attributes None None None

let private parseResource
    (tokens: string list)
    : Result<CapabilityName * Capability, PlanError> =
    match tokens with
    | "resource" :: name :: ":" :: "directory" :: "at" :: path :: attributes ->
        parseDirectoryOverrides path attributes
        |> Result.map (fun directory ->
            CapabilityName.create name,
            Directory directory)
    | [ "resource"; name; ":"; "trash"; "at"; path ] ->
        Ok(CapabilityName.create name, Trash path)
    | [ "resource"; name; ":"; "unlink"; "at"; path ] ->
        Ok(CapabilityName.create name, Unlink path)
    | [ "resource"; name; ":"; "vision"; "at"; model; "threshold"; threshold ] ->
        parseThreshold threshold
        |> Result.map (fun parsed -> CapabilityName.create name, Vision(model, parsed))
    | "resource" :: _ :: ":" :: kind :: _ -> Error(UnknownResourceKind kind)
    | "resource" :: _ :: ":" :: [] -> Error(MissingAttribute "kind")
    | "resource" :: _ -> Error(MissingAttribute "at")
    | _ -> Error(InvalidPolicy "a resource line is malformed")

let private parseDefault (tokens: string list) (policy: Policy) : Result<Policy, PlanError> =
    match tokens with
    | [ "default"; "delete"; name ] ->
        Ok
            {
                policy with
                    Delete = Some(CapabilityName.create name)
            }
    | "default" :: "all" :: attributes ->
        parseDirectoryAttributes attributes
        |> Result.map (fun all -> { policy with All = all })
    | [ "default"; "undecided"; "skip" ] ->
        Ok { policy with Undecided = SkipUndecided }
    | [ "default"; "undecided"; "refuse" ] ->
        Ok { policy with Undecided = RefuseUndecided }
    | [ "default"; "confirmation"; "required" ] ->
        Ok { policy with Confirmation = ConfirmationRequired }
    | [ "default"; "confirmation"; "not-required" ] ->
        Ok { policy with Confirmation = ConfirmationNotRequired }
    | _ -> Error(InvalidPolicy "a default line is malformed")

type private ParserState =
    {
        Name: string option
        Capabilities: Map<CapabilityName, Capability>
        Policy: Policy
        World: WorldSpec option
        InBlock: bool
        Closed: bool
    }

let private emptyParserState: ParserState =
    {
        Name = None
        Capabilities = Map.empty
        Policy = defaultPolicy
        World = None
        InBlock = false
        Closed = false
    }

let private updateWorld (state: ParserState) (update: WorldSpec -> WorldSpec) : ParserState =
    let world = state.World |> Option.defaultValue defaultWorld |> update
    { state with World = Some world }

let private parseWorld (tokens: string list) (state: ParserState) : Result<ParserState, PlanError> =
    match tokens with
    | [ "world"; "image"; value ] ->
        if String.IsNullOrWhiteSpace value then
            Error MissingWorldImage
        else
            Ok(updateWorld state (fun world -> { world with Image = value }))
    | [ "world"; "type"; "virtual-machine" ] ->
        Ok(updateWorld state (fun world -> { world with InstanceType = VirtualMachine }))
    | [ "world"; "type"; "container" ] ->
        Ok(updateWorld state (fun world -> { world with InstanceType = Container }))
    | [ "world"; "cpus"; value ] ->
        parsePositiveInt value
        |> Result.map (fun cpus -> updateWorld state (fun world -> { world with Cpus = cpus }))
    | [ "world"; "memory"; value ] ->
        parsePositiveInt value
        |> Result.map (fun memory -> updateWorld state (fun world -> { world with MemoryMiB = memory }))
    | [ "world"; "disk"; value ] ->
        parsePositiveInt value
        |> Result.map (fun disk -> updateWorld state (fun world -> { world with DiskGiB = disk }))
    | [ "world"; "network"; value ] ->
        if String.IsNullOrWhiteSpace value then
            Error(InvalidWorldValue value)
        else
            Ok(updateWorld state (fun world -> { world with Network = value }))
    | _ -> Error(InvalidWorldValue "a world line is malformed")

let private parseLine
    (lineNumber: int)
    (line: string)
    (state: ParserState)
    : Result<ParserState, PlanError> =
    tokenize line
    |> Result.mapError (fun _ -> InvalidLine lineNumber)
    |> Result.bind (fun tokens ->
        match tokens with
        | [] -> Ok state
        | [ "install"; name; "{" ] when not state.InBlock && not state.Closed ->
            if String.IsNullOrWhiteSpace name then
                Error EmptyInstallationName
            else
                Ok
                    {
                        state with
                            Name = Some name
                            InBlock = true
                    }
        | [ "}" ] when state.InBlock ->
            Ok
                {
                    state with
                        InBlock = false
                        Closed = true
                }
        | "resource" :: _ when state.InBlock ->
            parseResource tokens
            |> Result.bind (fun (name, capability) ->
                let key = CapabilityName.value name

                if Map.containsKey name state.Capabilities then
                    Error(DuplicateCapability key)
                else
                    Ok
                        {
                            state with
                                Capabilities = Map.add name capability state.Capabilities
                        })
        | "default" :: _ when state.InBlock ->
            parseDefault tokens state.Policy
            |> Result.map (fun policy -> { state with Policy = policy })
        | "world" :: _ when state.InBlock -> parseWorld tokens state
        | _ -> Error(InvalidLine lineNumber))

let parse (text: string) : Result<Installation, PlanError> =
    let lines = text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)

    let rec loop index state =
        if index >= lines.Length then
            if state.InBlock then
                Error UnclosedInstallBlock
            else
                match state.Name with
                | None -> Error MissingInstallBlock
                | Some name ->
                    let world =
                        match state.World with
                        | None -> Ok None
                        | Some world when String.IsNullOrWhiteSpace world.Image -> Error MissingWorldImage
                        | Some world -> Ok(Some world)

                    match world with
                    | Error error -> Error error
                    | Ok world ->
                        Ok
                            {
                                Name = name
                                Capabilities = state.Capabilities
                                Policy = state.Policy
                                World = world
                            }
        else
            parseLine (index + 1) lines.[index] state
            |> Result.bind (loop (index + 1))

    loop 0 emptyParserState

let private yamlString (value: string) : string =
    "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""

let renderIncusProfile (installation: Installation) : Result<string, PlanError> =
    match installation.World with
    | None -> Error MissingWorldImage
    | Some world when String.IsNullOrWhiteSpace world.Image -> Error MissingWorldImage
    | Some world ->
        let instanceType =
            match world.InstanceType with
            | Container -> "container"
            | VirtualMachine -> "virtual-machine"

        let builder = StringBuilder()
        builder.AppendLine("name: " + yamlString installation.Name) |> ignore
        builder.AppendLine("description: " + yamlString ("Scale world " + installation.Name)) |> ignore
        builder.AppendLine("config:") |> ignore
        builder.AppendLine("  limits.cpu: " + yamlString (string world.Cpus)) |> ignore
        builder.AppendLine("  limits.memory: " + yamlString (string world.MemoryMiB + "MiB")) |> ignore
        builder.AppendLine("  user.scale.image: " + yamlString world.Image) |> ignore
        builder.AppendLine("  user.scale.instance-type: " + yamlString instanceType) |> ignore
        builder.AppendLine("  user.scale.disk: " + yamlString (string world.DiskGiB + "GiB")) |> ignore
        builder.AppendLine("devices:") |> ignore
        builder.AppendLine("  root:") |> ignore
        builder.AppendLine("    type: disk") |> ignore
        builder.AppendLine("    pool: default") |> ignore
        builder.AppendLine("    path: /") |> ignore
        builder.AppendLine("    size: " + yamlString (string world.DiskGiB + "GiB")) |> ignore
        builder.AppendLine("  eth0:") |> ignore
        builder.AppendLine("    type: nic") |> ignore
        builder.AppendLine("    name: eth0") |> ignore
        builder.AppendLine("    network: " + yamlString world.Network) |> ignore
        Ok(builder.ToString())

let private findCapability
    (name: CapabilityName)
    (installation: Installation)
    : Result<Capability, PlanError> =
    match Map.tryFind name installation.Capabilities with
    | Some capability -> Ok capability
    | None -> Error(UnknownCapability name)

let private deleteCapability
    (intentAction: CapabilityName option)
    (installation: Installation)
    : Result<CapabilityName * Capability, PlanError> =
    let selected =
        match intentAction with
        | Some name -> Ok name
        | None ->
            match installation.Policy.Delete with
            | Some name -> Ok name
            | None ->
                installation.Capabilities
                |> Map.toList
                |> List.tryFind (fun (_, capability) ->
                    match capability with
                    | Trash _
                    | Unlink _ -> true
                    | _ -> false)
                |> Option.map fst
                |> function
                    | Some name -> Ok name
                    | None -> Error MissingDeleteCapability

    selected
    |> Result.bind (fun name ->
        findCapability name installation
        |> Result.map (fun capability -> name, capability))

let private actionFor
    (capabilityName: CapabilityName)
    (capability: Capability)
    : Result<PlanAction, PlanError> =
    match capability with
    | Trash _ -> Ok(MoveToTrash capabilityName)
    | Unlink _ -> Ok(UnlinkFile capabilityName)
    | other ->
        Error(
            WrongCapabilityKind(
                capabilityName,
                "trash or unlink",
                kindName other
            )
        )

let private hasValidEvidence
    (installation: Installation)
    (candidate: Candidate)
    : bool =
    let evidence = candidate.Evidence

    let matchesVision =
        installation.Capabilities
        |> Map.exists (fun _ capability ->
            match capability with
            | Vision(model, threshold) ->
                evidence.ModelId = model && evidence.Score >= threshold
            | _ -> false)

    not (String.IsNullOrWhiteSpace evidence.ModelId)
    && not (String.IsNullOrWhiteSpace evidence.ModelVersion)
    && evidence.Score >= 0.0
    && evidence.Score <= 1.0
    && evidence.ContentHash = candidate.Hash
    && matchesVision

let planRemove
    (installation: Installation)
    (intent: Intent)
    (candidates: Candidate list)
    : Result<Plan, PlanError> =
    match intent with
    | Remove(scopeName, requestedAction, NotPrettyPredicate) ->
        findCapability scopeName installation
        |> Result.bind (fun scope ->
            match scope with
            | Directory directory ->
                let hasVision =
                    installation.Capabilities
                    |> Map.exists (fun _ capability ->
                        match capability with
                        | Vision _ -> true
                        | _ -> false)

                if not hasVision then
                    Error MissingVisionCapability
                else
                    deleteCapability requestedAction installation
                    |> Result.bind (fun (actionName, actionCapability) ->
                        actionFor actionName actionCapability
                        |> Result.bind (fun action ->
                            let recursive =
                                directory.Recursive
                                |> Option.defaultValue installation.Policy.All.Recursive

                            let hidden =
                                directory.Hidden
                                |> Option.defaultValue installation.Policy.All.Hidden

                            let followLinks =
                                directory.FollowLinks
                                |> Option.defaultValue installation.Policy.All.FollowLinks

                            let rec loop
                                (remaining: Candidate list)
                                (items: PlanItem list)
                                (undecided: int)
                                (notApplicable: int)
                                : Result<Plan, PlanError> =
                                match remaining with
                                | [] ->
                                    Ok
                                        {
                                            Installation = installation.Name
                                            Scope = directory.Path
                                            Items = List.rev items
                                            Confirmation = installation.Policy.Confirmation
                                            Undecided = undecided
                                            NotApplicable = notApplicable
                                        }
                                | candidate :: rest ->
                                    let relative = PathPolicy.relativePath directory.Path candidate.Path

                                    if not (PathPolicy.isUnderRoot directory.Path candidate.Path) then
                                        loop rest items undecided notApplicable
                                    elif candidate.Kind = SymbolicLink && not followLinks then
                                        loop
                                            rest
                                            ({ Path = candidate.Path
                                               Hash = candidate.Hash
                                               Verdict = candidate.Verdict
                                               Evidence = candidate.Evidence
                                               Action = Skip "symbolic link" }
                                             :: items)
                                            undecided
                                            notApplicable
                                    elif candidate.Kind <> RegularFile && candidate.Kind <> SymbolicLink then
                                        loop
                                            rest
                                            ({ Path = candidate.Path
                                               Hash = candidate.Hash
                                               Verdict = candidate.Verdict
                                               Evidence = candidate.Evidence
                                               Action = Skip "not a regular file" }
                                             :: items)
                                            undecided
                                            notApplicable
                                    elif not recursive && not (PathPolicy.isDirectChild relative) then
                                        loop
                                            rest
                                            ({ Path = candidate.Path
                                               Hash = candidate.Hash
                                               Verdict = candidate.Verdict
                                               Evidence = candidate.Evidence
                                               Action = Skip "not recursive" }
                                             :: items)
                                            undecided
                                            notApplicable
                                    elif not hidden && PathPolicy.isHidden relative then
                                        loop
                                            rest
                                            ({ Path = candidate.Path
                                               Hash = candidate.Hash
                                               Verdict = candidate.Verdict
                                               Evidence = candidate.Evidence
                                               Action = Skip "hidden file" }
                                             :: items)
                                            undecided
                                            notApplicable
                                    elif not (hasValidEvidence installation candidate) then
                                        Error(InvalidVerdictEvidence candidate.Path)
                                    else
                                        match candidate.Verdict with
                                        | NotPretty ->
                                            loop
                                                rest
                                                ({ Path = candidate.Path
                                                   Hash = candidate.Hash
                                                   Verdict = candidate.Verdict
                                                   Evidence = candidate.Evidence
                                                   Action = action }
                                                 :: items)
                                                undecided
                                                notApplicable
                                        | Pretty ->
                                            loop
                                                rest
                                                ({ Path = candidate.Path
                                                   Hash = candidate.Hash
                                                   Verdict = candidate.Verdict
                                                   Evidence = candidate.Evidence
                                                   Action = Skip "pretty" }
                                                 :: items)
                                                undecided
                                                notApplicable
                                        | NotApplicable reason ->
                                            loop
                                                rest
                                                ({ Path = candidate.Path
                                                   Hash = candidate.Hash
                                                   Verdict = candidate.Verdict
                                                   Evidence = candidate.Evidence
                                                   Action = Skip reason }
                                                 :: items)
                                                undecided
                                                (notApplicable + 1)
                                        | Undecided reason ->
                                            match installation.Policy.Undecided with
                                            | RefuseUndecided -> Error(UndecidedVerdict candidate.Path)
                                            | SkipUndecided ->
                                                loop
                                                    rest
                                                    ({ Path = candidate.Path
                                                       Hash = candidate.Hash
                                                       Verdict = candidate.Verdict
                                                       Evidence = candidate.Evidence
                                                       Action = Skip reason }
                                                     :: items)
                                                    (undecided + 1)
                                                    notApplicable

                            loop candidates [] 0 0)
                        |> Result.bind (fun plan ->
                            if plan.Undecided > 0 && installation.Policy.Undecided = RefuseUndecided then
                                Error(UndecidedVerdict "one or more candidates")
                            else
                                Ok plan))
            | other ->
                Error(
                    WrongCapabilityKind(
                        scopeName,
                        "directory",
                        kindName other
                    )
                ))

type IPlanExecutor =
    abstract Hash: path: string -> Result<string, string>
    abstract Apply: PlanItem -> Result<unit, string>

type ApplyReport =
    {
        Applied: PlanItem list
        Skipped: PlanItem list
    }

type ApplyError =
    {
        Item: PlanItem
        Reason: string
        Applied: PlanItem list
    }

let private actionText (action: PlanAction) : string =
    match action with
    | MoveToTrash capability -> "trash:" + CapabilityName.value capability
    | UnlinkFile capability -> "unlink:" + CapabilityName.value capability
    | Skip reason -> "skip:" + reason

let private verdictText (verdict: Verdict) : string =
    match verdict with
    | Pretty -> "pretty"
    | NotPretty -> "not-pretty"
    | NotApplicable reason -> "not-applicable:" + reason
    | Undecided reason -> "undecided:" + reason

let planFingerprint (plan: Plan) : string =
    let builder = StringBuilder()
    builder.AppendLine("installation:" + plan.Installation) |> ignore
    builder.AppendLine("scope:" + plan.Scope) |> ignore

    for item in plan.Items do
        builder.AppendLine("path:" + item.Path) |> ignore
        builder.AppendLine("hash:" + item.Hash) |> ignore
        builder.AppendLine("verdict:" + verdictText item.Verdict) |> ignore
        builder.AppendLine("model:" + item.Evidence.ModelId) |> ignore
        builder.AppendLine("model-version:" + item.Evidence.ModelVersion) |> ignore
        builder.AppendLine("score:" + item.Evidence.Score.ToString("R", CultureInfo.InvariantCulture)) |> ignore
        builder.AppendLine("evidence-hash:" + item.Evidence.ContentHash) |> ignore
        builder.AppendLine("action:" + actionText item.Action) |> ignore

    SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))
    |> Convert.ToHexString
    |> fun value -> value.ToLowerInvariant()

let apply
    (receipt: ConfirmationReceipt option)
    (executor: IPlanExecutor)
    (plan: Plan)
    : Result<ApplyReport, ApplyError> =
    let resolvedScope = PathPolicy.resolveRealPath plan.Scope

    let actionable =
        plan.Items
        |> List.filter (fun item ->
            match item.Action with
            | Skip _ -> false
            | MoveToTrash _
            | UnlinkFile _ -> true)

    let confirmed =
        match plan.Confirmation with
        | ConfirmationNotRequired -> true
        | ConfirmationRequired ->
            match receipt with
            | Some confirmation ->
                ConfirmationReceipt.value confirmation = planFingerprint plan
            | None -> false

    if not confirmed && not (List.isEmpty actionable) then
        Error
            {
                Item = List.head actionable
                Reason = "confirmation required"
                Applied = []
            }
    else
        let rec loop remaining applied skipped =
            match remaining with
            | [] ->
                Ok
                    {
                        Applied = List.rev applied
                        Skipped = List.rev skipped
                    }
            | item :: rest ->
                match item.Action with
                | Skip _ -> loop rest applied (item :: skipped)
                | MoveToTrash _
                | UnlinkFile _ ->
                    let resolvedPath = PathPolicy.resolveRealPath item.Path

                    if not (PathPolicy.isUnderRoot resolvedScope resolvedPath) then
                        Error
                            {
                                Item = item
                                Reason = "plan item resolves outside the scope"
                                Applied = List.rev applied
                            }
                    else
                        match executor.Hash item.Path with
                        | Error reason ->
                            Error
                                {
                                    Item = item
                                    Reason = "hash unavailable: " + reason
                                    Applied = List.rev applied
                                }
                        | Ok actualHash when actualHash <> item.Hash ->
                            Error
                                {
                                    Item = item
                                    Reason = "hash mismatch: plan is stale"
                                    Applied = List.rev applied
                                }
                        | Ok _ ->
                            match executor.Apply item with
                            | Ok () -> loop rest (item :: applied) skipped
                            | Error reason ->
                                Error
                                    {
                                        Item = item
                                        Reason = reason
                                        Applied = List.rev applied
                                    }

        loop plan.Items [] []
