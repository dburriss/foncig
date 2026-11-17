module MergeTests

open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Foncig

let private build (policy: ListPolicy) (layers: (string * EntryValue) list list) =
    let b =
        layers
        |> List.mapi (fun i pairs -> ConfigSource.inMemoryEntries ("layer" + string i) pairs)
        |> List.fold (fun b s -> ConfigBuilder.add s b) (ConfigBuilder.empty |> ConfigBuilder.withListPolicy policy)

    match ConfigBuilder.build b with
    | Ok c -> c
    | Error e -> failwithf "build failed: %A" e

let private text (k: string) (v: string) = k, Text v

[<Fact>]
let ``later source overrides earlier, case-insensitively, first spelling kept`` () =
    let cfg = build Replace [ [ text "Db:Host" "a" ]; [ text "db:HOST" "b" ] ]
    Assert.Equal(Some "b", Config.tryGet cfg "DB:host")
    Assert.Equal<(string * string) list>([ "Db:Host", "b" ], Config.entries cfg)

[<Fact>]
let ``null in a later layer clears the value`` () =
    let cfg = build Replace [ [ text "a" "1" ]; [ "a", Unset ] ]
    Assert.Equal(None, Config.tryGet cfg "a")
    Assert.Equal(Some Unset, Config.tryFindEntry cfg "a")
    Assert.Equal(None, Config.tryFindEntry cfg "never")

[<Fact>]
let ``a value set after an unset is visible again`` () =
    let cfg = build Replace [ [ text "a" "1" ]; [ "a", Unset ]; [ text "a" "3" ] ]
    Assert.Equal(Some "3", Config.tryGet cfg "a")

[<Fact>]
let ``Replace discards earlier items`` () =
    let cfg =
        build Replace [ [ text "a:0" "x"; text "a:1" "y"; text "a:2" "z" ]; [ text "a:0" "n" ] ]

    Assert.Equal<(string * string) list>([ "a:0", "n" ], Config.entries cfg)

[<Fact>]
let ``MergeByIndex replaces only that element`` () =
    let cfg =
        build MergeByIndex [ [ text "a:0" "x"; text "a:1" "y"; text "a:2" "z" ]; [ text "a:1" "n" ] ]

    Assert.Equal<string list>([ "x"; "n"; "z" ], Config.list cfg "a" |> Result.defaultValue [])

[<Fact>]
let ``Append renumbers after the existing items`` () =
    let cfg =
        build Append [ [ text "a:0" "x"; text "a:1" "y" ]; [ text "a:0" "p"; text "a:3" "q" ] ]

    Assert.Equal<string list>([ "x"; "y"; "p"; "q" ], Config.list cfg "a" |> Result.defaultValue [])

[<Fact>]
let ``Append keeps nested item keys together`` () =
    let cfg =
        build
            Append
            [ [ text "s:0:n" "a"; text "s:0:p" "1" ]
              [ text "s:0:n" "b"; text "s:0:p" "2" ] ]

    Assert.Equal(Some "b", Config.tryGet cfg "s:1:n")
    Assert.Equal(Some "2", Config.tryGet cfg "s:1:p")
    Assert.Equal(Some "a", Config.tryGet cfg "s:0:n")

[<Fact>]
let ``empty list replaces under Replace and is a no-op otherwise`` () =
    let basis = [ text "a:0" "x"; text "a:1" "y" ]
    let replaced = build Replace [ basis; [ "a", EmptyList ] ]
    Assert.Equal<string list>([], Config.list replaced "a" |> Result.defaultValue [ "?" ])
    Assert.Equal(Some EmptyList, Config.tryFindEntry replaced "a")
    let byIndex = build MergeByIndex [ basis; [ "a", EmptyList ] ]
    Assert.Equal<string list>([ "x"; "y" ], Config.list byIndex "a" |> Result.defaultValue [])
    let appended = build Append [ basis; [ "a", EmptyList ] ]
    Assert.Equal<string list>([ "x"; "y" ], Config.list appended "a" |> Result.defaultValue [])

