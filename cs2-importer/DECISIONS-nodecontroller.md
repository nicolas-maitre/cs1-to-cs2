# Node Controller alignment: decisions

Decisions for docs/DECISIONS.md #107 (apply CS1 Node Controller shifts). Research: [NOTES-lane-alignment.md](NOTES-lane-alignment.md).

1. **Shifts are applied as off-node edge ends** (Claude, 2026-11-21, agreed with worker-2 for web and worker-3 for the importer).
   - **CS2 mechanism.** CS2 has no lane snap and saves no per-end alignment. But road prefabs lack `GeometryFlags.StrictNodes`, so `GenerateEdge` keeps the course curve's own end points. A road edge can end off its node's centre while sharing the node.
   - **What it gives.** Each segment end sits exactly where Node Controller put it, and the node stays at the CS1 centre. No extra nodes or segments, no node moves, so traffic lane refs, transit stops and heights are untouched.
   - **Rejected:**
     - *Moving nodes.* Only exact for a uniform shift; a lane drop then jogs on the other road.
     - *Splitting edges near the node.* Synthetic segments break the plan's 1:1 segment ids.
     - *Asymmetric Road Builder variants.* A per-edge offset isn't per end, and it multiplies the prefabs.
2. **The formula is NC Renewal's own** (`SegmentEndData.GetSegmentPosAndDir`, read from its IL).
   - Each end moves `shift` to the left of the direction from its node into the segment.
   - Both end directions turn clockwise by asin((s_start + s_end) / L), clamped at sine 0.95 for short, strongly shifted segments; the report warns.
   - CS1's handle lengths are kept, scaled with the chord.
   - FreeForm ends get no shift, as in NC.
   - Shifts under 5 cm are ignored.
   - It applies to every kind (ground, elevated, bridge, tunnel).
3. **The plan carries CS2-ready geometry** (web ≥ 0.9.0).
   - `segments[].bezier` is the shifted curve, rounded to 3 decimals.
   - `offNode: true` and `ncShift: [start, end]` (m, + = left looking from the node into the segment) mark it.
   - `nodes[]` stay at the CS1 centres.
   - Terrain conform reads the shifted curve.
   - An importer without offNode support snaps the ends back to the nodes, which is the old result.
4. **The importer keeps the offset, and finds nodes where CS2 puts them.**
   - **The curve.** `ImportPlanner.Curve`: for an offNode segment, the curve ends at the final node position + the plan shift, in x/z, y unchanged.
     - The shift is `SegmentJob.OffStart/OffEnd`, computed in `Build` as bezier end − plan node. It's independent of the test offset (OffsetX/Z moves the nodes, not the bezier).
     - Left-hand reversal (#55) inherits it through `BuildCurve`.
   - **The course.** `World.CreateSegment` passes the node position as `CoursePos.m_Position`.
   - **Where the node ends up.** CS2 ignores that position: it puts the node at the mean of its edges' curve ends, and recomputes it when an edge attaches (NOTES §3). Worker-3's first offNode run looked nodes up at the plan centre and missed them: 33 node-not-created and 56 node-missing on a Skibidi crop. So:
     - *Batch resolution* (Entry.FindBuilt): look each new node up at the mean of the curve ends its batch created there, with the plan centre as fallback. Later segments then attach at the plan centre (x/z) with the found node's y, so every edge keeps its CS1 end however the node moves.
     - *Plan-node mapping* for the M2 steps, lanedirs, heights and the arm check (M2Ops.Resolve): `ImportPlanner.OffNodeCentres(job)` gives each shifted node's final mean over all its plan segments, tried first.
     - *Fallback for both* (`GameWorld.NodeByEdgeEnds`): the node whose real edges end at the shifted curve ends, by votes. CS2 doesn't always use the plain mean: at an elevated merge on CacaoCity (node 17774, −10 m shift) the node was 3.3 m off it. But every edge keeps its curve end to the millimetre, so the edges find the node.
   - **Switch.** Worker-3 put it behind `ImportOptions.ApplyOffNode` (exec arg `offNode`), off by default (DECISIONS-importer #59). It flips to on once the before/after run (item 6) has zero node failures on Skibidi, CacaoCity and Westfield.
5. **Offset, slope, twist, stretch and deltaHeight stay report-only.**
   - CS2 decides junction length (node offsets) itself, from the prefab's `m_MinNodeOffset` and the intersection geometry.
   - It has no tilted or raised segment ends.
   - deltaHeight could use the same off-node mechanism in y. It's left alone while the road-height work (#110–113) is in flux.
6. **Verified in game, before/after on three cities** (2026-11-21, `spikes/nc-lane-align/verify.mjs`, run `.gamectl/nc-align/verify/20261121-165716/`).
   - **Setup.** Web plans with the shifts, `--auto-crop 3000`, imported at the flat test spot in three phases per city:
     - *before*: the CS1 beziers, no offNode;
     - *after-snap*: the same package, offNode off;
     - *after*: offNode on.
   - **Results with offNode on:**

     | City | Roads built | Nodes | Node failures | Lane directions | TM:PE lane refs matched (before → after) | Arm check: arms off |
     | --- | --- | --- | --- | --- | --- | --- |
     | Skibidi | 2035/2035 | 1670/1670 | 0 | identical | 541 → 553 / 685 | 0 |
     | CacaoCity | 684/684 | 605/605 | 0 | identical | 670 → 670 / 729 | 0 |
     | Westfield | 648/648 | 642/642 | 0 | identical | 217 → 224 / 301 | 0 |

   - **Other notes and save/load.** The remaining import notes (unexpected tunnel/elevated at the flat spot) are the same in every phase. After save + load, the off-node ends are unchanged and nothing crashes.
   - **Junction lanes.** Car lanes bend sideways through the node by:
     - Skibidi highway split 3071 (3 lanes into two 2-lane highways): −2.0 / −2.0 m before, +0.3 / +0.6 m after;
     - exit ramp 1745: −2.2 → −0.7 m for the through lanes, −6.0 → −5.1 m for the ramp. The rest comes from CS2's 4 m vanilla highway lanes vs CS1's 3.55 m, which a shift can't fix.
   - **Screenshots:** `.gamectl/nc-align/shots/` (same camera, checked with camera.get) and the spike's `cmp-drop.png` / `cmp-ramp.png`.
