# Getting started

In this tutorial you build a small program that reads settings from a YAML file and the command line, then asks
Foncig where one value came from. It takes about ten minutes and runs on .NET.

## 1. Create the project

```bash
dotnet new console -lang F# -o hello-foncig
cd hello-foncig
dotnet add package Foncig.Defaults
```

## 2. Add a configuration file

Create `appsettings.yaml` next to `Program.fs` (you run the program from this folder):

```yaml
Db:
  Host: localhost
  Port: 5432
Tags: [web, api]
```

## 3. Describe your settings

Replace `Program.fs` with:

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

[<EntryPoint>]
let main _ =
    match ConfigBuilder.build (Defaults.configure "hello") with
    | Error errors ->
        errors |> List.iter (ConfigError.describe >> printfn "%s")
        1
    | Ok cfg ->
        match Decode.run settings cfg with
        | Error errors ->
            errors |> List.iter (ConfigError.describe >> printfn "%s")
            1
        | Ok s ->
            printfn "%A" s
            Config.explain cfg "Db:Host" |> Option.iter (Explanation.describe >> printfn "%s")
            0
```

## 4. Run it

```bash
dotnet run
```

You see the settings from the file, followed by where `Db:Host` came from:

```
Db:Host = "localhost"  <- appsettings.yaml:2:9
```

## 5. Override from the command line

```bash
dotnet run -- --Db:Host=cli
```

`Db:Host` is now `cli`, and the explanation lists both the file value and the command-line value that replaced it.

## 6. Override from an environment variable

```bash
HELLO__Db__Port=9999 dotnet run
```

`Port` is `9999`. Environment variables beat the file; the command line beats both.

## What you built

A program that layers a file, environment variables and the command line, decodes them into a typed record, and
reports every problem at once instead of the first one. Next:

- [Control how lists merge](../how-to/control-list-merging.md)
- [Use Foncig with Fable](../how-to/use-with-fable.md)
- [Concepts and API reference](../reference/concepts.md)
