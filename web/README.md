# web: the cs1conv web tool and CLI

Reads a `.cs1x` export of a Cities: Skylines city (or the M0 network dump). You pick a crop square, and the tool:
- maps every CS1 NetInfo to a CS2 prefab or a generated Road Builder road;
- matches CS1 buildings to CS2 buildings;
- converts zoning, TM:PE rules, transit lines, water and districts;
- writes the import package (`schema/README.md` §2).

The same TypeScript core runs in the browser and as a Node CLI. Everything runs client-side: nothing is uploaded. The user guide lives in [`docs/guide/`](../docs/guide/user-guide.md) and in the app's Help page, both generated from `src/core/docs.ts`.

```
src/core/          pure TS, no DOM (Node ≥ 24 runs it directly through type stripping)
  cs1x.ts            .cs1x v1 reader (fflate) with typed ReadErrors + M0-dump adapter
  imt.ts             IMT fill → whole-lane bus/median overrides   (port of spikes/m0-roadbuilder-configs/imt-lanes.mjs)
  rb.ts              NetInfo lane layout → Road Builder RoadConfig (port of wq-to-rb.mjs, same ids and output; + speed variants)
  mapping.ts         NetInfo category → RB / vanilla CS2 prefab / skipped
  crop.ts            crop square, CS2 limits, auto crop
  plan.ts            buildPackage(): plan + roadbuilder/*.json + report.json + README.txt
  m2/                zoning, water, transit, traffic (TM:PE), nodecontroller → plan sections
  m3/                buildings (adapter over buildings/match/match.mjs), districts, slim CS2 catalogue
  summary.ts         what converts per category; .cs1x section check
  docs.ts            user guide, FAQ, conversion matrix, privacy text (app Help + docs/guide)
  readme.ts          the package README.txt
  synthetic.ts       synthetic n × n city with districts, for performance checks
src/ui/            React + canvas UI (map, landing, panels, Assets, Help); packageWorker.ts builds packages off the main thread
cli/cs1conv.ts
scripts/           gen-docs.mjs (docs/guide), notices.mjs (third-party notices), slim-catalogue.mjs (bundled CS2 catalogue)
public/            cs2-catalogue.json (slim CS2 building catalogue), THIRD-PARTY-NOTICES.txt
test/              vitest; includes parity with the M0 spike generator and with buildings/lib/plan.mjs on the real data, when present
```

## Use

```powershell
cd web; npm install
npm test                  # vitest
npm run typecheck
npm run dev               # UI on http://localhost:5178 (drop a .cs1x on the page)
npm run build             # static site in dist/ (relative paths: host it from any folder)
npm run docs              # regenerate docs/guide/*.md from src/core/docs.ts
npm run notices           # regenerate public/THIRD-PARTY-NOTICES.txt
npm run slim-catalogue    # refresh public/cs2-catalogue.json from %LOCALAPPDATA%\cs1-to-cs2\samples\cs2-catalogue.json
```

Dev server only:
- `http://localhost:5178/?open=/samples/skibidi.cs1x` opens a file from `%LOCALAPPDATA%\cs1-to-cs2\samples\`.
- `?synthetic=150` generates a 150 × 150 street grid with two districts, for performance checks. This one also works in the build.

CLI (from the repo root):

```powershell
node web/cli/cs1conv.ts inspect <file.cs1x> [--json]
node web/cli/cs1conv.ts plan <file.cs1x> --crop <cx>,<cz>,<size> --out <dir> [--overrides map.json] [--install-rb] [--keep-other-rb]
node web/cli/cs1conv.ts plan <file.cs1x> --auto-crop 3000 --out <dir> [--theme NA|EU] [--residential-high High|Medium|Mixed] [--office High|Low]
     [--catalogue <cs2-catalogue.json>] [--asset-overrides <map.json>] [--no-zoning] [--no-traffic] [--no-transit] [--no-water] [--no-buildings] [--no-districts]
node web/cli/cs1conv.ts install-rb <package dir>
```

- **`--auto-crop <size>`** centres the square on the 250 m grid position that fully contains the most road segments.
- **`--install-rb`** copies the package's `roadbuilder/*-cs1conv.json` into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`. It removes other `*-cs1conv.json` files there unless `--keep-other-rb` is given, and never touches any other file.
- **`--overrides`** (road types) is a JSON object keyed by ground NetInfo name. Values are `{"mode":"vanilla","type":"RoadPrefab","name":"Large Road"}`, `{"mode":"rb"}`, `{"mode":"skip"}` or `{"mode":"auto"}`.
- **`--asset-overrides`** (buildings) is a JSON object keyed by CS1 building type name, with a CS2 building prefab name or `"skip"` as value.
- **`--catalogue`:** the CS2 building catalogue. It defaults to `%LOCALAPPDATA%\cs1-to-cs2\samples\cs2-catalogue.json` when that exists (`node buildings/run.mjs catalogue` writes it). Without a catalogue, buildings aren't matched and the plan has no `buildings` section.

## Buildings

The matching module is the buildings worker's `buildings/match/match.mjs`. It's pure and shared, imported by relative path. On top of it, `src/core/m3/buildings.ts` adds two things:
- the per-type overrides from the Assets tab;
- the plan's `buildings` section, built by the same rules as `buildings/lib/plan.mjs`. A test compares the two on the real Skibidi export.

The site bundles a slim catalogue (`public/cs2-catalogue.json`, 263 placeable buildings, 98 kB): only the fields the matcher reads, and it gives identical matches (tested). The Assets tab can load another catalogue, for example after a game update.

## UI

- **Landing:** what the tool does, the steps, the mods, the limits, a privacy note, and clear errors for wrong files, unsupported format versions and failed sections.
- **Map:**
  - Roads are drawn at their real width and coloured by type, with tunnels faded and elevated roads outlined.
  - Layers: IMT fills, terrain hillshade, CS1 zones, transit lines with stops, building footprints by category, district areas with names, and the segments crossing the crop edge.
  - It has two canvas layers, tile culling and a worker for the package, so it stays smooth on very large cities.
- **Crop square:** drag to move and drag corners to resize (max 14 336 m). Keyboard: arrows move it, +/− resize. Touch: pinch to zoom.
- **Panel:**
  - Stats.
  - Download the package (zip with a README) or only the RB configs.
  - Tabs:
    - Overview: what converts per category, options, install steps, file check.
    - Mapping: per-NetInfo overrides.
    - Road types: the generated RB configs.
    - Assets: per CS1 building type, its count, the CS2 building, score, reason, and an override.
    - Report.
- **Help:** user guide, what converts, FAQ, and about & privacy (version, mods, third-party notices).

## Sample packages

All are made from `%LOCALAPPDATA%\cs1-to-cs2\samples\skibidi.cs1x` with `--auto-crop 3000` (centre 3750, −1250):

| Package | Contents |
| --- | --- |
| `skibidi-plan\` | M1, roads only |
| `skibidi-plan-m2\` | adds zoning, TM:PE, transit, water |
| `skibidi-plan-m3\` | adds `buildings`: 108 placed as 25 CS2 types, 7,226 growables left to zoning. Skibidi has no districts. |

Decisions are in [DECISIONS-web.md](DECISIONS-web.md).
