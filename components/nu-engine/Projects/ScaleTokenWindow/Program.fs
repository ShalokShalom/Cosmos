namespace ScaleTokenWindow

open System
open System.IO
open Nu

module Program =

    [<EntryPoint; STAThread>]
    let main _ =
        Directory.SetCurrentDirectory AppContext.BaseDirectory
        Nu.init ()

        let sdlWindowConfig =
            { SdlWindowConfig.defaultConfig with
                WindowTitle = "Codeberg Token"
            }

        let sdlConfig =
            { SdlConfig.defaultConfig with
                WindowConfig = sdlWindowConfig
            }

        let worldConfig =
            { WorldConfig.defaultConfig with
                SdlConfig = sdlConfig
            }

        World.run ignore worldConfig (TokenPlugin ())
