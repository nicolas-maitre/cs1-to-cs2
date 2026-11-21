# CS2 terrain: how a heightmap gets in, by script

Research notes, 2026-11-21. Source: the installed CS2 1.6 `Game.dll`, read with the same System.Reflection.Metadata dumper and IL disassembler as `spikes/m0-cs2-place-road/NOTES-cs2-placement.md`. **[IL]** marks statements read from the current `Game.dll`. **[game]** marks results measured in the running game by `terrain/apply.mjs`.

## TL;DR

- **The call to use:** `TerrainSystem.SetTerrainProperties(float2(heightScale, heightOffset))`, then `TerrainSystem.ReplaceHeightmap(Texture2D)`.
  - Both are public, and neither depends on the editor. The editor's *Import Heightmap* button makes the same calls [IL].
  - They work in a running city: on a loaded save, the terrain changed and CPU height sampling matched the file to 0.0001 m. The replace itself took 12–17 ms [game].
- **Format:** the heightmap must be 4096 × 4096, either `R16_UNorm` (GraphicsFormat 21) or `R16G16B16A16_UNorm` (24, which the game reduces to its first channel) [IL].
- **Geometry:**
  - The playable area is 14 336 m square, centred on the origin, so a pixel is 3.5 m. Pixel centres are at `−7168 + (i + 0.5) × 3.5` [IL].
  - Row 0 is the minimum z.
- **Heights:** height in metres = `raw / 65535 × heightScale + heightOffset`. The default is `(4096, 0)`, and the editor slider allows a scale of 200 to 10 000 [IL].
- **Refreshes needed after a replace:**
  - **Objects:** objects keep their old `y`. Queue the area with the public `TerrainSystem.OnAreaChanged(in Bounds2)`, and `GroundHeightSystem` re-snaps trees and other objects [game].
  - **Sea level:** the global sea level `WaterSystem.SeaLevel` comes from the map. Set it, or the new terrain floods or dries out.
  - **Roads:** existing roads don't follow the new terrain. Apply the terrain before building any roads.
- **World map:** the 57 344 m backdrop around the playable area can be replaced the same way with `ReplaceWorldHeightmap(Texture2D)`. It uses the same height scale.

## 1. Sizes and constants (`TerrainSystem..cctor`) [IL]

| Constant | Value |
| --- | --- |
| `kDefaultHeightmapWidth/Height` | 4096 |
| `kDefaultMapSize` | (14336, 14336) m |
| `kDefaultMapOffset` | −0.5 × size = (−7168, −7168) |
| `kDefaultWorldSize` | 4 × map size = (57344, 57344) m |
| `kDefaultWorldOffset` | (−28672, −28672) |
| `kDefaultHeightScaleOffset` | (4096, 0) |
| `kDownScaledHeightmapScale` | 4 |
| `WaterSystem.kDefaultSeaLevel` / `kMaxSeaLevel` | 511.7 / 2000 |

`IsValidHeightmapFormat(tex)` returns `width == 4096 && height == 4096 && graphicsFormat ∈ {21 R16_UNorm, 24 R16G16B16A16_UNorm}`. `ToR16` converts format 24 by taking every fourth `ushort`, which is the R channel.

Measured on the test map "M0 spike test" [game]:
- `heightScaleOffset (4096, 0)`, `playableArea 14336`, `worldSize 57344`;
- both heightmaps are 4096² `R16_UNorm`; `hasBackdrop true`, `baseLod 1`;
- the sea level is **370 m** and the centre is at 510 m;
- there are 4 water sources.

## 2. The import path

**The editor** (`Game.UI.Editor.TerrainPanelSystem`) [IL]:
- `OnLoadHeightmap(path)` → `ImageAsset.ImportImageFromPath` → `ImageAsset.Load(true)` → `IsValidHeightmapFormat` → `TerrainSystem.ReplaceHeightmap(tex)`. It shows `DisplayHeightmapError` if the format check fails.
- The height-scale slider (200–10 000) → `RefreshTerrainProperties(float2(value, current.y))` → `TerrainSystem.SetTerrainProperties`. The editor never changes the offset `y`.
- World map: `OnLoadWorldHeightmap` → `ReplaceWorldHeightmap(tex)`, and `RemoveWorldmap` → `ReplaceWorldHeightmap(null)`.

