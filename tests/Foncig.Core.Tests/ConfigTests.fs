module ConfigTests

open System
open Xunit
open Fnode
open Foncig

let private cfgOf (pairs: (string * string) list) =
    ConfigBuilder.empty
    |> ConfigBuilder.add (ConfigSource.inMemory "t" pairs)
    |> ConfigBuilder.build
    |> Result.defaultWith (fun e -> failwithf "%A" e)

[<Fact>]
let ``tryGet get require`` () =
    let c = cfgOf [ "A", "1" ]
    Assert.Equal(Some "1", Config.tryGet c "a")
    Assert.Equal("1", Config.get c "A" "z")
    Assert.Equal("z", Config.get c "B" "z")
    Assert.Equal(Ok "1", Config.require c "a")
    Assert.Equal(Error(Missing "B"), Config.require c "B")

[<Fact>]
let ``typed access`` () =
    let g = "d3b07384-d9a0-4c3e-a2f6-0a1b2c3d4e5f"

    let c =
        cfgOf
            [ "i", "42"
              "l", "9223372036854775807"
              "f", "0.1"
              "b", "TRUE"
              "g", g
              "u", "https://example.com/x"
              "t", "00:01:30"
              "d", "2026-10-14T01:02:03Z" ]

    Assert.Equal(Ok(Some 42), Config.tryInt c "i")
    Assert.Equal(Ok(Some 9223372036854775807L), Config.tryInt64 c "l")
    Assert.Equal(Ok(Some 0.1), Config.tryFloat c "f")
    Assert.Equal(Ok(Some true), Config.tryBool c "b")
    Assert.Equal(Ok(Some(Guid.Parse g)), Config.tryGuid c "g")
    Assert.Equal(Ok(Some(TimeSpan(0, 0, 1, 30, 0))), Config.tryTimeSpan c "t")
    Assert.Equal(Ok(Some(DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.Zero))), Config.tryDateTimeOffset c "d")
    Assert.Equal(Ok None, Config.tryInt c "missing")

    match Config.tryUri c "u" with
    | Ok(Some u) -> Assert.Equal("example.com", u.Host)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``type mismatch carries key, expected, actual and provenance`` () =
    let c = cfgOf [ "Db:Port", "abc" ]

    match Config.tryInt c "db:port" with
    | Error(TypeMismatch(key, expected, actual, Some origin)) ->
        Assert.Equal("Db:Port", key)
        Assert.Equal("int", expected)
        Assert.Equal("abc", actual)
        Assert.Equal("t", origin.Source)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``bool rejects 1 and 0`` () =
    let c = cfgOf [ "a", "1" ]
    Assert.True(Result.isError (Config.tryBool c "a"))

[<Fact>]
let ``section views`` () =
    let c = cfgOf [ "Db:Host", "h"; "Db:Port", "1"; "Db:Opts:X", "y"; "Other", "o" ]
    let db = Config.section c "Db"
    Assert.Equal(Some "h", Config.tryGet db "host")
    Assert.Equal(None, Config.tryGet db "Other")
    Assert.Equal<string list>([ "Host"; "Port"; "Opts" ], Config.children db)
    Assert.Equal(Some "y", Config.tryGet (Config.section db "opts") "x")
    Assert.Equal<string list>([ "Db"; "Other" ], Config.children c)
    Assert.Equal(Error(Missing "Db:Nope"), Config.require db "Nope")

[<Fact>]
let ``lists and maps`` () =
    let c =
        cfgOf
            [ "Tags:2", "c"
              "Tags:0", "a"
              "Tags:1", "b"
              "Env:A", "1"
              "Env:B", "2"
              "Env:Deep:X", "3" ]

    Assert.Equal(Ok [ "a"; "b"; "c" ], Config.list c "tags")
    Assert.Equal(Ok [], Config.list c "nothing")
    Assert.Equal<Map<string, string>>(Map.ofList [ "A", "1"; "B", "2" ], Config.map c "env")

