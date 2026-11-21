# CS1 → CS2 Exporter

A Cities: Skylines mod that exports your city to a single `.cs1x` file, so it can be rebuilt in Cities: Skylines II with the CS1 → CS2 converter.

The mod only **reads** your city. It never saves or changes it.

Version 1.2.0. What changed is in [CHANGELOG.md](CHANGELOG.md); the file format is in [`schema/README.md`](../schema/README.md) §1.

## Install

**From the Workshop** (once it's published): subscribe, then enable *CS1 → CS2 Exporter* in Content Manager → Mods.

**From source:**

```powershell
dotnet build cs1-exporter -c Release
```

- This copies `Cs1Exporter.dll` and `PreviewImage.png` to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\Cs1Exporter\`. It builds against your game's DLLs; set `CS1Managed` in `local.props` at the repo root (see `local.props.example`).
- CS1 keeps new local mods disabled. Enable it in Content Manager, or run this once with the game closed:

  ```powershell
  powershell -ExecutionPolicy Bypass -File tools\cs1-bridge\enable-mod.ps1 -ModName Cs1Exporter -Write
  ```

No other mod is required.

## Use

1. Load your city.
2. Open **Options → Mod Settings → CS1 → CS2 Exporter 1.2.0** and press **Export city**.
3. The status line under the button shows the progress. The simulation is paused for those few seconds and resumes afterwards.
4. A dialog shows where the file was written and a short summary.
5. Open the CS1 → CS2 web tool and drop the file on it. *Open web tool* in the options opens it; the address is a setting and defaults to the hosted tool, https://nicolas-maitre.github.io/cs1-to-cs2/.

### Options

| Setting | What it does |
| --- | --- |
| Export folder | Where `.cs1x` files go. Default: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Cs1Exporter`. *Open export folder* shows it; *Use the default folder* resets it. |
| Web tool address | The page *Open web tool* opens. |
| Include in the export | Switches for the optional parts: zoning, water, districts, transport lines, TM:PE, Node Controller, buildings, asset catalogue, props and trees. Roads, network types, IMT markings and terrain are always included. |

The settings are stored in the mod's own `Cs1Exporter.cgs` in the CS1 user folder.

### When something goes wrong

- **The file can't be written** (folder missing or read-only): an error dialog says so, and nothing is written.
- **One part fails** (for example because another mod changed something): that part is left out, and the rest of the file is still written. The result dialog says how many parts failed. The details are in `report.json` inside the file and in `output_log.txt`.
- **A mod isn't installed** (Intersection Marking Tool, TM:PE, Node Controller): its part is skipped. Nothing else changes, and the mod is never required.

## What's in a `.cs1x`

A `.cs1x` is a zip of JSON files (plus a few binary grids):

| Part | Content |
| --- | --- |
| `network.json`, `netinfos.json` | roads, tracks, paths and pipes with exact curves and elevation; every network type with its lanes and variants |
| `imt.json` | Intersection Marking Tool fills (polygons and styles) |
| `terrain.json` + `terrain.r16` | the full 17 280 m heightmap |
| `water.json` + `water.r16` | sea level, water sources, water depth |
| `zones.json` | zone blocks with the zone and occupied state of every cell |
| `districts.json` + `districts.u8` / `parks.u8` | districts and park / industry / campus / airport areas as grids, with names and policies |
| `transit.json` | public transport lines with colour, name and ordered stops |
| `tmpe.json` | TM:PE settings: lane arrows, lane connections, speed limits, vehicle and junction restrictions, priority signs, parking, timed traffic lights |
| `nodecontroller.json` | Node Controller node types and per-segment-end settings |
| `buildings.json` | every building: prefab, position, angle, level, service, size, road connection, district |
| `assets.json` | a catalogue of every building, prop, tree and network prefab in use, with title, workshop id, category, footprint, height, tags and count |
| `props.json`, `trees.json` | free-standing props and trees |
| `manifest.json`, `report.json` | city name, game and mod versions, the status of each part, and anything worth knowing about the export |

On Skibidi.02 (7436 nodes, 8247 segments, 11 741 buildings, 41 777 trees) the export takes about 5 s and the file is 5.5 MB.

## Privacy

The file contains only data about the city: map, networks, buildings, zoning, districts, transport lines and the traffic mods' settings. It also lists the enabled mods (names and versions) and the game version. It contains no user name, no file paths of your computer and no account data. Nothing is uploaded anywhere; you decide where the file goes.

## For developers

**Scripted export** through the CS1 bridge (`tools/gamectl`). The DLL doesn't need to be enabled as a mod for this:

```powershell
node tools/gamectl/bin/gamectl.js cs1 exec cs1-exporter/bin/Release/net35/Cs1Exporter.dll --type Cs1Exporter.Exporter --args '{"path":"%LOCALAPPDATA%/cs1-to-cs2/samples/skibidi.cs1x"}' --timeout 300
```

- **`path`:** may use environment variables. Use forward slashes; backslashes get mangled on the way into JSON.
- **`exclude`:** an array of optional parts to leave out.
- **Test hooks:**
  - `simulateMissing` (`imt`, `tmpe`, `nodecontroller`, `eightyOneTiles`) hides those mods' types from the readers, so their real "not installed" paths run.
  - `simulateFailure` (any part name) makes that part throw.
  - A file made with these has a `test` block in its manifest.
- **The result** is a JSON summary: path, size, counts, section statuses and report items.
- **Lock:** before driving CS1, take `%LOCALAPPDATA%\cs1-to-cs2\cs1.lock` (JSON `{owner, session, since, time}`) and delete it after `cs1 stop`.

**Checking a file:**

```powershell
node cs1-exporter/tools/inspect-cs1x.mjs $env:LOCALAPPDATA\cs1-to-cs2\samples\skibidi.cs1x --expect nodes=7436,segments=8247,imt=66,netinfosMin=246
```

The script needs no dependencies; it reads the zip with `node:zlib`. It checks:
- the CRCs and the JSON;
- every cross reference (segments, nodes, NetInfos, IMT fills, zone blocks, transit stops, TM:PE records, district rasters, asset counts);
- the terrain orientation against node heights.

`--json` gives machine-readable output. The exit code is 0 when everything passes.

**Workshop packaging** (not uploaded; publishing is the owner's call):
- `workshop/PreviewImage.png` (644 × 360), regenerated by `workshop/make-preview.ps1`;
- `workshop/description.txt` (Steam BBCode);
- [CHANGELOG.md](CHANGELOG.md).

The build puts the preview image next to the DLL, where the Workshop uploader expects it.

| File | What |
| --- | --- |
| `src/Mod.cs` | `IUserMod`, settings, the options panel, dialogs and the frame-by-frame export runner |
| `src/Exporter.cs` | options, the export routine (one section per step, streamed into the zip), the manifest, `Run(args)` |
| `src/ZipWriter.cs` | streaming zip writer for .NET 3.5 (patched local headers, CRC-32, `DeflateStream`, entries can be dropped) |
| `src/NetworkExporter.cs` | `network.json`, `netinfos.json` |
| `src/ImtReader.cs`, `src/TmpeExporter.cs`, `src/NodeControllerExporter.cs` | other mods' data by reflection |
| `src/TerrainExporter.cs`, `src/WaterExporter.cs`, `src/ZonesExporter.cs`, `src/DistrictsExporter.cs`, `src/TransitExporter.cs` | map sections, each with an in-game geometry check |
| `src/BuildingsExporter.cs`, `src/AssetsExporter.cs` | buildings, props, trees, asset catalogue |
| `src/GameInfo.cs`, `src/Refl.cs`, `src/Json.cs`, `src/Report.cs` | helpers |
| `tools/inspect-cs1x.mjs` | the checker |
| `DECISIONS-exporter.md` | design decisions with reasons (M1, M2, M3) |
