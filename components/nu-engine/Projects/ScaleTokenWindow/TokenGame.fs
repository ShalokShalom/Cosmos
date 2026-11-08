namespace ScaleTokenWindow

open System
open System.Diagnostics
open System.Numerics
open ImGuiNET
open Nu

module TokenStore =

    let private run (file: string) (arguments: string list) (input: string option) =
        let start = ProcessStartInfo(file)
        start.UseShellExecute <- false
        start.RedirectStandardInput <- true
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true

        for argument in arguments do
            start.ArgumentList.Add argument

        use child = Process.Start start

        match input with
        | Some value ->
            child.StandardInput.Write value
            child.StandardInput.Close()
        | None -> child.StandardInput.Close()

        let error = child.StandardError.ReadToEnd()
        child.StandardOutput.ReadToEnd() |> ignore
        child.WaitForExit()
        child.ExitCode, error

    let store (token: string) : Result<unit, string> =
        if String.IsNullOrWhiteSpace token then
            Error "the token is empty"
        else
            let code, error =
                run
                    "secret-tool"
                    [ "store"; "--label"; "Codeberg token"; "service"; "codeberg"; "username"; "ShalokShalom" ]
                    (Some(token + "\n"))

            if code = 0 then Ok()
            else Error(if String.IsNullOrWhiteSpace error then "secret-tool failed" else error.Trim())

type TokenDispatcher() =
    inherit GameDispatcherImSim()

    let mutable token = ""
    let mutable status = "Paste a Codeberg token, then choose Store."
    let mutable shouldExit = false

    override this.Process(_, world) =
        World.beginScreen "Token" true Vanilla [] world |> ignore

        ImGui.SetNextWindowSize(Vector2(520.0f, 180.0f), ImGuiCond.Once)
        ImGui.SetNextWindowPos(Vector2(80.0f, 80.0f), ImGuiCond.Once)

        if ImGui.Begin "Codeberg token" then
            ImGui.TextWrapped "The token is stored in the desktop keyring. It is not written to Git config or the repository."
            ImGui.Spacing ()
            ImGui.PushItemWidth 470.0f
            let mutable buffer = token

            if ImGui.InputText("Token", &buffer, 4096u, ImGuiInputTextFlags.Password) then
                token <- buffer

            ImGui.PopItemWidth ()

            if ImGui.Button "Store" then
                match TokenStore.store token with
                | Ok() ->
                    status <- "Stored in the keyring."
                    shouldExit <- true
                | Error reason -> status <- reason

            ImGui.SameLine ()

            if ImGui.Button "Cancel" then
                shouldExit <- true

            ImGui.Spacing ()
            ImGui.TextWrapped status
            ImGui.End ()

        World.endScreen world

        if shouldExit then
            World.exit world
