namespace Foncig

/// Where a configuration value came from. `Source` is the name of the source (a file path, "env", "args", ...);
/// `Detail` pins it down further: `file:line:col`, the environment variable name, or the CLI token.
type Provenance =
    { Source: string
      Detail: string option }

/// What one layer says about one key.
type EntryValue =
    /// A text value.
    | Text of string
    /// An explicit "unset": clears the value set by earlier layers (a `null` in a file).
    | Unset
    /// An empty array. It has no children, so this marker lets a later layer replace a list with an empty one.
    | EmptyList

/// One flattened key with the value a source gave it and where that came from.
type Entry =
    { Key: string
      Value: EntryValue
      Origin: Provenance }

/// Structured configuration errors. No exceptions cross the public API; everything is one of these.
type ConfigError =
    /// A required file could not be read.
    | FileNotFound of path: string
    /// The file text could not be parsed. `line`/`column` are 1-based (0 when the parser gave none).
    | ParseError of file: string * line: int * column: int * message: string * snippet: string
    /// A key (from a file, env var, CLI token or the caller) is not usable.
    | InvalidKey of key: string * reason: string * origin: Provenance option
    /// A YAML mapping used a key that is not a string.
    | NonStringKey of file: string * line: int * column: int * detail: string
    /// A value could not be read as the expected type.
    | TypeMismatch of key: string * expected: string * actual: string * origin: Provenance option
    /// A required key has no value.
    | Missing of key: string
    /// A single-dash CLI switch with no switch mapping.
    | UnknownSwitch of token: string * index: int
    /// No parser is registered for the file extension.
    | UnknownFormat of path: string
    /// Several errors from one operation.
    | Aggregate of ConfigError list

module Provenance =
    /// `source`, `source (detail)`, or just `detail` when it already starts with the source (`file:3:9`).
    let describe (p: Provenance) : string =
        match p.Detail with
        | Some d when d.StartsWith p.Source -> d
        | Some d -> p.Source + " (" + d + ")"
        | None -> p.Source

module ConfigError =

    let private at (line: int) (column: int) =
        if line > 0 then
            ":" + string line + ":" + string column
        else
            ""

    let private from (origin: Provenance option) =
        match origin with
        | Some o -> " [set by " + Provenance.describe o + "]"
        | None -> ""

    /// A message for humans, including the source name and position where known.
    let rec describe (e: ConfigError) : string =
        match e with
        | FileNotFound path -> "File not found: " + path
        | ParseError(file, line, column, message, snippet) ->
            let tail = if snippet = "" then "" else "\n    " + snippet
            "Cannot parse " + file + at line column + ": " + message + tail
        | InvalidKey(key, reason, origin) -> "Invalid key '" + key + "': " + reason + from origin
        | NonStringKey(file, line, column, detail) ->
            "Non-string mapping key in " + file + at line column + ": " + detail
        | TypeMismatch(key, expected, actual, origin) ->
            "Key '"
            + key
            + "' expected "
            + expected
            + " but found '"
            + actual
            + "'"
            + from origin
        | Missing key -> "Missing required key '" + key + "'"
        | UnknownSwitch(token, index) ->
            "Unknown switch '"
            + token
            + "' at argument "
            + string index
            + " (no switch mapping)"
        | UnknownFormat path -> "No parser registered for file extension: " + path
        | Aggregate errors -> errors |> List.map describe |> String.concat "\n"

    /// Flatten nested `Aggregate`s into a plain list.
    let rec flatten (e: ConfigError) : ConfigError list =
        match e with
        | Aggregate es -> List.collect flatten es
        | other -> [ other ]
