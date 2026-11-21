// The player's own CS1 power lines and water pipes (docs/DECISIONS.md #115) → plan.utilities, for the buildings
// worker's placer. Only the ones away from the converted roads: CS2 roads carry low voltage, water and sewage
// themselves, so a power line or pipe running along a road is left to CS2.
//
// Along a road = at least ALONG_SHARE of its sample points lie within the road's CS1 half-width + ALONG_MARGIN of a
// plan road segment (RoadPrefab / RoadBuilderPrefab, any kind).

import type { Bezier, Cs1Node, Cs1Segment, NetInfo, PackageReportItem, PlanPrefab, PlanSegment, SegmentKind } from '../types.ts';

export const ALONG_SHARE = 0.7;
export const ALONG_MARGIN = 4;
const SAMPLES = 10;
const CELL = 32;

export interface PlanUtilitySegment {
  /** CS1 segment id */
  id: number;
  utility: 'power' | 'pipe';
  cs1Info: string;
  /** CS1 node ids, as in plan.segments (not reversed), so segments chain and ends snap to plants, pumps and outlets */
  start: number;
  end: number;
  bezier: Bezier;
  elevationStart: number;
  elevationEnd: number;
  kind: 'ground' | 'elevated' | 'tunnel';
}

export interface PlanUtilities {
  formatVersion: 1;
  segments: PlanUtilitySegment[];
  stats: { power: number; pipe: number; alongRoad: number; alongRoadPower: number; alongRoadPipe: number; buildingNet: number; crossingCrop: number };
}

export interface UtilitiesInput {
  segments: Cs1Segment[];
  nodes: Map<number, Cs1Node>;
  infos: Map<string, NetInfo>;
  inCrop: (x: number, z: number) => boolean;
  dx: number; dz: number;
  planSegments: PlanSegment[];
  prefabs: Record<string, PlanPrefab>;
  segmentKind: (s: Cs1Segment, info: NetInfo | undefined) => SegmentKind;
  nodeElevation: (kind: SegmentKind, node: Cs1Node | undefined) => number;
}

const r3 = (v: number) => Math.round(v * 1000) / 1000;

function bez(b: number[][], t: number): [number, number] {
  const u = 1 - t, b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
  return [b0 * b[0][0] + b1 * b[1][0] + b2 * b[2][0] + b3 * b[3][0], b0 * b[0][2] + b1 * b[1][2] + b2 * b[2][2] + b3 * b[3][2]];
}

export function utilityKind(s: Cs1Segment): 'power' | 'pipe' | null {
  const ai = s.ai ?? '';
  return /PowerLineAI/.test(ai) ? 'power' : /WaterPipeAI/.test(ai) ? 'pipe' : null;
}

export function buildUtilities(inp: UtilitiesInput): { section: PlanUtilities | null; items: PackageReportItem[] } {
  const items: PackageReportItem[] = [];
  const stats = { power: 0, pipe: 0, alongRoad: 0, alongRoadPower: 0, alongRoadPipe: 0, buildingNet: 0, crossingCrop: 0 };
  const cands = inp.segments.filter(s => utilityKind(s));
  if (!cands.length) return { section: null, items };

  // spatial hash of the plan's road pieces (plan coordinates)
  const pieces: { ax: number; az: number; bx: number; bz: number; w: number }[] = [];
  const grid = new Map<string, number[]>();
  const key = (cx: number, cz: number) => `${cx},${cz}`;
  for (const ps of inp.planSegments) {
    const t = inp.prefabs[ps.prefab]?.type;
    if (t !== 'RoadPrefab' && t !== 'RoadBuilderPrefab') continue;
    const w = (ps.halfWidth ?? 0) + ALONG_MARGIN;
    let prev = bez(ps.bezier as unknown as number[][], 0);
    for (let k = 1; k <= 8; k++) {
      const p = bez(ps.bezier as unknown as number[][], k / 8);
      const i = pieces.push({ ax: prev[0], az: prev[1], bx: p[0], bz: p[1], w }) - 1;
      const x0 = Math.floor((Math.min(prev[0], p[0]) - w) / CELL), x1 = Math.floor((Math.max(prev[0], p[0]) + w) / CELL);
      const z0 = Math.floor((Math.min(prev[1], p[1]) - w) / CELL), z1 = Math.floor((Math.max(prev[1], p[1]) + w) / CELL);
      for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) { const kk = key(cx, cz); const l = grid.get(kk); if (l) l.push(i); else grid.set(kk, [i]); }
      prev = p;
    }
  }
  const nearRoad = (x: number, z: number) => {
    for (const i of grid.get(key(Math.floor(x / CELL), Math.floor(z / CELL))) ?? []) {
      const p = pieces[i], ex = p.bx - p.ax, ez = p.bz - p.az, len2 = ex * ex + ez * ez;
      let u = len2 > 0 ? ((x - p.ax) * ex + (z - p.az) * ez) / len2 : 0;
      if (u < 0) u = 0; else if (u > 1) u = 1;
      const dx = x - (p.ax + u * ex), dz = z - (p.az + u * ez);
      if (dx * dx + dz * dz <= p.w * p.w) return true;
    }
    return false;
  };

  const out: PlanUtilitySegment[] = [];
  for (const s of cands) {
    const sn = inp.nodes.get(s.startNode), en = inp.nodes.get(s.endNode);
    const a = !!sn && inp.inCrop(sn.position[0], sn.position[2]), b = !!en && inp.inCrop(en.position[0], en.position[2]);
    if (!a && !b) continue;
    if (!a || !b) { stats.crossingCrop++; continue; }
    if (/(^|,\s*)Untouchable(,|$)/.test(s.flags ?? '')) { stats.buildingNet++; continue; }
    const utility = utilityKind(s)!;
    const bz = s.bezier.map(p => [r3(p[0] + inp.dx), r3(p[1]), r3(p[2] + inp.dz)]) as Bezier;
    let near = 0;
    for (let k = 0; k < SAMPLES; k++) { const p = bez(bz as unknown as number[][], (k + 0.5) / SAMPLES); if (nearRoad(p[0], p[1])) near++; }
    if (near / SAMPLES >= ALONG_SHARE) { stats.alongRoad++; if (utility === 'power') stats.alongRoadPower++; else stats.alongRoadPipe++; continue; }
    const info = inp.infos.get(s.info);
    const k = inp.segmentKind(s, info);
    const kind = k === 'elevated' || k === 'bridge' ? 'elevated' : k === 'tunnel' ? 'tunnel' : 'ground';
    out.push({ id: s.id, utility, cs1Info: s.info, start: s.startNode, end: s.endNode, bezier: bz, elevationStart: inp.nodeElevation(k, sn), elevationEnd: inp.nodeElevation(k, en), kind });
    stats[utility]++;
  }
  out.sort((x, y) => x.id - y.id);
  if (out.length || stats.alongRoad) {
    items.push({
      level: 'info', kind: 'utilities',
      message: `${stats.power} power line and ${stats.pipe} pipe segment(s) away from the roads → plan.utilities (placed by the importer); ${stats.alongRoad} along converted roads left to CS2 (roads carry power and water)${stats.buildingNet ? `; ${stats.buildingNet} part of buildings` : ''}${stats.crossingCrop ? `; ${stats.crossingCrop} cross the crop edge` : ''}`,
      count: out.length,
    });
  }
  return { section: { formatVersion: 1, segments: out, stats }, items };
}
