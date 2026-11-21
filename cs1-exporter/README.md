# cs1-exporter

A Cities: Skylines 1 mod that exports the loaded city to a `.cs1x` file: the road network, NetInfos (with variant parents), IMT fills, terrain and water, zoning, transit lines, TM:PE and Node Controller settings, and (for M3) buildings, props and trees. The format is in [`schema/README.md`](../schema/README.md) §1. Decisions taken while building it are in [`DECISIONS-exporter.md`](DECISIONS-exporter.md).

The export only reads game state. It never saves the city.

## Build and deploy

```powershell
dotnet build cs1-exporter -c Release
```

- It builds against the game's own DLLs. `CS1Managed` comes from `local.props` at the repo root (see `local.props.example`).
- Release copies `Cs1Exporter.dll` to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\Cs1Exporter\`.
- CS1 keeps new local mods disabled. Enable it in Content Manager, or run this once with the game closed:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools\cs1-bridge\enable-mod.ps1 -ModName Cs1Exporter -Write
  ```

## Use

- **In game:** Options → Mod Settings → *CS1 → CS2 Exporter* → **Export city**. It writes `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Cs1Exporter\<city>-<yyyyMMdd-HHmmss>.cs1x` and shows a summary.
- **By script**, through the CS1 bridge. The DLL doesn't need to be enabled as a mod for this:

  ```powershell
  node tools/gamectl/bin/gamectl.js cs1 exec cs1-exporter/bin/Release/net35/Cs1Exporter.dll --type Cs1Exporter.Exporter --args '{"path":"%LOCALAPPDATA%/cs1-to-cs2/samples/skibidi.cs1x"}' --timeout 300
  ```

  - Use forward slashes in `path`: backslashes get mangled on the way through the shell into JSON.
  - Without `path` it writes to the default folder above.
  - The result is a JSON summary: path, bytes, counts, section statuses and the report items.
  - `--type Cs1Exporter.Mod --method ExportWithDialog` runs the button handler instead, with the dialog.
  - Takes about 5 s on Skibidi.02.
  - `path` may use environment variables (`%LOCALAPPDATA%`).
- **Check a file:**

  ```powershell
  node cs1-exporter/tools/inspect-cs1x.mjs %LOCALAPPDATA%\cs1-to-cs2\samples\skibidi.cs1x --expect nodes=7436,segments=8247,imt=66,netinfosMin=246
  ```

  This reads the zip with `node:zlib` (no dependencies) and checks the CRCs, the JSON, the cross references (segment → node, node → segment, NetInfo → parent and variants, IMT fill → node or segment) and the terrain orientation against node heights. `--json` gives machine-readable output. The exit code is 0 when every check passes.

## What's in a `.cs1x`

| Entry | Source |
| --- | --- |
| `manifest.json` | city, save, game version, enabled mods, detected converter-relevant mods (IMT, TM:PE, Node Controller, 81 Tiles), unlocked tiles, section statuses |
| `network.json` | `NetManager` nodes and segments, with the game's own bezier middle points |
| `netinfos.json` | used NetInfos, the parents of used variants, and all variants; `parent` / `variantKind` / `variants` |
| `imt.json` | IMT fillers, read by reflection (the IMT API has no geometry) |
| `terrain.json` + `terrain.r16` | `TerrainManager.RawHeights`: 1081², 17 280 m, ×1/64 m |
| `zones.json` | every `ZoneBlock`: position, angle, rows, road segment, per-cell zone code and occupied flag |
| `water.json` + `water.r16` | sea level, water sources, water depth grid on the terrain lattice |
| `transit.json` | transport lines: type, colour, name, ordered stops with segment and station |
| `tmpe.json` | TM:PE customisations from its own `SaveData` (arrows, connections, speed limits, vehicle and junction restrictions, priority signs, parking, light toggles, timed lights) |
| `nodecontroller.json` | Node Controller node types and per-segment-end settings |
| `buildings.json` | every building: prefab, position, angle, level, service, size, growable, workshop id |
| `props.json`, `trees.json` | free-standing props and trees, compact rows |
| `report.json` | info, warnings and errors per section |

Each section is isolated. A section that throws becomes `"failed"` in the manifest plus an `error` item in the report, and the other sections are still written. `tmpe` and `nodecontroller` are `"absent"` when their mod isn't loaded.

## Files

| File | What |
| --- | --- |
| `src/Mod.cs` | `IUserMod` with the Options button |
| `src/Exporter.cs` | orchestration, section isolation, the manifest, the `Run(args)` exec entry point |
| `src/NetworkExporter.cs` | `network.json` and `netinfos.json` (variant → parent map) |
| `src/ImtReader.cs` | IMT fillers by reflection (from the M0 dumper) |
| `src/TerrainExporter.cs` | heightmap capture and orientation checks |
| `src/GameInfo.cs` | city, save, mods, unlocked tiles |
| `src/ZonesExporter.cs` | `zones.json` (cell layout and geometry check) |
| `src/WaterExporter.cs` | `water.json` + `water.r16` |
| `src/TransitExporter.cs` | `transit.json` |
| `src/TmpeExporter.cs` | `tmpe.json` through TM:PE's own `SaveData`, by reflection |
| `src/NodeControllerExporter.cs` | `nodecontroller.json` by reflection |
| `src/BuildingsExporter.cs` | `buildings.json`, `props.json`, `trees.json` |
| `src/Refl.cs` | reflection helpers, generic data writer, lane id → segment/index/node map |
| `src/ZipWriter.cs` | zip writer for .NET 3.5 (local headers, central directory, CRC-32, `DeflateStream`) |
| `src/Json.cs` | streaming JSON writer (from the M0 dumper) |
| `tools/inspect-cs1x.mjs` | Node summary and sanity check |
