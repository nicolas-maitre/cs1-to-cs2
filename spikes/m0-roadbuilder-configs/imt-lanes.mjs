// M0 spike 3 add-on: which CS1 lanes are covered by an IMT fill?
//
// Rule (user requirement): a pavement/grass island fill covering a whole lane → RB median
// in that lane; a solid red fill covering a whole lane → RB bus lane.
//
// Lane geometry (checked against the CS1 IL, see NOTES-roadbuilder.md §9): a lane centre is the
// segment bezier offset sideways by lane.m_position; positive = right of start→end
// (NetSegment.CalculateCorner uses Cross(startDir, up) = left for leftSide), mirrored when the
// segment has the Invert flag (NetAI.UpdateLanes: t = pos / (2·halfWidth) + 0.5, then 1 − t
// if m_flags & 16). CS1 is left-handed (X east, Z north, Y up), so right of (dx, dz) is (dz, −dx).
// All tests are 2D on XZ.
//
// "Whole lane": IMT segment markings only have points at the two segment ends, so a
// segment fill always runs from one end line to the other. A lane counts as covered when
// its centre is inside the fill for ≥ 90 % of the fill's own length along the segment, and
// on average ≥ 70 % of the lane width is inside over that run. Node fills (intersections)
// never cover a whole lane; they are reported as touching lane ends only.

const N = 120;               // samples along the segment, t ∈ [0, 1]
const ACROSS = 11;          // samples across a lane
export const MIN_RUN = 0.9;
export const MIN_LATERAL = 0.7; // IMT insets fills from the lane lines (Offset + line width)

export function classifyFiller(f) {
  const st = `${f.styleClass} ${f.styleType}`;
  if (/Grass/i.test(st)) return 'median-grass';
  if (/Pavement|Gravel|Island|Cliff/i.test(st)) return 'median';
  const [r, g, b] = f.color ?? [0, 0, 0];
  if (/Solid/i.test(st) && r >= 100 && r > 2 * g && r > 2 * b) return 'bus';
  return 'paint';
}

const bez = (p, t) => {
  const u = 1 - t;
  const w = [u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t];
  return [0, 2].map(k => w[0] * p[0][k] + w[1] * p[1][k] + w[2] * p[2][k] + w[3] * p[3][k]); // [x, z]
};
const tangent = (p, t) => {
  const u = 1 - t;
  const w = [-3 * u * u, 3 * u * u - 6 * u * t, 6 * u * t - 3 * t * t, 3 * t * t];
  const v = [0, 2].map(k => w[0] * p[0][k] + w[1] * p[1][k] + w[2] * p[2][k] + w[3] * p[3][k]);
  const l = Math.hypot(v[0], v[1]) || 1;
  return [v[0] / l, v[1] / l];
};

function inside(poly, x, z) {
  let c = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, , zi] = poly[i], [xj, , zj] = poly[j];
    if ((zi > z) !== (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) c = !c;
  }
  return c;
}

const bboxOf = pts => pts.reduce((b, [x, , z]) => [Math.min(b[0], x), Math.min(b[1], z), Math.max(b[2], x), Math.max(b[3], z)], [Infinity, Infinity, -Infinity, -Infinity]);
const overlap = (a, b) => a[0] <= b[2] && b[0] <= a[2] && a[1] <= b[3] && b[1] <= a[3];

/**
 * @param dump    M0 network dump
 * @param slotsOf (info) => [{pos, width, kind}]  drive slots of a NetInfo
 * @returns one entry per (segment, filler) pair touching at least one lane:
 *   {segment, info, fillerIdx, fillerKind, markingType, markingId, extent (m),
 *    lanes: [{pos, width, kind, run (m), runFrac, lateral, whole}]}
 */
export function laneCoverage(dump, slotsOf) {
  const infos = new Map(dump.netInfos.map(i => [i.name, i]));
  const fillers = dump.imtFillers.map((f, idx) => ({ f, idx, bbox: bboxOf(f.polygon), kind: classifyFiller(f) }));
  const out = [];
  for (const s of dump.segments) {
    const info = infos.get(s.info);
    if (!info) continue;
    const hw = info.fields.m_halfWidth;
    const sb = bboxOf(s.bezier); sb[0] -= hw; sb[1] -= hw; sb[2] += hw; sb[3] += hw;
    const cands = fillers.filter(x => overlap(x.bbox, sb));
    if (!cands.length) continue;
    const slots = slotsOf(info);
    const flip = s.inverted ? -1 : 1;
    const frame = [];
    for (let i = 0; i <= N; i++) { const t = i / N; frame.push([bez(s.bezier, t), tangent(s.bezier, t)]); }
    const dt = s.length / N;

    for (const c of cands) {
      const at = (i, off) => { const [[x, z], [dx, dz]] = frame[i]; return inside(c.f.polygon, x + dz * off * flip, z - dx * off * flip); };
      const lanes = [];
      let first = Infinity, last = -Infinity;
      for (const sl of slots) {
        let run = 0, lat = 0;
        for (let i = 0; i <= N; i++) {
          if (!at(i, sl.pos)) continue;
          run++; first = Math.min(first, i); last = Math.max(last, i);
          let k = 0;
          for (let j = 0; j < ACROSS; j++) if (at(i, sl.pos + (j / (ACROSS - 1) - 0.5) * (sl.width - 0.2))) k++;
          lat += k / ACROSS;
        }
        if (run) lanes.push({ pos: sl.pos, width: sl.width, kind: sl.kind, runN: run, lateral: lat / run });
      }
      if (!lanes.length) continue;
      const extentN = last - first + 1;
      for (const l of lanes) {
        l.run = +(l.runN * dt).toFixed(1);
        l.runFrac = +(l.runN / extentN).toFixed(2);
        l.lateral = +l.lateral.toFixed(2);
        l.whole = c.f.markingType === 'Segment' && c.f.markingId === s.id && l.runFrac >= MIN_RUN && l.lateral >= MIN_LATERAL;
        delete l.runN;
      }
      out.push({ segment: s.id, info: s.info, fillerIdx: c.idx, fillerKind: c.kind, markingType: c.f.markingType, markingId: c.f.markingId, extent: +(extentN * dt).toFixed(1), lanes });
    }
  }
  return out;
}

/** Whole-lane bus/median overrides per segment: Map(segmentId → Map(lanePos → kind)). */
export function wholeLaneOverrides(coverage) {
  const bySeg = new Map();
  for (const c of coverage) {
    if (c.fillerKind === 'paint') continue;
    for (const l of c.lanes) {
      if (!l.whole) continue;
      if (!bySeg.has(c.segment)) bySeg.set(c.segment, new Map());
      bySeg.get(c.segment).set(l.pos, c.fillerKind);
    }
  }
  return bySeg;
}
