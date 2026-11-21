# web: preview, crop and road mapping (M1)

Reads a `.cs1x` (or the M0 network dump), lets you pick a crop square, maps every CS1 NetInfo to a CS2 prefab or a generated Road Builder road, and writes the import package (`schema/README.md` §2). The same TypeScript core runs in the browser and as a Node CLI.

```
src/core/   pure TS, no DOM (Node ≥ 24 runs it directly through type stripping)
  cs1x.ts      .cs1x v1 reader (fflate) + M0-dump adapter
  imt.ts       IMT fill → whole-lane bus/median overrides   (port of spikes/m0-roadbuilder-configs/imt-lanes.mjs)
  rb.ts        NetInfo lane layout → Road Builder RoadConfig (port of wq-to-rb.mjs, same ids and output)
  mapping.ts   NetInfo category → RB / vanilla CS2 prefab / skipped
  crop.ts      crop square, CS2 limits, auto crop
  plan.ts      buildPackage(): cs1conv-plan.json + roadbuilder/*.json + report.json
src/ui/     React + canvas UI
cli/cs1conv.ts
test/       vitest (includes byte-parity with the M0 spike generator on the real dump, when present)
```

## Use

```powershell
cd web; npm install
npm test                  # vitest
npm run dev               # UI on http://localhost:5178 (drop a .cs1x on the page)
```

In the dev server, `http://localhost:5178/?open=/samples/skibidi.cs1x` opens a file from `%LOCALAPPDATA%\cs1-to-cs2\samples\`. The built UI (`npm run build` → `dist/`) is fully client-side: nothing is uploaded.

CLI (from the repo root):

```powershell
node web/cli/cs1conv.ts inspect <file.cs1x> [--json]
node web/cli/cs1conv.ts plan <file.cs1x> --crop <cx>,<cz>,<size> --out <dir> [--overrides map.json] [--install-rb] [--keep-other-rb]
node web/cli/cs1conv.ts plan <file.cs1x> --auto-crop 3000 --out <dir>
node web/cli/cs1conv.ts install-rb <package dir>
```

- **`--auto-crop <size>`** centres the square on the 250 m grid position that fully contains the most road segments.
- **`--install-rb`** copies the package's `roadbuilder/*-cs1conv.json` into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`. It removes other `*-cs1conv.json` files there unless `--keep-other-rb` is given, and never touches any other file. Road Builder reads configs only at CS2 start.
- **`--overrides`** is a JSON object keyed by ground NetInfo name:
  - `{"mode":"vanilla","type":"RoadPrefab","name":"Large Road"}`
  - `{"mode":"rb"}`
  - `{"mode":"skip"}`
  - `{"mode":"auto"}`

  The UI's mapping table produces the same overrides (it keeps them in `localStorage`).

## UI

- **Map.** Roads are drawn at their real width and coloured by type. Also shown: tunnels (faded), elevated roads (outlined), IMT fills in their own colours, a terrain hillshade, and segments that cross the crop edge (dashed yellow, left out of the plan).
- **Crop square.** Drag it to move it, drag a corner to resize it. It is capped at 14 336 m, the CS2 playable area. Drag elsewhere to pan, wheel to zoom, hover a segment for its mapping.
- **Right panel.**
  - Plan stats.
  - Download the package (zip) or only the RB configs.
  - Mapping table with per-NetInfo overrides.
  - Generated RB configs with lanes, width loss and notes.
  - The report, filterable by kind.

## Sample package

`%LOCALAPPDATA%\cs1-to-cs2\samples\skibidi-plan\` comes from `skibidi.cs1x` with `--auto-crop 3000`.
- Crop: centre (3750, −1250).
- 2035 segments, 1670 nodes, 22 RB configs (two of them IMT bus variants).
- By prefab type: 1949 RB, 80 track, 6 path.

Decisions are in [DECISIONS-web.md](DECISIONS-web.md).
