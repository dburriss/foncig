# Trace where a value came from

Call `Config.explain` on a built config:

```fsharp
Config.explain cfg "Db:Host" |> Option.iter (Explanation.describe >> printfn "%s")
```

It returns `None` when the key is not set, otherwise the winning value and every layer that set it, lowest first:

```
Db:Host = "cli"  <- args (--Db:Host=cli)
  1. "localhost"  appsettings.yaml:3:9
  2. "db.prod.internal"  appsettings.Production.yaml:3:9
  3. "cli"  args (--Db:Host=cli)
```

To tell "explicitly unset with `null`" from "never mentioned", use `Config.tryFindEntry`; `Config.tryGet` returns
`None` for both.
