# Interchange formats (v1)

```
CS1 + Cs1Exporter ──▶ city.cs1x ──▶ web tool (preview, crop, road mapping) ──▶ import package ──▶ CS2 importer
                                                                      ├─ cs1conv-plan.json
                                                                      └─ roadbuilder/*.json  (copied to CS2 ModsData/RoadBuilder before CS2 starts)
```

These rules apply to every file below:
- **Encoding:** JSON is UTF-8 with no BOM.
- **Units:** distances are in metres and angles in degrees unless stated otherwise.
- **Unknown fields:** readers ignore fields they don't know.
- **Missing sections:** every section is optional, and readers must handle one that is missing or `"failed"`.
- **Versioning:** the format version bumps only for breaking changes. Adding fields is not breaking.

---

## 1. `.cs1x`: CS1 export

`.cs1x` is a zip file (deflate or stored) containing these entries:

| Entry | Required | Content |
| --- | --- | --- |
| `manifest.json` | yes | metadata and the status of each section |
| `network.json` | M1 | nodes and segments |
| `netinfos.json` | M1 | every NetInfo used, plus its variants and parents |
| `imt.json` | M1 | IMT fillers (fills) with world polygons |
| `terrain.json` + `terrain.r16` | M1 | raw heightmap |
| `report.json` | yes | non-fatal problems |
| `zones.json` | M2 | zone blocks with per-cell zone and occupied state |
| `water.json` + `water.r16` | M2 | sea level, water sources, water depth grid |
| `transit.json` | M2 | transport lines with ordered stops |
| `tmpe.json` | M2 | TM:PE customisations (absent without TM:PE) |
| `nodecontroller.json` | M2 | Node Controller node and segment-end settings (absent without Node Controller) |
| `buildings.json` | M3 data, exported since M2 | every building |
| `props.json`, `trees.json` | M3 data, exported since M2 | free-standing props and trees |
| `districts.json` + `districts.u8` + `parks.u8` | M3 | districts and park / industry / campus areas as cell grids, names, policies |
| `assets.json` | M3 | catalogue of every building, prop, tree and network prefab in use |

Readers must still check `manifest.sections` before reading an entry: a section can be `"failed"` or `"absent"` in any file.

### Coordinates

| What | Convention |
| --- | --- |
| Space | CS1 world space, in metres |
| Axes | X east, Y up, Z north. The map centre is the origin. |
| Positions | `[x, y, z]` |
| Map size | The full 81-tile area is 17 280 m × 17 280 m (−8640 … +8640) |
| Heights | `y` is the absolute height in metres. Terrain heights use the same datum. |

### `manifest.json`

```json
{
  "format": "cs1x", "formatVersion": 1,
  "exporter": { "name": "Cs1Exporter", "version": "1.0.x" },
  "exportedAt": "2026-11-21T01:23:45+01:00",
  "city": { "name": "Skibidi", "saveName": "Skibidi.02" },
  "traffic": { "leftHand": false },
  "game": { "version": "1.21.1-f9" },
  "mods": [ { "name": "IntersectionMarkingTool", "version": "1.15.0.0" }, { "name": "TrafficManager", "version": "…" } ],
  "coordinateSystem": "CS1 world space, metres, Y up, origin at map centre",
  "mapSize": 17280,
  "detectedMods": { "imt": "1.15.0.0", "tmpe": "11.9.4.25100", "nodeController": "3.5.4.1", "eightyOneTiles": "1.0.5.0" },
  "tiles": { "size": 1920, "grid": 9, "unlocked": [[3,4], [4,4], …] },
  "sections": { "network": "ok", "netinfos": "ok", "imt": "ok", "terrain": "ok", "zones": "ok", "water": "ok", "transit": "ok",
                "tmpe": "ok", "nodecontroller": "ok", "buildings": "ok", "props": "ok", "trees": "ok" }
}
```

