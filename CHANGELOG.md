# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `Foncig.Core`: layered configuration with flat, case-insensitive, colon-separated keys; last source wins.
- Flattening of `Node` trees with deterministic, culture-free scalar text (identical on .NET, Fable JS and Fable Python),
  explicit `null` as "unset", and the original number text (`1.10`) kept when the parser supplies it.
- Provenance on every entry (`source`, `file:line:col`, env var name or CLI token) and `Config.explain` with the full
  override chain.
- List merge policies `Replace` (default), `MergeByIndex` (the .NET behaviour) and `Append`, global or per key prefix.
- Environment variable source (prefix filter, `__` as `:`) and command-line source (`--k v`, `--k=v`, `--k:sub v`,
  `/k=v`, bare flags, switch mappings, `--` positionals).
- Typed access (`int`, `int64`, `float`, `bool`, `Guid`, `Uri`, `TimeSpan`, `DateTimeOffset`, lists, maps), section
  views, and an applicative decoder with a `config { let! ... and! ... }` builder that accumulates every error.
- Reflection-free record sources through explicit encoders (`Encode`, `ConfigSource.ofRecord`).
- `Foncig.Host` (environment, arguments, file reading, home and working directory, path helpers, upward search),
  `Foncig.Json`, `Foncig.Yaml` and `Foncig.Defaults` (`appsettings`, environment file, user file,
  env vars, CLI in one call).
- Packages ship their F# sources under `fable/` for Fable JavaScript and Python consumers.
