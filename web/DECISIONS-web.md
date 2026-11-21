# Web tool decisions (M1)

Taken by Claude (web worker) on 2026-11-21 while the user was away. Review them.

1. **One TypeScript core for the UI and the CLI** (`src/core/`). It has no DOM and no Node APIs; SHA-1 is implemented in-house and SHA-256 uses Web Crypto.
   - The code is written for Node's type stripping: no enums, namespaces or parameter properties, and imports use `.ts` paths.
   - The CLI runs with `node web/cli/cs1conv.ts`, with no build step.
   - Why: the CLI and the UI must produce byte-identical packages.

2. **The spike generator is ported verbatim.**
   - `rb.ts` produces the same RB config (deep-equal JSON) as `wq-to-rb.mjs` for all 40 non-highway road NetInfos of the M0 dump with drive lanes, and for every `--out-imt` variant. A test runs the spike script and compares when the dump is on disk.
   - IDs are unchanged (`r<sha1("cs1conv:"+name[+"|"+sig])>-cs1conv`), so re-runs overwrite the M0 configs instead of duplicating them.
   - The IMT spike test is ported to vitest as is.

3. **Highways get their own RB treatment** (new; the spike gave them sidewalks).
   - Applies to `m_highwayRules` roads without pedestrian lanes.
   - They get `Category: "Highway"`, 2 m asphalt shoulders (`ShoulderGroupPrefab`, Asphalt 2m/1m), the addons `HasUndergroundElectricityCable, RequiresUpgradeForElectricity` (+ traffic lights if CS1 has them) and no zoning.
   - These match the user's own RB highway files (NOTES-roadbuilder.md §1.3, §7).
   - Width loss is reported. CS1 highway lanes plus margins are wider than RB's, e.g. Highway 22 → 16 m.

4. **Mapping by category of the ground NetInfo.** The ground NetInfo is the `parent` for variants; elevated, bridge, slope and tunnel segments use their ground road's RB config, because RB generates those forms itself.

   | CS1 | CS2 |
   |---|---|
   | Road/Highway AI (service Road) | generated RB RoadConfig (one per NetInfo × IMT override set) |
   | Train track | `Double Train Track` (≥ 2 rail lanes) / `Twoway Train Track` (1 lane, both ways) / `Oneway Train Track` |
   | Metro track | `Double` / `Twoway` / `Oneway Subway Track` |
   | Tram track | `Double Tram Track` / `Oneway Tram Track` |
   | Pedestrian path | `Pavement Path` |
   | Bike-only path | `TwoWayBikePath` / `OneWayBikePath` |
   | Pipes, power lines, dams, transport lines, ship/plane/cable-car paths, building connections, monorail | skipped as "not placeable", reported (info) |
   | Anything else | skipped as "unmapped", reported (warning) |

   - **Vanilla names.** They come from the CS2 prefab-name list in `Cities2_Data\Content\Game\Locale.cok` (`Assets.NAME[<prefab>]` keys). The M0 prefab log had been overwritten.
   - **What's confirmed.** "Small Road", "Medium Road", "Pedestrian Street" and "Double Train Track" were confirmed in M0 / RB source. The others are unverified in game; if the importer can't find one, it should report it.
   - **Tracks stay vanilla.** RB TrackConfigs would also work for tracks. Vanilla is simpler and exact for the common layouts.

5. **Building sub-networks are skipped.** These are segments with the CS1 `Untouchable` flag: station tracks, park paths, dam roads and harbour roads inside assets. They belong to their buildings, which come in M3. Placing them alone would leave stubs.

6. **Inverted CS1 segments are reversed in the plan.**
   - CS1 mirrors the lanes of a segment with the Invert flag (NOTES-roadbuilder.md §9).
   - So the plan swaps `start`/`end`, reverses the bezier and swaps the elevations, and marks the segment `"reversed": true`.
   - The importer then builds every course start → end and gets the right lane order and one-way direction.
   - This was the only way to keep the importer free of mapping decisions (schema: "the plan is already in CS2 coordinates").

7. **Segment kind and elevation.**
   - `kind` comes from the exporter's `elevationKind` (+ `Slope` in the NetInfo name; `underground` → `tunnel`). A `bridge`-kind segment whose NetInfo name has no "Bridge" becomes `elevated`.
   - `elevationStart`/`End` = CS1 node `heightAboveTerrain`, rounded to 0.01 m:
     - 0 for ground segments;
     - ≥ 0 for elevated and bridge;
     - ≤ 0 for tunnels;
     - |v| < 5 cm becomes 0.

8. **Crop.**
   - A segment is included only when both its nodes are inside the crop. Crossing segments are counted and drawn in the UI.
   - The maximum size is 14 336 m (the CS2 playable area) and the minimum 100 m.
   - The CS2 transform is `dx = −centerX`, `dz = −centerZ`; JSON never gets `-0`.

