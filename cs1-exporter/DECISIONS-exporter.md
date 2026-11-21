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

---

# M2 (2026-11-21, 04:45–05:30)

All taken by Claude while the user was asleep; review them. The new sections are specified in `schema/README.md` §1.

## Decisions

14. **CS1 autosave guard: keep the autosave coroutine stopped, don't count pauses** (`tools/cs1-bridge/AutosaveGuard.cs`).
    - **How CS1 autosaves** (by IL inspection): `LoadingManager.AutoSaveTimer.AutoSaveRoutine` is the only caller of `SavePanel.AutoSave`.
    - **First try:** one extra `AutoSaveTimer.Pause()`. It failed on the real game: an autosave still went through about 2 min after a guarded load. The game's load and save panels call `UnPause()` when they're hidden even if they never paused, which clamps the counter back to 0.
    - **Now:** every frame, if the guard is on and the timer's private `m_Running` is set, the bridge calls the public `AutoSaveTimer.Stop()`. Releasing the guard calls `Start()`, which re-reads the user's settings and restarts the interval, so lifting the guard never autosaves at once.
    - **In memory only:** nothing is written to the settings.
    - **When it's on:** set by `CS1TOCS2_GAMECTL=1` (gamectl launches), `save.load` and `autosave.set`. It's released on mod unload.
    - **`state.autosave_blocked`** reports it.

15. **TM:PE is read through its own `SaveData` methods** rather than walking its internals or calling the public API for every lane.
    - **Why:** each manager's `ICustomDataManager<List<Configuration.X>>.SaveData(ref bool)` returns exactly the records TM:PE writes into the savegame: only what the user customised, in a stable persisted format. That's what a converter needs (defaults come from the game).
    - **Safety:** checked by IL that these methods only read state and build lists.
    - **Reflection only:** no compile-time reference to TrafficManager.dll or TMPE.API.dll, so a missing or changed TM:PE never breaks the build or the load.
    - **Isolation:** each part (arrows, connections, …) fails on its own, and `tmpe.json.parts` lists the status of each.
    - **Lane records are resolved** to `segment`, `laneIndex` and the `node` the lane flows into, so readers never need CS1 lane ids, which aren't stable across saves.
    - **Lane arrows** come from `LaneArrowManager.GetFinalLaneArrows`, TM:PE's own final arrows.
    - **"Unlimited" speed** (`SpeedValue.UNLIMITED` = 20 game units) shows up as 1000 km/h. It's flagged with `unlimited: true`.
    - **Manual traffic lights** aren't persisted by TM:PE, so they're not exported. Timed lights are TM:PE's own record, written field by field.

16. **Node Controller: a fixed list of user-settable properties per node and per segment end**, read by reflection from `ModsCommon.SingletonManager<NodeController.Manager>.Instance.Buffer`.
    - Derived geometry and UI state are left out.
    - `ModsCommon` is taken from the Node Controller assembly, because every mod by this author ships its own copy.
    - Node Controller holds data for every node it has touched, not only user edits: 1179 nodes on Skibidi. They're all exported; `type` vs `defaultType` shows the user's type changes.

17. **Zones: one record per `ZoneBlock`**, with the cells as two short strings (`zones`: one hex zone code per cell or `-` for an invalid cell; `occupied`: 0/1).
    - Why: compact (1.7 MB → 0.35 MB zipped for 10 288 blocks) and easy to index.
    - **Cell centre formula:** from the game's own math. The exporter re-checks it on every export by measuring each column's distance from the block's road. Column 0 is the cell next to the road: 13.7 m from the road centre line on Skibidi, then +8 m per column.

18. **Water depth grid on the terrain lattice** (`water.r16`, 1081², same scale and row order as `terrain.r16`). It's sampled as `SampleRawHeightWithWater − SampleRawHeightSmooth`.
    - Why: the terrain session can treat it like a second heightmap. It's a snapshot of the simulated water, not of the water sources' targets; the sources are exported separately with `targetHeight`.
    - The grid takes about 1 s.

19. **Transit stops:** each stop's lane (`NetNode.m_lane`) gives `segment` / `laneIndex` / `laneOffset`. For station types, the station building is the nearest building of the line's station service and sub-service within 64 m (`BuildingManager.FindBuilding`).

