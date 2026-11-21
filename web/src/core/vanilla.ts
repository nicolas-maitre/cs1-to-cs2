// Prefer vanilla CS2 roads when they match (docs/DECISIONS.md #100). Pure: compares the cross-section of the road
// cs1conv would generate (rb.ts RoadProfile) with the vanilla CS2 road layouts dumped from the game
// (cs2-roads.json, worker-3) and picks the closest vanilla road within the tolerances below, or says why none fits.
//
// Tolerances (web/DECISIONS-web.md):
//  - same category (road / highway);
//  - per side of the centre line, the same driving lanes (car / bus / tram, with combinations like car+tram) in the same
//    order outwards, with the same direction; each within ±0.5 m in width and ±0.75 m in position;
//  - parking and bike lanes: present on the same sides;
//  - median: both or neither (wider than 1 m: a narrower gap between the driving lanes is lane layout), widths within ±1 m;
//  - total width within ±1 m;
//  - a speed set by TM:PE within ±5 km/h of the vanilla road's (a TM:PE speed variant goes vanilla only at that speed).
//    The CS1 road's default speed doesn't count: CS1 and CS2 defaults differ (Basic Road 50 vs Small Road 40 km/h,
//    highways 100 vs 120 km/h), and the vanilla road keeps its own;
//  - no tree addons on the generated road (vanilla roads have none; trees would be lost).
// Shoulders, sidewalk widths and "other" filler only count through the total width.
//
// cs2-roads.json (cs2-importer RoadLayouts.cs, format "cs2-roads" v1): roads[] with name, highwayRules, speedKmh, width,
// medianWidth, oneWay, zoning and lanes[] {type car|bus|tram|car+tram|bike|parking|pedestrian, dir forward|backward|both|null,
// offset (+ = right), width}. A highway is a road with highwayRules. The median is measured here from the lanes (the gap
// between the innermost driving lanes), the same way the dump's medianWidth now is; the first dump summed the composition's
// Median pieces (kept as medianPieces), which are non-zero on roads without a median. Bridges, quays and dams have ordinary
// road lanes but are special-purpose roads: never chosen (by name; uiGroup doesn't separate them).

import type { ProfileLane, RoadProfile } from './rb.ts';

export interface Cs2Road {
  name: string;
  type?: string;
  /** road / highway; when absent, from highwayRules */
  category?: 'road' | 'highway' | 'other' | string;
  highwayRules?: boolean;
  width: number;
  /** median pieces of the composition (m), when the dump gives it */
  medianWidth?: number;
  speedKmh?: number | null;
  oneWay?: boolean;
  zoning?: boolean;
  lanes: ProfileLane[];
}

export interface Cs2RoadCatalogue {
  format?: string;
  game?: string | null;
  roads: Cs2Road[];
}

export const TOLERANCE = { laneWidth: 0.5, laneOffset: 0.75, totalWidth: 1, median: 1, speedKmh: 5 } as const;

const DRIVE = /^(car|bus|tram)([,+](car|bus|tram))*$/;
const parts = (t: string) => t.split(/[,+]/).map(s => s.trim());
const typeKey = (t: string) => parts(t).sort().join('+');
const isDrive = (l: ProfileLane) => DRIVE.test(l.type.replace(/\s/g, ''));
/** -1 left, 0 centre (a lane on the centre line, e.g. the middle lane of a 3-lane highway), 1 right */
const sideOf = (l: ProfileLane): -1 | 0 | 1 => (Math.abs(l.offset) < 0.25 ? 0 : l.offset < 0 ? -1 : 1);
const has = (lanes: ProfileLane[], type: string, side: -1 | 0 | 1) => lanes.some(l => parts(l.type).includes(type) && sideOf(l) === side);
/** A gap between the driving lanes up to the median tolerance (1 m) is lane layout, not a median. */
const MEDIAN_MIN = TOLERANCE.median;
export const roadCategory = (r: Cs2Road): string => r.category ?? (r.highwayRules ? 'highway' : 'road');
const SPECIAL = /bridge|quay|\bdam\b/i;
const isRoad = (r: Cs2Road & { uiGroup?: string | null }) => { const c = roadCategory(r); return (c === 'road' || c === 'highway') && !SPECIAL.test(r.name) && r.uiGroup !== 'Electricity'; };

/** The median: an explicit median lane, else the gap between the innermost driving lanes of the two sides. */
function medianWidth(lanes: ProfileLane[]): number {
  const m = lanes.filter(l => l.type === 'median').reduce((t, l) => t + l.width, 0);
  if (m) return m;
  const drive = lanes.filter(isDrive);
  if (drive.some(l => Math.abs(l.offset) < l.width / 2)) return 0; // a driving lane across the centre line
  const left = drive.filter(l => l.offset < 0), right = drive.filter(l => l.offset > 0);
  if (!left.length || !right.length) return 0;
  const l = left.reduce((a, b) => (b.offset > a.offset ? b : a)), r = right.reduce((a, b) => (b.offset < a.offset ? b : a));
  const gap = (r.offset - r.width / 2) - (l.offset + l.width / 2);
  return gap > MEDIAN_MIN ? gap : 0;
}

