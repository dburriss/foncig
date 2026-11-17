namespace Foncig

open Fnode

/// Options for the conventional layout used by `Defaults`.
type DefaultsOptions =
    {
        /// Names the user config directory (`~/.appname/`) and the default environment variable prefix.
        AppName: string
        /// Base file name without extension. Default `appsettings`.
        BaseName: string
        /// Directory holding the base files. `None` means the current directory.
        Directory: string option
        /// Environment variables that name the environment (`Production`, ...), first one set wins.
        /// Default: `<APPNAME>_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT`.
        EnvironmentVariables: string list
        /// Prefix for configuration environment variables. Default `<APPNAME>_`.
        EnvPrefix: string
        /// Switch mappings for the command line.
        SwitchMappings: (string * string) list
        /// Where environment variables and arguments come from. Default: the real process (see `Host`).
        Env: unit -> seq<string * string>
        Args: unit -> string list
    }

/// `Defaults.configure "myapp"`: the usual layers in one call.
///
/// Order, later overriding earlier (every file is optional; for each location `.json`, `.yaml`, `.yml` in that order):
/// 1. `appsettings.{json|yaml|yml}`
/// 2. `appsettings.{ENV}.{json|yaml|yml}` (when an environment is named)
/// 3. `~/.appname/config.{json|yaml|yml}`
/// 4. environment variables with the prefix (`MYAPP_`, so `MYAPP__Db__Host` is `Db:Host`)
/// 5. command-line arguments
module Defaults =

    /// Json and Yaml.
    let formats: Format list = [ JsonSource.format; YamlSource.format ]

    let private sanitize (appName: string) =
        appName.ToUpperInvariant()
        |> String.map (fun c -> if System.Char.IsLetterOrDigit c then c else '_')

    let options (appName: string) : DefaultsOptions =
        let upper = sanitize appName

        { AppName = appName
          BaseName = "appsettings"
          Directory = None
          EnvironmentVariables = [ upper + "_ENVIRONMENT"; "DOTNET_ENVIRONMENT"; "ASPNETCORE_ENVIRONMENT" ]
          EnvPrefix = upper + "_"
          SwitchMappings = []
          Env = Host.env
          Args = Host.args }

    let private extensions = [ ".json"; ".yaml"; ".yml" ]

    /// The sources, in order.
    let sources (o: DefaultsOptions) : ConfigSource list =
        let dir = defaultArg o.Directory (Host.cwd ())

        let environment =
            o.EnvironmentVariables
            |> List.tryPick (fun name -> o.Env() |> Seq.tryFind (fun (k, v) -> k = name && v <> "") |> Option.map snd)

        let files (basePath: string) =
            extensions
            |> List.map (fun ext -> HostSources.file formats false (basePath + ext))

        let baseFiles = files (Host.pathJoin [ dir; o.BaseName ])

        let envFiles =
            match environment with
            | Some e -> files (Host.pathJoin [ dir; o.BaseName + "." + e ])
            | None -> []

        let userFiles =
            files (Host.pathJoin [ "~"; "." + o.AppName.ToLowerInvariant(); "config" ])

        let envSource =
            ConfigSource.create "env" (fun () ->
                (ConfigSource.fromEnv "env" (EnvOptions.withPrefix o.EnvPrefix) (o.Env())).Load())

        let argsSource =
            ConfigSource.create "args" (fun () ->
                (ConfigSource.fromArgs "args" { SwitchMappings = o.SwitchMappings } (o.Args())).Load())

        baseFiles @ envFiles @ userFiles @ [ envSource; argsSource ]

    /// A builder with those sources (add more, or set list policies, before `ConfigBuilder.build`).
    let builder (o: DefaultsOptions) : ConfigBuilder =
        ConfigBuilder.empty |> ConfigBuilder.addMany (sources o)

    /// `builder (options appName)`.
    let configure (appName: string) : ConfigBuilder = builder (options appName)

    /// Build straight away.
    let build (appName: string) : Result<Config, ConfigError list> = ConfigBuilder.build (configure appName)