9. **Auto crop = most road segments, not most nodes.** Node density is skewed by pipes and power lines: the densest 3 km of nodes is downtown (−125, −125) with 748 placeable segments. The road-densest 3 km is (3750, −1250) with 1949 road segments. The shared sample package uses the latter. Both have IMT bus variants.

10. **IMT handling** follows docs/DECISIONS.md #3, #7 and #8.
    - Whole-lane red, pavement or grass fills on a segment's own marking become RB variants.
    - Every override signature becomes its own config.
    - Overrides that don't match a lane of the ground NetInfo are dropped with a warning.
    - Partial fills, grey paint and node fills are reported only.

11. **Package report.**
    - Per-segment detail only where it's useful: IMT items, and up to 20 sample IDs per skipped NetInfo.
    - Aggregate items for outside-crop / crossing-crop-edge / not-placeable.
    - One item per used RB config with its width change and lane approximations.
    - The report adds `count` and `cs1` fields, plus `format: "cs1conv-report"`. These are additive to the schema.

12. **The plan adds fields** (additive to schema v1):
    - `generator`, `bounds` (CS2 crop bounds);
    - `source.kind` (`cs1x` | `m0-dump`);
    - `prefabs[*].imt`;
    - `segments[*].reversed` and `segments[*].cs1Info`;
    - `stats.{nodes, outsideCrop, crossingCropEdge, unmapped, rbConfigs, byPrefabType}`.

    Coordinates are rounded to mm.

13. **`--install-rb`** replaces our own configs. It writes the package's `*-cs1conv.json` files (refusing any file whose `ID` doesn't match its name). By default it also deletes other `*-cs1conv.json` files in the RB folder, so stale variants don't pile up. Nothing else there is touched. RB re-adds configs embedded in saves anyway (NOTES-roadbuilder.md §1.5).

14. **Dev-only `/samples/` route** in `vite.config.ts`. It serves `%LOCALAPPDATA%\cs1-to-cs2\samples\*.cs1x|json`, so `?open=/samples/skibidi.cs1x` works for scripted UI checks; browser automation can't drop files. It isn't present in the production build.

15. **UI state** (overrides, and the crop per file hash) lives in `localStorage` as a convenience only. The package is always rebuilt from the file.

## Open issues

- `ShoulderGroupPrefab` Asphalt 2m in a generated highway has not been checked in game. (The importer confirmed that all 26 prefabs of the M1 Skibidi plan exist, including the vanilla track and path names.)
- Highway width loss: CS1 highways are 3–6 m wider than the RB result. An `Empty` filler or a wider shoulder could pad them; decide when looking at it in CS2.
- RB's 7 m sidewalk limit, which narrows wide WQ roads, is unchanged from M0 (reported as `width-loss`).
- Bike lanes inside a CS1 road become sidewalk-with-bike (`Parking:"B"`) only at the edge. Elsewhere they are mapped to car lanes with an `UNSUPPORTED` note, as in the spike.
- The M0 dump has no `parent` for elevated-only workshop roads. There the variant stands in, as in the spike. The `.cs1x` has parents.

## M2 (2026-11-21, overnight)

Also taken by Claude while the user was away. Review them.

### Release-grade web app

16. **Landing page and error states.**
    - The empty state explains the tool, the six steps (install the exporter → export → open → crop and map → install the package → import in CS2), the mods with links, and the limits.
    - The links come from `src/ui/links.ts`. Road Builder (Paradox Mods 87190) and Traffic (80095) use the ids of the installed mods. The exporter and importer show "coming soon" until they are published; nothing is linked to a guessed URL.
    - Unusable files raise a typed `ReadError` with a message and a hint: empty file, a CS1 `.crp` save, not a zip or JSON, a corrupt zip, no manifest, a format version too old or too new (v > 1 is refused, because the version only bumps on breaking changes).
    - Failed sections are not fatal. They show as an alert above the stats, in the file check and in the conversion summary ("the export of this section failed").

17. **Large cities.**
    - Package building runs in a Web Worker (latest request wins), with a main-thread fallback when workers are unavailable.
    - The map has two canvases: base (terrain, network, zones, transit) and overlay (crop square, mask, crop-edge segments). Dragging the crop redraws only the overlay.
    - Network paths are split into 2 km tiles for viewport culling.
    - If a full base redraw takes more than 14 ms, pan and zoom move the last image with a CSS transform and redraw when the view settles.
    - Measured at 1400×860, including rasterisation:
      - base redraw: 4–7 ms on Skibidi and on a synthetic 180 × 180 grid (64k segments);
      - overlay: 3–7 ms;
      - building the 46,726-segment package: 235 ms, off the main thread.

