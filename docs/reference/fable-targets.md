# Fable target differences

Reference for how `Foncig` behaves on .NET, Fable JavaScript and Fable Python. To compile or test under Fable, see
[Use Foncig with Fable](../how-to/use-with-fable.md).

Compiled with [Fable](https://fable.io) 5.21.0 (pinned in `.config/dotnet-tools.json`) from the same source. The core
(`Foncig.Core`) has one platform branch (`#if FABLE_COMPILER` in `Scalar.tryFloat`); everything else platform-specific
is in `Foncig.Host/Host.fs`. Fnode is versioned by `FnodeVersion` (0.2.0), our packages by `Version`.

Verified results (2026-11-17, macOS arm64): .NET 118/118, Fable JS (bun 1.4.2) 118/118, Fable Python (3.13) 118/118.
Node was not run locally; CI uses Node 22.

## Divergences between targets

| Area | Finding | What the code does |
|---|---|---|
| `#elif` | Not available with F# language version 10 (needs 11). | `Host.fs` nests `#if FABLE_COMPILER_PYTHON` / `#else #if FABLE_COMPILER_JAVASCRIPT` / `#else`. |
| Float text | .NET prints `1E+21`, JS and Python `1e+21`; Python `1e-07`. | `Scalar.formatFloat` re-renders the platform's shortest round-trip digits canonically (`1e+21`, `1e-7`, `0.1`, `-0`). Tested for `1e21`, `0.1`, negative zero, `5e-324`, `Double.MaxValue`. |
| Number parsing | Fable's `Double.TryParse` rejects culture and `NumberStyles` arguments (compile error) and is always invariant. | Syntax is validated by our own grammar first (`Scalar.isFloatText`); the parse call is `#if FABLE_COMPILER`. `Int64` parsing is our own loop (identical everywhere, full range). |
| int64 | Exact on all targets (JS sees `Long` objects). | `Int` flattens with `string`. Tested with `Int64.MaxValue`, `MinValue`, `9007199254740993`. |
| `String.LastIndexOfAny` | Not supported by Fable. | Use `max (s.LastIndexOf a) (s.LastIndexOf b)`. |
| Record field inference | Fable's F# check resolved `e.Value` to a different record than the .NET compile did (`Explanation` vs `Entry`). | Annotate lambda parameters (`fun (e: Entry) -> ...`) in tests. |
| `TimeSpan`/`%A` | `printfn "%A"` prints a `TimeSpan` as milliseconds (JS) or ticks (Python). | Format with `Scalar.formatTimeSpan`. Parsing and formatting are our own and identical. |
| `DateTimeOffset` | Culture-aware parsing is unreliable (also noted in flexi-yaml). | `Scalar.tryDateTimeOffset` parses ISO 8601 shapes itself and builds from components. No zone means UTC. |
| Python strings | Code points, not UTF-16 units: columns in positions count an astral character as one. | Positions come from Flexi.*, unchanged. |
| SRTP overloads | `Decode.optional "Db:Port" 5432` picks its reader from the default's type with a static-member overload trick. It works on all three targets. A `Uri` overload fails F# type checking (FS0064), so a `Uri` default is not supported: use `optionalWith Readers.uri`. | `ReaderOf` in `Decode.fs`. |
| `Uri` | `Uri.TryCreate` works on all three targets in the tests. | `Scalar.tryUri` requires an absolute URI. |
| JS host | `node:fs` is reached via `process.getBuiltinModule` (Node 20.16+/22.3+, Bun), not a static import, so importing the library never fails in a browser. Without `process` (browsers) `Host.env` is empty. Deno is covered for env/args only (Deno's `process` compatibility is used for files); not run here. | `Host.fs` `[<Emit>]` calls with guards; every call is wrapped so `Host` never throws. |
| Python host | `open(path, encoding='utf-8-sig')` strips a BOM; `os.environ`, `sys.argv[1:]`, `os.path`. | `Host.fs`. |
| Fable.Core | 5.6.1 works with Fable 5.21.0. | `Foncig.Host` references it. |
| NuGet | A user-level feed may be unreachable. | `nuget.config` restores from nuget.org only (as fnode does). |

## Left out under Fable

Nothing in the public API is compiled out. The file functions do real IO only through `Host`; in a runtime without
`node:fs` (browser) `Host.tryReadFile` returns `None`.
