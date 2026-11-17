# Contributing

Notes for working on Foncig itself. If you want to *use* Foncig, start at the [docs index](docs/index.md).

## Prerequisites

- .NET SDK 10 (pinned in `global.json`)
- `bun` or `node` for the Fable JS run (any runtime that runs ES modules; CI uses Node 22)
- Python 3.12+ for the Fable Python run (3.13 tested)

## Build and test

```bash
dotnet test Foncig.slnx                # .NET
scripts/fable-test.sh js               # Fable JS
scripts/fable-test.sh python           # Fable Python
scripts/fable-test.sh                  # both Fable targets
```

The same test files run on all three targets: xUnit and FsCheck on .NET, small shims under Fable. xUnit and FsCheck
cannot run under Fable, so `tests/Foncig.Fable` compiles the same files as `tests/Foncig.Core.Tests` and
`tests/Foncig.Host.Tests` through `XunitShim.fs` and `FsCheckShim.fs` (copied from fnode). The shim's FsCheck uses a
seeded generator, so property cases differ from real FsCheck's.

The Fable tool is pinned in `.config/dotnet-tools.json` and restored by the script. The Python run creates
`tests/Foncig.Fable/.venv` and installs `fable-library` at the same version.

Last verified (2026-11-17, macOS arm64): .NET 118/118, Fable JS (bun 1.4.2) 118/118, Fable Python (3.13) 118/118.

### Add a test file

Add it to the .NET test project and to `tests/Foncig.Fable/Foncig.Fable.fsproj`, then regenerate the runner with
`scripts/fable-gen-runner.fsx` (the generated `Runner.generated.fs` is git-ignored).

## Examples

`examples/` runs one program on each target. See [examples/README.md](examples/README.md).

## Fable packaging

Each package ships its F# sources and a generated project file under `fable/`, so consumers can use a plain
`PackageReference` from a Fable project. `build/Fable.targets` generates that project.

- Declare a dependency next to the normal reference as `<FablePackageReference Include="Id" Version="x" />`. A
  `ProjectReference` does not exist inside a package, so the generated file turns each one into a `PackageReference`.
- The generated project is written to `obj/<config>/pack-fable-project/`, not to a folder called `fable`: Fable would
  mistake a project reference inside `fable/` for a packed Fable package and fail to compile it.
- Fnode is versioned by `FnodeVersion` in `Directory.Build.props`, our packages by `Version`.
- This was checked by packing and compiling a consumer project from a local feed for JS and Python.

## Writing code that compiles on every target

`Foncig.Core` has one platform branch (`#if FABLE_COMPILER` in `Scalar.tryFloat`). Everything platform-specific lives
in `Foncig.Host/Host.fs`. Things we ran into:

| Area | Finding | What to do |
|---|---|---|
| `#elif` | Not available with F# language version 10 (needs 11). | Nest `#if FABLE_COMPILER_PYTHON` / `#else #if FABLE_COMPILER_JAVASCRIPT` / `#else`, as `Host.fs` does. |
| `String.LastIndexOfAny` | Not supported by Fable. | `max (s.LastIndexOf a) (s.LastIndexOf b)`. |
| Parsing numbers | Fable's `Double.TryParse` rejects culture and `NumberStyles` arguments (compile error) and is always invariant. | Validate syntax with our own grammar (`Scalar.isFloatText`), then parse under `#if FABLE_COMPILER`. `Int64` parsing is our own loop. |
| Culture-aware parsing | Unreliable for `DateTimeOffset` (also noted in flexi-yaml). | Parse the shapes yourself and build from components (`Scalar.tryDateTimeOffset`). |
| Record field inference | Fable's F# check resolved `e.Value` to a different record than the .NET compile did (`Explanation` vs `Entry`). | Annotate lambda parameters (`fun (e: Entry) -> ...`) in tests. |
| `%A` on `TimeSpan` | Prints milliseconds (JS) or ticks (Python). | Use `Scalar.formatTimeSpan`. |
| SRTP overloads | `Decode.optional` picks its reader from the default's type (`ReaderOf` in `Decode.fs`). A `Uri` overload fails type checking (FS0064). | Keep to the types that work; `Uri` goes through `optionalWith Readers.uri`. |
| Host calls | `node:fs` is reached with `[<Emit>]` through `process.getBuiltinModule`, never a static import. | Guard every call and catch, so `Host` never throws. |
| `Fable.Core` | 5.6.1 works with Fable 5.21.0. | `Foncig.Host` references it. |
| NuGet feed | A user-level feed may be unreachable. | `nuget.config` restores from nuget.org only (as fnode does). |

## Releasing

Releases are tag-driven: pushing a `v*` tag starts the workflows in `.github/workflows/`, and nothing talks to
nuget.org or GitHub from a developer machine.

- `scripts/publish.fsx` is the interactive release script: it runs a preflight build and test, asks for the next
  version, updates `Directory.Build.props` and moves `Unreleased` in [CHANGELOG.md](CHANGELOG.md) into a release, then
  commits and tags, and optionally pushes. The header of the script lists its arguments (`--fsproj`, `--solution`,
  `--dry-run`, ...).
- `scripts/pack.sh <version> [output-dir]` packs every package at one version. Its `PACKAGES` list is the list of
  packages in a release; a package must also be in `Foncig.slnx`.
- Record user-visible changes under `Unreleased` in the changelog ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/)).
