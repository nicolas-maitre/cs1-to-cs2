# cs2-importer (M1)

Builds the roads of a `cs1conv-plan.json` (import package, [schema/README.md](../schema/README.md) §2) in a running Cities: Skylines II city. M1 is roads only: networks, junctions, curves, bridges and tunnels.

The importer is a plain class library that the Cs2Bridge hot-loads with `exec` ([tools/PROTOCOL.md](../tools/PROTOCOL.md)). It is not a mod and isn't deployed; a rebuild takes effect on the next call, without restarting the game. Decisions and their reasons: [DECISIONS-importer.md](DECISIONS-importer.md).

## Quick start

```bash
# once per package, with CS2 closed: Road Builder reads its configs only at startup
node cs2-importer/run.mjs copy-rb <package dir>

node cs2-importer/run.mjs start                     # takes the CS2 lock, starts CS2 through gamectl
node cs2-importer/run.mjs new --name cs1conv-m1     # new city on Corral Riches (CS1CONV_MAP / --map)
node cs2-importer/run.mjs check  <package dir>      # plan + prefab check, no changes
node cs2-importer/run.mjs import <package dir> --clear   # bulldoze the area, build, verify; report in .gamectl/import/
node cs2-importer/run.mjs shots  <package dir>      # overview + close-up screenshots
node cs2-importer/run.mjs save cs1conv-m1-<name>
node cs2-importer/run.mjs stop                      # quits CS2, releases the lock
```

`check`, `import`, `verify`, `clear` and `shots` shift the plan by `--offset x,z` (default `2000,-4000`, env `CS1CONV_OFFSET`), the flattest dry spot of Corral Riches; `--offset 0,0` uses the plan's own coordinates. `run.mjs` builds the DLL (`dotnet build -c Release`, needs `CS2Managed` in `../local.props`) before each `exec`. The same ops work through plain gamectl:

```bash
gamectl cs2 exec cs2-importer/bin/Release/net48/Cs2Importer.dll --phase tool --pump tool \
  --args '{"op":"import","plan":"C:/…/cs1conv-plan.json","clear":true}' --timeout 3600
```

## Ops (`Cs2Importer.Entry.Run`, `args.op`)

| Op | exec params | What it does |
| --- | --- | --- |
| `check` | phase main | Parses and validates the plan, looks up every prefab (with a hint for missing Road Builder configs), counts CS2 roads already in the area. |
| `clear` | phase tool, pump tool | Bulldozes top-level CS2 roads in the plan's bounds + `margin` (50 m), or in `x0,z0,x1,z1`. |
| `import` | phase tool, pump tool | Optional `clear`, then the batches, then a verify pass. Returns the report; `report` writes it to a file too. |
| `verify` | phase main | Re-checks an earlier import against the plan. |
| `terrain` | phase main | Height range, water and roads in a square (`x, z, size=3200, step=50`): for picking a test spot. |
| `inspect` | phase main | Real nodes within `r` of `x, z` with their edges: for debugging a failure. |

Options: `offsetX` / `offsetZ` (0), `batchSize` (400), `cellSize` (400 m), `settleFrames` (2), `verifyFrames` (30), `groundSnap` (1 m), `minLength` (0.5 m), `shortLength` (4 m), `margin` (50 m).

## How it builds

1. **Plan → job** (`src/Core`, no game code): skip invalid segments; one elevation per node from the segment ends, plus CS1 terrain offsets of 4 m or more (trenches, embankments, clamped to −11.5 … +7.5 m), smaller offsets snap to the terrain; spatial batches.
2. **Prefabs**: `PrefabID(type, name)` for every key the segments use. A missing prefab fails its segments and is listed in the report.
3. **One position per node**: CS2 terrain height + node elevation, computed once, used by every segment at that node.
4. **Batches** in `ToolUpdate`: `Permanent` road definitions; ends shared within a batch merge into one node; ends on a node built by an earlier batch attach to its entity (`CoursePos.m_Entity`). After each batch the new nodes are looked up (within 1 m) to extend the node map.
5. **Verify**: a segment counts as built when real edges connect its two node entities. The report also has elevated and tunnel compositions, edges with an unexpected prefab, other edges left in the area and the world's `Temp` count (ghosts).

## Report

