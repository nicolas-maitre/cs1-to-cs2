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

- The vanilla CS2 prefab names other than the four confirmed ones haven't been checked in game. Neither has `ShoulderGroupPrefab` Asphalt 2m in a generated highway.
- Highway width loss: CS1 highways are 3–6 m wider than the RB result. An `Empty` filler or a wider shoulder could pad them; decide when looking at it in CS2.
- RB's 7 m sidewalk limit, which narrows wide WQ roads, is unchanged from M0 (reported as `width-loss`).
- Bike lanes inside a CS1 road become sidewalk-with-bike (`Parking:"B"`) only at the edge. Elsewhere they are mapped to car lanes with an `UNSUPPORTED` note, as in the spike.
- The M0 dump has no `parent` for elevated-only workshop roads. There the variant stands in, as in the spike. The `.cs1x` has parents.
