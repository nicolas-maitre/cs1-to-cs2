# Transit decisions

Decisions taken autonomously (by Claude) on 2026-11-21; please review them. The evidence is in [NOTES-cs2-transit.md](NOTES-cs2-transit.md) and in the run summaries under `.gamectl/transit/`.

1. **Lines go through the line tool's own pipeline, with one forced apply frame.**
   - **How:**
     - frame N: write the route definitions (`CreationDefinition` + `ColorDefinition` + `WaypointDefinition` per stop);
     - frame N+1: set the active tool's `applyMode` to `Apply`, by reflection, so `ToolOutputSystem` runs the vanilla ApplyTool phase.
   - **Why:**
     - `GenerateRoutesSystem` always creates Temp routes (there is no Permanent path), and only `ApplyRoutesSystem` makes them real.
     - Rebuilding routes by hand (waypoints, segments, connections, path targets) would copy a lot of game logic that changes between versions.
     - The vanilla apply also triggers pathfinding.
   - **Verified:** Metro Line 3 (4 stops) and Bus Line 12 (22 stops) came out `complete`, with all segments.

2. **Kerbside stops are permanent object definitions attached to the nearest road edge** (`CreationDefinition.m_Attached = edge`), at the plan's `t` and `side`.
   - **Position:** they sit 1.5 m inside the road's outer edge and face the road.
   - **Side:** comes from the plan segment's direction, not the CS2 edge's, because the importer may have built the edge reversed.
   - **Shared stops:** one CS2 stop per CS1 (segment, side, t), so lines that share a CS1 stop share the CS2 stop.

3. **Stop prefabs follow the city theme:** `EU_BusStop01` / `EU_TramStop01` by default, `--theme NA` for the `NA_` ones.
   - **Why:** CS2 has no prefab called "Bus Stop" (the plan's generic name); the real ones are per theme.

4. **Station lines use existing station stops.** A metro or train stop resolves to the nearest stop object of the line's transport type within 200 m of the CS1 stop. Those are the station buildings' platforms, which the buildings worker places.
   - **Skipped lines:** a line with fewer than 2 resolved stops is skipped and reported ("stations come with the buildings").
   - **Why:** station placement belongs to the buildings step; transit only connects the stations.

5. **Small batches:** stops go 20 per exec call, with a 10-frame pause between calls, and lines go one per call.
   - **Why:** one call with all 145 Skibidi stops and 9 lines crashed CS2 natively, while 3 stops and one line per call worked.

6. **Names and CS1 numbers are set in a separate call in the main phase** (`lines.name`).
   - **Why:** `NameSystem.SetCustomName` creates a command buffer, which CS2 refuses during the tool phase ("Trying to create EntityCommandBuffer when it's not allowed").

7. **Vehicles come from depots, which transit doesn't place.**
   - **Why:** a line with no depot of its type exists but has no vehicles. Depots belong to the buildings step (BusDepot01 / SubwayYard / RailYard from the plan's `buildings` section).
   - **Verification:** `run-transit.mjs --depot` borrows the buildings placer (unchanged) for one bus depot.
