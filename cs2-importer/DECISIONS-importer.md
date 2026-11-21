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
