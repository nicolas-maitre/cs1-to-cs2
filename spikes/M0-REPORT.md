# M0 – feasibility spikes report

Status: **DRAFT – in-game verification pending** (verdicts marked *pending* until the user has confirmed in the game).

Environment: CS1 (Steam, Mono/.NET 3.5) with IMT 1.15, TM:PE, NC-R, WQ roads; CS2 1.6.0f1 with Road Builder **0.7.0** (installed; source read at 0.8.0), Anarchy, Move It, Traffic 0.2.12.

| # | Spike | Verdict | Code |
|---|---|---|---|
| 1 | CS2: place a road from code (vanilla + Road Builder prefab) | *pending in-game test* – compiles against game DLLs | [`m0-cs2-place-road/`](m0-cs2-place-road/) |
| 2 | CS1: export network + lane layouts + IMT fill polygons | **works** – ran on Skibidi.02 (see below) | [`m0-cs1-network-dump/`](m0-cs1-network-dump/) |
| 3 | WQ road → Road Builder config (+ IMT paint → bus lane / median) | *pending in-game check* – 5 configs generated from the real dump and placed in `ModsData\RoadBuilder` | [`m0-roadbuilder-configs/`](m0-roadbuilder-configs/) |
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

Results (Skibidi.02, CS1 1.21.1-f9, run 2026-11-20):
- [x] The dump completes: `network-20261120-170444.json`, 12.1 MB, with 7009 nodes, 7745 segments, 246 NetInfos (used roads plus their elevated/bridge/slope/tunnel variants) and 66 IMT fillers. The report has one entry, which is informational (`IMT assembly: IntersectionMarkingTool 1.15.0.0`), so no section failed.
- [x] The IMT fillers have plausible polygons and colours. 66 = **55 node fills** + **11 segment fills**, and every polygon is unique, so there are no duplicates.
  - The 55 node fills match a separate count taken from the save file: 38 red `rgba(136,0,0,94)`, 15 grey, and 2 `FillerPavement` islands.
  - The 11 segment fills are all red bus paint. The save-file count had looked at node markings only.
  - The segment polygons line up with lane boundaries; see the coverage results under Spike 3.
- Found while testing: the dumper adds variants of the used NetInfos, but not the *parent* of a variant. So a road that is only used elevated (`2094491511.Asymmetrical Three Lane Road Elevated0`) has no ground NetInfo in the dump. Fix in M1.

## Spike 3 – WQ road → Road Builder road