[<Fact>]
let ``items after an empty list remove the marker`` () =
    let cfg = build Replace [ [ "a", EmptyList ]; [ text "a:0" "x" ] ]
    Assert.Equal<string list>([ "x" ], Config.list cfg "a" |> Result.defaultValue [])
    Assert.Equal(None, Config.tryFindEntry cfg "a")

[<Fact>]
let ``per prefix policy overrides the global one`` () =
    let b =
        ConfigBuilder.empty
        |> ConfigBuilder.withListPolicy Replace
        |> ConfigBuilder.withListPolicyFor "Merged" MergeByIndex
        |> ConfigBuilder.withListPolicyFor "Logging:Sinks" Append
        |> ConfigBuilder.add (
            ConfigSource.inMemory
                "base"
                [ "L:0", "a"
                  "L:1", "b"
                  "Merged:0", "a"
                  "Merged:1", "b"
                  "Logging:Sinks:0", "a" ]
        )
        |> ConfigBuilder.add (ConfigSource.inMemory "over" [ "L:0", "n"; "Merged:1", "n"; "Logging:Sinks:0", "n" ])

    let cfg = ConfigBuilder.build b |> Result.defaultWith (fun e -> failwithf "%A" e)
    Assert.Equal<string list>([ "n" ], Config.list cfg "L" |> Result.defaultValue [])
    Assert.Equal<string list>([ "a"; "n" ], Config.list cfg "Merged" |> Result.defaultValue [])
    Assert.Equal<string list>([ "a"; "n" ], Config.list cfg "Logging:Sinks" |> Result.defaultValue [])

[<Fact>]
let ``non-list keys are never affected by list policies`` () =
    let cfg = build Replace [ [ text "a:b" "1"; text "a:c" "2" ]; [ text "a:b" "3" ] ]
    Assert.Equal(Some "2", Config.tryGet cfg "a:c")

[<Fact>]
let ``explain shows the winner and the whole override chain`` () =
    let b =
        ConfigBuilder.empty
        |> ConfigBuilder.add (ConfigSource.inMemory "base" [ "Db:Host", "a" ])
        |> ConfigBuilder.add (ConfigSource.inMemory "user" [ "db:host", "b" ])
        |> ConfigBuilder.addSet "Db:Host" "c"

    let cfg = ConfigBuilder.build b |> Result.defaultWith (fun e -> failwithf "%A" e)

    match Config.explain cfg "DB:HOST" with
    | Some x ->
        Assert.Equal("Db:Host", x.Key)
        Assert.Equal(Some "c", x.Value)
        Assert.Equal("set", x.Winner.Source)
        Assert.Equal<string list>([ "base"; "user"; "set" ], x.Chain |> List.map (fun o -> o.Origin.Source))
        Assert.Equal<EntryValue list>([ Text "a"; Text "b"; Text "c" ], x.Chain |> List.map (fun o -> o.Value))
        Assert.Contains("base", Explanation.describe x)
    | None -> failwith "expected an explanation"

    Assert.True((Config.explain cfg "nope").IsNone)

[<Fact>]
let ``explain keeps the chain through a Replace`` () =
    let cfg = build Replace [ [ text "a:0" "x" ]; [ text "a:0" "y" ] ]

    match Config.explain cfg "a:0" with
    | Some x -> Assert.Equal(2, x.Chain.Length)
    | None -> failwith "expected an explanation"

