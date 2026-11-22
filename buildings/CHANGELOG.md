# Changelog

The buildings, objects, districts and utilities steps (`buildings/`), compiled into the CS2 importer mod. The version is `Steps.Version` in `src/Steps.cs`; the matcher (`match/match.mjs`, shared with the web tool) has its own version. Players see these changes through the importer's changelog. Decision numbers refer to [docs/DECISIONS.md](../docs/DECISIONS.md) unless marked "buildings #".

## 0.4.0 (2026-11-22)

- **Power plants join the grid** (#150, buildings #44–45): a new `powerhook` step, after `utilities`.
  - Low-voltage producers (wind turbines) get a low-voltage line to the nearest non-highway road node.
  - A transformer station and the high-voltage line to it are opt-in (mod setting "Add a transformer station next to each imported power plant", off by default). Otherwise the report names each plant that needs one.
- **Utility lines never touch road topology** (#145, #148): line ends stop 3 m short of road nodes and don't merge into them, highways aren't targets, and `utilities` and `powerhook` fail if any road edge's endpoints change.
- **Bus and tram depots** (#152, buildings #46): after the normal pass, a bus or tram depot kind with none placed retries one CS1 depot with the default depots' `nearby` fallback, so its lines have a depot. Opt-out `depotFallback` (default on); the report gives `depotsMoved`, `depotRetries` and a reason note. Rail and subway yards and other buildings never move. Skibidi: the Bus Depot is placed 117 m away.
- **Buildings are placed through the game's own apply** (#166, #168, buildings #47): Temp definitions plus the tool's Apply, in batches, instead of Permanent definitions. A depot built the Permanent way wasn't joined to the street, so it never sent out a vehicle. The placer writes only visible sub-nets, protects imported two-edge road nodes from CS2's node reduction during the apply, and keeps a road guard. On Skibidi, 6 of 8 imported bus lines run 23 buses; the other 2 wait for vehicles, because the CS1 city's one bus depot has 24 buses, all out.
- **Depot warning:** the report warns when a depot type the plan has still ends up with none placed, because its lines then have no depot (RC3).
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