**`ReplaceHeightmap(Texture2D inMap)`** [IL] does the following in order:
1. `null` gives a flat default map.
2. `ToR16`, then `SetHeightmap`, which `Graphics.CopyTexture`s into the `TerrainHeights` RenderTexture (R16, random-write).
3. `FinalizeTerrainData(map, null, heightScaleOffset, kDefaultMapOffset, kDefaultMapSize, kDefaultWorldOffset, kDefaultWorldSize, worldHeightMinMax)`.
4. `WaterSystem.UseActiveCellsCulling = false` and `WaterSystem.TerrainWillChange()`.

It does not destroy the caller's texture. `ReplaceHeightmap` keeps the current `heightScaleOffset`, so call `SetTerrainProperties` first.

**`FinalizeTerrainData`** [IL] does the following:
- It sets `heightScaleOffset`, the playable and world area, `baseLod` (1 when a world map exists, otherwise 0), `m_NewMap` and `m_CascadeReset`.
- It recreates the cascade textures and copies the map into the cascade at `baseLod`.
- It sets the global shader values: `colossal_TerrainScale = 1 / (playableArea.x, heightScale, playableArea.y)` and `colossal_TerrainOffset = −positionOffset`, where `positionOffset = (playableOffset.x, heightOffset, playableOffset.y)`.
- It calls **`WriteCPUHeights(map.GetRawTextureData<ushort>())`**. The texture must therefore stay CPU-readable (`Apply(false, false)`). Once that call returns, CPU height sampling uses the new data.
- It initialises the terrain min/max map.

**`SetTerrainProperties(float2)`** calls `FinalizeTerrainData(null, null, …)` with the current areas. It is safe on its own [IL].

**`ReplaceWorldHeightmap(Texture2D)`** [IL]:
- **Outside the editor, `SetWorldHeightmap(map, isEditor:false)` stores the passed texture itself as `worldHeightmap`, without copying it.** The caller must not destroy it.
- In the editor it copies the texture into an editable RenderTexture.
- With `null` it removes the world map. `baseLod` becomes 0, and everything outside the playable area renders as void [game].

## 3. Height sampling, i.e. what roads and objects see [IL]

`TerrainSystem.GetHeightData(waitForPending)` returns a `TerrainHeightData` with:
- `heights` = `m_CPUHeights`, which holds the cascade's base layer, so it includes the terrain flattening under roads and buildings;
- `resolution (4096, 65536, 4096)`;
- `scale = (4096 / 14336, 65535 / heightScale, 4096 / 14336)`, measured as `(0.285714, 15.99976, 0.285714)` at the default scale [game];
- `offset = −positionOffset − (0.5 / scale.x, 0, 0.5 / scale.z)`, which is (7166.25, 0, 7166.25) [game].

`TerrainUtils.SampleHeight(ref data, worldPos)` works like this:
1. It maps the position to heightmap space: `(pos + offset) × scale`.
2. It takes the floor, clamps to the grid, and lerps bilinearly between the 4 samples.
3. It converts back with `raw / scale.y − offset.y`.

So pixel *i* is centred at `−7168 + (i + 0.5) × 3.5`, row 0 is the minimum z, and height = `raw × heightScale / 65535 + heightOffset`. `terrain/lib/heightmap.mjs: sampleCs2` reproduces this, and the game agreed with it to within 0.0001 m at 93 points, both on a 9 × 9 grid and at node positions [game].

## 4. What has to be refreshed after a replace in a running city

