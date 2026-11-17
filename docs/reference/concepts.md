# Concepts and API reference

## Packages

| Package | Contents | Depends on |
|---|---|---|
| `Foncig.Core` | Keys, flattening, provenance, sources, merging, typed access, decoder, encoder | `Fnode.Core` |
| `Foncig.Host` | Env, argv, file read, home/cwd, paths; file sources by extension | `Foncig.Core` |
| `Foncig.Json` | JSON file format | `Foncig.Core`, `Fnode.Json` |
| `Foncig.Yaml` | YAML file format | `Foncig.Core`, `Fnode.Yaml` |
| `Foncig.Defaults` | `Defaults.configure "app"`: the usual layers in one call | all of the above |

## Keys

| Rule | Detail |
|---|---|
| Separator | `:` (`Db:Host`, `Servers:0:Name`) |
| Case | Insensitive; the first writer's spelling is kept |
| Segments | Cannot contain `:` or be empty. Rejected with a position; there is no escape syntax |

## Layers and builder

| Function | Purpose |
|---|---|
| `ConfigBuilder.empty` / `add` / `addFirst` / `build` | Compose layers; later wins |
| `ConfigBuilder.addSet "Db:Port" "1"` | One override |
| `ConfigBuilder.withListPolicy` / `withListPolicyFor prefix` | List merge policy: `Replace` (default), `MergeByIndex`, `Append` |
| `ConfigSource.optional` | Skip a missing file (a required missing file is `FileNotFound path`) |
| `ConfigSource.inMemory` | In-memory pairs |
| `ConfigSource.fromNode` | Any `Fnode.Node` |
| `ConfigSource.ofRecord name encoder value` | A record, via an explicit encoder |

`null` is "unset": it clears earlier layers.

## Default layers (`Defaults.configure "app"`)

Lowest first: `appsettings.yaml`, `appsettings.{ENV}.yaml`, `~/.app/config.yaml`, `APP__*` environment variables,
command line. `ENV` comes from the first set of `<APP>_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT`.

## Environment source (`FromEnv`)

| Rule | Example |
|---|---|
| Prefix `APP_` is stripped (case-insensitive) | `APP_X` is `X` |
| `__` becomes `:` | `APP__Db__Host` is `Db:Host` |

## Command-line source

| Form | Meaning |
|---|---|
| `--key value`, `--key=value`, `--key:sub value`, `/key=value` | Set `key` |
| bare `--flag` | `"true"` |
| `-h` with a switch mapping | Mapped key (`-h` to `Db:Host`); unmapped single-dash is `UnknownSwitch` |
| `--` | Ends switches; the rest are positionals |

A next token is the value unless it starts with `-` (negative numbers are values).

## Typed access

Invariant-culture parsing: `Config.tryInt`, `tryBool`, `tryGuid`, `tryTimeSpan`, ...; `Config.list`, `Config.map`,
`Config.section`. `bool` accepts `true`/`false` (any case) only, not `1`/`0`.

## Decoder and encoder

| Function | Purpose |
|---|---|
| `config { let! ... and! ... }` | Build a `Decoder<'a>`; `and!` accumulates all errors, `let!` short-circuits |
| `Decode.required` / `optional key default` / `list` | Read a key |
| `Decode.requiredWith` / `optionalWith reader` | Use an explicit reader (`Readers.timeSpan`, `Readers.uri`, ...) |
| `Decode.run decoder cfg` | `Result<'a, ConfigError list>` |
| `Encode.string`/`int`/`bool`/`timeSpan`/`list` | Build pairs for `ConfigSource.ofRecord` |

## Provenance

`Config.explain cfg key` returns the winning value and the whole override chain. Each entry's provenance is the source
name plus `file:line:col`, the env var name or the CLI token.

## Errors

Nothing throws across the public API. `ConfigError.describe` renders text with source and position.

| Case | Meaning |
|---|---|
| `FileNotFound path` | Required file missing |
| `ParseError(file, line, col, message, snippet)` | Syntax error |
| `InvalidKey` / `NonStringKey` | Bad key |
| `TypeMismatch(key, expected, actual, provenance)` | Value cannot be read as the requested type |
| `Missing` | Required key absent |
| `UnknownSwitch(token, index)` | Unmapped single-dash switch |
| `UnknownFormat` | No format for the file extension |
| `Aggregate` | Several errors together |
