# Road Builder (CS2) config schema notes, for the CS1 to CS2 road converter

Sources:
- Source checkout: `scratchpad/src/RoadBuilder-CSII` at commit 5f4b1ab ("v0.8.0 and Editor UI", 2026-10-18). `RoadBuilder/RoadBuilder.csproj:8` has `<Version>0.8.0</Version>`.
- Installed build: `...\.cache\Mods\pdx_mods\87190_42\RoadBuilder.dll` reports **FileVersion 0.7.0** (ProductVersion `0.7.0+b9fbd0f8...`), file dated 2026-08-03. So the user runs **0.7.0** and the source is **0.8.0**. A string scan of the DLL shows 0.7.0 has all the same section names and option strings: `Sidewalk With Bikelane`, `Harbor Drive Section 4`, `Two-way Support`, `RB Parking Section Parallel NoMarking`, `Parking Lane 5.5 - Alley`, `RB Sound Barrier 1`, `CURRENT_VERSION`, `VER_CHANGE_SOUND_BARRIER`. It does **not** have the 0.8.0 helpers `GetPositivePlaysets`, `TryGetCombinationValue`, `SupportsDecoration` or `RestoreDeletedPrefab`, and it lacks the `-{playset}` exclusion logic. So these differ in 0.7.0: playset handling, case-insensitive option matching, and the "closest variant" fallback (see the caveats).
- Ground truth: all 29 files in `%USERPROFILE%AppDataocallow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\*.json` (28 RoadConfig, 1 TrackConfig, all Version 5). They are summarised in section 7.

All `file:line` references below are relative to `src/RoadBuilder-CSII/RoadBuilder/`.

---

## 1. Config JSON schema (Version 5)

### 1.1 Top-level fields
`Domain/Configurations/RoadConfig.cs:15-27` (Track is identical, `TrackConfig.cs`. Path and Fence have **no** `SpeedLimit`):

| Field | Type | Notes |
|---|---|---|
| `Type` | string | **Required.** Exactly `"RoadConfig"`, `"TrackConfig"`, `"PathConfig"` or `"FenceConfig"`. The loader switches on it and skips anything else (`Utilities/LocalSaveUtil.cs:113-131`). |
| `Version` | ushort | Use **5**, which is `CURRENT_VERSION` (`Systems/RoadBuilderSerializeSystem.cs:28-34`). A lower or missing value (0) triggers old migrations in `ApplyVersionChanges` (`RoadConfig.cs:97-134`): v<3 forces `HasUndergroundElectricityCable` and renames sections, v<4 strips the obsolete RaisedSidewalk category and sets `Playsets ??= []`, v<5 renames the sound barrier. |
| `OriginalID` | string | Set it equal to `ID`. On save, if `OriginalID != ID`, RB deletes `{OriginalID}.json` (`LocalSaveUtil.cs:23-54`). |
| `ID` | string | Unique key, also the **prefab name** and the file name `{ID}.json`. See 1.4. |
| `Name` | string | Display name, free text. |
| `PillarPrefabName` | string | Name of any `StaticObjectPrefab` with `PillarData`. All pillar prefabs are collected, plus `"Train Pillar Placeholder"`, `"Subway Pillar Placeholder"` and `"Pillar Pedestrian Placeholder"` explicitly (`Systems/RoadBuilderGenerationDataSystem.cs:139-163`). An unknown name just gives no pillar on elevated nodes (`Utilities/NetworkPrefabGenerationUtil.cs:736-746`). The user's road files use `"Pillar Placeholder"`, the train file uses `"Train Pillar Placeholder"`. |
| `SpeedLimit` | float | **Game units = km/h × 2.** The UI shows `SpeedLimit / 2` km/h or `SpeedLimit / 3.218688` mph, floored to a multiple of 5 (`Utilities/RoadOptionsUtil.cs:40`). The UI step is 5 km/h = 10 units (`RoadOptionsUtil.cs:230-234`). Evidence: the user's file named "30kph" has `SpeedLimit: 60`, normal roads use 80 (40 km/h) and highways use 200/240 (100/120 km/h). It is copied verbatim to `RoadPrefab.m_SpeedLimit` (`NetworkPrefabGenerationUtil.cs:93`). Not present on Path/Fence. |
| `MaxSlopeSteepness` | float | Fraction, e.g. 0.2 = 20 %. Copied to `m_MaxSlopeSteepness` (`NetworkPrefabGenerationUtil.cs:83`). There is no UI for it. All 29 user files use 0.2. |
| `Category` | enum string | See 1.2. |
| `Addons` | flags enum string | Comma-separated names, e.g. `"HasUndergroundWaterPipes, HasUndergroundElectricityCable, GeneratesZoningBlocks"`. See 1.3. |
| `Lanes` | array of LaneConfig | Lanes are listed left to right. See section 2. |
| `ToolbarState` | enum string | `"Inherit"` (0), `"Show"` (1) or `"Hide"` (2) (`Domain/Enums/ShowInToolbarState.cs`). `Inherit` shows the road unless the "hide roads from toolbar by default" setting is on (`NetworkPrefabGenerationUtil.cs:598`). |
| `Playsets` | string[] or null | See 1.5. |

LaneConfig (`Domain/Configurations/LaneConfig.cs:180-184`):

| Field | Notes |
|---|---|
| `Version` | Ignored for JSON. The user files have 0 or 5. Write 5 or 0. |
| `SectionPrefabName` | Raw `NetSectionPrefab` name. It **takes priority** over the group (`NetworkPrefabGenerationUtil.cs:483-489`). Use `null` when you use a group. |
| `GroupPrefabName` | Full C# type name of a lane group, e.g. `"RoadBuilder.LaneGroups.CarGroupPrefab"` (`prefab.name = type.FullName`, `Systems/RoadBuilderNetSectionsSystem.cs:316`). |
| `GroupOptions` | `{ "<option name>": "<value>" }`. Missing keys fall back to the option's `DefaultValue` (`Utilities/LaneOptionsUtil.cs:465-491`). |
| `Invert` | bool, the lane direction (see 2.0). |

