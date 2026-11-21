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

## Release-grade mod, 0.3.0 (2026-11-21, coordinator task after b03c9a5)

47. **Version and packaging.**
    - **Version:** 0.3.0 in `Mod.Version`, the csproj `<Version>`, `Properties/PublishConfiguration.xml` `ModVersion` and the top of the new `CHANGELOG.md`. `tools/release/build.mjs --check` passes. The extra checks and copying `Properties/` and the changelog into the release are a change for the buildings worker, who owns `build.mjs`; the patch was sent to them.
    - **Paradox Mods:** `mod/Properties/PublishConfiguration.xml` is in the CS2 toolchain's ModPublisher format: display name, short and long description, Code Mod / Tools tags, `Dependency` on Road Builder [BETA] (87190). The format has no optional dependencies, so Traffic (80095) is only named in the description. Nothing is uploaded.
    - **Thumbnail:** `Thumbnail.png` (512²), drawn by `make-thumbnail.py` (Pillow, reproducible).
    - **In game:** the version shows in the About line, with whether Road Builder and Traffic are running.
48. **Choosing a package.**
    - **Browse… opens Windows' own Open dialog** (`GetOpenFileNameW` by P/Invoke; the settings UI can't browse), modal over the game. The filter is `cs1conv-plan.json;*.zip`. It starts in the folder of the last package, else Downloads.
    - **Zips** are unpacked to `%LOCALAPPDATA%\cs1-to-cs2\packages\<zip name>\`, replacing an earlier copy, with a guard against `..\` entries. A zip without a plan is refused.
    - **Pasted paths:** the folder, its plan or a zip, with or without quotes or spaces; a plan one level down (a zip's own folder) is found too.
    - **Recent packages:** the last five are kept in a hidden setting and listed first in "Packages on this PC". "Open packages folder" opens the unpack folder in Explorer.
    - **Tests:** the op `mod` can answer the dialog: a background thread finds it by title, types the path into the file-name box (control 0x47C) and presses Open or Cancel.
49. **Clearer errors** (all texts are in `Strings.cs`, English and French):
    - package format older or newer than this mod reads (with what to update); not a package; damaged plan; zip without a plan;
    - Road Builder not running (by its loaded assembly), or its configs folder missing (with "Install…, then restart");
    - **a city that wasn't prepared:** Import samples 181 terrain points against the package's heightmap. A median gap over 1 m means not prepared (308.6 m on a fresh Sunshine Peninsula, 0 m after Prepare map), and Import refuses with what to do. "Keep this map's terrain" lets it import anyway. Check reports the same.
50. **Cancel** (Status group, enabled only while something runs). It's cooperative: Import stops between road batches (the road import reads a `_cancel` flag) or between steps; Prepare map between clear, terrain and water (or cuts the settle short); Remove imported between batches. Interrupting a step in the middle of a frame's work could leave half-made definitions, so it never does. What's built so far stays and is recorded, so Remove imported deletes it.
51. **Remove imported** (`mod/Undo.cs`).
    - **Snapshot and diff:** the import runs between two snapshots of the top-level entities: roads, nodes, buildings, static objects, areas and transit routes. Everything new was made by the import, whichever step and whoever's code made it, so the buildings and transit steps need no change.
    - **Stable keys:** entity ids don't survive a save/load, so each item is stored by kind, prefab and position (edge: curve middle; area: first node; route: first waypoint) in `import-reports\undo-<city>.json`, and found again within 0.25 m. Several imports into one city add up.
    - **Deletion:** routes as the game's transport overview deletes a line (`Deleted` on the route, read from its IL); areas, objects, buildings and roads with `Deleted` on them and their sub-objects; then the imported nodes left without a road. A node that another, not imported, road still uses stays and is updated.
    - **Limits:**
      - Zoning and traffic settings go with the roads.
      - The base map removed by Prepare map doesn't come back.
      - An existing road that CS2 split to connect an imported one counts as imported. Clear the area first makes this rare.
52. **Traffic mod lane connections and lane arrows: in, experimental, off by default** (time box of about 1 h, coordinator #5; `src/Game/LaneOps.cs`, op `lanes`).
    - **Data model, read from Traffic.dll 0.2.13's IL:**
      - the node gets the tag `ModifiedConnections` and a buffer `ModifiedLaneConnections`, one element per source lane: lane index, carriageway and group, position, source edge, and a connections entity;
      - that entity holds `DataOwner(node)`, a `PrefabRef` to the mod's fake prefab (`ModDefaultsSystem.FakePrefabRef`) and the `GeneratedConnection` rows;
      - then `Updated` on the node and its edges.
      
      This is the recipe of the mod's own `TLEDataMigrationSystem`, which builds connections from data outside the tool. Lane positions and carriageway/group come from its `NetUtils.GetAdditionalLaneDetails`. Everything is by reflection; if the mod or those types are missing, the step is skipped and the report says which.
    - **Matching the plan's lanes** (schema §2 "Lane references"): the CS2 edge's car lanes at that node, sorted left → right in the plan segment's direction. The ordinal picks one; the count must match. Inbound lanes must end at the node, targets must start there.
    - **Arrows:** they become connections to the junction's existing CS2 lanes from that source whose turn flags the arrows allow. Explicit connections win over arrows (as in TM:PE).
    - **Verified in game** (`.gamectl/modprep/lanes1`, Skibidi 3 km, after Prepare map):
      - 237 connections + 211 arrows in the plan (124 arrows overridden), on 94 junctions;
      - 90 junctions written, 429 connections; every lane reference matched (no count or ordinal problems);
      - after the mod regenerated the lanes, 117 of 156 explicit source lanes lead exactly to CS1's targets;
      - traffic ran normally afterwards (104 cars, 14 trains after 60 s).
    - **Why off by default:** a quarter of the explicit lanes differ, and the cause isn't known yet (it may be where the mod merges or splits lanes). Persistence after a save/load wasn't checked; the Traffic mod serializes these components itself.
    - **U-turn rules** are still not applied.
53. **French Options page** (`LocaleFR.cs`; status and error texts in `Strings.cs`). It uses the web tool's French terms: paquet, zonage, quartiers, connexions extérieures, configurations Road Builder, Préparer la carte. Both locale sources are registered (`en-US`, `fr-FR`), and the status line follows the game's language. The road import's own English progress ("batch i of n …") is translated in the mod.
    - **Verified:** every label read back from the game's active dictionary after `SetActiveLocale("fr-FR")`, plus a French Check status.
    - `LocaleEN.cs` keeps the `Get…LocaleID(…), "label"` form the web tool's guide test parses.
54. **Lane connections: the 39 mismatches, persistence, U-turns, traffic handedness** (coordinator task after 9211fe5; `.gamectl/lanes/run3`, `run4`).
    - **The mismatches weren't a mapping error.** With per-lane diagnostics (want / got / the node's lanes before the write), every one of them sat at a node where one road simply continues into the next. CS2 builds no junction lanes there: each lane runs on into the lane that starts where it ends. Writing Traffic mod data there produces nothing, and the mod drops the data from some of those nodes.
      - **Now:** such nodes are skipped. Their sources are judged by that geometric continuation: "continuous" when it is exactly CS1's target set, else "pass_through_not_expressible" (a lane split or shift that only a junction could carry).
      - **Suspects ruled out:** Road Builder lane order (every count matched, and the junction sources are 117 of 117 exact), reversed segments (the plan frame is consistent: 474 of 474 Skibidi references point the right way), and the vanilla turn bans (identical result with the traffic step off).
    - **Skibidi 3 km (right-hand traffic), 176 explicit source lanes:**
      - 117 match at junctions (all of them) and 45 are continuous, so **162 of 176 (92 %)** are as in CS1;
      - 14 can't be expressed;
      - after a save and load: 70 of 70 junctions keep their data, **165 of 176 (93.8 %)** (the continuity check reads slightly differently after the load).
    - **Persistence:** the Traffic mod saves and restores the data itself. After `save.load`, `op lanes {checkOnly:true}` finds the same junctions and matches.
    - **U-turns:** the Traffic mod 0.2.13 has no U-turn rule (`TrafficUpgrade` / `NoUturn` exist but nothing uses them), so TM:PE U-turn rules become lane connections:
      - a ban drops a source lane's connections back into its own road;
      - "allowed" adds a connection from the innermost inbound lane to the innermost outbound lane of the same road, when CS2 has no U-turn there.

      Skibidi: 31 rules, 17 already as in CS2, 8 of 9 changed ones hold. The failures are at nodes without a junction (now counted as `uturn_at_nodes_without_junction`), plus one ban on a lane whose only movements are U-turns (an empty connection set can't be written).
    - **Traffic handedness:** CacaoCity drove on the left. Into a right-hand CS2 city every CS1 lane is where CS2 has it but flows the other way, which explains the acceptance run's 0 of 265.
      - **Exporter 1.2.1** writes `manifest.traffic.leftHand`, and the web tool writes `plan.source.leftHandTraffic`.
      - **Prepare map** sets `CityConfigurationSystem.leftHandTraffic` to it while the map has no roads. The net systems read it every update, and the city saves it. The status says "Traffic drives on the left, as in the CS1 city." Check warns when an existing city drives on the other side.
      - **Lane matching** compares the plan's side with the city's. For older plans without the field it tries plain and mirrored ordinals over the whole plan (`LaneMatch.Mirror`; mirrored only when clearly better).
      - **Verified:** Cacao into a right-hand city was detected as mirrored (342 of 344 fitting mirrored, 2 plain); Cacao with the field, prepared as left-hand, matched plainly.
    - **What's left before turning the toggle on by default** (the coordinator's bar was 95 % and persistence):
      1. Skibidi is at 92–94 %. The rest are lane splits or shifts at nodes without a junction that CS2 can't represent, so no matching fix will raise it. Only a junction there could, and making one would change the network.
      2. Left-hand cities: only 342 of Cacao's 764 lane references resolve. 212 are lane-count differences (workshop "more lanes" roads whose Road Builder version has another count) and 210 flow the wrong way even in a left-hand CS2 city. The latter likely come from one-way roads: CS2 flips one-way roads with the handedness (`FlipTrafficHandednessSystem.FlipOnewayRoadsJob`), so in a left-hand city the importer's one-way roads may run against CS1's direction. That needs a road-level check in a left-hand city, not a lane fix.

      Everything that resolves matches exactly (Skibidi junctions 117 of 117, Cacao 58 of 58), so the step never makes a junction worse than leaving it alone.
    - **Status line:** "Lane connections: N connections at a of b junctions; x of y CS1 lane references matched[ (the CS1 city drove on the other side of the road)]."
    - **Tests:** `LaneMatch` (game-free: pick by ordinal, mirror, arrows, innermost lanes, outcome) has 6 tests in CoreTests (34 in all).
55. **Left-hand cities: roads built end → start; lane connections on by default in both kinds of city** (coordinator decisions after #54).
    - **Default:** "Lane connections (Traffic mod)" is on by default (coordinator (a)). Everything the step writes is exact, and what's left can't be expressed in CS2. `Actions.LeftHandLanesReady` gated left-hand cities until the measurement below; it is now true, and stays as a one-line rollback.
    - **Measured** with op `lanedirs` (`src/Game/LaneDirs.cs`): every plan segment's CS2 car lanes, left → right in the plan frame, forward or backward. These were compared offline with CS1's lanes from the export (netinfos `m_direction`, reversed in a left-hand city):
      - **Skibidi, right-hand, control:** every comparable segment the same (1258 two-way, 309 one-way, 5 asymmetric). The count differences are CS2 listing a lane twice, with the same directions.
      - **Cacao in a left-hand city:** two-way roads were right (329), but **every one-way road ran backwards** (42, plus 4 with a count difference) and **the asymmetric roads came out mirrored and reversed** (15).
    - **Why:** in a left-hand CS1 city every lane keeps its place but drives the other way. The plan's own lane references showed it first: 515 of 530 point backwards. The importer built each road plan start → end, so a one-way prefab drove along the plan direction.
    - **Fix (game-free, in the planner):** for a plan with `source.leftHandTraffic: true`, `SegmentJob.BuildReversed` makes the importer create the course end → start (`ImportPlanner.BuildCurve`; `Entry` swaps the end nodes and elevations).
      - Building a road the other way round mirrors and reverses it in the plan frame. That is exactly what one-way and asymmetric roads need, and it changes nothing for symmetric two-way roads.
      - The plan frame (Start/End, lane references, segment-end sides, stop sides) stays as it is for every other step; they all find their edge by node, not by direction.
      - `ImportOptions.ReverseLeftHand` turns it off. Core tests: `LeftHandTests` (3).
    - **After the fix** (`.gamectl/lht/run1`, Cacao prepared as left-hand, saved as `cs1conv-lht-cacao`):
      - roads 684 of 684; one-way 141 of 141 and asymmetric 16 of 16 the same as in CS1, nothing reversed or mirrored;
      - lane connections 125 of 127 explicit sources as in CS1 (**98.4 %**), U-turn rules 17 of 17, outside connections 4 of 4.
    - **Left:** 212 of Cacao's 764 lane references don't resolve because the CS2 road has a different lane count. CS1 counts bicycle lanes as driving lanes (e.g. "One-Lane One-Way Rd w/Bike Lanes": 3 in CS1, 1 in CS2). That is a plan-side refinement: count only the lanes CS2 builds as car lanes, or give each lane reference its vehicle type.
56. **Vanilla CS2 roads where they match** (DECISIONS.md #100; the web tool picks the target, the importer builds it).
    - **The road list** (op `roadlayouts`, `src/Game/RoadLayouts.cs`): every vanilla road and highway prefab (not Road Builder's `r<uuid>-…` prefabs) is built as a straight 60 m segment in a throwaway city and read back from its edge composition. The data: lanes left → right with type, direction, width and offset; width, sidewalks, median, speed, zoning, highway rules, UI group.
      - Written to `%LOCALAPPDATA%\cs1-to-cs2\samples\cs2-roads.json`; the web tool bundles a slim copy.
      - CS2 1.6.4f1: 64 prefabs, all built; 63 placeable from a roads menu.
      - `RoadPrefab.m_SpeedLimit` is twice the km/h the game shows (Highway 240 → 120 km/h), so `speedKmh` is half of it and `speedPrefab` keeps the raw value.
    - **Importer support:** nothing in the import special-cases Road Builder roads.
      - Prefabs are looked up by type and name; the only Road Builder-specific code is the "config not loaded" hint.
      - The left-hand reversal (#55), traffic settings, priority signs, zoning and verify work from edges and nodes.
      - Lane matching (#52, #54) counts the CS2 car lanes at the node (car, car+tram and bus lanes; not tram-only or bike), whatever the prefab. The web tool's lane ordinals and counts follow the same rule.
      - Core test: `VanillaRoadTests`, a mixed vanilla + Road Builder left-hand plan.
    - **Measured with web 0.6.0 packages** (vanilla where matched; tram-only and bicycle lanes out of the counts, DECISIONS.md #102):

      | City | Vanilla road types | Roads | Lane references resolved | Lane connections as in CS1 | After save/load |
      | --- | --- | --- | --- | --- | --- |
      | Skibidi (right-hand) | 7 | 2035/2035 | 685/685 | 162/176 (92 %), every junction lane exact | 93.8 % |
      | Westfield (right-hand) | 5 | 648/648 | 299/301 | 88/92 (95.7 %) | 95.7 % |
      | CacaoCity (left-hand) | 8 | 684/684 | 727/729 | 206/208 (99 %) | n/a |

      The same as with the Road Builder-only packages. What's left is lane splits or shifts at nodes without a junction (#54). Cacao's one-way and asymmetric roads still all run as in CS1.
    - **The dump's median, corrected after the web worker's check:** `medianWidth` is now the gap between the innermost backward and forward driving lanes (Large Road 0, Large Road Divided 2). The composition's median pieces (kept as `medianPieces`) add up to 2 m on Large Road although its lanes meet at the centre line.
57. **Road heights: per-type height modes on road-conformed terrain** (DECISIONS.md #110–113; design in `NOTES-road-heights.md`, agreed with the web and terrain workers).
    - **Plan:** `prefabs[key].heightMode` (`conform` | `follow` | `walls`) for ground and slope segments; `terrain.roadConformed` (web 0.7.0 and later) makes `conform` the default. Plans without either keep the M2 rules (#7, #28).
    - **Node rule** (`ImportPlanner.AssignElevations`, `HeightModeTests`):
      - conform wins: the heightmap is burned to the node's CS1 height, so elevation 0 (`Conformed`), even where an elevated or tunnel segment ends;
      - then walls: the node's CS1 height above the natural terrain;
      - then follow: elevation 0 on the natural terrain.
    - **Profiles:** conform and walls ground courses keep CS1's vertical profile (the bezier's bulge), like elevated, bridge and tunnel courses; follow courses are straight between their nodes. The clearance pass (#40) measures conformed nodes from their CS1 height.
    - **Superseded by #61 (DECISIONS.md #129):** walls keeps the CS1 height, beyond the wall limits too (coordinator, after the before run showed CacaoCity's highway ramp — the user's screenshot, a "ground" ramp 25 m above the exported terrain — clamped to +7.5 m and up to 24.6 m low):
      - within −11.5…+7.5 m CS2 builds retaining walls or quays;
      - up to 1 m (the ground snap) beyond a limit the wall is kept, so Skibidi's open 12 m highway trench (about 20 nodes at −11.6…−12.3 m) doesn't become a tunnel;
      - further out the node keeps its CS1 height and CS2 chooses the elevated (pillars) or tunnel composition; reported as `walls-beyond-limits`.
      - 0.8.0 plans: CacaoCity 2 nodes (the ramp, +25.1 and +9 m), Westfield ~30 (+8…+20 m), Skibidi 2 above and a few deep cuts below (−15…−39 m).
    - **walls keeps CS1's height along the segment, not only at its ends** (`ImportPlanner.KeepWallsProfile`, in game): CS2 lays a course with elevation 0 at both ends on the terrain whatever the curve's y. CacaoCity's highway segment 25425 crosses a 9 m dip on a CS1 embankment between two ground-level ends and came out down in the dip (8.8 m low, before and after the burn: walls roads aren't burned). The course's middle elevation becomes CS1's height at the curve's midpoint above the real terrain there, when it differs from the ends' mean by 1 m or more; CS2 then builds raised or lowered sides. Report: `walls_middle_kept`.
    - **Absolute heights on reshaped terrain** (`ImportPlanner.KeepHeightsOnConformedTerrain`, in game before the nodes are placed): the burn cuts or raises the ground under elevated, bridge, tunnel and walls nodes too (Skibidi: an elevated highway end 6.6 m low over a road in a cut). On a road-conformed plan every node that isn't conformed and isn't at ground level gets elevation = (CS1 natural terrain + planned elevation) − actual terrain, so it stays at its CS1 height. Report: `nodes_kept_on_conformed_terrain`.
    - **Measured** (op `heights`: each segment's CS2 course sampled at five points against CS1's bezier; `.gamectl/heights2` before = web 0.6.1 natural terrain, `.gamectl/heights3` after = web 0.8.0 conformed terrain, same isolated mod build):

      | City | Ground roads | Before: median / p90 / over 2 m | After | Roads | CS1 lane references | Lane directions | Buildings |
      | --- | --- | --- | --- | --- | --- | --- | --- |
      | Skibidi | 1870 | 0.88 / 3.05 / 408 | 0.08 / 0.55 / 5 | 2035/2035 | 685/685 | all 2035 | 78/108 both (#106) |
      | CacaoCity | 668 | 0.26 / 0.95 / 44 | 0.03 / 0.42 / 6 | 684/684 | 727/729 | all 684 | 26/43 both |
      | Westfield | 579 | 0 / 5.02 / 90 | 0 / 0.31 / 0 | 648/648 | 299/301 | all 648 | 7/13 both |

      Before = web 0.6.1 on natural terrain (`.gamectl/heights2`); after = web 0.8.0 on conformed terrain (`.gamectl/heights4`); same isolated mod per series, so the buildings worker's uncommitted code is not in either. Errors in metres, the largest |CS2 road y − CS1 y| of five samples per segment.
      - CacaoCity's ramp 18231 (the user's screenshot) went from 24.6 m low to within 1.0 m.
      - Elevated Skibidi segments over 2 m: 5 → 2, with the absolute-height pass (7 without it).
      - With the walls burn (web 0.9.3, `.gamectl/walls8`, #61): CacaoCity 0.03 / 0.43 / 4, the ramp within 0.9 m as an embankment 7 m above the ground (not elevated), 25425 within 0.6 m (#62); Skibidi 0.08 / 0.54 / 6.
58. **CS1 tunnels stay tunnels: too-shallow tunnels are deepened to CS2's tunnel rule** (DECISIONS.md #124, user report).
    - **CS2's rule** (`NetCompositionHelpers.GetElevationFlags`, read in the IL in M0, `spikes/m0-cs2-place-road/NOTES-cs2-placement.md` §5): a course gets the tunnel composition only when every elevation (both ends and the middle) is at most −2L and the deepest at most −3L. L = the prefab's `NetGeometryData.m_ElevationLimit`: 4 for every road and track prefab the packages use, 2 for Pavement Path. Between that and ground, CS2 builds a lowered cut with retaining walls, which is what the user saw.
    - **Checked against a real import:** on the whole-Skibidi run (`import-20261121-105532`) the rule predicts 31 of 101 CS1 tunnel segments as cuts; CS2 reported 30 `not-tunnel`, and every sampled id is in the predicted set. They are CS1 tunnels at −8.2…−11.96 m: the twin highway tunnels at CS1 (2110, −540), metro track, a one-way road tunnel and pedestrian tunnels.
    - **Fix** (`ImportPlanner.DeepenTunnels`, in the planner before the clearance pass, and again in game on the real terrain after #57's height pass):
      - each CS1 tunnel segment (`kind: "tunnel"`) that fails the rule gets its deeper end lowered to −(3L + 0.5), then, if still needed, its shallower end to −(2L + 0.5). Portals stay at their CS1 positions and as shallow as the rule allows;
      - slope or ground segments that would then rise more steeply than 20 % (CS2's `m_MaxSlopeSteepness`) and than in CS1 get their other end lowered too, along the slope's below-ground, non-junction nodes (`tunnel-descent-extended`); anything left steep is `tunnel-portal-steep`;
      - the clearance pass (#43) may lower an already deepened tunnel node further, so two crossing CS1 tunnels deepened to the same depth keep their CS1 order and spacing.
    - **Never conformed:** a tunnel node is never conformed, and a conform slope is built as walls (CS1 height in the natural terrain). CS2 needs the ground at the portal to stand 8–12 m above the road; a burned slope cuts it away. The burn must skip slope segments (asked of the web and terrain workers; this reverses "the slope segment burns an open cut down to the portal" in `NOTES-road-heights.md`).
    - **Report:** `tunnels` (`cs1_tunnel_segments`, `deepened`) and `tunnels_deepened` (per segment: CS1 and CS2 end elevations, deepest before and after, delta, plan or terrain pass); warnings `tunnel-deepened`. Status line: "Tunnels: N of M CS1 tunnel segments were too shallow for a CS2 tunnel and were built deeper (listed in the report)."
    - **Tests:** `TunnelDepthTests` (8) and `ClearanceTests.TestDeepenedTunnelsKeepTheirCs1OrderAndSpacing`.
    - **Measured:**
      - Skibidi crop around the tunnels, web 0.9.0 (slopes still burned), `.gamectl/heights4/tunnels-{before,after}`:
        - before: 21 CS1 tunnel segments with an open-cut composition (`not-tunnel`, of 60 checked);
        - after: none, 65 checked; 30 segments deepened (highway tunnels −8.8…−9.2 → −12.5 m, metro −8.2…−11.96 → −12.5 m), 5 of them by the terrain pass at burned portals;
        - roads 1261/1328 in both: the offNode failures of #59, unrelated.
      - Web 0.9.1 (slopes not burned), `.gamectl/lanes6/lanes-before-091`: roads 1326/1328, all 66 CS1 tunnel segments built as tunnels, 25 deepened, all in the planner (no portal needed the terrain pass).
      - Screenshots: `view-tunnel-*.png` in both runs. The open dark trenches become tunnels with the street at ground level.
59. **Node Controller shifts (`offNode`): off until verified, now on by default** (found in the #124 tunnel run; the code is worker-5's, DECISIONS-nodecontroller.md; on per DECISIONS.md #135).
    - **What happened:** on a Skibidi crop built with web 0.9.0 (78 of 1329 segments `offNode`), 33 nodes were not created and 56 segments had no node (1261 of 1328 roads). Every sampled failure sits on a node touched by an offNode segment; the whole-Skibidi import without offNode had none.
    - **Likely cause:** with off-node curve ends, CS2 makes the node at the course's curve end, while the importer looks for it at the node centre (within 1 m) after each batch. It misses it, and every later segment at that node fails.
    - **Decision:** `ImportOptions.ApplyOffNode` (exec arg `offNode`), false by default. Off = the curve ends snap to their nodes, as with plans before web 0.9.0. Worker-5's code is unchanged behind the switch. Turn it on once the post-batch lookup finds nodes at shifted ends and an in-game run on a 0.9.0+ package has no node failures.
    - **Verified, then on by default:** worker-5's node lookups (the mean of the edge ends, then `NodeByEdgeEnds`) passed:
      - on the flat-spot crops of all three cities (worker-5, #135);
      - on the package that failed, with offNode on, `.gamectl/offnode9`: skibidi-tunnels-web-0.9.0 (78 offNode segments) 1326/1328 roads, 0 nodes not created, 0 segments without a node; skibidi-1000-web-0.9.1 the same. The 2 `edge-missing` are the same with offNode off.

      `ApplyOffNode` is now true by default; exec arg `offNode: false` turns it off.
    - **Tests:** `NodeControllerTests.TestOffNodeIsOnByDefault` (on by default, can be turned off).
60. **Lane connections: an independent geometric check; junctions that fail it are left to CS2** (DECISIONS.md #126, user report: connections crossing to the wrong arms at a 6-arm junction).
    - **Why:** the "as in CS1" rate compares what was written with what was meant. A systematic error in the mapping (plan segment → CS2 edge → lane) passes it.
    - **Arm check** (`LaneOps`, `LaneMatch.AngleDeg`): for every lane reference, the CS2 edge's direction leaving the node must match the CS1 segment's bezier direction leaving that node within 30° (`armTolerance`). The mapping can't hide a swap: it's geometry against geometry. A junction where any arm fails gets nothing written ("a wrong connection is worse than CS2's default") and is listed (`arm_check.junctions`); its source lanes count as `left_to_cs2_arm_check`, not in the match rate.
    - **Lane position check** (diagnostic, `LaneMatch.PickByPosition`): the CS2 lane nearest the CS1 lane's position across the road (offset as a fraction of the half-width) should be the lane the ordinal picks. Measured with CS1's offset as is and mirrored (`lane_position_check.agree`, `agree_mirrored`).
    - **Status line:** "… N junctions were left as CS2 builds them: their roads didn't line up with CS1's, so a connection could have gone to the wrong road (listed in the report)."
    - **Candidate junction:** in a Skibidi crop around CS1 (1000, −100), plan node 3163, at CS2 (1220, 213): 6 elevated arms, including HighwayRampElevated, 17 TM:PE connections, rail 19 m away. The three acceptance crops have no junction with more than 4 arms and lane connections.
    - **Tool for screenshots:** op `laneview` (the Traffic mod's LaneConnectorToolSystem on a plan node, by reflection).
    - **Measured** (check-only lanes op after the import, `.gamectl/lanes6`, `.gamectl/walls8`):
      - Skibidi crop with junction 3163, web 0.9.1: 421 arms, 0 off; lane position 1481/1490 agree; explicit sources 419/434.
      - CacaoCity 0.9.3: 193 arms, 0 off; 719/727 agree; 208/208.
      - Skibidi 0.9.3: 206 arms, 0 off; 665/685 agree; 165/176.

      No junction anywhere has a swapped arm, so the user's wrong connections at the 6-arm junction aren't an arm mix-up in these packages. #126 continues with worker-5 (the non-matching sources and position disagreements; `laneview` activates the Traffic mod's tool but doesn't select the node yet).
61. **CS1's elevation kind decides the CS2 composition family; ground roads stay walls** (DECISIONS.md #129, user report; replaces #57's unclamped walls, DECISIONS.md #123).
    - **Report:** a sunken twin-highway corridor under a street in the height after-series city became partly tunnels with portals, with walled cut stubs ending abruptly, where CS1 has an open trench. #123 let walls nodes beyond the wall limits keep their full CS1 height, and CS2 then chose a tunnel (below −12 m) or pillars (above +8 m) for a CS1 ground road. A corridor that crosses the threshold switches composition mid-way, which gives the portals and stubs.
    - **Rule:**
      - CS1 `tunnel` → CS2 tunnel (deepened to CS2's rule, #58);
      - CS1 `ground` (deep trenches and high embankments included) → never a tunnel and never pillars;
      - CS1 `elevated`, `bridge` and `slope` → as before.
    - **Importer:**
      - a walls node whose segments are all ground or slope (`WallsGround`) is clamped to −11.5…+7.5 m, in the planner and again in game, where its elevation is CS1's height over the real terrain;
      - the walls middle elevation (#57) is clamped too: ends at −11.5 m with a −20 m middle would make CS2 build a tunnel;
      - nodes still short of their CS1 height are listed (`walls_short`: CS1 height above the terrain, elevation used, short by);
      - plans without modes keep the M2 rules.
    - **Package (worker-2 web, worker-4 convert.mjs):** a walls burn after the conform burn, on `kind: "ground"` walls segments:
      - target t = clamp(n, h − 7, h + 11);
      - core d ≤ halfWidth + 2, smoothstep blend b = clamp(2|t − n|, 3.5, 40);
      - conform cores untouched; water as for conform; skip beyond 30 m.
      
      It brings the ground to within the limits, so the road keeps its CS1 height as an embankment or open cut with walls. It also restores CacaoCity's ramp (#57), which the clamp alone leaves at +7.5 m.
    - **Only on the package's terrain:**
      - the in-game passes that take CS1 heights against the real terrain (#57 absolute heights, #61 walls, the walls middle) run only when it is the package's: median |terrain − CS1 natural terrain| over the nodes under 3 m (`ImportPlanner.TerrainIsThePlans`, report `terrain_is_the_plans`);
      - found by worker-5's flat-test-spot verify: 8 ground segments as tunnels in Skibidi, 24 elevated in Westfield, the same with and without offNode;
      - CS2's tunnel rule (#58) still runs on any terrain.
    - **Verify, both ways:**
      - `tunnel_by_cs1_kind` and `elevated_by_cs1_kind` (segments whose CS2 edges have the tunnel or elevated composition, by CS1 kind) and `cs1_tunnel_segments_built`;
      - warnings `unexpected-tunnel` and `unexpected-elevated` for CS1 ground segments.
    - **Tests:** `HeightModeTests.TestGroundRoadsStayWallsBeyondTheLimits`, `TestWallsNodesUseTheReshapedTerrain`, and the updated reshaped-terrain and dip tests.
    - **Measured:**
      - **Before** (walls unclamped, #123; Skibidi web 0.8.0, `.gamectl/heights4/after-skibidi`): 35 segments with a tunnel composition against 10 CS1 tunnels.
      - **With the walls burn** (web 0.9.3, `.gamectl/walls8`):
        - CacaoCity: 684/684 roads; no tunnel or elevated composition on a CS1 ground road; nothing short of its CS1 height. The ramp is an embankment 7 m above the raised ground, within 0.9 m of CS1.
        - Skibidi: 2035/2035; tunnel compositions = CS1's 10 (+7 slopes); 0 on ground roads; nothing short.
      - **Without the burn** (web 0.9.1, first clamp only, `.gamectl/lanes6/lanes-after-091`): 4 ground ramps still became tunnels (a slope's −14.8 m end won at their node) and 7 ground segments elevated (walls ramps meeting bridge ends at +9…12.5 m, with the middle above 8 m).
        - Fixed by clamping the walls ground node whatever its segment ends say, and the walls middle after every pass (`ClampWallsMiddles`).
        - Re-run with that build (`.gamectl/final10/crop-091`, offNode on): the same 4 + 7.
          - **Tunnels:** CS2 cuts those ramps into 2–4 edges, and in the middle, where the natural ground rises over the CS1 cut, its own pieces sit 12.5–14 m below the terrain and get the tunnel composition. Only the ground beside the road can fix that, and the walls burn does: 0 in the web 0.9.3 runs.
          - **Elevated:** CS2 gives pillars to a walls segment running into a bridge end at ≥ 8 m even with the course's middle at +7.5 m, so the composition next to an elevated node isn't ours to set. Also 0 with the walls burn.
          - One more cause fixed: the clearance pass (#43) pushed a walls node to −17.3 m for a crossing. `Clearance.Room` now keeps walls ground nodes within the limits.
          - **Residual for packages without the walls burn** (before web 0.9.3): a few ground segments in deep cuts or at bridge ends, reported as `unexpected-tunnel` / `unexpected-elevated`.
62. **Walls segments split where CS1's height leaves the ground line** (coordinator, after #57's middle elevation had no effect on CacaoCity 25425).
    - **Why:** CS2 lays a course with ground-level ends on the terrain whatever its curve's y or its middle elevation. CacaoCity's highway 25425 runs straight across a 9 m dip on a CS1 embankment and came out 8.8 m low in it (`walls_middle_kept` set the middle elevation; the course still followed the ground).
    - **Rule:**
      - a walls ground segment (not offNode, not already split) is split at the farthest of 15 sample points (t = i/16) when the deviation there exceeds `ProfileSplit` (1.5 m), with each half at least 8 m long. The new node (id = −segment id) is at CS1's height there, within the wall limits (#61), so CS2 gets the profile from node heights;
      - **planner:** the deviation is the CS1 profile against the straight line between the ends (catches humps in the road itself);
      - **in game, on the package's terrain** (`SplitOverTerrain`, re-batches): the deviation is CS1's height against where CS2 would lay the road, i.e. the real terrain plus the line between the end elevations (catches 25425, whose profile is straight; the dip is in the ground).
    - **Plan ids:** both halves keep the plan segment's id.
      - `ImportJob.SegmentAt(id, node)` gives the half at a node (lane matching, the arm check);
      - `M2Ops.Resolve` joins the halves' paths;
      - ops that rebuild the job without the split find the whole segment through the split node, since `FindPath` walks through non-plan nodes with two edges;
      - verify counts plan segments, built when all their halves are (`segments_planned/built/failed` per plan segment; `courses_planned/built`, `segments_split`, `segments_split_over_terrain`).
    - **Offline count** (plan profile only): 0 segments in the 0.8.0 and 0.9.3 packages of all three cities. The CS1 roads' own profiles are smooth; the in-game count is in the runs.
    - **Tests:** `TestWallsSegmentSplitAtItsProfile`, `TestWallsSegmentSplitOverATerrainDip` (CoreTests 70).
    - **Measured:**
      - The in-game split ran on the package's terrain everywhere: CacaoCity 0.9.3 20 segments split, Skibidi 0.9.3 7, the 0.9.1 crop 18. All roads built (684/684, 2035/2035, 1328/1328).
      - CacaoCity 25425 is within 0.6 m of CS1 (8.8 m low before).
      - **With offNode on by default (#59):** the split first skipped offNode segments, and 25425 has a 5 m Node Controller shift. 17 splits instead of 20, and 25425 was 7 m low again (`.gamectl/final10/cacao-093`).
        - Fixed: offNode segments are split too, each half keeping the shift at its own end and none at the split node (`NodeControllerTests.TestSplitKeepsTheShiftOnItsHalf`).
        - Re-run `.gamectl/final10/cacao-093-b`: 20 splits, 25425 within 0.6 m, ground roads over 2 m: 4, 684/684 roads.
63. **The import never builds a plan on the other side of the road** (DECISIONS.md #138, user report: a CacaoCity test city built right-hand; CacaoCity drives on the left).
    - **Cause:** only the mod's Prepare map sets `CityConfigurationSystem.leftHandTraffic` from `plan.source.leftHandTraffic` (#55, DECISIONS.md #90). Scripts that create a city with `game.new` and import through the exec (`tools/e2e/run.mjs`, the flat-spot verifies) left CS2's default, right-hand.
    - **Refusal:**
      - `DrivingSide.Mismatch(plan, city)`: null when the plan has no driving side (exports before exporter 1.2.1), the city's is unknown, or they agree;
      - otherwise "This package is for a left-hand city; this city drives on the right. Start a new city and press Prepare map." (or the mirror);
      - the exec import returns it as `error` with `refused: "driving-side"` and builds nothing;
      - the mod's Import refuses before any step, with the same text and the setting to use.
    - **Override:** exec `allowDrivingSideMismatch: true`; mod setting "Import into a city driving on the other side" (`AllowOtherDrivingSide`, off by default).
    - **Setting the side in scripts:** exec op `drivingside` {plan} or {leftHand} sets the city's side to the plan's. `tools/e2e/run.mjs` calls it right after `game.new`, before any road. worker-2 was told about `terrain/mapprep.mjs`.
    - **Acceptance:** `tools/release/acceptance.mjs`'s lane-direction step fails when the city's side differs from the plan's.
    - **Right-hand CacaoCity saves from 2026-11-21** (not evidence for left-hand behaviour):
      - `cs1conv-acceptance-20261121-103442`, `cs1conv-qa-cacao`, `cs1conv-qa-cacao-water`: plans without the field (older exports), CS2's default;
      - `cs1conv-util-cacao`, `-cacao2`, `-cacao3`, `-cacao4`: e2e runs (`.gamectl/e2e/20261121-154648`, `-165929`, `-184045`, `-190400`), left-hand plans in right-hand cities;
      - `cs1conv-nc-verify`: worker-5's flat-spot verify (its CacaoCity lane directions came back 366 mirrored).
      
      Left-hand, as they should be: `cs1conv-lht-cacao`, `cs1conv-conform-cacao`, `cs1conv-heights-before-cacao`, `cs1conv-heights-after-cacao`, `cs1conv-heights4-after-cacao`, `cs1conv-lanes6-lanes-cacao`, `cs1conv-walls8-cacao-093`, and `cs1conv-acceptance-20261121-134438`. All went through Prepare map, so every CacaoCity number in #57–#62 came from a left-hand city.
    - **Tests:** `DrivingSideTests` (2).
64. **The roads after a save and reload must be the roads we built** (coordinator, worker-5's #142 measurements; the full-city stress run, docs/QA.md §10).
    - **Resolver** (importer bug, found in the full-city run):
      - where a CS1 power line or pipe ends on a road node, CS2 gives the line its own node at the same position, listing the road edge in `ConnectedEdge` as a local connection;
      - the importer's node index could pick that node, and `EdgesOf` returned the local connection as if it were the node's edge, so 21 built segments became unresolvable for `heights`, `lanedirs`, the lane step and traffic.
      - Fixed: `GameWorld.EdgesOf` returns only edges whose `m_Start`/`m_End` is the node, and `IndexRealNodes` ranks nodes that aren't road/track/path nodes (`IsTransportNode`) last.
    - **Roads that change on reload:** 19 of 2028 Skibidi segments (17 r260c00c0, 1 rdad5619a, 1 rc5895258) gain car lanes after a save and reload.
      - Op `edgestate` (new: per edge prefab, composition flags and lanes, Upgraded, sub-lanes with master/slave/secondary) shows it on all of them: the composition sides go from `L 0 | R 0` in session to `L PrimaryStop | R PrimaryStop` after load, plus a Car Bay Lane. Lane indices shift (e.g. 1516: the bay takes index 1).
      - All 19 carry a bus stop from the transit step. `transit/cs2-exec` `StopsCreate` marks only its CreationDefinition `Updated`, not the attached edge, so CS2 re-selects the composition only on the next load. The other 85 stops of that road type are on compositions without a stop variant.
      - Fix with worker-4 (transit): mark the attached edges `Updated`. Worker-5's final `lanesrepair` step rewrites junctions whose lanes changed after the lanes step.
    - **Bus stops fixed:** worker-4's `stops.touch` marks the stop edges `Updated` after placing the stops, called from `mod/TransitStep.cs`. Worker-5's final `lanesrepair` rewrites the bay-shifted junctions (13 on Skibidi), so no step reorder is needed.
      - Verified (`.gamectl/reload/{skibidi,cacao}-c`): car lanes unchanged on reload for every segment (Skibidi 0 of 2035, was 19; Cacao 0 of 684).
      - Turn check right after the import 117/117 and 167/167, including the 22 Skibidi junctions with connections on bus-stop roads.
      - `lanedirs` skips "Bay Lane" lanes, worker-5's rule.
    - **Turn bans and crosswalk changes at nodes that aren't junctions:**
      - Before the fix: the side flags were lost on reload on 4 Skibidi and 2 Cacao roads, and on 26 Skibidi roads with only roads + traffic.
      - CS1's TM:PE allows turn bans and crosswalk changes at dead ends and at nodes between two segments. CS2 keeps them only where 3 or more roads meet: it drops them when it validates the upgrades, and the composition follows only on the next load.
      - Every lost rule end was at a node of degree 1 or 2 (27 of the 30 such ends in the bisect), none of the 62 at degree 3–4.
      - Fixed: the traffic op skips side upgrades at CS2 nodes with fewer than 3 edges and reports them (`ends_not_at_a_junction`, with examples). CS2 wouldn't keep them.
      - That wasn't all: CacaoCity also lost crosswalk changes at real junctions (12 roads, several elevated, where CS2 draws no crosswalk), and a different set from run to run. CS2's acceptance rules aren't predictable from the plan.
      - So the mod now ends with a `settle` step (op `settle`, before `lanesrepair`): every plan edge whose composition's turn-ban/crosswalk bits differ from its `Upgraded` flags is marked `Updated`, with its nodes, so CS2 re-selects it in session. Status line only when it did something: "Roads: N updated to the crosswalks and turn rules CS2 keeps there …".
    - **Verified with everything on** (`.gamectl/reload/{skibidi,cacao}-f`: lane connections, transit with `stops.touch`, `settle`, `lanesrepair`):
      - CacaoCity: 684/684 roads, 0 car-lane and 0 composition changes on reload, turn check 167/167.
      - Skibidi: 2035/2035, 0 composition changes, turn check 117/117, `lanesrepair` rewrote the 13 bay-shifted junctions (18 turns rechecked, 0 wrong), 1 car-lane change.
      - That one (segment 3712) has its lanes split at mid-edge in session (EdgeDelta 0–0.5 / 0.5–1) and whole after the reload. It comes from the new powerhook step's low-voltage line joining the road mid-edge, handed to the buildings worker.
    - **Acceptance:** step `roads-reload` dumps `lanedirs` and `edgestate` before a save and after reloading it, and fails on any segment whose car lanes, prefab or composition differ (`roads-reload.json`).