18. **Keyboard and accessibility.**
    - The map is focusable (`role="application"`, described key help).
      - Arrows move the crop (Shift = 500 m) and +/− resize it.
      - Ctrl+arrows pan, `[` `]` zoom, F fits the city, C zooms to the crop.
      - Key events update the crop immediately, so fast key repeat doesn't lose steps.
    - Tabs follow the ARIA tab pattern (roving tabindex, arrows, Home/End).
    - There is a skip link and visible focus rings.
    - Labels: the selects are labelled per NetInfo, and the stats are announced through a polite live region.
    - The file button is a real button, not a hidden-input label, so the keyboard can reach it.
    - Touch: pinch zoom and larger crop handles.
    - Phone layout below 820 px: map on top, panels below, no horizontal scroll (checked at 375 px).

19. **The package zip has a `README.txt`** (CRLF, for Notepad) with what each file is, the Road Builder folder, a PowerShell copy command, the CS2 mods needed (`plan.requires`), the per-category conversion summary and the top warnings. The CLI writes it into package folders too. The "Road Builder configs only" zip has a short README of its own. The Overview tab has the same install steps, with copy buttons for the folder path and an `Expand-Archive` command.

20. **Static site.**
    - `base: './'`, so `dist/` works from any folder or sub-path. The worker is emitted as its own asset.
    - Checked with `vite preview`: the worker loads, and Skibidi builds in 58 ms.
    - Dev-only conveniences, all absent from the build:
      - the `/samples/` route;
      - the `/__shot` endpoint, which saves a canvas snapshot to `test/.out/` for checks while the browser pane is hidden;
      - `window.__cs1convMap` timing hooks.
    - `?synthetic=<n>` stays in the build: it generates a test grid in the browser and touches nothing.

### M2 plan additions (documented in schema/README.md §2)

21. **The plan stays `formatVersion: 1`.** Every M2 field is an optional top-level section (`zoning`, `water`, `transit`, `traffic`, `requires`) or an optional field on an existing object, so an M1 importer still builds the roads.

22. **Zoning: cell centres, not blocks.**
    - CS2 makes its own zone blocks along roads.
    - The plan lists the CS2-coordinate centre of every zoned CS1 cell, plus an index into the CS2 zone prefab names, and the importer paints the CS2 cell under each point. This keeps the importer free of CS1 block geometry.
    - Only cells whose block's road is in the plan are listed.
    - The cell-centre formula from §1 was checked against Skibidi: the median distance from a cell to its own road is 12 / 20 / 28 / 36 m for columns 0–3.

23. **Zone mapping.**
    - Theme: NA by default, EU in the UI and CLI. The CS1 export has no environment/theme field, and vanilla CS1 growables look North American.
    - High-density residential maps to "Residential High" by default (option: Medium, Mixed), and office to "Office High" (option: Low).
    - Industrial maps to "Industrial Manufacturing". Specialised industry, self-sufficient, leisure, tourism and IT are district policies, not in the export, and are reported.
    - All prefab names are from the CS2 prefab list (Locale.cok).

24. **TM:PE speed limits become Road Builder variants.**
    - CS2 has one speed per road prefab, so a segment's most common custom lane speed (else its NetInfo's custom default) becomes an RB variant with that `SpeedLimit`, when it differs from the road's own speed by 5 km/h or more.
    - The variant id adds `speed=<kmh>` to the signature. Configs without a speed change keep their M1 ids.
    - On Skibidi's 3 km crop this adds 11 RB configs (22 → 33).
    - "No limit" lanes keep the road speed. Speeds on tracks and paths are reported.

25. **TM:PE bus-only lanes become RB bus lanes.** A lane counts as bus-only when it allows Bus but neither PassengerCar nor CargoTruck. It goes through the IMT lane-override mechanism; `lanesFrom` records the source. Other vehicle restrictions are reported: CS2 has no per-lane restrictions.

26. **Junction rules: vanilla first, the Traffic mod for the rest.**
    - Vanilla CS2:
      - traffic lights on/off for every junction, from the CS1 node flag;
      - all-way stops, when every road into a junction has a TM:PE Stop sign;
      - turn bans (`Forbid*`), derived from lane arrows only when every inbound car lane is customised, so a default lane never loses a turn;
      - crosswalks.

      The flag names were checked in CS2's Game.dll.
    - Traffic mod (installed: Paradox Mods 80095, which has a lane connector and Yield/Stop/Priority signs): priority signs, lane arrows, lane connections and U-turns. `plan.requires` lists "Traffic" only when one of these is present.
    - Report only: timed lights (they become plain lights), turn on red, blocked-junction entry, lane changing, parking restrictions, and track lane connections.

27. **Lane references use the plan frame.** Each reference gives `ordinal` among the driving lanes left → right, `count`, `offset` (CS1 metres, + = right of start → end) and `dir`. The plan's reversal of inverted segments makes NetInfo positions and directions valid in the plan frame without flipping. RB roads keep the CS1 driving-lane order, so the importer matches by ordinal.

