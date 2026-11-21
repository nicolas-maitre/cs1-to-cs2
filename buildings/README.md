# buildings

M3, CS2 side: turns the CS1 export's buildings into CS2 buildings.

The pipeline has four parts:
1. **CS2 prefab catalogue:** read from the running game.
2. **Matching:** each CS1 building type is matched to its best CS2 prefab. Pure, tested, and importable by the web tool.
3. **Buildings plan:** placements in import-plan coordinates.
4. **Placement:** an exec library builds them in a CS2 city where the roads were imported.

Decisions and their reasons are in [DECISIONS-buildings.md](DECISIONS-buildings.md).

```
.cs1x (assets.json, buildings.json, network.json) ─┐
cs2-catalogue.json (from the game) ─────────────────┼─ match/match.mjs ─ lib/plan.mjs ─▶ cs1conv-buildings.json ─▶ Cs2Buildings.dll (place) ─▶ CS2 city
cs1conv-plan.json (road plan: crop, transform) ─────┘
```

## Quick start

```powershell
node buildings/run.mjs catalogue          # starts CS2 if needed (cs2.lock), writes the catalogue (+ slim copy), quits CS2 if it started it
node buildings/run.mjs match --cs1x $env:LOCALAPPDATA\cs1-to-cs2\samples\skibidi.cs1x
node buildings/run.mjs plan  --cs1x $env:LOCALAPPDATA\cs1-to-cs2\samples\skibidi.cs1x --plan <package dir> --out .gamectl\buildings\b.json
node buildings/run.mjs place --buildings .gamectl\buildings\b.json --save <cs2 save with the roads> --offset 0,0 --shots --write-save cs1conv-m3-buildings
node --test buildings/match/match.test.mjs buildings/lib/plan.test.mjs
```

`--offset` is the importer's own offset (cs2-importer `run.mjs` defaults to `2000,-4000`; the e2e flow uses `0,0`). Every command that drives CS2 takes `%LOCALAPPDATA%\cs1-to-cs2\cs2.lock` and waits while another owner holds a fresh one.

## 1. Catalogue: `cs2-catalogue.json`

`Cs2Buildings.dll` op `catalogue` (phase main; the main menu is enough) walks `PrefabSystem`'s prefabs and writes every `BuildingPrefab`, `BuildingExtensionPrefab` and `StaticObjectPrefab`. It takes under 1 s.

| Field | Source |
| --- | --- |
| `name`, `kind` (`building` / `buildingExtension` / `object`), `type` | prefab |
| `lot` `[width, depth]` (8 m cells), `accessType` | `BuildingPrefab` |
| `size` `[x, y, z]` m, `height` | ECS `ObjectGeometryData` (measured meshes) |
| `uiGroup`, `uiMenu`, `uiPriority` | `UIObject` (menu category, e.g. `Healthcare`, `SignaturesCommercial`) |
| `service` | `ServiceObject` |
| `zone`, `level` | `SpawnableBuilding` (zoned buildings) |
| `theme`, `content` (DLC), `assetPacks` | `ThemeObject`, `ContentPrerequisite`, `AssetPackItem` |
| `placeable` | built from a menu: `PlaceableObject` or `SignatureBuilding`, with a UI group, not spawnable, not a service upgrade |
| `upgrade`, `signature` | `ServiceUpgrade` (extensions like "MedicalClinic01 Extension Wing"), `SignatureBuilding` |
| `capacity` | one number per service family: `patients`, `students`, `jail`, `prisoners`, `fireEngines`, `bodies`, `garbage`, `electricity`, `water`, `sewage`, `depotVehicles`, `mail`, `shelter`, `parking`, `coverageRange`, … |
| `components` / `flags` | every authoring component: primitive, enum and string fields, prefab references by name; field-less ones in `flags` |

**On this machine** (CS2 1.6.4f1): 3571 buildings and 14 146 objects, 10 MB. 314 of the buildings are placeable once upgrades are removed.

**`cs2-catalogue-buildings.json`** is the slim copy for the web tool: only placeable buildings, and only the components the matcher reads. It's 263 buildings and 116 KiB, and gives the same matches as the full catalogue. `slimCatalogue()` in `match.mjs` makes it.

## 2. Matching: `match/match.mjs`

A pure ES module: no I/O, no dependencies, deterministic, JSDoc-typed. The web tool imports the same file.

```js
import { matchAssets } from './buildings/match/match.mjs';
const m = matchAssets(assetsJson, catalogue, { theme: 'European' });
m.byName['Medical Clinic'];
// → { decision: 'place', cs2: 'MedicalClinic01', score: 1, reason: 'same family (hospital); same kind (clinic); patients 100 vs 100; lot 4×4 vs 11×6; name 100%', alternatives: [...] }
```

**Decisions:**
- `place`: a CS2 equivalent was found.
- `growable`: a zoned CS1 building. It's not placed, because CS2 zoning grows its own.
- `skip`: not a building in CS2 terms: pillars, poles, pipe junctions, outside connections, rocks, sub-buildings.
- `noEquivalent`: for example libraries, taxi stands, cable car stations, ferry depots.

**Algorithm:**
- Both sides are classified into a **family** (hospital, school, power, park, depot, station, landmark, …) and a **kind** (education level, power source, transport type, clinic or hospital, …).
- Same kind is a filter. For depots, stations, cargo terminals and schools it is strict: no prefab of that kind means `noEquivalent`.
- Score: 0.55 + 0.10 (kind) + 0.25 × capacity closeness + 0.10 × lot closeness + 0.08 × name similarity, capped at 1. Parks, parking and landmarks have no capacity, so their name weighs 0.20 and their lot 0.15.
- Ties go to the preferred theme, then to the name.

**Skibidi.02:**