Enums are written as their names by Colossal.Json (that is how all user files look). Stick to names. Integer enum values may work but are untested.

### 1.2 Category (`Domain/Enums/RoadCategory.cs`, `[Flags] ulong`)
`Road=0, Highway=1, PublicTransport=2, Train=4, Tram=8, Subway=16, Gravel=32, Tiled=64, RaisedSidewalk=128 (obsolete), Fence=256, Pathway=512`.

- **RoadConfig**: the UI lets you pick exactly one of `Road` (none selected), `Highway`, `PublicTransport` ("Bus / Public Transport Road", meaning `RoadType.PublicTransport`), `Gravel` or `Tiled` (`RoadOptionsUtil.cs:45-80, 265-272`).
  - `Highway` sets `m_HighwayRules`, the highway sides and median, and the "Highways" toolbar group.
  - `PublicTransport` sets `m_RoadType = PublicTransport` and the Transportation > Road toolbar group (`NetworkPrefabGenerationUtil.cs:94-96, 808-821`).
- **TrackConfig**: `Tram`, `Train` or `Subway`, optionally OR'd with `Gravel` or `Tiled` (`RoadOptionsUtil.cs:99-141, 245-262`). This sets `m_TrackType` (`NetworkPrefabGenerationUtil.cs:100-109`).
- **PathConfig** uses `Pathway`. **FenceConfig** uses `Fence`.
- Track, train and subway networks **are a different `Type`**: `"TrackConfig"`, which generates a `TrackBuilderPrefab : TrackPrefab` (`TrackConfig.cs:342-345`, `Domain/Prefabs/TrackBuilderPrefab.cs:12`). Paths use `"PathConfig"` (`PathBuilderPrefab`), fences use `"FenceConfig"`. The user's "Custom Double Train Track" is `Type: "TrackConfig", Category: "Train", SpeedLimit: 320`.

### 1.3 Addons (`Domain/Enums/RoadAddons.cs`, `[Flags] ulong`)

| Flag | Value | Effect |
|---|---|---|
| `HasUndergroundWaterPipes` | 1 | Water, sewage and stormwater sub-sections (`NetworkPrefabGenerationUtil.cs:630-633, 662-720`) |
| `HasUndergroundElectricityCable` | 2 | Low-voltage cable (`:582-594`) |
| `RequiresUpgradeForElectricity` | 4 | The cable is only active with the Lighting upgrade (highways) (`:589, 684`) |
| `GeneratesTrafficLights` | 8 | Sets `m_TrafficLights` (`:95`) |
| `GeneratesZoningBlocks` | 16 | Zone blocks (`:97`) |
| `GrassLeft` | 32 | Edge state `OppositeGrass` |
| `GrassRight` | 64 | Edge state `SideGrass` |
| `GrassCenter` | 128 | Edge state `MiddleGrass` |
| `TreesLeft` | 256 | Edge state `OppositeTrees` |
| `TreesRight` | 512 | Edge state `SideTrees` |
| `TreesCenter` | 1024 | Edge state `MiddleTrees` (`:290-318`) |

Typical combinations in the user files:
- Roads: `HasUndergroundWaterPipes, HasUndergroundElectricityCable, [GeneratesTrafficLights,] GeneratesZoningBlocks`
- Highways: `HasUndergroundElectricityCable, RequiresUpgradeForElectricity`
- Train: `HasUndergroundElectricityCable`

