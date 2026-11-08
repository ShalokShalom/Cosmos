module DeclarativeIoExecutor

open System
open System.IO
open System.Security.Cryptography
open DeclarativeIo

let private sha256 (path: string) : Result<string, string> =
    try
        if not (File.Exists path) then
            Error "file is missing"
        else
            File.ReadAllBytes path
            |> SHA256.HashData
            |> Convert.ToHexString
            |> fun value -> value.ToLowerInvariant()
            |> Ok
    with error ->
        Error error.Message

let private uniqueTrashPath (trashRoot: string) (item: PlanItem) : string =
    let name = Path.GetFileName item.Path
    let candidate = Path.Combine(trashRoot, name)

    if File.Exists candidate then
        let suffix =
            if item.Hash.Length >= 12 then
                item.Hash.Substring(0, 12)
            else
                item.Hash

        Path.Combine(trashRoot, name + "." + suffix)
    else
        candidate

type FileSystemExecutor(trashRoot: string, allowUnlink: bool) =
    interface IPlanExecutor with
        member _.Hash path = sha256 path

        member _.Apply item =
            match item.Action with
            | Skip _ -> Ok()
            | MoveToTrash _ ->
                try
                    if not (File.Exists item.Path) then
                        Error "file is missing"
                    else
                        Directory.CreateDirectory trashRoot |> ignore
                        let target = uniqueTrashPath trashRoot item
                        File.Move(item.Path, target)
                        Ok()
                with error ->
                    Error error.Message
            | UnlinkFile _ ->
                if not allowUnlink then
                    Error "unlink capability is disabled"
                else
                    try
                        if not (File.Exists item.Path) then
                            Error "file is missing"
                        else
                            File.Delete item.Path
                            Ok()
                    with error ->
                        Error error.Message
