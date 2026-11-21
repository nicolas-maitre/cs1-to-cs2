# web: the cs1conv web tool and CLI

Reads a `.cs1x` export of a Cities: Skylines city (or the M0 network dump). You pick a crop square, and the tool:
- maps every CS1 NetInfo to a CS2 prefab: a vanilla CS2 road when the generated road has the same lanes, else a generated Road Builder road;
- matches CS1 buildings to CS2 buildings;
- converts zoning, TM:PE rules, transit lines, water, districts, trees and rocks, and IMT junction surfaces;
- writes the import package (`schema/README.md` §2).

The same TypeScript core runs in the browser and as a Node CLI. Everything runs client-side: nothing is uploaded. The user guide lives in [`docs/guide/`](../docs/guide/user-guide.md) (French: [`docs/guide/fr/`](../docs/guide/fr/user-guide.md)) and in the app's Help page, all generated from `src/core/docs.ts` and `docs.fr.ts`.

The app is in English and French: it follows the browser's language, and a menu in the header switches it (remembered locally). The "What converts" list and the package `README.txt` come in the chosen language; `report.json` and the Report tab's messages stay in English.

```
src/core/          pure TS, no DOM (Node ≥ 24 runs it directly through type stripping)
  cs1x.ts            .cs1x v1 reader (fflate) with typed ReadErrors + M0-dump adapter
  imt.ts             IMT fill → whole-lane bus/median overrides   (port of spikes/m0-roadbuilder-configs/imt-lanes.mjs)
  rb.ts              NetInfo lane layout → Road Builder RoadConfig (port of wq-to-rb.mjs, same ids and output; + speed variants)
  mapping.ts         NetInfo category → RB / vanilla CS2 prefab / skipped
  crop.ts            crop square, CS2 limits, auto crop
  plan.ts            buildPackage(): plan + roadbuilder/*.json + report.json + README.txt
  m2/                zoning, water, transit, traffic (TM:PE), nodecontroller → plan sections
  m3/                buildings (adapter over buildings/match/match.mjs), districts, slim CS2 catalogue,
                     objects (trees, rocks and IMT junction surfaces from buildings/lib/objects.mjs)
  terrain/           port of terrain/convert.mjs + lib/water.mjs + lib/connections.mjs: heightmap 4096², world map,
                     water.set sources, outside connections (byte-identical to the Node scripts, tested)
  zip.ts             the downloadable package zip (deflated terrain), built in the Web Worker
  summary.ts         what converts per category; .cs1x section check
  docs.ts, docs.fr.ts  user guide, FAQ, conversion matrix, privacy text in English and French (app Help + docs/guide)
  i18n/              en.ts / fr.ts messages (UI, What converts, README.txt) and a small ICU-like formatter (plurals, numbers)
  readme.ts          the package README.txt
  synthetic.ts       synthetic n × n city with districts, for performance checks
src/ui/            React + canvas UI (map, landing, panels, Assets, Help); packageWorker.ts builds packages off the main thread;
                   i18n.tsx is the language provider and switch
cli/cs1conv.ts
scripts/           gen-docs.mjs (docs/guide), notices.mjs (third-party notices), slim-catalogue.mjs (bundled CS2 catalogue), slim-roads.mjs (bundled vanilla road layouts)
public/            cs2-catalogue.json (slim CS2 building catalogue), THIRD-PARTY-NOTICES.txt
test/              vitest; includes parity with the M0 spike generator and with buildings/lib/plan.mjs on the real data, when present
                   ui.test.tsx: the real UI in jsdom (smoke tests + axe-core accessibility checks), ui/dom.ts stubs canvas and downloads
```

## Use

