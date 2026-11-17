namespace Foncig

open System
open Fable.Core

/// Platform shims: the only place that differs between .NET, Fable JavaScript (Node, Bun, Deno) and Fable Python.
/// The public signatures use plain F# types only.
module Host =

    // ---- raw platform calls (never exposed) ---------------------------------------------------------------
    // Nested #if instead of #elif: #elif needs F# language version 11.

#if FABLE_COMPILER_PYTHON
    [<Emit("[(k, v) for k, v in __import__('os').environ.items()]")>]
    let private rawEnv () : (string * string)[] = jsNative

    [<Emit("list(__import__('sys').argv[1:])")>]
    let private rawArgs () : string[] = jsNative

    [<Emit("open($0, encoding='utf-8-sig').read()")>]
    let private rawReadFile (_path: string) : string = jsNative

    [<Emit("__import__('os').path.isfile($0)")>]
    let private rawFileExists (_path: string) : bool = jsNative

    [<Emit("__import__('os').path.expanduser('~')")>]
    let private rawHome () : string = jsNative

    [<Emit("__import__('os').getcwd()")>]
    let private rawCwd () : string = jsNative
#else
#if FABLE_COMPILER_JAVASCRIPT
    [<Emit("((typeof process !== 'undefined' && process.env) ? Object.entries(process.env).filter(function (kv) { return kv[1] !== undefined; }) : (typeof Deno !== 'undefined' && Deno.env ? Object.entries(Deno.env.toObject()) : []))")>]
    let private rawEnv () : (string * string)[] = jsNative

    [<Emit("((typeof process !== 'undefined' && process.argv) ? process.argv.slice(2) : (typeof Deno !== 'undefined' ? Deno.args : []))")>]
    let private rawArgs () : string[] = jsNative

    [<Emit("process.getBuiltinModule('node:fs').readFileSync($0, 'utf8')")>]
    let private rawReadFile (_path: string) : string = jsNative

    [<Emit("process.getBuiltinModule('node:fs').existsSync($0)")>]
    let private rawFileExists (_path: string) : bool = jsNative

    [<Emit("((typeof process !== 'undefined' && process.env) ? (process.env.HOME || process.env.USERPROFILE || '') : '')")>]
    let private rawHome () : string = jsNative

    [<Emit("((typeof process !== 'undefined' && process.cwd) ? process.cwd() : '')")>]
    let private rawCwd () : string = jsNative
#else
    let private rawEnv () : (string * string)[] =
        let table = Environment.GetEnvironmentVariables()

        [| for key in table.Keys do
               let k = string key
               let v = table.[key]

               if not (isNull v) then
                   yield k, string v |]

    let private rawArgs () : string[] =
        Environment.GetCommandLineArgs() |> Array.skip 1

    let private rawReadFile (path: string) : string = System.IO.File.ReadAllText path

    let private rawFileExists (path: string) : bool = System.IO.File.Exists path

    let private rawHome () : string =
        match Environment.GetEnvironmentVariable "HOME" with
        | null
        | "" -> Environment.GetFolderPath Environment.SpecialFolder.UserProfile
        | home -> home

    let private rawCwd () : string = Environment.CurrentDirectory
#endif
#endif

    // ---- public API ---------------------------------------------------------------------------------------

    /// The process environment as (name, value) pairs. Empty where there is no environment (browsers).
    let env () : seq<string * string> =
        try
            rawEnv () |> Seq.ofArray
        with _ ->
            Seq.empty

    /// One environment variable.
    let tryEnv (name: string) : string option =
        env () |> Seq.tryFind (fun (k, _) -> k = name) |> Option.map snd

    /// The command-line arguments after the program (JS: after runtime and script; Python: `sys.argv[1:]`).
    let args () : string list =
        try
            rawArgs () |> List.ofArray
        with _ ->
            []

    /// Strip a leading UTF-8 byte order mark.
    let stripBom (text: string) : string =
        if text.Length > 0 && text.[0] = '﻿' then
            text.Substring 1
        else
            text

    /// The file's text (UTF-8, BOM stripped), or `None` when it does not exist or cannot be read. Never throws.
    let tryReadFile (path: string) : string option =
        try
            if rawFileExists path then
                Some(stripBom (rawReadFile path))
            else
                None
        with _ ->
            None

    let fileExists (path: string) : bool =
        try
            rawFileExists path
        with _ ->
            false

    /// The user's home directory, if known.
    let homeDir () : string option =
        try
            match rawHome () with
            | null
            | "" -> None
            | h -> Some h
        with _ ->
            None

    /// The current working directory (empty string where there is none).
    let cwd () : string =
        try
            rawCwd ()
        with _ ->
            ""

    // ---- paths (pure, '/'-based and tolerant of '\\' input) ----------------------------------------------

    let private isSep (c: char) = c = '/' || c = '\\'

    /// Join path parts with `/`, collapsing duplicate separators between parts. An absolute later part
    /// (leading `/`) does not reset the path; pass the absolute part first.
    let pathJoin (parts: string list) : string =
        parts
        |> List.filter (fun p -> p <> "")
        |> List.fold
            (fun (acc: string) (p: string) ->
                if acc = "" then
                    p
                else
                    acc.TrimEnd('/', '\\') + "/" + p.TrimStart('/', '\\'))
            ""

    /// The containing directory, or `None` at the root / for a bare name.
    let parentDir (path: string) : string option =
        let trimmed = path.TrimEnd('/', '\\')
        let i = max (trimmed.LastIndexOf '/') (trimmed.LastIndexOf '\\')

        if i < 0 then
            None
        elif i = 0 then
            (if trimmed.Length > 1 then Some "/" else None)
        elif i = 2 && trimmed.[1] = ':' then
            (if trimmed.Length > 3 then
                 Some(trimmed.Substring(0, 3))
             else
                 None)
        else
            Some(trimmed.Substring(0, i))

    /// `~` and `~/x` become the home directory (unchanged when there is none).
    let expandHome (path: string) : string =
        if path = "~" || (path.Length > 1 && path.[0] = '~' && isSep path.[1]) then
            match homeDir () with
            | Some h -> pathJoin [ h; path.Substring(1) ]
            | None -> path
        else
            path

    /// Opt-in discovery: the first directory from `start` upward that contains one of `fileNames`.
    /// Returns the full path of the first match. Stops at the filesystem root.
    let searchUpward (fileNames: string list) (start: string) : string option =
        let rec go (dir: string) (guard: int) =
            if guard > 64 then
                None
            else
                match fileNames |> List.map (fun f -> pathJoin [ dir; f ]) |> List.tryFind fileExists with
                | Some found -> Some found
                | None ->
                    match parentDir dir with
                    | Some up when up <> dir -> go up (guard + 1)
                    | _ -> None

        if start = "" then None else go start 0
