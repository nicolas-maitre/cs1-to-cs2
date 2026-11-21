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
