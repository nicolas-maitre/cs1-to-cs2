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
node --test buildings/match/match.test.mjs buildings/match/vegetation.test.mjs buildings/lib/plan.test.mjs buildings/lib/objects.test.mjs
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

**`cs2-catalogue.slim.json`** is the slim copy that the web tool bundles: only placeable buildings, and only the components the matcher reads, with the game version in its header (`game.version`). It's 263 buildings, 116 KiB, about 11 KB gzipped, and gives the same matches as the full catalogue. `slimCatalogue()` in `match.mjs` makes it; `node buildings/run.mjs slim` rewrites it from an existing catalogue.

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

**Plan items** may carry `type` (the prefab type, default `BuildingPrefab`); the web tool's plans set it.

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

## 5. When a lot doesn't fit: place it yourself, or the opt-in nearby, wide and anywhere fallbacks

**Default (user decision, DECISIONS.md #106, buildings decision #38): never moved to free land.** The placer tries the exact CS1 spot, a slide along the same road (±4 … ±`maxSlide` m), then a **smaller** alternative of the same kind (lot area at most the match's). If none fits, the item fails as `collision` with "not placed … place it yourself at its CS1 position (x, z)", and the report gives `cs1` (the plan position). Stations always work this way, with slides of at most ±`stationMaxSlide` (8) m (decisions #36, #37).

**Opt-in, per plan item (`fallback: "nearby"` or `"anywhere"`):** when neither a slide nor an alternative fits, the placer searches for **free land nearby**, and alternatives of any size are tried:
- **Candidates:** every imported road edge within `nearbyRadius` m (default 150) of the CS1 position, both sides, every 6 m. It skips spots within `endMargin` m of the edge's ends, and road sides with an embankment or trench wall (`Raised` / `Lowered`).
- **Order:** closest first, measured as the distance of the lot centre to the CS1 centre. The match and its alternatives are tried at each spot, with the same fit test.

**Passes:** `nearby` (default) searches within `nearbyRadius` (150 m), then within `wideRadius` (400 m; 0 turns it off). `anywhere` does the same, then searches every imported road in the crop, closest to the CS1 spot first (up to `maxAnywhereTries`, 20 000). Each pass only tries spots farther than the previous radius.

**Report:** every placed item has `distance` (m between the placed and the CS1 lot centre) and, when a pass moved it, `fallbackPass` and `moved`. The summary has `movedByPass` and `distanceStats` (median, p90, max, over 50/150/400 m). On Skibidi with `anywhere`: 108/108 placed, max 1222 m (the University, Prison, Cargo Center and Combustion Plants; decision #28).

**Choosing per building type:** a plan item's `fallback` is `"nearby"`, `"anywhere"` or `"skip"`. The run option `fallback` (default `"skip"`, since 0.3.1) applies when an item has none; the default depots below carry an explicit `"nearby"`, because the converter adds them and they have no CS1 spot.
- `buildBuildingsPlan(…, { fallback, fallbackByType: { [cs1Name]: 'nearby' | 'anywhere' | 'skip' } })` writes the field only when one is chosen, so the default plan doesn't change.
- Run options: `nearbyRadius`, `wideRadius`, `maxNearbyTries` (4000), `maxAnywhereTries`.
- Per item: `requireRoad: true` skips highways (no building access) everywhere; `maxDistance: <m>` caps every pass, so the item fails rather than landing farther away (decision #35).

**Default depots:** when the plan has transit lines of a type with no depot among the items, `buildBuildingsPlan` (via `addDefaultDepots`, also used by the web tool) adds one `BusDepot01` / `TramDepot01` item at the line's first stop, `default: true`. Metro and train yards are planned in game by transit/'s `yards.plan` at the crop edge where the line's track leaves, in the same item shape, placed by this placer and linked by `tracklink` (decision #30, DECISIONS.md #83).

**Idempotent:** an item whose prefab (or an alternative) already stands within 30 m + the slide range of its spot is `alreadyPlaced`, so re-runs on a saved city don't stack duplicates.

**Pumps on dry land (decision #42):** a surface-water pump whose lot has no water at its CS1 spot first tries the slides toward water within `pumpShoreReach` (30) m, up to that far along its road. With no water near, it becomes `groundwaterPump` (GroundwaterPumpingStation01) where the ground has water; otherwise it is placed as planned and reported as dry. When the game's water maps read empty (they can from an exec op), pumps stay as planned (`pumps.noWaterData`). The report's `pumps` counts them, and `connectRoad` lists every power, water or sewage building placed without a road edge ("connect a road to it": no electricity reaches it otherwise). `warnings` says when no power plant or no water facility was placed at all (decision #41).

## 6. Trees, props, districts, IMT surfaces: `cs1conv-objects.json`

```powershell
node buildings/run.mjs objects-plan --cs1x <file> --plan <road plan> --out .gamectl\buildings\o.json [--synthetic-districts]
node buildings/run.mjs objects --objects .gamectl\buildings\o.json --buildings .gamectl\buildings\b.json --save <cs2 save> --offset 0,0 --shots --write-save cs1conv-m3-objects
```

`lib/objects.mjs` (pure, tested) builds three sections. Coordinates are plan coordinates.

| Section | Content |
| --- | --- |
| `objects` | `{ prefabs: [CS2 names], items: [[prefabIndex, x, z, angleDeg, age], …] }`: the crop's trees and props, mapped by `match/vegetation.mjs`. `age` is CS2 `ObjectDefinition.m_Age`; 0.3–0.55 = adult, deterministic per tree. |
| `districts` | `{ items: [{ name, type: "DistrictPrefab", prefab: "District Area", polygon: [[x, z], …], policies: [CS2 names], cs1Policies, cs1Id, cells }], parksNotConverted }`: the outer boundary of each 4-connected component of each district in `districts.u8`, inside the crop. |
| `surfaces` | `{ items: [{ type: "SurfacePrefab", prefab, polygon, cs1Node, styleType }], notConverted }`: IMT junction fills whose style has a CS2 surface. |

**Vegetation matching** (`match/vegetation.mjs`, pure, tested):
- The kind (bush / conifer / palm / deciduous) is a filter; then the species by name; then the closest height, with the theme prefix (EU_ / NA_) breaking near-ties.
- Fruit trees and flowering bushes are used only when the CS1 name asks for them.
- CS1 `Rock 0N` → CS2 `Boulder0N`. Other props are skipped and reported.
- **Skibidi:** Tree2variant → NA_LondonPlaneTree01, Tree3variant → EU_PoplarTree01, Beech01low → OakTree01, Bush01 → GreenBushWild01, the 8 rocks → the 8 boulders.

**Placer ops:**
- **`objects`:** a Permanent object definition per tree or prop, 2000 per frame. A tree on a CS2 road surface (+1.5 m, `roadClearance`) is moved perpendicular to just past the road edge when that is at most `roadsideMove` m (6) and the spot is free; otherwise it is skipped, as are trees inside a building lot. Skibidi: 705 moved (mean 3.1 m), 654 dropped (was 1359; decision #27).
- **`areas`:** one area definition per district or surface (CreationDefinition + `Node` polygon).
  - Districts are then named the way `NameSystem.SetCustomName` does it, and get their policies as a `Game.Policies.Modify` event, the way `PoliciesUISystem.SetPolicy` does it.
  - Both are done with the EntityManager: those methods use end-frame command buffers, which exec code may not create.
- **`list`:** prefabs by class name, for example `node buildings/run.mjs list --type "AreaPrefab|PolicyPrefab"`.

### Result: Skibidi 3 km, save `cs1conv-e2e-embank-fix` → **`cs1conv-m3-objects`**

That save is worker-3's re-import with embanked roads snapped to the terrain.

| | |
| --- | --- |
| buildings | **96 / 108 placed**: 36 slid, 11 alternatives, **20 moved to free land** (median 56 m, max 124 m, 14 onto another road). **94 connected.** 12 still refused (6 combustion plants, 2 cargo centres, 2 crematories, a prison, a university: their CS2 lots are 25 × 27 cells and similar). |
| trees and rocks | 4216 in the crop → **2744 created in 0.6 s** (fit checks 0.29 s, creation 13 ms, 2 frames). 1359 skipped on a CS2 road surface, 113 inside a new building lot. |
| districts | Skibidi has none, so 2 synthetic districts were used (an L shape and a rectangle, through the same raster → polygon path). **Both created and named; "Heavy Traffic Ban" set.** `HighriseBan` has no CS2 counterpart and is reported. |
| IMT junction fills | Not converted (see decisions 20–21). |

Screenshots are in `.gamectl/buildings/objects-20261121-053717/`.

## 7. Using the steps from a mod: `src/Steps.cs`

The same C# runs inside the CS2 importer mod (Cs2ImporterMod) as import steps, after the roads. It isn't a separate DLL; the mod compiles it:

```xml
<!-- cs2-importer/mod/Cs2ImporterMod.csproj -->
<Compile Include="..\..\buildings\src\**\*.cs" Link="buildings\%(RecursiveDir)%(Filename)%(Extension)" />
```

```csharp
// Mod.OnLoad
Cs2Buildings.Steps.RegisterAll(ImportSteps.Register);   // registers buildings, objects, districts, surfaces, utilities, powerhook (in that order)
```

| Arg | Meaning |
| --- | --- |
| `plan` | `cs1conv-plan.json` or its folder; `buildings` comes from the plan, `objects` / `districts` / `surfaces` from the plan or from `cs1conv-objects.json` next to it |
| `offsetX`, `offsetZ` | the importer's offset |
| `buildingsPlan`, `objectsPlan` | explicit files instead of the plan's sections |
| `buildings.<option>`, `objects.<option>`, … | passed to that step without the prefix, e.g. `buildings.fallback`, `buildings.nearbyRadius`, `objects.batchSize` |

Each step yields once per frame batch and ends with a result dictionary (the same as the exec op's JSON); a missing section gives `{ "skipped": "…" }`. No extra references are needed beyond what the mod already has. Versions: `Steps.Version` (see [docs/RELEASE.md](../docs/RELEASE.md)).

## 8. Power lines and pipes: op `utilities` (decision #40)

`utilities {plan, offsetX, offsetZ, kinds="power,pipe", powerPrefab="Low-voltage Line", pipePrefab="Combined Small Pipe", snapRadius=40}` builds the plan's `utilities.segments`, the player's own off-road CS1 power lines and pipes (DECISIONS.md #115), after the buildings. Power lines are low voltage, at ground level. Pipes are one combined water and sewage pipe, 10 m down.
- **Snapping, line ends:** a building's connection marker within `snapRadius` (attached), else a road node (roads take low-voltage lines and pipes locally), else another utility node.
- **Snapping, inner nodes:** these snap only to a marker, when a CS1 line runs through the building.
- **Report:** `endsSnapped`, `innerNodesToMarkers`, `snappedTo` (by target and layer) and `notSnappedSample`.
- **Where it runs:** the mod step `utilities` (options `utilities.<option>`) and the e2e step `buildings`.

CacaoCity (`cs1conv-util-cacao2`, built right-hand by mistake; CacaoCity drives on the left, see decision #42): 147 of 147 segments, 45 of 65 ends snapped; homes with water went from 0 to 43 %. Electricity stays at 0 % there: none of the 7 power plants could be placed at its CS1 spot (#106), and the map had no outside connections yet.

**Road ends and the road guard (decision #45):** a road-snapped end stops 3 m short of the road node (`roadSetback`) with `DisableMerge`, so CS2 joins it to the road by local connection and no utility node sits on a road node. Highways aren't targets. The result's `roadGuard` compares every road edge's start and end node before and after the step; `ok` is false on any change.

## 9. Power plants onto the grid: op / step `powerhook` (decision #44)

`powerhook {lvReach=200, hvReach=600, transformers=false, transformer="TransformerStation01"}` runs after `utilities`.
- **High-voltage plants** (coal, gas, incineration), opt-in `transformers` (default off; mod setting "Add a transformer station next to each imported power plant"): a transformer by the nearest road, plus a high-voltage line from the plant's marker to the transformer's. Off, the report's `needTransformer` names each such plant.
- **Low-voltage markers** (wind turbines, the plants' own) get a low-voltage line to the nearest non-highway road node, or chain to an already linked marker.
- **Report:** `needTransformer`, `transformersLinked`, `hvLines`, `lvLines`, `roadGuard`, `ok`.
- **Limit:** CS2 only puts a producer into the grid through its own road connection, so off-road producers still need a road; the placer's `connectRoad` lines name them.

Full-city Skibidi (`cs1conv-stress-full`): 3 transformers and 10 links, every not-connected notification gone, the road-side turbines produce. 8 off-road producers still need a road.
