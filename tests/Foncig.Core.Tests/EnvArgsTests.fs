module EnvArgsTests

open Xunit
open Foncig

let private env (prefix: string) (vars: (string * string) list) =
    Env.entries (EnvOptions.withPrefix prefix) vars
    |> List.map (fun e -> e.Key, e.Value)

[<Fact>]
let ``prefix is filtered and stripped, double underscore is a colon`` () =
    let r =
        env "APP_" [ "APP__Db__Host", "h"; "APP__Servers__0", "s0"; "OTHER", "x"; "PATH", "/bin" ]

    Assert.Equal<(string * EntryValue) list>([ "Db:Host", Text "h"; "Servers:0", Text "s0" ], r)

[<Fact>]
let ``prefix matching is case-insensitive`` () =
    let r = env "app_" [ "APP__A", "1"; "App__B", "2" ]
    Assert.Equal<(string * EntryValue) list>([ "A", Text "1"; "B", Text "2" ], r)

[<Fact>]
let ``single underscore after the prefix also works`` () =
    Assert.Equal<(string * EntryValue) list>([ "Db:Host", Text "h" ], env "APP_" [ "APP_Db__Host", "h" ])

[<Fact>]
let ``empty prefix reads everything`` () =
    Assert.Equal<(string * EntryValue) list>([ "A:B", Text "1" ], env "" [ "A__B", "1" ])

[<Fact>]
let ``unusable names are ignored`` () =
    Assert.Equal<(string * EntryValue) list>(
        [],
        env "APP_" [ "APP_", "1"; "APP__", "2"; "APP__A____B", "3"; "APP__A__", "4" ]
    )

[<Fact>]
let ``names differing only by case: the later wins`` () =
    let b =
        ConfigBuilder.empty
        |> ConfigBuilder.add (
            ConfigSource.fromEnv
                "env"
                (EnvOptions.withPrefix "APP_")
                [ "APP__Db__Host", "first"; "APP__DB__HOST", "second" ]
        )

    let cfg = ConfigBuilder.build b |> Result.defaultWith (fun e -> failwithf "%A" e)
    Assert.Equal(Some "second", Config.tryGet cfg "Db:Host")
    Assert.Equal(Some "Db:Host", Config.explain cfg "db:host" |> Option.map (fun x -> x.Key))

[<Fact>]
let ``env provenance names the variable`` () =
    match Env.entries (EnvOptions.withPrefix "APP_") [ "APP__A", "1" ] with
    | [ e ] ->
        Assert.Equal("env", e.Origin.Source)
        Assert.Equal(Some "APP__A", e.Origin.Detail)
    | other -> failwithf "unexpected %A" other

let private parse (mappings: (string * string) list) (args: string list) =
    Args.parse { SwitchMappings = mappings } args

let private pairs (r: Result<ArgsResult, ConfigError list>) =
    match r with
    | Ok x -> x.Entries |> List.map (fun e -> e.Key, e.Value), x.Positionals
    | Error es -> failwithf "unexpected errors %A" es

[<Fact>]
let ``all key value forms`` () =
    let entries, positionals =
        pairs (parse [] [ "--a"; "1"; "--b=2"; "--c:d"; "3"; "/e=4"; "--flag" ])

    Assert.Equal<(string * EntryValue) list>(
        [ "a", Text "1"
          "b", Text "2"
          "c:d", Text "3"
          "e", Text "4"
          "flag", Text "true" ],
        entries
    )

    Assert.Equal<string list>([], positionals)

[<Fact>]
let ``a flag followed by another switch is true`` () =
    let entries, _ = pairs (parse [] [ "--a"; "--b"; "x" ])
    Assert.Equal<(string * EntryValue) list>([ "a", Text "true"; "b", Text "x" ], entries)

[<Fact>]
let ``negative numbers are values`` () =
    let entries, _ = pairs (parse [] [ "--n"; "-5"; "--m"; "-0.5" ])
    Assert.Equal<(string * EntryValue) list>([ "n", Text "-5"; "m", Text "-0.5" ], entries)

[<Fact>]
let ``switch mappings`` () =
    let entries, _ =
        pairs (
            parse [ "-h", "Db:Host"; "-v", "Verbose"; "--port", "Db:Port" ] [ "-h"; "x"; "-v"; "--port=1"; "-H=y" ]
            |> fun r -> r
        )

    Assert.Equal<(string * EntryValue) list>(
        [ "Db:Host", Text "x"
          "Verbose", Text "true"
          "Db:Port", Text "1"
          "Db:Host", Text "y" ],
        entries
    )

[<Fact>]
let ``unknown single-dash switch reports token and index`` () =
    match parse [] [ "--a"; "1"; "-x" ] with
    | Error [ UnknownSwitch("-x", 2) ] -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``double dash stops and exposes positionals`` () =
    let entries, positionals =
        pairs (parse [] [ "--a"; "1"; "file.txt"; "--"; "--b"; "-x"; "c" ])

    Assert.Equal<(string * EntryValue) list>([ "a", Text "1" ], entries)
    Assert.Equal<string list>([ "file.txt"; "--b"; "-x"; "c" ], positionals)

[<Fact>]
let ``a slash token without equals is positional`` () =
    let entries, positionals = pairs (parse [] [ "/usr/bin" ])
    Assert.Equal<(string * EntryValue) list>([], entries)
    Assert.Equal<string list>([ "/usr/bin" ], positionals)

[<Fact>]
let ``path-like values are values`` () =
    let entries, _ = pairs (parse [] [ "--path"; "/usr/bin" ])
    Assert.Equal<(string * EntryValue) list>([ "path", Text "/usr/bin" ], entries)

[<Fact>]
let ``empty or malformed keys are errors`` () =
    match parse [] [ "--=x" ] with
    | Error [ InvalidKey _ ] -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``args provenance carries the token`` () =
    match Args.parse ArgsOptions.defaults [ "--a"; "1" ] with
    | Ok r ->
        Assert.Equal("args", r.Entries.Head.Origin.Source)
        Assert.Equal(Some "--a 1", r.Entries.Head.Origin.Detail)
    | Error e -> failwithf "%A" e

[<Fact>]
let ``list items by cli`` () =
    let b =
        ConfigBuilder.empty
        |> ConfigBuilder.add (ConfigSource.fromArgs "args" ArgsOptions.defaults [ "--Tags:0"; "a"; "--Tags:1=b" ])

    let cfg = ConfigBuilder.build b |> Result.defaultWith (fun e -> failwithf "%A" e)
    Assert.Equal<string list>([ "a"; "b" ], Config.list cfg "Tags" |> Result.defaultValue [])
