# M0 – feasibility spikes report

Status: **DRAFT – in-game verification pending** (verdicts marked *pending* until the user has confirmed in the game).

Environment: CS1 (Steam, Mono/.NET 3.5) with IMT 1.15, TM:PE, NC-R, WQ roads; CS2 1.6.0f1 with Road Builder **0.7.0** (installed; source read at 0.8.0), Anarchy, Move It, Traffic 0.2.12.

| # | Spike | Verdict | Code |
|---|---|---|---|
| 1 | CS2: place a road from code (vanilla + Road Builder prefab) | *pending in-game test* – compiles against game DLLs | [`m0-cs2-place-road/`](m0-cs2-place-road/) |
| 2 | CS1: export network + lane layouts + IMT fill polygons | *pending in-game test* – compiles, deployed | [`m0-cs1-network-dump/`](m0-cs1-network-dump/) |
| 3 | WQ road → Road Builder config | *pending dump + in-game check* – generator done | [`m0-roadbuilder-configs/`](m0-roadbuilder-configs/) |
| – | Bike lanes / trolleybus / monorail in CS2 + RB | **answered from source** (see below) | – |

---

## Spike 1 – CS2: place a road from code

**Approach.** A plain `GameSystemBase` (registered at `SystemUpdatePhase.ToolUpdate`) creates one *definition* entity with `CreationDefinition { m_Prefab = road prefab entity, m_Flags = Permanent | SubElevation }` + `NetCourse { m_Curve, m_StartPosition, m_EndPosition (CoursePos with IsFirst/IsLast), m_Elevation, m_Length, m_FixedIndex = -1 }` + `Updated` (+ `Deleted` so it is cleaned up). This is exactly what `NetToolSystem`'s definition job and the game's own `ZoneSpawnSystem.CreateSubNet` produce; the game's `CourseSplitSystem` → `GenerateNodesSystem` → `GenerateEdgesSystem` then build real nodes/edges. See [`NOTES-cs2-placement.md`](m0-cs2-place-road/NOTES-cs2-placement.md) for the full recipe with IL references.

**Prefab lookup.** Vanilla: `PrefabID("RoadPrefab", "Small Road")`. Road Builder: RB registers a `RoadBuilderPrefab : RoadPrefab` whose `name` **is the config ID**, so `PrefabID("RoadBuilderPrefab", configId)` works. Caveat: editing a road in the RB UI gives it a new ID.

**Collision checks off.** With `CreationFlags.Permanent` no `Temp` entities are created, so the tool validation system (overlaps, slope, cost) never runs – this is stronger than Anarchy, which keeps the temp flow and sets `ToolErrorFlags.DisableInGame` on the error prefabs for the duration of validation. *To verify:* what happens to trees/buildings under the new road.

**Toolchain.** The official CS2 modding toolchain is **not needed** for managed-only mods: a `net48` SDK project with `NoStdLib` referencing the game's own `Managed\*.dll` builds fine, deployed to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\<Name>\`. Constraints: no Burst, no `SystemAPI`, no `partial` systems / `IJobEntity` (those need the source generators + post-processor). For M2 bulk import this is fine (main-thread `EntityManager` writes of definition entities); Burst can be added later if import speed requires it.

In-game test (Ctrl+Shift+…): `1` Small Road, `2` all generated RB roads, `3` crossing road, `4` elevated (+12 m), `0` list road prefab names.

Results: *pending*
- [ ] mod loads (Logs/Modding.log)
- [ ] vanilla Small Road appears, is drivable, zoning works
- [ ] crossing road creates an intersection
- [ ] elevated road becomes a bridge with pillars
- [ ] Road Builder road (by config ID) appears
- [ ] what happens with overlapping objects

## Spike 2 – CS1: export the network

