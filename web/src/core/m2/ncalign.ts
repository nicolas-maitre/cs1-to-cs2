// Node Controller shifts → CS2 geometry (docs/DECISIONS.md #107, cs2-importer/DECISIONS-nodecontroller.md).
//
// In CS1, every road meets its node at the node's centre; Node Controller's per-segment-end `shift` moves the end
// sideways so lanes line up across lane drops, ramps and junctions. NC Renewal (SegmentEndData.GetSegmentPosAndDir,
// read from its IL) does, for a segment with end shifts s0 (start node) and s1 (end node):
//   P0' = P0 + s0 · left(d0)        d0 = start direction, from the start node into the segment
//   P3' = P3 + s1 · left(d1)        d1 = end direction, from the end node into the segment
//   a   = asin((s0 + s1) / |P3' − P0'|), and both directions turn clockwise by a
// where left(d) = (−d.z, d.x) (ModsCommon Turn90, counter-clockwise seen from above). A FreeForm segment end has no
// shift (NC drags its corners instead).
//
// CS2 stores no per-end offset, but its road prefabs lack GeometryFlags.StrictNodes, so GenerateEdge keeps a course
// curve's own end points: an edge may end off its node's centre. The plan therefore carries the shifted curve
// (`bezier`, ends off the node) and `offNode: true`, and the importer keeps those ends (nodes stay at the CS1 centres).
// Offsets, slopes, twists and deltaHeight have no CS2 counterpart and stay report-only (nodecontroller.ts).

import type { Bezier, NodeControllerSection, PackageReportItem, PlanSegment, Vec3 } from '../types.ts';
import { plural } from './context.ts';
import type { PlanContext } from './context.ts';

/** Shifts smaller than this (m) are ignored: NC rounding noise. */
export const MIN_SHIFT = 0.05;
/** |(s0 + s1) / L| above this is clamped (NC would take asin of > 1 on a very short, strongly shifted segment). */
export const MAX_SINE = 0.95;

export interface NcAlignStats {
  /** plan segments whose curve was shifted */
  segments: number;
  /** segment ends shifted */
  ends: number;
  /** ends with a shift that were left alone because their NC mode is FreeForm */
  freeForm: number;
  /** segments whose direction fix was clamped (short segment, large shift) */
  clamped: number;
  /** largest |shift| applied (m) */
  maxShift: number;
  /** a few CS1 segment ids, for the report */
  sample: number[];
}

interface EndShift { shift: number; freeForm: boolean }

/** (node id, segment id) → that segment end's shift, from nodecontroller.json. */
export function endShifts(nc: NodeControllerSection | null | undefined): Map<string, EndShift> {
  const m = new Map<string, EndShift>();
  for (const n of nc?.nodes ?? []) {
    for (const e of n.segmentEnds ?? []) {
      const shift = Number(e.shift) || 0;
      if (Math.abs(shift) < MIN_SHIFT) continue;
      m.set(`${n.id}:${e.segment}`, { shift, freeForm: e.mode === 'FreeForm' });
    }
  }
  return m;
}

const r3 = (v: number) => Math.round(v * 1000) / 1000;

function dirXZ(from: Vec3, to: Vec3): [number, number] | null {
  const x = to[0] - from[0], z = to[2] - from[2];
  const l = Math.hypot(x, z);
  return l > 1e-4 ? [x / l, z / l] : null;
}

/**
 * The NC-shifted curve of one segment, in the segment's own frame (b[0] at the start node, b[3] at the end node).
 * s0 / s1 = the shifts of the start / end segment ends (NC convention: + = left, looking from the node into the segment).
 * End heights and inner heights are kept. Returns null when the curve is degenerate.
 */