28. **Transit.**
    - Line type → CS2 line prefab. Trolleybus, tourist bus and intercity bus become Bus lines (reported); ferries become passenger ship lines.
    - Monorail, cable car, helicopter, blimp and walking tours have no CS2 line and are reported.
    - Kerbside stops (bus, tram) are kept only on plan roads, with side and position along the plan segment.
    - Station lines keep their stops with `requiresStations: true` until buildings are converted.
    - Lines keep at least two stops in the crop, otherwise they are left out. Truncated lines are flagged.
    - Colours are `#rrggbb`.

29. **Water.** The plan carries:
    - the CS1 sea level (absolute; the terrain step makes CS2 heights equal to CS1);
    - the natural sources in the crop, with their target heights;
    - the wet share and depth of the crop from `water.r16`, as a check.

    The depth snapshot itself isn't transferred, because CS2 re-simulates water. Pump, drain and cleaner sources belong to buildings.

30. **Node Controller is report only.** CS2 builds node geometry itself. The report lists changed node types (with sample ids), hidden markings and free-form shift/slope/twist nodes.

31. **A per-category conversion summary** (`pkg.summary`) drives the Overview "What converts" list, the README and the CLI output. Each category is converts / approximated / report only / not converted / not in this file. Buildings show as "not converted yet" when the export has them.

32. **The UI options for M2:**
    - zoning on/off, theme, and the high-density residential and office mappings;
    - traffic, transit and water on/off.

    They are saved in `localStorage` and also exposed as CLI flags (`--theme`, `--residential-high`, `--office`, `--no-zoning`, `--no-traffic`, `--no-transit`, `--no-water`). The map has Zones and Transit layers.

### Open issues (M2)

- Zone painting, lane connections and priority signs haven't been run by the importer yet. The formats are documented in §2 and tested here.
- Turn bans are derived conservatively, so partially customised ends get none and keep their default turns. That is reported.
- The transit stop position along the segment (`t`) assumes CS1 lane beziers run from the segment start to its end. Use the stop `x`/`z` when they disagree.
- Districts, district policies and specialised industry aren't exported yet, so specialised zones become generic.

## M3 / docs (2026-11-21, overnight)

Taken by Claude while the user was away. Review them.

33. **The buildings worker's matcher is used as is, not re-implemented.**
    - `buildings/match/match.mjs` (`matchAssets`) and `closestOnBezier` from `buildings/lib/plan.mjs` are imported by relative path into the web core, the worker and the CLI. TypeScript compiles them with `allowJs`, and Vite's dev server is allowed to serve `..`.
    - The web adapter (`src/core/m3/buildings.ts`) adds only what the UI needs:
      - per-type overrides (a CS2 prefab or "skip"; `score: null`, reason "chosen by you");
      - normalised field names. It accepts both `cs2`/`prefab`, `cs2Lot`/`lot` and `noEquivalent`/`no-equivalent`, in case the other worker renames them.
    - Agreed with the buildings worker by message.

34. **The plan's `buildings` section uses the same rules and item shape as `buildings/lib/plan.mjs`.**
    - The web builds it itself, because overrides must reach the items. It adds `type: "BuildingPrefab"` and `district`.
    - A test compares the items, the angle convention and the skip counts with `buildBuildingsPlan()`, on a fixture and on the real Skibidi export with the real catalogue.
    - That test caught a change the other worker made overnight: the `alternatives` list on each item, for the placer to try in order. The web adds it too, and leaves it empty when the user chose the prefab.
    - `types` and `notPlaced` don't go into the plan: they are the Assets tab and report.json items (`kind: "buildings"`).

35. **The theme for building matching follows the zoning theme** (NA → "North American", EU → "European"). It only breaks ties between equally good CS2 buildings.

36. **The site bundles a slim CS2 catalogue.**
    - `public/cs2-catalogue.json` holds 263 placeable buildings (98 kB, 9 kB gzipped), against 17,717 prefabs and 10 MB in the full one. It keeps only the fields `match.mjs` reads.
    - A test checks that matching against it is identical to the full catalogue on all 423 Skibidi building types.
    - A catalogue the user loads in the Assets tab is slimmed in the browser before use. The worker receives the catalogue once, not with every build.
    - Shipping CS2 prefab names, lot sizes and capacities in a static site is metadata, not game assets. Hosting the site is the user's call.

37. **Growables are counted, not placed.** CS2 grows its own buildings on the converted zones. Also not placed:
    - sub-buildings;
    - pillars, pipe junctions and poles;
    - types with no CS2 equivalent (libraries, taxi stands, cable car stops);
    - buildings whose access road isn't in the plan.

    Each type is a report item.

38. **Districts.**
    - **Outlines:** traced from the CS1 district raster (cell edges, collinear points removed), only over cells whose centre is in the crop. Holes are filled and reported, because CS2 districts are simple areas.
    - **Policies with a CS2 equivalent:** the CS2 names come from the game's policy list (`Policy.TITLE[…]` in Locale.cok).
      - Direct: Heavy Traffic Ban, Combustion Engine Ban, Recycling, Urban Cycling Initiative, Bicycle Traffic Restriction.
      - Approximated and reported: Small Business Enthusiast, smoke detectors → Fire Safety Sprinkler System, power saving → Energy Consumption Awareness, paid parking → Roadside Parking Fee.
    - **Reported:** zone specialisations (Leisure, Tourist, Organic, Hightech, SelfSufficient, specialised industry), other policies, and park, industry, campus and airport areas.
    - Skibidi has no districts, so the layer was checked on the synthetic city (which now has two) and by unit tests.

