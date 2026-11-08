module PatternHost

open PatternLogic

type HostPattern =
    | Portable of Pattern
    | HostOnly of host: string * feature: string * children: HostPattern list

type HostPatternError =
    | HostSpecific of host: string * feature: string

type IHostPatternElaborator =
    abstract Lower: host: string -> feature: string -> children: Pattern list -> Result<Pattern, HostPatternError>

let rec private lowerChildren
    (elaborator: IHostPatternElaborator)
    (children: HostPattern list)
    : Result<Pattern list, HostPatternError> =
    children
    |> List.fold
        (fun accumulated child ->
            match accumulated with
            | Error error -> Error error
            | Ok current ->
                lower elaborator child
                |> Result.map (fun lowered -> current @ [ lowered ]))
        (Ok [])

and lower
    (elaborator: IHostPatternElaborator)
    (pattern: HostPattern)
    : Result<Pattern, HostPatternError> =
    match pattern with
    | Portable portable -> Ok portable
    | HostOnly(host, feature, children) ->
        lowerChildren elaborator children
        |> Result.bind (fun lowered -> elaborator.Lower host feature lowered)

let toPortable
    (elaborator: IHostPatternElaborator option)
    (pattern: HostPattern)
    : Result<Pattern, HostPatternError> =
    match elaborator with
    | Some lowering -> lower lowering pattern
    | None ->
        match pattern with
        | Portable portable -> Ok portable
        | HostOnly(host, feature, _) -> Error(HostSpecific(host, feature))
