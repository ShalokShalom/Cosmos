module ShellHostPolicy

open HostPolicy
open ShellHost

/// Adapts the shell's program/root/environment policy to the shared
/// host-policy contract. File and token capabilities stay with their owners.
type ShellPolicyAdapter(policy: ShellPolicy) =
    interface IHostPolicy with
        member _.Authorize(capability) =
            match capability with
            | Program name ->
                match checkProgram policy name with
                | Ok() -> Allow
                | Error reason -> Refuse reason
            | WriteRoot path ->
                match checkWritePath policy path with
                | Ok() -> Allow
                | Error reason -> Refuse reason
            | Environment true ->
                match policy with
                | Unrestricted
                | Restricted(_, _, true) -> Allow
                | Restricted(_, _, false) -> Refuse "the shell policy clears the environment"
            | Environment false -> Allow
            | FileDelete _
            | Token _ -> Refuse "the shell policy does not grant file or token capabilities"
        member _.Release _ = Error "the shell policy has no resource tokens"