[<Fact>]
let ``list numeric order is numeric not lexical`` () =
    let c = cfgOf [ for i in 0..11 -> "L:" + string i, string i ]
    Assert.Equal(Ok [ for i in 0..11 -> string i ], Config.list c "L")

[<Fact>]
let ``a nested list element is a type mismatch`` () =
    let c = cfgOf [ "L:0:x", "1" ]
    Assert.True(Result.isError (Config.list c "L"))

// ---- decoder ----------------------------------------------------------------------------------------------

type Settings =
    { Host: string
      Port: int
      Tags: string list
      Debug: bool }

let private settings: Decoder<Settings> =
    config {
        let! host = Decode.required "Db:Host"
        and! port = Decode.optional "Db:Port" 5432
        and! tags = Decode.list "Tags"
        and! debug = Decode.optional "Debug" false

        return
            { Host = host
              Port = port
              Tags = tags
              Debug = debug }
    }

[<Fact>]
let ``decoder builds a record`` () =
    let c = cfgOf [ "Db:Host", "h"; "Tags:0", "a"; "Tags:1", "b" ]

    Assert.Equal(
        Ok
            { Host = "h"
              Port = 5432
              Tags = [ "a"; "b" ]
              Debug = false },
        settings c
    )

    let c2 = cfgOf [ "Db:Host", "h"; "Db:Port", "1"; "Debug", "true" ]

    Assert.Equal(
        Ok
            { Host = "h"
              Port = 1
              Tags = []
              Debug = true },
        Decode.run settings c2
    )

[<Fact>]
let ``decoder accumulates all errors`` () =
    let c = cfgOf [ "Db:Port", "x"; "Debug", "maybe" ]

    match settings c with
    | Error errors ->
        Assert.Equal(3, errors.Length)

        Assert.True(
            errors
            |> List.exists (function
                | Missing "Db:Host" -> true
                | _ -> false)
        )

        Assert.True(
            errors
            |> List.exists (function
                | TypeMismatch("Db:Port", _, "x", _) -> true
                | _ -> false)
        )

        Assert.True(
            errors
            |> List.exists (function
                | TypeMismatch("Debug", _, "maybe", _) -> true
                | _ -> false)
        )
    | Ok _ -> failwith "expected errors"

[<Fact>]
let ``decoder section roots keys`` () =
    let d =
        Decode.section
            "Db"
            (config {
                let! h = Decode.required "Host"
                return h
            })

    Assert.Equal(Ok "h", d (cfgOf [ "Db:Host", "h" ]))

// ---- encoder / record source ------------------------------------------------------------------------------

let private encodeSettings (s: Settings) : Encode.Pairs =
    List.concat
        [ Encode.string "Db:Host" s.Host
          Encode.int "Db:Port" s.Port
          Encode.list "Tags" s.Tags
          Encode.bool "Debug" s.Debug ]

[<Fact>]
let ``a record as a source round-trips through the decoder`` () =
    let s =
        { Host = "h"
          Port = 7
          Tags = [ "x"; "y" ]
          Debug = true }

    let cfg =
        ConfigBuilder.empty
        |> ConfigBuilder.add (ConfigSource.ofRecord "defaults" encodeSettings s)
        |> ConfigBuilder.build
        |> Result.defaultWith (fun e -> failwithf "%A" e)

    Assert.Equal(Ok s, settings cfg)

[<Fact>]
let ``a record can be the lowest layer and be overridden`` () =
    let s =
        { Host = "h"
          Port = 7
          Tags = [ "x" ]
          Debug = false }

    let cfg =
        ConfigBuilder.empty
        |> ConfigBuilder.add (ConfigSource.ofRecord "defaults" encodeSettings s)
        |> ConfigBuilder.add (ConfigSource.inMemory "override" [ "Db:Port", "99"; "Tags:0", "z" ])
        |> ConfigBuilder.build
        |> Result.defaultWith (fun e -> failwithf "%A" e)

    Assert.Equal(Ok { s with Port = 99; Tags = [ "z" ] }, settings cfg)
    Assert.Equal("override", (Config.explain cfg "Db:Port").Value.Winner.Source)

