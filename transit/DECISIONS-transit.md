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

8. **Rail and subway yards only at the crop edge** (docs/DECISIONS.md #83, the user's decision).
   - **Where:** `yards.plan` looks for the open ends of each rail type's imported track within 80 m of the crop bounds, places the compact prefab there (`SubwayYard02` / `RailYard02`, `requireRoad`, `maxDistance` 2500), then `tracklink` joins the yard to the line.
   - **No yard:** a type whose track doesn't leave the crop, a manual `depotMode`, or a yard that won't fit gets a report entry per line: "… place a subway/rail yard yourself and connect its track to the line".

9. **`tracklink` also joins station platforms to the imported track** (each open end to the nearest imported dead end within 200 m).
   - **Why:** CS1 station platforms are building sub-nets (not imported), so the CS2 station's own platform tracks end loose next to the line.

10. **Link course recipe:** every link is built the player's way: a Temp definition plus one forced apply frame. ApplyNetSystem then makes the level crossings and junctions.
    - **Why not Permanent:** a Permanent course across an existing edge leaves Temp ghosts (Spike 1), and one that starts on a building-owned node (a station or yard track end) is silently not built (QA §5c).

11. **Open:** train and metro lines still get no path. The causes are in QA §5c; the station-side ones (elevated metro matching, station position) were fixed by the buildings worker (buildings decision #36) and are being verified.

12. **Imported track under a long CS2 platform is trimmed back.** CS2 train stations are 200 m long, CS1's 128 m, so at the CS1 spot each platform runs about 36 m past the imported track's dead end.
    - **How:** when the nearest imported dead end lies behind a platform end (closer to the station centre, within 16 m of its axis: TrainStation03's platforms sit 10.5 m off the CS1 track), `tracklink` deletes the imported edges from that dead end up to the first node more than 8 m beyond the platform end, and joins the platform end to that node.
    - **Limits:** it stops (and only reports) at a junction under the platform or after 8 edges; junctions are never deleted.
    - **Why:** joining back to the dead end would lay a second track on top of the platform, and the line couldn't pass through the station.

13. **Facing imported dead ends are bridged across a missing station.** CS1 station platforms that no CS2 station reproduces leave a gap in the line, for example the metro platforms of Skibidi's "Large Trainstation", which CS2's TrainStation03 doesn't have (buildings worker, 2026-11-21).
    - **How:** after the station links, `tracklink` joins pairs of unowned dead ends of the same track type (subway, train) that are mutual nearest within 300 m, where the straight course leaves each end within 30° of its own track's direction. Ends already joined to a station or yard are skipped; `bridge:false` turns it off.
    - **Result:** the line passes through and loses that stop, which stays reported as a missing stop. That's a known limit until the plan can place a companion metro station.

14. **Interior yards, opt-in only.** `transit.mjs depots --interior-yards` (or `depotMode {Metro|Train: "interior"}` in the plan) lets a rail type whose track doesn't leave the crop get a yard anyway: the compact prefab beside an open dead end of its track inside the area (not one a station link or bridge will use), continuing it, then linked like an edge yard.
    - **Default unchanged:** decision #8 / docs/DECISIONS.md #83 (edge yards, otherwise a "place it yourself" report entry) stays the default.
    - **Why:** on Skibidi's 6 km crop the metro track never leaves the area. So the metro lines, which now have paths, can only get vehicles from an interior yard; this verifies that the chain works end to end.

    - **Grade limit on yard links (also opt-in, with `--interior-yards`):** `tracklink` then only joins the yard to line nodes at least 20 m away and within 6 % grade (`maxGrade`). In run 12, an 8 m link climbing 1.8 m was silently not built. In run 13, the 110 m link at 5.8 % was built, but the yard's trains still stayed parked (QA §5, rail hunt stopped by #133).

15. **Rail hunt round 2 (#159): the yard and some station joints have no lane connection.** The directed lane graph (`lanes.reach`, validated against CS2's own computed paths) shows the yards' track lanes reach nothing outside the yard. `lanes.joint` shows a building-owned joint node without switch node lanes and with dead-end lane flags on both sides (QA §5d).
    - **Tried, all opt-in behind `--interior-yards`, none fixed it:** the link in the yard's own two-way track prefab (`yardLinkPrefab: "yard"`); Updated on the joint nodes, their edges and the owning building after the apply (`touchEnds`).
    - **Next lead:** build the link with CS2's own net tool, and compare with the one working station joint.

16. **Smooth links (#160, opt-in): a train leaves its yard.** Track links must leave their end along the end's own track and arrive along the target's track (`tangentLinks`, a cubic curve). Sharp joints get no switch lanes in CS2, so nothing can drive through them.
    - **On Skibidi 6 km:** the rail yard connects to the network (246 lanes reachable from the yard), and a Train Line 1 train runs to its station.
    - **Opt-in with `--interior-yards`:** `tangentLinks` is on there; `--no-tangent-links` turns it off.
    - **`touchEnds` breaks line paths:** it's off unless `--touch-ends` is passed, as a diagnostic only.
    - **Metro needs the yard aligned with the line** (QA §5e).
    - **Worth considering as a default for station links:** after an acceptance run.

17. **Front-only links in the mod's rail step (opt-in `ConnectRail`, rail round 3, QA §5g).** A station or yard link must not double back: a joint that sharp gets no lanes (#16). The mod passes `frontOnly`.
    - **Straight fallbacks:** only to targets within 60° of the end's outward direction, or to a dead end under the platform that is then trimmed. If the trim fails, there's no link.
    - **The trim test** runs along the platform's own track, not from the building's centre.
    - **Yard bridges:** smooth candidates only, with each ramp sized to the grade limit.
    - **Result:** on Skibidi 6 km, the yard's train leaves the yard and runs on Train Line 1 (1 of 1), and every road gate stays green.
    - **Still open:** the bridge descent meets the elevated one-way track at the same height, and its landing isn't joined (heights: 6 unexplained missing segments). Train Line 1 still has no path, because the west terminus sits 14–30 m off CS1's tracks and its link stays straight.
    - **Why opt-in:** the heights gate fails, and the train runs only through an accidental junction.
