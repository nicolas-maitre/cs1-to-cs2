# Buildings decisions (M3, CS2 side, 2026-11-21)

All taken by Claude while the user was asleep; review them. To be merged into `docs/DECISIONS.md`.

1. **The catalogue comes from the managed prefabs, dumped generically.**
   - `PrefabSystem.m_Prefabs` (private, read by reflection) holds the authoring components that matching needs: UI group, service, zone and level, theme, DLC, asset packs, and every service component with its capacities and ranges.
   - Every primitive, enum or string field of every component is written, with prefab references by name. So a new CS2 component or DLC needs no code change.
   - The ECS side adds only the measured size (`ObjectGeometryData`).
   - It runs from the main menu in under 1 s.

2. **"Placeable"** means the player can build it from a menu: `PlaceableObject` or `SignatureBuilding`, a UI group, not spawnable, and not a service upgrade.
   - Two corrections came from looking at the real data:
     - service upgrades ("MedicalClinic01 Extension Wing") first slipped in as standalone buildings;
     - signature buildings have no `PlaceableObject` and were first missing.
   - Result: 314 placeable buildings out of 3571.

3. **Matching = the same "family" on both sides, the "kind" as a filter, then a score.**
   - **Why kind is a filter:** a high school must never become an elementary school because the elementary school's capacity is closer (a real failure in the first tests), and solar must stay solar.
   - **Strict kinds:** depots, stations, cargo terminals and schools. A ferry depot with no CS2 ferry depot is `noEquivalent`, not a subway yard.
   - **Score:** 0.55 family + 0.10 kind + 0.25 capacity + 0.10 lot + 0.08 name, capped at 1. Parks, parking and landmarks have no capacity, so their name weighs 0.20.
   - **Name similarity** is the Jaccard index of name words. It fixed "Decoration Concrete Parking Lot" → ParkingLot (was BicycleParkingHall) and "University" → University01 (was MedicalUniversity01).
   - **Capacity:** CS1 `aiFields` and CS2 `capacity` are compared per family (patients, students, jail, fire engines, electricity, …). Units differ between the games; the score uses min/max ratios only, so it ranks, it doesn't convert.
   - **Clinics:** a hospital under 150 patients (and not named "hospital") is the "clinic" kind on both sides.

4. **Growables are reported, never placed.** CS2 zoning grows its own; the plan counts them under `skipped.growable`.
   - Zoning itself is the importer's `zoning` section (M2).

5. **Not buildings in CS2 terms** → `skip` with a reason:
   - pillars and pylons: CS2 places them;
   - power poles and pipe junctions: networks;
   - outside connections: map features;
   - wildlife spawn points;
   - decoration rocks: props in CS2, not buildings;
   - sub-buildings: they come with their parent;
   - dam power houses: CS1 dams are networks.

6. **The matcher and plan builder are pure ES modules** (`match/match.mjs`, `lib/plan.mjs`) with `node --test` tests (13).
   - The web tool can import them as they are; proposed to worker-2.
   - **Slim catalogue for the browser:** `slimCatalogue()` gives the same matches as the full catalogue. Checked on Skibidi, where all 423 types come out identical; the slim file is 116 KiB instead of 10 MB.

7. **Plan coordinates, as the road plan.**
   - Items are in CS1 + transform; the importer's offset is applied at placement.
   - Each item carries the CS1 road-side point (`front`), the direction to the road (`facing`), its `accessSegment` and the ranked `alternatives`.
   - The CS2 placer snaps the front to the real imported road, so small differences between the CS1 curve and the CS2 edge don't matter.

8. **CS1 angle convention learned from the data**, not assumed: facing = (−sin a, cos a). It agrees for 108/108 buildings with a road, and is used only for buildings without one.

9. **Placement uses the game's own definition recipe** (`ZoneSpawnSystem.SpawnBuildingJob`, by IL inspection):
   - one `CreationDefinition` (Permanent) + `ObjectDefinition` + `Updated` + `Deleted` entity per building, in ToolUpdate;
   - a CS2 lot's front is local +Z at depth × 4 m (`BuildingUtils.CalculateFrontPosition`).
   - CS2 generated the lot's sub-objects (parking markings, vehicles, hedges, playgrounds) by itself, so no explicit sub-area or sub-net definitions were needed.

