// Which CS1 lanes are covered by an IMT fill? Port of spikes/m0-roadbuilder-configs/imt-lanes.mjs.
//
// Rule (user, docs/DECISIONS.md #3, #7, #8): a pavement/grass island fill covering a whole lane →
// RB median in that lane; a solid red fill covering a whole lane → RB bus lane. Full coverage only.
// Node fills and grey paint are reported only.
//
// Lane geometry (checked against the CS1 IL, NOTES-roadbuilder.md §9): a lane centre is the
// segment bezier offset sideways by lane.m_position; positive = right of start→end, mirrored when
// the segment has the Invert flag. CS1 is left-handed (X east, Z north, Y up), so right of (dx, dz)
// is (dz, −dx). All tests are 2D on XZ.
//
// "Whole lane": a lane counts as covered when its centre is inside the fill for ≥ 90 % of the
// fill's own length along the segment, and on average ≥ 70 % of the lane width is inside over
// that run. Only a fill on the segment's own marking can cover a lane.

import type { Bezier, Cs1Segment, ImtFiller, NetInfo, Vec3 } from './types.ts';

const N = 120;     // samples along the segment, t ∈ [0, 1]
const ACROSS = 11; // samples across a lane
export const MIN_RUN = 0.9;
export const MIN_LATERAL = 0.7; // IMT insets fills from the lane lines (Offset + line width)

export type FillerKind = 'bus' | 'median' | 'median-grass' | 'paint';
export type LaneOverrideKind = Exclude<FillerKind, 'paint'>;

export interface SlotLike { pos: number; width: number; kind: string }

export interface LaneHit { pos: number; width: number; kind: string; run: number; runFrac: number; lateral: number; whole: boolean }

export interface Coverage {
  segment: number;
  info: string;
  fillerIdx: number;
  fillerKind: FillerKind;
  markingType: string;
  markingId: number;
  extent: number;
  lanes: LaneHit[];
}

export function classifyFiller(f: Pick<ImtFiller, 'styleClass' | 'styleType' | 'color'>): FillerKind {
  const st = `${f.styleClass} ${f.styleType}`;
  if (/Grass/i.test(st)) return 'median-grass';
  if (/Pavement|Gravel|Island|Cliff/i.test(st)) return 'median';
  const [r, g, b] = f.color ?? [0, 0, 0];
  if (/Solid/i.test(st) && r >= 100 && r > 2 * g && r > 2 * b) return 'bus';
  return 'paint';
}

export function bezierPoint(p: Bezier, t: number): [number, number] {
  const u = 1 - t;
  const w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
  return [w0 * p[0][0] + w1 * p[1][0] + w2 * p[2][0] + w3 * p[3][0], w0 * p[0][2] + w1 * p[1][2] + w2 * p[2][2] + w3 * p[3][2]];
}

function tangent(p: Bezier, t: number): [number, number] {
  const u = 1 - t;
  const w0 = -3 * u * u, w1 = 3 * u * u - 6 * u * t, w2 = 6 * u * t - 3 * t * t, w3 = 3 * t * t;
  const x = w0 * p[0][0] + w1 * p[1][0] + w2 * p[2][0] + w3 * p[3][0];
  const z = w0 * p[0][2] + w1 * p[1][2] + w2 * p[2][2] + w3 * p[3][2];
  const l = Math.hypot(x, z) || 1;
  return [x / l, z / l];
}

export function pointInPolygon(poly: Vec3[], x: number, z: number): boolean {
  let c = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const xi = poly[i][0], zi = poly[i][2], xj = poly[j][0], zj = poly[j][2];
    if ((zi > z) !== (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) c = !c;
  }
  return c;
}

type BBox = [number, number, number, number];
export const bboxOf = (pts: Vec3[]): BBox =>
  pts.reduce<BBox>((b, p) => [Math.min(b[0], p[0]), Math.min(b[1], p[2]), Math.max(b[2], p[0]), Math.max(b[3], p[2])], [Infinity, Infinity, -Infinity, -Infinity]);
