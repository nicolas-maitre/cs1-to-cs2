# Changelog

The buildings, objects, districts and utilities steps (`buildings/`), compiled into the CS2 importer mod. The version is `Steps.Version` in `src/Steps.cs`; the matcher (`match/match.mjs`, shared with the web tool) has its own version. Players see these changes through the importer's changelog. Decision numbers refer to [docs/DECISIONS.md](../docs/DECISIONS.md) unless marked "buildings #".

## Unreleased (release candidate 3, proposed 0.4.0)

- **Power plants join the grid** (#150, buildings #44–45): a new `powerhook` step, after `utilities`.
  - Low-voltage producers (wind turbines) get a low-voltage line to the nearest non-highway road node.
  - A transformer station and the high-voltage line to it are opt-in (mod setting "Add a transformer station next to each imported power plant", off by default). Otherwise the report names each plant that needs one.
- **Utility lines never touch road topology** (#145, #148): line ends stop 3 m short of road nodes and don't merge into them, highways aren't targets, and `utilities` and `powerhook` fail if any road edge's endpoints change.
- **Depots:** the report warns when a bus or tram depot type the plan has ends up with none placed, because its lines then get no vehicles (RC3). With #152, that depot is placed with the default depots' `nearby` fallback (behind the same opt-out).
- **Matcher 1.2.1:** CS1's Water Tower (with a groundwater intake) matches the CS2 groundwater pump (buildings #43).

## 0.3.3 (2026-11-21)

- **CS1's off-road power lines and pipes are rebuilt** (#115, #139): low-voltage lines and combined small pipes, with their ends snapped to building markers, road nodes or line nodes.
- **Pumps on dry land** slide toward water within 30 m, else become groundwater pumps, else are flagged.
- **Warnings** when no power plant or water facility was placed (#128).
- **Matcher 1.2.0:** CS1 water towers map to the groundwater pump.

## 0.3.2 (2026-11-21)

- **Station platforms and stairs are built** (#120): elevated metro stations get their tracks; 0 of 1630 sub-net courses missing.
- "Place it yourself" lines in the report for every building not placed.

## 0.3.1 (2026-11-21)

- **Buildings are never moved by default** (#106). Stations are placed only at their CS1 spot.
- Station matching by platform length; elevated CS1 stations get elevated CS2 stations.

## 0.3.0 (2026-11-21, M3)

- CS2 prefab catalogue, CS1 → CS2 building matching, placement on the imported roads; trees, rocks, districts with names and policies; placed buildings get their internal networks.
