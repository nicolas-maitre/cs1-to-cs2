# Decision log

Important decisions, newest last. Each entry says what was decided, why, and who decided it ("user" = the repo owner, "Claude" = taken autonomously while the user was away; review those).

## 2026-11-20

1. **Architecture: two mods + a web tool** (user). The CS1 exporter writes `.cs1x`. A client-side web tool previews it, crops it and maps assets and roads. The CS2 importer rebuilds the city.
2. **Must support** (user): 81 Tiles 2 (cropping is fine), TM:PE, Node Controller, IMT (at minimum it must not crash) and Vanilla+ (WQ) roads → Road Builder.
3. **IMT fill rules** (user):
   - pavement or grass fill over a whole lane → RB median;
   - red solid fill over a whole lane → RB bus lane.
4. **Game control by script, not mouse** (user asked; design by Claude). A bridge mod per game, loopback HTTP with a token, plus the `tools/gamectl` CLI. See `tools/PROTOCOL.md`.
5. **CS2 autosaves are blocked in script-driven sessions** (user asked for it; in memory only, never written to settings). Later the user said overwriting an autosave "isn't a big deal", so the guard stays only because it's harmless.
6. **CS2 junctions: the importer builds every junction itself** (Claude, from Spike 1b).
   - One position per CS1 node.
   - `Permanent` definitions, placed in batches.
   - A CS1 node → CS2 node entity map.
   - Courses never cross or end on an existing edge.

## 2026-11-21 (before the user left for the night)

7. **Partial IMT fills: full coverage only** (user). A lane change (median, bus lane) is applied only when the fill covers (almost) the whole segment. Partial fills are listed in the import report, and the road stays plain.
8. **Grey IMT fills are junction paint, reported only** (Claude, after checking with the user).
   - The 15 grey fills in Skibidi.02 are `FillerSolid` `rgba(136,136,136,224)`: flat paint, not raised pavement.
   - All of them are node (junction) markings, on 6 nodes, each 5–143 m².
   - Node fills have no CS2 equivalent, so they go into the import report.
   - A grey solid fill on a segment would also be reported only. It's paint, not a kerb, so it doesn't become a median.
9. **Test saves** (user): Claude may create new CS1/CS2 cities and saves for tests, named `cs1conv-…`. Existing named saves (e.g. Skibidi.02) are only ever loaded read-only and are never overwritten.
10. **Landing work** (user): sub-sessions build; Claude reviews, merges into `main` and pushes. No PRs.
11. **Overnight scope** (user): after M1, keep going through M2 and M3 (buildings/assets, parks, services, IMT node-fill decals) and make the result release-grade: polished mods and website. Claude coordinates the 4 worker sessions and checks on them every 30 minutes, restarting any that stalled (usage limits, errors).

## 2026-11-21: M1 (overnight, by the worker sessions)

Full reasoning is in each component's log: [cs1-exporter](../cs1-exporter/DECISIONS-exporter.md), [web](../web/DECISIONS-web.md), [cs2-importer](../cs2-importer/DECISIONS-importer.md), [terrain](../terrain/DECISIONS-terrain.md). The main ones:

12. **Exporter: one DLL is both the mod and the `exec` library** (Claude). It has an "Export city" button and a `Exporter.Run(args)` entry point.
    - Its zip writer is hand-written, because .NET 3.5 has none.
    - It writes to a temp file and then renames it, so a crash never leaves a half-written `.cs1x`.
    - Errors are isolated per item and per section.
13. **Exporter: TM:PE / Node Controller data stays out of M1** (Claude). The M1 importer couldn't apply it; it comes with M2.
14. **Exporter: the terrain layout was verified in game** (Claude). The spec's layout is right: the median error is 0.011 m against 6520 ground nodes, while transposed or flipped layouts are off by 22–57 m.
15. **Web: one TypeScript core for both the UI and the CLI** (Claude). The spike's RB generator was ported verbatim; a test checks its output against the spike scripts.
16. **Web: highways get an RB highway profile** (Claude). They have shoulders instead of sidewalks and lose width (22 → 16 m), which is reported.
17. **Web: elevated, bridge, slope and tunnel segments use their ground road's RB config** (Claude). RB builds those forms itself.
18. **Web: CS1 building sub-networks (`Untouchable`: station tracks, park paths, dam roads) are skipped until M3** (Claude). Placing them alone would leave stubs.
19. **Web: inverted CS1 segments are reversed in the plan** (`"reversed": true`; Claude). The importer always builds a segment from `start` to `end`.
20. **Web: auto crop picks the densest area by road segments, not nodes** (Claude). Pipes and power lines skew node density.
21. **Web: `--install-rb` manages only `*-cs1conv.json` files in RB's folder** (Claude). It also removes stale ones.
22. **Importer: a hot-loaded `exec` library, not a deployed mod, for M1** (Claude). One call runs the whole import as a coroutine in `ToolUpdate`. The core has no game references and its own tests. A user-facing mod comes in M2.
23. **Importer: the Spike 1b recipe with spatial batches of 400 segments** (Claude).
    - A node map links batches; nodes are matched within 1 m horizontally and 3 m vertically.
    - Missing prefabs fail their own segments only, never the whole import.
