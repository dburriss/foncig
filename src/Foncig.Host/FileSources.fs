namespace Foncig

open Fnode

/// File, environment and argument sources wired to the real platform (through `Host`).
/// Reading happens when the configuration is built, not when the source is created.
module HostSources =

    /// The lower-case extension including the dot (`.yaml`), or "".
    let extensionOf (path: string) : string =
        let name = path.Substring((max (path.LastIndexOf '/') (path.LastIndexOf '\\')) + 1)
        let i = name.LastIndexOf '.'
        if i <= 0 then "" else name.Substring(i).ToLowerInvariant()

    /// The registered format for the path's extension.
    let formatFor (formats: Format list) (path: string) : Format option =
        let ext = extensionOf path

        formats
        |> List.tryFind (fun f -> f.Extensions |> List.exists (fun e -> e.ToLowerInvariant() = ext))

    /// A file source. The format comes from the extension (`.json`, `.yaml`, `.yml`, ... as registered in
    /// `formats`); an unknown extension is `UnknownFormat`. A missing file is `FileNotFound path` when
    /// `required`, and skipped silently otherwise. `~/` is expanded.
    let file (formats: Format list) (required: bool) (path: string) : ConfigSource =
        let expanded = Host.expandHome path

        ConfigSource.create expanded (fun () ->
            match formatFor formats expanded with
            | None -> (ConfigSource.unknownFormat expanded).Load()
            | Some format ->
                match Host.tryReadFile expanded with
                | Some text -> (ConfigSource.fromText expanded format text).Load()
                | None ->
                    let missing = ConfigSource.fileNotFound expanded
                    (if required then missing else ConfigSource.optional missing).Load())

    /// The process environment variables.
    let env (options: EnvOptions) : ConfigSource =
        ConfigSource.create "env" (fun () -> (ConfigSource.fromEnv "env" options (Host.env ())).Load())

    /// The process command-line arguments.
    let args (options: ArgsOptions) : ConfigSource =
        ConfigSource.create "args" (fun () -> (ConfigSource.fromArgs "args" options (Host.args ())).Load())

    /// The positional arguments (after `--`, and tokens that are neither switches nor values).
    let positionals (options: ArgsOptions) : Result<string list, ConfigError list> =
        Args.parse options (Host.args ()) |> Result.map (fun r -> r.Positionals)

    /// Opt-in: the first `fileNames` entry found searching up from the current directory, as an optional file source.
    let searchUp (formats: Format list) (fileNames: string list) : ConfigSource option =
        Host.searchUpward fileNames (Host.cwd ()) |> Option.map (file formats true)
