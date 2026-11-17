module FlattenTests

open Xunit
open Fnode
open Foncig

let private flat (n: Node) =
    match Flatten.node "t" n with
    | Ok es -> es |> List.map (fun e -> e.Key, e.Value)
    | Error es -> failwithf "unexpected error %A" es

let private text (k: string) (v: string) = k, Text v

[<Fact>]
let ``objects and arrays flatten to colon keys`` () =
    let n =
        Obj
            [ "Db", Obj [ "Host", Str "h"; "Port", Int 5432L ]
              "Servers", Arr [ Obj [ "Name", Str "a" ]; Obj [ "Name", Str "b" ] ]
              "Tags", Arr [ Str "x"; Str "y" ] ]

    Assert.Equal<(string * EntryValue) list>(
        [ text "Db:Host" "h"
          text "Db:Port" "5432"
          text "Servers:0:Name" "a"
          text "Servers:1:Name" "b"
          text "Tags:0" "x"
          text "Tags:1" "y" ],
        flat n
    )

[<Fact>]
let ``scalars flatten to invariant text`` () =
    let n =
        Obj
            [ "s", Str "x"
              "t", Bool true
              "f", Bool false
              "i", Int 9223372036854775807L
              "m", Int -9223372036854775808L
              "d", Float 0.1
              "e", Float 1e21
              "z", Float -0.0
              "ts", Timestamp "2026-10-14" ]

    Assert.Equal<(string * EntryValue) list>(
        [ text "s" "x"
          text "t" "true"
          text "f" "false"
          text "i" "9223372036854775807"
          text "m" "-9223372036854775808"
          text "d" "0.1"
          text "e" "1e+21"
          text "z" "-0"
          text "ts" "2026-10-14" ],
        flat n
    )

[<Fact>]
let ``original number text wins over canonical formatting`` () =
    let p =
        { Node = Obj [ "v", Float 1.1; "w", Float 1000.0 ]
          Locations = Map.empty
          ScalarText = Map.ofList [ "/v", "1.10"; "/w", "1e3" ] }

    match Flatten.parsed "t" p with
    | Ok es -> Assert.Equal<EntryValue list>([ Text "1.10"; Text "1e3" ], es |> List.map (fun e -> e.Value))
    | Error e -> failwithf "%A" e

[<Fact>]
let ``null is an explicit unset`` () =
    Assert.Equal<(string * EntryValue) list>([ "a", Unset; text "b" "1" ], flat (Obj [ "a", Null; "b", Int 1L ]))

[<Fact>]
let ``empty array is a marker and empty object produces nothing`` () =
    Assert.Equal<(string * EntryValue) list>([ "a", EmptyList ], flat (Obj [ "a", Arr []; "o", Obj [] ]))

[<Fact>]
let ``duplicate keys: last value wins, first spelling kept`` () =
    let n = Obj [ "Name", Str "first"; "other", Int 1L; "name", Str "last" ]
    Assert.Equal<(string * EntryValue) list>([ text "Name" "last"; text "other" "1" ], flat n)

[<Fact>]
let ``duplicate keys replace the whole subtree`` () =
    let n = Obj [ "a", Arr [ Str "1"; Str "2"; Str "3" ]; "a", Arr [ Str "9" ] ]
    Assert.Equal<(string * EntryValue) list>([ text "a:0" "9" ], flat n)

[<Fact>]
let ``a key containing a colon is rejected with its position`` () =
    let loc =
        { Start = { Line = 2; Column = 3 }
          End = { Line = 2; Column = 9 } }

    let p =
        { Node = Obj [ "Db:Host", Str "x" ]
          Locations = Map.ofList [ "/Db:Host", loc ]
          ScalarText = Map.empty }

    match Flatten.parsed "app.json" p with
    | Error [ InvalidKey(key, _, Some origin) ] ->
        Assert.Equal("Db:Host", key)
        Assert.Equal(Some "app.json:2:3", origin.Detail)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``an empty key is rejected`` () =
    match Flatten.node "t" (Obj [ "", Str "x" ]) with
    | Error [ InvalidKey _ ] -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``a scalar root is rejected`` () =
    match Flatten.node "t" (Str "x") with
    | Error [ InvalidKey _ ] -> ()
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``null root and empty object give no entries`` () =
    Assert.Equal<(string * EntryValue) list>([], flat (Obj []))
    Assert.Equal<(string * EntryValue) list>([], flat Node.Null)

[<Fact>]
let ``provenance carries file line and column`` () =
    let loc =
        { Start = { Line = 3; Column = 5 }
          End = { Line = 3; Column = 8 } }

    let p =
        { Node = Obj [ "Db", Obj [ "Host", Str "h" ] ]
          Locations = Map.ofList [ "/Db/Host", loc ]
          ScalarText = Map.empty }

    match Flatten.parsed "a.yaml" p with
    | Ok [ e ] ->
        Assert.Equal("a.yaml", e.Origin.Source)
        Assert.Equal(Some "a.yaml:3:5", e.Origin.Detail)
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``pointer escaping for keys with slash`` () =
    let loc =
        { Start = { Line = 1; Column = 1 }
          End = { Line = 1; Column = 2 } }

    let p =
        { Node = Obj [ "a/b", Str "x" ]
          Locations = Map.ofList [ "/a~1b", loc ]
          ScalarText = Map.empty }

    match Flatten.parsed "f" p with
    | Ok [ e ] -> Assert.Equal(Some "f:1:1", e.Origin.Detail)
    | other -> failwithf "unexpected %A" other
