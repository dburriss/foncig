/// Test-only platform helpers (temp directories, writing files, setting environment variables).
module Platform

open System
open Fable.Core

#if FABLE_COMPILER_PYTHON
[<Emit("__import__('tempfile').mkdtemp(prefix='fnc-')")>]
let private rawTempDir () : string = jsNative

[<Emit("__import__('os').makedirs($0, exist_ok=True)")>]
let private rawMkdir (_p: string) : unit = jsNative

[<Emit("open($0, 'w', encoding='utf-8', newline='').write($1)")>]
let private rawWrite (_p: string) (_t: string) : unit = jsNative

[<Emit("__import__('os').environ.__setitem__($0, $1)")>]
let setEnv (_name: string) (_value: string) : unit = jsNative
#else
#if FABLE_COMPILER_JAVASCRIPT
[<Emit("(function () { const fs = process.getBuiltinModule('node:fs'); const os = process.getBuiltinModule('node:os'); return fs.mkdtempSync(os.tmpdir() + '/fnc-'); })()")>]
let private rawTempDir () : string = jsNative

[<Emit("process.getBuiltinModule('node:fs').mkdirSync($0, { recursive: true })")>]
let private rawMkdir (_p: string) : unit = jsNative

[<Emit("process.getBuiltinModule('node:fs').writeFileSync($0, $1)")>]
let private rawWrite (_p: string) (_t: string) : unit = jsNative

[<Emit("(process.env[$0] = $1)")>]
let setEnv (_name: string) (_value: string) : unit = jsNative
#else
let private rawTempDir () : string =
    let d =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fnc-" + Guid.NewGuid().ToString("N"))

    System.IO.Directory.CreateDirectory d |> ignore
    d

let private rawMkdir (p: string) : unit =
    System.IO.Directory.CreateDirectory p |> ignore

let private rawWrite (p: string) (t: string) : unit = System.IO.File.WriteAllText(p, t)

let setEnv (name: string) (value: string) : unit =
    Environment.SetEnvironmentVariable(name, value)
#endif
#endif

/// A new empty directory.
let tempDir () : string = rawTempDir ()

/// Write a file (UTF-8, no BOM added), creating parent directories.
let writeFile (path: string) (text: string) : unit =
    let i = max (path.LastIndexOf '/') (path.LastIndexOf '\\')

    if i > 0 then
        rawMkdir (path.Substring(0, i))

    rawWrite path text