39. **One source for the user documentation.**
    - `src/core/docs.ts` holds the guide, requirements, FAQ, conversion matrix and privacy text.
    - The in-app Help renders it, and `scripts/gen-docs.mjs` writes `docs/guide/{user-guide,faq,what-converts}.md`. A test fails when the files are stale.
    - The matrix status "in the package; CS2 side in progress" is used where the importer doesn't build the section yet: zoning, TM:PE junction rules, transit, buildings and districts in the mod.

40. **The top-level README** states each feature's real status: what the package contains versus what CS2 builds today (mod or script). It says plainly that the exporter and importer aren't on the Workshop or Paradox Mods yet. The links point only to the two Paradox Mods pages taken from the installed mods.

41. **Release polish.**
    - One app version, `APP_VERSION` (0.3.0), in `src/core/docs.ts`. It's shown on the landing page and in Help, stamped into `plan.generator`, and checked against package.json by a test.
    - `scripts/notices.mjs` writes `public/THIRD-PARTY-NOTICES.txt` from the licences of the bundled production dependencies (fflate, react, react-dom, scheduler; all MIT). A test checks it's current.
    - There's a privacy note on the landing page and in Help: nothing leaves the browser, and only `localStorage` holds the user's choices.
    - Meta description, Open Graph tags and an inline SVG favicon.
    - No licence is set for the project itself; that's the user's call.

42. **Help dialog.** `role="dialog"`, `aria-modal`, focus moves to its title, and Escape or a click outside closes it. It fits phone width: the matrix uses a fixed table layout and wrapping status chips (checked at 375 px).

### Open issues (M3 / docs)

- The importer doesn't build `buildings` from the plan yet; the buildings worker's placer reads the same items. The status table says so.
- When the CS2 game updates, refresh the bundled catalogue: `node buildings/run.mjs catalogue`, then `npm run slim-catalogue`.
- A CS1 policy name is mapped only when it matches exactly. Names I couldn't check against a real export (no district in Skibidi) may simply go to the report.
- Free-standing props and trees are exported but not converted.

## Self-contained package: terrain, water, outside connections (2026-11-21, overnight)

Taken by Claude while the user was away. Review them.

43. **The terrain converter, water and outside-connection code are ported, not reimplemented.**
    - `src/core/terrain/{heightmap,water,connections}.ts` are line-by-line TypeScript ports of `terrain/lib/heightmap.mjs`, `water.mjs` and `connections.mjs`: same arithmetic in the same order, the same float32 CS1 heights, the same defaults.
    - Tests compare the output with the terrain worker's own code:
      - **Synthetic terrain:** `terrain/convert.mjs` is run as a child process. Its `heightmap.r16` and `worldmap.r16` are byte-identical to ours, and the metadata is equal.
      - **Skibidi:** the same checks, plus `water.set` sources, depth checkpoints and outside connections compared with `convertWater` + `regionSources`, `depthCheckpoints` and `deriveConnections`.
    - The terrain worker keeps owning the algorithms. If they change, the tests show the divergence.

