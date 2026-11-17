# Foncig.Defaults

`Defaults.configure "myapp"` returns a `ConfigBuilder` with the usual layers, lowest first: `appsettings.{json|yaml|yml}`,
`appsettings.{ENV}.{json|yaml|yml}`, `~/.myapp/config.{json|yaml|yml}`, `MYAPP_` environment variables, command-line
arguments. Every file is optional. `Defaults.options` exposes the directory, environment variable names, prefix,
switch mappings and the env/args providers.

## Documentation

- [Getting started](https://github.com/dburriss/foncig/blob/main/docs/tutorials/getting-started.md)
- [Concepts and API reference](https://github.com/dburriss/foncig/blob/main/docs/reference/concepts.md)
- [Foncig on GitHub](https://github.com/dburriss/foncig)
