# Foncig.Core

The pure core of a layered configuration library for F# (.NET, Fable JS, Fable Python): flat colon-separated keys,
last source wins, provenance and `explain`, list merge policies (`Replace`, `MergeByIndex`, `Append`), env and CLI
sources, typed access, and an applicative decoder. No IO, no reflection.

File reading and the real environment live in `Foncig.Host`; formats in `Foncig.Json` and
`Foncig.Yaml`; the one-call setup in `Foncig.Defaults`.

## Documentation

- [Getting started](https://github.com/dburriss/foncig/blob/main/docs/tutorials/getting-started.md)
- [Concepts and API reference](https://github.com/dburriss/foncig/blob/main/docs/reference/concepts.md)
- [Foncig on GitHub](https://github.com/dburriss/foncig)