### 1.4 ID format, and whether we can invent IDs
- RB generates IDs as `{first letter of prefab class, lowercased}{Guid.NewGuid()}-{PlatformManager.instance.userSpecificPath}` (`NetworkPrefabGenerationUtil.cs:125`). That gives `r…` for RoadBuilderPrefab, `t…` for TrackBuilderPrefab, `p…` for PathBuilderPrefab and `f…` for FenceBuilderPrefab, followed by the Steam/PDX user id (`<user-id>` in the user's files).
- **Nothing parses the ID.** The only checks are that it is not empty or whitespace, contains no invalid file-name characters and no `..` (`LocalSaveUtil.IsSafeFileName`, `LocalSaveUtil.cs:77-82`), and that it is unique (`RoadBuilderSystem.AddPrefab` skips duplicates, `Systems/RoadBuilderSystem.cs:313-318`). Roads downloaded from the online catalogue keep the *uploader's* ID with a foreign user id suffix (`Systems/UI/RoadBuilderRoadManagementUISystem.cs:~473-484`).
- So **yes, we can invent IDs.** Recommendation: keep the RB-like shape `r<guid>-<suffix>`, e.g. `r<uuid5(namespace, cs1AssetName)>-cs1conv`. A deterministic GUID makes re-runs overwrite instead of duplicate. Name the file exactly `{ID}.json` and set `OriginalID = ID`. Otherwise RB's next save writes a second `{ID}.json` next to your file, which is harmless because duplicates are skipped by ID, but messy.
- **Caveat:** if the user later edits the road in the RB UI, RB assigns a **new** ID and renames the prefab (`RoadBuilderSystem.UpdateRoad` enqueues `generateId: true`, `RoadBuilderSystem.cs:162-171`, then `NetworkPrefabGenerationUtil.cs:121-129`). The old file is deleted on save (`LocalSaveUtil.cs:50-53`). A lookup by our original ID then fails.

### 1.5 Loading at game start, and Playsets
- **Loading.** `RoadBuilderInitializerSystem` (registered at `SystemUpdatePhase.MainLoop`, `Mod.cs:65`) runs once, as soon as `RoadGenerationData` exists, which happens after RB finishes its net-section setup during startup in the main menu. It then disables itself (`Systems/RoadBuilderInitializerSystem.cs:26-61`).
  - It enumerates `ModsData\RoadBuilder\*.json` (`FoldersUtil.cs:16` → `EnvPath.kUserDataPath\ModsData\RoadBuilder`), `LoadFromJson` → `ApplyVersionChanges` → `RoadBuilderSystem.AddPrefab(config)` (`LocalSaveUtil.cs:84-111`).
  - So **a file dropped there before launching the game is loaded.** Files added while the game runs are **not** picked up until restart.
  - A malformed file is logged and skipped (`LocalSaveUtil.cs:101-106`).
- **Saving.** On every game save, `RoadBuilderSerializeSystem.OnUpdate` rewrites each known config to `ModsData` in normalised form (`RoadBuilderSerializeSystem.cs:52-91`; `SaveUsedRoadsOnly` defaults to false).
  - Configs are also embedded in the savegame and re-added on load if missing (`DeserializeNetwork`, `:264-308`).
  - If a save references an RB prefab whose config is gone, the edges are reset to "Small Road" or the default track (`FixInvalidEdges`, `:152-232`).
- **Playsets** in 0.8.0 (`Utilities/NetworkConfigExtensionsUtil.cs:14-36`):
  - `null`, the `NoPlaysetIsolation` setting, or an unknown current playset all mean "in playset".
  - An entry `"-<id>"` excludes that playset.
  - If there are **no positive entries** (including `[]`), the road is in every playset. Otherwise it is only in the listed playsets.
- **The playset only affects toolbar visibility.** The prefab is generated and registered regardless (`NetworkPrefabGenerationUtil.cs:600-603`, `ServiceObject`/`UIObject.m_Group` omitted). The playset is also used for the "not in playset" badge in the management UI.
- 0.7.0 (installed) has older playset code (no `-id` logic). Its changelog for 0.8.0 says "Playset isolation now works as intended". **Recommendation: omit `Playsets` (null).** Null is "in playset" in both versions. In 0.8.0 an empty list `[]` is equivalent. Alternatively, write the user's active playset id: the user files use `"5145850"` (22 files) and `"6818521"` (3). Two files have `[]` and two have null.
- New configs created by RB get `Playsets = [currentPlayset]` (`Utilities/NetworkConfigGenerationUtil.cs:65,127`).

---

## 2. Lanes and lane groups

### 2.0 Lane order, sides, direction (Invert), one-way vs two-way
- `Lanes[0]` is the **left** edge and `Lanes[^1]` the **right** edge, in right-hand-traffic layout. RB flips it for left-hand traffic via `m_InvertMode = InvertLefthandTraffic` (`NetworkPrefabGenerationUtil.cs:84`; comment in `NetworkConfigGenerationUtil.cs:207`).
- Generation (`NetworkPrefabGenerationUtil.GenerateSections`, `:363-479`):
  - Lanes whose section cannot be resolved are dropped with a warning (`:368-378`).
  - A left side section is prepended with `m_Invert = true`. It comes from the first lane's `RoadBuilderEdgeLaneInfo.LeftSidePrefab`, or a category default: `Alley Side 0`, `Highway Side 0`, `Train Side 0`, … (`:384-391, 861-899`).
  - A right side is appended (`:470-477`).
  - If the lane count is **even**, a width-0 centre median (`Road Median 0`, `Highway Median 0`, …, `:901-939`) is inserted at index `Count/2`. It is index-based, not width-based (`:393-399`).
- **Edge lanes.** The first and last lanes should be "edge lanes", i.e. groups or sections with `RoadBuilderEdgeLaneInfo`:
  - Sidewalk (`SidewalkGroupPrefab.cs:105-107`) and Shoulder (`ShoulderGroupPrefab.cs:70`) groups.
  - `Pavement Path Section 3`, `PavementPathWithBikeSection 8`, the bike sections and `RB Sound Barrier 1` (DoNotRequireBeingOnEdge).
  - Otherwise the UI shows a missing-edge placeholder (`Systems/UI/RoadBuilderUISystem.cs:523-524`), but generation still adds default sides.
- **Invert** = `NetSectionInfo.m_Invert` of that lane (`:428, 462`).
  - Two-way road: lanes left of the centre get `Invert: true`, lanes right of it get `false`.
  - One-way road: all car lanes have the same `Invert`, normally all `false`.
  - RB itself defines one-way as "all unconditional car or track sections have the same m_Invert, and none is internally two-way" (`NetworkConfigExtensionsUtil.cs:55-90`; outside-connection choice at `NetworkPrefabGenerationUtil.cs:750-779`).
  - Sidewalks and shoulders: the left one is `Invert: true` (mirrored), the right one `false`. Every user file does this, and the RB sidewalk variants pick a different piece offset when `Inverted` (`RoadBuilderNetSectionsSystem.cs:983-1004`).
  - Medians and no-direction parking ignore Invert.
- Track, bike and subway groups have a `TwoWay`-type option `"Two-way Support"`. Only the value `"1"` selects the bidirectional variant (`LaneOptionsUtil.cs:81-90, 105-106`). Otherwise the lane is one-way in its `Invert` direction.

### 2.1 How a group lane resolves to a section
`GetNetSection` (`NetworkPrefabGenerationUtil.cs:503-552`):
1. It takes all sections linked to the group (`LinkLaneGroups`, `RoadBuilderNetSectionsSystem.cs:128-147`; only exact `RoadBuilderLaneGroup` components, so `RoadBuilderVanillaLaneGroup` sections are import-only aliases).
2. It filters them by the section's own category requirements (`MatchCategories`, `NetworkConfigExtensionsUtil.cs:242-254`).
3. It returns the first section whose combination matches **every** non-Decoration option.
   - Values are compared case-insensitively, and a missing value counts as `""` (`LaneOptionsUtil.cs:57-60, 92-110`).
   - 0.8.0 falls back to the closest partial match (`:540-548`). That fallback may not exist in the 0.7.0 that is installed, so **always emit an exact, existing combination with exact casing**.
4. Group-level category restrictions are only enforced by the UI (lane picker, `Systems/UI/RoadBuilderNetSectionsUISystem.cs:86-125`). Section-level ones (e.g. bike = Pathway only) are enforced at generation.

**Decoration is NOT a GroupOptions value.** The `"Decoration"` key seen in user files is ignored for section choice (`NetworkPrefabGenerationUtil.cs:505-506`). Grass and trees come from `Addons`, keyed by lane **position** (`LaneOptionsUtil.cs:341-363, 481-488`):
- lane index 0 → `GrassLeft` / `TreesLeft`
- last index → `GrassRight` / `TreesRight`
- **any other index** → `GrassCenter` / `TreesCenter`

So all interior lanes (every median, and an interior sidewalk) share one centre flag. The flag becomes a road-wide edge state (`OppositeGrass`, `SideGrass`, `MiddleGrass`, … at `NetworkPrefabGenerationUtil.cs:290-318`). It only shows on sections that contain grass or tree pieces. The UI only enables Grass or Trees if the variant's combination `Decoration` value allows it: `null` = none, `""` = grass and trees, `"T"` = trees only, `"G"` = grass only (`LaneOptionsUtil.cs:62-79, 163-184`).

### 2.2 Lane groups
GroupPrefabName = `RoadBuilder.LaneGroups.<Class>`. Display names are in `LocaleCustomAssets.json:2-15`. The width column is the section width, from the section name or explicit piece width (see section 5). (V) marks a vanilla game section, (RB) one created by Road Builder.

#### CarGroupPrefab ("Car"): `LaneGroups/CarGroupPrefab.cs`
Options:

| Option | Values | Default | Line |
|---|---|---|---|
| `"Lane Width"` | `"3m"`, `"4m"` | `"3m"` | :18-28 |
| `"Transport Option"` | `""` = Car Only, `"Transport"` = Shared Car & Bus, `"Tram"` = Shared Car & Bus or Tram | `""` | :29-51; labels `LocaleCustomAssets.json:39-41` |
| `"Markings"` | `""` = with markings, `"1"` = remove markings | `""` | :52-70 |

Group restriction: not Train, Subway, Fence or Pathway (`:74`).

| Lane Width | Transport Option | Markings | Section | Width | Category |
|---|---|---|---|---|---|
| 3m | "" | "" | `Car Drive Section 3` (V) | 3 | not Gravel/Tiled |
| 3m | Transport | "" | `Car Drive Section 3 - Transport Option` (V) | 3 | not Gravel/Tiled |
| 3m | Tram | "" | `Car Drive Section 3 - Transport Tram Option` (V) | 3 | not Gravel/Tiled |
| 4m | "" | "" | `Highway Drive Section 4` (V) | 4 | not Gravel/Tiled |
| 4m | Transport | "" | `Highway Drive Section 4 - Transport Option` (V) | 4 | not Gravel/Tiled |
| 4m | Tram | "" | `Car Drive Section 4 - Transport Tram Option` (V) | 4 | not Gravel/Tiled |
| 3m | "" | "1" | `Alley Drive Section 3` (V) | 3 | not Gravel/Tiled |
| 3m | "" | "" | `Gravel Drive Section 3` (V) | 3 | Gravel only |
| 3m | "" | "" | `RB Tiled Drive Section 3 - Car` (RB) | 3 | Tiled only |
| 3m | Tram | "" | `Tiled Drive Section 3` (V) | 3 | Tiled only |

(`:80-89`)

The user files consistently use `Tram` on the outermost car lane of each direction on town roads, `Transport` on the outer highway lanes, and `""` inside.
Interpretation (vanilla-CS2 knowledge, not in RB source): the "Transport" variants are the lanes buses and taxis use, where stops can attach. The "Tram" variants additionally carry conditional tram-rail pieces that appear when the player places tram tracks on the road (`TramTrack` piece requirement; RB derives a bus-only section by stripping exactly those pieces, `RoadBuilderNetSectionsSystem.cs:673-702`).
**Bus lane is NOT a Car option.** "Transport" is a *shared* car and bus lane. Dedicated bus lanes use the Bus group below.

#### BusGroupPrefab ("Bus", dedicated bus-only lane): `LaneGroups/BusGroupPrefab.cs`
Options:

| Option | Values | Default |
|---|---|---|
| `"Lane Width"` | `"3m"`, `"4m"` | `"3m"` |
| `"Transport Option"` | `""` = Bus Only, `"Tram"` = Shared Bus & Tram | `""` |

Group restriction: `RequireNone NonAsphalt`, i.e. allowed on Road, Highway and PublicTransport (`:49`).

| Lane Width | Transport Option | Section | Width |
|---|---|---|---|
| 3m | "" | `RB Public Transport Lane Section 3` (RB clone of V minus tram pieces) | 3 |
| 4m | "" | `RB Public Transport Lane Section 4` (RB) | 4 |
| 3m | Tram | `Public Transport Lane Section 3 - Tram Option` (V) | 3 |
| 4m | Tram | `Public Transport Lane Section 4 - Tram Option` (V) | 4 |

(`:57-60`)

There is also a whole-road category `PublicTransport` (bus-only road) and a Shoulder `"Bus"` ground type.

#### SidewalkGroupPrefab ("Sidewalk", edge lane): `LaneGroups/SidewalkGroupPrefab.cs`
Options:

| Option | Values | Default | Line |
|---|---|---|---|
| `"Parking"` (UI label "Side Type") | `""` = No Parking, `"P"` = With Parking, `"B"` = Bikes (sidewalk with bike lane), `"H"` = Harbor | `""` | :18-46 |
| `"Width"` | `1m, 1.5m, 2m, 2.5m, 3m, 3.5m, 4m, 4.5m, 5m, 5.5m, 6m, 7m, 9m` | `"3.5m"` | :47-68 |
| `"Parking Angle"` | `""` = parallel, `"Angled"`, `"Perpendicular"` | `""` | :69-92 |
| `"Decoration"` | via Addons, see 2.1 | | :93-97 |

Edge info: side = `Road Side 0`, adds the sidewalk node state (`:105-107`). No category restriction. Width = total incl. parking or bike lane (inferred from names).

Valid combinations (`:109-148`):

| Parking | Width | Parking Angle | Section | Decoration support |
|---|---|---|---|---|
| "" | 1m, 1.5m, 2m, 2.5m, 3m, 3.5m, 4m, 4.5m | "" | `Sidewalk N` (1 to 3 and 4 are RB clones, 3.5 and 4.5 are V) | **none** |
| "" | 5m, 5.5m, 6m, 7m | "" | `Sidewalk N` (V) | grass + trees |
| P | 5m, 5.5m, 6m, 7m | "" (parallel) | `Sidewalk With Parking N` (V) | grass + trees |
| P | 7m, 9m | Perpendicular | `Sidewalk With Parking Lane 7` / `9` (V) | grass + trees |
| P | 7m, 9m | Angled | `Sidewalk With Parking Lane Angled 7` / `9` (V) | grass + trees |
| B | 3.5m | "" | `Sidewalk With Bikelane 3.5` (V) | none |
| B | 4.5m, 5m, 5.5m, 6m, 7m | "" | `Sidewalk With Bikelane N` (V) | trees only |
| H | 6m | "" | `Harbor Sidewalk Section 6` (V, only if present: Bridges & Ports content) | none |

There is no plain 9m sidewalk, and no parking sidewalk narrower than 5m. Vanilla-only aliases (import only): `Sidewalk With Parking Lane Divided 9` / `Divided Angled 9`.

#### MedianGroupPrefab ("Median"): `LaneGroups/MedianGroupPrefab.cs`
Options:

| Option | Values | Default |
|---|---|---|
| `"Median Width"` | `"1m"`, `"2m"`, `"5m"` | `"2m"` |
| `"Decoration"` | via Addons | |

`"Median Width"` has `IgnoreForSimilarDuplicate` (:16-27). Group restriction: not Train, Subway, Gravel, Fence or Pathway (:37).

| Median Width | Section | Width | Decoration |
|---|---|---|---|
| 1m | `RB Median 1` (RB clone of `Road Median 1`) | 1 | none |
| 2m | `RB Median 2` | 2 | none |
| 5m | `RB Median 5` | 5 | **grass and/or trees** (median trees via `TreesCenter`, grass via `GrassCenter`) |

(:51-53; RB tweaks in `RoadBuilderNetSectionsSystem.cs:751-823`, trees no longer need the Median flag `:330-352`)

Import aliases: `Road Median 1/2/5`, `Parking Road Median 5`.
**Only 5m medians can carry grass or trees**, and every interior lane shares the centre flag. Medians in the user's highway files have `{}` options, i.e. the default 2m.

#### ParkingGroupPrefab ("Parking", standalone parking lane): `LaneGroups/ParkingGroupPrefab.cs`
Options:

| Option | Values | Default |
|---|---|---|
| `"Parking Angle"` | `"Parallel"`, `"Angled"`, `"Perpendicular"` | `"Parallel"` |
| `"Markings"` | `"1"` = With Markings, `""` = Remove Markings | **`""`, i.e. no markings** |

(:17-57) Group restriction: `RequireNone NonAsphalt` (:63).

| Parking Angle | Markings | Section (all RB) | Width | Direction |
|---|---|---|---|---|
| Parallel | 1 | `RB Parking Section Parallel` | 3 | directional |
| Parallel | "" | `RB Parking Section Parallel NoMarking` | **2** | no direction |
| Angled | 1 or "" | `RB Parking Section Angled` / `… NoMarking` | 6 | no direction |
| Perpendicular | 1 or "" | `RB Parking Section Perpendicular` / `… NoMarking` | 6 | no direction |

Piece widths are set explicitly at `RoadBuilderNetSectionsSystem.cs:449-458`, sections at `:1099-1222`.

#### ShoulderGroupPrefab ("Shoulder", edge lane): `LaneGroups/ShoulderGroupPrefab.cs`
Options:

| Option | Values | Default |
|---|---|---|
| `"Ground Type"` | `Asphalt, Bus, Tram, Train, Subway, Gravel, Tiled, Parking, Harbor` | `"Asphalt"` |
| `"Lane Width"` | `1m, 2m, 6.5m, 9m` | `"1m"` |
| `"Parking Angle"` | `"Angled"`, `"Perpendicular"` (or `""`) | `""` |

(:16-67)

| Ground Type | Lane Width | Parking Angle | Section | Side |
|---|---|---|---|---|
| Asphalt | 1m | "" | `Alley Shoulder 1` | Alley Side 0 |
| Asphalt | 2m | "" | `Highway Shoulder 2` | Highway Side 0 |
| Bus | 1m | "" | `Public Transport Shoulder 1` | |
| Gravel | 1m | "" | `Gravel Shoulder 1` | |
| Tiled | 1m | "" | `Tiled Shoulder 1` | |
| Subway | 2m | "" | `Subway Shoulder 2` | |
| Train | 2m | "" | `Train Shoulder 2` | |
| Tram | 1m | "" | `Tram Shoulder 1` | |
| Harbor | 2m | "" | `Harbor Gate Shoulder 2` (if present) | |
| Parking | 9m | Perpendicular or Angled | `Parking Lane 8 - Alley` / `Parking Lane Angled 8 - Alley`, plus aggregated `Parking Lane Shoulder 1` | |
| Parking | 6.5m | Perpendicular or Angled | `Parking Lane 5.5 - Alley` / `Angled 5.5`, plus `Parking Lane Shoulder 1` (or 2.5 at nodes) | |

(:78-100)

#### EmptyGroupPrefab ("Empty Lane"): `LaneGroups/EmptyGroupPrefab.cs`
Options:

| Option | Values | Default |
|---|---|---|
| `"Ground Type"` | `Asphalt, Train, Gravel, Tiled, Harbor` | `"Asphalt"` |
| `"Lane Width"` | `0.5m … 4m` in 0.5 steps | `"1m"` |

(:22-53)

| Ground Type | Widths | Section | Restriction |
|---|---|---|---|
| Asphalt | 0.5–4m | `RB Empty Section {w}` | 3m and 4m unrestricted, others asphalt categories only |
| Train | 0.5–4m | `RB Train Empty Section {w}` | |
| Gravel | 0.5–3m | `RB Gravel Empty Section {w}` | |
| Tiled | 0.5–3m | `RB Tiled Empty Section {w}` | |
| Harbor | 4m | `Harbor Dummy Section 4` (if present) | |

(:58-64)
The empty sections are created with `NetDividerPiece.m_BlockTraffic = true` (`RoadBuilderNetSectionsSystem.cs:846-961`). Width = w. This is the easiest filler to match odd CS1 widths in 0.5 m steps.

#### TramGroupPrefab ("Tram Track", dedicated tram-only lane): `LaneGroups/TramGroupPrefab.cs`
No options. Section `Tram Track Section 3` (3m, not Tiled) or `RB Tiled Tram Section 3` (3m, Tiled) (:22-31). Group restriction: not Gravel, Pathway, Fence or Subway (:15), so it is **allowed on RoadConfig roads**. Direction comes from Invert.

#### TrainGroupPrefab ("Train Track"): `LaneGroups/TrainGroupPrefab.cs`
Option `"Two-way Support"` (TwoWay type; `"1"` = two-way). Sections: `Train Track Section 4` (one-way) and `Train Track Twoway Section 4`, both 4m (:32-33). Group restriction: not Gravel, Tiled, Fence, Pathway or Subway.

#### SubwayGroupPrefab ("Subway Track"): `LaneGroups/SubwayGroupPrefab.cs`
Option `"Two-way Support"`. Sections: `Subway Track Section 4` and `Subway Track Twoway Section 4`, both 4m (:33-34). Group restriction: RequireAll Subway (:24).

#### SubwayMedianGroupPrefab ("Platform"): `LaneGroups/SubwayMedianGroupPrefab.cs`
Option `"Style"` (UI label "Elevation"): `"Raised"` (default) or `"Flat"`. Sections: `RB Subway Median 8` and `RB Subway Median 8 - Plain`, both 8m (:44-48). Flat requires Train or Subway. Group restriction: not Tiled, Fence or Pathway.

#### PoleGroupPrefab ("Pole", catenary poles): `LaneGroups/PoleGroupPrefab.cs`
Options:

| Option | Values | Default |
|---|---|---|
| `"Ground Type"` | `"Tram"`, `"Train"` | `"Tram"` |
| `"Lane Width"` | `"1m"`, `"2m"` | `"1m"` |

Sections: `RB Tram Pole Section 1/2` and `RB Train Pole Section 1/2` (:43-46).

#### BikeGroupPrefab ("Bike"): `LaneGroups/BikeGroupPrefab.cs`
Option `"Two-way Support"`. Sections: `OneWayBikeSection 3` and `TwoWayBikeSection 3` (+ aggregated `TwoWayBikeSection 0`), both 3m (:32-46). **Both sections RequireAll Pathway** (:24 group, :32/:35 section), so they only work in PathConfig. In a RoadConfig the lane is dropped at generation (`NetworkPrefabGenerationUtil.cs:512` filter, then the warning at `:376`).

#### HarborCarGroupPrefab ("Harbor"): `LaneGroups/HarborCarGroupPrefab.cs`
Only registered if the `Harbor Drive Section 4` section exists (:14-15, returns false otherwise). Option `"Transport Option"`: `""` or `"Transport"`. Sections: `Harbor Drive Section 4` and `Harbor Drive Section 4 - Transport Option`, both 4m.

#### Raw sections usable via `SectionPrefabName` (no group)
| Section | Width | Category |
|---|---|---|
| `Tiled Pedestrian Section 3` | 3 | Tiled |
| `RB Tiled Median 2` | 2 | Tiled |
| `Pavement Path Section 3` | 3 | Pathway, edge lane |
| `PavementPathWithBikeSection 8` | 8 | Pathway; pedestrian plus two-way bike |
| `RB Sound Barrier 1` | 1 | any; edge, may sit outside the sidewalk as in the user's 4-lane asymmetric files |

(`RoadBuilderNetSectionsSystem.cs:1012-1097, 1224-1261`) Any `NetSectionPrefab` name works here, since the lookup is against all net sections (`RoadBuilderGenerationDataSystem.cs:124`), but only RB-tagged ones are sensible.

---

## 3. Runtime prefab: name, type, when, and how another mod finds it
- **Creation.** `RoadBuilderSystem.AddPrefab(config)` (`Systems/RoadBuilderSystem.cs:309-373`):
  - `NetworkPrefabGenerationUtil.CreatePrefab` creates a `ScriptableObject` of `config.GetPrefabType()`: `RoadBuilderPrefab : RoadPrefab` (a `NetGeometryPrefab`), or `TrackBuilderPrefab : TrackPrefab`, `PathBuilderPrefab`, `FenceBuilderPrefab`.
  - It sets `prefab.name = config.ID` (`NetworkPrefabGenerationUtil.cs:37-51`, again at `:82`, `RoadBuilderSystem.cs:352`).
  - It generates sections and components, then calls **`prefabSystem.AddPrefab(prefab)`** (`RoadBuilderSystem.cs:354`). It is queued for one more regeneration with `generateId: false` in `RoadBuilderSystem.OnUpdate` (Modification1) (`:361`, `:77-102`).
  - The prefab entity gets the marker component `RoadBuilderPrefabData` (`Domain/Prefabs/RoadBuilderPrefab.cs:19-31`). Deleted roads get `DiscardedRoadBuilderPrefab` instead.
- **When.** Local files are added by `RoadBuilderInitializerSystem` (MainLoop, once, right after RB's section setup at startup, in the main menu). Configs embedded in a save are added during deserialisation if not already present (`RoadBuilderSerializeSystem.DeserializeNetwork`). Lookups are safe from `OnGameLoadingComplete` onward.
- **Lookup from another mod:**
  - `prefabSystem.TryGetPrefab(new PrefabID("RoadBuilderPrefab", configId), out PrefabBase p)`. Use `"TrackBuilderPrefab"` for TrackConfig. PrefabID's type string is the prefab's class name, the same pattern RB uses for vanilla prefabs (`RoadBuilderSerializeSystem.cs:196`). Then use `prefabSystem.GetEntity(p)`.
  - Robust alternative (no type-name assumption): query entities `WithAll<PrefabData, RoadBuilderPrefabData>` and match `prefabSystem.GetPrefab<PrefabBase>(e).name == configId`. Or reflect into `RoadBuilderSystem.Configurations` (`public Dictionary<string, INetworkBuilderPrefab>`, keyed by config ID, `RoadBuilderSystem.cs:46, 450`).
  - Placing from code is then normal CS2 net creation with that prefab entity, e.g. `CreationDefinition` + `NetCourse` entities, or activating the NetTool with the prefab (vanilla knowledge, not RB-specific).
  - Remember the ID changes if the user edits the road in RB (1.4).

---

## 4. Support check
- **Bike lanes**
  - On roads, via **Sidewalk `"Parking":"B"`**, i.e. sidewalk with an integrated bike lane, widths 3.5 to 7 m (`SidewalkGroupPrefab.cs:132-137`).
  - Standalone bike lanes (Bike group) and `PavementPathWithBikeSection 8` are **Pathway (PathConfig) only** (`BikeGroupPrefab.cs:24,32,35`; `RoadBuilderNetSectionsSystem.cs:1237-1244`). **A separate painted bike lane in the carriageway of a RoadConfig is not possible.**
  - The bike sections are vanilla game sections, not created by RB. RB 0.6.0 (2025-11-19) "Added support for bike lanes on sidewalks and paths" alongside "Compatibility with patch 1.4.2f1" (`Changelog.json:~135-145`). Bicycles therefore exist in vanilla CS2 from roughly patch 1.4.x (knowledge plus inference).
- **Trolleybus**
  - Nothing in the RB source (grep for trolley returns nothing).
  - Vanilla CS2 (my knowledge, not verified against the installed game): no trolleybus vehicles or overhead-wire roads.
  - Closest mappings: a Bus lane, `Car "Transport"`, or the `PublicTransport` category.
- **Monorail**
  - Nothing in the RB source.
  - Vanilla CS2 (my knowledge): no monorail transport type. Track types are Train, Tram and Subway only (`NetworkPrefabGenerationUtil.cs:104-108`).
  - Convert monorail roads by dropping the monorail, or approximating with Train/Pole/Empty lanes.
- **Trams**
  - Two ways: a dedicated `TramGroupPrefab` lane (allowed in RoadConfig), or car/bus lanes with `"Tram"` (tram-ready).
  - TrackConfig with Category `Tram` gives a pure tram track network.

---

## 5. Total width
- Road width = sum over the prefab's **unconditional** sections (empty `m_RequireAll` and `m_RequireAny`) of the section width (`NetworkConfigExtensionsUtil.cs:130-135`).
- Section width = **max unconditional piece `m_Width`** + the widths of unconditional sub-sections (`:94-121`).
- Side sections (`* Side 0`) and the centre `* Median 0` are 0 m. So effectively **total = Σ lane widths** from the tables above.
- Aggregated helper sections are counted in the option value, not added on top: Shoulder Parking "9m" = 8 + 1, and "6.5m" = 5.5 + 1.
- Vanilla section widths are taken from their names (`Car Drive Section 3` = 3 m, etc.). RB's own sections set piece widths explicitly (sidewalks `RoadBuilderNetSectionsSystem.cs:539-581`, parking `:449-458`, empties `:618-635`).
- The toolbar tab and unlock requirement for `Road` category: width < 22 → Small Roads, 22–34 → Medium, ≥ 34 → Large (`NetworkPrefabGenerationUtil.cs:829-848`).
- Example user file "Custom Eight-lane Divided Road" = 7 + 3 + 2 + 3 + 5 + 3 + 2 + 3 + 7 = 35 m, which falls in Large.

---

## 6. Minimal template (RoadConfig, 2-lane two-way with parking sidewalks and a tree median)
```json
{
  "Type": "RoadConfig",
  "Version": 5,
  "OriginalID": "r6f1c0e2a-3b4d-5e6f-8a9b-0c1d2e3f4a5b-cs1conv",
  "ID":         "r6f1c0e2a-3b4d-5e6f-8a9b-0c1d2e3f4a5b-cs1conv",
  "Name": "WQ Two-Lane Road with Median",
  "PillarPrefabName": "Pillar Placeholder",
  "SpeedLimit": 100,
  "MaxSlopeSteepness": 0.2,
  "Category": "Road",
  "Addons": "HasUndergroundWaterPipes, HasUndergroundElectricityCable, GeneratesTrafficLights, GeneratesZoningBlocks, GrassCenter, TreesCenter",
  "Lanes": [
    { "Version": 5, "SectionPrefabName": null, "GroupPrefabName": "RoadBuilder.LaneGroups.SidewalkGroupPrefab",
      "GroupOptions": { "Parking": "P", "Width": "5m", "Parking Angle": "" }, "Invert": true },
    { "Version": 5, "SectionPrefabName": null, "GroupPrefabName": "RoadBuilder.LaneGroups.CarGroupPrefab",
      "GroupOptions": { "Lane Width": "3m", "Transport Option": "Tram", "Markings": "" }, "Invert": true },
    { "Version": 5, "SectionPrefabName": null, "GroupPrefabName": "RoadBuilder.LaneGroups.MedianGroupPrefab",
      "GroupOptions": { "Median Width": "5m" }, "Invert": false },
    { "Version": 5, "SectionPrefabName": null, "GroupPrefabName": "RoadBuilder.LaneGroups.CarGroupPrefab",
      "GroupOptions": { "Lane Width": "3m", "Transport Option": "Tram", "Markings": "" }, "Invert": false },
    { "Version": 5, "SectionPrefabName": null, "GroupPrefabName": "RoadBuilder.LaneGroups.SidewalkGroupPrefab",
      "GroupOptions": { "Parking": "P", "Width": "5m", "Parking Angle": "" }, "Invert": false }
  ],
  "ToolbarState": "Inherit"
}
```
- Width is 5 + 3 + 5 + 3 + 5 = 21 m, so it lands in the Small Roads tab.
- `Playsets` is omitted on purpose (null = all playsets).
- Save as `ModsData\RoadBuilder\<ID>.json`, UTF-8, before launching the game.
- SpeedLimit 100 = 50 km/h.

---

## 7. Ground truth from the user's 29 configs
- **Top-level keys.** Every file has exactly `Type, Version, OriginalID, ID, Name, PillarPrefabName, SpeedLimit, MaxSlopeSteepness, Category, Addons, Lanes, ToolbarState, Playsets`.
- **Common values.**
  - `ID == OriginalID` in all files, and `Version` is 5 in all files.
  - `MaxSlopeSteepness` is 0.2 in all files and `ToolbarState` is `Inherit` in all files.
- **Categories.** Road 22, Highway 5, Tiled 1, Train (TrackConfig) 1.
- **SpeedLimit values.** 60, 80, 100, 120, 140 (roads), 20 (pedestrian), 200 or 240 (highways), 320 (train).
- **Option keys and values seen:**
  - Sidewalk: `Width` ∈ {1.5, 2, 2.5, 3, 3.5, 4.5, 5, 6, 7}m, `Parking` ∈ {"", "P"}, `Parking Angle` "", `Decoration` "" or null (ignored).
  - Car: `Lane Width` 3m or 4m, `Transport Option` ∈ {"", "Transport", "Tram"}, `Markings` "".
  - Median: `Median Width` ∈ {1m, 2m, 5m}, or `{}` for the default 2m.
  - Shoulder: `Lane Width` 1m or 2m, `Ground Type` ∈ {Asphalt, Tiled, Train}.
  - Parking: `Parking Angle` Parallel.
  - Train: `{}`.
- **Raw sections used:** `Tiled Pedestrian Section 3`, `RB Tiled Median 2`, `RB Sound Barrier 1`.
- **Lane patterns:**
  - Left edge Invert=true and right edge Invert=false in all files.
  - Two-way roads: left-half car lanes Invert=true.
  - One-way roads ("one way street no parking", "faster two lane road", "One-Way Highway"): all car lanes Invert=false.
- **Decoration example.** "main urban" = Median 5m (interior) + Addons `GrassLeft, GrassCenter, TreesLeft, TreesCenter`.

## 8. Caveats / unverified
- Section widths for vanilla sections are inferred from their names. I could not read the game's prefab data offline.
- Behaviour notes are from 0.8.0 source. The installed 0.7.0 lacks the case-insensitive and closest-variant fallback and has different playset logic (string scan only, no decompile). Emit exact casing and existing combinations, and omit `Playsets`.
- The "Transport" and "Tram" car-lane semantics, and the vanilla absence of trolleybus and monorail, are from CS2 knowledge, not from code.

## 9. CS1 lane geometry and IMT lane coverage (from the Skibidi.02 dump)
- **Lane side, checked in the CS1 IL** (Assembly-CSharp, read with a local metadata dumper; nothing decompiled is committed):
  - `NetSegment.CalculateCorner` offsets the corner by `Cross(startDir, up)` × (`leftSide ? +halfWidth : -halfWidth`). In Unity's left-handed frame, that vector is the **left** of start→end.
  - `NetAI.UpdateLanes` computes `t = m_position / (2·halfWidth) + 0.5`, flips it when `m_flags & 16` (Invert), then lerps from the left corner to the right one.
  - Result: positive `m_position` = **right of start→end**, mirrored on inverted segments. For a direction (dx, dz), right is (dz, −dx).
- **IMT fills in the dump:** 66 = 55 node fills + 11 segment fills, with no duplicates (every polygon is unique). The 55 node fills match the count from the save (38 red, 15 grey, 2 pavement). The 11 segment fills are all red.
- **Segment fills always span the whole segment marking.** An IMT segment marking only has points at its two end lines, so a fill runs from one end line to the other, and `extent` is the visible segment length.
  - A lane counts as covered when its centre is inside the fill for ≥ 90 % of `extent` and, on average, ≥ 70 % of its width is inside. IMT insets fills by the line offset; 11 samples across the lane resolve that.
  - On real data, whole-lane fills score a lateral ≥ 0.80. Partial or diagonal fills score ≤ 0.67 (seg 12520: a lane-change taper; seg 31218: the half lane next to a painted parking lane).
- **Node fills never cover a whole lane.** The longest run of a node fill along a lane is 19.4 m. They are the bus paint continuing through junctions, and CS2/RB has nothing to map them to.
- **Fills from other segments** (bridges overlapping a road below) are excluded because they must belong to the segment's own marking (`markingId`).