| Decision | Types | Instances |
| --- | --- | --- |
| place | 46 | 255 |
| growable | 335 | 10 328 |
| skip | 38 | 1143 |
| noEquivalent | 4 | 15 |

Examples:
- **Strong matches:** Medical Clinic → MedicalClinic01 (1.00), Fire House → FireHouse02 (1.00), High School → HighSchool03 (0.97), Elementary School → ElementarySchool02 (0.96), Hospital → Hospital01, Train Station → TrainStation02.
- **Weaker matches:** Crematory → Crematorium01 (0.71) and Combustion Plant → IncinerationPlant01 (0.68), because CS2's capacities are far larger.
- **No equivalent:** Library, Taxi Stand, Cable Car Station, Ferry Depot.

## 3. Buildings plan: `lib/plan.mjs` → `cs1conv-buildings.json`

`buildBuildingsPlan(cs1, catalogue, roadPlan)` is pure. Its output has the shape proposed for the import plan's `buildings` section:

```json
{ "format": "cs1conv-buildings", "formatVersion": 1, "generator": { "matcher": "1.0.0" }, "catalogue": { "game": "1.6.4f1 …" },
  "crop": { … }, "transform": { "dx": -3750, "dz": 1250 },
  "cs1AngleConvention": { "facing": "-sin,cos", "agreement": 1, "buildingsWithRoad": 108 },
  "items": [ { "id": 3010, "cs1Info": "Medical Clinic", "prefab": "MedicalClinic01", "score": 1, "family": "hospital",
               "x": -165.5, "z": 598.1, "front": [-160.2, 611.9], "facing": [0.36, 0.93], "lot": [11, 6], "cs1Lot": [4, 4],
               "accessSegment": 21345, "alternatives": ["MedicalClinic02", …] } ],
  "stats": { "items": 108, "withRoad": 108, "withoutRoad": 0,
             "skipped": { "outsideCrop": 4023, "subBuilding": 0, "growable": 7226, "skip": 372, "noEquivalent": 8, "roadNotImported": 4 } },
  "types": [ { "cs1", "count", "cs2", "score", "reason", "alternatives" } ],
  "notPlaced": [ { "cs1", "decision", "reason", "count" } ] }
```

- **Coordinates:** `x`, `z` and `front` are in plan coordinates (CS1 + `transform`), like the road plan's nodes.
- **`front`:** the closest point of the CS1 access road (`accessSegment`) to the building.
- **`facing`:** the unit vector from the building to its road. Without a road it comes from the CS1 angle.
- **CS1 angle convention:** facing = (−sin a, cos a). Learned from the data: it agrees for 108/108 buildings with a road.
- **Roads not imported:** a building whose access road isn't in the road plan is skipped (`roadNotImported`).

## 4. Placement: `Cs2Buildings.dll` op `place`

Runs in phase tool with pump tool: `node buildings/run.mjs place …`, or `gamectl cs2 exec buildings/bin/Release/net48/Cs2Buildings.dll --phase tool --pump tool --args '{"op":"place","plan":"…","offsetX":0,"offsetZ":0}'`.

1. **Snap:** the front point snaps to the nearest real CS2 road edge (`snap`, default 16 m).
   - The lot's front edge is put on the road's side, facing the road, on the side of the road where the CS1 building was.
   - The building is set at the road's height, so the lot terraforms to the road.
2. **Fit:** the lot must stay clear of every road surface, existing buildings and lots placed earlier in this run.
   - If it doesn't fit, the lot slides along its road (±4 … ±24 m, `maxSlide`), first keeping its front at least 12 m from the edge's ends (`endMargin`), then without that rule.
   - Then the matcher's next-best prefabs (`alternatives`) are tried the same way.
3. **Create:** one `CreationDefinition` (Permanent) + `ObjectDefinition` + `Updated` + `Deleted` per building, created in ToolUpdate. This is the game's own recipe, from the zone spawner. CS2 generates the lot's sub-objects and areas itself.
4. **Verify:** each building is looked up in the city; `connected` means CS2 linked it to a road edge.

**Report:** `.gamectl/buildings/<ts>/report.json`, with per item `status` (`placed` / `collision` / `noRoad` / `prefabMissing` / `notCreated`), reason, position, yaw, lot, alternative and slide.

**Other ops:** `catalogue`, and `inspect {x, z, r}` (buildings, road edges with lanes and heights, and all edges near a point).

### Result: Skibidi.02, 3 km crop at (3750, −1250), save `cs1conv-e2e-20261121-0530`

This is the e2e city (Plains, converted CS1 terrain and water, 2035 roads), saved as **`cs1conv-m3-buildings`**.

| | |
| --- | --- |
| buildings to place | 108 (all with an imported access road) |
| placed | **77** (36 slid along their road, 8 with an alternative prefab), in 0.9 s |
| connected to a road | **71**, all to the road they were snapped to |
| placed, not connected | 6: their road side is `Raised` / `Lowered` (embankment walls) where the imported road runs 4–7 m above the converted terrain |
| refused (collision) | 31: the CS2 lot doesn't fit between CS1's roads. CS2 service lots are much bigger: a CS1 crematory is 3 × 3 cells, CS2's Crematorium01 is 8 × 10. |

| Family | Placed (connected) | Collisions |
| --- | --- | --- |
| school | 22 (22) | 7 |
| parking | 17 (17) | 2 |
| hospital | 12 (10) | 1 |
| park | 9 (8) | 1 |
| fire | 5 (4) | 1 |
| police, landmark, station | 3 each | police 1 |
| deathcare | 2 (1) | 8 |
| depot | 1 (1) | 1 |
| garbage, cargo, prison | 0 | 6, 2, 1 |

Screenshots are in `.gamectl/buildings/<ts>/` (git-ignored).
