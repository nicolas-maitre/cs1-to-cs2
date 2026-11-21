# Terrain decisions

Decisions taken autonomously (by Claude) on 2026-11-21 while the user was away; please review them. The evidence behind each one is in [NOTES-cs2-terrain.md](NOTES-cs2-terrain.md).

1. **Apply in a running city by script, not through the map editor.**
   - The `exec` library calls `TerrainSystem.SetTerrainProperties` and `ReplaceHeightmap`, the same calls the editor's import button makes.
   - **Why:**
     - They are public, work in game mode and take about 15 ms.
     - The editor path would need UI or file-dialog automation, a map asset and a new-game-from-map step, none of which can be scripted today.
     - In-game sampling matched the file to 0.0001 m.

2. **Height mapping "absolute" by default: `heightScale 1024`, `heightOffset 0`, so a CS2 height equals the CS1 height.**
   - **Why:**
     - CS1 heights are 0–1024 m by construction: uint16 × 1/64.
     - The step size is 1.56 cm, the same as CS1's own.
     - The plan's bezier `y` values, `heightAboveTerrain` and later water heights stay valid without any translation.
   - `--mode fit` (offset just under the minimum, scale just over the range, at least 200) and an explicit `--height-scale`/`--height-offset` are there if CS2 ever needs it. Nothing found so far says it does.

3. **The CS2 heightmap covers the whole 14 336 m playable area around the crop centre, filled from CS1 data, not just the crop.**
   - **Why:** CS1 has terrain out to ±8640 m, so there's no reason to leave flat ground or a cliff at the crop edge.
   - Wherever the window runs past the CS1 map, the edge values are repeated (clamp), and the converter reports the share as `stats.clampedFraction`. The Skibidi plan crop at x = 3750 gives 16 %.

4. **The world (backdrop) map is built from CS1 data too:** 57 344 m, 14 m per pixel, the same mapping, written as `worldmap.r16` and applied by default.
   - **Why:** the world map shares the height scale with the playable map.
     - Leaving it unchanged shows the old map's heights at the wrong scale.
     - Removing it (`--remove-worldmap`) leaves a void past the edge.
     - With CS1 data it joins without a seam (screenshot checked).
   - Outside the CS1 map it's 91 % clamped, which shows as flat land at the edge height.

5. **Sea level `auto` = `floor(min terrain) − 20`, i.e. a dry map.**
   - **Why:** CS2 has a global `WaterSystem.SeaLevel` that comes from the base map (370 m on the test map, 511.7 by default).
     - With CS1 heights (Skibidi: 118–820 m) that would flood or misplace water.
     - The `.cs1x` carries no water yet (`seaLevel: null`).
   - Converting water is M2 work. `--sea-level n|keep` overrides the default.

6. **After a replace, queue the whole playable area for `GroundHeightSystem`** (`TerrainSystem.OnAreaChanged`), on by default.
   - **Why:** without it every tree and object keeps its old `y` (measured: a median of 369 m in the air). With it, they snap to the ground (median 0.000 m).

7. **Terrain goes in before any roads.** The order is new city → terrain → import roads.
   - **Why:** existing nets keep their elevation, and CS2 raises the terrain toward them. That leaves cliff walls and very tall bridge pillars.
   - The importer's import area is empty anyway (Spike 1 rule), so this costs nothing.

8. **Conventions:**
   - `heightmap.r16` is row 0 = south (minimum z), column 0 = west, little-endian, with pixel centres at `−7168 + (i + 0.5) × 3.5`. That is exactly CS2's texture and CPU-height layout, and also the `.cs1x` row order.
   - `heightmap.png` is north up, as an image is viewed, and is meant for a manual editor import. Its orientation in the editor has not been checked.

9. **Bilinear resampling,** as the task asked.
   - It is exact on planes and rounds sharp kinks by about `slope × 1.75 m`.
   - **Why that's fine:** CS1 ground nodes sit on CS1's own smoothed terrain, which is far smoother than that. On Skibidi, the converted heightmap vs CS1 terrain under the plan's 1564 ground nodes gives a median of 0.013 m, p95 0.065 m and max 0.23 m.

