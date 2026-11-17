module HostTests

open Xunit
open Foncig

let private join (parts: string list) = Host.pathJoin parts

[<Fact>]
let ``pathJoin parentDir`` () =
    Assert.Equal("a/b/c", join [ "a"; "b"; "c" ])
    Assert.Equal("a/b", join [ "a/"; "/b" ])
    Assert.Equal("/a/b", join [ "/a"; "b" ])
    Assert.Equal("b", join [ ""; "b" ])
    Assert.Equal(Some "/a/b", Host.parentDir "/a/b/c")
    Assert.Equal(Some "/", Host.parentDir "/a")
    Assert.Equal(None, Host.parentDir "/")
    Assert.Equal(None, Host.parentDir "name")

[<Fact>]
let ``cwd and homeDir are known`` () =
    Assert.True(Host.cwd () <> "")
    Assert.True((Host.homeDir ()).IsSome)

[<Fact>]
let ``expandHome`` () =
    let home = (Host.homeDir ()).Value
    Assert.Equal(join [ home; "x/y" ], Host.expandHome "~/x/y")
    Assert.Equal(home, Host.expandHome "~")
    Assert.Equal("/abs/x", Host.expandHome "/abs/x")
    Assert.Equal("~other/x", Host.expandHome "~other/x")

[<Fact>]
let ``env reads real variables`` () =
    Platform.setEnv "FNCONFIG_HOST_TEST" "hello"
    Assert.Equal(Some "hello", Host.tryEnv "FNCONFIG_HOST_TEST")

    Assert.True(
        Host.env ()
        |> Seq.exists (fun (k, v) -> k = "FNCONFIG_HOST_TEST" && v = "hello")
    )

    Assert.Equal(None, Host.tryEnv "FNCONFIG_DOES_NOT_EXIST_XYZ")

[<Fact>]
let ``args returns a list without throwing`` () = Assert.True((Host.args ()).Length >= 0)

[<Fact>]
let ``tryReadFile missing is None, existing is read, BOM is stripped`` () =
    let dir = Platform.tempDir ()
    Assert.Equal(None, Host.tryReadFile (join [ dir; "nope.txt" ]))
    Platform.writeFile (join [ dir; "a.txt" ]) "hello é world\nline2"
    Assert.Equal(Some "hello é world\nline2", Host.tryReadFile (join [ dir; "a.txt" ]))
    Platform.writeFile (join [ dir; "bom.txt" ]) "﻿with bom"
    Assert.Equal(Some "with bom", Host.tryReadFile (join [ dir; "bom.txt" ]))

[<Fact>]
let ``searchUpward finds the nearest file walking up`` () =
    let dir = Platform.tempDir ()
    Platform.writeFile (join [ dir; "config.yaml" ]) "a: 1"
    Platform.writeFile (join [ dir; "x"; "y"; "z"; "keep.txt" ]) "k"
    let found = Host.searchUpward [ "config.yaml" ] (join [ dir; "x"; "y"; "z" ])
    Assert.Equal(Some(join [ dir; "config.yaml" ]), found)
    Assert.Equal(None, Host.searchUpward [ "no-such-file-xyz.yaml" ] (join [ dir; "x" ]))

let private formats = Defaults.formats

let private build (sources: ConfigSource list) =
    ConfigBuilder.empty |> ConfigBuilder.addMany sources |> ConfigBuilder.build

[<Fact>]
let ``format is chosen by extension`` () =
    let dir = Platform.tempDir ()
    Platform.writeFile (join [ dir; "a.json" ]) """{ "Db": { "Host": "j" } }"""
    Platform.writeFile (join [ dir; "b.yaml" ]) "Db:\n  Port: 5432\n"
    Platform.writeFile (join [ dir; "c.YML" ]) "Db:\n  User: u\n"

    let cfg =
        build [ for f in [ "a.json"; "b.yaml"; "c.YML" ] -> HostSources.file formats true (join [ dir; f ]) ]
        |> Result.defaultWith (fun e -> failwithf "%A" e)

    Assert.Equal(Some "j", Config.tryGet cfg "Db:Host")
    Assert.Equal(Some "5432", Config.tryGet cfg "Db:Port")
    Assert.Equal(Some "u", Config.tryGet cfg "Db:User")

[<Fact>]
let ``unknown extension is an error`` () =
    let dir = Platform.tempDir ()
    Platform.writeFile (join [ dir; "a.toml" ]) "a = 1"

    match build [ HostSources.file formats true (join [ dir; "a.toml" ]) ] with
    | Error [ UnknownFormat p ] -> Assert.Contains("a.toml", p)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``missing optional is skipped and missing required names the path`` () =
    let dir = Platform.tempDir ()
    let p = join [ dir; "missing.yaml" ]
    Assert.True(Result.isOk (build [ HostSources.file formats false p ]))

    match build [ HostSources.file formats true p ] with
    | Error [ FileNotFound path ] -> Assert.Equal(p, path)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``yaml provenance has file line and column`` () =
    let dir = Platform.tempDir ()
    let p = join [ dir; "a.yaml" ]
    Platform.writeFile p "Db:\n  Host: h\n  Port: 1\n"

    let cfg =
        build [ HostSources.file formats true p ]
        |> Result.defaultWith (fun e -> failwithf "%A" e)

    match Config.explain cfg "Db:Port" with
    | Some x -> Assert.Equal(Some(p + ":3:9"), x.Winner.Detail)
    | None -> failwith "expected explanation"

