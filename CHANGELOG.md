# Changelog

The CS1 → CS2 converter as a whole. The version is in [`VERSION`](VERSION); the scheme is in [docs/RELEASE.md](docs/RELEASE.md). Each component also keeps its own changelog (for example [cs1-exporter/CHANGELOG.md](cs1-exporter/CHANGELOG.md)), and the release notes combine them.

## 0.3.0 (2026-11-21, M3: buildings and objects)

- **Exporter 1.2.0:**
  - asset catalogue, districts and park areas;
  - building road connections;
  - a release-grade Options panel, streaming export, and Workshop packaging.
- **Web tool:**
  - an Assets tab with CS1 → CS2 building matching (the shared `buildings/match` module) and per-type choices;
  - a `buildings` section in the import plan;
  - a user guide.
- **CS2 buildings, objects and districts** (`buildings/`):
  - a prefab catalogue and building matching;
  - placement snapped to the imported roads, with slides, alternatives and nearby free land;
  - trees and rocks, and districts with names and policies.
  - The steps are ready to compile into the importer mod.
- **Importer:** embankments snap to the terrain, so buildings can connect to those roads.
- **Not converted:** IMT junction fills (no CS2 equivalent; see `buildings/DECISIONS-buildings.md` #20).

## 0.2.0 (2026-11-21, M2: the rest of the city)

- **Exporter:** zones, water, transit, TM:PE, Node Controller, buildings, props and trees sections, and a CS1 autosave guard for scripted runs.
- **Terrain:** the CS1 heightmap converted and applied to CS2, plus map preparation (clearing the base map, outside connections, water).
- **Importer:** a user-facing CS2 mod (Options page, Check, Install Road Builder configs, Import) and an end-to-end script.
- **Web tool:** a release-grade app and the M2 plan sections (zoning, water, transit, traffic).

## 0.1.0 (2026-11-21, M1: roads first)

- **The vertical slice:** CS1 exporter (`.cs1x`) → web tool (crop and road mapping) → CS2 importer that builds the roads in a new CS2 city, all scripted.
- **Skibidi.02:** 2035/2035 road segments built.

## 0.0.1 (2026-11-20, M0: feasibility)

- **Spikes:** CS2 road placement and junctions by script, the CS1 network dump, WQ roads to Road Builder configs, and IMT paint as bus lanes and medians.
- **Game control:** bridge mods for both games and the `gamectl` CLI, for scripted game control.
