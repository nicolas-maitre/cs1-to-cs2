# CS2 importer: decision log (M1)

Decisions taken by the importer worker on 2026-11-21 while the user was away ("Claude"). Review them. Project-wide decisions live in [`docs/DECISIONS.md`](../docs/DECISIONS.md).

## Architecture

1. **The importer is a hot-loaded `exec` library, not a deployed mod** (Claude).
   - `Cs2Importer.dll` is a plain class library. The Cs2Bridge loads a fresh copy on every `exec` call, so a rebuild takes effect without restarting CS2, which was how every M0 check ran.
   - A deployed mod with its own `ToolUpdate` system would need a game restart for every code change and a second channel (bridge methods or files) to trigger and poll it. M2 wraps the same code in a user-facing mod with a button; `src/Core` and `src/Game` don't depend on how they are called.
2. **One `exec` call runs the whole import as a coroutine stepped in `ToolUpdate`** (Claude).
   - Placement must happen in `ToolUpdate` (Spike 1b), and the import spans many frames: a batch, a few frames for the game to build it, the node lookup, the next batch.
   - The bridge already ran an `exec` coroutine, but only its first step in `ToolUpdate`; later steps ran on the main-loop pump. I added the opt-in `exec` param `pump:"tool"` to the bridge (`BridgeToolPhaseSystem`), so every step runs in `ToolUpdate`. It is generic and documented in `tools/PROTOCOL.md`.
   - All import state (node map, positions, report) lives in the coroutine, so statics never matter. Progress goes to `Logs/Cs2Importer.log`, the report is the `exec` result and is also written to a file.
3. **Game-free core** (Claude). `src/Core` (plan parser, validation, elevation, curves, batching) has no game references and is compiled into `tests/CoreTests` (net8, no NuGet packages), which also has a `plan <path>` mode to dry-run a real plan.

## Placement

4. **Spike 1b recipe, unchanged** (Claude): `Permanent | SubElevation` definitions, one CS2 position per plan node computed once, `Entity.Null` ends within a batch, and a plan-node-id → node-entity map so later batches attach through `CoursePos.m_Entity` at the node's real position.
5. **Spatial batches of 400 segments** (Claude). Segments are sorted by a 400 m grid cell (serpentine rows) and cut into chunks, so most junctions merge inside one batch. 400 is a guess that keeps a frame short; the report has the time per batch to tune it.
6. **Node lookup after each batch**: 2 frames after the batch, then each new node is matched to the nearest real node within 1 m horizontally and 3 m vertically, with one extra frame of retries. A node that never appears fails every later segment that uses it (`node-not-created`).
7. **Node elevation: segment ends first; CS1 terrain offsets only when they are real terraforming** (Claude, from the Skibidi runs).
   - In the Skibidi crop, 368 nodes used only by ground segments sit 1–4 m above or below CS1's terrain (CS1's terrain smoothing, small embankments). Taken literally, those roads would sink into or float above CS2's terrain. They count as ground.
   - 156 ground nodes sit 4 m or more off CS1's terrain: deliberate terraforming. The Two Lane Highway, for example, runs in an 11 m trench that the city's roads bridge over.
     - The first Skibidi import flattened these to ground. One "Basic Road Elevated" then met the highway at grade, and CS2 split it with an unplanned junction (1 `edge-missing`).
     - They now keep their offset, so CS2 builds retaining walls and quays and the crossings stay grade-separated.
     - The offset is clamped to −11.5 … +7.5 m, so a trench stays an open cut. CS2 turns ≤ −12 m (3× the road's elevation limit of 4 m) into a tunnel and ≥ +8 m into a bridge with pillars.
   - Rule: each node takes the candidate with the largest magnitude among its segments' end elevations (`elevationStart` / `elevationEnd`, under 1 m = 0) and its clamped CS1 offset when that is ≥ 4 m. Segment ends that disagree by more than 1 m are reported (`elevation-mismatch`).
   - Result on Skibidi: 1344 ground, 195 raised and 131 lowered nodes. All 2035 segments are built, with no unplanned junctions.
8. **Curves**: X/Z of the inner control points come from the plan's bezier. Y follows the straight line between the two CS2 node heights. Elevated, bridge and tunnel segments also keep the CS1 curve's vertical bulge over that line (arches). Ground segments don't, so they stay flat on CS2's terrain.
9. **Course elevation**: each end gets its node's elevation (`CoursePos.m_Elevation`), the course gets the mean of the two (`NetCourse.m_Elevation`). CS2 then picks the composition (bridge with pillars at ≥ 2× the prefab's elevation limit, tunnel at ≤ −2×/−3×, quay or retaining wall in between). No forced flags.
10. **Plan validation before building**: segments with a missing node, an unknown prefab key, both ends on one node, an end outside the CS2 map (|x| or |z| > 7168 m) or shorter than 0.5 m are skipped and counted. Segments under 4 m, parallel segments between the same two nodes and distinct nodes less than 5 cm apart are built and reported.
11. **Missing prefabs fail their segments, they don't stop the import**. The report lists every prefab key with `found`, its width and, for a missing Road Builder config, the hint to copy it into `ModsData/RoadBuilder` before starting CS2.