[<Fact>]
let ``json parse error carries position and snippet`` () =
    let dir = Platform.tempDir ()
    let p = join [ dir; "bad.json" ]
    Platform.writeFile p "{\n  \"a\": 1,\n  \"b\": oops\n}"

    match build [ HostSources.file formats true p ] with
    | Error [ ParseError(file, line, col, _, snippet) ] ->
        Assert.Equal(p, file)
        Assert.Equal(3, line)
        Assert.True(col > 0)
        Assert.Contains("oops", snippet)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``yaml non-string key is NonStringKey with position`` () =
    match (YamlSource.fromText "k.yaml" "a: 1\n? [1, 2]\n: x\n").Load() with
    | Error [ NonStringKey("k.yaml", line, _, _) ] -> Assert.True(line >= 1)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``duplicate keys: last wins for json and yaml alike`` () =
    let j =
        (JsonSource.fromText "d.json" """{ "a": 1, "A": 2, "b": [1, 2, 3], "b": [9] }""").Load()

    let y = (YamlSource.fromText "d.yaml" "a: 1\nA: 2\nb: [1, 2, 3]\nb: [9]\n").Load()

    let show (r: Result<Entry list, ConfigError list>) =
        r |> Result.map (List.map (fun (e: Entry) -> e.Key, e.Value))

    let expected = Ok [ "a", Text "2"; "b:0", Text "9" ]
    Assert.Equal(expected, show j)
    Assert.Equal(expected, show y)

[<Fact>]
let ``original number text survives json and yaml`` () =
    let j = (JsonSource.fromText "n.json" """{ "v": 1.10, "w": 1e3, "i": 5 }""").Load()
    let y = (YamlSource.fromText "n.yaml" "v: 1.10\nw: 1e3\ni: 5\n").Load()

    let values (r: Result<Entry list, ConfigError list>) =
        r |> Result.map (List.map (fun (e: Entry) -> e.Value))

    Assert.Equal(Ok [ Text "1.10"; Text "1e3"; Text "5" ], values j)
    // YAML resolves 1e3 as the integer 1000, so there is no float text to keep
    Assert.Equal(Ok [ Text "1.10"; Text "1000"; Text "5" ], values y)

[<Fact>]
let ``yaml null clears and scalars flatten`` () =
    let y =
        (YamlSource.fromText "n.yaml" "a: ~\nb: true\nc: 2026-10-14\nd: []\n").Load()

    match y with
    | Ok es ->
        Assert.Equal<EntryValue list>(
            [ Unset; Text "true"; Text "2026-10-14"; EmptyList ],
            es |> List.map (fun (e: Entry) -> e.Value)
        )
    | Error e -> failwithf "%A" e

[<Fact>]
let ``defaults layers files env and args in order`` () =
    let dir = Platform.tempDir ()
    Platform.writeFile (join [ dir; "appsettings.yaml" ]) "Db:\n  Host: base\n  Port: 1\nTags: [a, b]\nLevel: info\n"
    Platform.writeFile (join [ dir; "appsettings.Production.json" ]) """{ "Db": { "Host": "prod" }, "Tags": ["p"] }"""

    let o =
        { Defaults.options "fnapp" with
            Directory = Some dir
            SwitchMappings = [ "-l", "Level" ]
            Env = fun () -> Seq.ofList [ "FNAPP_ENVIRONMENT", "Production"; "FNAPP__Db__Port", "9999"; "OTHER", "x" ]
            Args = fun () -> [ "-l"; "debug"; "--Db:User=cli" ] }

    let cfg =
        ConfigBuilder.build (Defaults.builder o)
        |> Result.defaultWith (fun e -> failwithf "%A" e)

    Assert.Equal(Some "prod", Config.tryGet cfg "Db:Host")
    Assert.Equal(Some "9999", Config.tryGet cfg "Db:Port")
    Assert.Equal(Some "cli", Config.tryGet cfg "Db:User")
    Assert.Equal(Some "debug", Config.tryGet cfg "Level")
    Assert.Equal(Ok [ "p" ], Config.list cfg "Tags")

    match Config.explain cfg "Db:Port" with
    | Some x ->
        Assert.Equal("env", x.Winner.Source)
        Assert.Equal(Some "FNAPP__Db__Port", x.Winner.Detail)
        Assert.Equal(2, x.Chain.Length)
    | None -> failwith "expected explanation"

    match Config.explain cfg "Db:Host" with
    | Some x ->
        Assert.Equal<string list>(
            [ "appsettings.yaml"; "appsettings.Production.json" ],
            x.Chain
            |> List.map (fun o ->
                (o.Origin.Source
                 |> fun s -> s.Substring((max (s.LastIndexOf '/') (s.LastIndexOf '\\')) + 1)))
        )
    | None -> failwith "expected explanation"

[<Fact>]
let ``defaults without any files is fine`` () =
    let dir = Platform.tempDir ()

    let o =
        { Defaults.options "fnapp2" with
            Directory = Some dir
            Env = fun () -> Seq.empty
            Args = fun () -> [] }

    let cfg =
        ConfigBuilder.build (Defaults.builder o)
        |> Result.defaultWith (fun e -> failwithf "%A" e)

    Assert.Equal<(string * string) list>([], Config.entries cfg)
