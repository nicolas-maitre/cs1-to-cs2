// CS1 districts → CS2 districts (plan.districts): name, outline and the policies CS2 has an equivalent for.
// Outlines are traced from the exporter's district raster (cell edges, holes dropped), clipped to the crop.
// Park areas (parks, industry areas, campuses, airports) are reported: CS2 has no park areas, and its
// specialised industry works through resource areas instead.

import type { PackageReportItem, PlanDistricts } from '../types.ts';
import { plural, r1, tally } from '../m2/context.ts';
import type { PlanContext, SectionResult } from '../m2/context.ts';

/** CS1 DistrictPolicies → CS2 policy prefab (names from the CS2 policy list, Policy.TITLE[…]). */
export const POLICY_MAP: Record<string, { cs2: string; approx?: string }> = {
  HeavyTrafficBan: { cs2: 'Heavy Traffic Ban' },
  CombustionEngineBan: { cs2: 'Combustion Engine Ban' },
  Recycling: { cs2: 'Recycling' },
  EncourageBiking: { cs2: 'Urban Cycling Initiative' },
  BikeBan: { cs2: 'Bicycle Traffic Restriction' },
  SmallBusiness: { cs2: 'Small Business Enthusiast', approx: 'small business enthusiasts' },
  SmokeDetectors: { cs2: 'Fire Safety Sprinkler System', approx: 'smoke detectors → sprinkler systems' },
  PowerSaving: { cs2: 'Energy Consumption Awareness', approx: 'power saving → energy consumption awareness' },
  PaidParking: { cs2: 'Roadside Parking Fee', approx: 'paid parking → roadside parking fee' },
};
const SPECIALISATIONS = new Set(['Leisure', 'Tourist', 'Organic', 'Hightech', 'SelfSufficient', 'Forest', 'Farming', 'Oil', 'Ore', 'Wall2Wall', 'HighTech', 'Hotels', 'Financial']);

export function mapPolicies(cs1: string[]): { cs2: string[]; approx: string[]; specialisation: string[]; none: string[] } {
  const out = { cs2: [] as string[], approx: [] as string[], specialisation: [] as string[], none: [] as string[] };
  for (const p of cs1) {
    const m = POLICY_MAP[p];
    if (m) { if (!out.cs2.includes(m.cs2)) out.cs2.push(m.cs2); if (m.approx) out.approx.push(m.approx); }
    else if (SPECIALISATIONS.has(p)) out.specialisation.push(p);
    else out.none.push(p);
  }
  return out;
}

/**
 * Outer outlines of the cells equal to `id` (and passing `keep`) of a row-major grid, as closed polygons in
 * grid vertex coordinates (x = column, y = row). Interior is on the left (counter-clockwise outers); holes
 * (clockwise loops) are returned separately. Collinear vertices are removed.
 */
export function traceOutlines(grid: Uint8Array, n: number, id: number, keep: (c: number, r: number) => boolean = () => true): { outers: [number, number][][]; holes: number } {
  const inside = (c: number, r: number) => c >= 0 && r >= 0 && c < n && r < n && grid[r * n + c] === id && keep(c, r);
  const next = new Map<number, number[]>(); // start vertex → end vertices
  const key = (x: number, y: number) => y * (n + 1) + x;
  const add = (x0: number, y0: number, x1: number, y1: number) => {
    const k = key(x0, y0);
    const l = next.get(k);
    if (l) l.push(key(x1, y1)); else next.set(k, [key(x1, y1)]);
  };
  for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) {
    if (!inside(c, r)) continue;
    if (!inside(c, r - 1)) add(c, r, c + 1, r);
    if (!inside(c + 1, r)) add(c + 1, r, c + 1, r + 1);
    if (!inside(c, r + 1)) add(c + 1, r + 1, c, r + 1);
    if (!inside(c - 1, r)) add(c, r + 1, c, r);
  }
  const outers: [number, number][][] = [];
  let holes = 0;
  for (const [start, ends] of next) {
    while (ends.length) {
      const loop: number[] = [start];
      let cur = ends.pop()!;
      while (cur !== start) {
        loop.push(cur);
        const l = next.get(cur);
        if (!l || !l.length) break;
        cur = l.pop()!;
      }
      const pts = loop.map(k => [k % (n + 1), Math.floor(k / (n + 1))] as [number, number]);
      const simple = pts.filter((p, i) => {
        const a = pts[(i - 1 + pts.length) % pts.length], b = pts[(i + 1) % pts.length];
        return (p[0] - a[0]) * (b[1] - p[1]) - (p[1] - a[1]) * (b[0] - p[0]) !== 0;
      });
      let area = 0;
      for (let i = 0; i < simple.length; i++) { const a = simple[i], b = simple[(i + 1) % simple.length]; area += a[0] * b[1] - b[0] * a[1]; }
      if (area > 0) outers.push(simple); else holes++;
    }
  }
  return { outers, holes };
}

