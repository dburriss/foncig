namespace Foncig

open System

/// Explicit, reflection-free encoding of values into configuration pairs (the mirror of the decoder).
///
///     let encodeSettings (s: Settings) =
///         [ Encode.string "Db:Host" s.Host
///           Encode.int "Db:Port" s.Port
///           Encode.list "Tags" s.Tags
///           Encode.option Encode.string "Db:User" s.User ]
///         |> List.concat
///
/// Use with `ConfigSource.ofRecord`. Formatting is the same deterministic text the file sources produce.
module Encode =

    type Pairs = (string * EntryValue) list

    let private one key (text: string) : Pairs = [ key, Text text ]

    let string (key: string) (v: string) : Pairs = one key v
    let int (key: string) (v: int) : Pairs = one key (Operators.string v)
    let int64 (key: string) (v: int64) : Pairs = one key (Operators.string v)
    let float (key: string) (v: float) : Pairs = one key (Scalar.formatFloat v)
    let bool (key: string) (v: bool) : Pairs = one key (if v then "true" else "false")

    let guid (key: string) (v: Guid) : Pairs =
        one key (v.ToString().ToLowerInvariant())

    let uri (key: string) (v: Uri) : Pairs = one key v.OriginalString
    let timeSpan (key: string) (v: TimeSpan) : Pairs = one key (Scalar.formatTimeSpan v)
    let dateTimeOffset (key: string) (v: DateTimeOffset) : Pairs = one key (Scalar.formatDateTimeOffset v)

    /// Clears the key in earlier layers.
    let unset (key: string) : Pairs = [ key, Unset ]

    /// `None` produces nothing; `Some` encodes the value.
    let option (f: string -> 'a -> Pairs) (key: string) (v: 'a option) : Pairs =
        match v with
        | Some x -> f key x
        | None -> []

    /// A list of strings as `Key:0`, `Key:1`, ...; an empty list is an `EmptyList` marker.
    let list (key: string) (items: string list) : Pairs =
        match items with
        | [] -> [ key, EmptyList ]
        | _ ->
            items
            |> List.mapi (fun i s -> ConfigKey.combine key (Operators.string i), Text s)

    /// A string map as `Key:name`.
    let map (key: string) (items: Map<string, string>) : Pairs =
        [ for KeyValue(k, v) in items -> ConfigKey.combine key k, Text v ]
