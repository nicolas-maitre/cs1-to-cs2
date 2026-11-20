# M0 – feasibility spikes report

Status: **final** (2026-11-20). All spikes were run in game. Spikes 1 and 3 were tested by the user by hand. Spike 1b and the last Spike 3 checks were run by script through `tools/gamectl` and the Cs2Bridge `exec` command.

Environment: CS1 (Steam, Mono/.NET 3.5) with IMT 1.15, TM:PE, NC-R, WQ roads; CS2 1.6.0f1 with Road Builder **0.7.0** (installed; source read at 0.8.0), Anarchy, Move It, Traffic 0.2.12.

| # | Spike | Verdict | Code |
|---|---|---|---|
| 1 | CS2: place a road from code (vanilla + Road Builder prefab) | **works with caveats** – free ground, bridges, zoning and RB prefabs work; crossing an existing edge leaves `Temp` ghosts | [`m0-cs2-place-road/`](m0-cs2-place-road/) |
| 1b | CS2: T/X junctions through explicit shared nodes | **works** – X (one frame), X and T (attached to the node entity) and an 84-segment grid all give clean junctions with no `Temp` entities left | [`m0-cs2-junctions/`](m0-cs2-junctions/) |
| 2 | CS1: export network + lane layouts + IMT fill polygons | **works** – ran on Skibidi.02 (see below) | [`m0-cs1-network-dump/`](m0-cs1-network-dump/) |
| 3 | WQ road → Road Builder config (+ IMT paint → bus lane / median) | **works** – all 5 configs load and look right; widths in game match the generator exactly. The only loss is RB's 7 m sidewalk limit | [`m0-roadbuilder-configs/`](m0-roadbuilder-configs/) |
| – | Bike lanes / trolleybus / monorail in CS2 + RB | **answered from source** (see below) | – |

---

## Spike 1 – CS2: place a road from code

**Approach.** A plain `GameSystemBase` (registered at `SystemUpdatePhase.ToolUpdate`) creates one *definition* entity with `CreationDefinition { m_Prefab = road prefab entity, m_Flags = Permanent | SubElevation }` + `NetCourse { m_Curve, m_StartPosition, m_EndPosition (CoursePos with IsFirst/IsLast), m_Elevation, m_Length, m_FixedIndex = -1 }` + `Updated` (+ `Deleted` so it is cleaned up). This is exactly what `NetToolSystem`'s definition job and the game's own `ZoneSpawnSystem.CreateSubNet` produce; the game's `CourseSplitSystem` → `GenerateNodesSystem` → `GenerateEdgesSystem` then build real nodes/edges. See [`NOTES-cs2-placement.md`](m0-cs2-place-road/NOTES-cs2-placement.md) for the full recipe with IL references.

**Prefab lookup.** Vanilla: `PrefabID("RoadPrefab", "Small Road")`. Road Builder: RB registers a `RoadBuilderPrefab : RoadPrefab` whose `name` **is the config ID**, so `PrefabID("RoadBuilderPrefab", configId)` works. Caveat: editing a road in the RB UI gives it a new ID.

**Collision checks off.** With `CreationFlags.Permanent` no `Temp` entities are created, so the tool validation system (overlaps, slope, cost) never runs – this is stronger than Anarchy, which keeps the temp flow and sets `ToolErrorFlags.DisableInGame` on the error prefabs for the duration of validation. *To verify:* what happens to trees/buildings under the new road.

**Toolchain.** The official CS2 modding toolchain is **not needed** for managed-only mods: a `net48` SDK project with `NoStdLib` referencing the game's own `Managed\*.dll` builds fine, deployed to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\<Name>\`. Constraints: no Burst, no `SystemAPI`, no `partial` systems / `IJobEntity` (those need the source generators + post-processor). For M2 bulk import this is fine (main-thread `EntityManager` writes of definition entities); Burst can be added later if import speed requires it.

In-game test (Ctrl+Shift+…): `1` Small Road, `2` all generated RB roads, `3` crossing road, `4` elevated (+12 m), `5`/`6`/`7` junctions (Spike 1b), `9` junction inspection log, `0` list road prefab names.

