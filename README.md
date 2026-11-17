# Foncig

Layered configuration for F# that behaves the same on **.NET**, **Fable JavaScript** (Node, Bun, Deno) and
**Fable Python**. It follows the model of `Microsoft.Extensions.Configuration`: flat colon-separated keys
(`Db:Host`, `Servers:0:Name`), case-insensitive, last source wins. Built on [Fnode](https://github.com/dburriss/fnode).

```fsharp
open Foncig

type Settings = { Host: string; Port: int; Tags: string list }

let settings: Decoder<Settings> =
    config {
        let! host = Decode.required "Db:Host"
        and! port = Decode.optional "Db:Port" 5432
        and! tags = Decode.list "Tags"
        return { Host = host; Port = port; Tags = tags }
    }

// appsettings.yaml, appsettings.{ENV}.yaml, ~/.myapp/config.yaml, MYAPP__* variables, command line
match ConfigBuilder.build (Defaults.configure "myapp") with
| Error errors -> errors |> List.iter (ConfigError.describe >> printfn "%s")
| Ok cfg ->
    match Decode.run settings cfg with        // Result<Settings, ConfigError list>, all errors at once
    | Ok s -> printfn "%A" s
    | Error errors -> errors |> List.iter (ConfigError.describe >> printfn "%s")
    Config.explain cfg "Db:Host" |> Option.iter (Explanation.describe >> printfn "%s")
```

`explain` shows where a value came from and everything it overrode:

```
Db:Host = "cli"  <- args (--Db:Host=cli)
  1. "localhost"  appsettings.yaml:3:9
  2. "db.prod.internal"  appsettings.Production.yaml:3:9
  3. "cli"  args (--Db:Host=cli)
```

## Packages

| Package | What it is | Depends on |
|---|---|---|
| `Foncig.Core` | The pure core: keys, flattening, provenance, sources, merging, typed access, decoder, encoder | `Fnode.Core` |
| `Foncig.Host` | Platform shims: env, argv, file read, home/cwd, paths; file sources by extension | `Foncig.Core` |
| `Foncig.Json` | JSON file format | `Foncig.Core`, `Fnode.Json` |
| `Foncig.Yaml` | YAML file format | `Foncig.Core`, `Fnode.Yaml` |
| `Foncig.Defaults` | `Defaults.configure "app"`: the usual layers in one call | all of the above |

You pay only for what you use. A TOML adapter later is one more package that supplies a `Fnode.Format`; the core does
not change.

## Documentation

- **Learn:** [Getting started](docs/tutorials/getting-started.md)
- **Do:** [Control list merging](docs/how-to/control-list-merging.md),
  [Trace where a value came from](docs/how-to/trace-a-value.md), [Use with Fable](docs/how-to/use-with-fable.md)
- **Look up:** [Concepts and API reference](docs/reference/concepts.md),
  [Fable target differences](docs/reference/fable-targets.md)
- **Understand:** [Design decisions](docs/explanation/design-decisions.md)
- **Run:** [examples/](examples/README.md) for each target

## Build and test

```bash
dotnet test Foncig.slnx          # .NET
scripts/fable-test.sh js               # Fable JS (bun or node)
scripts/fable-test.sh python           # Fable Python (3.12+)
```

The same test files run on all three targets (xUnit and FsCheck on .NET; small shims under Fable).