## Clearing the area

12. **Bulldoze by marking entities `Deleted`, Road Builder's pattern** (Claude).
    - Optional (`clear=true`): every top-level edge with a point inside the plan's bounds + 50 m gets `Deleted`, with its sub-objects. One frame later, end nodes left without edges are deleted too, and the surviving ones get `Updated` with their edges.
    - Edges owned by buildings (`Owner`) are left alone and counted.
    - Why not the bulldoze tool's definitions: those go through the `Temp` → apply path, which Spike 1b showed can't be driven from outside a tool. Road Builder ships the `Deleted` pattern and it is in use.

## Verification

13. **Built = there is a path of real edges between the two node entities.** Only CS1 `elevated`/`bridge` segments are expected to get an elevated composition, and only `tunnel` segments a tunnel composition; terraformed ground roads become retaining walls or quays. The walk may pass through intermediate CS2 nodes that are not plan nodes and join exactly two edges, in case CS2 splits a long course. Each edge is claimed by one segment, so parallel segments need their own edges. The report also counts edges with a different prefab than planned, elevated and tunnel compositions against what the plan asks for, other CS2 edges left in the area, and world `Temp` entities (the ghost check).

## Bridge additions

14. **`game.new`** replays the menu's New Game (`MenuUISystem.NewGame`, read from the IL) but writes the options only to `CityConfigurationSystem`'s overrides. The menu also stores them in the user's settings as the next defaults, which a script must not touch. Test cities default to unlock all, unlimited money and all map tiles on (the import area must be buildable), natural disasters off.
15. **`save.write`** replays `AutoSaveSystem.AutoSave`, including deleting an existing asset of the same name first. It refuses any name that doesn't start with `cs1conv-` (docs/DECISIONS.md #9), and only allows letters, digits, `-`, `_` and `.`.

## Testing

17. **M1 test map: Corral Riches, with the plan shifted by (+2000, −4000)** (Claude).
    - No built-in map is flat around its centre. Over the 3.2 km square around the origin: Plains 366 m height range, Sunshine Peninsula 200 m, River Valley 142 m. Corral Riches is the flattest (71 m), but 39% of it is water.
    - A search over 3.2 km squares centred on a 1 km grid found the Corral Riches square at (2000, −4000): 5.7 m height range, no water, no existing roads. The flattest square elsewhere was 15.8 m (Sunshine Peninsula).
    - The importer has `offsetX` / `offsetZ` for this test placement. `run.mjs` uses `CS1CONV_MAP` / `CS1CONV_OFFSET` (default `Corral Riches`, `2000,-4000`); `--offset 0,0` puts a plan at its own coordinates.
    - Once the terrain worker's heightmap lands, the offset becomes 0 and the map becomes the converted terrain.

16. **CS2 lock**: `run.mjs start` takes `%LOCALAPPDATA%\cs1-to-cs2\cs2.lock` and refuses while another worker's lock is under 2 h old; `run.mjs stop` releases it. It reads both lock formats in use (`{owner, time}` and the terrain worker's `{session, since}`).

## M2 (2026-11-21, still overnight)

