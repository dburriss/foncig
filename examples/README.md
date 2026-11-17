# Examples

The same program (`shared/Example.fs`) on each target: layered YAML (`config/`), environment variables and
command-line arguments, then the typed settings and `explain` for a few keys. Run from the repository root.

```bash
export FNEX_ENVIRONMENT=Production FNEX__Db__Port=9999

# .NET
dotnet run --project examples/dotnet -- --Db:Host=cli -d

# Node / Bun (Fable JS)
dotnet tool restore
dotnet fable examples/node --lang js -o examples/node/out-js
bun examples/node/out-js/Program.js --Db:Host=cli -d      # or: node examples/node/out-js/Program.js ...

# Python (Fable Python, Python 3.12+)
dotnet fable examples/python --lang python -o examples/python/out-python
python3 -m venv .venv && .venv/bin/pip install fable-library==5.21.0
.venv/bin/python examples/python/out-python/program.py --Db:Host=cli -d
```

Layers, lowest first: the `defaults` record, `appsettings.yaml`, `appsettings.Production.yaml`
(because `FNEX_ENVIRONMENT=Production`), `~/.fnex/config.*`, `FNEX__*` variables, the command line.
`Tags` is replaced (not merged by index) by the production file: the default list policy is `Replace`.
