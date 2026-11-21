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
