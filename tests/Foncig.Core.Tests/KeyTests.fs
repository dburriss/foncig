module KeyTests

open Xunit
open Foncig

[<Fact>]
let ``combine and split`` () =
    Assert.Equal("Db:Host", ConfigKey.combine "Db" "Host")
    Assert.Equal("Host", ConfigKey.combine "" "Host")
    Assert.Equal("Db", ConfigKey.combine "Db" "")
    Assert.Equal<string list>([ "Servers"; "0"; "Name" ], ConfigKey.split "Servers:0:Name")
    Assert.Equal<string list>([], ConfigKey.split "")

[<Fact>]
let ``parent and last segment`` () =
    Assert.Equal(Some "Db", ConfigKey.parent "Db:Host")
    Assert.Equal(None, ConfigKey.parent "Db")
    Assert.Equal("Host", ConfigKey.lastSegment "Db:Host")
    Assert.Equal("Db", ConfigKey.lastSegment "Db")

[<Fact>]
let ``normalize is case-insensitive`` () =
    Assert.Equal(ConfigKey.normalize "Db:HOST", ConfigKey.normalize "db:host")

[<Fact>]
let ``index segments`` () =
    Assert.True(ConfigKey.isIndex "0")
    Assert.True(ConfigKey.isIndex "123456789")
    Assert.False(ConfigKey.isIndex "1234567890")
    Assert.False(ConfigKey.isIndex "")
    Assert.False(ConfigKey.isIndex "1a")
    Assert.False(ConfigKey.isIndex "-1")
    Assert.Equal(Some 12, ConfigKey.tryIndex "12")
    Assert.Equal(None, ConfigKey.tryIndex "x")

[<Fact>]
let ``validate rejects empty segments`` () =
    Assert.True(Result.isOk (ConfigKey.validate "a:b"))
    Assert.True(Result.isError (ConfigKey.validate ""))
    Assert.True(Result.isError (ConfigKey.validate ":a"))
    Assert.True(Result.isError (ConfigKey.validate "a:"))
    Assert.True(Result.isError (ConfigKey.validate "a::b"))

[<Fact>]
let ``a segment containing the separator is rejected`` () =
    Assert.True(Result.isError (ConfigKey.validateSegment "a:b"))
    Assert.True(Result.isError (ConfigKey.validateSegment ""))
    Assert.Equal(Ok "a b", ConfigKey.validateSegment "a b")
    Assert.Equal(Ok "a:b", ConfigKey.ofSegments [ "a"; "b" ])
    Assert.True(Result.isError (ConfigKey.ofSegments [ "a"; "b:c" ]))

[<Fact>]
let ``isUnder is segment aware`` () =
    Assert.True(ConfigKey.isUnder "Db" "db:host")
    Assert.True(ConfigKey.isUnder "Db" "DB")
    Assert.False(ConfigKey.isUnder "Db" "Dbx:host")
    Assert.True(ConfigKey.isUnder "" "anything")
    Assert.Equal(Some "Host", ConfigKey.tryRelative "db" "Db:Host")
    Assert.Equal(None, ConfigKey.tryRelative "db" "Db")