20. **Buildings, props, trees exported now (M3 data):**
    - **Buildings** are full objects (11 741). `angle` is in degrees (the schema rule) and `level` is 1-based.
    - **Props and trees** are compact rows `[infoIndex, x, y, z, …]`, because there can be hundreds of thousands. Only free-standing instances are included; building and network props come with their prefabs.
    - **No limit-raising mods:** Skibidi has no EML / unlimited-trees mod, so the vanilla buffers are complete. With such a mod the vanilla arrays could be partial; see the open issues.

21. **`Exporter.Run` expands environment variables in `path`** (`%LOCALAPPDATA%/…`), so the documented command works from any shell.

22. **A shared `Refl` helper and `LaneMap`** (lane id → segment, index, flow node) are used by TM:PE and transit. `Refl.WriteData` writes any plain object (lists, dictionaries, nullables, enums by name) for TM:PE's timed-light records.

## Results (Skibidi.02, 2026-11-21 05:15)

- **Export:** 5.4 MB in 4–5 s, every section `ok`, report 0 errors / 0 warnings. `inspect-cs1x.mjs`: 136/136 checks pass. The web tool's `cs1conv inspect` reads it.
- **zones:** 10 288 blocks, 148 555 valid cells, 122 119 zoned. ResidentialLow 65 370, CommercialLow 30 746, Unzoned 26 436, Industrial 12 504, Office 6608, ResidentialHigh 4389, CommercialHigh 2502. 123 246 cells occupied.
- **water:** sea level 181.98 m. 40 sources: 3 natural, 37 facility. 134 043 wet samples, max depth 82.5 m.
- **transit:** 21 lines (17 bus, 3 metro, 1 train), 345 stops. Metro stops resolve to station buildings.
- **tmpe** (TM:PE 11.9.4):
  - 674 lane arrows, 928 lane connections;
  - 2286 lane speed limits (695 of them "unlimited") and 22 custom default speeds;
  - 30 vehicle restrictions (all bus lanes: Bus + Emergency);
  - 227 junction restriction ends, 625 priority signs, 4 parking restrictions;
  - 0 light toggles, 37 timed traffic lights.