`import` returns (and `run.mjs` writes to `.gamectl/import/<tag>-report.json`):

- `plan`: segments and nodes in the plan / to build, skipped by reason, ground / elevated / tunnel, batches.
- `bounds`: the crop and the CS2 bounds actually used, and whether they fit the CS2 map.
- `prefabs`: every key with `found`, width, elevation limit, segment count and a hint when missing.
- `clear`: edges and nodes deleted, when `clear` was set; otherwise `existing_edges_in_area`.
- `batches`: per batch, segments created, ends attached to earlier nodes, nodes resolved / missing, frames, `create_ms`, `resolve_ms`, `wall_ms`.
- `result`: `segments_built` / `segments_failed`, `cs2_edges_for_plan`, `nodes_found`, `elevated_built` vs `elevated_planned`, `tunnel_built` vs `tunnel_planned`, `wrong_prefab`, `other_cs2_edges_in_area`.
- `failures_by_reason`, `failures` (10 samples per reason), `warnings_by_kind`, `warnings`, `skipped`, `temp_before` / `temp_after`, `seconds`.

## Tests

```bash
cd cs2-importer/tests/CoreTests && dotnet run                       # game-free core tests
cd cs2-importer/tests/CoreTests && dotnet run -- plan <package dir> # dry-run a real plan through the planner
node cs2-importer/samples/make-handmade-plan.mjs [--at x,z]           # regenerate the hand-made test plan
```

`samples/m1-handmade/` is a 24-segment plan: a 3×3 grid of Small and Medium roads (X and T junctions), a curve, a Medium Road bridge over a Small Road with no shared node, a Small Road tunnel and a Road Builder road (the M0 `Four-Lane Road with Decorative Trees` config, `r5266315b-…-cs1conv`, which must be in `ModsData/RoadBuilder`).

## M1 results (2026-11-21, CS2 1.6.0f1, Road Builder 0.7.0)

New city on Corral Riches, plan shifted by (+2000, −4000), simulation paused, autosave blocked by the bridge.

| Plan | Segments built | Nodes | CS2 edges | Temp | Time | Save |
| --- | --- | --- | --- | --- | --- | --- |
| `samples/m1-handmade` (batches of 5) | 24 / 24 | 23 / 23 | 33 | 0 | 1.9 s, 5 batches | `cs1conv-m1-handmade` |
| same, re-imported with `--clear` | 24 / 24 (33 edges, 32 nodes bulldozed first) | 23 / 23 | 33 | 0 | 0.7 s | – |
| Skibidi.02 crop (web tool, 3 km at (3750, −1250)) with `--clear` | **2035 / 2035** | 1670 / 1670 | 2188 | 0 | 1.6 s, 6 batches of ≤ 400 | `cs1conv-m1-skibidi` |

- **Skibidi report:** all 26 prefabs found: 22 generated Road Builder configs, plus `Double Train Track`, `Oneway Train Track`, `Double Subway Track` and `Pavement Path`.
  - No failures and no unclaimed edges in the area.
  - 130 edges have an elevated composition (109 planned CS1 bridges / elevated segments, plus ramps); 21 have a tunnel composition (10 planned).
  - Each batch takes 6–9 ms to create its definitions and 50–150 ms of wall time, including the 2 settle frames.
- **The game adds edges:** CS2 splits long courses, so 2035 segments become 2188 edges. The verifier walks through those intermediate nodes.
- **Screenshots** (local, `.gamectl/import/`, git-ignored):
  - `handmade-*`: the grid with crosswalks and traffic markings; the curve; the Road Builder tree road; a Medium Road bridge with pillars over Aspen Street, with no junction; a tunnel ramp into a lowered cutting.
  - `skibidi-overview` / `-oblique`: the whole 3 km street network: the grid, curved residential loops, the sunken highway across the middle, and the rail lines on the east side. Orange dead-end markers sit where roads stop at the crop edge.
  - `skibidi-elevated`: the highway interchange viaducts and an elevated rail line on pillars.
  - `skibidi-trench-crossing`: the Two Lane Highway in its retaining-wall trench, with Elm Street bridging over it.
- **First Skibidi run (before DECISIONS-importer.md #7):** 2034 / 2035. One CS1 road that bridged the trenched highway met it at grade, and CS2 joined them with a junction.