```powershell
cd web; npm install
npm test                  # vitest
npm run typecheck
npm run dev               # UI on http://localhost:5178 (drop a .cs1x on the page)
npm run build             # static site in dist/ (relative paths: host it from any folder)
npm run docs              # regenerate docs/guide/*.md and docs/guide/fr/*.md from src/core/docs.ts and docs.fr.ts
npm run notices           # regenerate public/THIRD-PARTY-NOTICES.txt
npm run slim-catalogue    # refresh public/cs2-catalogue.json from %LOCALAPPDATA%\cs1-to-cs2\samples\cs2-catalogue.json
npm run slim-roads        # refresh public/cs2-roads.json from %LOCALAPPDATA%\cs1-to-cs2\samples\cs2-roads.json
```

Dev server only:
- `http://localhost:5178/?open=/samples/skibidi.cs1x` opens a file from `%LOCALAPPDATA%\cs1-to-cs2\samples\`, and `?open=/qa/cacao.cs1x` one from `…\qa\`.
- `?synthetic=150` generates a 150 × 150 street grid with two districts, for performance checks. This one also works in the build.

CLI (from the repo root):

```powershell
node web/cli/cs1conv.ts inspect <file.cs1x> [--json]
node web/cli/cs1conv.ts plan <file.cs1x> --crop <cx>,<cz>,<size> --out <dir> [--overrides map.json] [--install-rb [--prune]]
node web/cli/cs1conv.ts plan <file.cs1x> --auto-crop 3000 --out <dir> [--theme NA|EU] [--residential-high High|Medium|Mixed] [--office High|Low]
     [--catalogue <cs2-catalogue.json>] [--asset-overrides <map.json>] [--fallback nearby|anywhere|skip] [--fallback-by-type <map.json>] [--no-zoning] [--no-traffic] [--no-transit] [--no-water] [--no-buildings] [--no-districts]
     [--no-objects] [--no-surfaces] [--no-terrain] [--no-world] [--lang en|fr] [--roads <cs2-roads.json>] [--no-prefer-vanilla]
