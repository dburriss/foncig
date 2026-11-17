namespace Foncig

/// Ordered sources plus list policies. Later sources override earlier ones.
type ConfigBuilder =
    { Sources: ConfigSource list
      Policies: ListPolicies }

module ConfigBuilder =

    let empty: ConfigBuilder =
        { Sources = []
          Policies = ListPolicies.defaults }

    /// Append a source: it overrides everything added before it.
    let add (source: ConfigSource) (builder: ConfigBuilder) : ConfigBuilder =
        { builder with
            Sources = builder.Sources @ [ source ] }

    /// Put a source below everything added so far (the lowest layer), for example a record of defaults.
    let addFirst (source: ConfigSource) (builder: ConfigBuilder) : ConfigBuilder =
        { builder with
            Sources = source :: builder.Sources }

    let addMany (sources: ConfigSource list) (builder: ConfigBuilder) : ConfigBuilder =
        sources |> List.fold (fun b s -> add s b) builder

    /// Shortcut: one in-memory override layer with a single key.
    let addSet (key: string) (value: string) (builder: ConfigBuilder) : ConfigBuilder =
        add (ConfigSource.inMemory "set" [ key, value ]) builder

    /// The list policy for every list (default `Replace`).
    let withListPolicy (policy: ListPolicy) (builder: ConfigBuilder) : ConfigBuilder =
        { builder with
            Policies =
                { builder.Policies with
                    Default = policy } }

    /// Override the list policy for a key prefix (`Servers`, `Logging:Sinks`).
    let withListPolicyFor (prefix: string) (policy: ListPolicy) (builder: ConfigBuilder) : ConfigBuilder =
        { builder with
            Policies =
                { builder.Policies with
                    Overrides = builder.Policies.Overrides @ [ prefix, policy ] } }

    /// Load every source and merge. All source errors are collected, in source order.
    let build (builder: ConfigBuilder) : Result<Config, ConfigError list> =
        let loaded = builder.Sources |> List.map (fun s -> s.Load())

        match
            loaded
            |> List.collect (function
                | Error es -> es
                | Ok _ -> [])
        with
        | [] ->
            loaded
            |> List.choose (function
                | Ok entries -> Some entries
                | Error _ -> None)
            |> List.fold (Merge.applyLayer builder.Policies) Merge.empty
            |> Config.ofState
            |> Ok
        | errors -> Error errors
