/// One example, shared by the .NET, Node and Python entry points: layered YAML + environment variables +
/// command-line arguments, printed with `explain`.
///
/// Run from the repository root:
///   FNEX_ENVIRONMENT=Production FNEX__Db__Port=9999 <run> -- --Db:Host=cli -d
module Example

open Foncig

type Settings =
    { Host: string
      Port: int
      Timeout: System.TimeSpan
      Tags: string list
      Debug: bool
      Version: string }

let private defaults: Settings =
    { Host = "unset"
      Port = 0
      Timeout = System.TimeSpan.FromSeconds 5.0
      Tags = []
      Debug = false
      Version = "0" }

/// The record as the lowest layer: explicit, no reflection.
let private encode (s: Settings) : Encode.Pairs =
    List.concat
        [ Encode.string "Db:Host" s.Host
          Encode.int "Db:Port" s.Port
          Encode.timeSpan "Db:Timeout" s.Timeout
          Encode.list "Tags" s.Tags
          Encode.bool "Debug" s.Debug
          Encode.string "Version" s.Version ]

let private decoder: Decoder<Settings> =
    config {
        let! host = Decode.required "Db:Host"
        and! port = Decode.optional "Db:Port" 5432
        and! timeout = Decode.requiredWith Readers.timeSpan "Db:Timeout"
        and! tags = Decode.list "Tags"
        and! debug = Decode.optional "Debug" false
        and! version = Decode.required "Version"

        return
            { Host = host
              Port = port
              Timeout = timeout
              Tags = tags
              Debug = debug
              Version = version }
    }

let run () : int =
    let dir = Host.pathJoin [ Host.cwd (); "examples"; "config" ]

    let options =
        { Defaults.options "fnex" with
            Directory = Some dir
            SwitchMappings = [ "-d", "Debug" ] }

    let builder =
        Defaults.builder options
        |> ConfigBuilder.addFirst (ConfigSource.ofRecord "defaults" encode defaults)
        |> ConfigBuilder.withListPolicy Replace

    match ConfigBuilder.build builder with
    | Error errors ->
        for e in errors do
            printfn "error: %s" (ConfigError.describe e)

        1
    | Ok cfg ->
        match Decode.run decoder cfg with
        | Error errors ->
            for e in errors do
                printfn "error: %s" (ConfigError.describe e)

            1
        | Ok s ->
            printfn
                "Host=%s Port=%d Timeout=%s Debug=%b Version=%s"
                s.Host
                s.Port
                (Scalar.formatTimeSpan s.Timeout)
                s.Debug
                s.Version

            printfn "Tags=%s" (String.concat "," s.Tags)
            printfn ""

            for key in [ "Db:Host"; "Db:Port"; "Tags:0"; "Debug"; "Version" ] do
                match Config.explain cfg key with
                | Some x -> printfn "%s\n" (Explanation.describe x)
                | None -> printfn "%s: not set\n" key

            0