**Option vocabulary** (read from RB source, verified against the user's 29 existing configs; full tables in [`NOTES-roadbuilder.md`](m0-roadbuilder-configs/NOTES-roadbuilder.md)):
- `Type: "RoadConfig"`, `Version: 5`, `SpeedLimit` = **km/h × 2**, ID is free-form (we use deterministic `r<sha1-uuid>-cs1conv` so re-runs overwrite), omit `Playsets`.
- Lanes left→right; left half `Invert: true`. Groups: `Sidewalk` (`Parking` ""/P/B/H, `Width` 1–9 m), `Car` (`Lane Width` 3m/4m, `Transport Option` ""/Transport/Tram), **`Bus` is its own group** (dedicated bus lane), `Median` (1/2/5 m), `Parking` (2 m parallel no-marking), `Empty` (0.5–4 m filler), `Tram`, `Shoulder`, …
- **Decoration is not a lane option**: grass/trees come from road-level `Addons` flags by *position* (`GrassLeft/Right/Center`, `TreesLeft/Right/Center`), and only **5 m medians** or **≥ 5 m sidewalks** can carry them.
- Installed RB is 0.7.0, which has no fuzzy "closest variant" fallback → the generator only emits exact, existing combinations.

**Generator.** [`wq-to-rb.mjs`](m0-roadbuilder-configs/wq-to-rb.mjs) reads the dump, builds a left→right cross-section from CS1 lanes (merging lanes at the same position, folding edge parking/bike lanes into the sidewalk option), detects medians from gaps between opposite-direction lanes and tree props, and writes RB configs plus a width-comparison report.

**IMT paint → lane type** (new user requirement): [`imt-lanes.mjs`](m0-roadbuilder-configs/imt-lanes.mjs) tests every IMT fill polygon against every lane centre line. It offsets the segment bezier by the lane position; the side convention was checked against the CS1 IL (see [`NOTES-roadbuilder.md` §9](m0-roadbuilder-configs/NOTES-roadbuilder.md)).
- A segment fill that covers a lane end to end becomes a lane override: red solid → **bus lane**; pavement or grass island → **median** in that lane (grass forces a 5 m median, the only width that carries grass).
- Red paint over a parking lane *and* the car lane beside it becomes one bus lane, and the parking is dropped.
- Each (road, override set) becomes its own RB config with its own deterministic ID: `--out-imt`.
- On Skibidi.02, 7 road variants come out of 9 painted segments. Node fills (bus paint through junctions, 81 fill/lane contacts) and partial or diagonal fills are reported but don't change lanes.
- The city has no segment-level pavement or grass islands, so the median rule is proven by [`imt-lanes.test.mjs`](m0-roadbuilder-configs/imt-lanes.test.mjs) on synthetic data instead. It checks:
  - a pavement island → `Median 2m`;
  - grass on an inverted segment → median;
  - red paint over parking + lane → one bus lane;
  - a taper, a half-lane fill, a foreign segment's fill, a node fill and grey paint → no change.

**Roads chosen.** "WQ" is the Vanilla+ workshop pack (`<workshopId>.<name>_Data` NetInfos). Skibidi.02 has **no WQ road with a median**, so the median case uses the most-used vanilla median road.

| Config (`[CS1] …` in the RB menu) | CS1 NetInfo (uses) | CS1 → RB width | Notes |
|---|---|---|---|
| Four-Lane One-Way Road | `2094492458.Four-Lane One-Way Road_Data` (16) – plain WQ | 32 → **26 m** | CS1 has 9 m sidewalks; RB's maximum is 7 m → −2 m per side |
| Four-Lane Road with Decorative Trees | `Medium Road Decoration Trees` (294) – median | 32 → 31 m | 6 m tree median → 5 m (only 5 m medians carry trees); sidewalk + parking 6.8 → 7 m |
| Four-Lane One-Way Rd w/Bus Ln NP | `2160517773.Four-Lane One-Way Rd w/Bus Ln NP_Data` (14) – WQ bus lane | 16 → 16 m | exact |
| Five-Lane One-Way Road (IMT bus) | `2225246713.Five-Lane One-Way Road_Data` + red fill on centre lane (seg 36518) | 32 → 29 m | 6.5 m pavement + 2 m parking → 7 m sidewalk with parking |
| Six-Lane Road (IMT bus) | `Large Road` + red fill over left parking + curb lane (segs 394, 22549, 31666) | 32 → 30 m | parking under the paint dropped |

The other generated IMT variants are not deployed: Asymmetrical Three Lane ±1.5 bus, Asymmetrical Seven-Lane −9 / ±9 bus, Asymmetrical Five-Lane 8.5 bus. Their widths are exact or within 1 m.

Results: *pending in-game check*
- [ ] plain WQ road
- [ ] road with median (vanilla; no WQ median road in the city)
- [ ] WQ road with bus lane
- [ ] IMT-derived bus lanes (centre lane; parking absorbed)
- [ ] widths CS1 vs RB match the table above

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
- The exporter must also dump the **parent** NetInfo of every used variant (elevated-only roads).
- RB configs come from **(NetInfo × IMT lane overrides)**, so one CS1 road can need several RB roads (Large Asymmetric: 2). M2 must map each segment to its variant config, and should cap or merge rare variants.
- IMT **node** fills (bus paint through junctions, pavement islands at junctions) have no CS2/RB equivalent. They go into the import report and are candidates for a decal/surface pass later.
- RB sidewalks top out at 7 m. Wide WQ pavements (9 m) lose width, so either accept the narrower road or pad it with an `Empty` or grass section. Decide in M2 by checking the result against zoning in CS2.
