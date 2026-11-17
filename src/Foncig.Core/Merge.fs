namespace Foncig

/// How a later layer's array combines with an earlier layer's array at the same key.
type ListPolicy =
    /// If the later layer defines any `a:*`, all earlier `a:*` are discarded first. (Default.)
    | Replace
    /// .NET behaviour: `a:0` in a later layer replaces only that element.
    | MergeByIndex
    /// The later layer's items are renumbered after the existing ones.
    | Append

/// A global policy plus overrides per key prefix (longest matching prefix wins; case-insensitive).
type ListPolicies =
    { Default: ListPolicy
      Overrides: (string * ListPolicy) list }

/// One layer's say about a key (used for the override chain in `explain`).
type Override =
    { Origin: Provenance
      Value: EntryValue }

/// The merged state of one key: its first-written spelling, first-seen order and every override, oldest first.
type Resolved =
    { Key: string
      Order: int
      Value: EntryValue
      Chain: Override list }

type MergeState =
    { Items: Map<string, Resolved>
      Next: int }

module ListPolicies =
    let defaults = { Default = Replace; Overrides = [] }

    /// The policy for the list at `prefix`: the longest override at or above it, else the default.
    let policyFor (policies: ListPolicies) (prefix: string) : ListPolicy =
        policies.Overrides
        |> List.filter (fun (p, _) -> ConfigKey.isUnder p prefix)
        |> List.sortByDescending (fun (p, _) -> p.Length)
        |> List.tryHead
        |> Option.map snd
        |> Option.defaultValue policies.Default

module Merge =

    let empty: MergeState = { Items = Map.empty; Next = 0 }

    let private segmentsOf (key: string) =
        ConfigKey.split (ConfigKey.normalize key)

    /// Prefixes of length `depth` that are lists in this layer: followed by an index segment, or an `EmptyList`.
    let private listPrefixes (depth: int) (entries: Entry list) : string list =
        entries
        |> List.choose (fun e ->
            let segs = ConfigKey.split e.Key
            let n = List.length segs

            if n > depth && ConfigKey.isIndex (List.item depth segs) then
                Some(ConfigKey.join (List.take depth segs))
            elif n = depth && e.Value = EmptyList then
                Some e.Key
            else
                None)
        |> List.distinctBy ConfigKey.normalize

    let private isUnderStrict (np: string) (nk: string) =
        nk.StartsWith(np + ConfigKey.Separator, System.StringComparison.Ordinal)

    /// Every list prefix (ancestor followed by an index segment) of a key.
    let private listAncestors (key: string) : string list =
        let segs = ConfigKey.split key

        [ for i in 1 .. List.length segs - 1 do
              if ConfigKey.isIndex (List.item i segs) then
                  yield ConfigKey.join (List.take i segs) ]

    /// Renumber the layer's items under `prefix` (depth `depth`) to follow the existing ones.
    let private appendRewrite
        (items: Map<string, Resolved>)
        (prefix: string)
        (depth: int)
        (entries: Entry list)
        : Entry list =
        let np = ConfigKey.normalize prefix

        let existing =
            items
            |> Map.toList
            |> List.choose (fun (nk, _) ->
                if isUnderStrict np nk then
                    let segs = ConfigKey.split nk

                    if List.length segs > depth then
                        ConfigKey.tryIndex (List.item depth segs)
                    else
                        None
                else
                    None)

        let first = if existing.IsEmpty then 0 else (List.max existing) + 1

        let underPrefix (e: Entry) =
            isUnderStrict np (ConfigKey.normalize e.Key)
            && (let segs = ConfigKey.split e.Key in List.length segs > depth && ConfigKey.isIndex (List.item depth segs))

        let indices =
            entries
            |> List.filter underPrefix
            |> List.choose (fun e -> ConfigKey.tryIndex (List.item depth (ConfigKey.split e.Key)))
            |> List.distinct
            |> List.sort

        let mapping = indices |> List.mapi (fun rank idx -> idx, first + rank) |> Map.ofList
        let hasExisting = items |> Map.exists (fun nk _ -> isUnderStrict np nk)

        entries
        |> List.choose (fun e ->
            if underPrefix e then
                let segs = ConfigKey.split e.Key
                let idx = (ConfigKey.tryIndex (List.item depth segs)).Value

                let rewritten =
                    segs |> List.mapi (fun i s -> if i = depth then string mapping.[idx] else s)

                Some
                    { e with
                        Key = ConfigKey.join rewritten }
            elif e.Value = EmptyList && ConfigKey.normalize e.Key = np && hasExisting then
                None
            else
                Some e)

    /// Apply one layer (a source's entries, in order) on top of the state.
    let applyLayer (policies: ListPolicies) (state: MergeState) (layer: Entry list) : MergeState =
        let maxDepth =
            layer |> List.fold (fun m e -> max m (List.length (ConfigKey.split e.Key))) 0

        let mutable items = state.Items
        let mutable removed: Map<string, Resolved> = Map.empty
        let mutable entries = layer

        for depth in 1..maxDepth do
            for p in listPrefixes depth entries do
                match ListPolicies.policyFor policies p with
                | Replace ->
                    let np = ConfigKey.normalize p

                    let gone, keep =
                        items
                        |> Map.partition (fun nk r -> isUnderStrict np nk || (nk = np && r.Value = EmptyList))

                    items <- keep
                    removed <- gone |> Map.fold (fun acc k v -> Map.add k v acc) removed
                | MergeByIndex -> ()
                | Append -> entries <- appendRewrite items p depth entries

        let mutable next = state.Next

        for e in entries do
            let nk = ConfigKey.normalize e.Key
            // a list that gains items is no longer an empty list
            for anc in listAncestors e.Key do
                let na = ConfigKey.normalize anc

                match Map.tryFind na items with
                | Some r when r.Value = EmptyList && e.Value <> EmptyList -> items <- Map.remove na items
                | _ -> ()

            let prior =
                match Map.tryFind nk items with
                | Some r -> Some(r, true)
                | None -> Map.tryFind nk removed |> Option.map (fun r -> r, false)

            let link = { Origin = e.Origin; Value = e.Value }

            let resolved =
                match prior with
                | Some(r, true) ->
                    { r with
                        Value = e.Value
                        Chain = r.Chain @ [ link ] }
                | Some(r, false) ->
                    let order = next
                    next <- next + 1

                    { r with
                        Order = order
                        Value = e.Value
                        Chain = r.Chain @ [ link ] }
                | None ->
                    let order = next
                    next <- next + 1

                    { Key = e.Key
                      Order = order
                      Value = e.Value
                      Chain = [ link ] }

            items <- Map.add nk resolved items

        { Items = items; Next = next }
