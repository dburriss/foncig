# Design decisions

Why Foncig behaves the way it does: the decisions taken in v1 and what is deferred. For the resulting rules, see the
[reference](../reference/concepts.md).

## Decisions

| Topic | Decision | Why |
|---|---|---|
| Keys | `:` separated, case-insensitive (invariant lower-casing), first writer's spelling kept. A segment containing `:` or an empty segment is rejected (`InvalidKey` with position). **No escape syntax.** | Matches .NET; rejecting is clearer than silently nesting `"Db:Host"` keys, and escapes would need a second syntax in env vars and CLI. |
| Scalars | Strings as is, `true`/`false`, ints exactly, floats via `Scalar.formatFloat`, timestamps as their original text. When the parser supplies the original number text (Fnode 0.2 `Parsed.ScalarText`) a float keeps it (`1.10` stays `1.10`). | Deterministic across targets; no surprises for version-like numbers. Ints always use the exact `int64` text (YAML `0x1F` reads as `31`). |
| Null | An explicit **unset** entry. It clears earlier layers. `tryGet` is `None`; `tryFindEntry` distinguishes unset from absent. | A file must be able to remove a value set below it. |
| Empty collections | Empty array: an `EmptyList` marker (under `Replace` it clears the earlier list; under `MergeByIndex`/`Append` it changes nothing). Empty object: no entries. Items arriving later remove the marker. | An override can say "this list is now empty" without an object ever needing a marker. |
| Duplicate keys in a file | **Last wins**, identical for JSON and YAML, done in `Flatten` (the same also for keys differing only by case); first spelling kept. Provenance reports the first occurrence's position (Fnode limitation, documented in Fnode 0.2). | Matches .NET. |
| Default list policy | **`Replace`**. `MergeByIndex` is the .NET behaviour, `Append` renumbers after the existing items. Per key prefix overrides, longest prefix wins. | Index merge surprises people; `Replace` is what they expect from "override the list". Property tests compare all three with a reference model. |
| Which keys are lists | Any key followed by an index segment (1 to 9 digits), and any `EmptyList` marker. | No schema is available at merge time. |
| Environment | `PREFIX_` filter (case-insensitive), prefix stripped, leading `_` trimmed, `__` to `:`. Names that are empty or have empty segments are ignored. Names differing only by case: the later in input order wins. | `APP__Db__Host` with prefix `APP_` yields `Db:Host`. |
| CLI | `--k v`, `--k=v`, `--k:sub v`, `/k=v`, bare flag = `"true"`. A next token is the value unless it starts with `-` (negative numbers are values). `/key` without `=`, bare tokens and everything after `--` are positionals. Unmapped single-dash switch is `UnknownSwitch(token, index)`. | `/key` alone is indistinguishable from a path. |
| Bool | `true`/`false` in any case. `1`/`0`, `yes`/`no` are rejected. | Strict and documented; use an explicit reader to be lenient. |
| Typed parsing | Own strict invariant parsers (`Scalar`), identical on all targets. | Fable's parsers ignore culture and styles. |
| Decoder | `Decoder<'a> = Config -> Result<'a, ConfigError list>`; `and!` accumulates all errors, `let!` short-circuits. | Applicative validation, no reflection. |
| Records | Explicit encoder (`Encode.*`, `ConfigSource.ofRecord`) mirrors the decoder. | Reflection-free binding in both directions. |
| Provenance | `{ Source; Detail }`: the source name plus `file:line:col`, the env var name or the CLI token. `Config.explain` returns the winner and the whole override chain (including layers cleared by `Replace`). | Core feature. |
| Errors | `ConfigError` union, `ConfigError.describe`. No exceptions across the public API; `Host` catches IO exceptions. | |
| Packages | Core is pure and depends on `Fnode.Core` only. Host, Json, Yaml, Defaults are separate. `Foncig.Defaults` is a fifth package (not in the original sketch) because the one-call setup needs Host, Json and Yaml, which the Host package should not force on everyone. It is `Defaults.configure`, not `Config.defaults`, since F# cannot add a function to another assembly's `Config` module. | Pay for what you use; a TOML adapter adds a package without touching the core. |
| Env name | `Defaults` finds the environment from `<APP>_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` (first set). | .NET convention plus an app-specific name. |

## Deferred

- **TOML:** an adapter package supplying a `Fnode.Format`; nothing in `Foncig` changes.
- **Reflection-free auto-derivation** of encoders/decoders from record shape (needs a generator or Fable plugin).
- **Reload / change watching** and **remote providers**, **secrets stores**: out of scope for v1.
- **Key escaping** for keys that contain `:`.
- Multi-document YAML streams as layers (`Yaml.parseAll` exists in Fnode).
- A `Uri` default for `Decode.optional` (see [Fable target differences](../reference/fable-targets.md)).