10. **Node-only dependencies, written by hand:** a zip reader, a 16-bit PNG codec and the converter, all on `node:zlib`.
    - **Why:** the task asked for no dependencies, and the format needs are tiny.
    - The zip reader handles stored and deflated entries and zip64, and checks the CRC of each entry.

11. **Base map nets are left alone by the terrain step, and reported.**
    - The base map's own highway and rail (outside connections) stay at the old height and raise terrain walls. On Archipelago, 7 of 281 samples were off by up to 618 m.
    - **Why it stays out of the terrain step:**
      - Deleting nets and rebuilding the outside connections is road work, which belongs to the importer's "clear the import area" step (Spike 1 rule).
      - The terrain library shouldn't delete things on its own.
    - `run-newcity.mjs` takes the first playable map unless `--map` is given. A base map without pre-built nets, or clearing them first, is the fix.

## M2: map preparation

Decisions taken autonomously (by Claude) on 2026-11-21; please review them. The evidence is in NOTES-cs2-terrain.md §7–8 and in the run summaries under `.gamectl/mapprep/`.

12. **`clear` deletes every top-level net of the base map (the same as the importer's clear).** The road/train outside-connection markers go with their end nodes.
    - **Opt-out:** `--keep-utilities` keeps powerlines, seaways and pipes, along with their power and ship connections.
    - **Why everything:** at first only roads and tracks were cleared, since they are what raise terrain walls. But in worker-3's e2e run 20261121-044148, leftover powerline/seaway pieces inside the crop were then deleted by the importer's pre-import clear, right before a native crash in the import. Clearing everything first, as the importer clear does, passed (run 20261121-045036, 2035/2035).
    - **Verified:** on Sunshine Peninsula, 125 road/track edges and 128 nodes were deleted (roads-only mode). The terrain then matched the converted file everywhere (max 0.0005 m at 281 points), against 618 m walls before.

13. **Every structural change runs in `ToolUpdate`** (exec `phase:"tool"`, and `pump:"tool"` for coroutines), using the importer's pattern: `Deleted` on edges and their sub-objects, one frame, then orphan nodes, then `Updated` on the survivors.
    - **Why:** the same deletions made from the main-loop pump crashed CS2 natively, with no managed stack (twice).

14. **Outside connections are rebuilt at the crop edge, not at the map edge.**
    - **What:** every CS1 highway (class `Highway`, ramps included) or train track (`TrainTrack*AI`) that crosses the crop edge, and whose inner node was imported, gives one marker on that node.
    - **Prefabs:** a road marker is `Road Outside Connection - Oneway` or `- Twoway`, a train marker `Train Outside Connection - Oneway` or `- Twoway`. The first one by name is used.
    - **Why:** CS2's `OutsideConnectionSystem` builds the connection lanes from any node that owns a marker, ending 10 m outward from the map centre. Extending the CS1 roads to the map edge would invent network that CS1 didn't have.
    - **Other roads:** small roads crossing the edge are not connections by default (`--all-roads` makes them so). A connection on every residential street would make CS2 route through traffic through neighbourhoods.

15. **Water: the CS1 sea level is used as is (heights are absolute), then `ResetToSealevel()`. The base map's water sources are removed, and only CS1 `natural` sources are created.**
    - **Facility sources:** CS1 water and sewage buildings' outlets and intakes come later, with the buildings (M3).
    - **Height:** CS2 source height = CS1 target level − ground at the source.
    - **Radius:** a fixed 20 m, because CS1 sources have no radius. The base maps use 8–20 m.
    - **Region sources:** CS1 rivers and lakes above the sea that no converted source feeds also get one source each, because CS1 feeds many of them from sources outside the playable area. They come from CS1's depth grid: one 8-connected region per lake or river (cells more than 4 m above the sea level; CS1's sea surface varies by ±3 m).
      - **Inflow:** a region touching the playable edge gets its source at the deepest cell within 300 m of the edge.
      - **Lake:** a region without a source gets its source at its deepest cell.
      - **Size:** height = CS1 depth there; radius 20–80 m, growing with the region.
      - Skibidi gets 4: 2 inflows and 2 lakes.
    - **Creation:** sources are created as entities directly, not through definitions (NOTES §8). Definitions only ever give Temp sources, which vanish.

