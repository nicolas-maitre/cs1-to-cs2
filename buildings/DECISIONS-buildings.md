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
