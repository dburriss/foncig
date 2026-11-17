# Control how lists merge

By default a later layer that defines any `a:*` entry discards the earlier `a:*` entries (`Replace`). To change that:

**Globally**

```fsharp
builder |> ConfigBuilder.withListPolicy Append   // or MergeByIndex
```

**For one key prefix** (the longest matching prefix wins)

```fsharp
builder |> ConfigBuilder.withListPolicyFor "Servers" Append
```

| Policy | Effect |
|---|---|
| `Replace` | Default. The later layer's list replaces the earlier one. |
| `MergeByIndex` | .NET behaviour: `a:0`, `a:1` are merged item by item. |
| `Append` | Later items are renumbered after the existing ones. |

To clear a list from a later layer, set it to an empty array (`Tags: []`).

See [why `Replace` is the default](../explanation/design-decisions.md).