16. **Default base map: Sunshine Peninsula.** All 12 built-in maps were surveyed in game (`mapprep.mjs survey-maps`).
    - **Score:** nets + 5 × buildings + 50 × water sources + 200 if the sea covers part of the map. Every map has some sea, and all of it gets replaced.
    - **Why Sunshine Peninsula:**
      - fewest nets to clear (197, of which 125 are roads/tracks);
      - few buildings (5 ruins landmarks);
      - 3 water sources;
      - it is buildable.
    - **Runner-up:** Corral Riches, with 378 nets and 18 buildings.
    - The e2e uses Plains, which has 372 nets and 14 buildings, but the more important part is that the clear works on any map.

    | Map | Score | Nets | Buildings | Water sources | Sea level | Terrain (playable) |
    | --- | ---: | ---: | ---: | ---: | ---: | --- |
    | Sunshine Peninsula | 572 | 197 | 5 | 3 | 600 | 481–802 |
    | Corral Riches | 818 | 378 | 18 | 3 | 507 | 339–536 |
    | Plains | 842 | 372 | 14 | 4 | 370 | 138–2359 |
    | Coast | 866 | 351 | 33 | 3 | 255 | 58–1544 |
    | Mountain Village | 953 | 453 | 10 | 5 | 242 | 57–2306 |
    | Fjords | 1029 | 344 | 7 | 9 | 173 | 0–886 |
    | River Valley | 1035 | 410 | 15 | 7 | 390 | 333–1479 |
    | Archipelago | 1051 | 471 | 26 | 5 | 865 | 684–1575 |
    | Barrier Island | 1076 | 636 | 18 | 3 | 405 | 311–596 |
    | Highlands | 1215 | 550 | 43 | 5 | 519 | 306–1039 |
    | Delta | 1458 | 598 | 12 | 12 | 90 | 0–984 |
    | Lakeland | 1860 | 585 | 35 | 18 | 88 | 55–173 |

17. **Large lakes get an extra "lake-boost" source** at their deepest cell, even when a CS1 source feeds them: any lake of at least 500 CS1 cells (about 0.13 km²), radius 80 m, height = CS1 depth.
    - **Why:** QA Q9 (CacaoCity). Six lakes of 0.7–2 km² each had one CS1 source, converted with a 20 m radius, and filled far too slowly. CS1 also ran its water simulation for years.
    - **Result after 5 minutes (300 s):** 105/150 CS1-wet points wet (before: 42/150, though that number was taken before any simulation), median depth error 3.2 m (before: 7.6 m), dry 150/150.
    - The lakes are still filling, so a longer simulation would raise the numbers.

18. **`clear` also deletes the base map's top-level buildings** (`--keep-buildings` opts out).
    - **Why:** Sunshine Peninsula's 5 ruin landmarks keep the old map height (480–800 m), and their lots raise the converted terrain into tall pillars. That is the "vertical wall at the east edge" in QA Q9.
    - **Verified on CacaoCity:** 5 buildings + 197 edges deleted, no crash. Terrain = file to max 0.0004 m afterwards.

19. **Big water regions get spread sources:** farthest-point sampling over the region, one per 1500 CS1 cells, at most 8, radius 40 m, height = CS1 depth at that cell. Sampling stops once every cell is within 400 m of a source.
    - **Why:** in the full-city Skibidi run, a 3.5 km river fed from one point stayed dry along most of its length (wet 100/150, p90 53 m).
    - **Result after 300 s:**
      - full-city Skibidi: wet 146/150, median 1.07 m, p90 6.8 m;
      - CacaoCity: wet 143/150, median 1.8 m, p90 6.1 m.
    - **Cost:** 15 and 29 sources, about 100 ms to compute.

20. **`run-mapprep.mjs` installs the package's Road Builder types with `--keep-other-rb` before starting CS2.**
    - **Why:** another run's `--install-rb` had removed them, which made 431 of CacaoCity's 684 segments `prefab-missing`.