**Approach.** CS1 mod (`net35`, compiled against the game's own `mscorlib`, no reference-assembly package) with a button in *Options → Mod Settings*. Writes `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\M0NetworkDump\network-<timestamp>.json` containing:
- `nodes`: id, NetInfo, position, flags, elevation, height above terrain, connected segments
- `segments`: id, NetInfo, AI type, start/end node, start/end direction, full cubic **bezier** (via `NetSegment.CalculateMiddlePoints` with the game's smoothing rules), flags, `inverted`, `oneWay`, elevation kind (ground/elevated/bridge/slope/tunnel)
- `netInfos`: every used NetInfo **plus its elevated/bridge/slope/tunnel variants**, all simple fields (half width, pavement width, lane counts, …), AI fields, and every lane with all fields (`m_position`, `m_width`, `m_laneType`, `m_vehicleType`, `m_direction`, `m_speedLimit`, `m_verticalOffset`, `m_stopType`, …) plus lane props/trees (decoration: trees, lights, median props)
- `imtFillers`: world-space polygon, style class/type, RGBA colour and all style properties (elevation, theme, …) for every IMT filler on nodes and segments
- `report`: non-fatal problems; every section is isolated so a failure becomes a report entry

**IMT finding.** `IntersectionMarkingTool.API.dll` (`IMT.API.IFillerData`) exposes only a filler's entrance points – **no geometry and no style**. The exporter therefore walks IMT internals by reflection (`SingletonManager<NodeMarkingManager>.Instance → Marking.Fillers → MarkingFiller.Contour.TrajectoriesProcessed / Style.Value`). No compile-time dependency; if IMT changes, only the `imtFillers` section is lost.

Results: *pending*
- [ ] dump on Skibidi.02 completes (counts, file size, time)
- [ ] IMT fillers present with plausible polygons and colours

## Spike 3 – WQ road → Road Builder road

**Option vocabulary** (read from RB source, verified against the user's 29 existing configs; full tables in [`NOTES-roadbuilder.md`](m0-roadbuilder-configs/NOTES-roadbuilder.md)):
- `Type: "RoadConfig"`, `Version: 5`, `SpeedLimit` = **km/h × 2**, ID is free-form (we use deterministic `r<sha1-uuid>-cs1conv` so re-runs overwrite), omit `Playsets`.
- Lanes left→right; left half `Invert: true`. Groups: `Sidewalk` (`Parking` ""/P/B/H, `Width` 1–9 m), `Car` (`Lane Width` 3m/4m, `Transport Option` ""/Transport/Tram), **`Bus` is its own group** (dedicated bus lane), `Median` (1/2/5 m), `Parking` (2 m parallel no-marking), `Empty` (0.5–4 m filler), `Tram`, `Shoulder`, …
- **Decoration is not a lane option**: grass/trees come from road-level `Addons` flags by *position* (`GrassLeft/Right/Center`, `TreesLeft/Right/Center`), and only **5 m medians** or **≥ 5 m sidewalks** can carry them.
- Installed RB is 0.7.0, which has no fuzzy "closest variant" fallback → the generator only emits exact, existing combinations.

**Generator.** [`wq-to-rb.mjs`](m0-roadbuilder-configs/wq-to-rb.mjs) reads the dump, builds a left→right cross-section from CS1 lanes (merging lanes at the same position, folding edge parking/bike lanes into the sidewalk option), detects medians from gaps between opposite-direction lanes and tree props, and writes RB configs plus a width-comparison report.

Results: *pending*
- [ ] plain WQ road
- [ ] WQ road with median
- [ ] WQ road with bus lane
- [ ] widths CS1 vs RB

## Support matrix (CS2 + Road Builder)

| CS1 feature | CS2 / RB | Converter consequence |
|---|---|---|
| Painted bike lane in carriageway | **No.** RB bike groups are Pathway-only; on roads only *sidewalk with bike lane* (`Sidewalk Parking:"B"`, 3.5–7 m) | Map CS1 bike lanes to sidewalk-with-bike-lane; can't have both parking and bike lane on one side → report |
| Separate bike paths | Yes (RB `PathConfig` with Bike group, or vanilla bike paths) | Convert as paths |
| Trolleybus | **No** (not in RB, not in vanilla CS2 – latter from knowledge, not verified in game) | Convert to bus lane / bus line; report |
| Monorail | **No** (CS2 track types are Train, Tram, Subway only) | Drop or approximate as elevated train; report |
| Trams on roads | Yes: `Tram` lane group, or car/bus lane with `Tram` option | Supported |
| Dedicated bus lanes | Yes: `Bus` lane group | Supported |

## Changes to the M1–M2 plan

*(to be finalised after in-game results)*
- No official CS2 toolchain needed; keep importer managed-only for now.
- Importer places roads as permanent definitions → no Anarchy dependency for collisions; need our own cleanup of overlapping trees/props.
- Road mapping must be **per-NetInfo profile generation** (generated RB configs), not a lookup table; RB IDs are ours and deterministic. RB configs must be written **before CS2 starts** (RB loads them once at startup) → the web tool / a pre-step writes them, not the in-game importer.
- IMT data must be exported by reflection, not via the public API.
