# Changelog

## 1.2.2 (2026-11-21)

- **Changed:** the web tool address now defaults to the hosted tool, https://nicolas-maitre.github.io/cs1-to-cs2/. A saved address equal to the old default (the local development server) follows the new default automatically; any other saved address is kept.

## 1.2.1 (2026-11-21)

- **New:** `manifest.traffic.leftHand`, true for a left-hand-traffic city ("invert traffic"). The importer needs it to match lane connections: on CacaoCity, a left-hand city, none of 265 matched without it.
- **Fix:** the zoning self-check in `report.json` reports the median distance and skips blocks whose road no longer exists (it showed a 3.3 km mean on one city whose data was fine).

## 1.2.0 (2026-11-21, M3 / release candidate)

- **New:** `assets.json`, a catalogue of every building, prop, tree and network prefab in use. Each entry has its title, workshop id, category, footprint, height, tags, instance count, and the building AI's settings (capacities, radii).
- **New:** `districts.json` + `districts.u8` / `parks.u8`: districts and park, industry, campus and airport areas as cell grids, with names, types, levels and policies, plus the city-wide policies.
- **New:** each building in `buildings.json` now has `accessSegment` (its road connection), `district` and `park`.
- **Options panel:**
  - Export button with a live status line.
  - Export folder setting with Open folder and Use default buttons.
  - Web tool address with an Open web tool button.
  - Toggles for every optional section.
  - A result dialog with the counts, the path and the next step.
  - Clear error dialogs.
- **Export runs over several frames**, with the simulation paused and resumed afterwards.
- **Streaming export:** every section is written straight into the zip, so memory no longer grows with city size. A failed section is removed from the file instead of left half-written.
- **Version** is shown in the mod name. The manifest has `exporter.version` (release) and `exporter.build`.
- **Workshop packaging:** `PreviewImage.png`, a Workshop description and this changelog.

## 1.1.0 (2026-11-21, M2)

- **New sections:** zones, water (+ depth grid), transit, TM:PE, Node Controller, buildings, props, trees.
- **Export paths** may use environment variables.
- **The CS1 bridge** (dev tool) blocks autosaves while scripts drive the game.

## 1.0.0 (2026-11-21, M1)

- **First version:** network, NetInfos (with variant parents), IMT fills and the terrain heightmap, written as `.cs1x` (a zip of JSON).
- **The Options button and `exec` entry point** share the same code.
