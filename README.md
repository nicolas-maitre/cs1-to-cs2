# cs1-to-cs2

Rebuild a **Cities: Skylines** (CS1) city as a working **Cities: Skylines II** (CS2) city.

```
CS1 + Exporter mod ──▶ city.cs1x ──▶ web tool (crop, mapping, checks) ──▶ import package ──▶ CS2 + Importer mod ──▶ CS2 city
```

There are three parts:
- **CS1 → CS2 Exporter:** a CS1 mod that writes the loaded city to one `.cs1x` file. It never changes the save.
- **cs1conv web tool:** a static web page, so everything stays on your computer. You choose the part of the city that fits a CS2 map, check how each road type, building and rule converts, and download the import package. The package is self-contained: it also carries the converted terrain, water and outside connections. The same core is a Node CLI for scripts.
- **CS1 → CS2 Importer:** a CS2 mod that builds the package in a new CS2 city.

**User guide:** [docs/guide/user-guide.md](docs/guide/user-guide.md) · [FAQ and troubleshooting](docs/guide/faq.md) · [What converts](docs/guide/what-converts.md). The web tool's Help page shows the same text. **En français :** [guide](docs/guide/fr/user-guide.md) · [FAQ](docs/guide/fr/faq.md) · [ce qui est converti](docs/guide/fr/what-converts.md); the web tool follows the browser's language (English or French).

## Status

The plan columns say whether the web tool writes a feature into the import package; the CS2 column says what builds it in CS2 today. Details: [what converts](docs/guide/what-converts.md) and [docs/PLAN.md](docs/PLAN.md).

| Feature | In the package | Built in CS2 |
| --- | --- | --- |
| Roads: lanes, medians, bus/tram lanes, trees, speed (Road Builder), bridges, tunnels, slopes | yes | yes: Importer mod and scripts (Skibidi 3 km crop: 2035/2035 segments) |
| Train, metro and tram tracks, pedestrian and bike paths | yes | yes, same as roads |
| IMT lane paint → bus lanes and medians | yes | yes, part of the Road Builder road types |
| TM:PE speed limits and bus-only lanes | yes | yes, as Road Builder road variants |
| Terrain (playable area and world map) | yes: `terrain/heightmap.r16`, `terrain/worldmap.r16` | yes: Importer mod (**Prepare map**, in a new city), or by script (`terrain/apply.mjs`) |
| Water (sea level, rivers, lakes), outside connections | yes: `water.apply`, `outsideConnections` | yes: Importer mod (**Prepare map** for the water, **Outside connections** as the last import step), or by script (`terrain/mapprep.mjs`) |
| Zoning (NA or EU theme) | yes | yes: Importer mod (**Zoning**) |
| TM:PE traffic lights, all-way stops, turn bans, crosswalks | yes | yes: Importer mod (**Traffic lights, stops, turn bans, crosswalks**) |
| TM:PE priority signs | yes | yes: Importer mod with the Traffic mod (**Priority signs**) |
| TM:PE lane arrows and lane connections | yes | experimental: Importer mod with the Traffic mod (**Lane connections (Traffic mod)**, off by default) |
| TM:PE U-turn rules | yes | not applied yet (listed in the import report) |
| Transit lines with stops and colours, default depots | yes | yes: Importer mod (**Transit lines**); metro and train lines need their stations |
| Service buildings, stations, parks, landmarks | yes (matched to CS2 buildings, with a fallback per type) | yes: Importer mod (**Buildings, trees, districts**) and the `buildings/` script |
| Districts and policies | yes | yes: Importer mod (**Buildings, trees, districts**) |
| Trees and rocks | yes (CS2 trees and boulders) | yes: Importer mod (**Buildings, trees, districts**) |
| IMT pavement, grass and gravel fills at junctions | yes (CS2 surfaces) | yes: Importer mod (**Buildings, trees, districts**) |
| Node Controller, timed lights, painted IMT junction markings | report only | – |
| Other props (benches, lamps, …) | exported, not converted | – |

## Requirements