10. **Lots go on the road side, at the road's height, on the side of the road where the CS1 building was.**
    - **Why the road's height:** a building at terrain height next to an embanked road ended up below it. The game's own tool puts the building at road level and terraforms the lot.

11. **Collisions are checked before placing, and a collision is reported, not forced.**
    - **What counts:** the road's surface (centre line sampled every 2 m, against the lot rectangle shrunk by 1 m), existing buildings and lots placed earlier in this run (separating-axis test).
    - **Why not just place:** CS2 doesn't refuse an overlapping Permanent definition, so an unchecked lot would sit on a road. A first check sampled the lot on a coarse grid and let a 12 m road through.
    - **Fallbacks:** slide along the road (±24 m, first keeping the front 12 m from the edge's ends), then the matcher's alternatives.
    - **Effect on Skibidi:** placed went from 62 to 77 of 108, with 36 slid and 8 using alternatives.

12. **Buildings whose access road wasn't imported are skipped** (`roadNotImported`). A CS2 building without a road connection is useless, and placing it at the CS1 spot could block a road that was imported elsewhere.

13. **The test city is the e2e save `cs1conv-e2e-20261121-0530`** (Skibidi 3 km, converted terrain, 2035 roads). The result was saved as **`cs1conv-m3-buildings`**; the bridge refuses any other prefix.
    - The plan was written to `.gamectl/buildings/`, not into the e2e package (worker-3's folder).

14. **CS2 is shared.**
    - `buildings/run.mjs` uses the importer's lock helpers (`cs2-importer/lib/driver.mjs`, imported, not changed): it waits while another owner holds a lock under 2 h old, and releases the lock at the end.
    - It quits CS2 only if it started it.

## Open issues

- **6 placed buildings aren't connected.** Their road side is `Raised` / `Lowered` (embankment walls), because the imported road runs 4–7 m above the converted terrain there. This is for the importer and terrain workers: roads at the terrain's level, or a lower embankment threshold. The report names the road side for each.
- **31 buildings don't fit** next to their CS1 roads. CS2 service lots are much bigger: a CS1 crematory is 3 × 3 cells, CS2's Crematorium01 is 8 × 10; a CS1 combustion plant is 5 × 4 cells, CS2's IncinerationPlant01 is 25 × 27. Options for later:
  - let the user pick a smaller alternative in the web tool;
  - place them on free land nearby (a search, not a snap);
  - accept leaving the service out, which CS2 cities replace with their own.
- **A grey "Wood Street" strip** in the Skibidi e2e city has no network edge (checked in the original save). It's probably the converted CS1 terrain's road imprint, or a road the plan didn't import. A building is placed over it legitimately; worth a look by the terrain worker.
- **Capacity units differ between the games** (CS1 garbage per week vs CS2 per month, electricity in different units, …), so capacity closeness ranks but doesn't mean equal service. The CS2 buildings are generally bigger and stronger, so cities get more coverage than in CS1.
- **Themes:** the matcher prefers a theme only to break ties. Most CS2 service buildings have no theme; signature buildings do (EU_/NA_). The plan doesn't pass a theme yet; the web tool could pass the CS2 city's theme.
- **DLC prefabs** are in the catalogue only if they're loaded in the game that generated it, so a catalogue from another machine could name prefabs this game doesn't have. The placer then reports `prefabMissing` and tries the alternatives.

15. **Interface with the web tool (agreed with worker-2):**
    - **Imports:** the web core imports `matchAssets` and `MATCHER_VERSION` from `match/match.mjs`, and `closestOnBezier` from `lib/plan.mjs`, by relative path, from both Vite and Node. These exports and the match result shape (`cs2`, `cs2Lot`, `noEquivalent`) stay stable; both files stay pure, without Node APIs.
    - **Overrides:** per-type user overrides ("pick another prefab / skip") are applied in the web adapter on top of `matchAssets`, so the matcher has no override option.
    - **Plan section:** the web core builds the plan's `buildings` section with the same rules as `buildBuildingsPlan` and checks that with a vitest comparison; its items add `type: "BuildingPrefab"`. `types` and `notPlaced` go to the UI and report.json, not into the plan section.
    - **Placer:** reads `type` (default `BuildingPrefab`) and accepts either `plan.buildings.items` or a standalone `cs1conv-buildings.json`.
    - **Slim catalogue:** `cs2-catalogue.slim.json` (the name worker-2 asked for) is bundled into the static site.

# M3, round 2 (2026-11-21 ~05:35)

16. **Oversized lots get a "nearby free land" fallback, after the slides and alternatives.**
    - **Search:** road edges within 150 m, both sides, every 6 m, closest first, with the same fit test.
    - **Excluded:** spots near the edge ends (unconnectable), and road sides with `Raised` / `Lowered` walls. Also requiring the `Side.Sidewalk` flag left no candidates: the road compositions don't carry it.
    - **Choosing per building type:** item field `fallback` (`nearby` | `skip`), with the run option `fallback` as the default. The plan builder writes the field only when asked, so worker-2's comparison of default items doesn't change.
    - **Result on Skibidi:** placed went from 77 to 96 of 108. The moved ones went 8–124 m (median 56 m), 14 onto another road.

17. **Placement is idempotent.** An item already standing near its spot (same prefab or an alternative, within 30 m + the slide range) is `alreadyPlaced`.
    - **Why:** worker-3 re-imported roads into my saved city, so its buildings were already there, and a second run would have scattered duplicates via the fallback.
    - **Caveat:** buildings that a previous run moved by more than that can't be recognised.

18. **Vegetation:** a small separate matcher (`match/vegetation.mjs`), with a fixed rule order (kind → species → height → theme).
    - **Rocks:** CS1 and CS2 both ship 8 rocks/boulders, mapped one-to-one.
    - **Trees:** placed adult (`m_Age` 0.3–0.55, deterministic) with a random but deterministic rotation.
    - **Skipped:** trees on a CS2 road surface or inside an imported lot. CS2's Road Builder roads are wider than CS1's, which is why 1359 of 4216 go.

19. **Districts are polygons traced from the raster:** the outer boundary of each 4-connected component (holes dropped and reported, specks under 4 cells dropped), restricted to the crop.
    - **Prefab:** CS2 has a single district prefab, "District Area".
    - **Policies:** mapped by a small table (`DISTRICT_POLICY_MAP`, CS2 names from the game's prefab list): heavy traffic ban, combustion engine ban, speed bumps, recycling, power saving → energy consumption awareness, encourage biking → urban cycling initiative, bike ban → bicycle traffic restriction, parking fees → roadside parking fee. Everything else is counted as unmapped.
    - **Park, industry, campus and airport areas** have no CS2 district counterpart and are listed under `parksNotConverted`.
    - **Verification:** Skibidi has no districts, so the check used a synthetic input in the exporter's own format (`run.mjs objects-plan --synthetic-districts`).

20. **IMT junction fills: stopped after one test, as allowed.**
    - **Surfaces:** a CS2 surface area at a CS1 junction fill is created but invisible; CS2 draws the road and junction surface over terrain surfaces (screenshot `.gamectl/buildings/objects-20261121-053449/surface-0.png`). Pavement and grass islands on junctions would be hidden the same way.
    - **Decals:** CS2's paintable decals are fixed images (parking icons, "no parking" stripes, letters), with no colour fill. So there's nothing for red bus paint or grey paint.
    - **What stays:** `buildSurfacesSection` keeps the mapping (pavement → "Pavement Surface 01", grass → "Grass Surface 01") for fills off the road surface, which do render.
    - **Skibidi:** the crop holds only 2 junction fills, both red paint, so nothing is converted there. Segment fills remain the bus / median lane overrides (M1).

21. **End-of-frame command buffers can't be used from exec.**
    - **What happens:** `NameSystem.SetCustomName` and `PoliciesUISystem.SetPolicy` throw "Trying to create EntityCommandBuffer when it's not allowed!". The same error hits the first tool-phase exec after every `save.load`, before the op does anything.
    - **Names and policies:** done with the EntityManager instead (the name dictionary by reflection plus `CustomName` / `BatchesUpdated`; a `Modify` event entity).
    - **First exec after a load:** the driver retries it once. Reported to worker-3, who owns the bridge.

22. **Embankments:** fixed by worker-3 in the importer (embankments now snap to the terrain). In its re-imported save, 94 of 96 placed buildings connect, against 71 of 77 before.
    - **The 2 left:** one faces a trench wall (`Lowered`). One was moved onto a road side with no flags, probably a ramp; the fallback can't tell such sides apart yet.

## Release engineering

23. **The buildings C# is compiled into the importer mod, not shipped as a second DLL.** `src/Steps.cs` is the facade: `Steps.RegisterAll(Cs2ImporterMod.ImportSteps.Register)` registers four steps (`buildings`, `objects`, `districts`, `surfaces`), each an `IEnumerator` over the import's args.
    - **Why:** one mod to install, and the existing routines already yield per frame and have no exec-only dependencies (MiniJson is compiled by the mod too). The mod csproj already references every assembly they use.
    - **Recipe (sent to worker-3, who owns `cs2-importer/`):** one `<Compile Include="..\..\buildings\src\**\*.cs" Link="buildings\…" />` line and one `RegisterAll` call in `Mod.OnLoad`. Checked with a scratch project that compiles `buildings/src` plus a stub `ImportSteps` with the same signature.
    - **Args:** `plan`, `offsetX/Z`, `buildingsPlan` / `objectsPlan` overrides, and step-prefixed options (`buildings.fallback` → `fallback` for that step). A missing section returns `{skipped: …}` instead of failing the import.
    - **Names and policies** keep the EntityManager versions (#21): they work both from exec and inside a system.

24. **Versioning: a product version plus a SemVer per component.** `VERSION` = `0.<milestone>.<patch>` (0.3.0 = M3). Components: cs1-exporter 1.2.0, cs2-importer 0.2.0, web 0.4.0, buildings 0.3.0.
    - **Why:** users install the components separately (CS1 Workshop, CS2 mod, a website), so each needs a version that moves only when it changes; the product version names the release and the combined notes.
    - **One source of truth per component;** any duplicate declaration (exporter changelog heading, importer csproj `<Version>`) must match. `build.mjs --check` enforces it, in CI and before every release build.

25. **Release build: `node tools/release/build.mjs` → `release/<product version>/` (git-ignored).** Mod folders, one zip per component named `<id>-<componentVersion>.zip`, `versions.json`, `SHA256SUMS.txt`, `RELEASE-NOTES.md` built from the root and component changelogs.
    - **Mods are deployed into `release/`** through the projects' own deploy properties (`CS1ModsDir`, `CS2LocalMods`), so a release build never touches the installed games.
    - **Zips are written by a small built-in writer** (deflate, UTF-8 names) to avoid a dependency and a PowerShell call; tested by round-trip and by `Expand-Archive`.
    - **The web zip** is `web/dist` as built, which includes worker-2's `cs2-catalogue.json`.
    - **npm on Windows** runs through `cmd.exe /d /s /c` (Node's DEP0190 warns about `shell: true` with an argument list).

26. **CI: one Windows job** (Node 22, .NET 8, npm cache) that runs the version check, the web tool (test, typecheck, build), gamectl, terrain, buildings and release tests, and the CS2 importer core tests.
    - **Why Windows:** gamectl's tests and helpers resolve Windows paths; a Linux runner would test something else.
    - **Not built in CI:** anything compiled against the games' DLLs (both mods, both bridges, `buildings/src`, terrain's CS2 apply). The DLLs aren't on runners and can't be redistributed. The workflow says so in its header.
    - **Checked locally** with the same commands and an empty `LOCALAPPDATA`: all green (web 67 + 2 skipped, gamectl 46, terrain 13, buildings 23, release 3, CoreTests 20).

## Quality pass (2026-11-21, after QA)

27. **Trees on a wider CS2 road are moved, not dropped.** A tree within `halfWidth + roadClearance` of a road is pushed along the perpendicular from that road's closest point to `halfWidth + roadClearance + 0.25 m`. It's kept when that's at most `roadsideMove` m (default 6) and the new spot is off every road and lot; otherwise it's dropped as before.
    - **Why perpendicular to the nearest road:** CS1 trees along a road sit on its sidewalk or verge, so "the same side, just past the wider CS2 surface" keeps the CS1 look. A free-spot search would scatter them.
    - **Skibidi 3 km:** 3404 trees created (was 2744), 705 moved (mean 3.1 m), 654 dropped (was 1359), 158 in a lot. Screenshot `.gamectl/buildings/objects-20261121-090240/trees-close.png`: trunks on the verges, crowns overhanging the road as in a normal CS2 city.

28. **Huge lots: a wider pass and an "anywhere" fallback.** `fallback` is `nearby` (default: 150 m, then a second pass to `wideRadius` 400 m), `anywhere` (then every imported road in the crop, closest to the CS1 spot first, up to `maxAnywhereTries` 20 000 candidates), or `skip`. Later passes only try candidates farther than the earlier pass's radius.
    - **Every placed building now reports `distance`** (placed vs CS1 lot centre) and `fallbackPass`. The summary has `movedByPass` and `distanceStats` (median, p90, max, over 50/150/400 m).
    - **Skibidi 3 km, all items "anywhere":** **108/108 placed** (was 96), 105 connected. Passes: nearby 20, wide 3, anywhere 9. Median distance 9.6 m, p90 151 m, max 1222 m. The 9 "anywhere" moves (620–1222 m) are the University, Prison, Cargo Center and five Combustion Plants: lots no free land near home can hold.
    - **Per type in the web tool (worker-2):** "anywhere" when the CS2 lot has more than 400 cells, else "nearby", and a per-type select in the Assets tab.

29. **Districts accept the web tool's shape.** `plan.districts.items[]` with `outlines` (several polygons) and no `prefab` become one "District Area" per outline, each with the name and policies. `plan.districts.cityPolicies` are set on the city entity with the same `Modify` event as district policies.
    - **Why here, not in the web:** the web shape (outlines, area, cs1Policies) is the published plan format (schema §2), and one reader in C# is cheaper than two writers.
    - **Verified in game:** 2 outlines → 2 districts, both named "QA Twin District", Heavy Traffic Ban + Recycling set on both, the city-wide "Fire Safety Sprinkler System" reported as set, **wrongly**: CS2 has no such policy, and the name lookup had returned a non-policy prefab. Since then only real PolicyToggle/SliderPrefabs are used and other names are reported as `policiesMissing` (CS2 has 23 policy prefabs; list in `%LOCALAPPDATA%cs1-to-cs2qacs2-policies-all.json`).

30. **Default depots for transit lines** (`addDefaultDepots` in `lib/plan.mjs`, also exported for the web tool). CS2 lines get vehicles only from a depot of their type; CS1 metro and train lines need none, so converted cities often have lines and no depot (worker-4: Metro Line 3 had 0 trains).
    - **What it adds:** for every CS1 line type (Bus, Tram, Metro, Train) with lines but no matched depot of that type, one item: `BusDepot01` / `TramDepot01` / `SubwayYard01` / `RailYard01`, `default: true`, fallback "nearby", at the first stop of the first line.
    - **Rail yards** sit at the first *station* stop and get `needsTrackLink: true` plus a reason saying the track must be linked by hand. A CS2 yard dispatches only over track connected to the line, and snapping a yard's built-in track onto an imported track end needs more than lot placement.
    - **Skibidi:** adds RailYard01 (Train Line 1) and SubwayYard01 (Metro Lines 1–3); bus depots already exist.

31. **Release acceptance test as a script** (`tools/release/acceptance.mjs`): build the release, install the importer mod **from the release zip** (the dev build in Mods is backed up and restored), make the package with the web CLI, then press the mod's own Options buttons (Install Road Builder configs → restart → new city → Check → Import) through the exec library's `op mod`. It calls the same `Actions` methods the buttons do; no import code runs by exec. Results: `docs/QA.md` "Release acceptance".

32. **The placer's fit checks yield frames** (`frameBudgetMs`, default 20 ms): the resolve loop and the fallback candidate loop yield whenever the budget is used up. Before, every item's slides, alternatives and fallback passes ran in one frame, before the first creation batch; worker-4's 14 km Skibidi run froze CS2 for 26.8 s (bridge status age, 0 fps).
    - **Verified** on that city (`cs1conv-e2e-20261121-1025`, 253 items) with idempotency off (`idempotent: false`, new run option for stress tests), so every item collides with its twin and runs the full search: **max status age 0.94 s, min 14.4 fps**, 2803 yielded frames, 92 s. A real import does far less searching.

33. **Default depots: bus and tram only** (changes #30, after DECISIONS.md #83). `addDefaultDepots` adds only bus and tram depots. Metro and train yards go at the crop edge where the line's track leaves the crop (not in the middle of the city), so worker-4's transit/ plans them in game (`yards.plan`, open track ends near the plan bounds, SubwayYard02 / RailYard02, `depot: "edge"`) in this item shape; this placer places them unchanged and transit/'s `tracklink` connects them. No `needsTrackLink` items any more. A line type whose track never leaves the crop gets a report line instead ("place a subway yard yourself").
    - The default items' `lot` is now the catalogue's lot in cells (it was the size in metres).

34. **Placed buildings get their sub-nets** (`subNets`, default on). A building definition alone gets none: the object tool, `ZoneSpawnSystem` and `BuildingConstructionSystem` write the prefab's `Game.Prefabs.SubNet` elements (depot and station tracks, internal paths, parking roads) as separate Permanent course definitions. Found by worker-4: SubwayYard02 spawned 8 trains with no track to leave on.
    - **How:** the vanilla `CreateSubNet` recipe, in the same batch as the building definition. Each element's local curve goes to world space, elevation = local height, `IsFirst`/`IsLast | DisableMerge`, `OwnerDefinition` = (building prefab, position, rotation). Upgrade-only elements are skipped.
    - **Verified** on Skibidi 3 km (110 buildings, `.gamectl/buildings/20261121-102406`): 1461 courses, **every one of the 106 buildings with sub-nets got them** (`subNetsMissing` 0). SubwayStation03 5 elements → 13 sub-net entities (edges and nodes), SubwayYard02 7 → 18, RailYard02 35 → 84. Trains running on them: worker-4's yard test.
    - Buildings placed before this have none; re-place them in a fresh city rather than patching saves.

35. **Per-item `requireRoad` and `maxDistance`; policies checked by type** (for worker-4's crop-edge yards and QA §6 A6).
    - **`requireRoad`:** highway edges (`RoadData.UseHighwayRules`) are not candidates for that item, neither for the front snap nor in any fallback pass. A yard next to Skibidi's crop-edge highways had been placed "not connected to a road" and never dispatched.
    - **`maxDistance`:** caps every fallback pass (150 / 400 m / anywhere) so an item fails rather than landing farther than its consumer can use. RailYard02 at Skibidi's east edge: nothing within 400 m, the nearest fitting non-highway spot is 4.1 km in. With worker-4's 2.5 km link reach, `maxDistance: 2500` turns that into a clean failure and a "place a yard yourself" report.
    - **Policies:** a `PrefabID` lookup by name can return a non-policy prefab of the same name ("Fire Safety Sprinkler System" looked set, though CS2 has no such policy). Only real `PolicyTogglePrefab` / `PolicySliderPrefab` results are used now; other names are reported as `policiesMissing`. CS2 has 23 policy prefabs (`%LOCALAPPDATA%\cs1-to-cs2\qa\cs2-policies-all.json`); this map and the web tool's are equal, and a web test enforces it.
36. **Stations stay at their CS1 spot and keep their height** (worker-4's rail-route findings, QA §5c, causes 3 and 4). Buildings 0.3.1, matcher 1.1.0.
    - **Placer:** an item of family `station` is tried only at its CS1 spot: no slide along the road, and no fallback move, whatever its `fallback` says. A station that doesn't fit there fails as `stationCollision` ("place it yourself"). A moved station's platform tracks would not continue the imported CS1 track, and the line would have no path.
    - **Matcher, height:** an elevated CS1 station (name or title has "Elevated") only gets elevated CS2 stations, and a ground one only ground stations. This is a filter like the kind, so the alternatives keep the height too. Before, "Overground Metro Station Elevated 01" became the ground SubwayStation03, whose platform ends sit 12–15 m below the imported elevated track. CS2's tool rejects a link that steep, so the metro lines had no path.
    - **Matcher, terminus:** a through station never becomes a terminus (tracks end inside the lot), and the other way round.
    - **Matcher, size:** stations compare platform length (lot width; 0.8) plus a little depth (0.2) instead of lot area. Skibidi: Elevated 01 [16×4] → SubwayStationElevated01 [19×1] (alternative Elevated02); Train Station [16×6] and Large Trainstation [16×8] → TrainStation03 [25×5].
    - **Left for transit:** every CS2 train station is 25 cells (200 m) long, against 16 cells (128 m) in CS1. Even at the exact CS1 spot, the CS2 platforms overrun the CS1 track ends by about 36 m on each side, so the track link has to join or trim the imported track where the platform ends are.
    - **Verified:** matcher tests (station rules), buildings and web m3 tests pass. Not yet verified in game. It needs a new package and worker-4's `run-tracklink.mjs --recreate`; it was not part of the release-candidate acceptance (QA §7).
37. **Stations placed first, short slides, roads above and below ignored; net-attached prefabs skipped** (worker-4's test of #36 on a 6 km Skibidi: 8 of 11 stations failed as `stationCollision`, and the elevated ones got no tracks).
    - **Order:** stations are resolved before every other building. A CS2 police station 3× wider than its CS1 original had taken the Large Trainstation's spot.
    - **Slides:** a station may slide up to ±8 m along its road (`stationMaxSlide`). The 5 "Metro Entrance" lots (CS2 24 m wide against CS1 16 m) sat 5 m from a side road. A few metres along the road is what worker-4's track link absorbs (it trims the imported track under the platforms and joins the next node, DECISIONS-transit #12).
    - **Roads that don't block:** tunnel roads never collide with a lot. Elevated roads don't collide with a station either: one Train Station sits under two elevated roads, 15 and 20 m up, as in CS1.
    - **Net-attached prefabs:** `PlaceableObjectData.m_Flags & PlacementFlags.Attached` (SubwayStationElevated01: `NetObject.m_Attached`) are skipped, and the next alternative is used (SubwayStationElevated02, which brings its own elevated tracks). An attached station is built onto an existing track and has no `SubNet` elements, so placed free-standing it got 0 sub-nets, and its integrated stops attached to nothing. The report counts `netAttachedSkipped`.
      - **Why in the placer and not the matcher:** the web tool's slim catalogue doesn't carry `NetObject`.
38. **Never moved to free land by default** (user, DECISIONS.md #106; supersedes the `nearby` default of #16 and #28). Buildings 0.3.1.
    - **Default:** the run option `fallback` is now `"skip"`, and `buildBuildingsPlan` writes `fallback: "skip"` unless the caller chooses otherwise for a type (as web 0.6.1 does). An item without `fallback` is tried at its exact CS1 spot, then with slides along the same road, then with a **smaller** alternative: lot area at most that of the first usable prefab, which may fit where the match didn't. A larger alternative would only fit by covering more.
    - **Not placed:** the item's reason ends in "place it yourself at CS1 (x, z) = CS2 (x, z)", in the web report's words: CS1 = plan − transform, CS2 = plan + import offset. The report has `placeYourself`, one line per building not placed ("Hospital: place it yourself at CS1 (…) = CS2 (…) (at its CS1 spot the lot overlaps a road)"), for the import status. Each item also has `cs1` and `cs2Spot`.
    - **Opt-in:** `fallback: "nearby"` or `"anywhere"` on the item (or as the run option) keeps the free-land passes and alternatives of any size. The default bus and tram depots carry an explicit `"nearby"`: the converter adds them and they have no CS1 spot.
    - **Tests:** the rules are game-free in `src/PlacementRules.cs` (fallback, smaller alternatives, station slides, which roads block, the position text), tested by `buildings/tests/RulesTests` (`dotnet run`, in CI). `Placement.cs` calls them.
39. **Every sub-net element is built; `m_Upgrades` is passed on, not skipped** (corrects #34; found with the new `prefab` op).
    - **What was wrong:** #34 skipped `SubNet` elements whose `m_Upgrades` was set, as "upgrade-only". These are not upgrades: `m_Upgrades` holds the composition flags the edge gets (`ObjectSubNets` → `NetPieceRequirements`: station platform pieces, stairs). Vanilla `ZoneSpawnSystem.CreateSubNet` builds every element and gives it an `Upgraded { m_Flags }` component.
    - **What it cost:** SubwayStationElevated02 got none of its 6 "Double Subway Track - Station Side" edges (all have flags), so its integrated stops attached to nothing (worker-4's finding). SubwayStation03 lost 4 of its 9 elements and TrainStation03 2 of its 4: the pedestrian paths (stairs) between the street and the platforms.
    - **Fix:** every element with a prefab is created; one with flags gets `Upgraded`. `subNetsExpected` counts them all.
    - **Verified** on the 6 km Skibidi (`.gamectl/e2e/20261121-145428`, save `cs1conv-train6k-st3`): SubwayStationElevated02 6 of 6 elements (14 entities), TrainStation03 4 of 4 (10), SubwayStation01 2 of 2; `subNetsMissing` 0 over 1630 courses. Buildings 0.3.2.
    - **New op:** `prefab {names}` (read only, main menu) dumps a building prefab's `SubNet` and `SubObject` elements, placement flags and authoring components.
