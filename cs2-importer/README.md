# cs2-importer

Builds the roads of a `cs1conv-plan.json` (import package, [schema/README.md](../schema/README.md) §2) in a running Cities: Skylines II city. M1 is roads only: networks, junctions, curves, bridges and tunnels.

The same code (`src/Core` + `src/Game`) ships two ways:

| Build | Project | For |
| --- | --- | --- |
| `Cs2Importer.dll`, an exec library | `Cs2Importer.csproj` | Scripts. The Cs2Bridge hot-loads it with `exec` ([tools/PROTOCOL.md](../tools/PROTOCOL.md)), so a rebuild takes effect on the next call. |
| `Cs2ImporterMod.dll`, a mod (M2) | `mod/Cs2ImporterMod.csproj` | Players. **Options → CS1 → CS2 importer** shows the import page. `dotnet build -c Release` deploys it to `Mods\Cs2Importer`. |

The whole pipeline, from CS1 to a saved CS2 city, runs with [`tools/e2e/run.mjs`](../tools/e2e/README.md). Decisions and their reasons: [DECISIONS-importer.md](DECISIONS-importer.md).

## The mod's Options page

| Control | What it does |
| --- | --- |
| Package folder / Packages on this PC | The import package (the folder with `cs1conv-plan.json` and `roadbuilder\`). The dropdown lists packages under `%LOCALAPPDATA%\cs1-to-cs2`; picking one also runs Check. |
| **Check** | Plan check, plus whether every road type is available. For a missing Road Builder road it says whether its config isn't installed yet ("press Install…, then restart") or is installed but not loaded ("restart the game"). It also counts roads already in the area. |
| **Install Road Builder configs** | Copies the package's `*-cs1conv.json` to `ModsData\RoadBuilder`, after a confirmation. Road Builder reads configs only at startup, so the game must be restarted. |
| **Prepare map** (group "New city") | In a new city, before Import, after a confirmation: removes the base map's roads, tracks and buildings, then applies the package's terrain and world map, the CS1 sea level and water sources (or a dry map), and checks the terrain against the file and the water at the package's checkpoints. It uses the terrain worker's library (`terrain/cs2-apply`, compiled in; `mod/MapSteps.cs`). About 15 s. |
| Clear the area first | On by default. Bulldozes the existing roads in the area. |
| Outside connections | On by default, the last step. Where CS1 highways and train tracks leave the crop, the imported end nodes become CS2 outside connections (`plan.outsideConnections`, `MapPrep oc.create`). |
| Transit lines | On by default. CS1 bus and tram stops and lines (names, numbers, colours) via the transit worker's library (`transit/cs2-exec`, compiled in; `mod/TransitStep.cs`), after the buildings. Metro and train lines need the stations from the buildings step. |
| Buildings, trees, districts | On by default. The buildings/ worker's steps (`buildings/src/Steps.cs`, compiled into the mod: buildings, objects, districts, surfaces) run after zoning. A plan without those sections reports them as skipped. |
| Zoning / Traffic lights, stops, turn bans, crosswalks / Priority signs (Traffic mod) | On by default. The M2 steps run after the roads in one chain, and their results show in the status line and the combined report. Other mods can add steps with `Cs2ImporterMod.ImportSteps.Register` (see `mod/ImportSteps.cs`). |
| **Import into this city** | After a confirmation, builds the roads over the next frames. It is disabled while an import runs. |
| Status | Progress ("batch 3 of 6 …"), then the summary: roads built / planned, problems by kind, skipped roads, leftover ghosts, and the path of the full report (`%LOCALAPPDATA%\cs1-to-cs2\import-reports\`). |

**Testing by script:** the exec library's op `mod` sets these values and presses the buttons through the same `Actions` the page calls:

```bash
gamectl cs2 exec cs2-importer/bin/Release/net48/Cs2Importer.dll --args '{"op":"mod","set":{"PackageFolder":"…","OffsetX":2000,"OffsetZ":-4000},"press":"Check"}'
gamectl cs2 exec cs2-importer/bin/Release/net48/Cs2Importer.dll --phase tool --pump tool --args '{"op":"mod","press":"PrepareMap","wait":true}'
gamectl cs2 exec cs2-importer/bin/Release/net48/Cs2Importer.dll --phase tool --pump tool --args '{"op":"mod","press":"Import","wait":true}'
gamectl cs2 exec cs2-importer/bin/Release/net48/Cs2Importer.dll --args '{"op":"mod","press":"WaterCheck"}'   # CS2 water vs the package's CS1 checkpoints
```

**The player's flow** (verified 2026-11-21, no Node tools; see the results below):
1. Unzip the web tool's package under `%LOCALAPPDATA%\cs1-to-cs2`.
2. Start CS2, pick the package, press **Install Road Builder configs**, then restart CS2.
3. Start a new city (Sunshine Peninsula has the fewest base-map nets).
4. Options → CS1 → CS2 importer: pick the package; it runs Check.
5. Press **Prepare map**, then **Import into this city**.
6. Unpause for a few minutes so rivers and lakes fill.

## Quick start (scripts)

```bash
# once per package, with CS2 closed: Road Builder reads its configs only at startup
node cs2-importer/run.mjs copy-rb <package dir>

node cs2-importer/run.mjs start                     # takes the CS2 lock, starts CS2 through gamectl
node cs2-importer/run.mjs new --name cs1conv-m1     # new city on Corral Riches (CS1CONV_MAP / --map)
node cs2-importer/run.mjs check  <package dir>      # plan + prefab check, no changes
node cs2-importer/run.mjs import <package dir> --clear   # bulldoze the area, build, verify; report in .gamectl/import/
node cs2-importer/run.mjs shots  <package dir>      # overview + close-up screenshots
node cs2-importer/run.mjs save cs1conv-m1-<name>
node cs2-importer/run.mjs stop                      # quits CS2, releases the lock
```

`check`, `import`, `verify`, `clear` and `shots` shift the plan by `--offset x,z` (default `2000,-4000`, env `CS1CONV_OFFSET`), the flattest dry spot of Corral Riches; `--offset 0,0` uses the plan's own coordinates. `run.mjs` builds the DLL (`dotnet build -c Release`, needs `CS2Managed` in `../local.props`) before each `exec`. The same ops work through plain gamectl:

```bash
gamectl cs2 exec cs2-importer/bin/Release/net48/Cs2Importer.dll --phase tool --pump tool \
  --args '{"op":"import","plan":"C:/…/cs1conv-plan.json","clear":true}' --timeout 3600
```

## Ops (`Cs2Importer.Entry.Run`, `args.op`)

| Op | exec params | What it does |
| --- | --- | --- |
| `check` | phase main | Parses and validates the plan, looks up every prefab (with a hint for missing Road Builder configs), counts CS2 roads already in the area. |
| `clear` | phase tool, pump tool | Bulldozes top-level CS2 roads in the plan's bounds + `margin` (50 m), or in `x0,z0,x1,z1`. |
| `import` | phase tool, pump tool | Optional `clear`, then the batches, then a verify pass. Returns the report; `report` writes it to a file too. |
| `verify` | phase main | Re-checks an earlier import against the plan. Segments CS2 built through a fork of its own (two courses leaving a node at a shallow angle share their first metres) count as built and are listed in `notes_by_kind.merged-fork`. |
| `terrain` | phase main | Height range, water and roads in a square (`x, z, size=3200, step=50`): for picking a test spot. |
| `inspect` | phase main | Real nodes within `r` of `x, z` with their edges: for debugging a failure. |
| `mod` | phase main; Import with `wait`: phase tool, pump tool | Drives the deployed mod's Options page: `set`, `press`, `wait`. |
| `traffic` | phase tool, pump tool | M2: traffic lights, all-way stops (node `Upgraded`), turn bans and crosswalks (edge `Upgraded`) from `plan.traffic`. |
| `trafficmod` | phase tool, pump tool | M2: Traffic mod priority signs (by reflection); lane connections, arrows and U-turns are counted only. Skipped if the mod is missing. |
| `zoning` | phase tool, pump tool | M2: paints `plan.zoning.cells` onto the zone blocks of the imported roads; `checkOnly` only measures. |
| `view` | phase main | `{tool: "zone"}` shows the zone overlay for screenshots; `{tool: "default"}` turns it off again. |

Options: `keepEmbankments` (false), `heights` (`flat`; `exact` = CS2 terrain + CS1 `heightAboveTerrain`, experimental, see DECISIONS M2), `trace` (file: segment ids per batch, written before the batch), `offsetX` / `offsetZ` (0), `batchSize` (400), `cellSize` (400 m), `settleFrames` (2), `verifyFrames` (30), `groundSnap` (1 m), `minLength` (0.5 m), `shortLength` (4 m), `margin` (50 m).

## How it builds

1. **Plan → job** (`src/Core`, no game code): skip invalid segments; one elevation per node from the segment ends, plus CS1 trenches of 4 m or more (clamped to −11.5 m). Embankments and smaller offsets snap to the terrain, because buildings can't connect to embankment walls (`keepEmbankments` keeps them, clamped to +7.5 m). Spatial batches.
2. **Prefabs**: `PrefabID(type, name)` for every key the segments use. A missing prefab fails its segments and is listed in the report.
3. **One position per node**: CS2 terrain height + node elevation, computed once, used by every segment at that node.
4. **Batches** in `ToolUpdate`: `Permanent` road definitions; ends shared within a batch merge into one node; ends on a node built by an earlier batch attach to its entity (`CoursePos.m_Entity`). After each batch the new nodes are looked up (within 1 m) to extend the node map.
5. **Verify**: a segment counts as built when real edges connect its two node entities. The report also has elevated and tunnel compositions, edges with an unexpected prefab, other edges left in the area and the world's `Temp` count (ghosts).

## Report

`import` returns (and `run.mjs` writes to `.gamectl/import/<tag>-report.json`):

- `plan`: segments and nodes in the plan / to build, skipped by reason, ground / elevated / tunnel, batches.
- `bounds`: the crop and the CS2 bounds actually used, and whether they fit the CS2 map.
- `prefabs`: every key with `found`, width, elevation limit, segment count and a hint when missing.
- `clear`: edges and nodes deleted, when `clear` was set; otherwise `existing_edges_in_area`.
- `batches`: per batch, segments created, ends attached to earlier nodes, nodes resolved / missing, frames, `create_ms`, `resolve_ms`, `wall_ms`.
- `result`: `segments_built` / `segments_failed`, `cs2_edges_for_plan`, `nodes_found`, `elevated_built` vs `elevated_planned`, `tunnel_built` vs `tunnel_planned`, `wrong_prefab`, `other_cs2_edges_in_area`.
- `failures_by_reason`, `failures` (10 samples per reason), `warnings_by_kind`, `warnings`, `skipped`, `temp_before` / `temp_after`, `seconds`.

## Shared driver code

`lib/driver.mjs`: game locks (`%LOCALAPPDATA%\cs1-to-cs2\<game>.lock`), the importer build, plan screenshots and the report summary. `run.mjs` and `tools/e2e/run.mjs` both use it.

## Tests

```bash
cd cs2-importer/tests/CoreTests && dotnet run                       # game-free core tests
cd cs2-importer/tests/CoreTests && dotnet run -- plan <package dir> # dry-run a real plan through the planner
node cs2-importer/samples/make-handmade-plan.mjs [--at x,z]           # regenerate the hand-made test plan
```

`samples/m2-geometry/` (`make-geometry-plan.mjs`) has the M2 geometry cases, one segment-id range per case: a roundabout, short segments (0.7–6 m), a 45° overpass, an underpass, rail over a road, a tight r = 15 m curve, a single 400 m curve, an S-curve, an acute 15° Y, a 6-way junction, near-parallel roads and a ramp chain.

`samples/m1-handmade/` is a 24-segment plan: a 3×3 grid of Small and Medium roads (X and T junctions), a curve, a Medium Road bridge over a Small Road with no shared node, a Small Road tunnel and a Road Builder road (the M0 `Four-Lane Road with Decorative Trees` config, `r5266315b-…-cs1conv`, which must be in `ModsData/RoadBuilder`).

## M1 results (2026-11-21, CS2 1.6.0f1, Road Builder 0.7.0)

New city on Corral Riches, plan shifted by (+2000, −4000), simulation paused, autosave blocked by the bridge.

| Plan | Segments built | Nodes | CS2 edges | Temp | Time | Save |
| --- | --- | --- | --- | --- | --- | --- |
| `samples/m1-handmade` (batches of 5) | 24 / 24 | 23 / 23 | 33 | 0 | 1.9 s, 5 batches | `cs1conv-m1-handmade` |
| same, re-imported with `--clear` | 24 / 24 (33 edges, 32 nodes bulldozed first) | 23 / 23 | 33 | 0 | 0.7 s | – |
| Skibidi.02 crop (web tool, 3 km at (3750, −1250)) with `--clear` | **2035 / 2035** | 1670 / 1670 | 2188 | 0 | 1.6 s, 6 batches of ≤ 400 | `cs1conv-m1-skibidi` |

- **Skibidi report:** all 26 prefabs found: 22 generated Road Builder configs, plus `Double Train Track`, `Oneway Train Track`, `Double Subway Track` and `Pavement Path`.
  - No failures and no unclaimed edges in the area.
  - 130 edges have an elevated composition (109 planned CS1 bridges / elevated segments, plus ramps); 21 have a tunnel composition (10 planned).
  - Each batch takes 6–9 ms to create its definitions and 50–150 ms of wall time, including the 2 settle frames.
- **The game adds edges:** CS2 splits long courses, so 2035 segments become 2188 edges. The verifier walks through those intermediate nodes.
- **Screenshots** (local, `.gamectl/import/`, git-ignored):
  - `handmade-*`: the grid with crosswalks and traffic markings; the curve; the Road Builder tree road; a Medium Road bridge with pillars over Aspen Street, with no junction; a tunnel ramp into a lowered cutting.
  - `skibidi-overview` / `-oblique`: the whole 3 km street network: the grid, curved residential loops, the sunken highway across the middle, and the rail lines on the east side. Orange dead-end markers sit where roads stop at the crop edge.
  - `skibidi-elevated`: the highway interchange viaducts and an elevated rail line on pillars.
  - `skibidi-trench-crossing`: the Two Lane Highway in its retaining-wall trench, with Elm Street bridging over it.
- **First Skibidi run (before DECISIONS-importer.md #7):** 2034 / 2035. One CS1 road that bridged the trenched highway met it at grade, and CS2 joined them with a junction.

## M2 results (2026-11-21)

| Run | Built | Time | Notes |
| --- | --- | --- | --- |
| Geometry plan, imported **through the mod** (`op mod`: Check, Import) on the Corral Riches test spot | 61 / 61, 80 CS2 edges, 0 Temp | 1.5 s | Saved as `cs1conv-m2-geometry`. Every case looks right in screenshots. The −12 m underpass became a lowered open cut rather than a tunnel (one `not-tunnel`). |
| e2e, Skibidi 3 km crop, converted terrain and CS1 water on Plains | **2035 / 2035**, 0 Temp | import 1.9 s; whole run 2 min 10 s, 71 s of it starting CS2 | 20 outside connections rebuilt (mapprep). Saved as `cs1conv-e2e-20261121-0530`. |
| e2e, whole city (`--auto-crop 14300`, 4740 segments) | **4732 / 4740**, 0 Temp | import 3.0 s, 12 batches (create 5–10 ms, wall 80–200 ms each) | 8 `edge-missing` at two shallow grade separations, where CS2 joined the roads; see the open issues in DECISIONS. 30 CS1 tunnels became lowered cuts. |

- **The mod's other actions, by script:** "Install Road Builder configs" copied 22 configs. Check then reported all 26 road types available and 80 roads in the area. A package without `roadbuilder\` and a non-existent folder both give clear error messages.

## M2 plan additions: results (2026-11-21)

`tools/e2e/run.mjs --apply-terrain --simulate 120 --save-name cs1conv-m2-full`, Skibidi 3 km on converted terrain (Sunshine Peninsula base), then the same chain again through the mod's Import action (`op mod`):

| Step | Result |
| --- | --- |
| roads | 2035 / 2035 |
| traffic | 518 / 518 junction light states as planned (1 added, 121 removed), 66 turn-ban ends (59 in node compositions), 41 crosswalks removed, 1.1 s |
| trafficmod | 325 priority signs (167 main, 158 yield) on 287 roads; on those junctions' car lanes RightOfWay 0 → 836, Yield 532 → 793; 237 lane connections, 211 arrows, 31 U-turn rules not applied |
| zoning | 93.1 % of 76 658 cells; all 89 848 CS2 cells painted (shared copies included) hold their zone after the step and after 2 min of simulation |
| mod chain | roads, traffic and Traffic mod: the same numbers; the status line showed each batch, then traffic, trafficmod and zoning, and the summary. This run used the zoning version from before the shared-cell fix (nearest cell only). The fix was verified with the exec op, then in the mod (mod-only user flow below) |

The city is saved as `cs1conv-m2-full`. Screenshots in `.gamectl/e2e/20261121-054647/`: `zones-*` and `zones-closeup-fixed` (zone overlay), `m2-light` (signal poles), `m2-forbid` (yield triangles on a signed approach next to the highway trench), `roads-*`.

**Integration run** (`.gamectl/e2e/20261121-083655`, then `verify.json` there):
- **e2e:** `--apply-terrain`, M2 steps, the buildings/ plug-in, 60 s simulation. ok: roads 2035/2035; buildings 96/108 placed (94 connected, 12 collisions); trees, districts and surfaces from the plug-in. Saved as `cs1conv-m2-full`.
- **Bridge barrier fix:** right after `save.load cs1conv-m2-full`, a phase-tool exec and a main-phase exec both created command buffers from `EndFrameBarrier` and `ModificationBarrier1` without error.
- **Mod chain by script** on that city: roads 2035/2035; traffic as above; 325 priority signs; zoning 92.9 %; buildings 86 already there + 4 placed (18 collisions, because the city already had them); objects, districts and surfaces skipped (the plan has no such sections). Each step showed in the status line. Saved as `cs1conv-m2-modchain`.

**Full e2e** (`.gamectl/e2e/20261121-090231`): `--apply-terrain`, MapPrep clear (197 edges, 5 buildings), M2 steps, both plug-ins, 60 s simulation. ok: roads 2035/2035; buildings 99/108; objects 3453/4216; transit 145/145 stops, 9/9 lines named, 25 buses running; water check after the simulation: 150/150 dry points dry, 141/150 wet points wet, median error 0.66 m. Saved as `cs1conv-m2-full`.

**Mod-only user flow** (`.gamectl/e2e/20261121-091639`, `modflow.json` there): a city with the converted terrain (`--no-import --keep-cs2`), then only the mod's Check and Import (every toggle on), driven through the `mod` op. Check: "All 37 road types are available." The status after Import:
- roads 2035/2035 in 4.3 s;
- Traffic: 1 lights added, 121 removed, 66 turn bans, 41 crosswalk changes;
- Traffic mod: 325 priority signs on 287 roads;
- Zoning: 71 348 of 76 658 cells (93.07 %);
- Buildings: 108 of 108; Objects: 3404 of 4216; districts and surfaces skipped (none in the crop);
- Transit: 9 of 9 lines, 145 of 145 stops.

Report `%LOCALAPPDATA%\cs1-to-cs2\import-reports\import-20261121-102724.json`; saved as `cs1conv-m2-modflow`. At the time, the mod didn't apply terrain, water or outside connections (fixed by "Prepare map" and the connections step, below).

**Release flow, mod only, from a web-CLI package** (no Node map prep; `.gamectl/modprep/run1` and `full1`, `modprep.json` there). Each run: new city on Sunshine Peninsula → Prepare map → Check → Import → simulate → `WaterCheck`.

| | Skibidi 3 km crop (`cs1conv-m2-modprep`) | Whole city, 14.3 km (`cs1conv-m2-modprep-full`) |
| --- | --- | --- |
| Prepare map | 7.5 s; 197 base-map nets and 5 buildings removed | 16.6 s; the same base map |
| Terrain vs the package file | 0 m error at 181 of 181 points (world map applied) | 0 m at 181 of 181 |
| Water | sea level 182 m, 5 CS1 sources; after 2 min of simulation 94/100 wet, 100/100 dry points as in CS1, median depth error 0.73 m | 15 sources; after 3 min 95/100 wet, 100/100 dry, median depth error 1.65 m |
| Import steps | all on: roads 2035/2035 in 4.4 s, traffic, Traffic mod, zoning 93.07 %, buildings 108/108, objects 3404/4216, transit 9/9 lines with 145/145 stops; whole chain 47 s | roads and connections only: **4738/4740** in 8.3 s (6 via CS2 forks, #44) |
| Outside connections | 20/20 (13 road, 7 train); after the simulation 190 connection lanes and 624 cars | 10/10 (6 road, 4 train); 96 lanes, 28 trains, 24 cargo vehicles |

The 2 segments still missing in the whole city are CS1 geometry that overlaps in CS2: a 16 m isolated pavement stub under an elevated highway (snapped onto it), and Basic Road 25217, which runs 9 m from a parallel road it meets through a 9 m stub.
