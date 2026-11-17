namespace Foncig

open System

/// Reads a typed value from a `Config`, accumulating every error.
type Decoder<'a> = Config -> Result<'a, ConfigError list>

/// Chooses a `Reader` from the type of a default value, so `optional "Db:Port" 5432` reads an int.
type ReaderOf =
    | ReaderOf

    static member Of(_: string) : Reader<string> = Readers.string
    static member Of(_: int) : Reader<int> = Readers.int
    static member Of(_: int64) : Reader<int64> = Readers.int64
    static member Of(_: float) : Reader<float> = Readers.float
    static member Of(_: bool) : Reader<bool> = Readers.bool
    static member Of(_: Guid) : Reader<Guid> = Readers.guid
    static member Of(_: TimeSpan) : Reader<TimeSpan> = Readers.timeSpan
    static member Of(_: DateTimeOffset) : Reader<DateTimeOffset> = Readers.dateTimeOffset

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Decode =

    let inline private readerOfImpl< ^R, ^a when (^R or ^a): (static member Of: ^a -> Reader< ^a >)>
        (_: ^R)
        (x: ^a)
        : Reader< ^a > =
        ((^R or ^a): (static member Of: ^a -> Reader< ^a >) x)

    /// The reader for the type of `x` (string, int, int64, float, bool, Guid, TimeSpan, DateTimeOffset; for a Uri use `optionalWith Readers.uri`).
    let inline readerOf (x: ^a) : Reader< ^a > = readerOfImpl ReaderOf x

    let private single (r: Result<'a, ConfigError>) : Result<'a, ConfigError list> = r |> Result.mapError List.singleton

    /// A fixed value.
    let succeed (value: 'a) : Decoder<'a> = fun _ -> Ok value

    let fail (error: ConfigError) : Decoder<'a> = fun _ -> Error [ error ]

    let map (f: 'a -> 'b) (d: Decoder<'a>) : Decoder<'b> = fun cfg -> d cfg |> Result.map f

    /// Both must succeed; errors from both are kept.
    let map2 (f: 'a -> 'b -> 'c) (da: Decoder<'a>) (db: Decoder<'b>) : Decoder<'c> =
        fun cfg ->
            match da cfg, db cfg with
            | Ok a, Ok b -> Ok(f a b)
            | Error ea, Error eb -> Error(ea @ eb)
            | Error ea, _ -> Error ea
            | _, Error eb -> Error eb

    /// A required value read with an explicit reader.
    let requiredWith (reader: Reader<'a>) (key: string) : Decoder<'a> =
        fun cfg -> Config.read reader cfg key |> single

    /// An optional value (`None` when absent or unset).
    let optionalWith (reader: Reader<'a>) (key: string) : Decoder<'a option> =
        fun cfg -> Config.tryRead reader cfg key |> single

    /// A value or `fallback` when absent. A present but malformed value is still an error.
    let withDefault (reader: Reader<'a>) (key: string) (fallback: 'a) : Decoder<'a> =
        fun cfg -> Config.readOr reader fallback cfg key |> single

    /// A required string.
    let required (key: string) : Decoder<string> = requiredWith Readers.string key

    /// A value of the same type as `fallback` (string, int, int64, float, bool, Guid, TimeSpan, DateTimeOffset; for a Uri use `optionalWith Readers.uri`).
    let inline optional (key: string) (fallback: ^a) : Decoder< ^a > =
        withDefault (readerOf fallback) key fallback

    let int key = requiredWith Readers.int key
    let int64 key = requiredWith Readers.int64 key
    let float key = requiredWith Readers.float key
    let bool key = requiredWith Readers.bool key
    let guid key = requiredWith Readers.guid key
    let uri key = requiredWith Readers.uri key
    let timeSpan key = requiredWith Readers.timeSpan key
    let dateTimeOffset key = requiredWith Readers.dateTimeOffset key

    /// A list of strings (`Key:0`, `Key:1`, ...); empty when absent.
    let list (key: string) : Decoder<string list> =
        fun cfg -> Config.list cfg key |> single

    /// A map of the direct text children of `key`; empty when absent.
    let map' (key: string) : Decoder<Map<string, string>> = fun cfg -> Ok(Config.map cfg key)

    /// Decode a sub-object with its own decoder, rooted at `key`.
    let section (key: string) (inner: Decoder<'a>) : Decoder<'a> =
        fun cfg -> inner (Config.section cfg key)

    /// Run a decoder.
    let run (decoder: Decoder<'a>) (cfg: Config) : Result<'a, ConfigError list> = decoder cfg

type DecoderBuilder() =
    member _.Return(x: 'a) : Decoder<'a> = Decode.succeed x
    member _.ReturnFrom(d: Decoder<'a>) : Decoder<'a> = d

    member _.Bind(d: Decoder<'a>, f: 'a -> Decoder<'b>) : Decoder<'b> =
        fun cfg ->
            match d cfg with
            | Ok a -> f a cfg
            | Error e -> Error e

    /// `and!` support: both sides run and their errors accumulate.
    member _.MergeSources(a: Decoder<'a>, b: Decoder<'b>) : Decoder<'a * 'b> = Decode.map2 (fun x y -> x, y) a b

    member _.BindReturn(d: Decoder<'a>, f: 'a -> 'b) : Decoder<'b> = Decode.map f d

[<AutoOpen>]
module DecoderBuilderInstance =
    /// `config { let! host = required "Db:Host" and! port = optional "Db:Port" 5432 return {...} }`
    let config = DecoderBuilder()
