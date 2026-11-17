# Use Foncig with Fable

## Compile your app

The packages ship F# sources under `fable/`, so a plain `PackageReference` works from a Fable project:

```bash
dotnet add package Foncig.Defaults
dotnet fable MyApp.fsproj --lang js
dotnet fable MyApp.fsproj --lang python
pip install fable-library==<your Fable version>   # Python only
```

## Runtime requirements

| Target | Requirement |
|---|---|
| JavaScript | Node 20.16+/22.3+ or Bun for reading files. Browsers work with no file access: `Host.env` is empty and `Host.tryReadFile` returns `None`. |
| Python | Python 3.12+ |

Environment variables, command-line arguments and files are read through `Foncig.Host`. A complete program for each
target is in [examples/](../../examples/README.md).

For behaviour differences between targets, see [Fable target differences](../reference/fable-targets.md).
