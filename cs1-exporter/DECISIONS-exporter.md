# Exporter decisions (M1, 2026-11-21)

All taken by Claude while the user was asleep; review them. To be merged into `docs/DECISIONS.md`.

1. **One DLL serves as both the mod and the `exec` library.** `Cs1Exporter.dll` is an `IUserMod` with an "Export city" button, and it also has `public static object Exporter.Run(Dictionary<string, object> args)`.
   - Why: both paths run the same code, so a script export is the same as a user export.
   - `Run` checks `LoadingManager.m_loadingComplete` and the tool mode itself, instead of a `LoadingExtension` flag, because an exec'd copy of the assembly is a separate load whose statics are never set.

2. **Hand-written zip writer, deflate with a stored fallback.** `ZipWriter` writes local headers, the data, the central directory and the end record, with CRC-32 and UTF-8 names.
   - Each entry is buffered in full first, so the sizes and CRC are known before its header is written. No data descriptors, no zip64; an export is about 15 MB.
   - Deflate uses `System.IO.Compression.DeflateStream`, which needs Mono's native `MonoPosixHelper.dll` (it ships in `Cities_Data\Mono`). If it throws anyway, the writer stores every following entry and adds a report warning, so the file is still valid.
   - An entry is also stored when deflate doesn't make it smaller (for example `terrain.r16` of a flat map).
   - Checked with `Expand-Archive` and `inspect-cs1x.mjs`.

3. **The file is written to `<path>.tmp` and then renamed.** Why: a crash or an exception during the write never leaves a half-written `.cs1x` that other tools might pick up.

4. **NetInfo closure:** used NetInfos, plus the parent of every used variant, plus every NetInfo-typed field of the AI of all of those (as M0 did).
   - **Parents** come from a scan of all loaded NetInfos (`PrefabCollection<NetInfo>`), mapping the four vanilla fields `m_elevatedInfo`, `m_bridgeInfo`, `m_slopeInfo` and `m_tunnelInfo` back to their owner. Self references are ignored.
   - **When several parents share a variant**, the exporter picks one used in the city first, then the first by name, so the choice is deterministic. The others go into `otherParents`.
   - **Extra fields:** a variant also gets `variantKind` (`elevated`, `bridge`, `slope` or `tunnel`). Every NetInfo gets `used` (true when a node or segment uses it). A ground NetInfo always has `variants`, which may be `{}`.
   - **Only the four vanilla fields** count for `variants` / `parent`. Other AI NetInfo refs (mod-specific) are still included in the file and listed by name in `aiFields`, but they aren't called variants.