- **CS1:** the city, plus the CS1 → CS2 Exporter. Cities using 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool and Vanilla+ / workshop roads are supported. Their data is exported when the mod is on, and nothing breaks without them.
- **CS2:** version 1.6 or later, with:
  - [Road Builder](https://mods.paradoxplaza.com/mods/87190/Windows);
  - [Traffic](https://mods.paradoxplaza.com/mods/80095/Windows), optional, for TM:PE priority signs, lane arrows and lane connections;
  - the CS1 → CS2 Importer.

  The exporter and importer aren't on the Workshop / Paradox Mods yet; build them from source (below).
- **Web tool:** any current browser. Nothing is uploaded.

## Limits

- **Crop:** a CS2 map's playable area is 14.3 km across, so an 81-tile city (17.3 km) is converted one square at a time. Roads crossing the square's edge are left out.
- **Road widths:** Road Builder has fixed lane widths and sidewalks of at most 7 m, so some roads come out a few metres narrower or wider. Each change is reported.
- **No CS2 equivalent:** monorail, cable cars, libraries, park areas, Node Controller geometry, timed traffic lights and junction paint. They are listed in the package's `report.json`.
- **Zoned buildings:** CS2 grows its own buildings on the converted zones, so they are not copied one by one.

## Using the parts

**1. Export (CS1).** Build and enable the exporter, load your city, then Options → Mod Settings → *CS1 → CS2 Exporter* → **Export city**. It writes `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Cs1Exporter\<city>-<date>.cs1x`. See [cs1-exporter/README.md](cs1-exporter/README.md).

**2. Plan (web tool or CLI).** Open the web tool (`cd web; npm install; npm run dev`, or host the static `web/dist/` anywhere) and drop the `.cs1x` on it. Or from a terminal:

```powershell
node web/cli/cs1conv.ts plan city.cs1x --auto-crop 3000 --out package --install-rb
```

See [web/README.md](web/README.md).

**3. Import (CS2).** Start CS2 with Road Builder and the importer. In Options → *CS1 → CS2 importer*, press **Browse…** and choose the downloaded zip (it is unpacked under `%LOCALAPPDATA%\cs1-to-cs2\packages\`), then **Install Road Builder configs** and restart (Road Builder reads its configs only at startup). Start a new city, pick the package → **Prepare map** → **Import into this city**, and let the game run a few minutes so rivers and lakes fill. **Remove imported** undoes an import. The [user guide](docs/guide/user-guide.md) ([français](docs/guide/fr/user-guide.md)) has every step; see also [cs2-importer/README.md](cs2-importer/README.md).

**All in one, by script:** [tools/e2e](tools/e2e/README.md) runs export → plan → new CS2 city → terrain and water → roads → outside connections → save:

```powershell
node tools/e2e/run.mjs --apply-terrain --cs1x $env:LOCALAPPDATA\cs1-to-cs2\samples\skibidi.cs1x
```

## Repository

| Folder | What |
| --- | --- |
| [`schema/`](schema/README.md) | File formats: `.cs1x` (§1, exporter) and the import package (§2, web tool → importer) |
| [`cs1-exporter/`](cs1-exporter/README.md) | CS1 mod (C#, .NET 3.5) |
| [`web/`](web/README.md) | Web tool and CLI (TypeScript, React, Vite) |
| [`cs2-importer/`](cs2-importer/README.md) | CS2 importer: mod and `exec` library (C#, .NET 4.8 against the game's DLLs) |
| [`terrain/`](terrain/README.md) | CS1 heightmap → CS2 terrain, water and map preparation |
| [`buildings/`](buildings/README.md) | CS2 building catalogue, CS1 → CS2 building matcher (shared with the web tool), placement |
| [`tools/`](tools/PROTOCOL.md) | Bridge mods for both games, `gamectl` (start, stop and script either game) and the e2e run |
| [`docs/`](docs/) | [User guide](docs/guide/user-guide.md), [plan](docs/PLAN.md), [decisions](docs/DECISIONS.md) |
| [`spikes/`](spikes/M0-REPORT.md) | Early feasibility spikes |

## Developer setup

- **Tools:** Windows, the .NET SDK (for the mods), Node ≥ 24 (the web tool and CLI; `tools/` and `terrain/` need Node ≥ 20), Steam with CS1 and CS2 installed.
- **Game paths:** copy `local.props.example` to `local.props` and set `CS1Managed` / `CS2Managed` to the games' `…_Data\Managed` folders. Every C# project builds against your installed game.
- **Builds** (Release builds also deploy to the games' local mod folders):

  ```powershell
  dotnet build cs1-exporter -c Release
  dotnet build cs2-importer/mod -c Release
  dotnet build tools/cs1-bridge -c Release
  dotnet build tools/cs2-bridge -c Release
  ```

  The bridge mods are only needed to script the games.
- **Web tool:** `cd web; npm install; npm run dev` (http://localhost:5178). `npm run build` writes the static site to `web/dist/`.
  - `npm run docs` regenerates `docs/guide/` from `web/src/core/docs.ts`.
  - `npm run notices` regenerates the third-party notices.
  - `npm run slim-catalogue` refreshes the bundled CS2 building catalogue from `buildings/run.mjs catalogue`.
- **Scripting the games:**
  - `node tools/gamectl/bin/gamectl.js cs2 status | start | stop | exec …` ([PROTOCOL.md](tools/PROTOCOL.md)).
  - CS1 keeps new local mods disabled; enable them in Content Manager, or with `tools/cs1-bridge/enable-mod.ps1`.
  - Scripts that start CS2 take `%LOCALAPPDATA%\cs1-to-cs2\cs2.lock`.
- **Tests:**

  ```powershell
  cd web; npm test; npm run typecheck
  node --test buildings/match/match.test.mjs buildings/lib/plan.test.mjs
  ```

  `terrain/` and `tools/gamectl/` have their own `npm test`, and `cs2-importer/tests/` has the importer's core tests.
- **Sample data:** scripts read and write under `%LOCALAPPDATA%\cs1-to-cs2\samples\`: `skibidi.cs1x`, `cs2-catalogue.json` and the sample packages.
