namespace Foncig

/// Flat configuration keys: segments joined by `:` (`Db:Host`, `Servers:0:Name`).
///
/// Rules:
/// - Comparison is case-insensitive (`normalize` lowercases invariantly); the casing of the first writer is kept.
/// - A segment must be non-empty and must not contain `:`. There is no escape syntax in v1: such keys are rejected
///   with `InvalidKey` (so a JSON file with `"Db:Host": "x"` is an error with a position, not a silent nesting).
/// - An array index is a segment of 1 to 9 ASCII digits.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ConfigKey =

    [<Literal>]
    let Separator = ":"

    /// The comparison form of a key.
    let normalize (key: string) : string = key.ToLowerInvariant()

    /// Split into segments. The empty key (the root) has no segments.
    let split (key: string) : string list =
        if key = "" then [] else key.Split(':') |> List.ofArray

    /// Join segments with `:`.
    let join (segments: string list) : string = String.concat Separator segments

    /// `combine "Db" "Host"` is `Db:Host`; an empty side is ignored.
    let combine (parent: string) (child: string) : string =
        if parent = "" then child
        elif child = "" then parent
        else parent + Separator + child

    /// The key without its last segment; `None` for a single-segment key or the root.
    let parent (key: string) : string option =
        let i = key.LastIndexOf ':'
        if i < 0 then None else Some(key.Substring(0, i))

    /// The last segment (the whole key when it has one segment).
    let lastSegment (key: string) : string =
        let i = key.LastIndexOf ':'
        if i < 0 then key else key.Substring(i + 1)

    /// True for a segment that is an array index: 1 to 9 ASCII digits.
    let isIndex (segment: string) : bool =
        segment.Length > 0
        && segment.Length <= 9
        && segment |> Seq.forall (fun c -> c >= '0' && c <= '9')

    let private toIndex (segment: string) : int =
        let mutable n = 0

        for c in segment do
            n <- n * 10 + (int c - int '0')

        n

    /// The index a segment stands for, if it is one.
    let tryIndex (segment: string) : int option =
        if isIndex segment then Some(toIndex segment) else None

    /// Check one segment: non-empty, no `:`.
    let validateSegment (segment: string) : Result<string, string> =
        if segment = "" then
            Error "a key segment cannot be empty"
        elif segment.IndexOf ':' >= 0 then
            Error "a key segment cannot contain ':' (it separates levels; there is no escape syntax)"
        else
            Ok segment

    /// Check a whole key: non-empty, no empty segments.
    let validate (key: string) : Result<string, string> =
        if key = "" then
            Error "a key cannot be empty"
        elif split key |> List.exists (fun s -> s = "") then
            Error "a key cannot contain an empty segment (leading, trailing or doubled ':')"
        else
            Ok key

    /// Build a key from segments, rejecting any segment that contains `:` or is empty.
    let ofSegments (segments: string list) : Result<string, string> =
        match
            segments
            |> List.tryPick (fun s ->
                match validateSegment s with
                | Error m -> Some m
                | Ok _ -> None)
        with
        | Some m -> Error m
        | None ->
            if segments.IsEmpty then
                Error "a key cannot be empty"
            else
                Ok(join segments)

    /// True when `key` equals `prefix` or lies under it (segment-aware, case-insensitive): `Db:Host` is under `db`
    /// but `Dbx` is not.
    let isUnder (prefix: string) (key: string) : bool =
        if prefix = "" then
            true
        else
            let p = normalize prefix
            let k = normalize key
            k = p || k.StartsWith(p + Separator, System.StringComparison.Ordinal)

    /// The part of `key` below `prefix`, or `None` when it is not strictly below it.
    let tryRelative (prefix: string) (key: string) : string option =
        if prefix = "" then
            Some key
        else
            let p = normalize prefix + Separator

            if normalize(key).StartsWith(p, System.StringComparison.Ordinal) then
                Some(key.Substring(p.Length))
            else
                None
