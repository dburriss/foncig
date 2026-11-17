module ScalarTests

open System
open Xunit
open Foncig

[<Theory>]
[<InlineData(1e21, "1e+21")>]
[<InlineData(1e20, "100000000000000000000")>]
[<InlineData(0.1, "0.1")>]
[<InlineData(0.2, "0.2")>]
[<InlineData(1.5, "1.5")>]
[<InlineData(100.0, "100")>]
[<InlineData(-2.5, "-2.5")>]
[<InlineData(0.000001, "0.000001")>]
[<InlineData(1e-7, "1e-7")>]
[<InlineData(1.5e-10, "1.5e-10")>]
[<InlineData(123456789.125, "123456789.125")>]
[<InlineData(5e-324, "5e-324")>]
[<InlineData(1.7976931348623157e308, "1.7976931348623157e+308")>]
[<InlineData(1.2345678901234568e20, "123456789012345680000")>]
[<InlineData(1.2e22, "1.2e+22")>]
let ``formatFloat is canonical`` (value: float, expected: string) =
    Assert.Equal(expected, Scalar.formatFloat value)

[<Fact>]
let ``formatFloat special values`` () =
    Assert.Equal("0", Scalar.formatFloat 0.0)
    Assert.Equal("-0", Scalar.formatFloat -0.0)
    Assert.Equal("NaN", Scalar.formatFloat nan)
    Assert.Equal("Infinity", Scalar.formatFloat infinity)
    Assert.Equal("-Infinity", Scalar.formatFloat -infinity)

[<Fact>]
let ``float text round trips`` () =
    for f in [ 0.1; 1e21; 1e-7; -0.5; 3.141592653589793; 1e300; 5e-324 ] do
        Assert.Equal(Some f, Scalar.tryFloat (Scalar.formatFloat f))

[<Fact>]
let ``int64 parsing is strict and covers the full range`` () =
    Assert.Equal(Some 9223372036854775807L, Scalar.tryInt64 "9223372036854775807")
    Assert.Equal(Some -9223372036854775808L, Scalar.tryInt64 "-9223372036854775808")
    Assert.Equal(None, Scalar.tryInt64 "9223372036854775808")
    Assert.Equal(None, Scalar.tryInt64 "-9223372036854775809")
    Assert.Equal(Some 5L, Scalar.tryInt64 "+5")
    Assert.Equal(None, Scalar.tryInt64 "")
    Assert.Equal(None, Scalar.tryInt64 "-")
    Assert.Equal(None, Scalar.tryInt64 " 5")
    Assert.Equal(None, Scalar.tryInt64 "5 ")
    Assert.Equal(None, Scalar.tryInt64 "1_000")
    Assert.Equal(None, Scalar.tryInt64 "1.0")
    Assert.Equal(None, Scalar.tryInt64 "0x10")

[<Fact>]
let ``int parsing`` () =
    Assert.Equal(Some 2147483647, Scalar.tryInt "2147483647")
    Assert.Equal(Some -2147483648, Scalar.tryInt "-2147483648")
    Assert.Equal(None, Scalar.tryInt "2147483648")

[<Fact>]
let ``float parsing is invariant and strict`` () =
    Assert.Equal(Some 1.5, Scalar.tryFloat "1.5")
    Assert.Equal(Some 1.5, Scalar.tryFloat "1.5e0")
    Assert.Equal(Some 1000.0, Scalar.tryFloat "1e3")
    Assert.Equal(Some 0.5, Scalar.tryFloat ".5")
    Assert.Equal(Some 5.0, Scalar.tryFloat "5.")
    Assert.Equal(None, Scalar.tryFloat "1,5")
    Assert.Equal(None, Scalar.tryFloat "")
    Assert.Equal(None, Scalar.tryFloat "1e")
    Assert.Equal(None, Scalar.tryFloat ".")
    Assert.Equal(None, Scalar.tryFloat "abc")
    Assert.Equal(None, Scalar.tryFloat " 1")
    Assert.Equal(None, Scalar.tryFloat "0x1")
    Assert.Equal(Some infinity, Scalar.tryFloat "Infinity")
    Assert.True(Option.exists Double.IsNaN (Scalar.tryFloat "NaN"))

