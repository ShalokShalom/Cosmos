module DeclarativeIoPolicy

open HostPolicy
open DeclarativeIo

/// Adapts a declarative-IO installation to the shared host-policy contract.
/// The installation remains the authority; this adapter only answers delete
/// questions and never performs the effect.
type Adapter(installation: Installation) =
    let deleteCapability =
        installation.Policy.Delete
        |> Option.bind (fun name ->
            installation.Capabilities
            |> Map.tryFind name
            |> Option.bind (fun capability ->
                match capability with
                | Trash path
                | Unlink path -> Some(name, path)
                | Directory _
                | Vision _ -> None))

    interface IHostPolicy with
        member _.Authorize(capability) =
            match capability with
            | FileDelete path ->
                match deleteCapability with
                | None -> Refuse "the installation has no delete capability"
                | Some(name, root) ->
                    let resolvedRoot = PathPolicy.resolveRealPath root
                    let resolvedPath = PathPolicy.resolveRealPath path

                    if PathPolicy.isUnderRoot resolvedRoot resolvedPath then
                        Allow
                    else
                        Refuse("the delete capability " + CapabilityName.value name + " does not cover " + path)
            | Program _
            | WriteRoot _
            | Environment _
            | Token _ -> Refuse "the declarative-IO policy grants only file deletion"
        member _.Release _ = Error "the declarative-IO policy has no resource tokens"
