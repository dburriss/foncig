namespace Foncig

open Fnode

/// Turn a `Node` tree into flat `Entry` values.
///
/// - Objects and arrays are walked; array items get index segments (`Servers:0:Name`).
/// - Scalars become text (`Scalar`): strings as is, `true`/`false`, ints exactly, floats canonically, timestamps
///   as their original text. A number's original source text (`1.10`) is kept when the parser supplied it.
/// - `null` becomes `Unset` (a `null` document, such as an empty YAML file, gives no entries); an empty array becomes an `EmptyList` marker; an empty object produces nothing.
/// - Duplicate keys in one object (also keys that differ only by case): the last value wins, the first spelling
///   is kept. The same for JSON and YAML.
/// - A key containing `:` or an empty key is an `InvalidKey` error with the file position.
module Flatten =

    let private scalarText (ptr: string) (text: Map<string, string>) (n: Node) : EntryValue =
        match n with
        | Node.Null -> Unset
        | Node.Bool b -> Text(if b then "true" else "false")
        | Node.Int i -> Text(string i)
        | Node.Float f ->
            match Map.tryFind ptr text with
            | Some t when Scalar.isFloatText t -> Text t
            | _ -> Text(Scalar.formatFloat f)
        | Node.Str s -> Text s
        | Node.Timestamp t -> Text t
        | Node.Arr _
        | Node.Obj _ -> Unset

    /// Last value wins per case-insensitive key; keeps first spelling and first position. Returns
    /// (first spelling, pointer key of the winning pair, winning value).
    let private dedupe (kvs: (string * Node) list) : (string * string * Node) list =
        let order = System.Collections.Generic.List<string>()
        let table = System.Collections.Generic.Dictionary<string, string * string * Node>()

        for (k, v) in kvs do
            let nk = ConfigKey.normalize k

            match table.TryGetValue nk with
            | true, (first, _, _) -> table.[nk] <- (first, k, v)
            | _ ->
                order.Add nk
                table.[nk] <- (k, k, v)

        [ for nk in order -> table.[nk] ]

    /// Flatten `node`. `source` names the origin; `parsed` supplies positions and number text when available.
    let parsed (source: string) (p: Parsed) : Result<Entry list, ConfigError list> =
        let entries = ResizeArray<Entry>()
        let errors = ResizeArray<ConfigError>()

        let origin (ptr: string) : Provenance =
            match Map.tryFind ptr p.Locations with
            | Some span ->
                { Source = source
                  Detail = Some(source + ":" + string span.Start.Line + ":" + string span.Start.Column) }
            | None -> { Source = source; Detail = None }

        let emit key ptr value =
            entries.Add
                { Key = key
                  Value = value
                  Origin = origin ptr }

        let rec go (key: string) (ptr: string) (n: Node) =
            match n with
            | Node.Obj kvs ->
                for (first, raw, v) in dedupe kvs do
                    let childPtr = ptr + "/" + Path.escapeSegment raw

                    match ConfigKey.validateSegment first with
                    | Error reason -> errors.Add(InvalidKey(ConfigKey.combine key first, reason, Some(origin childPtr)))
                    | Ok seg -> go (ConfigKey.combine key seg) childPtr v
            | Node.Arr [] ->
                if key <> "" then
                    emit key ptr EmptyList
            | Node.Arr items ->
                items
                |> List.iteri (fun i v -> go (ConfigKey.combine key (string i)) (ptr + "/" + string i) v)
            | Node.Null when key = "" -> ()
            | scalar ->
                if key = "" then
                    errors.Add(
                        InvalidKey("", "the document root must be an object or array, not a scalar", Some(origin ""))
                    )
                else
                    emit key ptr (scalarText ptr p.ScalarText scalar)

        go "" "" p.Node

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok(List.ofSeq entries)

    /// Flatten a node built in code (no positions).
    let node (source: string) (n: Node) : Result<Entry list, ConfigError list> =
        parsed
            source
            { Node = n
              Locations = Map.empty
              ScalarText = Map.empty }