const overlap = (a: BBox, b: BBox) => a[0] <= b[2] && b[0] <= a[2] && a[1] <= b[3] && b[1] <= a[3];

export interface CoverageInput {
  segments: Cs1Segment[];
  netInfos: NetInfo[] | Map<string, NetInfo>;
  imtFillers: ImtFiller[];
}

/**
 * One entry per (segment, filler) pair touching at least one lane of the segment.
 * @param slotsOf drive slots of a NetInfo, in NetInfo lane space
 */
export function laneCoverage(input: CoverageInput, slotsOf: (info: NetInfo) => SlotLike[]): Coverage[] {
  const infos = input.netInfos instanceof Map ? input.netInfos : new Map(input.netInfos.map(i => [i.name, i]));
  const fillers = input.imtFillers.map((f, idx) => ({ f, idx, bbox: bboxOf(f.polygon), kind: classifyFiller(f) }));
  const out: Coverage[] = [];
  if (!fillers.length) return out;
  const slotCache = new Map<string, SlotLike[]>();
  for (const s of input.segments) {
    const info = infos.get(s.info);
    if (!info) continue;
    const hw = Number(info.fields.m_halfWidth) || 0;
    const sb = bboxOf(s.bezier); sb[0] -= hw; sb[1] -= hw; sb[2] += hw; sb[3] += hw;
    const cands = fillers.filter(x => overlap(x.bbox, sb));
    if (!cands.length) continue;
    let slots = slotCache.get(info.name);
    if (!slots) { slots = slotsOf(info); slotCache.set(info.name, slots); }
    const flip = s.inverted ? -1 : 1;
    const frame: [[number, number], [number, number]][] = [];
    for (let i = 0; i <= N; i++) { const t = i / N; frame.push([bezierPoint(s.bezier, t), tangent(s.bezier, t)]); }
    const dt = s.length / N;

    for (const c of cands) {
      const at = (i: number, off: number) => {
        const [[x, z], [dx, dz]] = frame[i];
        return pointInPolygon(c.f.polygon, x + dz * off * flip, z - dx * off * flip);
      };
      const hits: (Omit<LaneHit, 'run' | 'runFrac' | 'whole'> & { runN: number })[] = [];
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
        if (run) hits.push({ pos: sl.pos, width: sl.width, kind: sl.kind, runN: run, lateral: lat / run });
      }
      if (!hits.length) continue;
      const extentN = last - first + 1;
      const lanes: LaneHit[] = hits.map(h => {
        const runFrac = +(h.runN / extentN).toFixed(2);
        const lateral = +h.lateral.toFixed(2);
        return {
          pos: h.pos, width: h.width, kind: h.kind,
          run: +(h.runN * dt).toFixed(1), runFrac, lateral,
          whole: c.f.markingType === 'Segment' && c.f.markingId === s.id && runFrac >= MIN_RUN && lateral >= MIN_LATERAL,
        };
      });
      out.push({ segment: s.id, info: s.info, fillerIdx: c.idx, fillerKind: c.kind, markingType: c.f.markingType, markingId: c.f.markingId, extent: +(extentN * dt).toFixed(1), lanes });
    }
  }
  return out;
}

/** Whole-lane bus/median overrides per segment: Map(segmentId → Map(lanePos → kind)). */
export function wholeLaneOverrides(coverage: Coverage[]): Map<number, Map<number, LaneOverrideKind>> {
  const bySeg = new Map<number, Map<number, LaneOverrideKind>>();
  for (const c of coverage) {
    if (c.fillerKind === 'paint') continue;
    for (const l of c.lanes) {
      if (!l.whole) continue;
      if (!bySeg.has(c.segment)) bySeg.set(c.segment, new Map());
      bySeg.get(c.segment)!.set(l.pos, c.fillerKind);
    }
  }
  return bySeg;
}

/** Stable signature of an override set, e.g. "6=bus,8.5=bus" (same as the M0 spike). */
export function overrideSignature(m: Map<number, string> | null | undefined): string {
  if (!m || !m.size) return '';
  return [...m].sort((x, y) => x[0] - y[0]).map(([p, k]) => `${p}=${k}`).join(',');
}