| What | Behaviour | What to do |
| --- | --- | --- |
| Terrain rendering, cascades, CPU heights | Updated by `FinalizeTerrainData` and the next `UpdateCascades` (`m_CascadeReset`) | Nothing. The CPU samples were already right 30 frames later [game]. |
| Trees and other objects | **They keep their old `Transform.y`.** After a 380 m drop, the median tree floated 369 m above the ground [game]. `ReplaceHeightmap` queues nothing for `GroundHeightSystem`. | Call **`TerrainSystem.OnAreaChanged(in Bounds2 playableArea)`**, which adds the area to `GroundHeightSystem.GetUpdateBuffer()` the way the terrain brush does. 120 frames later, the median tree is 0.000 m off the ground, and 4749 of 4752 trees are within 1 m [game]. |
| The base map's own nets in a **new** city (outside-connection highway and rail) | Same as existing roads (next row). On Archipelago they stay at the old map's height, up to 880 m, and the terrain walls under them threw 7 of 281 samples off by up to 618 m [game]. | Bulldoze them before applying, or use a base map without nets. The importer then has to rebuild the outside connections at the new height. |
| Existing roads, rails and other nets | **They stay at their old heights.** The terrain cascade is raised toward them, which leaves cliff walls under every old road, and bridges stand on 300 m pillars [game screenshots]. | Apply the terrain to a city with no nets in the area **before** importing roads. This is the planned order: new city → terrain → roads. |
| Water | `ReplaceHeightmap` calls `WaterSystem.TerrainWillChange()`. The **global sea level `WaterSystem.SeaLevel`** comes from the map (370 m on the test map; the default is 511.7). Water bodies already in the simulation stay where they are, at least while the game is paused [game]. | Set `WaterSystem.SeaLevel` (`apply` defaults to `floor(min) − 20`, a dry map). Converting CS1 water is M2 work. |
| World (backdrop) map | It shares `heightScaleOffset` with the playable map. If only the playable map is replaced, the backdrop keeps the old map's raw values at the new scale. | Replace it too, with the CS1 data around the crop (`worldmap.r16`). The result is seamless at the edge [game screenshot]. Removing it gives void. |

## 5. In game vs the editor

- **In game (chosen):** the bridge's `exec` hot-loads `terrain/cs2-apply` (`Cs1ConvTerrain.Ops`). The library builds a `Texture2D(4096, 4096, R16_UNorm)` from the `.r16` file and makes the calls above in `MainLoop`.
  - This needs no UI, no map asset, no file dialog and no editor round trip.
  - **The terrain survives a save and reload** [game]. In a new Archipelago city saved as `cs1conv-terrain-test` with `save.write` and loaded again, the samples were identical. `heightScaleOffset (1024, 0)`, the CS1 world map and `SeaLevel 98` all persisted.
- **Editor:** `ImageAsset.ImportImageFromPath` takes a 16-bit greyscale PNG or TIFF. `convert.mjs` also writes `heightmap.png` for that (north up).
  - Not tested. The orientation convention of `ImageAsset.Load` (whether the top row is north) has not been checked, so treat the PNG as untested.

## 6. Open points

- **Base map nets:** the base map's own nets (outside connections) have to go before the terrain is applied, and the importer then has to rebuild the connections. The terrain library does not do this; bulldozing belongs with the importer's "clear the import area" step.
- **Water:** CS1 sea level, rivers and water sources → CS2 `SeaLevel` and water sources. The `.cs1x` has no water yet.
- **Leftover water:** old water bodies from the base map stay in the water simulation. A flat or empty map, or a map with no water, avoids that. Picking the map for `game.new` matters here.
- **Kinks:** bilinear resampling of a 16 m CS1 grid onto 3.5 m pixels is exact on planes. It rounds sharp kinks, such as cliff edges and cone tips, by about `slope × 1.75 m` (0.2 m at a 1:8 slope, 2 m at a cone tip in the synthetic test). CS1 roads sit on CS1's own smoothed terrain, which is well within that.

---

# M2: map preparation (2026-11-21)

## 7. Outside connections [IL]

- **Data model:**
  - **Prefab side:** a prefab component `Game.Prefabs.OutsideConnection` holds `m_TransferType`, `m_Remoteness`, `m_Commuting` and `m_TradedResources`. It becomes `OutsideConnectionData {m_Type, m_Remoteness}`.
  - **Transfer types:** `OutsideConnectionTransferType` is Road 1, Train 2, Air 4 or Ship 16.
  - **In the city:** every connection is an entity carrying the tag `Game.Objects.OutsideConnection`.
  - **Lanes:** lanes leading out of the map carry `Game.Net.OutsideConnection {m_Delay}`.