node web/cli/cs1conv.ts install-rb <package dir> [--prune]
```

- **`--auto-crop <size>`** centres the square on the 250 m grid position that fully contains the most road segments.
- **`--install-rb`** copies the package's `roadbuilder/*-cs1conv.json` into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`. It keeps the `*-cs1conv.json` configs of other converted cities, so several packages can share the folder; `--prune` removes the `*-cs1conv.json` files that aren't in this package (`--keep-other-rb`, the old opt-out, is accepted and does nothing). `CS1CONV_RB_FOLDER` points it at another folder. It never touches any other file.
- **`--overrides`** (road types) is a JSON object keyed by ground NetInfo name. Values are `{"mode":"vanilla","type":"RoadPrefab","name":"Large Road"}`, `{"mode":"rb"}`, `{"mode":"skip"}` or `{"mode":"auto"}`.
- **`--asset-overrides`** (buildings) is a JSON object keyed by CS1 building type name, with a CS2 building prefab name or `"skip"` as value.
- **`--fallback`** sets what the importer does for every building type that doesn't fit at its CS1 spot: `nearby` (free land within 400 m), `anywhere` (anywhere along the imported roads in the crop) or `skip`. Without it, every type gets `skip` (docs/DECISIONS.md #106): a building that doesn't fit after its slide and smaller alternatives isn't moved; the report lists it with its CS1 position for you to place. (Until 0.6.0 lots over 400 CS2 cells got `anywhere` and the rest `nearby`.) **`--fallback-by-type`** is a JSON object keyed by CS1 building type name with one of those values.
- **`--lang fr`** writes the "What converts" lines and `README.txt` in French (report.json stays English).
- **`--roads`:** the vanilla CS2 road layouts used to prefer vanilla roads (docs/DECISIONS.md #100): a road type whose generated Road Builder road has the same lanes as a vanilla CS2 road uses that road (`v:<name>`, `matched: true`), so fewer Road Builder configs are installed. It defaults to `%LOCALAPPDATA%\cs1-to-cs2\samples\cs2-roads.json` (the importer's `roadlayouts` exec op writes it), else the bundled `web/public/cs2-roads.json`. **`--no-prefer-vanilla`** keeps every road on Road Builder. An override `{"mode":"rb"}` keeps that type on Road Builder.
- **`--catalogue`:** the CS2 building catalogue. It defaults to `%LOCALAPPDATA%\cs1-to-cs2\samples\cs2-catalogue.json` when that exists (`node buildings/run.mjs catalogue` writes it). Without a catalogue, buildings aren't matched and the plan has no `buildings` section.

## Package

The package is self-contained for the CS2 importer mod. Besides the plan, the report, the README and the Road Builder configs, it has:
- `terrain/heightmap.r16`, `terrain/worldmap.r16` and `terrain/terrain.json`, byte-identical to `terrain/convert.mjs`;
- in the plan: `terrain` (a pointer to the terrain files), `water.apply` (the args for `water.set`), `water.checkpoints` and `outsideConnections`.

The formats are in `schema/README.md` §2.

The two heightmaps are 32 MB each. They take about 1.3 s to compute and deflate to about 22 MB in the zip. Building and zipping run in the Web Worker (Skibidi: 3.6 s, with the UI responsive throughout), and only when you download; dragging the crop never computes them. `--no-terrain` and `--no-world` leave them out of a CLI package.

## Buildings

The matching module is the buildings worker's `buildings/match/match.mjs`. It's pure and shared, imported by relative path. On top of it, `src/core/m3/buildings.ts` adds two things:
- the per-type overrides from the Assets tab;
- the plan's `buildings` section, built by the same rules as `buildings/lib/plan.mjs`. A test compares the two on the real Skibidi export.

The site bundles a slim catalogue (`public/cs2-catalogue.json`, 101 kB): 263 placeable buildings and 28 CS2 trees and boulders, with only the fields the matchers read. It gives identical matches (tested).

Trees, rocks and IMT junction surfaces (`plan.objects`, `plan.surfaces`) come from `buildings/lib/objects.mjs`, called unchanged, so they match the scripted flow's `cs1conv-objects.json`. The Assets tab can load another catalogue, for example after a game update.

## Tests and accessibility

`npm test` runs everything, the UI included: `test/ui.test.tsx` renders the real app in jsdom (with React Testing Library), opens a synthetic `.cs1x` through the file input and checks the landing page, the map and tabs, keyboard moves of the crop, the What converts list, the downloaded zip's entries and the switch to French. It also runs axe-core on the landing page (EN and FR), every tab and every Help section and fails on any violation.

jsdom has no layout, so a few axe rules can't be decided there (colour contrast, "one level-one heading", "one main landmark", heading order). The test asserts the structural ones directly. For contrast, check in a browser: with `npm run dev`, load `/node_modules/axe-core/axe.min.js` into the page and run `axe.run(document)` on each view (done for 0.5.1: no violations, contrast included).

The guide must quote every label of the importer's Options page (`cs2-importer/mod/LocaleEN.cs` for the English guide, `LocaleFR.cs` for the French one). The test reads them as committed; `CS1CONV_IMPORTER_WORKTREE=1 npm test` checks the working copy, for instance while the importer worker is adding options.

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
    - Assets: per CS1 building type, its count, the CS2 building, score, reason, an override, and what happens if it doesn't fit at its spot.
    - Report.
- **Help:** user guide, what converts, FAQ, and about & privacy (version, mods, third-party notices).

## Sample packages

All are made from `%LOCALAPPDATA%\cs1-to-cs2\samples\skibidi.cs1x` with `--auto-crop 3000` (centre 3750, −1250):

| Package | Contents |
| --- | --- |
| `skibidi-plan\` | M1, roads only |
| `skibidi-plan-m2\` | adds zoning, TM:PE, transit, water |
| `skibidi-plan-m3\` | adds `buildings`: 108 placed as 25 CS2 types, 7,226 growables left to zoning. Skibidi has no districts. |
| `skibidi-package\` | self-contained: adds `terrain/` (heightmap, world map), `water.apply`, `water.checkpoints` and `outsideConnections` (20 connections) |

Decisions are in [DECISIONS-web.md](DECISIONS-web.md).