[<Fact>]
let ``source errors are collected from all sources`` () =
    let b =
        ConfigBuilder.empty
        |> ConfigBuilder.add (ConfigSource.fileNotFound "a.json")
        |> ConfigBuilder.add (ConfigSource.inMemory "ok" [ "k", "v" ])
        |> ConfigBuilder.add (ConfigSource.inMemory "bad" [ "a::b", "v" ])

    match ConfigBuilder.build b with
    | Error [ FileNotFound "a.json"; InvalidKey _ ] -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``an optional missing file is skipped, a required one is an error naming the path`` () =
    let optionalOk =
        ConfigBuilder.empty
        |> ConfigBuilder.add (ConfigSource.optional (ConfigSource.fileNotFound "x.json"))

    Assert.True(Result.isOk (ConfigBuilder.build optionalOk))

    match ConfigBuilder.build (ConfigBuilder.empty |> ConfigBuilder.add (ConfigSource.fileNotFound "x.json")) with
    | Error [ e ] -> Assert.Contains("x.json", ConfigError.describe e)
    | other -> failwithf "unexpected %A" other

// ---- property tests against a simple reference model -------------------------------------------------------

type ListCase =
    { Base: string list
      Override: (int * string) list
      EmptyOverride: bool }

let private genItem = Gen.elements [ "a"; "b"; "c"; "d"; "e" ]

let private genCase =
    Gen.map2
        (fun (b, o) e ->
            { Base = b
              Override = o |> List.distinctBy fst
              EmptyOverride = e })
        (Gen.zip (Gen.listOf genItem) (Gen.listOf (Gen.zip (Gen.choose (0, 5)) genItem)))
        (Gen.frequency [ 1, Gen.constant true; 4, Gen.constant false ])

type ListCaseArb =
    static member Case() : Arbitrary<ListCase> = Arb.fromGen genCase

let private baseLayer (c: ListCase) =
    c.Base |> List.mapi (fun i v -> "a:" + string i, Text v)

let private overrideLayer (c: ListCase) =
    if c.EmptyOverride then
        [ "a", EmptyList ]
    else
        c.Override |> List.map (fun (i, v) -> "a:" + string i, Text v)

let private actual (policy: ListPolicy) (c: ListCase) : Map<int, string> =
    let cfg = build policy [ baseLayer c; overrideLayer c ]

    Config.entries (Config.section cfg "a")
    |> List.map (fun (k, v) -> int k, v)
    |> Map.ofList

let private baseMap (c: ListCase) =
    c.Base |> List.mapi (fun i v -> i, v) |> Map.ofList

let private overrideMap (c: ListCase) =
    if c.EmptyOverride then Map.empty else Map.ofList c.Override

// Reference models: a layer "defines a:*" when it has at least one item (or is an explicit empty list).
let private modelReplace (c: ListCase) =
    if c.EmptyOverride then Map.empty
    elif c.Override.IsEmpty then baseMap c
    else overrideMap c

let private modelMergeByIndex (c: ListCase) =
    Map.fold (fun acc k v -> Map.add k v acc) (baseMap c) (overrideMap c)

let private modelAppend (c: ListCase) =
    let items = c.Base @ (overrideMap c |> Map.toList |> List.map snd)
    items |> List.mapi (fun i v -> i, v) |> Map.ofList

[<Property(Arbitrary = [| typeof<ListCaseArb> |], MaxTest = 300)>]
let ``Replace matches the reference model`` (c: ListCase) = actual Replace c = modelReplace c

[<Property(Arbitrary = [| typeof<ListCaseArb> |], MaxTest = 300)>]
let ``MergeByIndex matches the reference model`` (c: ListCase) =
    actual MergeByIndex c = modelMergeByIndex c

[<Property(Arbitrary = [| typeof<ListCaseArb> |], MaxTest = 300)>]
let ``Append matches the reference model`` (c: ListCase) = actual Append c = modelAppend c

[<Property(Arbitrary = [| typeof<ListCaseArb> |], MaxTest = 300)>]
let ``merging with an empty layer changes nothing`` (c: ListCase) =
    let one = build Replace [ baseLayer c ]
    let two = build Replace [ baseLayer c; [] ]
    Config.entries one = Config.entries two