5. **`elevationKind` comes from the variant relationship first, then the AI class name.**
   - Why: if a parent's `m_bridgeInfo` points to the NetInfo, it's a bridge, whatever a modded AI class is called. The AI class name (M0's method) is only the fallback.
   - The spec allows only `ground`, `elevated`, `bridge`, `slope` and `tunnel`, so M0's `underground` maps to `tunnel`, and a NetInfo with no AI is `ground`.

6. **Per-item isolation inside sections.** A node, segment, NetInfo or IMT filler that throws while being read is skipped with a report warning (the first 20 are listed, then a count), and the section stays `ok`.
   - Values are read before anything is written, so a throw never leaves broken JSON.
   - Only a failure of the section as a whole makes it `"failed"`.

7. **Terrain constants are read from the game, and the layout is verified twice.**
   - **Constants:** `RAW_RESOLUTION` (1080), `RAW_CELL_SIZE` (16) and `TERRAIN_HEIGHT` (1024) are read from `TerrainManager` at run time; they were also checked in `Assembly-CSharp` offline. That gives 1081 × 1081 samples over 17 280 m, and heightScale = 1024 / 65536 = 1/64. The spec values are only a fallback.
   - **In game:** at 400 random grid points, `TerrainManager.SampleRawHeight(x, z)` must equal `raw[row × 1081 + col] × 1/64`. The report also says how far off the transposed layout would be, which shows the check can tell the two apart.
   - **Offline:** `inspect-cs1x.mjs` compares `position.y − heightAboveTerrain` of every ground node with a bilinear sample of the grid, under the spec layout and under the transposed, X-flipped and Z-flipped layouts. The spec layout must be the best one.
   - **Results:** see "Results" below.

8. **TM:PE lane arrows are not in M1.** `tmpe` and `nodecontroller` are `"absent"`.
   - Why: the M1 importer builds plain roads and has nothing to apply lane arrows to.
   - TM:PE keeps its data per lane id, in internal managers that change between versions. A "cheap" reflection dump would need its own format, its own tests and a matching importer.
   - Better done once, properly, in M2 together with priority signs and junction restrictions. The manifest still records whether TM:PE and Node Controller are enabled, and their versions.

9. **Manifest extras** (allowed, because readers ignore unknown fields):
   - `mods[]` uses the assembly name as `name`, as the spec example does, and adds `title` (the Content Manager name) and `workshopId` (a string, since uint64 doesn't fit a JS number).
   - `detectedMods` gives `{imt, tmpe, nodeController, eightyOneTiles}` versions, or null.
   - `tiles` lists the unlocked `[col,row]` tiles of the 9 × 9 grid of 1920 m tiles, found with `GameAreaManager.PointOutOfArea` at each tile centre. 81 Tiles 2 patches that method, so it works with and without the mod. The web tool can use it to suggest a crop.

10. **`exportedAt` is local time with its offset** (`2026-11-21T05:12:00+01:00`), as in the spec example.

11. **The exporter mod is enabled in the user's CS1** (`enable-mod.ps1 -ModName Cs1Exporter -Write`, run with the game closed; the script backs up `userGameState.cgs`).
    - Why: the Options button can be used straight away, and the Skibidi.02 test runs with the exporter loaded next to IMT, TM:PE, Node Controller and 81 Tiles 2, which is the setup it has to survive.
    - The scripted export itself doesn't need the mod enabled.

12. **Started Steam (`steam.exe -silent`).** It wasn't running, and `gamectl` needs it to launch CS1.

13. **The in-game terrain probe tests several conventions instead of assuming one.**
    - `TerrainManager.SampleRawHeight(float, float)` takes **grid cells** and returns **raw units**, not world metres. The first run used world metres and was off by 42 km.
    - The probe now tries world/cell input × metre/raw output and needs the best match to equal the spec layout and not the transposed one. It also checks `SampleRawHeightSmooth(Vector3 world)` at exact grid points.

## Results (Skibidi.02, CS1 1.21.1-f9, 2026-11-21 04:31)

- **Export:** 3.5 MB in 1.5 s, deflate working: `network.json` 5.2 MB → 1.2 MB, `netinfos.json` 7.1 MB → 0.3 MB, `terrain.r16` 2.3 MB → 1.9 MB. `inspect-cs1x.mjs`: 72/72 checks pass.
- **Counts:** **7436 nodes, 8247 segments**, 250 NetInfos (118 used, 92 ground, 158 variants; 1 parent added that M0 lacked), **66 IMT fillers** (55 node, 11 segment; 64 `FillerSolid`, 2 `FillerPavement`).
  - **Why they differ from M0** (7009 / 7745): Skibidi.02 was re-saved at 2026-11-21 04:06 (with an AutoSave at 04:05), after the M0 dump (2026-11-20 17:04), presumably by the user playing.
  - **Diff against the M0 dump:** +552 / −50 segments (7745 + 552 − 50 = 8247). The new ones are mostly water pipes, tree roads, basic/medium roads and a Two Lane Highway with elevated, slope and tunnel parts. IMT is unchanged.
  - **The export matches the game:** in the same session the exporter's counts equal M0's filter applied to the live buffers (`Created` and not `Deleted`).
- **elevationKind:** ground 7602, elevated 442, tunnel 122, bridge 54, slope 27.
- **Terrain:** 1081², 17 280 m, ×1/64; min 118.2 m, max 819.9 m.
  - **Orientation in game:** exact, 0.000 m at 400 probes; the transposed layout would be off by 667 m.
  - **Against 6520 ground nodes:** median 0.011 m, p99 0.27 m, max 2.6 m (the game's smoothing on steep slopes). The transposed layout gives 56.7 m, flip-Z 21.9 m, flip-X 34.8 m. **The spec layout is confirmed.**
- **Mods:** 20 enabled. Detected IMT 1.15.0.0, TM:PE 11.9.4.25100, Node Controller 3.5.4.1, 81 Tiles (2) 1.0.5.0. 8 tiles unlocked. No exceptions with any of them.
- **The Options button path** (`Mod.ExportWithDialog`, run through exec) wrote `Skibidi-20261121-043345.cs1x` to the default folder and showed the summary dialog (screenshot `.gamectl/shots/cs1-20261121-043355-589.png`). That file also passes inspect.
- **Sample:** `%LOCALAPPDATA%\cs1-to-cs2\samples\skibidi.cs1x`.

## Open issues

- **CS1 overwrote its own `Saves\AutoSave.crp`** at 04:33 while Skibidi.02 was loaded for the export. The previous AutoSave was the user's own from 04:05, 41 s before their manual Skibidi.02 save, so nothing unique was lost.
  - Skibidi.02 itself is untouched: its Steam Cloud file still has 04:06:05.
  - The CS1 bridge has no autosave guard; only CS2's does. Worth adding one to `tools/cs1-bridge` (outside this folder).
- **Water is not exported** (`seaLevel` is null; water sources and the water level are M2).
- **The 81 Tiles version is the assembly version**, and the detection matches on "EightyOne" / "81 Tiles". If the user ever runs the original 81 Tiles mod instead, it's detected the same way.