export function shiftBezier(b: Bezier, s0: number, s1: number): { bezier: Bezier; clamped: boolean } | null {
  const chord = dirXZ(b[0], b[3]);
  const d0 = dirXZ(b[0], b[1]) ?? chord;
  const d1 = dirXZ(b[3], b[2]) ?? (chord && [-chord[0], -chord[1]] as [number, number]);
  if (!d0 || !d1) return null;
  const left = (d: [number, number]): [number, number] => [-d[1], d[0]];
  const l0 = left(d0), l1 = left(d1);
  const p0: Vec3 = [b[0][0] + s0 * l0[0], b[0][1], b[0][2] + s0 * l0[1]];
  const p3: Vec3 = [b[3][0] + s1 * l1[0], b[3][1], b[3][2] + s1 * l1[1]];
  const L = Math.hypot(b[3][0] - b[0][0], b[3][2] - b[0][2]);
  const L2 = Math.hypot(p3[0] - p0[0], p3[2] - p0[2]);
  if (L2 < 1e-3) return null;
  let sine = (s0 + s1) / L2;
  const clamped = Math.abs(sine) > MAX_SINE;
  if (clamped) sine = Math.sign(sine) * MAX_SINE;
  const a = Math.asin(sine);
  // clockwise by a (NC TurnRad(dir, a, isClockWise: true)): x' = x cos a + z sin a, z' = −x sin a + z cos a
  const cw = (d: [number, number]): [number, number] => [d[0] * Math.cos(a) + d[1] * Math.sin(a), -d[0] * Math.sin(a) + d[1] * Math.cos(a)];
  const e0 = cw(d0), e1 = cw(d1);
  // keep CS1's handle lengths, scaled with the chord
  const k = L > 1e-3 ? L2 / L : 1;
  const h0 = Math.hypot(b[1][0] - b[0][0], b[1][2] - b[0][2]) * k;
  const h1 = Math.hypot(b[2][0] - b[3][0], b[2][2] - b[3][2]) * k;
  const p1: Vec3 = [p0[0] + e0[0] * h0, b[1][1], p0[2] + e0[1] * h0];
  const p2: Vec3 = [p3[0] + e1[0] * h1, b[2][1], p3[2] + e1[1] * h1];
  const round = (p: Vec3): Vec3 => [r3(p[0]), r3(p[1]), r3(p[2])];
  return { bezier: [round(p0), round(p1), round(p2), round(p3)], clamped };
}

/**
 * Applies the Node Controller shifts to the plan segments in place: `bezier` becomes the shifted curve, `offNode`
 * and `ncShift` ([start, end] in the plan frame, m) are set. Plan nodes are not moved.
 */
export function applyNodeControllerShifts(nc: NodeControllerSection | null | undefined, segments: PlanSegment[]): NcAlignStats {
  const stats: NcAlignStats = { segments: 0, ends: 0, freeForm: 0, clamped: 0, maxShift: 0, sample: [] };
  const shifts = endShifts(nc);
  if (!shifts.size) return stats;
  for (const s of segments) {
    const a = shifts.get(`${s.start}:${s.id}`), b = shifts.get(`${s.end}:${s.id}`);
    if (!a && !b) continue;
    let s0 = 0, s1 = 0;
    for (const [e, set] of [[a, (v: number) => { s0 = v; }], [b, (v: number) => { s1 = v; }]] as const) {
      if (!e) continue;
      if (e.freeForm) { stats.freeForm++; continue; }
      set(e.shift);
    }
    if (!s0 && !s1) continue;
    const r = shiftBezier(s.bezier, s0, s1);
    if (!r) continue;
    s.bezier = r.bezier;
    s.offNode = true;
    s.ncShift = [r3(s0), r3(s1)];
    stats.segments++;
    stats.ends += (s0 ? 1 : 0) + (s1 ? 1 : 0);
    if (r.clamped) stats.clamped++;
    stats.maxShift = Math.max(stats.maxShift, Math.abs(s0), Math.abs(s1));
    if (stats.sample.length < 30) stats.sample.push(s.id);
  }
  stats.maxShift = r3(stats.maxShift);
  return stats;
}

/**
 * buildPackage's entry point: applies the shifts to the plan segments in place (see applyNodeControllerShifts) and
 * returns the counts plus the report items. Call it right after planSegments / planNodes are built.
 */
export function alignNcShifts(planSegments: PlanSegment[], ctx: PlanContext): NcAlignStats & { items: PackageReportItem[] } {
  const stats = applyNodeControllerShifts(ctx.city.nodeController, planSegments);
  const items: PackageReportItem[] = [];
  if (stats.ends) {
    items.push({
      level: 'info', kind: 'node-controller',
      message: `${plural(stats.ends, 'road end')} on ${plural(stats.segments, 'segment')} shifted sideways as Node Controller had them (up to ${stats.maxShift} m), so lanes line up across lane drops, ramps and junctions; the CS2 edges end off the node centre there`,
      count: stats.ends, cs1: { segments: stats.sample },
    });
  }
  if (stats.freeForm) items.push({ level: 'info', kind: 'node-controller', message: `${plural(stats.freeForm, 'free-form road end')} with a Node Controller shift: free-form corners have no CS2 equivalent, the road meets the node centre`, count: stats.freeForm });
  if (stats.clamped) items.push({ level: 'warning', kind: 'node-controller', message: `${plural(stats.clamped, 'short segment')} with a Node Controller shift larger than the segment: the shifted curve was flattened, check it in CS2`, count: stats.clamped });
  return { ...stats, items };
}