24. **Importer: node elevation rule** (Claude).
    - CS1 terrain offsets under 4 m are ignored.
    - Offsets of 4 m or more (real terraforming, e.g. a highway trench) are kept, within −11.5 … +7.5 m.
    - This changes once real terrain is imported.
25. **Importer: "built" means a path of real edges connects the two nodes** (Claude). The report also counts wrong-prefab edges, elevated and tunnel compositions, and Temp ghosts.
26. **Bridge: `game.new` writes city options only to the in-memory city overrides, never to user settings** (Claude). `save.write` accepts only `cs1conv-*` names.
27. **M1 test map: Corral Riches, with the plan shifted by (+2000, −4000)** (Claude). No built-in map is flat at its centre.
28. **CS1 autosave** (Claude, noted): CS1 rewrote its own `AutoSave.crp` while a script had Skibidi.02 loaded. Nothing unique was lost, and the user doesn't mind autosaves being overwritten. Skibidi.02 itself is never saved.
29. **Terrain: CS2 height = CS1 height** (`SetTerrainProperties(1024, 0)` + `ReplaceHeightmap`, which also works in a running city; Claude).
    - The 4096² heightmap covers the whole 14 336 m playable area from CS1 data, repeating the edge values outside CS1's map.
    - The 57 km world map backdrop is also built from CS1 data, so there is no void past the edge.
    - Verified in game: median 0.013 m against CS1 node heights, and it survives save/reload.
30. **Terrain: sea level is set just below the lowest point** (a dry map until CS1 water conversion lands in M2), and the whole area is re-snapped so trees follow the new ground (Claude).
31. **Order: new city → clear the base map's own roads and rails → terrain → road import** (Claude). CS2 doesn't move existing nets when the terrain changes; it builds walls up to them, as with the test map's pre-built highway and rail. So the base map's nets must go first, and outside connections must be rebuilt afterwards.

## 2026-11-21: M2 (overnight)

