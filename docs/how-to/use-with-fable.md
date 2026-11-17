# Use Foncig with Fable

## Compile your app

The packages ship F# sources under `fable/`, so a plain `PackageReference` works:

```bash
dotnet fable MyApp.fsproj --lang js
dotnet fable MyApp.fsproj --lang python
pip install fable-library==<your Fable version>   # Python only
```

Requirements: Node 20.16+/22.3+ or Bun for file access in JS; Python 3.12+ for Python.

## Run the Foncig test suite on a Fable target

```bash
scripts/fable-test.sh          # JS and Python
scripts/fable-test.sh js
scripts/fable-test.sh python
```

The script needs .NET SDK 10, `bun` or `node` for JS, and Python 3.12+ (it creates a venv and installs
`fable-library==5.21.0`).

## Add a test file

Add it to both `tests/Foncig.Core.Tests` (or `Foncig.Host.Tests`) and `tests/Foncig.Fable/Foncig.Fable.fsproj`, then
regenerate the runner with `scripts/fable-gen-runner.fsx`.

## Add a dependency to a Foncig package

Declare it next to the normal reference as `<FablePackageReference Include="Id" Version="x" />`;
`build/Fable.targets` generates the `fable/` project from it.

Behaviour differences between targets: [Fable target differences](../reference/fable-targets.md).
