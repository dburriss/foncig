namespace Foncig

open System

/// How a value reads as a type: what it expects (for errors) and a strict, culture-free parser.
type Reader<'a> =
    { Expected: string
      Parse: string -> 'a option }

module Readers =
    let string: Reader<string> = { Expected = "string"; Parse = Some }

    let int: Reader<int> =
        { Expected = "int"
          Parse = Scalar.tryInt }

    let int64: Reader<int64> =
        { Expected = "int64"
          Parse = Scalar.tryInt64 }

    let float: Reader<float> =
        { Expected = "float"
          Parse = Scalar.tryFloat }

    let bool: Reader<bool> =
        { Expected = "bool (true/false)"
          Parse = Scalar.tryBool }

    let guid: Reader<Guid> =
        { Expected = "Guid"
          Parse = Scalar.tryGuid }

    let uri: Reader<Uri> =
        { Expected = "absolute Uri"
          Parse = Scalar.tryUri }

    let timeSpan: Reader<TimeSpan> =
        { Expected = "TimeSpan ([-][d.]hh:mm:ss[.fffffff])"
          Parse = Scalar.tryTimeSpan }

    let dateTimeOffset: Reader<DateTimeOffset> =
        { Expected = "ISO 8601 date/time"
          Parse = Scalar.tryDateTimeOffset }

/// The key's final value with every layer that touched it.
type Explanation =
    {
        Key: string
        /// `None` when the winning layer unset the key (or marked an empty list).
        Value: string option
        Winner: Provenance
        /// Every layer that set the key, oldest first; the last one is the winner.
        Chain: Override list
    }

module Explanation =
    let private show (v: EntryValue) =
        match v with
        | Text s -> "\"" + s + "\""
        | Unset -> "(unset)"
        | EmptyList -> "(empty list)"

    /// Multi-line text: the winner, then the override chain.
    let describe (x: Explanation) : string =
        let head =
            x.Key
            + " = "
            + (match x.Value with
               | Some v -> "\"" + v + "\""
               | None -> "(none)")
            + "  <- "
            + Provenance.describe x.Winner

        let chain =
            x.Chain
            |> List.mapi (fun i o ->
                "  "
                + string (i + 1)
                + ". "
                + show o.Value
                + "  "
                + Provenance.describe o.Origin)

        String.concat "\n" (head :: chain)

/// An immutable, merged configuration, or a view of it rooted at a section.
type Config =
    internal
        {
            Items: Map<string, Resolved>
            /// Normalised root prefix; empty for the whole configuration.
            Root: string
            /// The root as written, for messages.
            RootName: string
        }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Config =

    let empty: Config =
        { Items = Map.empty
          Root = ""
          RootName = "" }

    let internal ofState (state: MergeState) : Config =
        { Items = state.Items
          Root = ""
          RootName = "" }

    let private full (cfg: Config) (key: string) : string =
        let nk = ConfigKey.normalize key

        if cfg.Root = "" then nk
        elif nk = "" then cfg.Root
        else cfg.Root + ConfigKey.Separator + nk

    /// The raw state of a key: `None` = absent (no layer mentioned it); `Some Unset` = explicitly unset.
    /// This is how "unset" differs from "absent".
    let tryFindEntry (cfg: Config) (key: string) : EntryValue option =
        Map.tryFind (full cfg key) cfg.Items |> Option.map (fun r -> r.Value)

    let private tryResolved (cfg: Config) (key: string) : Resolved option = Map.tryFind (full cfg key) cfg.Items

    /// The value, or `None` when absent or unset.
    let tryGet (cfg: Config) (key: string) : string option =
        match tryResolved cfg key with
        | Some { Value = Text s } -> Some s
        | _ -> None

    /// The value, or `fallback`.
    let get (cfg: Config) (key: string) (fallback: string) : string = defaultArg (tryGet cfg key) fallback

    /// The value, or `Missing`.
    let require (cfg: Config) (key: string) : Result<string, ConfigError> =
        match tryGet cfg key with
        | Some s -> Ok s
        | None -> Error(Missing(ConfigKey.combine cfg.RootName key))

    /// The winning provenance of a key (`None` when absent).
    let tryOrigin (cfg: Config) (key: string) : Provenance option =
        tryResolved cfg key |> Option.map (fun r -> (List.last r.Chain).Origin)

    let private mismatch (r: Resolved) (reader: Reader<'a>) (text: string) =
        TypeMismatch(r.Key, reader.Expected, text, Some (List.last r.Chain).Origin)

    /// Read a value as a type: `Ok None` when absent/unset, `Error TypeMismatch` when it does not parse.
    let tryRead (reader: Reader<'a>) (cfg: Config) (key: string) : Result<'a option, ConfigError> =
        match tryResolved cfg key with
        | Some({ Value = Text s } as r) ->
            match reader.Parse s with
            | Some v -> Ok(Some v)
            | None -> Error(mismatch r reader s)
        | _ -> Ok None

    /// Read a value as a type; `Missing` when absent.
    let read (reader: Reader<'a>) (cfg: Config) (key: string) : Result<'a, ConfigError> =
        match tryRead reader cfg key with
        | Ok(Some v) -> Ok v
        | Ok None -> Error(Missing(ConfigKey.combine cfg.RootName key))
        | Error e -> Error e

    /// Read a value as a type, or `fallback` when absent.
    let readOr (reader: Reader<'a>) (fallback: 'a) (cfg: Config) (key: string) : Result<'a, ConfigError> =
        tryRead reader cfg key |> Result.map (Option.defaultValue fallback)

    let tryInt cfg key = tryRead Readers.int cfg key
    let tryInt64 cfg key = tryRead Readers.int64 cfg key
    let tryFloat cfg key = tryRead Readers.float cfg key
    let tryBool cfg key = tryRead Readers.bool cfg key
    let tryGuid cfg key = tryRead Readers.guid cfg key
    let tryUri cfg key = tryRead Readers.uri cfg key
    let tryTimeSpan cfg key = tryRead Readers.timeSpan cfg key
    let tryDateTimeOffset cfg key = tryRead Readers.dateTimeOffset cfg key

    /// A view rooted at `name` (`section cfg "Db"`, then `tryGet "Host"` reads `Db:Host`).
    let section (cfg: Config) (name: string) : Config =
        { cfg with
            Root = full cfg name
            RootName = ConfigKey.combine cfg.RootName name }

    /// Resolved items strictly below the root that have a visible value (text or empty-list marker), in order.
    let private visible (cfg: Config) : (string list * Resolved) list =
        cfg.Items
        |> Map.toList
        |> List.choose (fun (nk, r) ->
            match r.Value with
            | Unset -> None
            | _ ->
                if cfg.Root = "" then
                    Some(ConfigKey.split r.Key, r)
                elif nk.StartsWith(cfg.Root + ConfigKey.Separator, StringComparison.Ordinal) then
                    let rootDepth = List.length (ConfigKey.split cfg.Root)
                    Some(ConfigKey.split r.Key |> List.skip rootDepth, r)
                else
                    None)
        |> List.sortBy (fun (_, r) -> r.Order)

    /// Names of the direct children (next key segment) with a value somewhere at or below them, in first-seen order.
    let children (cfg: Config) : string list =
        visible cfg
        |> List.map (fun (segs, _) -> List.head segs)
        |> List.distinctBy ConfigKey.normalize

    /// Every visible key (relative to this view) with its text value, in first-seen order.
    let entries (cfg: Config) : (string * string) list =
        visible cfg
        |> List.choose (fun (segs, r) ->
            match r.Value with
            | Text s -> Some(ConfigKey.join segs, s)
            | _ -> None)

    /// The items of an array under `key`, ordered by index (a missing key or an empty list gives `[]`).
    /// An element that is not a plain value is a `TypeMismatch`.
    let list (cfg: Config) (key: string) : Result<string list, ConfigError> =
        let sec = section cfg key

        let indexed =
            visible sec
            |> List.choose (fun (segs, r) ->
                match ConfigKey.tryIndex (List.head segs) with
                | Some i -> Some(i, segs, r)
                | None -> None)

        let bad =
            indexed
            |> List.tryFind (fun (_, segs, r) ->
                List.length segs > 1
                || (match r.Value with
                    | Text _ -> false
                    | _ -> true))

        match bad with
        | Some(_, _, r) ->
            Error(TypeMismatch(r.Key, "list of plain values", "nested value", Some (List.last r.Chain).Origin))
        | None ->
            Ok(
                indexed
                |> List.sortBy (fun (i, _, _) -> i)
                |> List.choose (fun (_, _, r) ->
                    match r.Value with
                    | Text s -> Some s
                    | _ -> None)
            )

    /// The direct children of `key` that have plain text values, as a map from child name to value.
    let map (cfg: Config) (key: string) : Map<string, string> =
        visible (section cfg key)
        |> List.choose (fun (segs, r) ->
            match segs, r.Value with
            | [ name ], Text s -> Some(name, s)
            | _ -> None)
        |> Map.ofList

    /// The winning value and the full override chain for a key (`None` when no layer mentioned it).
    let explain (cfg: Config) (key: string) : Explanation option =
        tryResolved cfg key
        |> Option.map (fun r ->
            { Key = r.Key
              Value =
                (match r.Value with
                 | Text s -> Some s
                 | _ -> None)
              Winner = (List.last r.Chain).Origin
              Chain = r.Chain })
