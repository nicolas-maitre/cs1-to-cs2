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