- **`mods[]`:** `name` is the assembly name. Each entry also has `title` (the name in Content Manager) and, for workshop mods, `workshopId` as a string.
- **`detectedMods`:** the versions of the mods the converter cares about, or `null` when not enabled.
- **`exporter`** (since 1.2): `version` is the release version (`1.2.0`), `build` the assembly build.
- **`traffic.leftHand`** (since 1.2.1): `true` when the CS1 city drives on the left (the game's "invert traffic" setting). Lanes are then mirrored relative to the segment direction, so CS1 lane indices map to the mirrored CS2 lanes. Missing in older exports: treat as unknown.
- **`excludedSections`** (since 1.2): optional sections the user switched off in the mod options. They are `"absent"` in `sections`.
- **`test`** appears only in test exports (`simulateMissing` / `simulateFailure`, see `cs1-exporter/README.md`). Never use such a file for a real conversion.
- **`tiles.unlocked`:** `[col, row]` pairs in a 9 × 9 grid of 1920 m tiles. Col 0 is minimum x and row 0 is minimum z, so tile (4,4) is the centre tile.

Each section has one of these statuses:
- `"ok"`: written completely.
- `"failed"`: the entry may be missing or partial; `report.json` says why.
- `"absent"`: not exported (not implemented, or the mod isn't present).

### `network.json`

```json
{ "nodes": [ { "id": 12, "info": "Train Track", "position": [x,y,z], "flags": "Created, Middle, OnGround",
               "elevation": 0, "heightAboveTerrain": -0.07, "building": 0, "segments": [24308, 17418] } ],
  "segments": [ { "id": 2, "info": "Train Track", "ai": "TrainTrackAI", "startNode": 13593, "endNode": 3469,
                  "startDirection": [x,y,z], "endDirection": [x,y,z],
                  "bezier": [[x,y,z],[x,y,z],[x,y,z],[x,y,z]], "flags": "…", "inverted": true, "length": 93.08,
                  "oneWay": false, "elevationKind": "ground" } ] }
```

- **`bezier`** is the segment's cubic curve from the start node to the end node, using the game's own middle points (`NetSegment.CalculateMiddlePoints`).
- **`elevationKind`** is one of `ground`, `elevated`, `bridge`, `slope`, `tunnel`.
- **`info`** is the NetInfo name, which is the key into `netinfos.json`. For an elevated, bridge, slope or tunnel segment it names the variant NetInfo.

### `netinfos.json`

```json
{ "netInfos": [ { "name": "Basic Road", "title": "…", "ai": "RoadAI", "service": "Road", "subService": "None",
                  "level": "Level1", "className": "…", "fields": { "m_halfWidth": 8, "m_pavementWidth": 3, … },
                  "aiFields": { … }, "parent": null,
                  "variants": { "elevated": "Basic Road Elevated", "bridge": "Basic Road Bridge", "slope": "Basic Road Slope", "tunnel": "Basic Road Tunnel" },
                  "lanes": [ { "index": 0, "fields": { "m_position": -5, "m_width": 3, "m_laneType": "Pedestrian", … },
                               "props": [ { "prop": "…", "tree": null, "finalProp": "…", "finalTree": null, "fields": { … } } ] } ] } ] }
```

- **What's included:** every NetInfo a node or segment uses, plus its variants, plus the parent of every variant. The parent is the ground NetInfo that owns the variant.
- **`parent`:** on a variant, the name of its ground NetInfo; `null` on a ground NetInfo.
- **`variants`:** on a ground NetInfo, the names of its variants; a variant that doesn't exist is omitted.
- **`variantKind`:** on a variant, `elevated`, `bridge`, `slope` or `tunnel`. **`otherParents`:** the other ground NetInfos that share the variant, when there are any.
- **`used`:** true when a node or segment in the city uses this NetInfo.

### `imt.json`

```json
{ "fillers": [ { "markingType": "Node", "markingId": 2302, "fillerId": 537978560, "isMedian": true,
                 "styleClass": "SolidFillerStyle", "styleType": "FillerSolid", "color": [136,0,0,94],
                 "styleProperties": { … }, "polygon": [[x,y,z], …] } ] }
```

- **`markingType`** is `Node` or `Segment`, and `markingId` is that node or segment id.
- **`styleType`** is the IMT style, e.g. `FillerSolid`, `FillerPavement`, `FillerGrass`, `FillerStripe`, `FillerChevron`, `FillerGravel`, `FillerRuined`, `FillerCliff`.
- **Interpretation** is in `docs/DECISIONS.md`:
  - red solid fill → bus lane;
  - pavement or grass fill → median;
  - full coverage only;
  - node fills and grey paint → report only.

### `terrain.json` + `terrain.r16`

```json
{ "resolution": 1081, "size": 17280, "heightScale": 0.015625, "file": "terrain.r16",
  "rowOrder": "row 0 = minimum z (south), column 0 = minimum x (west)", "byteOrder": "little-endian uint16",
  "seaLevel": null, "min": 40.2, "max": 612.7 }
```

- **`terrain.r16`** holds `resolution × resolution` uint16 values. Height in metres is `raw × heightScale`.
- **Sample spacing** is `size / (resolution − 1)` metres. Sample `(col, row)` is at `x = −size/2 + col × spacing`, `z = −size/2 + row × spacing`.
- **The exporter verifies the orientation** against node heights. Anything that differs from this spec gets reported in `report.json`, and the spec is corrected here.

### `report.json`

```json
{ "items": [ { "level": "info|warning|error", "section": "imt", "message": "IMT assembly: IntersectionMarkingTool 1.15.0.0" } ] }
```

### `zones.json` (M2)

```json
{ "cellSize": 8, "columns": 4,
  "zoneCodes": { "0": "Unzoned", "1": "Distant", "2": "ResidentialLow", "3": "ResidentialHigh", "4": "CommercialLow",
                 "5": "CommercialHigh", "6": "Industrial", "7": "Office", "15": "None" },
  "blocks": [ { "id": 2, "position": [x,y,z], "angle": -2.3717, "rows": 5, "segment": 34701,
                "zones": "----------------22--", "occupied": "00001111111100011111" } ] }
```

One entry per created CS1 `ZoneBlock`.

- **Block shape:** 4 columns × `rows` rows (0–8) of 8 m cells. `segment` is the road the block belongs to.
- **`zones`** has `rows × 4` characters, row-major: row 0 columns 0–3, then row 1, …
  - Each character is the cell's zone code as one hex digit (see `zoneCodes`), or `-` when the cell isn't valid (blocked or cut off).
  - Valid cells are the ones a player could zone.
- **`occupied`** has the same layout: `1` when a building occupies the cell (CS1 `m_occupied1 | m_occupied2`). Invalid cells can be occupied too.
- **Cell centre** of column `c`, row `r`, with `a = angle` (radians, as CS1 stores it):
  - `colDir = (cos a, 0, sin a) × 8` and `rowDir = (sin a, 0, −cos a) × 8`;
  - `centre = position + (c − 3.5) × colDir + (r − 3.5) × rowDir`.
  - Column 0 is the cell next to the road and column 3 the deepest. On Skibidi.02, column 0 centres are on average 13.7 m from the road centre line, then +8 m per column. The exporter re-checks this on every export (report item).

### `water.json` + `water.r16` (M2)

```json
{ "seaLevel": 181.98, "nextSeaLevel": 181.98,
  "depth": { "file": "water.r16", "resolution": 1081, "size": 17280, "heightScale": 0.015625, "wetSamples": 134043, "maxDepth": 82.5,
             "meaning": "water depth above the terrain …" },
  "sources": [ { "index": 0, "id": 1, "type": "natural", "inputPosition": [x,y,z], "outputPosition": [x,y,z],
                 "inputRate": 65535, "outputRate": 65535, "target": 17157, "targetHeight": 268.1,
                 "flow": 9086, "water": 0, "pollution": 0, "terrainHeightAtOutput": 202.5 } ] }
```

- **`seaLevel`** is CS1's `WaterSimulation.m_currentSeaLevel` in metres. The heights use the terrain datum.
- **`water.r16`** is on the same lattice and in the same row order as `terrain.r16`.
  - Each value is the water depth above the terrain, `raw × heightScale` metres; 0 means dry.
  - Depth = `SampleRawHeightWithWater − SampleRawHeightSmooth` at each sample point, which is a snapshot of the simulated water at export time.
- **`sources`** come from `WaterSimulation.m_waterSources`.
  - `type` is `natural` (map water sources), `facility` (pumps and drains of water buildings) or `cleaner`.
  - `target` is the target water surface height in raw units (×1/64 m); `targetHeight` is the same in metres.
  - Rates, `flow`, `water` and `pollution` are CS1's internal units.

### `transit.json` (M2)

```json
{ "lines": [ { "id": 12, "name": "Metro Line 3", "number": 3, "info": "Metro", "type": "Metro", "vehicleType": "Metro",
               "color": [234,0,255], "customColor": true, "complete": true, "vehicles": 4, "flags": "…",
               "stops": [ { "node": 25675, "position": [x,y,z], "lane": 7117, "segment": 16511, "laneIndex": 3,
                            "laneOffset": 0.498, "building": 13291 } ] } ] }
```

- **`type`** is CS1's `TransportInfo.TransportType`: `Bus`, `Metro`, `Train`, `Tram`, `Ship`, `Airplane`, `Monorail`, `CableCar`, `TouristBus`, `Trolleybus`, `Helicopter`, `Pedestrian`, …
- **`color`** is the colour the game shows (`TransportManager.GetLineColor`), as 0–255 RGB.
- **`stops`** are in line order, starting at the line's first stop. The loop closes back to the first stop, which isn't repeated.
  - `node` is the stop's NetNode: a transport-line node, also listed in `network.json`.
  - `segment` / `laneIndex` / `laneOffset` (0–1 along the lane) say where the stop stands on the road or track network.
  - `building` is the station within 64 m for station-based types (metro, train, …); it is 0 for kerbside stops.
- **Line paths** are in `network.json` too: segments whose NetInfo is the line type, e.g. `Bus Line`. The importer should skip them as roads.

### `tmpe.json` (M2)

Everything TM:PE saves in the savegame: only what the user customised, never defaults. It's read through each TM:PE manager's own `SaveData`, and lane-based records are resolved to segment, lane index and node.

```json
{ "assembly": { "name": "TrafficManager", "version": "11.9.4.25100" },
  "laneArrows":   [ { "lane": 237, "segment": 35152, "laneIndex": 3, "node": 10252, "arrows": ["Forward"], "savedRaw": 16 } ],
  "laneConnections": [ { "sourceLane": 193748, "sourceSegment": 21512, "sourceLaneIndex": 3, "targetLane": 104395,
                         "targetSegment": 27548, "targetLaneIndex": 3, "sourceStartNode": false, "node": 32029, "group": "Road" } ],
  "speedLimits":  [ { "lane": 1, "segment": 2, "laneIndex": 0, "node": 3, "kmh": 60, "unlimited": false } ],
  "defaultSpeedLimits": { "Medium Road": 1.2 },
  "vehicleRestrictions": [ { "lane": 113721, "segment": 119, "laneIndex": 5, "node": 28464, "allowed": ["Bus","Emergency"], "allowedRaw": 34 } ],
  "junctionRestrictions": [ { "segment": 244, "startNode": false, "node": 4484, "uturnAllowed": false, "enterWhenBlockedAllowed": false } ],
  "prioritySigns": [ { "segment": 99, "node": 14671, "type": "Main" } ],
  "parkingRestrictions": [ { "segment": 23023, "forwardParkingAllowed": false, "backwardParkingAllowed": true } ],
  "trafficLights": [ { "node": 123, "trafficLight": false } ],
  "timedTrafficLights": [ { "nodeId": 2269, "nodeGroup": [ … ], "started": true, "currentStep": 2, "timedSteps": [ … ] } ],
  "parts": { "laneArrows": "ok", … } }
```

- **Lane records** (`laneArrows`, `speedLimits`, `vehicleRestrictions`) carry `lane` (the CS1 lane id), `segment`, `laneIndex` (the index into the segment NetInfo's `lanes`) and `node`.
  - `node` is the node the lane's traffic flows into: from `m_finalDirection`, flipped on inverted segments. It is 0 for two-way lanes.
- **`laneArrows.arrows`** are TM:PE's final arrows for that lane: any of `Left`, `Forward`, `Right`. Only lanes with custom arrows are listed.
- **`laneConnections`:** traffic in `source` may only continue into the `target` lanes listed for it, at `node`, which is the source segment's start node when `sourceStartNode` is true. `group` is `Road` or `Track`.
- **`speedLimits.kmh`** is km/h as TM:PE saves it. `unlimited` is true for TM:PE's "no limit" (1000 km/h).
- **`defaultSpeedLimits`:** custom default speed per NetInfo name, in game units (× 50 = km/h).
- **`vehicleRestrictions.allowed`:** the vehicle types still allowed on the lane, as `ExtVehicleType` names (`PassengerCar`, `Bus`, `Taxi`, `CargoTruck`, `Service`, `Emergency`, `PassengerTrain`, `CargoTrain`, `Tram`, `Bicycle`, …).
- **`junctionRestrictions`:** one record per segment end with at least one explicit setting.
  - Possible keys: `uturnAllowed`, `turnOnRedAllowed`, `farTurnOnRedAllowed`, `straightLaneChangingAllowed`, `enterWhenBlockedAllowed`, `pedestrianCrossingAllowed`.
  - A missing key means the default.
- **`prioritySigns.type`** is `Main`, `Stop` or `Yield`, for the segment end at `node`.
- **`parkingRestrictions`:** forward is the start → end direction of the segment.
- **`trafficLights`:** junctions where the user added (`true`) or removed (`false`) a light. Whether a node has a light in the end is in `network.json` node `flags` (`TrafficLights`).
- **`timedTrafficLights`:** TM:PE's own `Configuration.TimedTrafficLights` records, field by field.
  - `nodeGroup` lists the nodes that are synchronised.
  - `timedSteps[]` have `minTime` / `maxTime` (s), `changeMetric`, `waitFlowBalance` and `segmentLights`, keyed by segment id.
  - Each `segmentLights` entry has `customLights`, keyed by vehicle-type set, with `leftLight` / `mainLight` / `rightLight` = `Red|Green|RedToGreen|GreenToRed`, plus `pedestrianLightState`.
- **Manual traffic lights** aren't saved by TM:PE, so they aren't exported.
- **`parts`:** one status per key, `"ok"` or `"failed: …"`. A failed part is `null`.

### `nodecontroller.json` (M2)

```json
{ "assembly": { "name": "NodeControllerRenewal", "version": "3.5.4.1" },
  "nodes": [ { "id": 35, "type": "Custom", "defaultType": "Custom", "mode": "Flat", "offset": 54.0, "shift": -0.59,
               "rotateAngle": 0, "slopeAngle": 0, "twistAngle": 0, "stretch": 1, "stretchPercent": 100, "gap": 90.0,
               "noMarkings": false, "collision": true, "forceNodeLess": false, "followSlope": true,
               "isEnd": false, "isJunction": true, "isTwoRoads": false, "isUnderground": false,
               "segmentEnds": [ { "segment": 233, "isStartNode": false, "mode": "Flat", "offset": 8.0, "leftOffset": null, "rightOffset": null,
                                  "minOffset": 0, "maxOffset": 68.4, "rotateAngle": 0, "slopeAngle": 0, "twistAngle": 0, "shift": -1.78,
                                  "stretch": 1, "stretchPercent": 100, "deltaHeight": 0, "noMarkings": false, "collision": true,
                                  "forceNodeLess": false, "followSlope": true, "leftFlatEnd": false, "rightFlatEnd": false,
                                  "keepDefaults": false, "isNodeLess": false, "shouldHideCrossingTexture": null,
                                  "widthRatio": 1, "heightRatio": 0, "isMainRoad": false } ] } ] }
```

- **Which nodes:** every node that has Node Controller data (Node Controller creates it for nodes it touches, not only for user edits). Compare `type` with `defaultType` to see whether the user changed the node type.
- **`type`:** `Middle`, `Bend`, `Stretch`, `Crossing`, `UTurn`, `Custom` or `End`. **`mode`:** `Flat`, `Slope` or `FreeForm`.
- **Units:** offsets, shifts and `deltaHeight` are metres; angles are degrees; `stretchPercent` is percent.
- **Nullable flags:** `null` means Node Controller's default for that node.
- **Missing properties:** a property missing in another Node Controller version is `null`.

### `buildings.json` (M3 data)

```json
{ "angleUnit": "degrees, CS1 Building.m_angle (radians) × 180/π, same rotation sense",
  "buildings": [ { "id": 422, "info": "L3 3x2 Detached03", "workshopId": null, "position": [x,y,z], "angle": 179.1,
                   "service": "Residential", "subService": "ResidentialLow", "classLevel": "Level3", "level": 3,
                   "ai": "ResidentialBuildingAI", "growable": true, "width": 3, "length": 4, "size": [20.7, 10.3, 14.9],
                   "parent": 0, "subBuilding": 0, "netNode": 0, "accessSegment": 1234, "district": 3, "park": 0,
                   "flags": "Created, Completed, Active", "customName": "…" } ] }
```

- **`accessSegment`** (since exporter 1.2): the segment the building connects to (its road access), 0 if none.
- **`district` / `park`** (since 1.2): the district and park-area ids at the building's position (`districts.json`), 0 if none.
- **Service-building specifics** (capacity, radius, workers, vehicles, …) are per prefab: see `assets.json` → `buildings[].aiFields`.
- **`growable`** is true for zoned (`PrivateBuildingAI`) buildings; everything else is ploppable.
- **`level`** is 1-based (CS1 `m_level + 1`).
- **`width` × `length`** are in 8 m cells. `size` is the prefab's bounding size in metres.
- **`workshopId`** is the numeric prefix of a workshop asset name, or `null` for vanilla assets.
- **`parent` / `subBuilding`** link sub-buildings. `netNode` is the building's first network node.
- **`customName`** appears only when the user renamed the building.

### `props.json` / `trees.json` (M3 data)

```json
{ "format": "props[i] = [infoIndex, x, y, z, angleDegrees, fixedHeight]", "props": [[0, 12.5, 260.1, -40.2, 90, 0]], "infos": ["Bench 01"] }
{ "format": "trees[i] = [infoIndex, x, y, z, fixedHeight]", "trees": [[0, 12.5, 260.1, -40.2, 0]], "infos": ["Conifer"] }
```

- **Which instances:** only free-standing props and trees (`PropManager` / `TreeManager`). Props and trees that are part of buildings or networks come with their prefabs.
- **Layout:** compact rows, with `infoIndex` pointing into `infos`.
- **`fixedHeight`** is 1 when the player set the height explicitly (Move It), so `y` must be kept.

### `districts.json` + `districts.u8` + `parks.u8` (M3)

```json
{ "grid": { "resolution": 512, "cellSize": 19.2, "size": 9830.4, "districtFile": "districts.u8", "parkFile": "parks.u8",
            "rowOrder": "uint8 id per cell, row 0 = minimum z (south), column 0 = minimum x (west); 0 = none" },
  "city": { "policies": ["SmokeDetectors", "…"] },
  "districts": [ { "id": 3, "name": "Old Town", "nameLocation": [x,y,z], "cells": 812, "area": 299335.7, "policies": ["HighriseBan"] } ],
  "parks": [ { "id": 1, "name": "Ore Industry Area", "type": "Ore", "kind": "industry", "level": "Level2", "mainGate": 4711,
               "cells": 400, "area": 147456, "policies": ["…"], "flags": "Created" } ] }
```

- **Rasters:** `districts.u8` and `parks.u8` each hold `resolution × resolution` bytes, one per cell.
  - Each byte is the id of the district (or park area) that owns the cell; 0 means none.
  - CS1 blends up to four ids per cell; the raster keeps the one with the highest weight.
  - The exporter checks the rasters against the game's own `SampleDistrict` / `SamplePark` on every export (report item).
- **Cell centre** of column `c`, row `r`: `x = −size/2 + (c + 0.5) × cellSize`, `z = −size/2 + (r + 0.5) × cellSize`.
  - Vanilla CS1 uses 512 × 19.2 m = 9830.4 m, which covers the central 25 tiles. A mod that enlarges the grid shows up as a larger `resolution`.
- **`city.policies`** are the city-wide policies (CS1 district 0). Each district's `policies` are its own.
  - Names are CS1 `DistrictPolicies.Policies` values, e.g. `HighriseBan`, `SmokeDetectors`, `HeavyTrafficBan`, `OldTown`, `Leisure`, `Tourist`, `Organic`, `Hightech`, `SelfSufficient`.
- **`parks`** are all CS1 "park" areas.
  - `kind` is `park`, `industry`, `campus`, `airport`, `pedestrianZone` or `other`.
  - `type` is the CS1 `ParkType`: `Generic`, `AmusementPark`, `Zoo`, `NatureReserve`, `Industry`, `Farming`, `Forestry`, `Ore`, `Oil`, `GenericCampus`, `TradeSchool`, `LiberalArts`, `University`, `Airport` or `PedestrianZone`.
  - `level` is `Level1` … `Level5`. `mainGate` is the main building's id in `buildings.json`.
- **`cells` / `area`** count the raster cells (and m²) that belong to the district or area.

### `assets.json` (M3)

A catalogue of every prefab the city uses. It's the input for matching CS1 assets to CS2 equivalents.

```json
{ "countUnit": "instances in the city (buildings include sub-buildings; networks count segments)",
  "buildings": [ { "name": "L3 3x2 Detached03", "kind": "building", "workshopId": null, "custom": false, "title": "…", "count": 312,
                   "service": "Residential", "subService": "ResidentialLow", "zone": "ResidentialLow", "level": "Level3",
                   "ai": "ResidentialBuildingAI", "growable": true, "footprint": [3, 2], "size": [20.7, 10.3, 14.9], "height": 10.3,
                   "placementMode": "Roadside", "subBuildings": 0, "tags": ["growable", "vanilla", "lowDensity"],
                   "aiFields": { "m_constructionCost": 0, "…": 0 } } ],
  "props":    [ { "name": "…", "kind": "prop", "workshopId": null, "custom": false, "title": "…", "count": 12, "size": [x,y,z], "height": 2.1, "tags": ["prop", "vanilla"] } ],
  "trees":    [ { "name": "…", "kind": "tree", …, "tags": ["tree", "vanilla"] } ],
  "networks": [ { "name": "Basic Road", "kind": "network", …, "service": "Road", "subService": "None", "ai": "RoadAI", "width": 16,
                  "tags": ["network", "vanilla", "road"] } ] }
```

- **`count`:** instances in this city, from the same instances as `buildings.json`, `props.json` and `trees.json`; network segments for networks. Entries are sorted by count, highest first.
- **`title`:** the name shown in game (for workshop assets usually the asset's own name). `custom` is true for custom content.
- **`zone`:** the zone a growable belongs to (`ResidentialLow`, `ResidentialHigh`, `CommercialLow`, `CommercialHigh`, `Industrial`, `Office`); `null` for ploppables.
- **`footprint`:** `[width, length]` in 8 m cells. `size` / `height` are the prefab's bounding size in metres.
- **`tags`:** a small, fixed vocabulary for matching. Several apply at once.
  - always one of `growable` / `ploppable` and one of `workshop` / `vanilla`;
  - `service` (any non-growable with a service);
  - `park`, `station`, `depot`, `landmark`, `unique` (CS1 Monument service), `campus`, `industries` (Industries DLC buildings), `intersection`, `decoration`;
  - `subBuilding` (used as a sub-building), `eco`, `highDensity`, `lowDensity`;
  - for networks: `road`, `highway`, `train`, `metro`, `tram`, `path`, `utility`, `transportLine`.
- **`aiFields`:** every primitive public field of the building's AI (CS1 keeps service-building specifics there), keyed by CS1 field name. Examples: `m_patientCapacity`, `m_studentCount`, `m_jailCapacity`, `m_fireDepartmentAccumulation`, `m_policeDepartmentRadius`, `m_workPlaceCount0..3`, `m_noiseAccumulation`, `m_constructionCost`, `m_maintenanceCost`.

---

## 2. Import package: web tool → CS2 importer

The web tool, or its CLI (`node web/cli/cs1conv.ts plan …`), writes these from a `.cs1x` and the user's choices:

```
<package>/
  cs1conv-plan.json
  roadbuilder/<id>-cs1conv.json     # generated Road Builder configs (see spikes/m0-roadbuilder-configs)
  report.json                       # everything that was dropped or approximated, for the user
  README.txt                        # for the user: what each file is, where it goes, what converts (CRLF)
  terrain/heightmap.r16             # CS2 playable-area heightmap (see "Terrain files" below)
  terrain/worldmap.r16              # CS2 world map around it
  terrain/terrain.json              # heightmap metadata
```

The package is self-contained: everything the CS2 importer mod needs (roads, terrain, water, outside connections, zoning, traffic, transit, buildings, districts) is in the plan or next to it. No Node script has to run on the user's machine.

Road Builder reads its configs once, when CS2 starts. So the `roadbuilder/*.json` files must be copied into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\` **before** CS2 starts. The CLI does that (`--install-rb`); the web UI offers them as a download, with install instructions in the zip's README.

The plan stays at `formatVersion: 1`. Every addition below is optional: a v1 reader that ignores unknown fields still builds the roads.

### `cs1conv-plan.json`

The plan is already in CS2 coordinates, so the importer makes no mapping decisions.

```json
{
  "format": "cs1conv-plan", "formatVersion": 1,
  "generator": { "name": "cs1conv-web", "version": "0.2.0", "generatedAt": "…" },
  "source": { "city": "Skibidi", "saveName": "Skibidi.02", "exportedAt": "…", "cs1xSha256": "…", "kind": "cs1x", "leftHandTraffic": false },
  "crop": { "centerX": 1200, "centerZ": -300, "size": 4000 },
  "transform": { "dx": -1200, "dz": 300, "heightMode": "terrain-relative" },
  "bounds": { "minX": -2000, "maxX": 2000, "minZ": -2000, "maxZ": 2000 },
  "requires": ["RoadBuilder", "Traffic"],
  "prefabs": {
    "rb:5266315b": { "type": "RoadBuilderPrefab", "name": "r5266315b…-cs1conv", "source": "Four-Lane Road with Decorative Trees" },
    "rb:4a322dd1": { "type": "RoadBuilderPrefab", "name": "r4a322dd1…-cs1conv", "source": "2225246713.Five-Lane One-Way Road_Data", "imt": "0=bus", "lanesFrom": "IMT" },
    "rb:1d0c7e2a": { "type": "RoadBuilderPrefab", "name": "r1d0c7e2a…-cs1conv", "source": "Medium Road", "speedKmh": 60 },
    "v:Double Train Track": { "type": "TrackPrefab", "name": "Double Train Track", "source": "Train Track" }
  },
  "nodes": [ { "id": 12, "x": 262.6, "z": -343.9, "y": 273.1, "heightAboveTerrain": 0.0, "elevation": 0 } ],
  "segments": [ { "id": 2, "prefab": "v:Double Train Track", "start": 3469, "end": 13593,
                  "bezier": [[x,y,z],[x,y,z],[x,y,z],[x,y,z]],
                  "elevationStart": 0, "elevationEnd": 0, "kind": "ground", "reversed": true, "cs1Info": "Train Track" } ],
  "stats": { "segments": 1234, "skipped": 56, "nodes": 1100, "outsideCrop": 4000, "crossingCropEdge": 40, "unmapped": 16,
             "rbConfigs": 22, "byPrefabType": { "RoadBuilderPrefab": 1100, "TrackPrefab": 134 } },
  "zoning": { … }, "water": { … }, "transit": { … }, "traffic": { … }, "buildings": { … }, "districts": { … },
  "objects": { … }, "surfaces": { … }, "terrain": { … }, "outsideConnections": { … }
}
```

- **`source.leftHandTraffic`:** `true` when the CS1 city drives on the left (`manifest.traffic.leftHand`, exporter 1.2.1+), `false` when it drives on the right; absent when the export predates the field, and the importer then detects it from the lanes. It applies to the whole city (also without a `traffic` section): the importer sets the CS2 city's handedness in Prepare map and matches lane connections accordingly. In a left-hand city the CS1 lanes are mirrored relative to the segment direction; the plan's lane refs stay as in CS1. report.json says so (`kind: "traffic"`).
- **Crop and transform:**
  - `crop` is a square in CS1 coordinates (no rotation), at most 14 336 m (the CS2 playable area).
  - CS2 position = CS1 position + (`dx`, `dz`), so the crop centre lands at the CS2 map origin. `bounds` is the crop in CS2 coordinates.
- **Height** (`heightMode: "terrain-relative"`): the importer puts each node at CS2 terrain height + `heightAboveTerrain`. The bezier `y` values are informational only.
- **Elevation:** `elevationStart` / `elevationEnd` is the height above terrain at each end, passed to CS2 as the course elevation. It's > 0 for elevated roads and bridges and < 0 for tunnels; ground segments are 0, values under 5 cm are 0.
- **`kind`:** `ground`, `elevated`, `bridge`, `slope` or `tunnel`.
- **Orientation (`reversed`):** a CS1 segment with the Invert flag has its lanes mirrored. The plan builds every course from `start` to `end`, so for such a segment it swaps `start`/`end`, reverses the `bezier`, swaps the elevations and sets `"reversed": true`. Building start → end then gives the right lane order and one-way direction. Lane offsets in `traffic` and stop sides in `transit` are in this plan frame.
- **`cs1Info`:** the segment's own CS1 NetInfo (the variant name for elevated, bridge, slope and tunnel segments), for traceability.
- **Nodes and segments:**
  - Only nodes used by an included segment are listed.
  - A segment is included when both its nodes are inside the crop and its prefab maps to something.
  - Building sub-networks (CS1 `Untouchable` segments: station tracks, park paths) are left out.
  - Everything excluded is counted in `stats` (`skipped` = crossing the crop edge + not placeable / unmapped; `outsideCrop` is separate) and explained in `report.json`.
- **`requires`:** the CS2 mods the plan needs: `RoadBuilder` when there are RB prefabs, `Traffic` when `traffic` has priority signs, lane arrows, lane connections or U-turn rules.
- **`prefabs` keys** are opaque ids used by `segments[].prefab`:
  - `type`: `RoadPrefab`, `RoadBuilderPrefab`, `TrackPrefab` or `PathwayPrefab`;
  - `name`: the CS2 prefab name (for a Road Builder road, its config id);
  - `source`: the CS1 ground NetInfo(s);
  - RB variants only: `imt` (lane overrides, e.g. `"6=bus,8.5=bus"`; the name is historical), `lanesFrom` (`IMT`, `TM:PE` or `IMT+TM:PE`) and `speedKmh` (the variant's speed when TM:PE changed it). Each (road, overrides, speed) combination is its own RB config.
  - `matched: true` (web 0.6.0, docs/DECISIONS.md #100): a vanilla `RoadPrefab` used because the Road Builder road cs1conv would generate for this road type (or one of its IMT / TM:PE variants, then with `imt` and `lanesFrom`) has the same lanes. The key is `v:<name>`, like any vanilla prefab. Lane references (`traffic` lane arrows and connections) count the vanilla road's driving lanes, which are the same as the generated road's by construction (same driving lanes per side, in the same order). A road matches when, per side of the centre line, it has the same driving lanes (car / bus / tram) with the same directions, each within 0.5 m in width and 0.75 m in position; parking and bike lanes on the same sides; the same median (within 1 m); a total width within 1 m; the same speed (within 5 km/h); and no trees on the generated road. The vanilla layouts come from `cs2-roads.json` (the importer's `roadlayouts` exec op).

### `zoning` (M2)

```json
"zoning": { "theme": "NA", "cellSize": 8,
            "prefabs": ["NA Residential Low", "NA Commercial Low", "Industrial Manufacturing", "Office High"],
            "cells": [[271.3, -1316.9, 0], [265.6, -1322.5, 1]],
            "stats": { "cells": 76658, "blocks": 5546, "outsideCrop": 43887, "roadNotInPlan": 1574 } }
```

- **`cells`:** `[x, z, prefabIndex]` per zoned CS1 cell: the cell centre in CS2 coordinates and an index into `prefabs` (CS2 zone prefab names). The importer zones the CS2 cell under each point. CS2 creates the zone blocks itself along roads that generate zoning (our RB roads do).
- **Which cells:** valid, zoned cells whose centre is in the crop and whose CS1 block belongs to a segment in the plan.
- **Mapping:** ResidentialLow → `<theme> Residential Low`; ResidentialHigh → `<theme> Residential High|Medium|Mixed` (user option, default High); CommercialLow/High → `<theme> Commercial Low/High`; Industrial → `Industrial Manufacturing`; Office → `Office High|Low` (option, default High). `theme` is `NA` (default) or `EU`. Unzoned and Distant cells are not listed.

### `water` (M2)

```json
"water": { "seaLevel": 181.98, "wetFraction": 0.12, "maxDepth": 14.5,
           "sources": [ { "cs1Index": 0, "x": 120.0, "z": -40.5, "y": 202.47, "targetHeight": 268.08, "outputRate": 65535 } ] }
```

- **`seaLevel`:** CS1 sea level in absolute metres; the same datum as CS2 heights once the terrain step has applied the CS1 heightmap.
- **`sources`:** CS1 *natural* water sources in the crop (CS2 coordinates; `y` is the output height). `targetHeight` is the water surface height the source aims for, `outputRate` CS1's internal rate. Pump, drain and cleaner sources belong to water buildings and are left out.
- **`wetFraction` / `maxDepth`:** how much of the crop was under water in the CS1 snapshot, for the terrain step to sanity-check its result. `null` / 0 without a depth grid.

### Terrain files (`terrain/`) and `terrain`

The web tool and the CLI write these when the `.cs1x` has a terrain. They are byte-identical to `terrain/convert.mjs` for the same crop and transform (`--no-png`); a web test compares them on the Skibidi export.

| File | Content |
| --- | --- |
| `terrain/heightmap.r16` | 4096 × 4096 little-endian uint16, row 0 = minimum z (south), column 0 = minimum x (west), pixel centres at `−size/2 + (i + 0.5) × 3.5` m over the 14 336 m playable area, centred on the CS2 origin (= the crop centre). 32 MB, deflated in the zip (about a third). |
| `terrain/worldmap.r16` | Same layout over the 57 344 m world map around it (the CS1 map, edge-clamped outside it). Optional: `--no-world`. |
| `terrain/terrain.json` | `terrain/convert.mjs`'s `terrain-cs2.json`: `heightScale`, `heightOffset`, `heightMode`, `encoding`, `crop`, `transform`, `source`, `stats`, `world { file, size, stats }`. `file` and `world.file` are relative to `terrain/`. |

**Coordinates are absolute.** The terrain files cover the CS2 map fixed on the origin, and `terrain`, `water.apply`, `water.checkpoints` and `outsideConnections` are in plan/CS2 coordinates, used as they are. The importer's `offsetX`/`offsetZ` is only a test placement on a flat spot of a base map without converted terrain. Whenever the package's terrain is applied, roads (and everything else) are imported with offset 0, as `tools/e2e` does.

Height in metres = `raw / 65535 × heightScale + heightOffset`. The default mapping is absolute (`heightScale` 1024, `heightOffset` 0): a CS2 height equals the CS1 height, so plan `y` values, the sea level and water heights need no conversion. Pass `(heightScale, heightOffset)` to CS2's `TerrainSystem` as they are (`terrain/apply.mjs` does).

The plan points at them:

```json
"terrain": { "dir": "terrain", "meta": "terrain/terrain.json", "heightScale": 1024, "heightOffset": 0, "heightMode": "absolute",
             "seaLevelDry": 98, "min": 118.173, "max": 653.517, "world": true }
```

`seaLevelDry` = `floor(min) − 20`: the sea level `terrain/apply.mjs` uses ("auto") when the city has no water data, so the map is dry. With water data, use `water.apply.seaLevel`.

### Road-conformed terrain (docs/DECISIONS.md #110–113)

The ground follows the road instead of the road following the ground. Agreed by the web worker, worker-3 and worker-4 on 2026-11-21; the importer side is in `cs2-importer/NOTES-road-heights.md`. Two builders implement the burn, the web core (`web/src/core/terrain/conform.ts`, web 0.7.0, implemented) and `terrain/lib` (worker-4). They must stay **byte-identical**, so this text is the contract for both.

**Plan fields:**
- **`segments[].halfWidth`**: the CS1 NetInfo's `m_halfWidth` in metres (the segment's own NetInfo, else its ground NetInfo), the whole road including sidewalks. It's on every segment, because one prefab key can stand for several CS1 types of different widths.
- **`prefabs[key].heightMode`**: `conform` | `follow` | `walls`, on every prefab (see the importer notes for what each does to the road).
  - **Defaults (user #113):** `conform` for roads whose CS1 NetInfo allows zoning (`aiFields.m_enableZoning` not `false`); `walls` for roads that don't (highways, ramps); `conform` for tracks; `follow` for pedestrian and bike paths.
  - **Choosing it:** the user picks it per CS1 type in the mapping table, or with `--height-mode` / `--height-mode-by-type` on the CLI.
  - **Shared keys:** a mode other than the category's default (`follow` for paths, `conform` for everything else) is folded into the key, e.g. `v:Highway Oneway - 3 lanes|walls`. Keys stay opaque, so `prefabs[key].heightMode` is always unambiguous.
- **`terrain.roadConformed`**: `true` when the heightmap was burned, mirroring `terrain.json`. `--no-conform` on the CLI writes the natural terrain, for before/after comparisons.

**Where the burn runs:** on the 4096² playable-area grid, after the bilinear resample, in CS1 metres, before the height mapping and quantization (r16 orientation: row 0 = minimum z). Pixel `(col, row)` has its centre at `x = −size/2 + (col + 0.5) × cell`, `z = −size/2 + (row + 0.5) × cell` (cell 3.5 m), in plan coordinates.
- `H0` is the resampled natural grid (its float32 values).
- All burn arithmetic is float64. Each burned pixel's result is stored back into the float32 grid.
- The height mapping (`fit` mode's min/max) is chosen from the burned grid.
- The world map is not burned.

**What burns:**
- Segments with `kind` `ground` or `slope` whose prefab's `heightMode` is `conform`, in plan order (ascending `id`).
- **Safety:** a segment is skipped and counted (`conform.skipped`) when any of its sample points `P_k` is more than 30 m above or below `H0` at the pixel containing it, i.e. `col = clamp(floor((x + size/2) / cell), 0, N − 1)` and the same for `row`.
- After the segments, one junction disc per node used by at least one burned segment, in ascending node `id`.
- Elevated, bridge and tunnel segments don't burn, and nor do `follow` and `walls` segments.

**Per segment:**
1. **Bezier.** The plan bezier `a, b, c, d` (`x, y, z`; `y` is the absolute CS1 height).
   - `B(t)`: `u = 1 − t`, `b0 = u*u*u`, `b1 = 3*u*u*t`, `b2 = 3*u*t*t`, `b3 = t*t*t`, then `B = b0*a + b1*b + b2*c + b3*d` per coordinate, summed in that order.
2. **Sampling.**
   - `L` = the 3-D length of the 16-piece chord polyline `B(j/16)`, `j = 0…16`, summed in order.
   - `n = max(1, ceil(L))`.
   - Sample points `P_k = B(k / n)` for `k = 0…n`.
3. **Nearest point** for a pixel centre `p`, in x/z only:
   - For each piece `P_k P_(k+1)`: `u = clamp(((p − P_k) · (P_(k+1) − P_k)) / |P_(k+1) − P_k|², 0, 1)`, using 0 for a zero-length piece. The nearest point is `Q = P_k + u (P_(k+1) − P_k)`.
   - `d = Math.sqrt(dx*dx + dz*dz)` from `p` to `Q`. Never use `Math.hypot`: it can differ in the last bit between engines.
   - `h_road = P_k.y + u × (P_(k+1).y − P_k.y)`.
   - Take the smallest `d`, with a strict `<`, so the lowest `k` wins a tie.
4. **Zones:** `w = halfWidth`, `Δ = h_road − H0(p)`, blend width `b = min(40, max(3.5, 2 × |Δ|))`.
   - **Core** (`d ≤ w`): weight 1, target `h_road`.
   - **Blend** (`w < d ≤ w + b`): `t = (d − w) / b`, `s = t*t*(3 − 2*t)`, weight `1 − s`, target `h_road`.
   - Beyond that the segment doesn't touch the pixel.
   - The factor 2 keeps the smoothstep's steepest point near 1 : 1.33 (about 37°). The 3.5 m minimum is one cell. Any pixel window that contains the polyline's bounding box grown by `w + 40` gives the same result.
5. **Wet pixels.**
   - `depth` = the CS1 water depth bilinearly sampled at `(x − dx, z − dz)`, exactly like the terrain (0 without water data). `sea` = the CS1 sea level (none without water data).
   - A pixel is wet when `depth > 0` or `H0 < sea`. On a wet pixel `b` is 3.5 m, and a blend-only result is capped at `surface = max(sea, H0 + depth)`.
   - Cores are not capped.

**Junction discs:** radius `r` = the largest `halfWidth` among the burned segments at the node, height = the node's `y`, centre = the node's `x, z`, and `d` = the distance to the centre. It's a core within `r` and a blend beyond it, with the same `b`, `t`, weight and wet rules as a segment.

**Combining contributions per pixel** (a sparse map of touched pixels is enough):
- **Core:** if any core covers the pixel, `h` = the target of the core with the smallest `|Δ|` (strict `<`, so the first one in processing order wins a tie).
- **Blend only:** with `w4 = (w*w)*(w*w)` per contribution, `target = Σ(w4 × h_road) / Σw4` (both sums in processing order), `W = max w` and `h = H0 + W × (target − H0)`. On a wet pixel, `h = min(h, surface)`.
- **Untouched:** `h = H0`.

**Water sources:** after the burn, every `water.apply.sources[]` entry whose pixel (the pixel containing its `x, z`, as in the safety rule) changed by more than 1 mm gets `height = round2(max(1, height + H0 − H))`, where `H` is the burned value there. Its absolute level stays as on natural terrain. Both water converters do this.

**`terrain.json`** gains:
- `roadConformed: true`;
- `conform: { segments, skipped, nodes, blendFactor: 2, minBlend: 3.5, maxBlend: 40, maxDifference: 30, pixelsChanged, maxRaise, maxCut, belowSeaLevelPixels, before: { min, max } }`. `belowSeaLevelPixels` counts core pixels whose road height is below the sea level; worker-4 checks the Cacao and Skibidi coasts in game, and a channel rule is added only if water gets in. `stats` holds the burned grid.

`plan.terrain.roadConformed` mirrors it.

### `water.apply` and `water.checkpoints`

Added to the M2 `water` section when the `.cs1x` has `water.json`:

```json
"water": { "seaLevel": 181.98, "wetFraction": 0, "maxDepth": 0, "sources": [ … ],
           "apply": { "seaLevel": 181.984009, "sources": [ { "x": 1200.5, "z": -40.2, "height": 12.3, "radius": 20, "polluted": 0,
                                                           "cs1": { "id": 1, "type": "natural", "targetHeight": 268.08, "inputRate": 65535, "outputRate": 65535 } } ] },
           "checkpoints": { "wet": [ { "x": 0, "z": 0, "cs1Depth": 3.2 } ], "dry": [ … ], "totals": { "wet": 103264, "dry": 556776 } } }
```

- **`apply`** is exactly what `terrain/mapprep.mjs water` sends to MapPrep `water.set`. Set the sea level, clear the base map's sources, add these, then reset the water to the sea level.
  - Order (terrain worker): `water.set { seaLevel, clear: true, sources }` in phase `tool` (the `cs1` field is ignored), then `water.reset { mode: "sea" }`.
  - Without `apply`, `terrain/apply.mjs --sea-level auto` sets `terrain.seaLevelDry` and resets the water to it.
  - `sources` = `convertWater()`: CS1 natural sources inside the playable area (50 m margin); `height` is the water height above the source's ground.
  - Plus `regionSources()`: CS1 rivers and lakes above the sea that would otherwise stay dry. These are inflow sources at the playable edge, lake sources for enclosed water without a source, `lake-boost` sources for big lakes that have one, and up to 8 `<kind>-spread` sources (radius 40 m) spread over long regions so rivers fill along their length; `cs1.kind` says which. The web tool's port matches `terrain/lib/water.mjs` (tested).
  - Coordinates are plan coordinates, like the nodes.
- **`checkpoints`:** up to 100 wet and 100 dry points of the CS1 depth grid inside the playable area (`depthCheckpoints()`), for an optional wet/dry check after the water has settled (`terrain/mapprep.mjs water-check`).

### `outsideConnections`

```json
"outsideConnections": { "connections": [ { "node": 4711, "x": -1500, "z": 220.4, "type": "road", "info": "Highway", "cs1Segments": [123], "outward": [-1, 0] } ],
                        "skipped": [], "counts": { "crossing": 155, "typed": 20, "connections": 20, "road": 13, "train": 7 } }
```

- Where a highway (CS1 class `Highway`) or a train track leaves the crop, the imported end node becomes a CS2 outside connection.
- `x`, `z` is that plan node; `outward` points away from the crop; `type` is `road` or `train`.
- Same as `terrain/lib/connections.mjs` `deriveConnections()` (without `allRoads`); MapPrep `oc.create` takes `{x, z, type}`.
- **Order:** run `oc.create` after the road import, in phase `tool`. It looks for the imported node within 3 m of `x`, `z`; `node` is the plan node id, so the importer can also resolve the CS2 node from its node map, as it does for `traffic`.

### `transit` (M2)

```json
"transit": { "lines": [ { "cs1Id": 6, "name": "Bus Line 5", "number": 5, "cs1Type": "Bus", "line": "Bus Line", "stopPrefab": "Bus Stop",
                          "color": "#05fe00", "vehicles": 5, "requiresStations": false, "truncated": true,
                          "stops": [ { "cs1Node": 4824, "x": 296.9, "z": 110.5, "y": 261.15, "segment": 22033, "side": "right", "t": 0.5, "cs1Building": 0 } ] } ] }
```

- **`line`:** CS2 line prefab: `Bus Line` (Bus; TouristBus, IntercityBus and Trolleybus approximated), `Tram Line`, `Subway Line` (Metro), `Passenger Train Line`, `Passenger Ship Line` (Ship, Ferry), `Passenger Airplane Line`. Monorail, cable car, helicopter, blimp, walking tours and taxis have no CS2 line and are reported.
- **`stopPrefab`:** `Bus Stop` / `Tram Stop` for kerbside lines; `null` for station-based lines, which have `requiresStations: true`: their stops are stations (`cs1Building`), which come with buildings later.
- **`stops`** are in line order (CS2 closes the loop). A kerbside stop is kept only when it stands on a segment in the plan: `segment` is that CS1 segment id (a plan segment), `side` is the road side in the plan frame (`right` = right of start → end), `t` the position along the plan segment (0 = `start`). Stops outside the crop or off the plan are dropped (`truncated: true`); lines with fewer than two stops left are not listed.
- **`color`:** the line colour as `#rrggbb`.
- **`depotMode`** (optional): `{ "Metro": "edge" | "manual", "Train": "edge" | "manual" }`, one key per rail line type that has lines in the plan (keys as in `lines[].cs1Type`). Rail and subway yards are never placed inside the converted city (docs/DECISIONS.md #83). `"edge"` (the default) asks the importer to place a compact yard (SubwayYard02 / RailYard02) just inside the crop where that type's track leaves it, and link it; when no track of that type leaves the crop, nothing is placed and the import report asks the user to place one. `"manual"`: nothing is placed, and the report says so for every line of that type. The user chooses per type in the web tool (Overview → Options) or with the CLI's `--metro-depot` / `--train-depot`. Bus and tram lines get default depots in `buildings` instead (`addDefaultDepots`).

### `traffic` (M2): TM:PE, traffic lights

```json
"traffic": {
  "requires": ["Traffic"],
  "nodes": [ { "id": 1927, "trafficLights": true }, { "id": 2302, "trafficLights": false, "allWayStop": true } ],
  "segmentEnds": [ { "segment": 11715, "node": 1927, "priority": "stop", "forbid": ["left", "right"], "crosswalk": false, "uturn": true } ],
  "laneConnections": [ { "node": 29571, "from": { "segment": 3077, "ordinal": 3, "count": 4, "offset": 7.5, "dir": "forward", "cs1LaneIndex": 5 },
                                         "to":   { "segment": 27189, "ordinal": 3, "count": 4, "offset": 7.5, "dir": "forward", "cs1LaneIndex": 5 } } ],
  "laneArrows": [ { "node": 10252, "lane": { "segment": 35152, "ordinal": 2, "count": 4, "offset": 1.5, "dir": "forward", "cs1LaneIndex": 3 }, "arrows": ["straight"] } ]
}
```

- **Left-hand traffic:** see `source.leftHandTraffic` in the plan header; lane refs here are as in CS1 (mirrored in a left-hand city).

- **`nodes`:** every junction in the plan with its CS1 traffic-light state (node flag `TrafficLights`, with or without TM:PE): the importer adds or removes CS2 lights where CS2's default differs (vanilla `TrafficLights` / `RemoveTrafficLights`). `allWayStop: true` when TM:PE has a Stop sign on every road into the junction (vanilla all-way stop).
- **`segmentEnds`:** per (segment, node):
  - `priority`: TM:PE `main` / `stop` / `yield` sign → Traffic mod priority sign;
  - `forbid`: turns no lane of that end may make (`left`, `straight`, `right`), derived from TM:PE lane arrows only when every inbound car lane of the end has custom arrows → vanilla `ForbidLeftTurn` / `ForbidStraight` / `ForbidRightTurn`;
  - `crosswalk`: TM:PE pedestrian crossing allowed → vanilla `AddCrosswalk` / `RemoveCrosswalk`;
  - `uturn`: TM:PE U-turn rule → Traffic mod.
- **Lane references** (`laneConnections`, `laneArrows`): `ordinal` is the lane's index among the segment's driving lanes left → right in the plan frame, `count` their number (car lanes as the CS2 road has them, the way the importer counts them: car, car+tram and bus lanes. Bicycle-only lanes, tram-only lanes (web 0.6.0, docs/DECISIONS.md #102) and lanes the segment's Road Builder variant turns into a median, from an IMT island, don't count; a TM:PE rule on such a lane is left out and reported), `offset` the CS1 lane centre in metres (+ = right of start → end), `dir` its direction in the plan frame. RB roads and matched vanilla roads (`matched: true`) keep the CS1 driving-lane count and order, so match by `ordinal`; `offset` is CS1 metres and only a tie-breaker.
- **`laneConnections`:** TM:PE road lane connections (traffic from `from` may only continue into the listed `to` lanes at `node`) → Traffic mod lane connector. Track connections are reported.
- **`laneArrows`:** TM:PE custom lane arrows → Traffic mod (also partly expressed as `forbid`).
- **Not in `traffic`:**
  - TM:PE speed limits become RB variants (`prefabs[*].speedKmh`; CS2 has one speed per road, the segment's most common custom speed is used, then the NetInfo's custom default);
  - bus-only vehicle restrictions become RB bus lanes (`lanesFrom: "TM:PE"`).
- **Report only:** timed traffic lights (the junction keeps plain lights), turn on red, entering blocked junctions, lane changing, parking restrictions, other vehicle restrictions, and all Node Controller settings.

### `buildings` (M3)

```json
"buildings": {
  "generator": { "matcher": "1.0.0", "web": "1.0.0" },
  "catalogue": { "game": "1.6.4f1 (1097.158a)", "prefabs": 263 },
  "cs1AngleConvention": { "facing": "-sin,cos", "agreement": 1, "buildingsWithRoad": 108 },
  "items": [ { "id": 422, "cs1Info": "Hospital", "type": "BuildingPrefab", "prefab": "Hospital01", "score": 0.93, "family": "hospital",
               "x": 1012.4, "z": -340.2, "front": [1020.1, -330.0], "facing": [0.6, 0.8], "lot": [23, 10], "cs1Lot": [4, 4],
               "accessSegment": 1234, "alternatives": ["Hospital02"], "fallback": "nearby", "district": 3 } ],
  "stats": { "items": 108, "withRoad": 108, "withoutRoad": 0, "growablesInCrop": 7226,
             "skipped": { "outsideCrop": 4023, "subBuilding": 0, "growable": 7226, "skip": 372, "noEquivalent": 8, "roadNotImported": 4 } }
}
```

- **What's placed:** ploppable CS1 buildings (services, stations, depots, parks, landmarks, utilities) in the crop, matched to a CS2 building prefab by the buildings worker's matcher (`buildings/match/match.mjs`) or chosen by the user per building type in the web tool's Assets tab.
- **What's not placed:**
  - growables, which CS2 grows itself on the converted zones (counted in `growablesInCrop`);
  - sub-buildings;
  - things CS2 rebuilds itself (pillars, pipe junctions, poles: `skip`);
  - types without a CS2 equivalent (libraries, taxi stands, cable car stops: `noEquivalent`);
  - buildings whose access road isn't in the plan (`roadNotImported`).

  report.json lists each not-placed type (`kind: "buildings"`).
- **Item fields:**
  - `x`, `z`, `front`, `facing` are in plan coordinates, like `nodes`.
  - `front` is the closest point of the CS1 access road; the placer snaps it to the imported road and keeps the lot on the CS1 side.
  - `facing` is the unit vector building → road. Without an access road (`front: null`) it comes from the CS1 angle under the convention learned from the buildings that have one (`cs1AngleConvention`).
  - `accessSegment` is a `plan.segments[].id`.
  - `score` is the match score 0–1, `null` when the user chose the prefab.
  - `alternatives` are the next-best prefabs, tried in order when the first doesn't fit; empty when the user chose the prefab.
  - `district` is a `plan.districts.items[].cs1Id`.
  - `type` is always `BuildingPrefab`.
  - **Default depots:** CS2 lines get their vehicles only from a depot of their type. For each bus or tram line type in `transit` without a depot of that type among the items, the plan adds one item from `buildings/lib/plan.mjs` `addDefaultDepots`: `id` < 0, `cs1Info: null`, `cs1Lot: null`, `default: true`, `reason`, `fallback: "nearby"`, at the line's first stop. `buildings.defaultDepots` lists them and `stats.defaultDepots` counts them. Metro and train yards are never added here: see `transit.depotMode`.
  - `fallback` says what the placer does when the building doesn't fit at its CS1 spot, after slides and alternatives (`buildings/README.md` §5):
    - `nearby`: free land along an imported road within the run's `nearbyRadius` (150 m), then `wideRadius` (400 m);
    - `anywhere`: the same, then any free connectable spot along an imported road in the crop, closest to the CS1 spot first;
    - `skip`: no free-land search.

    The web tool writes it on every item. It defaults to `anywhere` for CS2 lots over 400 cells (more than 20 × 20: universities, power plants, incinerators, prisons, cargo terminals) and `nearby` otherwise. The user can change it per type in the Assets tab, or with the CLI's `--fallback` (all types) and `--fallback-by-type <map.json>`.
- **Same shape as the buildings worker's standalone file:** the items match `buildings/lib/plan.mjs` `buildBuildingsPlan()` field for field (plus `type` and `district`; `fallback` as given in `fallbackByType`); a web test checks this on the real Skibidi export. Its standalone `cs1conv-buildings.json` stays valid for its placer.

### `districts` (M3)

```json
"districts": {
  "items": [ { "cs1Id": 3, "name": "Old Town", "outlines": [[[-300, -300], [100, -300], [100, 0], [-300, 0]]],
               "policies": ["Heavy Traffic Ban"], "cs1Policies": ["HeavyTrafficBan", "Tourist"], "area": 120000 } ],
  "cityPolicies": ["Recycling"]
}
```

- **`outlines`:** the district's outer boundaries in plan coordinates (counter-clockwise, one polygon per separate piece), traced from the CS1 district raster's cell edges and clipped to the crop. Holes are filled, because CS2 districts are simple areas.
- **`policies`:** CS2 policy prefabs with a CS1 equivalent:

  | CS1 | CS2 |
  | --- | --- |
  | `HeavyTrafficBan` | `Heavy Traffic Ban` |
  | `CombustionEngineBan` | `Combustion Engine Ban` |
  | `Recycling` | `Recycling` |
  | `EncourageBiking` | `Urban Cycling Initiative` |
  | `BikeBan` | `Bicycle Traffic Restriction` |
  | `PowerSaving` | `Energy Consumption Awareness` (approximation) |
  | `PaidParking` | `Roadside Parking Fee` (approximation) |
  | `ParkingFees` | `Roadside Parking Fee` (approximation) |
  | `SpeedBumps` | `Speed Bumps` |

  The names are CS2 PolicyTogglePrefab / PolicySliderPrefab names listed from the running game (`CS2_POLICIES` in `web/src/core/m3/districts.ts`, checked by a test). CS2 has no small-business or smoke-detector policy: `SmallBusiness` and `SmokeDetectors` are reported, not converted. `cityPolicies` maps the CS1 city-wide policies the same way. The importer applies each policy where CS2 allows it (district or city).
- **Not converted, reported:** zone specialisations (Leisure, Tourist, Organic, Hightech, SelfSufficient, specialised industry), other policies, and park areas (parks, industry areas, campuses, airports).

### `utilities` (M3, web 0.8.0, docs/DECISIONS.md #115)

The player's own CS1 power lines and water pipes **away from the converted roads**, for the buildings worker's placer (CS2 power line / CS2 pipe). CS2 roads carry low voltage, water and sewage themselves, so lines along a road are left out.

```json
"utilities": { "formatVersion": 1,
  "segments": [ { "id": 714, "utility": "pipe", "cs1Info": "Water Pipe", "start": 24249, "end": 13965,
                  "bezier": [[515.1, 311.9, -261.5], …], "elevationStart": 0, "elevationEnd": 0, "kind": "ground" } ],
  "stats": { "power": 6, "pipe": 70, "alongRoad": 1054, "alongRoadPower": 0, "alongRoadPipe": 1054, "buildingNet": 0, "crossingCrop": 43 } }
```

- **Selection:** CS1 segments with ai `PowerLineAI` (`power`) or `WaterPipeAI` (`pipe`) with both nodes in the crop. Building sub-networks (`Untouchable`) are counted in `buildingNet` and left out, and segments with one node outside are counted in `crossingCrop`.
- **Along a road:** a segment counts as along a road when at least 70 % of its 10 sample points (`t = (k + 0.5) / 10`) lie within a plan road segment's `halfWidth` + 4 m. Plan road segments are those whose prefab is a `RoadPrefab` or `RoadBuilderPrefab`, of any kind. Such segments are counted in `alongRoad` and left out.
- **Fields** are as in `segments`, in plan coordinates:
  - `start` / `end` are the CS1 node ids, never reversed, so segments chain and their ends can snap to plants, pumps and outlets;
  - `kind` is `ground`, `elevated` (CS1 elevated or bridge) or `tunnel`;
  - `elevationStart` / `elevationEnd` follow the `segments` rule.
- **Order:** sorted by id. The section is absent when the crop has no power lines or pipes, or with `--no-utilities`.

### `objects` and `surfaces` (M3)

Both sections are built by `buildings/lib/objects.mjs` (`buildObjectsSection`, `buildSurfacesSection`), the same code the scripted flow uses for `cs1conv-objects.json` (`buildings/README.md` §6). The web tool calls it unchanged, so the rows are the same in both. The importer reads them from the plan, or from `cs1conv-objects.json` next to it.

```json
"objects": {
  "generator": { "vegetation": "1.0.0" },
  "prefabs": ["EuropeanSpruce01", "SpruceTree01", "GreenBushWild01"],
  "items": [[0, -69.31, 422.1, 198.5, 0.34], [1, 416.38, -913.13, 125.6, 0.545]],
  "stats": { "items": 12260, "skipped": { "outsideCrop": 180637, "noEquivalent": 0 },
             "byType": { "Conifer": { "cs2": "SpruceTree01", "decision": "place", "reason": "same species (spruce), height 21.2 vs 21 m", "count": 5724 } } }
},
"surfaces": {
  "items": [ { "cs1Node": 1147, "styleType": "FillerGravel", "type": "SurfacePrefab", "prefab": "Sand Surface 01",
               "polygon": [[509.75, 1430.97], [515.58, 1432.4], …] } ],
  "notConverted": { "FillerSolid rgba(136,136,136,224)": 31 },
  "stats": { "items": 44 }
}
```

- **`objects.items`:** `[prefabIndex, x, z, angleDegrees, age]` per free-standing CS1 tree or rock in the crop, in plan coordinates. `age` is CS2 `ObjectDefinition.m_Age` (0.3–0.55 = adult), deterministic per tree. Trees are matched by `buildings/match/vegetation.mjs`: same species when CS2 has it, else same kind and closest height, preferring the zoning theme (`EU_` / `NA_`). CS1 `Rock 0N` → CS2 `Boulder0N`; other props are counted in `byType` and `skipped.noEquivalent`, not placed.
- **Catalogue:** matching trees needs the CS2 trees and boulders in the catalogue. The web tool's bundled slim catalogue has them. Without a catalogue, or with one that lists only buildings, `objects` is left out and the summary says why.
- **`surfaces.items`:** IMT fills at junctions (`markingType: "Node"`) whose style has a CS2 surface: `FillerPavement` → `Pavement Surface 01`, `FillerGrass` → `Grass Surface 01`, `FillerGravel` → `Sand Surface 01`. `polygon` is the fill outline in plan coordinates. A fill is in the crop when its centroid is. Painted fills have no CS2 surface: they are counted in `notConverted` and reported (`imt-node-fill`).
- Each section is present only when it has items. The CLI's `--no-objects` and `--no-surfaces` leave them out.

### `report.json` (package)

```json
{ "format": "cs1conv-report", "formatVersion": 1,
  "items": [ { "level": "info|warning", "kind": "width-loss", "message": "…", "cs1": { "segment": 394 }, "count": 12 } ] }
```

- **`kind`** is one of:
  - roads: `width-loss`, `lane-approximation`, `unmapped-netinfo`, `not-placeable`, `mapping-override`, `speed-limit`;
  - IMT: `imt-override`, `imt-node-fill`, `imt-partial-fill`, `imt-paint`;
  - crop and source: `outside-crop`, `crossing-crop-edge`, `crop`, `source`;
  - M2: `zoning`, `water`, `transit`, `traffic`, `node-controller`;
  - M3: `buildings`, `districts`, `objects`, `surfaces`;
  - terrain and water: `outside-connections`.
- **`count`:** how many CS1 objects an aggregated item stands for.
- **`cs1`:** sample ids (`segment`, `segments`, `node`, `nodes`, `netInfo`, `filler`, `line`).
