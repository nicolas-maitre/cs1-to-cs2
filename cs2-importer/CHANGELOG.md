# Changelog

The CS1 → CS2 Importer, a Cities: Skylines II mod (`Cs2ImporterMod`). The version is `Mod.Version` in `mod/Mod.cs` (the `<Version>` in `mod/Cs2ImporterMod.csproj` must match; `node tools/release/build.mjs --check` verifies both).

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