44. **Package layout:**
    - Files: `terrain/heightmap.r16`, `terrain/worldmap.r16` and `terrain/terrain.json` (= `terrain-cs2.json`, without `createdAt` and the PNG fields; `source.file` names the save and the .cs1x hash).
    - Plan: `terrain` (a pointer, with `seaLevelDry` = apply.mjs's "auto" sea level), `water.apply` (the exact `water.set` args), `water.checkpoints` and `outsideConnections`.
    - Proposed to workers 3 and 4 by message (no change requested at the time of writing), and documented in schema §2.
    - The PNG is not included: the mod uses the r16, and the PNG is only for the map editor's manual import.

45. **The heightmaps are computed only when the package is written.** That's `completePackage()`, called by the CLI and the download, never on a crop change. Two 4096² passes take about 1.3 s.
    - The cheap parts are computed in `buildPackage`: water sources, region sources, checkpoints (one pass over 1081²) and connections.
    - The UI zips in the Web Worker: Skibidi takes 3.6 s for 21.8 MB. While it ran, there were no long tasks and the main thread never went more than 30 ms without answering.

46. **The zip stores plain r16, deflated at level 6** (64 MB → about 21 MB). Splitting the high and low bytes would give 12.5 MB, but the mod would then need an un-split step; we keep the agreed byte-identical r16 files.

47. **UI:**
    - A "Terrain of the crop" preview in Overview: a hillshade of the square, north up, with CS1 water in blue (depth grid, else below the sea level), and the ground range and sea level as text.
    - The Terrain line of "What converts" now says the heightmap is in the package and gives the sea level (CS1's, or the dry level).
    - The download button shows progress while the worker builds the zip.

48. **Layout fix found while checking:** `.work` was a flex item with `flex: 1 1 0%` in a container that has only a `min-height`, so a tall right panel (with the new preview) stretched the map to 2,494 px. Now it's `flex: none` with an explicit height, and the grid row is `minmax(0, 1fr)`.

49. **Version 0.4.0.** The docs gain a terrain/world-map row, an outside-connections row, and an FAQ entry on the package size.

## Trees, rocks and IMT junction surfaces

Asked by the buildings worker (through the exporter session) so the CS2 mod's Import button can place them from a web package.

50. **`plan.objects` and `plan.surfaces` come from `buildings/lib/objects.mjs`, called unchanged** (`buildObjectsSection`, `buildSurfacesSection`), the same way the web tool already uses `match.mjs`. The rows are then identical to the scripted flow's `cs1conv-objects.json`, and the mod has one format to read. The adapter `src/core/m3/objects.ts` only adds report lines and the "What converts" entry. Tests compare the plan with direct calls, on a fixture and on Skibidi.

51. **In the plan, not in a separate `cs1conv-objects.json`.** Cacao's 3 km crop adds 12,260 tree rows: about 0.4 MB of compact JSON, small next to the terrain. One file is simpler for the mod and for the user.

52. **The bundled slim catalogue now also carries the CS2 trees and boulders** (28 objects, 98 → 101 kB): name, kind, uiGroup, height and a TreeObject marker, all that `vegetation.mjs` reads. Building matching is unchanged, because `match.mjs` only looks at placeable buildings. A test checks that the slim catalogue gives the same objects section as the full one on Skibidi. A catalogue that lists only buildings (an old slim one loaded in the Assets tab) leaves `objects` out, and "What converts" says the catalogue has no trees.

53. **Sections only when they have items.** `objects` needs a catalogue; without one, "What converts" says the trees wait for it. A converted junction fill is no longer reported as "no CS2 equivalent"; the IMT line of "What converts" counts the surfaces. Options: "Trees and rocks" and "IMT junction surfaces" in the UI, `--no-objects` / `--no-surfaces` in the CLI.

54. **The reader exposes `trees` and `props`** (trees.json / props.json, schema §1) under the names `objects.mjs` expects.

## Placement fallback per building type

Asked by the buildings worker: its placer (Cs2Buildings) reads `plan.buildings.items[].fallback` = `nearby` | `anywhere` | `skip`.

55. **The web tool writes `fallback` on every item**, not only when the user chose one. The plan then says what will happen, and the mod's own run default no longer matters for web packages. `buildings/lib/plan.mjs` gives the same items when called with the same `fallbackByType` (tested on the fixture and on Skibidi).

56. **Default: `anywhere` for CS2 lots over 400 cells, else `nearby`.** The suggestion was "lot bigger than 6 × 6". On the real cities, that sent 84 of 108 Skibidi buildings, 33 of 43 Cacao and 172 of 204 Rennmont "anywhere": a clinic is 11 × 6 and a police station 12 × 7. Moving those across the crop is worse than a 400 m search. With more than 400 cells (bigger than 20 × 20, about 160 m square), the default picks exactly the huge ones: University (40 × 28), incinerator, prison, coal, gas and solar plants, cargo train terminal (10, 7 and 15 buildings in those three crops). Harbours and airports are bigger still. Big parks (CityPark11 is 12 × 12) stay `nearby`; the user can change them.

57. **UI:** a second select per placed type in the Assets tab, "If it doesn't fit at its spot: Auto (…) / look nearby (≤ 400 m) / anywhere in the crop / don't place it". Choices are saved with the other preferences, and "Reset choices" clears them too. **CLI:** `--fallback nearby|anywhere|skip` for all types, `--fallback-by-type <map.json>` per type. Unknown values fall back to the default.

58. **Trees:** the objects op now moves a tree off a CS2 road (its `roadsideMove`) instead of dropping it. That's an importer option, so the plan doesn't change; only the "What converts" text says so.

## Release polish: real-city pass, French, in-game guide (0.5.0)

59. **Real-city UI pass** (Skibidi, Rennmont, Cacao, Westfield; desktop 1440 × 900 and phone 375 × 812). Fixed:
    - **Slow open on big cities:** Rennmont took about 8 s from page load to its first plan, 6 s of it in drawing the 6,043 zone blocks into one Path2D. Chrome's Path2D gets quadratically slower as one path grows (40,000 squares: 4.7 s in one path, 0.2 s in 2,000-square paths). Zones and building footprints are now split into paths of at most 2,000 shapes. Rennmont: 2.2 s to the plan in the production build.
    - **"Not in this file" for things the file has but the city doesn't** (no transit lines, no districts, no IMT fills, no trees in the crop): a new status, "Nothing to convert".
    - **Wording:** parts with a count of 0 are left out ("0 segments get bus lanes…"). The water and terrain lines give the same sea level (0.1 m). A flat crop reads "flat ground at 60 m". Districts say "city-wide: …" when only city policies convert.
    - **Report tab:** CS1 references read "segment 394 · road type Highway" instead of raw JSON, and the filter chips have readable names.
    - **Layout:** the tabs stay on one row (they scroll sideways when the panel is narrower than the labels). The Assets table gives its last column more room, so the two selects are readable.

60. **i18n without a library.** `src/core/i18n/` has the English and French messages and an ICU-like formatter (variables, plural with `one` / `other` / `=n`, numbers by locale). It is about 100 lines with no dependency. The core needs it too: the "What converts" summaries and `README.txt` are built in the Web Worker and by the CLI. The language comes from `navigator.languages` (French if the first of en/fr listed is French, else English), a header menu switches it, and the choice is kept in localStorage. `document.lang` and the title follow.

61. **What is translated:** every UI string; the "What converts" summaries and details; the package `README.txt`; the user guide, FAQ, conversion matrix and privacy text (`docs.fr.ts`, generated to `docs/guide/fr/`); the reader's error messages (`ReadError` carries i18n keys).
    - **What stays English:** `report.json` and its messages in the Report tab, a technical log the importer and support read. A note says so in French. Also in English: the names of CS2 prefabs, CS2 policies and the importer's option labels, which the guide quotes as the game shows them.
    - **`--lang fr`** in the CLI. French tab names are shorter ("Aperçu", "Types RB") so the tabs fit.

62. **Translation tests** (`test/i18n.test.ts`):
    - French has every key, with the same variables, and every message parses.
    - Every key the code uses exists, as do the keys built at run time (statuses, report kinds, sections).
    - No literal English text is left in the JSX.
    - The French docs mirror the English ones: same steps and paragraphs, same matrix statuses, and the same importer labels, taken from `cs2-importer/mod/LocaleEN.cs`.

63. **Guide rewritten around the importer's real options**, read from `cs2-importer/mod/LocaleEN.cs`:
    - Unzip the package under `%LOCALAPPDATA%\cs1-to-cs2\`, so it shows in "Packages on this PC".
    - **Install Road Builder configs**, then restart.
    - New city → **Check** → **Prepare map** → the steps under **Import into this city** → **Import into this city**.

    "Prepare map" comes from the importer's own check message; worker-3 is finishing it and was asked to confirm the labels. The matrix now says what the importer builds today (zoning, junction settings, priority signs, buildings, trees, districts, surfaces, transit). It says "in progress" for terrain/water (Prepare map) and for lane arrows and lane connections. The README.txt and the Overview install steps follow the same flow.

64. **Kept in step with the other workers' in-flight changes:**
    - `regionSources` gains `lake-boost` and `<kind>-spread` sources (terrain worker), ported so the terrain test stays byte-for-byte.
    - The buildings worker's `addDefaultDepots` is called on the plan's `buildings` after transit, so lines get a depot. Rail yards are reported as needing their track connected. The Skibidi parity test now compares items and `defaultDepots` with theirs.

65. **Version 0.5.0.** The build-size warning limit is raised to 700 kB. The page carries its docs and messages in two languages: 504 kB, 165 kB gzipped. There is no second route to split off.

66. **Guide corrected with worker-3's facts** (supersedes the "Prepare map" part of 63):
    - The importer doesn't apply terrain, water or outside connections yet. The guide says the roads follow the map's own ground, and that the package's `terrain` folder is ready for later. There is no "Prepare map" step until worker-3 confirms the label.
    - New steps "Check the package" and "Import into the city", with the exact option labels, status messages, timing and the import report path.
    - Limits: TM:PE lane connections, lane arrows and U-turn rules are not applied in this version (only priority signs); about 7 % of zone cells find no CS2 block; metro and train lines need their stations; rail yards need their track linked by hand.
    - Two new FAQ entries, and the matrix notes updated. The Overview install steps, README.txt and the top-level README follow the same flow.
    - Worker-4's expected water source counts match the web port (Skibidi 14 region sources, Cacao 23).

## Automated UI quality (0.5.1)

67. **UI tests run the real app in jsdom** with React Testing Library and axe-core. The new dev dependencies are jsdom, @testing-library/react, @testing-library/dom and axe-core, and none of them is bundled.
    - **No browser in CI:** Playwright would bring a browser download and a second runner for six smoke tests. jsdom lacks canvas, Path2D, ResizeObserver and Worker, and `test/ui/dom.ts` stubs the first three in about 60 lines. Without Worker, `usePackage` already falls back to building on the main thread, which the tests want anyway.
    - **Synthetic `.cs1x`:** the test zips `syntheticCity()` into a real `.cs1x` and opens it through the file input, so the reader is covered too.
    - **Downloads:** they are captured from `URL.createObjectURL` plus the anchor click, and the zip is unpacked and checked.
68. **axe-core on every view:** the landing page in EN and FR, all five tabs, and the four Help sections. A test fails on violations and on undecided results.
    - **Undecided rules:** jsdom has no layout, so axe reports page-has-heading-one, landmark-one-main, heading-order and color-contrast as undecided even when the page is fine. The tests skip those four, assert one `<h1>` and one `<main>` per view directly, and contrast was checked in Chromium (dev server plus axe).
    - **Checks have been proven:** a planted low-contrast paragraph and planted unlabelled controls were caught in both environments.
69. **Violations found and fixed:**
    - The main view had no `<h1>`: it now has a visually hidden "Converting {city}". The map and package halves have hidden `<h2>`s, so the Overview's `<h3>`s follow in order.
    - `aria-label` sat on the map legend `<div>` and on the `<code>` copy boxes, where ARIA doesn't allow it. They are now labelled groups.
    - The ✕ buttons' names didn't contain their visible text. The symbol is now `aria-hidden`, with hidden text for the name.
    - The Help body scrolled but couldn't take focus. It is now a focusable, labelled region.
    - The Help conversion table had spanning area-header rows with no data cells. It is now one table per area with a `<caption>`, like the Markdown version.
70. **"Prepare map" in the guide:** worker-3's labels landed in `cs2-importer/mod/LocaleEN.cs` (commit b03c9a5).
    - The guide (EN + FR) gains step 10, "Prepare the map" (group **New city**, about 8 s for a 3 km crop). The import step ends with **Outside connections**, and "After the import" says to let the water fill.
    - The What-converts table marks terrain and outside connections "converts" and water "approximated" (CS2 re-simulates it). The FAQ answer about missing hills and rivers is rewritten. The install steps, README.txt and the top-level README include Prepare map.
    - The label test now requires every option, group and tab label of the importer's page to be quoted in the guide, in both languages, so a new option can't go undocumented.
71. **Version 0.5.1:** user-visible changes are the guide, the Help table layout and the accessibility fixes.

72. **Guide brought to worker-3's verified in-game texts:**
    - **Check** quotes the new "Map: …" line.
    - **Prepare map** quotes its progress and result lines, takes 10–20 s, and gives the "Start a new city first" and "no terrain in the package" cases.
    - **Import** quotes "Outside connections: 20 of 20 (13 road, 7 train)." and the "turned off in the Options" skip text.
    - **Limits:** the shallow-junction sentence is replaced with worker-3's wording: CS2 merges the first metres of roads leaving at a very shallow angle, and a few overlapping CS1 bits are left out and reported.

73. **Metro and train yards follow decision #83 (never inside the city):**
    - **Field:** the plan writes `transit.depotMode = { Metro | Train: "edge" | "manual" }`, worker-4's proposed field, one key per rail type that has lines in the plan. The default is "edge". formatVersion is unchanged.
    - **Choosing it:** in the UI, a select per type under Transit lines, "at the crop edge (where the line's track leaves the area)" or "I'll place it myself"; it is saved with the options. In the CLI, `--metro-depot` and `--train-depot`.
    - **Bus and tram:** `addDefaultDepots` is now called with the bus and tram lines only. The buildings worker's `plan.mjs` dropped rail on its side too, so both agree and the Skibidi parity test still holds.
    - **Text:** one report line and one "What converts" detail per rail type; the FAQ, the matrix, the guide's limits and schema §2 are updated.
74. **The importer-label test reads `LocaleEN.cs` as committed (`git show HEAD:…`, falling back to the file).** Worker-3's uncommitted new options in the shared checkout (Browse…, Remove imported, lane connections, a French Options page) had turned the web suite red mid-work. CI sees HEAD, so the guide must still follow as soon as they are committed.

75. **Guide updated for Cs2ImporterMod 0.3.0** (worker-3's verified labels; uncommitted on their side):
    - **Packages:** no need to unzip. **Browse…** takes the downloaded zip and unpacks it under `%LOCALAPPDATA%\cs1-to-cs2\packages\`; **Open packages folder** opens that folder.
    - **New import toggles:** **Keep this map's terrain** (off: the import refuses a city that wasn't prepared) and **Lane connections (Traffic mod)** (experimental, off).
    - **New controls:** **Remove imported** undoes an import, **Cancel** stops after the current step.
    - **"Start a new city first"** only appears with no city loaded.
    - **Lane arrows and lane connections:** the matrix now says "approximated, experimental"; U-turn rules are "report only".
    - **FAQ:** two new entries (undo an import; "terrain isn't the package's").
    - **French guide:** it quotes the importer's French labels and status texts (`LocaleFR.cs`, `Strings.cs`), since the mod's page follows the game's language. It also says to see the English guide when the game runs in English.
    - **Label test:** it checks each guide against its own locale file. `CS1CONV_IMPORTER_WORKTREE=1` checks the working copy; it passes now against worker-3's uncommitted files.
