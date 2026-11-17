namespace Foncig

open Fnode

/// A named producer of entries. `Load` does no IO: file sources are given the text.
type ConfigSource =
    { Name: string
      Load: unit -> Result<Entry list, ConfigError list> }

module ConfigSource =

    let create (name: string) (load: unit -> Result<Entry list, ConfigError list>) : ConfigSource =
        { Name = name; Load = load }

    let private ofPairs (name: string) (pairs: (string * EntryValue) list) : Result<Entry list, ConfigError list> =
        let results =
            pairs
            |> List.map (fun (key, value) ->
                let origin = { Source = name; Detail = Some key }

                match ConfigKey.validate key with
                | Ok k ->
                    Ok
                        { Key = k
                          Value = value
                          Origin = origin }
                | Error reason -> Error(InvalidKey(key, reason, Some origin)))

        match
            results
            |> List.choose (function
                | Error e -> Some e
                | Ok _ -> None)
        with
        | [] ->
            Ok(
                results
                |> List.choose (function
                    | Ok e -> Some e
                    | Error _ -> None)
            )
        | errors -> Error errors

    /// Key/value pairs given in code. Later pairs for the same key win.
    let inMemory (name: string) (pairs: (string * string) list) : ConfigSource =
        create name (fun () -> ofPairs name (pairs |> List.map (fun (k, v) -> k, Text v)))

    /// Like `inMemory` but each value may also be `Unset` (clear) or `EmptyList`.
    let inMemoryEntries (name: string) (pairs: (string * EntryValue) list) : ConfigSource =
        create name (fun () -> ofPairs name pairs)

    /// Any `Node` built in code.
    let fromNode (name: string) (node: Node) : ConfigSource =
        create name (fun () -> Flatten.node name node)

    /// A parsed document (positions and number text are used for provenance and values).
    let fromParsed (name: string) (parsed: Parsed) : ConfigSource =
        create name (fun () -> Flatten.parsed name parsed)

    /// A record encoded with an explicit encoder (see `Encode`), as one layer.
    let ofRecord (name: string) (encoder: 'a -> Encode.Pairs) (value: 'a) : ConfigSource =
        create name (fun () -> ofPairs name (encoder value))

    let private snippetAt (text: string) (line: int) : string =
        if line < 1 then
            ""
        else
            let lines = text.Replace("\r\n", "\n").Split('\n')

            if line > lines.Length then
                ""
            else
                let l = lines.[line - 1].TrimEnd()
                if l.Length > 120 then l.Substring(0, 120) + "..." else l

    let private mapFnodeError (name: string) (text: string) (e: Fnode.Error) : ConfigError =
        let line, column =
            match e.Location with
            | Some span -> span.Start.Line, span.Start.Column
            | None -> 0, 0

        if
            e.Code = ErrorCode.typeMismatch
            && e.Message.StartsWith "Mapping key must be a string"
        then
            NonStringKey(name, line, column, e.Message)
        else
            ParseError(name, line, column, e.Message, snippetAt text line)

    /// File text parsed by a registered `Format` (`Fnode.Json.Json.format`, `Fnode.Yaml.format`, ...).
    let fromText (name: string) (format: Format) (text: string) : ConfigSource =
        create name (fun () ->
            match format.Parse text with
            | Ok parsed -> Flatten.parsed name parsed
            | Error es -> Error(es |> List.map (mapFnodeError name text)))

    /// A file that could not be read: loading yields `FileNotFound path`.
    let fileNotFound (path: string) : ConfigSource =
        create path (fun () -> Error [ FileNotFound path ])

    /// A file with unknown extension: loading yields `UnknownFormat path`.
    let unknownFormat (path: string) : ConfigSource =
        create path (fun () -> Error [ UnknownFormat path ])

    /// A missing file is skipped silently (no entries) instead of failing the build.
    let optional (source: ConfigSource) : ConfigSource =
        create source.Name (fun () ->
            match source.Load() with
            | Error errors ->
                match
                    errors
                    |> List.filter (function
                        | FileNotFound _ -> false
                        | _ -> true)
                with
                | [] -> Ok []
                | rest -> Error rest
            | ok -> ok)

    /// Environment variables (name, value pairs), see `Env`.
    let fromEnv (name: string) (options: EnvOptions) (vars: seq<string * string>) : ConfigSource =
        create name (fun () -> Ok(Env.entries options vars))

    /// CLI arguments, see `Args`. Positional arguments are ignored here; use `Args.parse` to get them.
    let fromArgs (name: string) (options: ArgsOptions) (args: string list) : ConfigSource =
        create name (fun () -> Args.parse options args |> Result.map (fun r -> r.Entries))
