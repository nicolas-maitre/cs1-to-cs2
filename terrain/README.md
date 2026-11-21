# terrain

CS1 heightmap (`.cs1x`) → CS2 terrain, loaded into a running CS2 city by script. This is a head start on M2. Background is in [NOTES-cs2-terrain.md](NOTES-cs2-terrain.md); the choices made, with their reasons, are in [DECISIONS-terrain.md](DECISIONS-terrain.md).

```
.cs1x (terrain.json + terrain.r16) ──convert.mjs──▶ heightmap.r16 (14 336 m) + worldmap.r16 (57 344 m) + terrain-cs2.json
                                                     │
CS2 (city loaded, no roads yet) ◀──apply.mjs──▶ gamectl exec cs2-apply/Cs1ConvTerrain.dll ──▶ TerrainSystem.ReplaceHeightmap …
```

Needs Node ≥ 20 and no npm install.

## Convert

```bash
node terrain/convert.mjs --cs1x city.cs1x --plan package/cs1conv-plan.json --out out/terrain
node terrain/convert.mjs --cs1x city.cs1x --crop 3750,-1250,3000 --out out/terrain   # centerX,centerZ,size (CS1 m)
```

`--plan` reads `crop` and `transform` from the import plan, so terrain and roads share one transform. Options:

| Option | Effect |
| --- | --- |
| `--mode absolute` (default) | `heightScale` 1024, offset 0, so a CS2 height equals the CS1 height |
| `--mode fit` | tightest scale and offset that hold the range |
| `--height-scale s --height-offset o` | explicit scale and offset |
| `--no-world` | skip `worldmap.r16` |
| `--no-png` | skip `heightmap.png` |

**Output.** `heightmap.r16` is 4096² little-endian uint16, row 0 = south, and is what `apply` loads. Next to it:

- `worldmap.r16`: the backdrop, same layout.
- `heightmap.png`: 16-bit, north up, for a manual editor import.
- `terrain-cs2.json`: scale, offset, crop, transform and stats (`clampedFraction` is the share outside the CS1 map, edge-clamped).

Skibidi takes about 2 s.

## Apply in CS2

1. Build the library (once, and after every change; each build gets a new version, so `exec` picks it up live):
   ```bash
   dotnet build -c Release terrain/cs2-apply
   ```
2. Start a city, then apply:
   ```bash
   node tools/gamectl/bin/gamectl.js cs2 start --load <save>    # or a new city (bridge game.new)
   node terrain/apply.mjs --terrain out/terrain --cs1x city.cs1x --shots .gamectl/terrain/shots
   ```

`apply.mjs` does the following:
- sets the height scale and offset, the world map, the heightmap and the sea level (`--sea-level auto|keep|<m>`; `auto` = below the lowest point, a dry map);
- re-snaps trees and objects;
- samples the terrain at a 9×9 grid plus up to `--points 200` CS1 ground nodes (crop-transformed);
- writes `apply-report.json` with three comparisons: game vs file (should be about 0), game vs CS1 node terrain height, and immediate vs later samples. With `--shots` it also writes screenshots.

**Full check.** `node terrain/run-newcity.mjs --terrain out/terrain --cs1x city.cs1x [--map "<map>"]` runs the whole check and writes `run-newcity.json`:
- takes the shared CS2 lock;
- starts CS2 and creates a new city through the bridge's `game.new`;
- applies the terrain;
- saves the city as `cs1conv-terrain-test` (`save.write`);
- reloads that save and samples again, to check that the terrain persists;
- stops the game and releases the lock.

**Order.** Apply terrain **before** building roads: existing nets keep their height and pull the terrain up toward them.

**Ops.** The library's ops can also be called directly, e.g.

```bash
gamectl cs2 exec terrain/cs2-apply/bin/Release/net48/Cs1ConvTerrain.dll --type Cs1ConvTerrain.Ops --args '{"op":"info"}'
```

| Op | What it does |
| --- | --- |
| `info` | terrain and water state |
| `apply` | load the heightmap and world map, set the sea level, refresh objects |
| `sample` | terrain height at points |
| `refresh` | re-snap objects in an area |
| `objects` | how far trees sit from the ground |
| `sea` | set the sea level |

## Tests

```bash
cd terrain && npm test
```

Synthetic terrain only, no game. Covered:
- zip and PNG round trips;
- a tilted plane reproduced exactly, including orientation and crop transform;
- synthetic features (cone, trench, plateau) at the right place;
- edge clamping;
- the world map;
- the height mapping modes;
- CLI output consistency.

`node terrain/lib/synthetic.mjs out.cs1x` writes the synthetic `.cs1x` used for the first in-game check.

## Verified (2026-11-21)

**In game**, on a loaded test save, never saved:
- **Game vs file:** max 0.0001 m at 93 points.
- **Synthetic nodes:** within 0.2 m, except the cone tip at 2.07 m, a kink the 16 m CS1 grid can't hold.
- **Trees:** re-snapped; median 0.000 m off the ground.
- **World map:** joins at the edge without a seam.

**Offline, Skibidi:** heightmap vs CS1 terrain under 1564 plan ground nodes gives a median of 0.013 m, p95 0.065 m and max 0.23 m.

**New city, Skibidi** (`run-newcity.mjs`, base map Archipelago, saved as `cs1conv-terrain-test`):
- **Game vs file:** median 0 m, p95 0.0002 m at 281 points.
- **Game vs CS1 terrain** under 200 plan nodes: median 0.013 m, p95 0.15 m.
- **After save and reload:** identical samples. Height scale (1024, 0), the CS1 world map and the sea level (98 m) all persist.
- **Outliers:** 7 points read up to 618 m too high. They lie where the base map's **pre-built highway and rail** (outside connections) still run at the old map's height; CS2 raises the terrain into walls under them (screenshots in `.gamectl/terrain/skibidi/shots`).

**Still open:**
- **Base map nets:** before the terrain goes in, bulldoze the base map's own nets in the playable area (or pick a base map without any). The outside connections then have to be rebuilt by the importer.