- **nodecontroller** (3.5.4.1): 1179 nodes and 2919 segment ends. Types: Custom 557, Middle 526, Bend 78, Crossing 14, End 4.
- **buildings:** 11 741 (10 328 growable, 7 workshop). **props:** 539 (8 types). **trees:** 41 777 (4 types).
- **Autosave guard**, verified on the running game (the user's autosave interval is about 1 min):
  - **Pause-based first version:** autosaves still went through at 05:12:02 and 05:13:13. That's why it was replaced.
  - **Stop-based guard:** active from about 05:14:00. No AutoSave was written for 4 min 20 s, about 4 intervals; `state.autosave_blocked` was true.
  - **Release:** `autosave.set {"enabled":true}` at 05:18:22. The next AutoSave came one full interval later (05:19:23), not straight away.
  - **Block again:** `autosave.set {"enabled":false}` stopped the timer again.
  - **Mod reload:** a hot reload of the bridge mod released the guard and then re-armed it from the gamectl environment variable.
  - **The AutoSave.crp overwritten in these tests** was the test session's own (from 04:33), not the user's.

## Open issues (M2)

- **Props and trees with limit mods:** with Extended Managers Library or an unlimited-trees mod, the vanilla `PropManager` / `TreeManager` buffers may not hold everything. Skibidi doesn't use one; if a later city does, read the mod's buffer.
- **Node Controller exports every node it tracks.** A consumer that wants only real user edits should compare with defaults (`type` vs `defaultType`, offsets vs the defaults). Telling them apart reliably would need Node Controller's own "is default" logic.
- **`water.r16` is a simulation snapshot.** On a map where water is still filling, it shows the current state. Use the sources' `targetHeight` and the sea level for the intended water.
- **Transit station lookup is approximate:** nearest building of the right service within 64 m. That's fine for metro and train; for unusual multi-station hubs it could pick a neighbour.
- **The M1 open issue about the AutoSave overwrite is fixed** by decision 14.
- **Water is now exported**, which resolves the M1 open issue.

---

# M3 / release (2026-11-21, 05:25–06:00)

All taken by Claude while the user was asleep; review them. Exporter version **1.2.0**.

## Decisions

23. **Districts as byte rasters, not polygons.**
    - **What:** `districts.u8` and `parks.u8` hold one id per cell, row 0 = minimum z, as for the terrain. For each cell the exporter keeps the district with the highest blend weight (CS1 blends up to four).
    - **Why:** cheap, exact and lossless; a reader can vectorise them if it wants polygons.
    - **Grid size:** the resolution comes from the array length. On Skibidi, 81 Tiles 2 enlarges the grid from 512² to **900² × 19.2 m = 17 280 m**. The export handles that without special code.
    - **Checked in game:** I painted a temporary asymmetric L-shaped district and a park area into the loaded city's in-memory grids with a scratch exec probe, exported, and removed them again (0 non-empty cells left). The city was never saved: autosave was blocked and quit doesn't save.
      - The raster matched the painted cells exactly (480 cells, columns 300–329, rows 600–639).
      - The game's exact cell lookup (`GetDistrict` / `GetPark(Vector3)`) agreed at 400/400 and 240/240 cell centres.
      - That lookup is now the built-in check on every export. `SampleDistrict` blends neighbouring cells and disagreed on area edges, so it isn't used.
    - **Policies:** all `DistrictPolicies.Policies` values for which `IsPolicySet` is true, by name. District 0 gives the city-wide policies.

24. **`assets.json` is a catalogue per prefab, built from the live instances.**
    - **Fields:** count, title, workshop id, category, zone, footprint, size and height, and a small fixed tag vocabulary.
    - **Building AI settings:** every primitive public field of the building's AI, as `aiFields`. CS1 keeps service-building specifics there (capacities, radii, workers, vehicles, costs), so this covers "capacity or radius where cheap" generically, without a per-AI list that would go stale.
    - **Networks:** the networks in the catalogue count segments.

25. **Buildings get `accessSegment`** (CS1 `Building.m_accessSegment`, the road the building is connected to) **and `district` / `park`** from the game's own position lookup.

26. **The export streams each section straight into its zip entry.**
    - **Before:** each section was a full byte array (about 27 MB for Skibidi, more for big cities).
    - **How:** the zip writer deflates as the JSON is written, counts the CRC and size, then seeks back and patches the local header.
    - **Failed sections:** a section that fails half-way is truncated off the file (`EndEntry(keep: false)`), so the archive never holds partial JSON. The M1 spec's "may be partial" no longer happens for single-entry sections.
    - **Empty entries are stored:** a strict inflater (Node's) rejects a deflate stream with no block.

27. **The UI export runs one section per frame, with the simulation paused.**
    - **Status:** the status line shows "Exporting… n/15: section".
    - **Pausing:** the simulation is paused for the export (and two settle frames), then restored to its previous state. Reading while the simulation thread writes could see half-updated objects.
    - **exec path:** `Exporter.Run` drains the same routine in one call without pausing. gamectl-loaded cities are paused anyway.
    - **Fatal problems** (no city, unwritable folder) show an error dialog and leave no `.tmp` behind.

28. **Settings live in the mod's own `Cs1Exporter.cgs`** (ColossalFramework `SettingsFile`): export folder, web tool address, and one switch per optional section. No other game setting is touched, and the user's mod enablement isn't changed.
    - **The folder field wins:** what the field shows is what *Export city* uses and saves.

29. **The web tool address is a setting, defaulting to `http://localhost:5178`** (the web tool's dev server). No hosted version exists yet; when one does, only the default needs to change. The result dialog names the address as the next step, and *Open web tool* opens it on request only.

30. **The missing-mod test hook hides types at the lookup itself.**
    - **What:** `simulateMissing` adds namespaces to `Refl.HiddenNamespaces`. `Refl.FindType` and IMT's assembly scan treat those types as absent, so the readers' real "not installed" code runs, not a shortcut.
    - **Checked:**
      - With IMT, TM:PE, Node Controller and 81 Tiles "missing", all three sections came out `absent` through their normal messages (IMT's own "NodeMarkingManager not found"), and the export was otherwise complete.
      - The next normal export read all three again.
      - `simulateFailure` on zones, districts and assets gave `failed` sections, removed from the zip, with every other section intact.
    - **Never in real files:** test exports carry a `test` block in the manifest.

31. **Versioning:**
    - **Release version:** `ModInfo.Version` (1.2.0) is shown in the mod name and written as `manifest.exporter.version`.
    - **Build:** the assembly version stays `1.0.*` (CS1 hot reload needs a new one per build) and is written as `exporter.build`.
    - **Changelog numbering:** M1 = 1.0, M2 = 1.1, M3 = 1.2.

32. **Workshop packaging, not published.**
    - **Preview image:** `workshop/PreviewImage.png`, 644 × 360, generated by `workshop/make-preview.ps1` with System.Drawing (road-grid motif, title, feature line). The build copies it next to the DLL, where CS1's uploader looks for it.
    - **Texts:** `workshop/description.txt` (Steam BBCode) and `CHANGELOG.md`.
    - **Publishing** is the user's call.

## Results (Skibidi.02, 2026-11-21 05:53)

- **Final sample** (`%LOCALAPPDATA%\cs1-to-cs2\samples\skibidi.cs1x`):
  - 5.5 MB, 5.0 s, all 14 sections `ok`, report 0 errors / 0 warnings;
  - `inspect-cs1x.mjs` 162/162 checks;
  - the web tool's `cs1conv inspect` reads it;
  - a scan of all its JSON finds no user name or local path.
- **districts:** 0 districts and 0 park areas in Skibidi (none were ever drawn); grid 900². The raster logic was proven with the temporary test district (decision 23).
- **assets:** 553 prefab types in use:
  - 423 building, 8 prop, 4 tree and 118 network types; 22 of them from the workshop;
  - building tags: growable 335, ploppable 88, service 88, park 20, station 14, landmark 3, depot 3, subBuilding 2.
- **buildings:** 10 549 of 11 741 have a road connection (`accessSegment`).
- **UI**, driven through the real Options panel (opened with `OptionsMainPanel.SelectMod`, button clicked with `SimulateClick` from a scratch probe), screenshots checked:
  - **Options page:** title with the version, Export button, status line, folder and web tool fields, the ten section switches and the About text.
  - **While exporting:** status "Exporting… 7/15: districts", Export button disabled.
  - **Result dialog:** size, time, path, key counts, parts exported, next step.
  - **Error dialog** (folder on a missing drive): a red icon, "Can't create the export folder Q:\no-such-drive\exports …", what to do; no file and no `.tmp` written.
  - **Restored afterwards:** the export folder setting was reset to the default, and the UI test exports were deleted from the user's export folder.
- **Game:** CS1 closed, `cs1.lock` released. Skibidi.02 is untouched (Steam Cloud file still 04:06:05). AutoSave.crp is unchanged since 05:19 (the guard held through this session and the e2e run).

## Open issues (M3)

- **Skibidi has no districts or park areas**, so the real-data district path is only covered by the synthetic test district. A city with real districts would be a good extra check, including how 81 Tiles 2 blends cells near district edges.
- **The building `district` uses the game's `GetDistrict`**, which reads the cell's first slot. For a cell where two districts blend, it can differ from the raster's highest-weight id; the difference is cosmetic at district borders.
- **The result dialog uses CS1's ExceptionPanel**, which always shows a warning triangle for non-errors. The game has no neutral message box without extra UI code.
- **There's no public web tool address yet.** The default is the local dev server.
- **`assets.json` titles** for workshop assets are the in-game titles. Steam Workshop item names (which can differ) would need the Steam API and aren't fetched.

33. **Traffic handedness in the manifest (1.2.1):** `manifest.traffic.leftHand` from `SimulationManager.m_metaData.m_invertTraffic`.
    - **Why:** in a left-hand-traffic city, lanes are mirrored relative to the segment direction, so CS1 lane indices point at the wrong CS2 lanes. The importer matched 0 of 265 lane connections on CacaoCity (QA §6, A5) until it guessed the handedness from inverted segments. The game knows it; the export should say it.
    - **Verified:** CacaoCity re-export → `true`. Older exports lack the field, so readers treat it as unknown and keep their own detection.
    - **Also in 1.2.1:** the zoning self-check reports the median (QA Q2). Patch bump: the format only gains a field.