export function buildDistricts(ctx: PlanContext, enabled = true): SectionResult<PlanDistricts> {
  const d = ctx.city.districts;
  const items: PackageReportItem[] = [];
  const tr = ctx.t, label = tr('cat.districts');
  if (!d) return { items, summary: { id: 'districts', label, status: 'absent', summary: tr('sum.districts.absent'), details: [] } };
  if (!enabled) return { items, summary: { id: 'districts', label, status: 'not-converted', summary: tr('sum.off'), details: [] } };
  const g = d.grid, n = g.resolution, cs = g.cellSize, half = g.size / 2;
  const centreIn = (c: number, r: number) => ctx.inCrop(-half + (c + 0.5) * cs, -half + (r + 0.5) * cs);
  const city = mapPolicies(d.city?.policies ?? []);
  const result: PlanDistricts = { items: [], cityPolicies: city.cs2 };
  const unconverted = new Map<string, number>();
  let holes = 0;
  for (const dist of d.districts) {
    if (!d.districtGrid) break;
    const t = traceOutlines(d.districtGrid, n, dist.id, centreIn);
    if (!t.outers.length) continue;
    holes += t.holes;
    let cells = 0;
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (d.districtGrid[r * n + c] === dist.id && centreIn(c, r)) cells++;
    const p = mapPolicies(dist.policies ?? []);
    for (const x of [...p.none, ...p.specialisation]) tally(unconverted, x);
    result.items.push({
      cs1Id: dist.id, name: dist.name,
      outlines: t.outers.map(o => o.map(([x, y]) => [r1(-half + x * cs + ctx.dx), r1(-half + y * cs + ctx.dz)] as [number, number])),
      policies: p.cs2, cs1Policies: dist.policies ?? [], area: Math.round(cells * cs * cs),
    });
    for (const a of p.approx) items.push({ level: 'info', kind: 'districts', message: `${dist.name}: policy ${a} (approximation)` });
  }
  for (const x of [...city.none, ...city.specialisation]) tally(unconverted, x);
  if (result.items.length) items.push({ level: 'info', kind: 'districts', message: `${plural(result.items.length, 'district')} in the crop: ${result.items.map(i => `${i.name}${i.policies.length ? ` (${i.policies.join(', ')})` : ''}`).join('; ')}`, count: result.items.length });
  if (city.cs2.length) items.push({ level: 'info', kind: 'districts', message: `city-wide policies: ${city.cs2.join(', ')}` });
  for (const [p, k] of unconverted) items.push({ level: 'info', kind: 'districts', message: `policy ${p} (${plural(k, 'district')}): ${SPECIALISATIONS.has(p) ? 'a zone specialisation; CS2 has no such policy, the zones stay generic' : 'no CS2 equivalent'}`, count: k });
  if (holes) items.push({ level: 'info', kind: 'districts', message: `${plural(holes, 'hole')} inside districts are filled (CS2 districts are simple areas)`, count: holes });
  const parks = d.parks.filter(p => p.mainGate || p.cells);
  const parkKinds = new Map<string, number>();
  for (const p of parks) tally(parkKinds, p.kind ?? 'other');
  if (parks.length) items.push({ level: 'info', kind: 'districts', message: `${plural(parks.length, 'park area')} (${[...parkKinds].map(([k, v]) => `${k} ${v}`).join(', ')}): CS2 has no park areas; their buildings are placed as single buildings`, count: parks.length });
  return {
    section: result.items.length || result.cityPolicies.length ? result : undefined,
    items,
    summary: {
      id: 'districts', label,
      status: !result.items.length && !city.cs2.length ? 'none' : unconverted.size ? 'partial' : 'converted',
      summary: [
        result.items.length ? tr(result.items.some(i => i.policies.length) ? 'sum.districts.withPolicies' : 'sum.districts', { n: result.items.length }) : d.districts.length ? tr('sum.districts.noneInCrop') : tr('sum.districts.none'),
        ...(city.cs2.length ? [tr('sum.districts.city', { policies: city.cs2.join(', ') })] : []),
      ].join(ctx.lang === 'fr' ? ' ; ' : '; '),
      details: [
        tr('sum.districts.d1'),
        tr('sum.districts.d2'),
        tr('sum.districts.d3'),
      ],
    },
  };
}
