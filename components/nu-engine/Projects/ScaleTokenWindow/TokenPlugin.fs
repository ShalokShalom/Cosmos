namespace ScaleTokenWindow

open Nu

type TokenPlugin() =
    inherit NuPlugin()

    override this.EditModes =
        Map.ofList [ "Token", fun _ -> () ]