[<Fact>]
let ``encoded optional and unset`` () =
    Assert.Equal<Encode.Pairs>([], Encode.option Encode.string "k" None)
    Assert.Equal<Encode.Pairs>([ "k", Text "v" ], Encode.option Encode.string "k" (Some "v"))
    Assert.Equal<Encode.Pairs>([ "k", Unset ], Encode.unset "k")
    Assert.Equal<Encode.Pairs>([ "k", EmptyList ], Encode.list "k" [])

[<Fact>]
let ``encoded typed values read back`` () =
    let t = TimeSpan(1, 2, 3, 4, 500)
    let d = DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan(2, 0, 0))
    let g = Guid.Parse "d3b07384-d9a0-4c3e-a2f6-0a1b2c3d4e5f"

    let pairs =
        List.concat
            [ Encode.float "f" 0.1
              Encode.int64 "l" 9007199254740993L
              Encode.timeSpan "t" t
              Encode.dateTimeOffset "d" d
              Encode.guid "g" g ]

    let cfg =
        ConfigBuilder.empty
        |> ConfigBuilder.add (ConfigSource.inMemoryEntries "p" pairs)
        |> ConfigBuilder.build
        |> Result.defaultWith (fun e -> failwithf "%A" e)

    Assert.Equal(Ok(Some 0.1), Config.tryFloat cfg "f")
    Assert.Equal(Ok(Some 9007199254740993L), Config.tryInt64 cfg "l")
    Assert.Equal(Ok(Some t), Config.tryTimeSpan cfg "t")
    Assert.Equal(Ok(Some d), Config.tryDateTimeOffset cfg "d")
    Assert.Equal(Ok(Some g), Config.tryGuid cfg "g")

[<Fact>]
let ``error messages name the source and position`` () =
    let e = ParseError("a.yaml", 3, 5, "bad indent", "  x: [")
    let m = ConfigError.describe e
    Assert.Contains("a.yaml:3:5", m)
    Assert.Contains("bad indent", m)
    Assert.Contains("File not found: x.json", ConfigError.describe (FileNotFound "x.json"))
    Assert.Contains("-x", ConfigError.describe (UnknownSwitch("-x", 4)))
    Assert.Equal(2, (ConfigError.flatten (Aggregate [ Missing "a"; Aggregate [ Missing "b" ] ])).Length)

[<Fact>]
let ``fromText maps parser errors with position and snippet`` () =
    let format: Format =
        { Name = "fake"
          Extensions = [ ".fake" ]
          Parse =
            fun _ ->
                Error
                    [ { Error.create ErrorCode.parse "boom" [] with
                          Location =
                              Some
                                  { Start = { Line = 2; Column = 4 }
                                    End = { Line = 2; Column = 4 } } } ]
          Print = fun _ -> "" }

    match (ConfigSource.fromText "f.fake" format "line1\nline two").Load() with
    | Error [ ParseError("f.fake", 2, 4, "boom", "line two") ] -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``fromText maps a non string key error`` () =
    let format: Format =
        { Name = "fake"
          Extensions = [ ".fake" ]
          Parse =
            fun _ ->
                Error
                    [ { Error.create ErrorCode.typeMismatch "Mapping key must be a string, found 1" [] with
                          Location =
                              Some
                                  { Start = { Line = 1; Column = 1 }
                                    End = { Line = 1; Column = 1 } } } ]
          Print = fun _ -> "" }

    match (ConfigSource.fromText "f.fake" format "1: x").Load() with
    | Error [ NonStringKey("f.fake", 1, 1, _) ] -> ()
    | other -> failwithf "unexpected %A" other