- **`Game.Net.OutsideConnectionSystem`** queries `Game.Objects.OutsideConnection` (excluding Temp and electricity/water-pipe connections). For each one:
  - **Node path:** if its `Owner` has a `ConnectedEdge` buffer (that is, the marker is owned by a **net node**), `FillNodeData(node, edges, …)` takes the end lanes of that node's edges.
  - **Free-standing path:** otherwise it uses `FillNodeData(owner, transform, …)` with the prefab's `RouteConnectionData`. Air and ship connections work this way.
  - It sorts the results by angle around the map centre (`atan2(z, x)`) and creates connection lanes between neighbouring nodes of the same type.
  - The outward end of each lane is `CalculateEndPos(p) = p + 10 m × normalize(p.xz)`, i.e. 10 m outward from the map centre.
  - **So a connection does not have to sit on the map edge.** Any dead-end node works, including one at the edge of the imported crop.
- **Creating a marker:** `GenerateObjectsSystem/CreateObjectsJob.CreateObject` gives the new object `Owner(CreationDefinition.m_Owner)`. A permanent definition therefore creates a marker owned by a node:
  - `CreationDefinition {m_Prefab = marker, m_Owner = node, m_Flags = Permanent}`;
  - `ObjectDefinition {m_Position = node position, …}`;
  - `Updated`.
  This is the same pipeline as Spike 1's road definitions. The op is `MapPrep oc.create`, run in phase `tool`.
- **Which CS1 segments become connections:** `lib/connections.mjs` picks every CS1 segment that crosses the crop edge, belongs to class `Highway` (ramps included) or is a `TrainTrack*AI` track, and whose inner node was imported. The inner node becomes the connection, one per node and type. On the Skibidi plan that gives 20: 13 road and 7 train.

## 8. Water [IL]

- **Sea level:** `WaterSystem.SeaLevel` is a public getter and setter, with default 511.7 and maximum 2000.
- **Resetting water:** `ResetToSealevel()` and `Reset()` (empty) are the editor's buttons (`WaterPanelSystem.ApplySeaLevel` / `ResetWater`).
- **Sources:**
  - **Component:** a source is an entity with `Game.Simulation.WaterSourceData {m_ConstantDepth, m_Radius, m_Height, m_Multiplier, m_Polluted, m_Id, m_Modifier}` and a `Transform`.
  - **Creating one: a definition does not work.** `GenerateWaterSourcesSystem` (Modification1) turns `CreationDefinition` + `WaterSourceDefinition` into a source. But its archetype (`CreateArchetype(WaterSourceData, Transform, Temp, Created, Updated)`) **always includes `Temp`**; `Permanent` only skips filling the Temp data. Only `ApplyWaterSourcesSystem` (ApplyTool phase, when a tool applies) turns those into real sources, and the next tool clear removes them [game: 0 sources after 3 variants].
    - **What works:** create the entity directly with the generator's archetype minus `Temp` (`WaterSourceData {m_Height, m_Radius, m_Polluted, m_Id, m_Modifier = 1}`, `Transform`, `Created`, `Updated`). That is op `MapPrep water.set`.
    - **Verified:** 60 s later the water depth at each source equals its `m_Height` (30.0 / 47.6 / 30.0 m) [game].
  - **Fields used:** the 1.6 editor exposes height, radius and polluted (`WaterPanelSystem.WaterSourceEdit`).
  - **What the base maps show:** the test map's sources have `m_Height` of 3–8 m at positions on the terrain, so `m_Height` is the water height above the source and not an absolute level.
- **Sampling depth:** `WaterSystem.GetSurfaceData(out deps)` with `deps.Complete()`, then `WaterUtils.SampleDepth(ref data, pos)` (op `MapPrep water.sample`).
- **CS1 side:** the `.cs1x` `water.json` (exporter) has `seaLevel`, a depth grid `water.r16` on the terrain lattice, and the sources `{type natural|facility|cleaner, outputPosition, targetHeight, inputRate, outputRate, terrainHeightAtOutput}`.
  - Only `natural` sources are converted, at `height = targetHeight − ground`.
  - Facility sources are the outlets and intakes of CS1 buildings, so they go with the buildings (M3).
