# Fable target differences

How Foncig behaves on .NET, Fable JavaScript and Fable Python. Parsing and formatting are Foncig's own and give the
same results on all three, with the exceptions below. To compile for a Fable target, see
[Use Foncig with Fable](../how-to/use-with-fable.md).

Nothing in the public API is compiled out.

## Values

| Area | Behaviour on every target | What to do |
|---|---|---|
| Float text | Rendered canonically (`1e+21`, `1e-7`, `0.1`, `-0`), where .NET alone would print `1E+21` and Python `1e-07`. A number the parser supplies as text (`1.10`) keeps its text. | Nothing. |
| Number parsing | Strict, invariant-culture. `int64` is exact over its full range (JS sees `Long` objects). | Nothing. |
| `TimeSpan` | Parsed and formatted by Foncig, identically. | Format with `Scalar.formatTimeSpan`: `printfn "%A"` prints milliseconds on JS and ticks on Python. |
| `DateTimeOffset` | ISO 8601 shapes only; no zone means UTC. | Nothing. |
| `Uri` | Must be absolute. | A `Uri` default for `Decode.optional` is not supported (F# overload resolution fails, FS0064): use `Decode.optionalWith Readers.uri`. |
| Python strings | Code points, not UTF-16 units: a position column counts an astral character as one. | Nothing. |

## Host

| Target | Behaviour |
|---|---|
| JavaScript | Files are read through `node:fs`, reached at runtime (Node 20.16+/22.3+, Bun), so importing the library never fails in a browser. Without `process` (browsers) `Host.env` is empty and `Host.tryReadFile` returns `None`. Deno: environment and arguments are covered; files go through Deno's `process` compatibility and have not been tested. |
| Python | Files are read as UTF-8 and a BOM is stripped. Environment is `os.environ`, arguments are `sys.argv[1:]`. |

`Host` never throws.
