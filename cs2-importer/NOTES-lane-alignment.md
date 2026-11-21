# Lane alignment: CS1 Node Controller shifts in CS2

Research notes for docs/DECISIONS.md #107, 2026-11-21. Decisions: [DECISIONS-nodecontroller.md](DECISIONS-nodecontroller.md).

Sources:
- CS2 `Game.dll` (1.6), read with the System.Reflection.Metadata IL dumper used for `spikes/m0-cs2-place-road/NOTES-cs2-placement.md`. **[IL]** marks statements read from the IL.
- `NodeControllerRenewal.dll` 3.5.4.1 (workshop 2472062376), same tool.
- The in-game spike `spikes/nc-lane-align/` (`NcAlign.cs` + `run.mjs`). **[game]** marks results measured in CS2.

## 1. What Node Controller does in CS1

- **Junctions.** In CS1, every segment's bezier runs from node centre to node centre. Lanes only line up across a node when both roads are centred the same way.
- **Shift.** NC's per-segment-end `shift` (metres) moves the segment's end sideways. `SegmentEndData.GetSegmentPosAndDir` **[IL]**:
  - `startPos = startNode.pos + shift_start · Turn90(startDir, ccw)`, and the same at the end with `endDir`. `startDir` / `endDir` are CS1's `m_startDirection` / `m_endDirection`, pointing from each node *into* the segment.
  - `Turn90(v, false) = (−v.z, v.y, v.x)` **[IL]**: counter-clockwise seen from above. So **+shift = to the left, standing at the node and looking into the segment**.
  - Both directions are then turned clockwise by `a = asin((s_start + s_end) / |endPos − startPos|)` (`TurnRad(dir, a, true)` **[IL]**). `NodeStyle.NeedFixDirection` always returns true **[IL]**. So a segment whose two ends are shifted to opposite sides stays straight on the diagonal.
  - A segment end whose mode is `FreeForm` (enum value 4) gets no shift **[IL]**; NC drags its corners instead.
  - The curve itself is CS1's smooth bezier between the shifted ends (`BezierTrajectory(start, dir, end, dir)`).
- **Offset.** The distance from the node centre where the segment mesh stops and the node mesh starts, i.e. how long the junction is.
- **Slope, twist, stretch, deltaHeight:** the segment end's cross-section is tilted, stretched or raised.

What players use it for (measured on the exports):

| City | NC nodes | shifted ends | 2-segment nodes with a shift | of which a lane drop (one side aligned) | junctions with a shift | median \|shift\| |
| --- | --- | --- | --- | --- | --- | --- |
| Skibidi | 1179 | 133 | 38 | 37 | 67 | 1.78 m |
| CacaoCity | 306 | 52 | 21 | 19 | 15 | 2.0 m |
| Westfield | 309 | 45 | 11 | 10 | 25 | 1.78 m |
| Rennmont | 46 | 0 | – | – | – | – |

Typical cases:
- **Lane drop.** A 3-lane highway shifted by ±1.775 m (half a CS1 lane) at both ends, so its outer lanes continue into a 2-lane highway.
- **Exit ramp.** At the junction, the continuing highway is shifted to one side and the ramp to the other, by up to 12 m.

## 2. What CS2 stores

Saved net components **[IL]**:
- `Node {m_Position, m_Rotation}`;
- `Edge {m_Start, m_End}`;
- `Curve {m_Bezier, m_Length}`;
- `Road {…, m_Flags}`, where `StartHalfAligned` / `EndHalfAligned` are zone-grid alignment, not lanes;
- `Upgraded {CompositionFlags}`, `Elevation`.

There is no per-end offset or alignment component. `EdgeGeometry`, `StartNodeGeometry` / `EndNodeGeometry` (with `m_SyncVertexTargets*`) and `NodeGeometry` are derived every update by `GeometrySystem`.

The net tool's snapping (`NetToolSystem.SnapJob`, `NetIterator.HandleGeometry` / `HandleCurve`) **[IL]** snaps to:
- nodes, edge curves and edge sides at zone-cell distances (`Snap.NetSide`);
- composition areas for building on medians (`NetCompositionArea`, flags Buildable/Median);
- guide lines.

None of it looks at lanes. `CoursePosFlags.HalfAlign` only becomes `Road.Start/EndHalfAligned` (`GenerateEdgesJob.SetRoadAlignment` **[IL]**), which is zoning. So there is no lane snap to copy.

