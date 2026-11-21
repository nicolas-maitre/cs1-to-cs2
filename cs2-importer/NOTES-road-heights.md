# Road heights: road-conformed terrain and per-type height modes

Design for DECISIONS.md #110–112 (user: "for streets at least, the ground should snap to the road height, not the road to the ground", and the behaviour configurable per road type). **Agreed with worker-2 and worker-4 (their reviews folded in below).** Owners:
- web core heightmap builder and schema §2: worker-2;
- `terrain/convert.mjs` mirror: worker-4;
- importer: cs2-importer worker.

## Why roads come out lumpy today

CS1 shapes its terrain to its roads, but the export's heightmap is coarse: 1081² cells over 17 280 m, so **16 m per cell**. A 8–30 m wide road corridor is about one cell wide, so its flattened strip is smeared into the surrounding cells. The CS2 heightmap (3.5 m cells) is interpolated from that, so ground roads that CS2 lays on the terrain inherit the lumps.

The importer then adds its own snapping (#7, #28):
- CS1 ground nodes within ±4 m of terrain → elevation 0;
- trenches kept down to −11.5 m;
- embankments ≥ 4 m snapped down.

Curves are straight lines between the snapped nodes. That's what the user saw: a straight CS1 ramp following terrain bumps.

The fix restores what CS1 had: the road's exact profile, at the resolution CS2 needs.

## Height modes (per prefab key)

`plan.prefabs[key].heightMode`: one of `conform` | `follow` | `walls`.
- **Applies to** that key's **ground** segments (`kind: "ground"` and `"slope"`; a `conform` slope is built as `walls`, #58). Elevated, bridge and tunnel segments keep their own elevations whatever the mode.
- **Editing:** the user edits the mode per CS1 type in the mapping table. A non-default mode is folded into the prefab key (e.g. `v:Small Road|walls`), so `prefabs[key].heightMode` is never ambiguous when CS1 types share a target. Keys stay opaque to the importer. CLI: `--height-mode <m>`, `--height-mode-by-type map.json`.

**Defaults** (user, DECISIONS.md #113), from the CS1 NetInfo:

| CS1 type | Default | Rule |
| --- | --- | --- |
| Zoneable road types | `conform` | `aiFields.m_enableZoning: true` (RoadAI): Basic Road, Oneway Road, Large Road… |
| Non-zoneable road types | `walls` | `m_enableZoning: false`: highways, ramps, roads without zoning |
| Pedestrian and bike paths | `follow` | |
| Tracks (train, metro, tram on its own) | `conform` | Earth embankments like CS1's. Not covered by #113, which is about road types; the coordinator may change it. |

| Mode | Terrain (package heightmap) | Road (importer) |
| --- | --- | --- |
| `conform`: ground follows the road | CS1's vertical profile is burned into the heightmap along the corridor, with blended earth slopes on both sides | each node at CS1 height = the conformed terrain, so elevation 0 (an ordinary ground road, buildings connect). The course keeps CS1's vertical profile. |
| `follow`: road follows the ground | natural (converted) terrain, untouched | nodes at the terrain, elevation 0. CS1's height above terrain is ignored: no ±4 m features, no trench clamp. |
| `walls`: CS1 height, walls | natural terrain, untouched | nodes at CS1 height. Elevation = CS1 height above the (walls-burned) terrain, within −11.5…+7.5 m, so CS2 builds retaining walls / quays (Raised/Lowered sides), never a tunnel or pillars: CS1's elevation kind decides the composition family (DECISIONS.md #129, DECISIONS-importer.md #61; replaces #123). Beyond the limits the package reshapes the ground beside the road (walls burn: ground at most 7 m below / 11 m above the road), else the road stays at the limit and is reported. The course keeps CS1's profile. |

### Nodes where modes meet (importer, tested in `HeightModeTests`)

A node takes one elevation:
1. If **any** ground segment at the node is `conform`, the heightmap is burned at the node, and the node sits at its CS1 height = terrain, so **elevation 0**. That holds even where an elevated segment (the ramp's embankment meets the bridge) or a tunnel (the slope's open cut meets the portal) ends there. `walls` and `follow` segments starting there begin at that height.
2. Else, if any is `walls`, the node is at CS1 height with the walls elevation. A `follow` segment ramps from it to the terrain at its other end.
3. Else (only `follow`), elevation 0 on the natural terrain.
4. Plans without modes keep the M2 rules (#7, #28). Elevated/bridge/tunnel segment ends keep their elevation as today, except at conformed nodes (rule 1).

## Burning the profile into the heightmap (web core and convert.mjs, byte-identical)

Both builders read only the plan. The exact arithmetic goes into schema §2 (worker-2) so both sides code against one text.

**Where and in which units:** on the 4096² float64 grid in **CS1 metres**:
- **when:** after the bilinear resample, before the height-scale/offset or "fit" transform, before quantizing and any row flip;
- **orientation:** r16 (row 0 = south; pixel centre at `−size/2 + (i + 0.5) × 3.5`);
- **datum:** the plan bezier's y is absolute CS1 height, the same datum.

**Which segments:** every ground segment whose prefab is `conform` (slope segments not, see Tunnel portals).
- **Not burned:** elevated, bridge and tunnel segments; `follow` and `walls` segments.
- **Bridge ends:** the ground segment up to the abutment burns, and the terrain falls away under the bridge.
- **Tunnel portals:** slope segments are **not** burned (#124, DECISIONS-importer.md #58): CS2 builds a tunnel only with the ground at the portal 8–12 m above the road, and its lowered composition cuts the descent itself. The importer builds slopes as walls and never conforms a tunnel node.
- **Safety:** a ground segment more than 30 m from the natural terrain is probably misclassified. It is skipped and reported, so nothing digs a 30 m trench.

1. **Centre line:**
   - `L` = length of a fixed 16-step chord polyline of the plan `bezier`;
   - `n = ceil(L)` steps, uniform in t;
   - the distance is to that piecewise-linear polyline (exact projection on each piece, height interpolated along it; when two pieces are equally near, the lower index wins);
   - use `Math.sqrt(dx*dx + dz*dz)` (not `Math.hypot`).
2. **Corridor half-width `w`:** the segment's own `halfWidth` (plan, per segment) = its CS1 NetInfo `m_halfWidth`. That's the whole road incl. sidewalks, the width CS1 itself flattened.
3. **Core:** pixels with `d ≤ w` take the road height at the nearest centre-line point, flat across the road. Weight 1.
4. **Blend:** for `d ∈ (w, w + b]`, weight `wᵢ = 1 − t` with `t = smoothstep((d − w) / b)` and `smoothstep(x) = x·x·(3 − 2x)`.
   - `b = clamp(2 × |h_road − h_natural|, 3.5, 40) m`: smoothstep is steepest at 1.5× its average, so factor 2 gives about 37° at the steepest point.
   - `h_natural` = the converted value at that pixel before any burning.
5. **Several roads on one pixel** (continuous; no steps where blends meet):
   - (a) if any **core** covers the pixel, the core wins, ties to the smaller `|h_road − h_natural|` (only at junctions, where the heights agree);
   - (b) otherwise `W = max wᵢ`, `h_target = Σ wᵢ·hᵢ / Σ wᵢ`, `h = lerp(h_natural, h_target, W)`. Schema §2 is the contract and has the exact weighting (the web code weights by wᵢ⁴, on the float32 grid); it also has the walls burn (#129), a second pass on the conform result.

   Iterate segments in plan order, then junction discs in node-id order; sums in that fixed order.
6. **Junctions:** each node of a conform segment gets a core disc of radius `max(w)` of its conform segments, at the node's CS1 height, with a blend around it like any core.
7. **Water** (worker-4):
   - in blend pixels that are wet in CS1 (`water.r16` depth > 0, or natural height below the sea level), `b` is clamped to 3.5 m and the burn never raises a wet pixel above the CS1 water surface. Quays stay quays; the shoreline doesn't move out. Core pixels still burn.
   - water sources take their ground from the final conformed grid, so their absolute target level is kept (`water.mjs` and its web port; region and spread sources too).
8. **Memory (web):** each corridor's bounding box only (`w + 40 m`), with a sparse map of touched pixels.
9. **Output:**
   - the same heightmap file and format;
   - `terrain.json` gains `roadConformed: true` and `conform: { segments, skipped, blendFactor: 2, minBlend: 3.5, maxBlend: 40 }`, plus the stats before and after (max change, pixels changed);
   - `plan.terrain.roadConformed` mirrors it;
   - the world map is unchanged.

## Importer side (cs2-importer)

- Reads `prefabs[key].heightMode`, default `conform` when `plan.terrain.roadConformed`, else the M2 rules (no modes), and applies the node rule above (`ImportPlanner.AssignElevations`).
- On conformed terrain flat placement is exact for `conform` roads (terrain = CS1 height), so `heights: "exact"` isn't needed for them. `walls` nodes use the CS1 height through their elevation.
- **Checks:**
  - the height-error report (op `heights`: CS2 road y vs CS1 y along each segment) before and after, on Skibidi, CacaoCity and Westfield;
  - the user's interchange (CacaoCity near Eastgate / Mistwood Highway) before and after;
  - roads built, lane directions, lane connections and building connectivity must not regress.

## Who does what (agreed)

- **worker-2 (web, schema §2):**
  - per-segment `halfWidth`;
  - prefab `heightMode` with key folding and the #113 defaults;
  - the mapping-table select and CLI flags;
  - the burn in `web/src/core/terrain/` with tests;
  - the water port;
  - schema §2 with the exact arithmetic above.
- **worker-4 (terrain):** the `terrain/convert.mjs` mirror (byte-identical test), the water-source ground fix and the shore clamp, terrain test cases.
- **cs2-importer:** the node rule and profiles (done, `HeightModeTests`); the height-error report before/after (op `heights`, which samples the terrain as CS2 does); verification in game.