Results (CS2, fresh Sweeping Plains city "M0 spike test", 2026-11-20, not saved; Road Builder, Traffic, Anarchy, Move It, TLE active):
- [x] **Mod loads:** `M0RoadSpike.log` shows OnLoad and no errors. Ctrl+Shift+0 lists 97 road prefabs, including all 5 `RoadBuilderPrefab "*-cs1conv"`.
- [x] **Small Road on empty land:** it is a real road. It gets an automatic street name and dead-end turnarounds, and it can be selected.
- [x] **Elevated (+12 m):** it becomes a proper bridge with pillars and is named automatically.
- [x] **Zoning:** placed roads, including Road Builder roads, get zone cells on both sides.
- [x] **Road Builder road by config ID:** all 5 were placed (see Spike 3).
- [ ] **FAIL – crossing an existing edge.** With Ctrl+Shift+3 no crossing road appears. Instead, the existing road turns into a pale-blue "ghost" that can't be selected and looks like an unapplied `Temp` preview.
  - The same happened where Ctrl+Shift+2 roads landed on existing highways: a highway section became a ghost, one RB road merged into the highway with a big terrain trench and retaining walls, and one RB road did not appear at all.
  - Cause (inferred): `CourseSplitSystem` splits the existing edge and creates its replacement through the `Temp` → `ApplyNetSystem` path. A `Permanent` definition never applies, so those replacement edges stay `Temp`.
- [x] **Trees under a new road** (checked by script in Spike 1b and Spike 3, in wooded areas): they get `Game.Common.Overridden` and disappear, as with the vanilla road tool. Under 68 grid edges and 5 RB roads, 0 live trees were left on the roads, and 26 were overridden.
- [ ] Not checked: buildings under a new road. The importer clears its area before building (see plan changes), so this does not block M1.

**Verdict: works with caveats.** Placing permanent roads from code works on free ground, including bridges, zoning, Road Builder prefabs and trees under the road. Crossings and connections to *existing* edges don't work with `Permanent` definitions.
- The importer has to build junctions itself, through shared nodes. Spike 1b shows that this works.

Testing note: `CameraPivot()` used the camera position plus 150 m along the forward direction projected onto the ground plane. When the camera looks down steeply, that point is far from what is on screen. Spike 1b changes it to the ground point under the screen centre.

## Spike 1b – CS2: junctions through explicit shared nodes

**Question.** Can the importer build T and X junctions without `CourseSplitSystem` touching an existing edge? It would do that by creating the junction's segments so that they share one node.

**Approach.** The tests run by script, with no hotkeys and no mouse. [`m0-cs2-junctions/`](m0-cs2-junctions/) is a plain class library that the Cs2Bridge hot-loads with `exec` (`Junctions.cs`, one entry point `M0Junctions.Ops.Run`, dispatched on `op`). [`run.mjs`](m0-cs2-junctions/run.mjs) drives it through the `tools/gamectl` module.
- **Phase.** Placement ops run with `phase:"tool"` (ToolUpdate), so they hit the same frame slot as the Spike 1 mod.
  - A coroutine started there continues on the main-loop pump (as documented in `tools/PROTOCOL.md`).
  - So steps that must each run in ToolUpdate, like x2's "arm, then attach", are separate `exec` calls.
- **Evidence ops** (`inspect`, `gridcheck`, `rbwidth`) read the ECS world and return JSON:
  - the nodes within 8 m of the centre, with their `ConnectedEdge` count and the far end of each edge;
  - real, Temp and deleted nodes and edges within 70 m;
  - `Temp` entity counts for the whole world (the ghost check).
- **Free ground.** The `find` op searches a grid for spots with no nets or buildings within 150 m and less than 4 m of height difference. Each spot was then checked with a screenshot. One candidate turned out to be open water, because `find` does not check water.
- **Test city.** "M0 spike test" (`20-November-19-00-19`), paused, autosave blocked by the bridge. Every road is a vanilla Small Road, arms 60 m long.