**The edge end may sit off the node.** `GenerateEdgesJob.GenerateEdge` **[IL]** sets the edge's curve end points like this:
- if the prefab's `NetGeometryData.m_Flags` has `GeometryFlags.StrictNodes` (2), or the prefab has no geometry: `curve.a/d = start/end node position`;
- otherwise: `curve.a/d = MathUtils.Position(course.m_Curve, m_CourseDelta)`, the course curve's own end points. Only y is taken from the node, and only when the net layers differ.

`NetInitializeSystem` gives roads `SupportRoundabout|BlockZone|Directional|FlattenTerrain|ClipTerrain` (399368) and power lines `StrictNodes|…` (50) **[IL]**. No other prefab gets `StrictNodes`. So **a road edge keeps whatever end point its course curve has**, while it shares the node entity with the other edges. The node position comes from `CoursePos.m_Position` (or the existing node).

This is the CS2 equivalent of an NC shift: one node at the CS1 centre, each edge's curve ending where NC put the segment end.

## 3. In-game check

`spikes/nc-lane-align/run.mjs` on a new Corral Riches city (CS2 1.6, 2026-11-21, run `.gamectl/nc-align/20261121-160848/`). It uses vanilla `Highway Oneway - 3 / 2 / 1 lanes`, whose car lanes are 4 m apart (measured). Each case is built from Permanent definitions like the importer's.
- "aligned" cases: each course's `CoursePos.m_Position` is the node centre, and its curve end is shifted (the 2-lane road half a lane, the ramp one lane).
- "centred": every curve passes through the centre, as before.

Results **[game]**:

| Case | Edge ends off the node | Node car lanes, sideways bend (m) / turn |
| --- | --- | --- |
| 3 → 2 lanes, centred | 0 / 0 | −2, +2, +2: every lane jogs 2 m |
| 3 → 2 lanes, 2-lane road 2 m right | 1 / 1 | −4, 0, 0: two lanes run straight on, the third merges |
| same, the 2-lane road attached in a later frame to the existing node | 1 / 1 | −4, 0, 0 |
| 3 lanes → 2-lane highway + 1-lane ramp, centred | 0 / 0 / 0 | −2, −2 (the highway jogs); ramp −6.2 m / −21.6° |
| same, highway 2 m left, ramp 4 m right | 0.67 / 2.67 / 3.33 | 0, 0 (the highway runs straight); ramp −2.9 m / −11.8° |

What we saw:
- **Edge ends.** Each edge keeps its own curve end exactly, in the same frame or attached later. Off-node ends are stable.
- **Node position.** CS2 puts the node itself at the **plain mean of its edges' curve ends**. `CoursePos.m_Position` is ignored when the node is created, and the mean is recomputed when another edge attaches. Ramp case, ends at z = 0, +2, −4: the node sits at −0.67, and the distances 0.67 / 2.67 / 3.33 match. The importer therefore looks a node up at that mean, not at the plan centre: see DECISIONS-nodecontroller.md 4.
- **Geometry.** Node geometry, markings and lane connections are built normally, with no gaps or Temp leftovers. Screenshots `cmp-drop.png` and `cmp-ramp.png` (left centred, right aligned): the lane lines continue straight, one lane tapers off, and the ramp peels off the outer lane. In the centred versions, both sides pinch in.
- **The tool's way** (Temp definitions plus one forced apply frame, as in `TrackLink.cs`) built nothing from an exec call in this setup; Temp definitions need an active net tool to be applied. It isn't needed: the importer builds Permanent definitions, which is what was tested.
- **A spike bug, now fixed, that crashed CS2.** Two runs crashed CS2 natively ("End node has no ConnectedEdge", then a native crash on load or during the next update). The spike's own `clear` op deleted nodes by radius, including the end nodes of the previous case's live edges. That's unrelated to off-node ends; the spike no longer clears.

## 4. Transform

See DECISIONS-nodecontroller.md. In short:
- **Web plan.** For each plan segment end with |shift| ≥ 5 cm and a mode other than FreeForm, the plan applies NC's formula above to the plan bezier, keeping CS1's handle lengths scaled with the chord. `bezier` becomes that shifted curve, with `offNode: true` and `ncShift: [start, end]`. Plan nodes stay at the CS1 centres.
- **Importer.** For an `offNode` segment, the course curve ends at node position + (bezier end − plan node) in x/z. `CoursePos.m_Position` is the node position. CS2 moves the node to (about) the mean of its edges' curve ends, so the importer finds it there, or through the edges ending at the shifted points (DECISIONS-nodecontroller.md 4).