/** Why `ours` doesn't fit `road` (empty = it fits). */
export function profileDifferences(ours: RoadProfile, road: Cs2Road): string[] {
  const why: string[] = [];
  const cat = roadCategory(road) === 'highway' ? 'highway' : 'road';
  if (cat !== ours.category) why.push(`${ours.category} vs ${cat}`);
  if (ours.trees) why.push('trees (vanilla roads have none)');
  if (Math.abs(ours.width - road.width) > TOLERANCE.totalWidth) why.push(`width ${ours.width} vs ${road.width} m`);
  if (ours.speedSet && road.speedKmh && Math.abs(ours.speedKmh - road.speedKmh) > TOLERANCE.speedKmh) why.push(`TM:PE speed ${ours.speedKmh} vs ${road.speedKmh} km/h`);
  for (const side of [-1, 0, 1] as const) {
    const name = side < 0 ? 'left' : side > 0 ? 'right' : 'centre';
    const outwards = (ls: ProfileLane[]) => ls.filter(l => isDrive(l) && sideOf(l) === side).sort((a, b) => Math.abs(a.offset) - Math.abs(b.offset));
    const a = outwards(ours.lanes), b = outwards(road.lanes);
    if (a.length !== b.length) { why.push(`${a.length} vs ${b.length} driving lanes on the ${name}`); continue; }
    a.forEach((l, i) => {
      const v = b[i];
      if (typeKey(l.type) !== typeKey(v.type)) why.push(`${name} lane ${i + 1}: ${l.type} vs ${v.type}`);
      else if (l.dir !== v.dir) why.push(`${name} lane ${i + 1}: ${l.dir} vs ${v.dir}`);
      else if (Math.abs(l.width - v.width) > TOLERANCE.laneWidth) why.push(`${name} lane ${i + 1}: ${l.width} vs ${v.width} m wide`);
      else if (Math.abs(l.offset - v.offset) > TOLERANCE.laneOffset) why.push(`${name} lane ${i + 1} at ${l.offset} vs ${v.offset} m`);
    });
    if (side !== 0) for (const t of ['parking', 'bike']) if (has(ours.lanes, t, side) !== has(road.lanes, t, side)) why.push(`${t} on the ${name}: ${has(ours.lanes, t, side) ? 'yes' : 'no'} vs ${has(road.lanes, t, side) ? 'yes' : 'no'}`);
  }
  const ma = medianWidth(ours.lanes), mb = medianWidth(road.lanes);
  if ((ma > MEDIAN_MIN) !== (mb > MEDIAN_MIN) || Math.abs(ma - mb) > TOLERANCE.median) why.push(`median ${ma > MEDIAN_MIN ? `${ma} m` : 'none'} vs ${mb > MEDIAN_MIN ? `${mb} m` : 'none'}`);
  return why;
}

export interface VanillaMatch {
  /** the matched vanilla road, or null */
  road: Cs2Road | null;
  /** one line for the UI and the report: what matched, or the closest road and why it doesn't fit */
  reason: string;
}

/** The vanilla road that fits the profile best (smallest total-width difference), or the closest miss with its reason. */
export function matchVanilla(ours: RoadProfile, catalogue: Cs2RoadCatalogue | null | undefined): VanillaMatch {
  const roads = (catalogue?.roads ?? []).filter(isRoad);
  if (!roads.length) return { road: null, reason: 'no vanilla road list' };
  const scored = roads.map(r => ({ r, why: profileDifferences(ours, r), dw: Math.abs(ours.width - r.width) }))
    .sort((a, b) => a.why.length - b.why.length || a.dw - b.dw || a.r.name.localeCompare(b.r.name));
  const best = scored[0];
  if (!best.why.length) return { road: best.r, reason: `same lanes as ${best.r.name} (width ${ours.width} vs ${best.r.width} m)` };
  return { road: null, reason: `closest: ${best.r.name} (${best.why.slice(0, 3).join('; ')})` };
}

/** Only what the matcher reads, for the bundled public/cs2-roads.json. */
export function slimRoads(full: Cs2RoadCatalogue): Cs2RoadCatalogue {
  return {
    format: 'cs2-roads-slim', game: full.game ?? null,
    roads: full.roads.filter(isRoad).map(r => ({
      name: r.name, category: roadCategory(r), width: r.width, speedKmh: r.speedKmh ?? null,
      ...(r.oneWay != null ? { oneWay: r.oneWay } : {}), ...(r.zoning != null ? { zoning: r.zoning } : {}),
      lanes: r.lanes.map(l => ({ type: l.type, dir: l.dir ?? 'none', offset: l.offset, width: l.width })),
    })),
  };
}
