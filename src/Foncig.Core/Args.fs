namespace Foncig

/// Options for reading command-line arguments.
type ArgsOptions =
    {
        /// Maps a switch exactly as written (`-h`, `--host`) to a key (`Db:Host`). Case-insensitive.
        SwitchMappings: (string * string) list
    }

module ArgsOptions =
    let defaults = { SwitchMappings = [] }

/// Result of parsing arguments: configuration entries and the positional arguments.
type ArgsResult =
    { Entries: Entry list
      Positionals: string list }

/// Command-line arguments to entries.
///
/// Forms: `--key value`, `--key=value`, `--key:sub value`, `/key=value`, a bare `--flag` (value `"true"`),
/// and mapped switches (`-h value`, `-v`). A following token is the switch's value unless it starts with `-`
/// (a negative number such as `-5` is still a value). `--` stops switch parsing; everything after it, and any
/// token that is neither a switch nor a value, is a positional argument. `/key` without `=` is positional.
/// An unmapped single-dash switch is `UnknownSwitch(token, index)`.
module Args =

    let private isNegativeNumber (s: string) =
        s.Length > 1 && s.[0] = '-' && s.[1] >= '0' && s.[1] <= '9'

    let private looksLikeSwitch (s: string) =
        s.StartsWith "-" && not (isNegativeNumber s)

    let parse (options: ArgsOptions) (args: string list) : Result<ArgsResult, ConfigError list> =
        let mappings =
            options.SwitchMappings
            |> List.map (fun (k, v) -> k.ToLowerInvariant(), v)
            |> Map.ofList

        let argv = Array.ofList args
        let entries = ResizeArray<Entry>()
        let positionals = ResizeArray<string>()
        let errors = ResizeArray<ConfigError>()

        let add (key: string) (value: string) (detail: string) =
            match ConfigKey.validate key with
            | Ok k ->
                entries.Add
                    { Key = k
                      Value = Text value
                      Origin =
                        { Source = "args"
                          Detail = Some detail } }
            | Error reason ->
                errors.Add(
                    InvalidKey(
                        key,
                        reason,
                        Some
                            { Source = "args"
                              Detail = Some detail }
                    )
                )

        let mutable i = 0
        let mutable stopped = false

        while i < argv.Length do
            let token = argv.[i]

            if stopped then
                positionals.Add token
                i <- i + 1
            elif token = "--" then
                stopped <- true
                i <- i + 1
            elif token.StartsWith "/" && token.IndexOf '=' > 1 then
                let eq = token.IndexOf '='
                let name = token.Substring(0, eq)

                let key =
                    defaultArg (Map.tryFind (name.ToLowerInvariant()) mappings) (name.Substring 1)

                add key (token.Substring(eq + 1)) token
                i <- i + 1
            elif looksLikeSwitch token then
                let eq = token.IndexOf '='
                let name = if eq > 0 then token.Substring(0, eq) else token
                let inlineValue = if eq > 0 then Some(token.Substring(eq + 1)) else None
                let isDouble = name.StartsWith "--"
                let mapped = Map.tryFind (name.ToLowerInvariant()) mappings

                let key =
                    match mapped with
                    | Some k -> Some k
                    | None -> if isDouble then Some(name.Substring 2) else None

                match key with
                | None ->
                    errors.Add(UnknownSwitch(token, i))
                    i <- i + 1
                | Some key ->
                    match inlineValue with
                    | Some v ->
                        add key v token
                        i <- i + 1
                    | None ->
                        if
                            i + 1 < argv.Length
                            && argv.[i + 1] <> "--"
                            && not (looksLikeSwitch argv.[i + 1])
                            && not (argv.[i + 1].StartsWith "/" && argv.[i + 1].IndexOf '=' > 1)
                        then
                            add key argv.[i + 1] (token + " " + argv.[i + 1])
                            i <- i + 2
                        else
                            add key "true" token
                            i <- i + 1
            else
                positionals.Add token
                i <- i + 1

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok
                { Entries = List.ofSeq entries
                  Positionals = List.ofSeq positionals }
