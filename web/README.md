# web: the cs1conv web tool and CLI

Reads a `.cs1x` export of a Cities: Skylines city (or the M0 network dump), lets you pick a crop square, maps every CS1 NetInfo to a CS2 prefab or a generated Road Builder road, converts zoning, TM:PE rules, transit lines and water, and writes the import package (`schema/README.md` §2). The same TypeScript core runs in the browser and as a Node CLI. Everything runs client-side: nothing is uploaded.

```
src/core/          pure TS, no DOM (Node ≥ 24 runs it directly through type stripping)
  cs1x.ts            .cs1x v1 reader (fflate) with typed ReadErrors + M0-dump adapter
  imt.ts             IMT fill → whole-lane bus/median overrides   (port of spikes/m0-roadbuilder-configs/imt-lanes.mjs)
  rb.ts              NetInfo lane layout → Road Builder RoadConfig (port of wq-to-rb.mjs, same ids and output; + speed variants)
  mapping.ts         NetInfo category → RB / vanilla CS2 prefab / skipped
  crop.ts            crop square, CS2 limits, auto crop
  plan.ts            buildPackage(): plan + roadbuilder/*.json + report.json + README.txt
  m2/                zoning, water, transit, traffic (TM:PE), nodecontroller → plan sections
  summary.ts         what converts per category; .cs1x section check
  readme.ts          the package README.txt
  synthetic.ts       synthetic n × n city for performance checks
src/ui/            React + canvas UI (map, landing, panels); packageWorker.ts builds packages off the main thread
cli/cs1conv.ts
test/              vitest (includes byte-parity with the M0 spike generator on the real dump, when present)
```

## Use

```powershell
cd web; npm install
npm test                  # vitest
npm run typecheck
npm run dev               # UI on http://localhost:5178 (drop a .cs1x on the page)
npm run build             # static site in dist/ (relative paths: host it from any folder)
```

Dev server only:
- `http://localhost:5178/?open=/samples/skibidi.cs1x` opens a file from `%LOCALAPPDATA%\cs1-to-cs2\samples\`.
- `?synthetic=150` generates a 150 × 150 street grid for performance checks. This one also works in the build.

CLI (from the repo root):

```powershell
node web/cli/cs1conv.ts inspect <file.cs1x> [--json]
node web/cli/cs1conv.ts plan <file.cs1x> --crop <cx>,<cz>,<size> --out <dir> [--overrides map.json] [--install-rb] [--keep-other-rb]
node web/cli/cs1conv.ts plan <file.cs1x> --auto-crop 3000 --out <dir> [--theme NA|EU] [--residential-high High|Medium|Mixed] [--office High|Low] [--no-zoning] [--no-traffic] [--no-transit] [--no-water]
node web/cli/cs1conv.ts install-rb <package dir>
```

- **`--auto-crop <size>`** centres the square on the 250 m grid position that fully contains the most road segments.
- **`--install-rb`** copies the package's `roadbuilder/*-cs1conv.json` into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`. It removes other `*-cs1conv.json` files there unless `--keep-other-rb` is given, and never touches any other file. Road Builder reads configs only at CS2 start.
- **`--overrides`** is a JSON object keyed by ground NetInfo name:
  - `{"mode":"vanilla","type":"RoadPrefab","name":"Large Road"}`
  - `{"mode":"rb"}`
  - `{"mode":"skip"}`
  - `{"mode":"auto"}`

  The UI's mapping table produces the same overrides.
- **The M2 flags** set the zone theme and densities, and switch the zoning, traffic, transit and water sections off. They match the options in the UI.

## What converts

| CS1 | CS2 |
| --- | --- |
| Roads (vanilla, Vanilla+/workshop) | generated Road Builder roads: lanes, medians, parking, bus and tram lanes, trees, speed. Widths may change (reported). |
| Elevated roads, bridges, slopes, tunnels | same Road Builder road at the CS1 height |
| Train, metro, tram tracks, paths | vanilla CS2 tracks and paths |
| IMT fills over a whole lane | bus lane (red) or median (pavement, grass); other fills are reported |
| Terrain | heightmap for the terrain step |
| Zoning | zoned cells → CS2 zones, NA or EU theme |
| TM:PE | speed limits and bus-only lanes → RB variants; traffic lights, all-way stops, turn bans, crosswalks → vanilla; priority signs, lane arrows, lane connections → Traffic mod; the rest is reported |
| Transit lines | CS2 lines with colour and stops (station lines wait for stations) |
| Water | sea level and natural water sources |
| Node Controller | reported |
| Pipes, power lines, buildings | not converted (CS2 lays utilities along roads; buildings come later) |

## UI

- **Landing page:** what the tool does, the six steps, the mods, the limits, and clear errors for wrong files, unsupported format versions and failed sections.
- **Map:**
  - Roads are drawn at their real width and coloured by type, with tunnels faded and elevated roads outlined.
  - Layers: IMT fills, terrain hillshade, CS1 zones, transit lines with stops, and segments crossing the crop edge (dashed yellow).
  - The canvas has two layers, a worker builds the packages, and the view is culled by tile, so it stays smooth on very large cities.
- **Crop square:**
  - Drag to move, drag a corner to resize. It is capped at 14 336 m, the CS2 playable area.
  - Keyboard: focus the map; arrows move the crop (Shift = 500 m), +/− resize, Ctrl+arrows pan, `[` `]` zoom, F / C fit.
  - Touch: pinch to zoom.
- **Right panel:**
  - Plan stats.
  - Download the package (zip with a README) or only the RB configs.
  - Tabs:
    - Overview: what converts per category, M2 options, install steps with copy buttons, file check.
    - Mapping: per-NetInfo overrides.
    - Road types: generated RB configs with lanes, width change and notes.
    - Report: filterable by kind.

## Sample packages

Both packages come from `%LOCALAPPDATA%\cs1-to-cs2\samples\skibidi.cs1x` with `--auto-crop 3000` (centre 3750, −1250).

- **`skibidi-plan\` (M1, roads only):** 2035 segments, 1670 nodes, 22 RB configs.
- **`skibidi-plan-m2\` (M2):**
  - Roads: the same 2035 segments, with 33 RB configs (11 TM:PE speed variants).
  - Zoning: 76,658 zoned cells.
  - Transit: 9 lines.
  - TM:PE: 325 priority signs, 237 lane connections, 211 lane arrows, 66 turn bans.
  - Traffic lights at 14 of 588 junctions.

Decisions are in [DECISIONS-web.md](DECISIONS-web.md).