| Test | What it does | Result (inspect after 30+ frames) |
|---|---|---|
| **x1** – X, one frame | 4 arms from one centre point, all 4 definitions in the same exec call (same frame), every end `m_Entity = Null` at the identical centre `float3` | **1 node**, 0.00 m from the centre, **4 connected edges**. In 60 m: 5 real nodes, 4 real edges. Temp entities in the world: 0 |
| **x2** – X, two calls | call 1: the west arm, ending at the centre (in between: 2 nodes, 1 edge); call 2, a few frames later: looks up the real node at the centre (`49048:7`) and creates the E, N and S arms with `CoursePos.m_Entity = node` | **1 node**, **4 edges**, 5 real nodes, 4 edges, 0 Temp |
| **t2** – T, two calls | as x2 with only the E and N arms | **1 node**, **3 edges**, 4 real nodes, 3 edges, 0 Temp |
| **grid** – scale + mixed prefabs | 6×6 cells of 60 m: 84 segments created in **one** frame. Rows alternate Small Road / Medium Road, so every interior node joins two prefabs | **49/49 grid points correct**: 4 corners with 2 edges, 20 edge points with 3, 25 interior points with 4. No duplicate nodes, 0 Temp |
| **temp** – Temp → apply | the same X, but with definitions *without* `CreationFlags.Permanent`, then run the `ApplyTool` phase (`UpdateSystem.Update(SystemUpdatePhase.ApplyTool)`, which is what `ToolOutputSystem` does when the active tool's `applyMode` is `Apply`) | **nothing built.** A per-frame timeline (`tempx`) shows no Temp node or edge near the centre in any of the 12 frames after creation, so the apply finds 0 Temp entities. Likely cause, not proven: Temp output only lives until the next frame's tool phase, when the idle default tool clears it (`ApplyMode.Clear`), and the main-loop pump only sees the world after that. Not pursued further, because the Permanent variants already work |

**Screenshots** (local, `.gamectl/m0/`, git-ignored):
- x1, x2 and t2 each show one proper intersection: crosswalks on every arm, turn arrows, dead-end turnarounds at the arm ends, automatic street names (Umber/Pacific Street, …), and no pale-blue ghost.
- The grid is a clean street grid with traffic lights and crosswalks at the Small/Medium junctions. Each straight run gets one street name across several segments.

**Verdict: works.**
- Segments that share a node position merge into one junction, whether they are created in the same frame (x1, grid) or attach to an existing node entity (x2, t2).
- No `CourseSplitSystem` split happens, so no Temp ghosts appear.
- Mixed prefabs at one node work.
- Batching 84 segments in one frame works.

### Recommendation: how the M1/M2 importer creates junctions

1. **Clear the import area first.** If CS2 roads sit there, bulldoze them. A course must never cross or end on an existing edge mid-span; that is the Spike 1 ghost failure.
2. **One CS2 position per CS1 node.** Compute each CS1 node's `float3` once, then use that exact value as the course start or end of every segment at that node. Merging is by identical position, so never recompute the position per segment.
3. **Create the segments as `Permanent` definitions in batches** (an exec call or importer system in `ToolUpdate`). Within a batch, every end is `m_Entity = Null`, and `GenerateNodesSystem` merges shared positions into one node.
4. **Across batches,** after a batch is built, look up the real node entity at each boundary node's position (`Node` without `Temp`/`Deleted`, within 1 m) and keep a CS1 node ID → CS2 `Entity` map. Later segments at that node pass the entity as `CoursePos.m_Entity`, as x2 and t2 do.
5. **Don't use the Temp → apply path.** It is not reachable from outside a tool system, and the importer doesn't need it.
6. **Still to test in M2** (no blocker found):
   - grade-separated crossings: a bridge over a road with no shared node, and whether `CourseSplitSystem` leaves them alone;
   - curved segments;
   - roundabouts and very short segments (CS2's minimum edge length);
   - batch size and frame time for thousands of segments.

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

Results (same CS2 test city as Spike 1):
- [x] **All 5 configs load:** `RoadBuilder.log` reports "loaded 34 network configs" (29 existing + 5 `cs1conv`) with no errors. All 5 appear in the RB menu and can be placed by ID.
- [x] **Plain WQ road** (Four-Lane One-Way): a plain 4-lane one-way, which looks right.
- [x] **WQ road with bus lane** (Four-Lane One-Way Rd w/Bus Ln NP): 3 car lanes plus a red bus lane on the edge, which looks right.
- [x] **IMT bus lane that absorbs parking** (Six-Lane Road IMT bus): two-way with a yellow centre line and a BUS-marked lane on one side only, which looks right.
- [x] **Road with median** (Four-Lane with Decorative Trees) and **IMT centre bus lane** (Five-Lane One-Way IMT bus): re-tested by script on free, wooded ground (`node run.mjs rb 1900 -50`). All 5 roads were placed in parallel, 45 m apart.
  - The tree road has 2 + 2 lanes with a grass median and a row of trees, plus U-turn markings at the ends.
  - The Five-Lane road is one-way, with 2 lanes, a red bus lane in the centre lane, then 2 more lanes.
  - The other three look as they did in the manual test.
- [x] **Widths:** the edge width measured in the world (`EdgeGeometry` left to right at the middle of the edge) equals the prefab's `NetGeometryData.m_DefaultWidth`, and both equal the generator's table, to the metre:

  | Config | Generator (table above) | Prefab default width | Measured edge width |
  |---|---|---|---|
  | Four-Lane One-Way Road | 26 | 26 | 26 |
  | Four-Lane Road with Decorative Trees | 31 | 31 | 31 |
  | Four-Lane One-Way Rd w/Bus Ln NP | 16 | 16 | 16 |
  | Five-Lane One-Way Road (IMT bus) | 29 | 29 | 29 |
  | Six-Lane Road (IMT bus) | 30 | 30 | 30 |

  These numbers come from the game's own geometry rather than by reading the RB editor. That is a stronger check: it is the width the importer will actually get.
- [x] **Trees:** 22 trees under the five roads were overridden (hidden), and 0 live trees remain on them.
- One-way roads show dead-end warnings at both unconnected ends, which is expected.

**Verdict: works.**
- Road Builder accepts the generated configs as they are.
- All 5 road types look right in game, including the tree median and the IMT bus lanes, and their widths are exactly what the generator predicts.
- The real loss is width: 9 m WQ sidewalks become RB's 7 m maximum, a 32 m road becomes 26 m. That is a mapping decision for M2, not a feasibility problem.

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

- No official CS2 toolchain needed; keep importer managed-only for now.
- The importer places roads as permanent definitions, so it doesn't depend on Anarchy for collisions. Trees under new roads are overridden by the game itself. Buildings and props in the import area are cleared by the importer beforehand (see the next point).
- **Junctions: the importer builds every junction itself** (Spike 1b recommendation above).
  - One `float3` per CS1 node.
  - `Permanent` definitions in batches: shared positions merge within a batch, and later batches attach through the node entity (`CoursePos.m_Entity`).
  - A CS1 node ID → CS2 node entity map.
- **The importer must never let `CourseSplitSystem` split an existing edge.**
  - The import area must be free of CS2 roads beforehand, or the importer bulldozes them first.
  - Matching existing CS2 roads is out of scope.
  - Grade-separated crossings need a check early in M2.
- **Develop and test the importer by script.** `tools/gamectl` plus Cs2Bridge `exec` hot-loads a rebuilt DLL into the running game, with no restart. Every M0 CS2 check after the first manual round ran this way. Make the M2 importer's in-game steps `exec`-callable too, so that they can be regression-tested.
- Road mapping must be **per-NetInfo profile generation** (generated RB configs), not a lookup table; RB IDs are ours and deterministic. RB configs must be written **before CS2 starts** (RB loads them once at startup) → the web tool / a pre-step writes them, not the in-game importer.
- IMT data must be exported by reflection, not via the public API.
- The exporter must also dump the **parent** NetInfo of every used variant (elevated-only roads).
- RB configs come from **(NetInfo × IMT lane overrides)**, so one CS1 road can need several RB roads (Large Asymmetric: 2). M2 must map each segment to its variant config, and should cap or merge rare variants.
- IMT **node** fills (bus paint through junctions, pavement islands at junctions) have no CS2/RB equivalent. They go into the import report and are candidates for a decal/surface pass later.
- RB sidewalks top out at 7 m. Wide WQ pavements (9 m) lose width, so either accept the narrower road or pad it with an `Empty` or grass section. Decide in M2 by checking the result against zoning in CS2.
