namespace Foncig

/// Options for reading environment variables.
type EnvOptions =
    {
        /// Only variables starting with this (case-insensitive) are read; it is stripped. Include the trailing
        /// underscore: `APP_`. Empty means every variable.
        Prefix: string
        /// Replaced by `:`. Default `__`.
        Separator: string
    }

module EnvOptions =
    let defaults = { Prefix = ""; Separator = "__" }
    let withPrefix (prefix: string) = { defaults with Prefix = prefix }

/// Environment variables to entries.
///
/// `APP__Db__Host=x` with prefix `APP_` becomes `Db:Host = x`; leading underscores left after the prefix are
/// dropped. List items are numeric segments (`APP__Servers__0`). Names are case-insensitive. Two variables that
/// differ only by case resolve by input order: the later one wins. A variable whose stripped name is empty or has
/// an empty segment (`APP__`, `A____B`) is ignored, as is any segment containing `:`.
module Env =

    let entries (options: EnvOptions) (vars: seq<string * string>) : Entry list =
        let prefix = options.Prefix.ToLowerInvariant()

        [ for (name, value) in vars do
              if
                  prefix = ""
                  || name.ToLowerInvariant().StartsWith(prefix, System.StringComparison.Ordinal)
              then
                  let rest = name.Substring(options.Prefix.Length).TrimStart('_')

                  let key =
                      if options.Separator = "" then
                          rest
                      else
                          rest.Replace(options.Separator, ConfigKey.Separator)

                  match ConfigKey.validate key with
                  | Ok k ->
                      yield
                          { Key = k
                            Value = Text value
                            Origin = { Source = "env"; Detail = Some name } }
                  | Error _ -> () ]