[<Fact>]
let ``bool accepts true and false in any case only`` () =
    Assert.Equal(Some true, Scalar.tryBool "true")
    Assert.Equal(Some true, Scalar.tryBool "TRUE")
    Assert.Equal(Some false, Scalar.tryBool "False")
    Assert.Equal(None, Scalar.tryBool "1")
    Assert.Equal(None, Scalar.tryBool "0")
    Assert.Equal(None, Scalar.tryBool "yes")
    Assert.Equal(None, Scalar.tryBool "")

[<Fact>]
let ``guid parsing`` () =
    let g = "d3b07384-d9a0-4c3e-a2f6-0a1b2c3d4e5f"
    Assert.Equal(Some(Guid.Parse g), Scalar.tryGuid g)
    Assert.Equal(Some(Guid.Parse g), Scalar.tryGuid (g.ToUpperInvariant()))
    Assert.Equal(None, Scalar.tryGuid "not-a-guid")
    Assert.Equal(None, Scalar.tryGuid ("{" + g + "}"))

[<Fact>]
let ``timespan parsing and formatting`` () =
    Assert.Equal(Some(TimeSpan(0, 1, 30, 0, 0)), Scalar.tryTimeSpan "01:30:00")
    Assert.Equal(Some(TimeSpan(2, 3, 4, 5, 0)), Scalar.tryTimeSpan "2.03:04:05")
    Assert.Equal(Some(TimeSpan(0, 0, 0, 0, 500)), Scalar.tryTimeSpan "00:00:00.5")
    Assert.Equal(Some(TimeSpan(0, -1, 0, 0, 0)), Scalar.tryTimeSpan "-01:00:00")
    Assert.Equal(None, Scalar.tryTimeSpan "1:2")
    Assert.Equal(None, Scalar.tryTimeSpan "25:00:00")
    Assert.Equal(None, Scalar.tryTimeSpan "abc")
    Assert.Equal("01:30:00", Scalar.formatTimeSpan (TimeSpan(0, 1, 30, 0, 0)))
    Assert.Equal("2.03:04:05", Scalar.formatTimeSpan (TimeSpan(2, 3, 4, 5, 0)))
    Assert.Equal("-01:00:00", Scalar.formatTimeSpan (TimeSpan(0, -1, 0, 0, 0)))
    Assert.Equal("00:00:00.5", Scalar.formatTimeSpan (TimeSpan(0, 0, 0, 0, 500)))

[<Fact>]
let ``datetimeoffset parsing and formatting`` () =
    Assert.Equal(
        Some(DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.Zero)),
        Scalar.tryDateTimeOffset "2026-10-14T01:02:03Z"
    )

    Assert.Equal(Some(DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)), Scalar.tryDateTimeOffset "2026-10-14")

    Assert.Equal(
        Some(DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan(1, 0, 0))),
        Scalar.tryDateTimeOffset "2026-10-14 01:02:03+01:00"
    )

    Assert.Equal(None, Scalar.tryDateTimeOffset "2026-02-30")
    Assert.Equal(None, Scalar.tryDateTimeOffset "14/10/2026")
    Assert.Equal(None, Scalar.tryDateTimeOffset "2026-10-14T25:00:00Z")
    let t = DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan(-5, -30, 0))
    Assert.Equal("2026-10-14T01:02:03-05:30", Scalar.formatDateTimeOffset t)
    Assert.Equal(Some t, Scalar.tryDateTimeOffset (Scalar.formatDateTimeOffset t))

[<Fact>]
let ``uri parsing`` () =
    Assert.True((Scalar.tryUri "https://example.com/a?b=1").IsSome)
    Assert.True((Scalar.tryUri "nope").IsNone)
    Assert.True((Scalar.tryUri "").IsNone)
