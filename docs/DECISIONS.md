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
50. **Oversized CS2 service lots: "nearby free land" fallback** (Claude). It runs after the slides and alternatives: it searches imported roads within 150 m, closest first, and can be set per type (`nearby` | `skip`).
    - Skibidi 3 km: 96/108 placed, 94 connected; buildings moved a median of 56 m.
    - Still refused: 6 combustion plants, 2 cargo centres, 2 crematories, a prison and a university, whose CS2 lots are huge.
    - Placement is idempotent.
51. **Trees: a small tested species matcher, placed as adult objects, 2000 per frame** (Claude). In the crop, 2744 of 4216 were created in 0.6 s. 1359 were skipped because they fall on Road Builder roads, which are wider than CS1 roads.
52. **Districts: traced from the raster into polygons and created as CS2 "District Area"s, with names and the policies CS2 supports** (Claude). Verified only on a synthetic plan, since Skibidi has no districts. Park, industry and campus areas have no CS2 counterpart and are reported.
53. **IMT junction fills are not converted** (Claude). CS2 draws its junction surface over any surface area placed there, and its decals are fixed images with no colour fill. They stay in the import report. Off-road pavement/grass fills keep their surface mapping.
54. **Known bridge limit:** the first tool-phase `exec` after `save.load` fails with "EntityCommandBuffer not allowed". Drivers retry once, and exec code must not call game methods that rely on end-of-frame command buffers.

## 2026-11-21: Release engineering (overnight)

