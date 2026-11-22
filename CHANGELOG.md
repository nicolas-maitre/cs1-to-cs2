# Changelog

The CS1 → CS2 converter as a whole. The version is in [`VERSION`](VERSION); the scheme is in [docs/RELEASE.md](docs/RELEASE.md). Each component also keeps its own changelog (for example [cs1-exporter/CHANGELOG.md](cs1-exporter/CHANGELOG.md)), and the release notes combine them.

## 0.3.1 (2026-11-22)

**Added after the final acceptance of 693ba8a**
- **Importer:** a new opt-in option (off by default) joins train and metro stations to the imported track and adds rail yards where lines leave the area (#161). Transit lines get their CS1 names again in the mod. Trains still don't run on every city; see the importer's changelog.
- **Acceptance:**
  - a vehicles-per-imported-line check;
  - the heights check accounts for imported track trimmed under the longer CS2 platforms.

RC3 passed on Skibidi, CacaoCity (left-hand) and Westfield, from a clean build through the real mod flow (docs/QA.md §11). Components: exporter 1.2.2 (unchanged), **importer 0.4.0**, **web tool 0.9.5**, **buildings 0.4.0** (RC3 tested importer 0.3.4, web tool 0.9.3 and buildings 0.3.3; the changes since are listed below).

**What works now**
- **Roads at their CS1 heights:** the ground follows zoneable roads; highways and ramps keep their CS1 height with retaining walls or embankments; paths lie on the ground. Each road type's mode can be changed in the web tool. CS1 tunnels stay tunnels.
- **Roads as in CS1:** vanilla CS2 roads when they match, Road Builder roads otherwise, no invented medians, Node Controller shifts kept, the driving side enforced.
- **Stretched junctions kept** (web tool 0.9.5, #157): Node Controller offsets at junctions give long, smooth splits and merges instead of kinks and crossing lane lines.
- **Lane connections and arrows** as in CS1, and they survive a save and reload, like the roads themselves.
- **Power and water:** electricity and water outside connections at the edge of the imported area, CS1's own power lines and pipes rebuilt, low-voltage power plants linked to the roads. About 99–100 % of homes were supplied in RC3.
- **Buildings, trees, districts, zoning, bus and tram lines** as in 0.3.0, with a report that names everything left to do by hand.

**Opt-in**
- Transformer stations next to high-voltage power plants (importer setting).
- Utility facilities that don't fit may move up to 400 m (web tool notice).
- Importing into a city that drives on the other side (importer setting).
- Rail and subway yards inside the converted area (command line only).

**Known limits**
- **Trains and metros don't run yet** (#99, #133): lines and stations are created, but no train leaves its yard.
- **Buildings whose CS2 lot overlaps a road at the CS1 spot aren't placed** (#106); the report lists each one as "place it yourself".
- **Power producers away from any road produce nothing** in CS2 (#150); high-voltage plants need a transformer (opt-in).
- IMT junction fills aren't converted; a few roads lose their traffic upgrade flags on reload (#149).

**Since RC3**
- **Bus and tram depots** (buildings 0.4.0, #152): when none of a city's bus or tram depots fits at its CS1 spot, one is placed nearby so its lines get buses (Skibidi: 31 buses, 4 in RC3). Other buildings and rail yards still never move.
- **Matcher 1.2.1:** CS1's Water Tower becomes the CS2 groundwater pump.
- **Web tool 0.9.4:**
  - phone layout: the tabs wrap instead of hiding behind a sideways scroll, and the feature table's status badges no longer break mid-word;
  - guide and FAQ (English and French) updated to what the importer does now: electricity and water come in through outside connections, power plants need a transformer station (opt-in) or get a short line to the road, and bus and tram depots are placed nearby when none fits at its spot.

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
