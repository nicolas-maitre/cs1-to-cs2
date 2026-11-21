# Changelog

The web tool (https://nicolas-maitre.github.io/cs1-to-cs2/): it turns a CS1 export (`.cs1x`) into an import package for the CS2 mod. The version is `version` in `web/package.json`. Decision numbers refer to [docs/DECISIONS.md](../docs/DECISIONS.md).

## 0.9.3 (2026-11-21, release candidate 3)

- **Walls burn** (#129, #132): beside roads in "CS1 height with walls" mode, the ground is brought within 7 m below and 11 m above the road. CS2 then builds retaining walls and embankments where CS1 had them, instead of tunnels under deep trenches or pillars under high ramps.

## 0.9.2 (2026-11-21)

- **Utility facilities can move if they don't fit** (#128, #130). Power plants, pumps, water towers and sewage facilities stay at their CS1 spot by default. When the crop has any, the Overview shows a notice with "Let them move nearby (≤ 400 m) if they don't fit", with an undo. CLI: `--utilities-nearby`.
- "What converts" counts CS1's off-road power lines and pipes as rebuilt by the importer.

## 0.9.1 (2026-11-21)

- **Tunnels stay tunnels** (#124, #125): the ground above a tunnel approach is no longer cut down, so CS2 keeps the 8 m of ground it needs for a tunnel.

## 0.9.0 (2026-11-21)

- **Node Controller shifts** (#107, #122) become off-node road ends in the plan, so road ends sit where CS1's Node Controller put them.

## 0.8.0 (2026-11-21)

- **No invented medians** (#114): a raised median is generated only where the CS1 road has one (median props or a clearly wider gap), not where its directions are only separated by paint.
- **CS1's off-road power lines and pipes** go into the plan (#115), for the importer to rebuild.

## 0.7.0 (2026-11-21)

- **The ground follows the roads** (#110–113, #116). Each road type gets a height mode in the Mapping tab:
  - **Ground follows the road** (default for zoneable roads and tracks): the terrain is shaped to the road's CS1 profile;
  - **CS1 height with walls** (default for highways, ramps and roads without zoning): the road keeps its CS1 height;
  - **Road follows the ground** (default for paths).
- The package's heightmap is burned accordingly; water sources keep their level.

## 0.6.1 (2026-11-21)

- **Buildings are never moved by default** (#106): a building that doesn't fit at its CS1 spot is listed as "place it yourself". Better station matching (elevated metro stations, platform length).

## 0.6.0 (2026-11-21)

- **Vanilla CS2 roads are used when they match** (#100); other roads are generated with Road Builder. CLI: `--no-prefer-vanilla`.
- Tram-only lanes no longer shift car-lane numbering (#102).

## 0.5.x (2026-11-21)

- French interface and guide; default bus and tram depots when the CS1 city has none (0.5.0).
- UI smoke and accessibility tests; the guide matches importer 0.3.0 (0.5.1).
- Lane numbering follows the generated Road Builder road, including bicycle lanes and IMT medians (0.5.2).

## 0.4.0 (2026-11-21)

- **Self-contained package:** terrain, water, outside connections, trees and surfaces, with a terrain preview; per-type building fallbacks in the Assets tab.

## Earlier

- M1–M3: roads, then zoning, transit, traffic rules, buildings and objects; see [CHANGELOG.md](../CHANGELOG.md).