55. **Versioning** (Claude): the product `VERSION` is `0.<milestone>.<patch>` (now 0.3.0). Each component has its own SemVer, read from one source of truth each: cs1-exporter 1.2.0, cs2-importer 0.2.0, web 0.4.0, buildings 0.3.0. `node tools/release/build.mjs --check` enforces that they agree.
56. **Release build: `node tools/release/build.mjs` → `release/<version>/`** (git-ignored; Claude). It produces mod folders, zips, `SHA256SUMS.txt` and `RELEASE-NOTES.md` assembled from the component changelogs. Mods are deployed into `release/`, never into the installed games. **Nothing is published** (Workshop, Paradox Mods, web hosting are the user's call).
57. **CI: one GitHub Actions job on `windows-latest`, game-free checks only** (Claude). It runs the version check, the web build and tests, the gamectl, terrain, buildings and release tests, and the importer core tests. Builds that need the games' DLLs can't run in CI and stay local. One job with caching keeps Actions minutes low; delete `.github/workflows/ci.yml` if they aren't wanted.
58. **Buildings, objects and districts become importer steps** through `buildings/src/Steps.cs`. The CS2 importer mod includes these sources and registers them; a plan without those sections imports as before (Claude).
59. **Transit lines go into the CS2 importer mod, not only the e2e script** (Claude). A release-grade user flow can't depend on Node scripts. The mod includes `transit/`'s sources the way it includes `buildings/`.
60. **QA on 3 more of the user's cities** (Rennmont, CacaoCity, Westfield; read-only): every export section is `ok` and nothing crashed. CacaoCity went through the full e2e: 684/684 roads, 160/160 traffic lights, 33/43 buildings, 10 393 trees, traffic after 60 s. Its water converts poorly (42/150 wet points) and is being worked on. Details in `docs/QA.md`.
61. **Transit lines go through CS2's own line-tool pipeline** (route definition, then one forced apply frame), with kerbside stops attached to the nearest imported road edge (Claude). Details in `transit/DECISIONS-transit.md`.
    - Stop prefabs follow the city theme.
    - Station lines reuse the station stops within 200 m of the CS1 stations.
    - Vehicles come from depots placed by the buildings step.
    - Skibidi: 145/145 stops, 9/9 lines with CS1 names and colours, and 29–34 buses running within 4.5 minutes. The metro line has no trains because the city has no subway yard.
62. **`mapprep clear` also deletes the base map's own buildings** (Claude). Sunshine Peninsula's ruin landmarks had pulled the new terrain up into pillars. Big lakes get an extra 80 m water source at their deepest point so they fill in minutes. CacaoCity: 105/150 wet points after 5 minutes, up from 42.
63. **The package is self-contained** (Claude). The web core ports the terrain converter, water and outside-connection code, so a user needs only the web app and the CS2 mod, no Node scripts.
    - Heightmaps are computed only when the package is written (about 1.3 s).
    - Plain r16 is deflated inside the zip (64 MB → about 21 MB).
    - Trees, rocks and IMT surfaces are in the plan itself.
64. **Every building in a package carries a placement `fallback`** (Claude): `anywhere` for CS2 lots over 400 cells, `nearby` otherwise, and the user can change it per type in the Assets tab. Trees that land on a CS2 road are moved off it instead of dropped.
65. **Cs2ImporterMod runs the whole import from its Options page** (Claude):
    1. roads;
    2. vanilla traffic settings;
    3. Traffic mod priority signs (by reflection; skipped if the mod isn't installed);
    4. zoning;
    5. buildings, objects, districts and surfaces (`buildings/src` compiled in);
    6. transit (`transit/cs2-exec` compiled in).

    Every step has its own toggle. Progress shows in the status line, and a JSON report goes to `%LOCALAPPDATA%\cs1-to-cs2\import-reports`.

    The mod-only user flow on Skibidi 3 km built 2035/2035 roads in 4.3 s, 325 priority signs, 93% of 76 658 zone cells, 108/108 buildings and 9/9 transit lines.
66. **Heights are always "flat" (terrain-relative)** (Claude). The experimental "exact" mode crashes CS2 natively.
67. **Bridge and mod call `AllowUsage()` on every `SafeCommandBufferSystem` before running step or exec code** (Claude). This fixes "EntityCommandBuffer not allowed" after `save.load` (decision 54), verified in game.
68. **Traffic mod lane connections (237) and lane arrows (211) are not applied yet** (Claude). The mod's data model needs lane-handle entities; they're reported for now.
69. **The e2e has plug-in steps** (`tools/e2e/steps/*.mjs`, each run after a named phase) and a `--no-*` switch for every M2 step (Claude).
70. **Web 0.5.0: English + French** (Claude), picked from the browser language with a switch. A test fails if any UI string lacks a translation. The French guide is in `docs/guide/fr/`.
71. **The user guide quotes the importer mod's exact option labels, and a test checks them against `cs2-importer/mod/LocaleEN.cs`** (Claude), so a renamed option fails the build instead of silently making the guide wrong.
72. **Default transit depots** (Claude). When the CS1 crop has a line but no depot of its type, the package adds a default one (e.g. SubwayYard01) and reports it. Rail depots need their track connected by hand.
73. **Shared pure libraries are committed together with the web tool when the web tool depends on their new behaviour** (`terrain/lib/water.mjs`, `buildings/lib/plan.mjs`), so CI never sees one without the other (Claude, coordination rule).
74. **The CS2 importer mod prepares the map itself: a "Prepare map" button in a "New city" group** (Claude). It runs, in order:
    1. clear base-map nets and buildings;
    2. terrain and world map from the package, plus sea level;
    3. water sources;
    4. settle, then a wet/dry check.

    Outside connections are the last Import step. The terrain code is compiled in from `terrain/cs2-apply`, and its exec API stays stable.

    Mod-only flow, no Node tools:
    - Skibidi 3 km: terrain error 0 m; water 94/100 wet after 2 min; 2035/2035 roads; 108/108 buildings; 9/9 lines; 20/20 connections; 624 cars.
    - Whole city (14.3 km): 4738/4740 roads, 10/10 connections, trains and cargo running.
75. **"Edge missing" at shallow forks was a verification bug, not a build bug** (Claude). CS2 merges the first metres of two courses that leave a node at a shallow angle. Verify now walks through CS2 fork nodes and lists them as `merged-fork`. Clearance pass: crossings with < min(8 m, CS1's clearance) are lowered or raised, but never past their CS1 height. The 2 remaining whole-city misses are CS1 geometry that overlaps in CS2, and they're reported.
76. **The transit planner's C# port is guarded by a golden test** generated from `transit/lib/plan.mjs` (Claude).
77. **Full-city stress test passed** (Skibidi 14 336 m: 4741 segments, 253 buildings, 20 lines; Claude).
    - **Times:** import 3.9 s, buildings 37 s, transit 50 s.
    - **Memory and save:** CS2 working set up to 5 GB, save 35 MB.
    - **Fixed at scale:** a 27 s placer freeze (it now yields every 20 ms) and a dry 3.5 km river. Sources are now spread over big water regions: 146/150 wet, median depth error 1.07 m.
    - Details in `docs/QA.md` §4.
78. **Default depots for every transit type without a matched CS1 depot** (BusDepot01 / TramDepot01 / SubwayYard01 / RailYard01; Claude). CS2 lines only get vehicles from a depot of their type. Rail and subway yards still need their track linked, so metro and train lines show 0 trains until that's done.
79. **Release 0.3.0 accepted through the real user flow** (Claude; `tools/release/acceptance.mjs`, `docs/QA.md` §6).
    - **Setup:** the mod was installed from the release zip and the package built by the web CLI.
    - **Steps,** each driven by its own mod button: Install RB configs → restart → new city → Check → Prepare map (15.6 s) → Import (49 s).
    - **Result:** roads 2035/2035, zoning 93%, 325 priority signs, buildings 109/109, trees 3380/4216, transit 9/9 lines, outside connections 20/20.
80. **Trees on wider CS2 roads are moved aside (mean 3.1 m) instead of dropped**: 705 moved, 654 dropped, down from 1359 (Claude).
81. **Building fallback passes: nearby 150 m → 400 m → `anywhere` in the crop, chosen per type in the web tool** (Claude). With `anywhere`, Skibidi places 108/108, with a maximum move of 1.2 km for the huge CS2 lots. The distance is reported for every building.
82. **`tools/release/build.mjs` lands together with the importer's 0.3.0 version bump and changelog** (Claude, coordination). Committed on its own, its version check fails.
83. **Rail and subway depots are never invented inside the converted city** (user, 2026-11-21 morning; supersedes the rail/subway part of #78). Depots are big, and the importer shouldn't invent city content.
    - A rail or subway yard is placed only at the crop edge, next to where the line's own track leaves the crop (free land, nothing in the city disturbed), and linked to that track.
    - If the line has no track leaving the crop (e.g. a metro entirely inside it), no depot is placed. The import report names the line and asks the user to place one.
    - The web tool offers per type: "Depot at the crop edge" (default for rail/subway) or "I'll place it myself".
    - Bus and tram default depots stay automatic for now (small, road-only); open for the user to change.
84. **Placed buildings get their own internal networks** (station and depot tracks, internal paths, parking; Claude). They're created as owned sub-nets, the way the game does it, so "Remove imported" deletes them with their building. Verified on 110 Skibidi buildings with none missing. Default depots are now bus and tram only (#83).
85. **Cs2ImporterMod 0.3.0 is release-grade, but not published** (Claude; details in `cs2-importer/DECISIONS-importer.md` #47–53).
    - **Packaging:** Paradox Mods publish config (Road Builder as dependency; Traffic is optional and only mentioned, because the format has no optional dependencies) and a thumbnail. **Uploading is the user's call.**
    - **Packages:** a Windows "Browse…" picker; zips are unpacked automatically; the last 5 packages are remembered.
    - **Errors and checks:** clear error texts. Import refuses a city that wasn't prepared (median terrain gap over 1 m; "Keep this map's terrain" overrides).
    - **Controls:** Cancel during Prepare map and Import, and confirmations before destructive actions.
    - **French:** follows the game's language.
86. **"Remove imported"** (Claude). The import records what it created as the difference between a snapshot before and after, stored per city by kind, prefab and position (entity ids don't survive save/load). Removal uses the game's own deletion patterns. Verified after save/load: everything created was removed, back to exactly the prepared city.
87. **Traffic mod lane connections and arrows: experimental, OFF by default** (Claude). They go through the Traffic mod's own data model by reflection: 429 connections on 90/94 junctions, but only 117 of 156 explicit lanes match CS1 exactly, and persistence after save/load isn't checked. U-turn rules aren't applied.
88. **Web 0.5.1: automated UI quality** (Claude). UI smoke tests (landing, open a synthetic `.cs1x`, keyboard crop, What converts, package contents, French switch) and axe-core accessibility checks run in CI. The guide (EN + FR) follows importer 0.3.0: Browse… the zip, Prepare map, the new toggles, Cancel, Remove imported. The French guide quotes the mod's French labels, and both are checked against the mod's locale files.
89. **Lane connections and arrows: ON by default for right-hand cities, OFF for left-hand ones (for now)** (Claude; this overrides the 95% bar in #87).
    - Right-hand Skibidi matches 92–94% of CS1's lane sources: 117/117 at junctions, the rest continuous. The remainder are lane splits at nodes without a junction, which CS2 can't express.
    - Everything written matches exactly, so the step never makes a junction worse. Connections persist through save/load.
    - U-turn rules are written as lane connections, because the Traffic mod has no U-turn rule.
90. **Traffic handedness is carried through the pipeline** (Claude).
    - The exporter reads `manifest.traffic.leftHand` and the plan carries it as `plan.source.leftHandTraffic`.
    - Prepare map sets the CS2 city's handedness while the map has no roads.
    - Check warns when a loaded city drives on the other side.
    - Left-hand cities still need a road-level check (one-way direction under CS2's left-hand flip) before lane connections are enabled for them.
91. **Two-city acceptance with the 0.3.0 release artefacts passed** (Claude; `docs/QA.md` §6):
    - CacaoCity: 684/684 roads, 43/43 buildings, 10 813 trees, 44/44 IMT surfaces, zoning 91%.
    - Westfield: 648/648 roads, 13/13 buildings, zoning 99.99%, 22/22 connections.

    Metro and train vehicles are still pending on the crop-edge yard step (#83).
92. **District and city policies map only to real CS2 policy prefabs** (Claude). The list of CS2's 23 policies was read from the game, and anything without an equivalent is reported as `policiesMissing`; a web test keeps the web map equal to the placer's. Exporter 1.2.1 adds `manifest.traffic.leftHand`.
93. **Placement options `requireRoad` and `maxDistance` per item** (Claude), used for crop-edge yards, which keep to a 2.5 km link reach.
94. **Left-hand cities: the importer builds every course end → start** (Claude). Before this, every one-way road ran backwards in a converted left-hand city (CacaoCity: 42/42 one-way, 15/15 asymmetric wrong). Reversing a course mirrors and reverses it, which is exactly the left-hand change and a no-op for symmetric two-way roads. After the fix: one-way 141/141, asymmetric 16/16, lane connections 98.4%. Lane connections are now on by default for left-hand cities too (supersedes the left-hand part of #89). The fix ships as importer **0.3.1**.
95. **`install-rb` keeps other cities' Road Builder configs by default** (Claude). Pruning them becomes opt-in (`--prune`), because the old default twice silently broke runs.
96. **CS1 bicycle-only lanes don't count as driving lanes** when lanes are matched to CS2 Road Builder roads (Claude). Otherwise 212 of CacaoCity's lane references can't resolve.
97. **IMT median variants really replace the covered car lane** (Claude, applying user rule #3). A Large Road 3+3 with a pavement fill over one lane becomes 2+3 with a 2 m median, as the CS1 road looked. Lane references into the removed lane are dropped and reported ("lane removed by IMT median"), and the other ordinals are shifted to the variant's lanes. CacaoCity after the bicycle-lane fix: 572/764 lane references resolved, and the remaining 190 are all such lanes.
98. **Web 0.5.2 lane numbering follows the generated Road Builder road** (Claude). Bicycle-only lanes and lanes covered by an IMT median are left out of the counts and ordinals. References on a removed lane are dropped and reported. CacaoCity in game: 727/729 lane references resolve, and 99% of explicit source lanes match CS1 (decisions 96–97). `install-rb` keeps other configs unless `--prune` is given (#95).
99. **Crop-edge rail yards, track links and "place a depot yourself" messages are committed as an opt-in step** (`node transit/transit.mjs depots`; Claude). They work in game, but **converted train and metro lines still run no vehicles**: CS2 never computes their route, even with the stations joined. Buses run normally. Four causes are ruled out; the likeliest left is how the placer builds a station's own tracks and stops. **The release candidate doesn't wait for this.** Trains and metros are a documented known limit, and the investigation continues (build a station the game's own way and compare).
100. **Prefer vanilla CS2 roads when they match** (user, 2026-11-21).
     - A CS1 road whose generated profile matches a vanilla CS2 road within a tolerance (lanes, directions, widths, sidewalks, median) is mapped to that vanilla road. For example, Basic Road → Small Road.
     - Road Builder stays for everything without an equivalent: IMT variants, workshop roads, TM:PE speed and bus-lane variants.
     - The web tool gets a "Prefer vanilla roads" switch, on by default, plus the existing per-type override.
     - This reduces the dependency on Road Builder configs and gives native CS2 behaviour.
101. **Why converted trains and metros don't run: four causes found** (Claude; `docs/QA.md` §5c).
     1. CS2 only pathfinds a route segment when its target lanes change, so lines created before their stations are joined never get a path. Fixed: `transit depots --recreate`.
     2. A Permanent course from a building-owned node is silently not built. Fixed: links go through Temp + a forced apply.
     3. Elevated CS1 metro stations are matched to ground-level CS2 stations. They need SubwayStationElevated01/02. Open, buildings worker.
     4. CS2 train stations are placed over the imported track instead of continuing it. They need the CS1 position and orientation. Open, buildings worker.

     Trains and metros stay a known limit until 3–4 are fixed and verified.
102. **Tram-only lanes are left out of car-lane ordinals and counts, like bicycle-only lanes** (Claude). Shared car+tram lanes stay counted. Web and importer agree.
103. **The release candidate passed acceptance on three cities with importer 0.3.1 and web 0.5.2** (Claude; `docs/QA.md` §7):

     | | Skibidi 3 km | CacaoCity (left-hand) | Westfield |
     | --- | --- | --- | --- |
     | Roads | 2035/2035 | 684/684 | 648/648 |
     | Lane connections (CS1 sources matched) | 92% | 99% | 95.7% |
     | Buildings | 108/108 | 43/43 | 13/13 |

     Every compared road runs in CS1's direction, and no lane connection was set wrong. Trains and metros are the known limit (#99, #101). The final release is cut after the vanilla-road preference (#100) lands and passes the same acceptance.
104. **Web 0.6.0: the vanilla-road matcher** (`web/src/core/vanilla.ts`; Claude; details in `web/DECISIONS-web.md` 87–90). It compares the road that would be generated with CS2's 49 vanilla roads dumped from the game:
     - the same driving lanes and directions per side;
     - each lane ±0.5 m wide and ±0.75 m in position;
     - parking and bike lanes on the same sides;
     - median ±1 m (a gap of 1 m or less isn't a median);
     - total width ±1 m.

     Only a TM:PE speed counts as a speed difference, because default speeds always differ between the games. Roads with trees, and bridges, quays and dams, stay on Road Builder.

     | | Skibidi 3 km | CacaoCity | Westfield |
     | --- | --- | --- | --- |
     | Road types matched to vanilla | 7 | 8 | 5 |
     | Segments on vanilla roads | 341 | 117 | 402 of 647 |
     | Road Builder configs | 26, down from 33 | 32 | 10 |
105. **Importer 0.3.2: vanilla road targets need no special handling** (Claude). Every step works from edges, nodes and CS2 car lanes. The new `roadlayouts` exec op dumps the vanilla layouts the web tool matches against.

     | | Skibidi | Westfield | CacaoCity (left-hand) |
     | --- | --- | --- | --- |
     | Lane connections matched (web 0.6.0 packages) | 92% | 95.7% | 99% |

     That's the same as with all Road Builder roads, and connections persist through save/load.
106. **Buildings are never moved to free land by default** (user, 2026-11-21; supersedes the defaults in #64 and #81). The placer tries:
     1. the exact CS1 spot;
     2. a short slide along the same road;
     3. a smaller CS2 alternative of the same kind.

     If none fits, the building isn't placed. The import report lists it as "place it yourself", with its CS1 position and the reason. "Look nearby" and "anywhere in the crop" stay available as explicit per-type choices in the web tool, off by default. This applies to plans without a `fallback` field too.
107. **Node Controller shifts and offsets must be applied, not only reported** (user, 2026-11-21; supersedes the Node Controller part of #39). In CS1, roads can only join at a node's centre, and Node Controller's per-end shift and offset are how players lined lanes up across junctions and ramps. Dropping them makes converted roads jog sideways at those nodes in CS2, which itself supports lane-aligned connections.
     - The plan applies each road end's shift and offset.
     - The importer builds them the way CS2's lane alignment does, after research on how the road tool stores it.
     - Free-form slope and twist stay report-only unless CS2 has an equivalent.
108. **Web 0.6.1 + buildings 0.3.1 implement #106** (Claude).
     - **Default fallback** is "skip" for every type, in the web plan and in the placer for plans without a `fallback`.
     - **Report wording:** unplaced buildings read "place it yourself at CS1 (x, z) = CS2 (x, z)".
     - **Station fixes:** elevated CS1 metro stations match elevated CS2 stations; through stations never become a terminus; stations are placed only at their CS1 spot, otherwise reported. That places 10/11 stations on Skibidi 6 km.
     - **Tests:** placer rules are a game-free test project, now run in CI.
109. **Acceptance includes a utilities check** (user, 2026-11-21). After the simulation it measures the share of buildings with electricity, water and sewage service. It also flags water pumps and sewage outlets that aren't on water, and power plants or pumps with no network link. Utility networks (pipes, power lines) stay unconverted, because CS2 roads carry them. Fixes for the gaps found (e.g. linking remote plants, electricity/water outside connections) are decided from the measurements.
110. **Road heights must follow CS1's profile; the snapping in #44 is to be revisited** (user report, 2026-11-21). On CacaoCity, a highway ramp that is a straight slope in CS1 follows the terrain's bumps in CS2. Planned fix, being measured first:
     - ramps, highways and roads without building frontage keep their CS1 heights, with an embankment or retaining wall;
     - only roads that need building access snap to the terrain;
     - ground curves follow CS1's vertical profile.

     The crash in "exact" heights (#66) gets a time-boxed second look.
111. **The ground snaps to the road, not the road to the ground** (user, 2026-11-21; refines #110 and supersedes the snapping in #44). When the package is built, every ground-level road's CS1 vertical profile is burned into the converted heightmap: the road's height across its width, blended out to the natural terrain with side slopes, giving embankments and cuts made of ground like CS1. The importer then places ground roads at their real CS1 heights. Elevated roads, bridges and tunnels don't modify the terrain. Worker-3 leads the design; worker-2 implements it in the web terrain step, mirrored in `terrain/convert.mjs` by worker-4.
112. **The road-height behaviour is configurable per road type** (user, 2026-11-21; extends #111). The web tool's mapping table offers three modes, carried in the plan per prefab and honoured by both the heightmap builder and the importer:

     | Mode | What happens |
     | --- | --- |
     | Ground follows the road (default for every ground-level road) | The terrain is shaped to the road's CS1 profile. |
     | Road follows the ground | The road sits on the natural terrain, and the terrain is left alone. |
     | CS1 height with walls | The road keeps its CS1 height and the terrain is left alone, so CS2 builds retaining walls or quays. |
113. **Default height mode by zoneability** (user, 2026-11-21; refines #112).
     - **Zoneable CS1 road types** (the CS1 road AI enables zoning) default to `conform`: the ground meets the road's height.
     - **Non-zoneable road types** (highways, ramps, roads without zoning) default to `walls`: CS1 height, terrain untouched, so CS2 builds walls, embankments or pillars and a ramp stays a straight slope.
     - The per-type override in the mapping table still applies.
114. **No invented medians** (user report, 2026-11-21). The Road Builder generator turned any lateral gap over 0.6 m between the two directions' lanes into a raised median of at least 1 m, so CS1 roads whose directions are separated only by painted lines got a kerbed strip in CS2. A median is now generated only when the CS1 road has a physical one: median props (trees, barriers, lights) between the directions, or a gap clearly wider than a painted divider. Otherwise the gap becomes flat asphalt or is dropped, and CS2 draws its own centre line. IMT lines are ignored and only IMT fills count; that was already the case, and it stays the default.
115. **Utility links: convert CS1's own off-road power lines and pipes, don't invent links** (Claude, after the QA §9 findings: unlinked power plants produce 0, and pumps and outlets away from roads have no pipe link). Supersedes "utility networks are not converted" (#38 area).
     - CS1 power lines and water/sewage pipes are already in the export.
     - Segments not running along a converted road are mapped to CS2 power lines and pipes (prefabs found in the game). The ones along roads stay dropped, because CS2 roads carry those utilities.
     - Order: power, then pipes, then pumps on dry land. A dry-land pump snaps to water only within a small radius, otherwise it's swapped for a groundwater pump if one exists, otherwise reported.
     - Scheduled after the train and metro station work.
116. **Road-conformed terrain is implemented on the package side** (web 0.7.0 + `terrain/convert.mjs`; Claude; contract in `schema/README.md` §2).
     - Every road segment carries `halfWidth`, and every prefab a `heightMode`.
     - Defaults follow #113: zoneable roads and tracks `conform`, highways `walls`, paths `follow`. They can be changed per type in the Mapping tab and the CLI.
     - Ground segments' CS1 profiles are burned into the heightmap with linear side blending. Water sources keep their level.
     - The web and Node outputs are byte-identical. Skibidi burns 1747 segments, changing the ground by +11/−15 m at most.
     - The importer side (placing ground roads at CS1 heights per mode) is worker-3's next step.
117. **The utilities check exists** (`tools/qa/`, read-only; Claude). On the RC saves:

     | | Electricity | Water | Sewage |
     | --- | --- | --- | --- |
     | Skibidi | 0% | 0% | 0% |
     | Westfield | 0% | 0% | 0% |
     | CacaoCity | 86% | 37% | 74% |

     Causes:
     - no working plants in a 3 km crop;
     - power plants without a high-voltage connection;
     - pumps without power or a pipe link;
     - no electricity or water outside connections.

     Fixes are assigned: utility outside connections in map prep (worker-4), CS1 off-road power lines and pipes (#115, buildings worker), unconnected facilities in the import report (worker-3).
118. **RC2 passed (vanilla roads, `docs/QA.md` §8), but the release is not cut yet** (Claude). Results with no regression against RC1:
     - roads built 100%, and every compared road runs in CS1's direction;
     - lane connections matched 92% / 99% / 95.7%;
     - all buildings placed;
     - fewer Road Builder configs (26 / 32 / 10).

     The user's review of the converted cities since then found four issues worth fixing before a release:
     - road heights (#110–113);
     - Node Controller lane alignment (#107);
     - invented medians (#114);
     - utilities: no power or water service (#115, #117).

     **RC3 runs once those land, with a utilities and heights check added to acceptance.** RC builds come from a clean export of a commit, not the shared working tree.
119. **Web 0.8.0 implements #114 and the plan side of #115** (Claude).
     - **Medians:** a gap between the directions is a median only with props between them (trees, lights, signs) or when it's at least 2 m. Every real median in the three cities is ≥ 3.2 m with props. The 0.75 m prop-less gaps on Two Lane Highway (Twoway) are no longer kerbed. Changed configs: Skibidi 1/26, CacaoCity 5/32, Westfield 0.
     - **Utilities:** `plan.utilities` lists CS1 power lines and pipes that don't run along a converted road (≥ 70% of samples within the road's half-width + 4 m count as "along"). Skibidi has 6 power + 70 pipe segments off-road, CacaoCity 62 + 85, Westfield 64 + 42.
120. **Buildings 0.3.2: station sub-nets fixed** (Claude). Sub-net elements with `m_Upgrades` are composition flags (platform pieces, stairs), not upgrade-only parts. Vanilla builds them with an `Upgraded` component, and now so does the placer. Elevated metro stations get their tracks (SubwayStationElevated02: 6/6, was 0), train and subway stations get their stairs, and 0 sub-nets are missing over 1630 courses. On Skibidi 6 km, 10 of 11 stations are placed. Metro lines now pathfind, but they still run no vehicles, because no yard exists (#83: none is invented when the track never leaves the crop).
121. **With #106, a fresh 6 km Skibidi places 165 of 249 buildings; 84 are "place it yourself"** (Claude, noting the effect of the user's rule). Most of them are CS2 lots larger than CS1's that overlap a road at the CS1 spot. Every one is listed with its CS1 and CS2 position.
122. **Node Controller shifts become off-node road ends in CS2** (Claude, from worker-5's IL research; implements #107 on the plan side, web 0.9.0).
     - CS2 road prefabs have no `StrictNodes`, so an edge can end off its node. That's CS2's equivalent of Node Controller's per-end shift.
     - Shifted segments carry the shifted bezier, computed with Node Controller's own maths, plus `offNode: true` and `ncShift`. Nodes stay at the CS1 centres.
     - Only shifts over 5 cm on nodes that aren't free-form are applied. Node Controller offsets, slopes, twists and deltaHeight stay report-only.
     - 3 km crops: 17 / 39 / 18 shifted ends, up to 10 m.
     - Older importers snap the ends (the old result), so this is backward compatible. Importer support and in-game verification are with worker-5.
123. **`walls` mode keeps CS1 heights even beyond CS2's wall limits** (Claude). The −11.5 … +7.5 m clamp from #24 must not apply in `walls` mode. A segment beyond what CS2 can build as walls or embankments keeps its CS1 height, and CS2 picks the composition (elevated with pillars, or a cut). Found on CacaoCity's worst ramp, 25 m above the terrain, which would otherwise stay 17 m low.
     Height baseline before the conform change, ground roads:

     | | Skibidi | CacaoCity | Westfield |
     | --- | --- | --- | --- |
     | Median error | 0.88 m | 0.26 m | 0 m |
     | p90 | 3.05 m | 0.95 m | 5.0 m |
     | Segments over 2 m | 408 | 44 | 90 |
124. **CS1 tunnels must come out as CS2 tunnels** (user report, 2026-11-21). CS1 tunnels can be shallower than CS2's minimum tunnel depth, so CS2 built open cuttings with walls instead. Tunnel segments are placed at least at CS2's minimum tunnel depth, with the portals kept at their CS1 positions and the slopes adjusted. Every deepened tunnel is reported with its CS1 depth, the CS2 depth used and the difference, and the street above stays at ground level.
125. **Conformed terrain no longer cuts the ground above tunnel approaches** (web 0.9.1 + `terrain/lib`, byte-identical; Claude, for #124). CS2 needs at least 8 m of ground above a road tunnel. Burning the approach had removed it, so the first tunnel segment became an open cut. Now:
     - only `ground` segments burn; slope segments don't;
     - nodes touched by a tunnel or slope segment get no junction disc;
     - the importer builds conform-mode slopes as walls and deepens CS1 tunnels that are too shallow for CS2 (importer side, with worker-3).
126. **Lane connections need an independent geometric check** (user report, 2026-11-21). At a six-arm junction (Myrtle St × Aspen St × Foggy Highway), connection lines visibly cross to the wrong arms, although our "as in CS1" metric (#89) said 92–99%. That metric compares what was written with what was intended, so it can't catch a systematic mapping error.
     - **The check:** every written connection's target arm must match the CS1 target segment's direction at that node. It becomes part of acceptance.
     - **Junctions that can't be mapped confidently** get no written connections and are reported.
     - **The default (on) stays until the check's numbers are in;** if they're bad, it flips to off until fixed.
127. **Node Controller off-node ends stay OFF in the importer until they build reliably** (Claude). With offNode on, a Skibidi crop lost 67 of 1328 roads, because CS2 creates the node at the shifted curve end while the importer looks for it at the centre. `ImportOptions.ApplyOffNode` defaults to off, so ends snap as before web 0.9.0. Worker-5 fixes node creation and lookup, and the default flips only with 100% of roads built in all three cities and the user's ramp and split cases verified.
128. **Utility facilities stay strict by default, with a prominent opt-in** (user, 2026-11-21; refines #106). Power plants and water, sewage and treatment facilities follow the "never move" rule by default. CS2 versions are often much bigger, so on CacaoCity all 7 power plants and both treatment plants became "place it yourself" and the city had 0% power.
     - **Web tool:** when the crop contains utility facilities, a prominent one-click option "Let power, water and sewage facilities move nearby (≤ 400 m) if they don't fit", with the reason.
     - **Import report and the mod's status:** a highlighted warning when no power plant or no water facility was placed.
129. **The CS1 segment kind decides the CS2 composition family** (user report, 2026-11-21; corrects #123). Deep CS1 open trenches had become CS2 tunnels.
     - CS1 `tunnel` → CS2 tunnel (#124).
     - CS1 `ground` (deep trench or high embankment) → never a tunnel or pillars. Beyond CS2's wall limit, the ground beside the road is shaped (lowered beside trenches, raised beside embankments) so the walls fit while the road keeps its CS1 height. If that's impossible, the road is clamped to the wall limit and reported.
     - CS1 `elevated`, `bridge` and `slope` keep their kind.

     Acceptance checks that the tunnel count per city equals CS1's.
130. **Web 0.9.2: the utilities opt-in** (#128, Claude). When the crop has power, water or sewage facilities, Overview shows a prominent notice with "Let them move nearby (≤ 400 m) if they don't fit" and an undo. Only the power, water-pump/tower and sewage families are affected, per-type choices win, and the CLI flag is `--utilities-nearby`. The "What converts" list now counts off-road pipes and power lines as placed by the importer.
131. **Coordinator review no longer uses `git stash` in the shared checkout** (Claude, after an incident). Stashing to test a clean tree briefly removed other workers' uncommitted changes while they kept editing. Everything was restored by a file-by-file three-way merge, with no loss. From now on, reviews run in a separate temporary checkout of a temporary commit built from the index, in the scratch folder.
132. **Walls burn** (web 0.9.3; Claude, for #129). For `walls`-mode ground segments, after the conform burn, the ground beside the road is brought to between 7 m below and 11 m above the road, CS2's retaining-wall range. It uses the core half-width + 2 m with a smoothstep blend, never touches conform cores, skips (and reports) differences over 30 m, and never raises wet pixels above the water. Skibidi: 117 segments, its trenches become open cuts with walls (down to −15.8 m). CacaoCity: 103 segments, and the 25 m ramp gets an embankment (+19.3 m), so it keeps its CS1 height with walls instead of pillars. The `terrain/convert.mjs` mirror follows (worker-4).
133. **The train/metro investigation stops after the current check; utility outside connections take priority** (Claude). After 13 runs, metro lines have paths and an opt-in interior yard spawns trains, but they never leave the yard. One last check (whether the yard link is really built) decides it. If it doesn't unblock them, trains and metros stay a documented known limit for this release, and all rail depot and link features stay opt-in. Worker-4 then builds electricity and water outside connections in map prep: none exist in any converted city today, and that is the biggest lever for power and water (#117).
134. **Importer 0.3.3 checkpoint: road heights verified in game** (Claude). With conformed terrain, ground roads more than 2 m off their CS1 height dropped from 408 / 44 / 90 to 5 / 6 / 0 (Skibidi / CacaoCity / Westfield), with roads and lanes unchanged. CacaoCity's ramp stays within 1 m.
     Also in this commit, built and core-tested (70/70) but not yet verified in game:
     - composition by CS1 kind (#129) and tunnel deepening (#124);
     - segment splitting where CS2 would miss CS1's height by more than 1.5 m;
     - in-game height passes only on the package's own terrain;
     - the Node Controller off-node support, still off by default (#127);
     - the lane-connection view and arm-check groundwork (#126).

     The remaining in-game runs verify these before RC3.
135. **Node Controller shifts verified in game with off-node ends ON** (Claude; implements #107, ends the hold in #127).
     - **Results:** 100% of roads built and 0 node failures in Skibidi / CacaoCity / Westfield; identical lane directions; TM:PE lane references matched 541→553 / 670→670 / 217→224; no arm mismatches.
     - **The user's cases:** the ramp no longer jogs; the highway split's through lanes go from −2.0 m to within +0.6 m.
     - **Why it works:** CS2 moves a node to roughly the mean of its edges' ends, so the importer finds nodes there, or else through the edges ending at the shifted points. Off-node ends survive save/load.
     - **Default:** flips to on once worker-3's real-terrain tunnel package run passes with it on.
     - Node Controller offset, slope and twist stay report-only.
136. **Checkpoint after usage limits stopped workers 3, 4 and 5** (Claude, 2026-11-21 evening). Reviewed in a temp checkout: core 71/71, all four C# projects build, transit and terrain tests pass.
     - **Verified in game:**
       - Tunnels: all 66 CS1 tunnels in the Skibidi tunnel crop come out as CS2 tunnels (25 deepened), and open cuts where tunnels should be went from 21 to 0.
       - The in-game segment split (18 segments).
       - The lane `turn_check` (#126): 0 arm mismatches, and wrong turns only at CacaoCity node 17774 (2 of 167, the 10 m Node Controller merge; open).
     - **Committed, not verified in game:** walls clamping of ground nodes and segment middles; the terrain guard.
     - **Trains and metros: the hunt is closed** (#133). The yard link exists, but the yard's trains never leave it; the open lead is depot dispatch. They stay a known limit, and all rail depot features are opt-in.
     - **Utilities, cause found:** CS2's only electricity outside connection is a high-voltage line marker at the map edge, which Prepare map's clear deletes. Water has none on the base map. The fix (keep or recreate an HV connection plus a transformer, and create a water connection) is the next task.
137. **The repository is public and the web tool is hosted on GitHub Pages** (user, 2026-11-21): https://nicolas-maitre.github.io/cs1-to-cs2/.
     - **Before going public:** all 79 commits were audited for private data (names, emails, local paths, tokens, binaries). The only finding was a local Steam library path (`E:\GamesLibrarySteam\…`) in two old commits of the M0 notes, already removed from the current files. The user chose to keep the history unchanged rather than force-push a rewrite. Commits use the GitHub no-reply email.
     - **Deploy:** `.github/workflows/pages.yml` builds `web/` and deploys it on every push to `main` touching `web/` or `docs/guide/`.
     - **Exporter 1.2.2:** the "Open web tool" button now defaults to the hosted URL.
138. **The driving side can't end up wrong on any path** (user report, 2026-11-21). A CacaoCity test city, a left-hand city, was built right-hand, most likely by a scripted path that skipped the mod's Prepare map (#90).
     - Every path that creates or prepares a city sets CS2's driving side from `plan.source.leftHandTraffic` before the first road: the mod, the e2e script, map prep and the bridge's `game.new`.
     - Import (mod and exec) refuses when the loaded city's driving side differs from the plan's, unless explicitly overridden.
     - Acceptance asserts it.
     - Right-hand CacaoCity test saves are listed so they aren't used as evidence.
139. **Utilities work on CacaoCity: homes 97% powered, 98% water, 98% sewage** (buildings 0.3.3; Claude). The release candidate had 0% water.
     - **Lines:** CS1's off-road power lines and pipes are rebuilt (147/147 segments) as low-voltage lines and combined small pipes at −10 m. Their ends snap to building markers, road nodes or line nodes (45/65 ends), and inner nodes snap where a CS1 line runs through a building, which is what connected the treatment plant.
     - **Pumps:** a dry pump slides toward water within 30 m, else becomes a groundwater pump, else is flagged. Matcher 1.2.0 maps CS1 water towers to the groundwater pump.
     - **Warnings:** the import report warns when no power plant or water facility was placed (#128).
     - **Electricity source:** on this test it came through worker-2's new outside connection, because no plant was placed under #106.
     - **Open:** CS2's water maps read as empty from exec, so the slide-to-water guard can't tell where water is; the simulation's own "not enough surface water" flag is the reliable signal.
140. **Correction to #139 and #135** (Claude, from worker-3's #138 audit).
     - **Wrong-side runs:** the CacaoCity utilities numbers in #139 (`cs1conv-util-cacao*`) and worker-5's CacaoCity Node Controller verify (`cs1conv-nc-verify`) came from cities built right-hand. The e2e path skipped the driving side, although CacaoCity is left-hand, and these are the likely source of the user's screenshot. Both get re-run on correctly-sided cities.
     - **Still valid:** the height, tunnel, walls and lane results in #134–136 came from correctly-sided cities.
     - **The fix (#138), pending a guide update to land:** a `drivingside` op that e2e calls right after `game.new`; Import refuses a mismatch unless overridden; acceptance asserts the side.
141. **Checkpoint: driving side enforced (#138), utility outside connections (code), lane work in progress** (importer 0.3.4; Claude). Reviewed in a temp checkout: core 77/77, mod, importer and terrain build, web 147/147.
     - **#138:** a `drivingside` op; e2e and map prep set the side after `game.new`; Import (mod and exec) refuses a mismatch unless "Import into a city driving on the other side" is on (off by default, in EN/FR, in the guide and FAQ); acceptance asserts the side.
     - **Utility outside connections:** electricity and water-pipe outside-connection markers on imported road nodes (MapPrep `oc.utilities`, run by the mod's Connections step). The buildings worker's test already shows electricity being imported with 0 local production. Worker-2's three-city in-game run is pending.
     - **Node Controller shifts are on by default** (`ApplyOffNode`, #135).
     - **Lane turn-check work (#126) is in progress.**
142. **Lane connections must survive save/reload with 0 Traffic-mod load failures** (Claude, from worker-3's finding). A saved converted city showed a Traffic mod dialog: lane data failed to load at 13 of 70 junctions. That may be what the user saw at the six-arm junction, where the freshly written data checks out (17/17 connections as in CS1, turn_check clean). The acceptance lane check gains a save/reload step that reports load failures and re-runs the turn check. Junctions that can't be written reload-safely get nothing written and are reported. Also verified: the user's trench corridor (#129) is an open cut with walls and no tunnels.
143. **Worker-3's height and tunnel series is complete and verified** (Claude; importer 0.3.4 in 38e6f31).
     - **Node Controller shifts on:** 0 node failures, including on the package that failed before.
     - **Walls burn:** Skibidi has 2035/2035 roads, tunnels equal to CS1's 10, 0 ground roads built as tunnels or elevated, and 6 ground roads over 2 m off. CacaoCity has 684/684 roads, 4 ground roads over 2 m off (was 44), and the user's ramp within 1 m as an embankment. The user's trench corridor is an open cut with no portals.
     - **Split with shifts:** each half keeps its own shift; segment 25425 is within 0.6 m.
     - **Without the walls burn:** a few ground tunnels and elevated pieces remain where CS2 cuts long segments itself. That's documented, and the walls burn is the default.
144. **Utility outside connections sit on stubs, verified in game** (Claude; replaces the first version in 38e6f31).
     - **Why stubs:** CS2's `SubObjectSystem` deletes a road node's non-building sub-objects whenever the node updates (e.g. a building grows next to it), so markers placed directly on road nodes vanished (Westfield kept 1 of 5 after 3 min).
     - **How:** `oc.utilities` builds a 24 m low-voltage line and a 32 m combined pipe outward from each chosen edge road node, and puts the Powerline / Water Outside Connection marker on each stub's far node, which nothing else touches. No high-voltage line or transformer is needed, because the trade link doesn't check voltage.
     - **Results:** markers survive 3 min of simulation and a save/reload. Homes supplied with electricity / water / sewage: Skibidi 99.2 / 98.6 / 98.6% (was 0), Westfield the same, CacaoCity 100 / 99.4 / 99.4%. Electricity is imported even with 0 local production.
     - **Still to test:** a fresh Prepare map → Import through the mod, and "Remove imported" deleting the stubs. RC3 covers both.
145. **Full-city stress run: works at scale, with one regression to fix before RC3** (Claude; `docs/QA.md` §10).
     - **Results:** Skibidi 14 km through the mod flow imports in 70 s with no crash and worst stalls ≤ 3 s. Roads 4738/4740, CS1's 101 tunnels, ground roads median 0.06 m off, zoning 94.8%, transit 20/20 lines, outside connections 10/10, electricity 96.7% and water 99.1% after 5 min.
     - **Regression:** the utilities step's line-end snapping can put a power-line or pipe node exactly on a road node and take over a road edge, splitting the junction and cutting traffic. That leaves 21 roads unresolvable and part of the lane data stale (#142). **Utility lines must never create or take over road nodes or edges**; the buildings worker fixes it, with a guard that fails the step if road topology changes.
     - **Also open:** 10 power facilities aren't connected to the HV grid (buildings worker), and lane data after reload is being fixed (worker-5).
146. **The utility outside-connection stubs (#144) also split road junctions, so they get fixed, not disabled** (Claude). A stub starting at a road node's position with no entity reference creates a second node, and a road edge moves onto it. Worker-2's `oc.stubcheck` measured 7–8 of every 8–10 stubs splitting a junction on the test cities. Both the stubs and the utilities step's line ends (#145) switch to the buildings worker's topology-safe joint (an attach, setback or no-merge variant, whichever leaves road topology untouched). `oc.stubcheck` and a road-topology guard become permanent acceptance checks. No mitigation ships, because the mods aren't published and RC3 waits for the fix.
147. **Lane data lost on reload is a road-layout problem, fixed at the road** (Claude). Worker-5 measured the Skibidi acceptance run: all 70 junctions held valid lane data before saving, and 13 lost it after save and reload. The cause is that 19 of 2020 Road Builder segments come back with more car lanes (e.g. a BBFF road reloads as BBBFFF), so the stored lane indices no longer match. The road itself is then wrong after a reload, so the fix goes into road generation (worker-3), along with a check that each edge's lane layout is the same before saving and after reloading. As a fallback, the lane step doesn't write a junction on a road whose layout is known to change, and it logs those junctions; the reload check reports them by name. The repair pass (#126) stays, but it can't fix this, because the data is valid when written.
148. **Corrections to #145–#147: the "junction splits" were a misreading, and the reload change comes from bus stops** (Claude, from worker-3's and the buildings worker's measurements).
    - Where a CS1 power line or pipe ends on a road node, CS2 gives the line its own node at the same position. That node lists the road edge as a local connection, which is how a utility line joins a road. Read by real edge endpoints (`Edge.m_Start`/`m_End`), the road junction is intact, and no stub or line-end variant split one. Our own tools counted local connections as edges: the importer's `GameWorld.EdgesOf` and node lookup, and with them the "21 unresolvable segments" and `oc.stubcheck`'s count. The resolver is fixed (importer #64, cc20686).
    - The 3 m setback with no merge for stub starts and line ends stays the default, and so does the road guard. They cost nothing and keep the utility node away from the junction.
    - Roads changing on reload (#147) aren't a Road Builder config problem. The 19 segments carry bus stops from the transit step. The stops were placed without marking the road edge updated, so CS2 picked the stop composition (a bus bay, which shifts lane indices) only on the next load. Fix: the transit step marks the stops' edges and nodes updated after placing them (`stops.touch`, worker-4), in the e2e path and in the mod. The acceptance step `roads-reload` fails on any segment that differs after a save and reload. The lane-step fallback from #147 is then only a safety net.
149. **Lane connections survive a save and reload; #126 is closed** (Claude, worker-5's acceptance run on Skibidi, `20261121-211024`). 70 of 70 junctions keep their lane data after a reload (it was 57), the Traffic mod's loader drops none, the turn check passes 117/117 before and after, and 685 of 685 lane refs match. The causes were all on our side:
    - arrow lanes were mapped through CS2's turn flags (now by CS1 geometry);
    - bus stops changed road compositions only on the next load (#148);
    - indices shifted by a bus bay still pass the mod's loader check, so the final `lanesrepair` step now compares each junction's stored lanes with a fresh mapping and rewrites any that differ;
    - bay lanes are never matched to CS1 lanes.
    The fallback from #147 (leaving junctions on unstable roads unwritten) wasn't needed once the cause was fixed. The reload step would still list any such junction by road. Still open, with worker-3: 5 roads lose their traffic `Upgraded` flags (crosswalk removed, left or right turn forbidden) on reload.
150. **Power plants join the grid through `powerhook`. Low-voltage links are on by default, and transformer stations are opt-in** (Claude, from the buildings worker's #44).
    - In CS1, power flows between touching buildings. In CS2, a placed plant or turbine needs a line at its marker. Low-voltage producers (wind turbines) get a low-voltage line to the nearest non-highway road node, stopping 3 m short of it (#148). That is the connection CS1 made implicitly (#115), so it's on by default.
    - High-voltage plants (coal, gas, incineration) feed the city's grid only through a transformer, a CS2 building with no CS1 counterpart. Following the user's rule that the importer doesn't invent buildings (#83, #106), transformer placement and the HV line to it are an opt-in setting. When it's off, the report names each plant that needs one, and the city keeps power through its outside connection (homes ~99 % supplied).
    - Producers that stand away from any road (8 on the full city) produce nothing in CS2, which needs the building's own road connection. The report names them, and no road is invented.
    - `utilities` and `powerhook` carry a road guard: every road edge's endpoints are compared before and after, and any change fails the step.
151. **RC3 runs on b71a160** (Claude). Everything found since RC2 is in:
    - heights: the ground meets zoneable roads, other roads use per-type modes;
    - tunnels in the right places, Node Controller shifts, no invented medians;
    - the driving side is enforced;
    - lane connections and arrows are right and survive a reload (#149);
    - roads are unchanged after a reload (bus stops #148, settle step);
    - utility lines and stubs never touch a junction node (#148);
    - power plants are linked, with transformers opt-in (#150).
    The acceptance run now also fails on a missing import step in the report, on missing heights or tunnels, on a stub splitting a junction, and on any road or lane-connection change across a save and reload. RC3 builds from `git archive` of that commit and runs the real mod flow on Skibidi and CacaoCity with 5 minutes of simulation. Results go in docs/QA.md §11. Publishing stays the user's call.
152. **RC3 passed. A depot type with none placed gets the default depots' fallback** (Claude, from RC3 §11).
    - RC3 on b71a160 passed every step on Skibidi, CacaoCity (left-hand) and Westfield, all from a clean build through the real mod flow. Roads and lane connections were unchanged after a save and reload, the report was complete, no stub split a junction, and 99–100 % of homes were supplied. Matcher 1.2.1 (CS1's Water Tower → groundwater pump) and the transit library build in acceptance were fixed after RC3 (5d0c47c).
    - Skibidi's only Bus Depot overlaps a road at its CS1 spot, so it isn't placed (#106), and its 9 bus lines got 4 buses instead of RC2's 34. Bus and tram default depots are already added automatically, with a `nearby` fallback, when the CS1 city has none (the user's open call on that default). A city whose CS1 depots of a type were all not placed is in the same situation, so the same rule applies. For a bus or tram depot type with 0 placed, the CS1 depot is placed with the `nearby` fallback, and the report says it was placed away from its CS1 spot because its lines need a depot. It goes behind the same opt-out as the default depots.
    - Other buildings and the rail and subway yards keep #106 and #83: never moved, listed as "place it yourself".
153. **Versions for the next release** (Claude, from worker-4's proposal). The importer goes to 0.4.0 and buildings to 0.4.0: new steps (utility outside connections, power hookup), a new setting (transformers) and save/reload stability. The web tool becomes 0.9.4 if worker-2's guide pass changes anything user-visible, otherwise it stays 0.9.3. The exporter stays 1.2.2. The product goes to 0.3.1: 0.3.0 was never published, but RC artefacts were built under it, and a new number keeps them apart. The bump happens once #152's depot fallback lands and a Skibidi acceptance run on the bumped tree is green. Publishing stays the user's call.
154. **"Made with AI" disclaimer and the MIT licence** (user). The user asked for a prominent disclaimer, after a check that neither Paradox nor Valve forbids AI-assisted mods or requires disclosure (both require that the uploader created the content and holds the rights to it). The disclaimer comes first on the Steam Workshop page, the Paradox Mods page (long and short description), the web tool's landing page (EN/FR) and the README: the code was written with AI coding assistance (Anthropic's Claude) and tested in game by its author (45deb11). The repository is MIT-licensed (1cc16ee): the usual licence for Cities: Skylines mods and tools, permissive, and compatible with any MIT snippets the third-party audit may confirm. The copyright line reads "the cs1-to-cs2 authors" rather than a personal name. A full audit for copied or third-party code is running before publishing.
155. **Third-party code audit before publishing** (Claude, at the user's request). Every tracked file and the full history were checked, along with the built DLLs and the web bundle:
    - **Nothing copied ships.** No vendored binaries, no NuGet packages, no Harmony and no decompiled game or mod code; only reflection and our own code. The few facts taken from IL (a lane-flag mask, Node Controller's shift formula, CS2 recipes) are constants and formulas, not code. `MiniJson.cs` only shares the name with the public MiniJSON. The zip, CRC, PNG and SHA-1 code follows the specs in our own form. The web bundle's npm dependencies (React, react-dom, scheduler, fflate; all MIT) are listed in THIRD-PARTY-NOTICES.
    - **Fixed (c5b0be2):**
      - a 15-line quote of the Traffic mod (a repository with no licence) in an M0 research note is now a description;
      - the PNPOLY-shaped point-in-polygon loop is rewritten as our own even-odd test, with identical results on 400 000 random cases;
      - Vite's modulepreload polyfill is off, so no bundler code ships;
      - both mod descriptions say MIT and "not affiliated";
      - the README states that no game or mod code and no game assets are included.
    - **Left as is:** the Traffic quote remains in old commits (64f34fe and later). Removing it would need a history rewrite, and the user chose to keep history (#137). It is 15 lines of research notes, not shipped. The CS2 prefab data in the web tool (names, sizes, lane layouts) is factual interop data.
156. **Node Controller offsets get a real attempt before publishing** (user, 2026-11-22). The user showed two imported highway splits where the diverging lane curve inside the junction overshoots sideways across the through lanes. In CS1 they were smooth tapers. NC offsets (which lengthen a junction) and stretch were report-only (DECISIONS-nodecontroller #5), because CS2 decides junction length itself. Worker-5 now:
    - compares CS1 and CS2 at these splits;
    - tests pulling each segment end back by the NC offset with the existing off-node mechanism (and checks whether the shifts make small-angle splits worse);
    - measures the sideways overshoot, node failures, reload stability and lane connections.
    If it works without breaking anything, it becomes the default before publishing.
157. **NC offsets are applied by default at junctions (3+ arms)** (Claude, from worker-5's test, decision 156).
    - **Finding:** players stretch exactly the small-angle splits and merges. Every one of Skibidi's 28 in the 3 km crop carries a large NC offset (e.g. node 3071: 36/62/63 m against NC's 8 m default). CS2's default short junction then makes the diverging lane curve overshoot across the through lanes.
    - **Method:** pulling each such end back along its own CS1 curve by the offset, with the existing off-node mechanism, gives CS2 a long junction and a smooth taper. Shown on Skibidi 3071, 11237 and 22534, Cacao 30538 and Westfield 5867, with CS1 screenshots for reference. Every check stays clean on all three cities: roads 2035/684/648, turn checks 117/167/83, 0 loader failures, 0 node failures, lane data kept after a reload.
    - **Rule:** at nodes with 3+ arms, an end whose NC offset exceeds NC's default for its node type by more than 5 m is pulled back by the offset, capped at 45 % of the segment. Two-road nodes are excluded: there it made S-wiggles at lane drops.
    - **Not caused by the shift:** shift alone isn't the cause, and shift plus offset is best. Slope and twist stay report-only.
    - **Side effect:** a wider paved gore, closer to CS1's look.
    - **Implementation:** in the web plan's NC alignment (shortened bezier, `offNode`, `ncOffset` for traceability). The importer needs nothing new. One more release acceptance with the reload check follows.
158. **Release 0.3.1 is ready to publish; publishing stays the user's call** (Claude). The final release acceptance on 693ba8a (docs/QA.md §13) passed every step on Skibidi, CacaoCity (left-hand) and Westfield, from a clean archive through the real mod flow.
    - **Versions:** product 0.3.1, with exporter 1.2.2, importer 0.4.0, buildings 0.4.0 and web 0.9.5.
    - **Roads:** 0 differences after a reload, lane connections survive with 0 loader failures, and turns are all correct.
    - **Other checks:** heights and tunnels as planned, the report complete, 0 stub splits, and 99–100 % of homes supplied.
    - **Contents:** every zip carries the MIT licence, and the store texts carry the AI disclaimer and "not affiliated".
    - **Known limits** (in the store texts and changelogs): trains and metros don't run, buildings overlapping a road aren't placed, transformers are opt-in, and off-road power producers produce nothing.
    - **Before publishing** (docs/RELEASE.md): tag the commit, upload the CS1 exporter to the Steam Workshop and the CS2 importer to Paradox Mods, and fill in the published links in web/src/ui/links.ts. Nothing has been uploaded.
159. **The train/metro investigation resumes, time-boxed, after release 0.3.1 is ready** (Claude). The release is complete (#158), and imported train and metro lines running no vehicles is its biggest known limit (#99, #133). One clue is new: the final Skibidi run had 24 train vehicles entering from the rail outside connections. CS2's own rail traffic runs on the imported tracks, so the track network itself works. Worker-4 resumes with a fresh angle: compare an imported line, component by component, with a line the game builds itself on the same imported stations and tracks (route waypoints, segments and path elements, stop connections, the depot's route assignment). The work is capped at about 10 game runs. It stays off the release: anything found lands after 0.3.1 as opt-in until a full acceptance passes.
160. **Trains and metros: the cause is the track joint, so one more bounded round** (Claude, from worker-4's #159 round 2).
    - **Finding:** our lines aren't the problem. They go through the game's route pipeline and pathfind wherever the track is lane-connected. The yard's track lanes reach only each other: our link meets the yard (and some stations) at a joint with no node lanes, and both sides keep their dead-end lane flags. One working station joint has CS2's own switch lanes. A new diagnostic, `lanes.reach`, was validated against CS2's own line paths.
    - **Next:** a third round, capped at about 8 game runs:
      1. diff the working station joint against a failing one;
      2. build the link with CS2's own net tool flow (as a player would) and compare the joint;
      3. check whether lane generation skips node lanes at a building-owned node joined by a foreign edge.
    - Still opt-in, and outside release 0.3.1 (#158).
161. **Trains run: tangent track links; next, bring rail linking into the mod for the next release** (Claude, from worker-4's #160 round 3).
    - **Cause and fix:** CS2 builds switch lanes only where tracks meet at a shallow angle. Our links left yards and some platforms at 73–90°. With `tangentLinks` (a link leaves along its own track and meets the line along it), Skibidi's rail yard reaches the network, and Train Line 1 gets a train that runs and boards at its station (344ae1b, opt-in).
    - **Mod flow:** the mod's transit step doesn't link station platforms or place crop-edge yards at all today; that only exists in `transit.mjs` (#99). So the next release brings it into the mod: crop-edge rail and subway yards (#83, never invented inside the city), tangent station and yard links, and a line repath. It's on by default, with a setting to turn it off, and needs a mod-flow acceptance on a crop where a train line leaves the area.
    - **Release 0.3.1 (#158) stays as tested.** This goes into the next version. Metro is still blocked where an interior yard faces the line at a right angle; interior yards stay opt-in.
162. **Rail in the mod ships opt-in; the transit line-name bug is fixed for 0.3.1** (Claude, from worker-4's mod-flow acceptance runs on Skibidi 6 km).
    - **What was built:** the mod's transit step can now place crop-edge rail and subway yards and link stations and yards with tangent links (setting `ConnectRail`).
    - **Why opt-in:** with it on, acceptance isn't green yet. 4 imported roads near elevated metro stations go missing. The yard's join to the line either crosses roads (which splits them) or is blocked. Train Line 1 got no path in the mod flow. With it off, everything is green.
    - **Bug found on the way:** mod-imported transit lines never got their CS1 names (`named: 0` since RC3). `lines.create` returned a typed list that the in-process reader saw as empty. Fixed: all 20 lines are named in the baseline run.
    - **Release 0.3.1 (unpublished):** it takes the name fix and the opt-in setting. The final acceptance is repeated on that commit.
    - **Next:** one more bounded round makes rail road-safe: no imported road removed or split, elevated or tunnel joins over roads, level crossings only where CS1 had one, and Train Line 1's path in the mod flow.
163. **Release 0.3.1 final: af82ed1** (Claude). The final acceptance passed on Skibidi, CacaoCity and Westfield (docs/QA.md §14), matching §13 apart from the transit line-name fix (9 of 9 Skibidi lines named). Rail stays opt-in (#162). Two process fixes follow:
    - **Locks:** scripts must wait for a free `cs2.lock` (`waitAndTakeLock`) and never take a held one. Worker-4's rail scripts took it twice during the acceptance.
    - **The user's mod Options:** acceptance `--mod-set` and the rail scripts restore every Option they change to its previous value, in `finally`. The old restore flipped booleans, so a crashed run could leave the user's settings changed (it left `ConnectRail = true`, now being reset).
    The per-line vehicle count in acceptance (0 per line, while 33 buses run) is a counting bug in the new check, not in the import.
164. **Release 0.3.1 is on hold: imported bus lines get no buses** (Claude, from worker-4's bus check on the final Skibidi acceptance save).
    - **Finding:** all 25 Bus01 are parked in the depot. Every imported bus line has `RequireVehicles, NotEnoughVehicles`, no vehicle, and passengers waiting. Every line's vehicle request has failed 9 times and was never dispatched. The moving buses were CS2's through traffic.
    - **Correction:** the "31–34 buses on 9 routes" in QA §11–§14, the changelogs and #152 counted parked buses (a by-prefab total). The new per-line count (0 per line) was right.
    - **What's known:** the depot is on the same road graph as all 145 stops, so the failure is in the directed path (traffic rules, one-way access, or the depot's lot side after the #152 move) or in the line setup.
    - **Next:** worker-4 bisects with directed lane reach and the traffic steps turned off (up to ~6 runs).
    - **Rule for the release:** it waits until bus lines run, or until the store texts and changelogs stop claiming they do. Acceptance must fail when an imported bus line has no vehicle.
165. **Bus dispatch: the network and traffic rules are ruled out. The release texts are corrected now, and the hunt goes on** (Claude, from worker-4's ~6 runs).
    - **It never worked:** imported bus lines have never got vehicles in the mod flow; RC2's save shows the same.
    - **Ruled out:**
      - the depot's lanes reach every line's first waypoint (directed lane reach);
      - turning off lane connections, traffic settings and the Traffic mod changes nothing;
      - lane sizes and the vehicle model are fine;
      - a fresh test line fails too, and reset requests fail again.
    - **Next leads:** the depot as our placer builds it (spawn locations and their connected lanes), compared with a depot the game places itself in the same save, then a vanilla test city.
    - **Meanwhile:** worker-3 corrects every release text (changelogs, Paradox Mods change log and known limits, RELEASE.md, a QA correction note, the guide) so nothing claims bus lines run. The acceptance run fails when an imported bus or tram line has no vehicle (buildings worker).
166. **Bus dispatch cause: buildings placed with Permanent definitions aren't joined to the street; the placer switches to the game's own apply** (Claude, from worker-4's lead b).
    - **Cause:** our placer (#41) creates buildings with `CreationFlags.Permanent` definitions, which skip the tool's apply phase. A depot built that way connects its spawn locations only to its own internal path lanes, not to the street's lanes, so every dispatch request fails.
    - **Proof:** rebuilding the same BusDepot01 at the same spot through a Temp definition plus the forced apply frame (as the object tool does) put 25 buses on 6 of the 8 imported lines within 150 s. Before that it was 0 on every line, in every save since RC2.
    - **Fix at the source:** the buildings placer places service buildings (at least depots and yards, ideally all) through Temp definitions plus the apply frame, in batches. It must keep the guarantees of #106 (never moved), #152 (depot fallback), sub-nets, power hookup, the road guard and reload stability. A full acceptance on the three cities follows, with the new per-line bus check (fail on an imported bus or tram line without a moving vehicle).
    - **Separate issue:** the 2 lines still without buses after the rebuild (Skibidi Bus Line 14 and 2) have gaps in their stop-to-stop reach; worker-4 looks at them.
    - **Rail too:** yard and station dispatch may share this root, so rail is rechecked after the fix.
167. **The last two idle bus lines: a one-way stop trapped at the crop edge, and a utility stub blocking a turnaround** (Claude, from worker-2 and worker-4).
    - **Bus Line 14:** a stop on a one-way road that only leads to the crop edge can't be served. Web 0.9.6 drops such stops (and their mirror, stops only reachable from the edge) and names them in the report. Stops on two-way dead ends are kept, because CS2 turns buses round there (Lines 3, 15 and 16 run).
    - **Bus Line 2:** the utility outside-connection stub (#144) started 2 m from the avenue's dead end, so CS2 built no turnaround. Removing it in game put 2 buses on Line 2.
    - **Fix:** `oc.utilities` never picks a dead-end road node or a node on a stub leading only to the crop edge, prefers the nearest node with 2 or more road edges, and stays clear of bus and tram stop ends (worker-2).
    - **Then:** one acceptance on the three cities with the placer fix (#166) and the per-line bus check.
168. **Imported bus lines run** (Claude; 2680feb).
    - **Change:** the buildings placer now builds through Temp definitions plus the tool's Apply, in batches (buildings #47). It writes only visible sub-nets, protects imported two-edge road nodes from CS2's node reduction during the apply, and carries a road guard.
    - **Result:** on Skibidi, 6 of 8 imported bus lines run 23 buses; before this they had none, ever. The other 2 wait for capacity, because the CS1 city's one bus depot has 24 buses, all out. Acceptance passed on all three cities with 0 roads lost, 0 reload differences and lane reload intact.
    - **Acceptance rule:** acceptance now counts moving vehicles per imported line. It fails an imported bus or tram line without a vehicle, unless every depot of that kind is out of vehicles.
    - **Next:** the release texts are reworded to "bus and tram lines run (depot capacity permitting)", `oc.stubcheck` is brought in line with the new stub picks, then one final acceptance from a clean archive decides release 0.3.1.
169. **Release 0.3.1 is ready again, on 66558c8** (Claude). The final acceptance from a clean archive passed every gate on Skibidi, CacaoCity and Westfield (docs/QA.md §15):
    - **Buses:** Skibidi's imported bus lines run 24 buses on 6 of 8 lines. The other 2 are capacity-limited by the city's single depot.
    - **Stubs:** utility stubs all ok, 0 split.
    - **Reload:** 0 road differences and 0 lane loader failures.
    - **Other gates:** heights 0 missing, the report complete, transit lines named, the licence in every zip, and 98–100 % of homes supplied.
    - **Versions:** product 0.3.1, with exporter 1.2.2, importer 0.4.0, buildings 0.4.0 and web 0.9.6.
    - **Known limits:**
      - trains and metros don't run in the mod flow (rail linking is opt-in, #162);
      - buildings that overlap a road aren't placed;
      - transformers are opt-in;
      - off-road power producers produce nothing;
      - depot capacity can leave lines short of vehicles.
    This supersedes #158 and #163. Publishing stays the user's call (docs/RELEASE.md "Before you publish").