18. **The "turn the 4 m+ terrain offsets off" issue was a false alarm; they stay** (Claude, checked against the data).
    - In the `.cs1x`, node `y` equals the exported terrain plus `heightAboveTerrain` to within 0.28 m at all 1670 plan nodes. At the highway trench the exported terrain is about 11 m *above* the road.
    - So the heightmap is the undug terrain, and once `terrain/` has applied it, CS2 terrain + offset is the CS1 height. Nothing is counted twice.
    - With real terrain the importer only drops the M1 test offset (back to 0). It keeps the flat-mode height rules, which on real terrain place every node within a few metres of CS1.
19. **New height mode `heights: "exact"` (CS2 terrain + full CS1 `heightAboveTerrain`) exists but is experimental and off by default** (Claude).
    - On freshly converted terrain it natively crashed CS2 in 5 of 5 runs, always about 1.5 s into the import: batch 3 of 400, or batch 26 of 25 (different segments each time).
    - The same plan with flat heights on the same terrain never crashed: 2035/2035 in three runs.
    - What I ruled out:
      - a single bad segment (the crashing batches have ordinary slopes, ≤ 0.17);
      - the terrain still settling (30 s wait);
      - water (it crashed with and without the water step);
      - a cold terrain-modification state (a 24-segment warm-up import first didn't help).
    - It did work once, when a flat import had built and cleared the same roads first.
    - `ExactLimit` (4.5 m) caps how far a position may differ from the composition elevation. That was a first guess at the cause and wasn't enough on its own.
    - Left for later; the e2e uses flat heights (`--heights exact` opts in).
20. **The e2e clears the base map with the importer's clear, not `terrain/mapprep.mjs clear`.** The terrain worker's clear (in progress) natively crashed CS2 twice on a fresh Plains city; I reported it to them. `--mapprep-clear` opts in.
21. **Water is the terrain worker's step, called from e2e when `terrain/mapprep.mjs` exists.**
    - CS1 water from the `.cs1x` `water.json`; without it, a dry map (`water.set` with no sources, then `water.reset`).
    - Why it matters: with only the sea level lowered, the base map's river (Plains) kept flowing over the converted terrain and flooded the imported roads.
22. **Base map for e2e with terrain: Plains** (little water at its centre). Its pre-built nets (372 edges) are bulldozed before the terrain goes in.
23. **The mod is a separate assembly (`Cs2ImporterMod`) compiled from the same sources; the exec library stays `Cs2Importer`** (Claude).
    - Two assemblies with one name would collide when `exec` hot-loads the library next to the loaded mod (Mono returns the copy already loaded).
    - The script test hook lives in the exec library (op `mod`), which finds the loaded mod by reflection and calls the same `Actions` as the Options page buttons.
24. **The status shown in game is a locale entry** (the label of a multiline-text option), rewritten and reloaded on every progress step. The game's settings UI has no read-only bound text field. Scripts read the same text from the hidden `StatusText` setting.
25. **Import runs in the mod's own `ImportSystem` (ToolUpdate), one coroutine step per frame**, the same pacing as exec `pump:"tool"`. Loading or leaving the city aborts it with a message.
26. **Locks:** `%LOCALAPPDATA%\cs1-to-cs2\cs1.lock` (new) and `cs2.lock`, same rules: another owner's lock under 2 h old means wait. The e2e waits up to 60 min. The terrain worker once overwrote my live lock and restarted CS2 under a running e2e; I told them about the format and the rule.

27. **e2e defaults after the terrain worker's verified commands** (Claude).
    - Terrain only with `--apply-terrain` (coordinator).
    - Base map Sunshine Peninsula (fewest base nets, per the terrain worker's survey).
    - Water and outside connections from `terrain/mapprep.mjs`.
    - **The base-map clear stays the importer's.** With `mapprep clear` (which keeps power lines and seaways), CS2 crashed natively while creating batch 4 of the road import (e2e `20261121-044148`); the import's own pre-import clear had deleted 5 of those kept edges inside the crop just before. The same export on the same map with the importer clear passed (`20261121-045036`).
    - `--clear-with mapprep` keeps theirs available.

28. **Embankments snap to the terrain; trenches stay** (Claude, from the M3 buildings worker's report). This revises #7.
    - **What went wrong:** ground nodes 4 m or more *above* CS1's terrain kept that offset as their elevation, so CS2 gave the edges Raised sides (embankment walls). Buildings can't connect to those: 6 of 77 placed buildings in `cs1conv-e2e-20261121-0530` were left unconnected.
    - **Why it's safe to drop:** positive offsets aren't needed for grade separation, because bridges carry their own segment elevation. Trenches (≤ −4 m) are still needed to keep cross streets above sunken highways, so they stay.
    - **Option:** `keepEmbankments` (default false) brings back the old rule.
    - **Measured on converted terrain, same city and plan:** edges with Raised sides 94 → 26; Lowered sides 148 → 148; still 2035/2035 built, 0 Temp.
    - **New in verify:** `edges_raised_sides` / `edges_lowered_sides`.

### Open issues (M2)

- **Resolved by #43 and #44:** the 8 segments turned out to be CS2 merging shallow forks (verify now follows them), and the importer's own trench clamp is undone where it made crossings too shallow. The original note:
- **Shallow grade separations get joined.** In the whole-city run (4740 segments), 8 segments in two spots weren't built as planned:
  - (548, −2518): a Two Lane Highway in a 5.2 m cut next to a ground road;
  - (−942, −650): a one-way road in a 4.6 m cut under 12 m elevated ramps.

  CS2 creates a junction where courses cross with too little clearance. Fix idea: for crossings without a shared node, make sure the clearance is at least about 8 m, by pushing the lower road down or the upper one up within the CS1 tolerance, or report them.
- **CS1 tunnels sometimes become open cuts** (30 in the whole city, 1 in the geometry plan). CS2 picks the composition; forcing a tunnel would need elevation ≤ −12 m on both ends and in the middle.
- **`heights: "exact"` crash:** see #19.
- **Not driven yet:** the Options page itself was not opened on screen; scripts drive it through `Actions`. The dropdown of found packages is untested in game.

## M2 plan additions (2026-11-21)

29. **Three separate ops after the road import: `traffic`, `trafficmod`, `zoning`, in that order** (Claude).
    - Each is switchable in the e2e (`--no-traffic`, `--no-traffic-mod`, `--no-zoning`) and in the mod (one toggle each). All run with phase and pump `tool`, because structural changes from the main-loop pump crash CS2 (terrain worker's finding).
    - Zoning runs last: the traffic steps mark edges `Updated`, and the zone blocks must be final before they're painted.
    - The ops don't keep state from the import. They find plan nodes by position and plan segments by walking edges (`Resolve`), the same as `verify`, so each one can also be run on its own.
30. **Zoning: paint `Cell.m_Zone` directly, as the zone tool's apply does** (`ApplyZonesSystem`: write the block's `Cell` buffer, then `Updated` + `BatchesUpdated`).
    - It first waits until the number of blocks in the area has been stable for 20 frames; CS2 creates the blocks from the roads itself.
    - Each plan cell is matched to every CS2 cell whose centre lies within half a cell (4.5 m) of the plan cell's centre, in **every** block, Visible or Shared, not Blocked.
      - **Why every block:** blocks overlap at corners and along parallel roads. An overlapped cell is `Shared`, and the game keeps both copies in sync, so painting only the nearest copy got 13 450 cells reverted. All of the cells that failed were `Shared`.
    - When several plan cells land on one CS2 cell (the CS1 and CS2 grids don't line up), the CS2 cell takes the majority zone. On Skibidi only 2 cells had conflicting votes.
    - **Skibidi 3 km:**
      - 93.1 % of 76 658 plan cells matched; all 89 848 CS2 cells painted, shared copies included, still hold their zone (0 reverted);
      - the check after 2 minutes of simulation gives the same;
      - unmatched: 947 cells with no CS2 block (CS1's blocks reach deeper or lie along roads that don't zone in CS2) and 4343 hidden cells.
31. **Vanilla traffic settings go in `Game.Net.Upgraded`, the same component as the game's road upgrade tool** (read from `CompositionSelectSystem` in the IL).
    - Traffic lights, removed lights and all-way stop: `m_General` on the **node** entity. `GetNodeFlags` ORs the node's own `Upgraded` into every node composition.
    - Turn bans (`ForbidLeft/Right/Straight`) and crosswalks (`Add/RemoveCrosswalk`): side flags on the **edge**, left for the start node and right for the end node. The start node's composition gets the edge's flags mirrored, so each end's flags land on the same side of its own node composition. Measured: 57 of the 59 bans that reached a composition are on that side.
    - Lights are only changed where CS2's default differs from the plan; the default is read from the node composition first.
    - **Skibidi:** 518/518 junction light states as planned (1 added, 121 removed); 41 crosswalks removed; 59 of 66 turn-ban ends visible in node compositions.
    - **Not verified:** the turn bans' effect on lane connections.
32. **Traffic mod: priority signs only, by reflection, in the mod's own data format** (Claude).
    - **Detection:** the `Traffic` assembly (Paradox Mods 80095, 0.2.13) with `Traffic.Components.PrioritySigns.LanePriority`. No compile-time reference; skipped and reported when the mod is missing.
    - **What's written:** the edge gets a `LanePriority` buffer, with entries {`laneIndex` = (composition lane index, group, carriageway), `priority` 1 RightOfWay / 2 Yield / 3 Stop, `isEnd`}, plus a `ModifiedPriorities` tag. These are the same fields the mod's own apply step writes.
    - **Which lanes:** the same selection as the mod's handle generator: road edge lanes ending at that node, without secondary or master lanes.
    - **Measured** on the signed junctions' car lanes, CS2's own `CarLaneFlags`: RightOfWay 0 → 836, Yield 532 → 793. The mod's lane system applied them. Yield markings are visible at signed approaches.
    - **Not applied, reported with counts:** lane connections (237), lane arrows (211) and U-turn rules (31).
      - The mod stores connections as separate entities (`ModifiedLaneConnections` on the node, `GeneratedConnection` buffers with lane position and carriageway maps), a much deeper format to fake reliably.
      - Lane arrows are lane connections in that mod.
      - The parts of the arrows that are plain turn bans already land through the vanilla flags (#31).
33. **Hooks for transit and buildings.**
    - **e2e:** `tools/e2e/steps/*.mjs` plug-ins (`{name, after, run(ctx)}`) run as optional steps at `import`, `connections`, `shots` or `end`.
    - **Mod:** `Cs2ImporterMod.ImportSteps.Register(name, Func<args, IEnumerator>)` (by reflection) adds a step after the built-in ones, in the same ToolUpdate chain and the same combined report.
34. **The mod's "terrain is converted" toggle is hidden and the mod always uses flat heights.** It selected the `exact` mode, which crashes CS2 (#19), and flat heights are right on converted terrain too.
35. **e2e additions:** `--simulate s` (unpause at 3× for s seconds before the shots), zone-view screenshots (op `view` activates the zone tool, the only time CS2 draws zoned cells), a final `zoning-check` after the simulation, and `--save-name`.

### Open issues (M2 plan additions)

- Traffic mod lane connections and lane arrows are not applied (see #32).
- Turn bans are verified in the node compositions only, not in the generated lanes.
- No services, so the test city doesn't grow beyond a few buildings (population 74 after 2 min, then 54). Zones accept growth, but water, power and services are out of scope here.

## Integration (2026-11-21, after the usage-limit pause)

36. **Bridge: barrier command buffers are allowed before exec code runs** (`BarrierAccess` in `tools/cs2-bridge/BridgeSystems.cs`).
    - **Symptom** (buildings worker): the first `exec` with phase tool after a `save.load` failed with "Trying to create EntityCommandBuffer when it's not allowed!". Game methods that queue end-of-frame commands (`NameSystem.SetCustomName`, `PoliciesUISystem.SetPolicy`) failed the same way from exec.
    - **Cause, from the IL:** `Game.SafeCommandBufferSystem` only hands out buffers after its `AllowBarrier<T>` system has run since its last update.
    - **Fix:** the bridge calls `AllowUsage()` on every safe barrier right before exec code or a coroutine step runs, which is what those systems do every frame.
37. **The buildings/ worker's steps are compiled into Cs2ImporterMod** (one `Compile Include` of `buildings/src`, `Cs2Buildings.Steps.RegisterAll` in `OnLoad`). One Options toggle, "Buildings, trees, districts", gates all four. They run after zoning.
38. **e2e plug-ins.** Buildings is the buildings/ worker's own `steps/buildings.mjs` (on by default, `--no-buildings`; my duplicate was removed after both ran in one run). Transit (`steps/20-transit.mjs`, mine) is on by default since the terrain worker verified it (`--no-transit` skips it); it runs after the outside connections and the buildings plug-in (stations and depots first).
39. **e2e, after the terrain worker's map-prep update:**
    - **Clear:** with terrain the base-map clear is `mapprep clear` again. It now deletes every top-level net (the crash in #27 came from the power lines it used to keep) and also the base map's buildings: a Sunshine Peninsula landmark kept its old height and stood as a terrain pillar in the screenshots.
    - **Water check:** `water-check` runs after `--simulate`. Lakes above the sea level fill only while the simulation runs (3–5 simulated minutes).
40. **Transit in Cs2ImporterMod** (coordinator: a release user flow can't depend on the Node e2e script).
    - **Their code, compiled in:** the transit worker's library (`transit/cs2-exec/*.cs`) is compiled into the mod by reference, like `buildings/src`.
    - **`mod/TransitStep.cs`** replays their verified `transit.mjs apply` inside the import coroutine, with the same pacing:
      - stops 20 at a time, 10 frames apart, then 30 frames;
      - one line at a time, its `lines.create` coroutine stepped in ToolUpdate, with the forced apply frame;
      - names and numbers;
      - a 60-frame settle, then a check.
    - **Planner:** `BuildJobs` is a C# port of their `transit/lib/plan.mjs` `transitJobs` (kerbside stops keyed by segment:side:t, shared between lines). The two have to be kept in step.
    - **Names:** `NameSystem` needs the end-frame barrier, so `ImportSystem` allows the safe barriers before every step (the same fix as the bridge's, #36).
    - **Option and order:** one Options toggle, "Transit lines", on by default. The step runs after the buildings steps, so metro and train lines find their stations.

## Release flow in the mod (2026-11-21, coordinator decisions after 301b1ee)

41. **"Prepare map" in Cs2ImporterMod** (coordinator #1: terrain, water and connections were the biggest release gap).
    - **What it is:** a new Options group, "New city", with one button and a confirmation. It runs before Import, in a fresh city, from the self-contained web package (schema §2): `terrain/`, `plan.terrain`, `plan.water.apply`, `plan.water.checkpoints`.
    - **Their code, compiled in:** the terrain worker's exec library (`terrain/cs2-apply/*.cs`) is compiled into the mod by reference, like `buildings/src` and `transit/cs2-exec`. `mod/MapSteps.cs` calls their public `MapPrep.Run` / `Ops.Run` with the same args their scripts send. No change in `terrain/`; the terrain worker keeps the namespace, entry points, op names and args stable and will warn before changing them.
    - **Order and waits** (`tools/e2e` order, the terrain worker's timings):
      - `clear {all:true}`: every top-level net and base-map building; then 60 frames.
      - `apply`: heightmap, world map, `heightScale`/`heightOffset`, sea level = `water.apply.seaLevel`, else `terrain.seaLevelDry`.
      - Sample again 60 frames later and compare with the r16 file: a 9 × 9 grid over the map plus 10 × 10 over the crop (the bilinear sampler is ported from `terrain/lib/heightmap.mjs`).
      - `water.set {seaLevel, clear:true, sources}`, then `water.reset {mode:"sea"}`. Without water data but with terrain: a dry map (no sources, the lowered sea level).
      - 600 frames to settle, then the wet/dry check at the package checkpoints.
    - **All in ToolUpdate**, in the mod's own `ImportSystem` coroutine. The terrain worker had only run `apply` from the main phase; from ToolUpdate it gave 0 m error at all 181 points.
    - **Report:** status text plus `%LOCALAPPDATA%\cs1-to-cs2\import-reports\prepare-<ts>.json`.
42. **Outside connections are the last Import step** ("Outside connections" toggle, on by default): `plan.outsideConnections` → `MapPrep oc.create` on the imported end nodes, then 30 frames and `oc.list`. Last, because no later step may rebuild those nodes.
43. **Clearance pass in the planner** (`src/Core/Clearance.cs`, coordinator #2).
    - **What it does:** finds crossings without a shared node (16-piece polylines of the plan beziers, 128 m grid). It estimates the CS2 heights as built: flat heights on the converted terrain, node height = CS1 terrain (`y − heightAboveTerrain`) + Elevation, plus the bulge where `Curve()` keeps it.
    - **Target:** where the clearance is below min(8 m, CS1's clearance there), it lowers the lower road's nodes, then raises the upper road's elevated nodes.
    - **Limit:** no node ever goes past its own CS1 height above terrain. A shortfall it can't fix is reported as `shallow-crossing`; a fix as `clearance-restored`. `MinClearance = 0` turns it off.
    - **Effect on Skibidi:**
      - 3 km crop: no change (2035/2035 in the mod flow).
      - Whole city: 2 of 322 crossings were too shallow, both because our own −11.5 m trench clamp (#7) lifted 16–22 m deep CS1 ramps under other ramps: 0.9 → 8.0 m and 4.9 → 8.0 m. Both had been built anyway, so this is about the right shape, not the count.
44. **The 8 `edge-missing` of the whole-city run were not shallow crossings: they were CS2 merging forks** (in-game inspection of `cs1conv-e2e-20261121-1025`).
    - **What CS2 does:** where two CS1 segments leave a node at a shallow angle (a highway splitting at a tunnel portal, two ramps leaving an elevated junction, two one-way roads leaving a cut), CS2 merges their first 10–60 m into one edge. It adds its own node where they part. The roads and their connectivity are there.
    - **Why verify missed them:** it only walked through intermediate nodes with exactly two edges.
    - **Fix (verify):** when that walk fails, a breadth-first search through non-plan nodes of any degree (≤ 8 edges, ≤ 1.5 × chord + 50 m), allowing the shared trunk edge. Such segments count as built and are listed as `merged-fork` notes (`notes_by_kind`), not failures.
    - **Remaining (whole city, mod flow `cs1conv-m2-modprep-full`):** 4738/4740 built, 6 of them through forks. The 2 missing ones are CS1 geometry that overlaps in CS2:
      - a 16 m isolated pavement stub under an elevated highway, snapped onto it;
      - Basic Road 25217, 9 m from a parallel road it meets through a 9 m stub (12 m-wide roads).

      Neither can be fixed without changing the network, so both stay reported as `edge-missing`.
    - **Prefab check on a merged fork:** the shared trunk carries the neighbour's prefab, so one edge of the segment's own prefab is enough.
45. **Transit jobs: one C# port, checked against the Node original** (coordinator #4).
    - `BuildJobs` moved from `mod/TransitStep.cs` to the game-free `src/Core/TransitPlan.cs`.
    - `tests/fixtures/make-transit-golden.mjs` runs the transit worker's `transitJobs` on a trimmed plan (`transit-plan.json`: the whole-city Skibidi transit, the segments its stops use, plus a stop on a missing segment) for EU and NA, into `transit-jobs.json`.
    - `TransitPlanTests` compares field by field. A one-field change in the golden file fails it (tried), so drift on either side fails CoreTests. After changing `transit/lib/plan.mjs`, rerun the script without arguments.
    - Rounding now follows JavaScript: half up, round before the offset, and `String(t)` for the keys.
46. **Smaller additions.**
    - Check now adds a "Map:" line: terrain or not, world map, sea level, water sources, outside connections, and "press Prepare map" when there is terrain.
    - `Actions.WaterCheck` (not a button) lets scripts re-run the wet/dry check after simulating (op `mod`, `press: "WaterCheck"`).
    - The `mod` op's `wait` works for `PrepareMap` too.
    - Lane connections and lane arrows stay report-only for this release (coordinator #3; the web worker puts them in the user guide's limits).
