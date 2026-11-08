module HostResources

open Types

type ResourceState =
    | Live
    | Released
    | Leaked

type ResourceEntry =
    {
        Token: HostToken
        Handle: obj
        AcquirerImage: string
        AcquirerUnit: string
        HolderImage: string
        HolderUnit: string
        AcquisitionSequence: uint64
        State: ResourceState
    }

type ResourceTable =
    {
        Entries: Map<HostToken, ResourceEntry>
        NextToken: uint64
        NextSequence: uint64
    }

type ResourceError =
    | UnknownToken of HostToken
    | TokenNotLive of HostToken * ResourceState
    | TokenAlreadyPresent of HostToken

type ReleaseFailure =
    {
        Token: HostToken
        Reason: string
    }

type ReleaseReport =
    {
        Released: HostToken list
        Failed: ReleaseFailure list
    }

let empty: ResourceTable =
    {
        Entries = Map.empty
        NextToken = 0UL
        NextSequence = 0UL
    }

let acquire
    (table: ResourceTable)
    (handle: obj)
    (acquirerImage: string)
    (acquirerUnit: string)
    : HostToken * ResourceTable =
    let token = HostToken.create table.NextToken

    let entry =
        {
            Token = token
            Handle = handle
            AcquirerImage = acquirerImage
            AcquirerUnit = acquirerUnit
            HolderImage = acquirerImage
            HolderUnit = acquirerUnit
            AcquisitionSequence = table.NextSequence
            State = Live
        }

    token,
    {
        Entries = Map.add token entry table.Entries
        NextToken = table.NextToken + 1UL
        NextSequence = table.NextSequence + 1UL
    }

let tryFind (token: HostToken) (table: ResourceTable) : ResourceEntry option =
    Map.tryFind token table.Entries

let adopt
    (table: ResourceTable)
    (token: HostToken)
    (handle: obj)
    (acquirerImage: string)
    (acquirerUnit: string)
    : Result<ResourceTable, ResourceError> =
    if Map.containsKey token table.Entries then
        Error(TokenAlreadyPresent token)
    else
        let entry =
            {
                Token = token
                Handle = handle
                AcquirerImage = acquirerImage
                AcquirerUnit = acquirerUnit
                HolderImage = acquirerImage
                HolderUnit = acquirerUnit
                AcquisitionSequence = table.NextSequence
                State = Live
            }

        Ok
            {
                Entries = Map.add token entry table.Entries
                NextToken = max table.NextToken (HostToken.value token + 1UL)
                NextSequence = table.NextSequence + 1UL
            }

let transfer
    (table: ResourceTable)
    (token: HostToken)
    (holderImage: string)
    (holderUnit: string)
    : Result<ResourceTable, ResourceError> =
    match Map.tryFind token table.Entries with
    | None -> Error(UnknownToken token)
    | Some entry when entry.State <> Live -> Error(TokenNotLive(token, entry.State))
    | Some entry ->
        Ok
            {
                table with
                    Entries =
                        Map.add
                            token
                            {
                                entry with
                                    HolderImage = holderImage
                                    HolderUnit = holderUnit
                            }
                            table.Entries
            }

let releaseWith
    (releaseHandle: obj -> Result<unit, string>)
    (table: ResourceTable)
    (token: HostToken)
    : Result<ResourceTable, ResourceError> =
    match Map.tryFind token table.Entries with
    | None -> Error(UnknownToken token)
    | Some entry when entry.State <> Live -> Error(TokenNotLive(token, entry.State))
    | Some entry ->
        let nextState =
            match releaseHandle entry.Handle with
            | Ok () -> Released
            | Error _ -> Leaked

        Ok
            {
                table with
                    Entries = Map.add token { entry with State = nextState } table.Entries
            }

let releaseForImage
    (releaseHandle: obj -> Result<unit, string>)
    (image: string)
    (table: ResourceTable)
    : ReleaseReport * ResourceTable =
    let candidates =
        table.Entries
        |> Map.toList
        |> List.choose (fun (token, entry) ->
            if entry.State = Live && entry.AcquirerImage = image then
                Some(token, entry)
            else
                None)
        |> List.sortByDescending (fun (_, entry) -> entry.AcquisitionSequence)

    let rec loop
        (remaining: (HostToken * ResourceEntry) list)
        (current: ResourceTable)
        (released: HostToken list)
        (failed: ReleaseFailure list)
        : ReleaseReport * ResourceTable =
        match remaining with
        | [] ->
            {
                Released = List.rev released
                Failed = List.rev failed
            },
            current
        | (token, entry) :: rest ->
            match releaseHandle entry.Handle with
            | Ok () ->
                loop
                    rest
                    {
                        current with
                            Entries =
                                Map.add
                                    token
                                    { entry with State = Released }
                                    current.Entries
                    }
                    (token :: released)
                    failed
            | Error reason ->
                loop
                    rest
                    {
                        current with
                            Entries =
                                Map.add
                                    token
                                    { entry with State = Leaked }
                                    current.Entries
                    }
                    released
                    ({ Token = token; Reason = reason } :: failed)

    loop candidates table [] []

let liveTokens (table: ResourceTable) : HostToken list =
    table.Entries
    |> Map.toList
    |> List.choose (fun (token, entry) ->
        if entry.State = Live then Some token else None)

let liveCount (table: ResourceTable) : int =
    liveTokens table |> List.length
