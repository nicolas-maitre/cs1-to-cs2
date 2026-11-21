# Changelog

The CS1 → CS2 Importer, a Cities: Skylines II mod (`Cs2ImporterMod`). The version is `Mod.Version` in `mod/Mod.cs` (the `<Version>` in `mod/Cs2ImporterMod.csproj` must match; `node tools/release/build.mjs --check` verifies both).

## 0.3.3 (2026-11-21)

- **Road heights follow CS1** (DECISIONS.md #110–113; DECISIONS-importer.md #57). Packages from web tool 0.7.0 and later shape the ground to the roads and give each road type a height mode:
  - `conform` (zoneable roads): the ground follows the road;
  - `walls` (highways, ramps, roads without zoning): the road keeps its CS1 height with retaining walls or embankments;
  - `follow` (paths): the road lies on the ground.

  The importer places each road accordingly, keeps CS1's vertical profile along it, and keeps elevated, bridge and tunnel ends at their CS1 height where the ground was reshaped under them.

  Ground roads more than 2 m off their CS1 height:

  | City | Before | After |
  | --- | --- | --- |
  | Skibidi | 408 of 1870 | 5 |
  | CacaoCity | 44 of 668 | 6 |
  | Westfield | 90 of 579 | 0 |

  Roads built, lane connections, lane directions and buildings are unchanged.
- **CS1's road kind decides what CS2 builds** (#61, #129): a CS1 ground road is never turned into a tunnel or put on pillars. Beyond CS2's wall limits it gets its height from the package's ground (web 0.9.3 walls burn), else it stays at the limit and is listed in the report. Verify counts tunnels and elevated roads by CS1 kind. *In-game check pending.*
- **CS1 tunnels stay tunnels** (#58, #124): CS1 tunnels shallower than CS2's minimum tunnel depth are built deeper (the deep end to 12.5 m, the portal to 8.5 m at most), and each one is listed in the report. On a Skibidi crop, 21 tunnel segments that came out as open cuts are now all tunnels.
- **Walls roads over a dip** (#62): a walls road whose CS1 height leaves the ground line by more than 1.5 m is split there, with a node at its CS1 height. *In-game check pending.*
- **Lane connections:** an independent check (#60, #126). Each junction arm's direction in CS2 must match CS1's. A junction where an arm doesn't match gets no connections written and is listed in the report. *In-game numbers pending.*
- **Node Controller shifts** (web 0.9.0+, `offNode`): off by default until verified in game (#59); road ends snap to their nodes, as before.
- **For tests:**
  - op `heights`: the height error per segment;
  - op `laneview`: the Traffic mod's lane connection view on one junction.

## 0.3.2 (2026-11-21)

- **Vanilla CS2 roads:** packages from web tool 0.6.0 use the game's own roads where they match the CS1 road (same driving lanes per side, widths and sidewalks), and Road Builder roads only where nothing matches (DECISIONS.md #100). The import treats both kinds alike: traffic settings, priority signs, lane connections, zoning, the left-hand reversal and verify. Measured with vanilla-road packages (DECISIONS-importer.md #56):
  - Skibidi, 7 road types vanilla: 2035/2035 roads; lane connections 162 of 176 as in CS1 (every junction lane exact), the same after save/load;
  - Westfield, 5 vanilla: 648/648 roads, 88 of 92 (95.7 %);
  - CacaoCity, left-hand, 8 vanilla: 684/684 roads, 206 of 208 (99 %).
- **For the web tool:** op `roadlayouts` (exec library) writes `cs2-roads.json`, the lane layout of every vanilla road and highway (lanes left → right with type, direction, width and offset; sidewalks, median, speed, zoning). The web tool matches CS1 roads against it.

## 0.3.1 (2026-11-21)

- **Fix: left-hand cities.** In a city that drove on the left in CS1, one-way roads were built running backwards, and asymmetric roads mirrored. Roads of a left-hand plan (`plan.source.leftHandTraffic`) are now built end → start, which is exactly CS1's layout. CacaoCity: 141 of 141 one-way and 16 of 16 asymmetric roads as in CS1 (DECISIONS-importer.md #55).
- **Driving side:** Prepare map sets the new city's driving side to the CS1 city's ("Traffic drives on the left, as in the CS1 city."). Check warns when the loaded city drives on the other side. Needs a package from exporter 1.2.1 or later; older packages build as before.
- **Lane connections on by default** ("Lane connections (Traffic mod)"), in right-hand and left-hand cities alike:
  - TM:PE lane connections and lane arrows are written into the Traffic mod's data, exactly at junctions.
  - Where a road just continues into another, CS2 keeps its own lane continuation, which already matches CS1 in most cases.
  - Skibidi: 162 of 176 lanes as in CS1 (92 %; every junction lane exact); CacaoCity: 125 of 127 (98.4 %). They persist through save and load.
  - For older plans without the driving side, a left-hand city is recognised from its lanes.
- **U-turn rules** are applied as lane connections (the Traffic mod has no U-turn setting): bans remove the U-turn lane, allowed U-turns get one where CS2 has none.
- **Status line:** "Lane connections: N connections at a of b junctions; x of y CS1 lane references matched."

## 0.3.0 (2026-11-21, release candidate)

- **Packages:**
  - A Browse… button opens Windows' file dialog for a package folder's `cs1conv-plan.json` or a package `.zip`.
  - A zip is unpacked under `%LOCALAPPDATA%\cs1-to-cs2\packages\`.
  - Pasted paths still work, with or without quotes, for a folder, its plan file or a zip.
  - The last five packages are remembered at the top of "Packages on this PC".
- **Clearer errors:**
  - Road Builder isn't loaded, or its configs folder doesn't exist yet (with what to install and when to restart).
  - A package from a newer web tool (format version), or a file that isn't a package.
  - Importing a package with terrain into a city whose terrain differs from it: "press Prepare map in a new city first". "Keep this map's terrain" imports anyway.
- **Cancel:** stops Prepare map or Import between steps or road batches. What's built so far stays and can be removed.
- **Remove imported:** deletes everything the last import in this city created: roads, buildings, trees and props, districts, surface areas, transit lines and stops, outside connections. It uses a list of what the import added, kept per city under `%LOCALAPPDATA%\cs1-to-cs2\import-reports\`, so it still works after saving and reloading.
- **French:** the Options page in French (`LocaleFR`), with the web tool's French terms.
- **Version** in the Options title. Paradox Mods packaging: `Properties/PublishConfiguration.xml` and a thumbnail.
- **Traffic mod lane connections and lane arrows (experimental, off by default):** "Lane connections (Traffic mod)" writes them in the Traffic mod's own data. On the Skibidi 3 km crop, 90 of 94 junctions got 429 connections, and 117 of 156 lanes lead exactly where they did in CS1.

## 0.2.0 (2026-11-21, M2)

- **The mod:** an Options page (package, Check, Install Road Builder configs, Import, live status) and a ToolUpdate import system.
- **Prepare map:** in a new city, it removes the base map's roads and buildings and applies the package's terrain, world map, sea level and CS1 water sources, then checks them.
- **After the roads,** each step behind its own toggle:
  - zoning;
  - traffic lights, all-way stops, turn bans and crosswalks;
  - Traffic mod priority signs;
  - buildings, trees and props, districts and surfaces (the buildings steps);
  - transit stops and lines;
  - outside connections.
- **Verify** follows CS2's own forks. A clearance pass keeps CS1's grade separations.
- **Embankments** snap to the terrain, so buildings can connect to those roads.

## 0.1.0 (2026-11-21, M1)

- **The exec library** (`Cs2Importer.dll`, driven by scripts through the CS2 bridge): it checks a plan, clears the area, builds the roads in spatial batches and verifies them against the plan (2035 of 2035 on the Skibidi 3 km crop).