32. **CS1 autosave guard** (Claude; the CS2 guard's counterpart). Every frame, while the guard is on, the bridge stops CS1's `AutoSaveTimer` coroutine.
    - Lifting the guard calls `Start()`, which restarts the interval from zero, so there is never an instant autosave.
    - In memory only; settings are never changed.
    - `Pause()` was tried first and failed: CS1's panels reset the pause counter.
33. **Exporter M2 sections** (Claude; specs in `schema/README.md` §1):
    - `zones.json`: zone cells packed as compact strings;
    - `water.json` + `water.r16`: sea level, sources, and a snapshot of the water depth on the terrain grid;
    - `transit.json`: lines, colours, stops in order, and the station building found within 64 m;
    - `tmpe.json`: read through TM:PE's own save routines, so it contains only user customisations;
    - `nodecontroller.json`;
    - `buildings.json`, `props.json` and `trees.json`, ready for M3.

    Skibidi: 10 288 zone blocks, 40 water sources, 21 lines / 345 stops, 674 lane arrows, 928 lane connections, 625 priority signs, 37 timed lights, 11 741 buildings, 41 777 trees.
34. **Node Controller data covers every node NC touched, not only user edits** (Claude). Consumers compare `type` with `defaultType` to find the user's changes.
35. **Exporter M3 data** (Claude):
    - `assets.json`, a catalogue of every prefab used, including each building AI's primitive fields (capacity, radius, workers), for M3 asset matching;
    - `districts.json` with byte rasters for districts and park areas (orientation checked against the game's own lookup on every export);
    - `buildings.json` gains each building's access segment, district and park.

    Skibidi: 553 prefab types, 22 of them from the workshop.
36. **CS1 exporter 1.2.0 is release-grade, but not published** (Claude).
    - It has an options page with section switches, a live status, and result and error dialogs.
    - It exports one section per frame with the simulation paused, and streams the zip so memory stays flat.
    - It's tested against missing IMT / TM:PE / NC / 81 Tiles.
    - Workshop preview, description and changelog are prepared. **Uploading to the Workshop is left to the user.**
37. **The web tool has no public URL yet** (Claude). The exporter's "Open web tool" button defaults to the local dev address until a hosting decision is made, which is the user's call.
38. **Web tool: release-grade static site** (Claude).
    - A landing page with 6 steps and clear error states.
    - Packages are built in a Web Worker, and the map is drawn in 2 km tiles (4–7 ms per redraw on a 64k-segment city).
    - Keyboard, accessibility and phone layout covered.
    - The package zip includes a `README.txt`.
    - It builds to a relocatable `dist/`. **Nothing is deployed**: hosting is the user's call.
39. **Import plan M2 additions stay `formatVersion: 1`, all optional** (Claude). An M1 importer still builds the roads. Details are in `schema/README.md` §2 and `web/DECISIONS-web.md` 16–32. Each conversion is aimed at a specific CS2 target:

    | CS1 data | CS2 target |
    | --- | --- |
    | Zoning | CS2 zone cells, NA theme by default |
    | TM:PE speed limits, bus lanes | new Road Builder variants |
    | Traffic lights, all-way stops, turn bans, crosswalks | vanilla CS2 settings |
    | Priority signs, lane arrows, lane connections | Traffic mod |
    | Transit lines | CS2 lines (the station lines need stations) |
    | Water | sea level + sources |
    | Node Controller | reported only |

## 2026-11-21: M3 (overnight)

40. **CS2 building catalogue + CS1 → CS2 matching** (Claude; details in `buildings/DECISIONS-buildings.md`).
    - **Catalogue:** dumped from the running game: 3571 buildings, of which 263 placeable building prefabs are kept in a slim copy for the web tool.
    - **Matching:** the same family on both sides, then a score from kind, capacity, lot size and name. Capacity ranks the candidates but isn't converted, because the units differ between the games.
    - **Growables** are left to CS2 zoning.
    - **Not placed:** pillars, poles, pipe junctions and outside connections are skipped. Library, taxi stand, cable car station and ferry depot have no CS2 equivalent.
41. **Building placement uses the game's own recipe** (Claude): `CreationDefinition` Permanent + `ObjectDefinition`, as `ZoneSpawnSystem` does.
    - The lot front snaps to the imported road on the building's CS1 side, facing the road.
    - On a collision it slides ±24 m along the road, then tries the matcher's alternatives.
    - Skibidi 3 km: 77 of 108 placed, 71 of them connected.
    - 31 refused, because CS2 service lots are much bigger (e.g. CS1 crematory 3×3 cells vs CS2 8×10).
    - **Open:** a user choice (smaller alternative, nearby free land, or accept) belongs in the web tool. Roads on embankments block building access.

## 2026-11-21: M2 importer, map prep and M3 web (overnight)

42. **One-command end to end: `tools/e2e/run.mjs`** (Claude). The order is fixed:
    1. new city on Sunshine Peninsula, then clear the base map's nets;
    2. CS1 terrain;
    3. water;
    4. road import;
    5. outside connections;
    6. checks.

    Runs pass 2035/2035 on the Skibidi 3 km crop. CS1 and CS2 are shared between workers through lock files (`cs1.lock`, `cs2.lock`; another owner's lock under 2 h old means wait).
43. **User-facing CS2 importer mod `Cs2ImporterMod`** (Claude). It's built from the same sources as the exec library and runs in its own `ToolUpdate` system, one step per frame. The game's settings UI has no read-only text field, so progress is shown as a rewritten locale label. **Not published.**
44. **Embankments snap to the terrain; trenches keep their depth** (Claude). Raised CS1 roads had made CS2 build retaining walls with no building access (walled edges went from 94 to 26). `keepEmbankments:true` restores the old behaviour. An experimental `heights:"exact"` mode exists but is off.
45. **Map prep: rebuild outside connections at the crop edge, not the map edge** (Claude). After 5 minutes of simulation: 94–113 cars, up to 28 trains, 28–59 trucks.
46. **Water: CS1 sea level as is (heights are absolute), the base map's water sources removed, CS1 natural sources recreated** (Claude). CS2's definition path only made temporary sources, so they're now created directly. Water depth vs CS1: median error 0.66 m, p90 1.97 m.
47. **Default base map: Sunshine Peninsula**, chosen after surveying all 12 built-in maps in game (Claude).
48. **Web M3: the buildings worker's matcher is reused as is, and the plan's `buildings` section shares its item shape** (Claude). The zoning theme breaks ties between equally good CS2 buildings. The site bundles a slim CS2 catalogue. Growables are counted, not placed.
49. **One source for the user docs** (Claude): `docs/guide/` is also shown in the web app's Help. The README states each feature's real status and says plainly that the mods aren't published yet.
