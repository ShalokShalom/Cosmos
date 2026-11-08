module HostPolicy

/// One authority request. Capabilities are closed; host adapters interpret
/// them. New kinds are a versioned change, not an open plugin surface.
type Capability =
    | Program of name: string
    | WriteRoot of path: string
    | Environment of inheritEnvironment: bool
    | FileDelete of path: string
    | Token of token: string

type Decision =
    | Allow
    | Refuse of reason: string

type ResourceToken = private ResourceToken of string

module ResourceToken =
    let create (value: string) : ResourceToken = ResourceToken value
    let value (ResourceToken value) : string = value

/// The host-policy boundary. Executors authorize before acting; the policy
/// never performs the effect itself.
type IHostPolicy =
    abstract Authorize: Capability -> Decision
    abstract Release: ResourceToken -> Result<unit, string>

let allowed (decision: Decision) : bool =
    match decision with
    | Allow -> true
    | Refuse _ -> false

let denyAll: IHostPolicy =
    { new IHostPolicy with
        member _.Authorize _ = Refuse "default deny"
        member _.Release _ = Error "default deny" }
