// CS1 buildings → CS2 building prefabs (plan.buildings), using the buildings worker's matcher
// (buildings/match/match.mjs: pure, shared with its CLI). This adapter adds per-type user overrides
// and builds the plan section with the same rules as buildings/lib/plan.mjs (a test checks parity):
//   - buildings outside the crop and sub-buildings are skipped;
//   - the matcher decides place / growable / skip / noEquivalent per building type;
//   - a placed building whose access road is not in the road plan is skipped ("roadNotImported");
//   - front = closest point of the access road's CS1 curve, facing = unit vector building → road, both in
//     plan coordinates; buildings without a road face along the angle convention learned from the others.

import { MATCHER_VERSION, matchAssets } from '../../../../buildings/match/match.mjs';
import { closestOnBezier } from '../../../../buildings/lib/plan.mjs';
import type { Cs1Asset, PackageReportItem, PlanBuildingItem, PlanBuildings } from '../types.ts';
import { plural, tally } from '../m2/context.ts';
import type { PlanContext, SectionResult } from '../m2/context.ts';

export const BUILDINGS_ADAPTER_VERSION = '1.0.0';
export { MATCHER_VERSION };

/** The CS2 prefab catalogue written by `node buildings/run.mjs catalogue` (only the fields the web tool reads). */
export interface Cs2Catalogue {
  game?: { version?: string } | null;
  prefabs: { name: string; kind?: string; placeable?: boolean; lot?: number[] | null; theme?: string | null; [k: string]: unknown }[];
}

export type AssetDecision = 'place' | 'growable' | 'skip' | 'noEquivalent';

export interface AssetMatch {
  cs1: string;
  title: string;
  count: number;
  countInCrop: number;
  growable: boolean;
  service: string | null;
  decision: AssetDecision;
  prefab: string | null;
  lot: number[] | null;
  /** 0–1, null when the user chose the prefab */
  score: number | null;
  reason: string;
  family: string | null;
  alternatives: { prefab: string; score: number }[];
  /** what the user picked for this type, if anything */
  override?: string;
}

/** Per CS1 building type: a CS2 prefab name, or "skip". */
export type AssetOverrides = Record<string, string>;

const THEME_NAME: Record<string, string> = { EU: 'European', NA: 'North American' };

const cleanTitle = (a: Cs1Asset) => (a.title && !/^BUILDING_TITLE\[/.test(a.title) ? a.title : a.name.replace(/^\d+\./, '').replace(/_Data$/, ''));

/** Matches every CS1 building type, then applies the user's overrides. */
export function matchAssetTypes(assets: Cs1Asset[], catalogue: Cs2Catalogue, opts: { theme?: string; overrides?: AssetOverrides } = {}): AssetMatch[] {
  const m = matchAssets({ buildings: assets }, catalogue, { theme: THEME_NAME[opts.theme ?? ''] ?? opts.theme, alternatives: 6 });
  const byName = new Map(catalogue.prefabs.map(p => [p.name, p]));
  const assetByName = new Map(assets.map(a => [a.name, a]));
  return (m.results as Record<string, unknown>[]).map((r): AssetMatch => {
    const a = assetByName.get(r.cs1 as string)!;
    const decision = (r.decision === 'no-equivalent' ? 'noEquivalent' : r.decision) as AssetDecision;
    const out: AssetMatch = {
      cs1: r.cs1 as string, title: cleanTitle(a), count: a.count ?? 0, countInCrop: 0, growable: !!a.growable, service: a.service ?? null,
      decision, prefab: ((r.prefab ?? r.cs2) as string | null) ?? null, lot: ((r.lot ?? r.cs2Lot) as number[] | null) ?? null,
      score: decision === 'place' ? (r.score as number) : null, reason: r.reason as string, family: (r.family as string | null) ?? null,
      alternatives: ((r.alternatives as { prefab?: string; cs2?: string; score: number }[] | undefined) ?? []).map(x => ({ prefab: (x.prefab ?? x.cs2)!, score: x.score })),
    };
    const o = opts.overrides?.[out.cs1];
    if (o === 'skip') Object.assign(out, { decision: 'skip', prefab: null, lot: null, score: null, reason: 'skipped by you', override: o });
    else if (o) {
      const p = byName.get(o);
      if (p) Object.assign(out, { decision: 'place', prefab: p.name, lot: p.lot ?? null, score: null, reason: 'chosen by you', override: o });
    }
    return out;
  });
}

const round = (v: number, n = 2) => Math.round(v * 10 ** n) / 10 ** n + 0;
const CONVENTIONS = ['cos,sin', '-sin,cos', 'sin,-cos', '-cos,-sin'] as const;
/** Unit vector for a CS1 building angle (degrees) under one of the four candidate conventions. */
export function facingFor(conv: string, deg: number): [number, number] {
  const a = (deg * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a);
  return conv === 'cos,sin' ? [c, s] : conv === '-sin,cos' ? [-s, c] : conv === 'sin,-cos' ? [s, -c] : [-c, -s];
}

export interface BuildingsOptions {
  enabled: boolean;
  catalogue: Cs2Catalogue | null;
  overrides?: AssetOverrides;
  theme?: string;
}

export interface BuildingsResult extends SectionResult<PlanBuildings> { matches: AssetMatch[] }

export function buildBuildings(ctx: PlanContext, o: BuildingsOptions): BuildingsResult {
  const items: PackageReportItem[] = [];
  const label = 'Buildings and services';
  const bs = ctx.city.buildings, assets = ctx.city.assets;
  const none = (status: 'absent' | 'not-converted', summary: string, details: string[] = []): BuildingsResult => ({ items, matches: [], summary: { id: 'buildings', label, status, summary, details } });
  if (!bs || !assets) return none('absent', 'no buildings in this export (exporter 1.2 or later exports them)');
  if (!o.enabled) return none('not-converted', 'switched off in the options');
  const inCropAll = bs.buildings.filter(b => ctx.inCrop(b.position[0], b.position[2]) && !b.parent);
  const growablesInCrop = inCropAll.filter(b => b.growable).length;
  if (!o.catalogue) {
    return none('not-converted', `${plural(inCropAll.length - growablesInCrop, 'service building or park')} in the crop wait for the CS2 building catalogue`, [
      'Matching CS1 buildings to CS2 needs the CS2 building catalogue. Load it in the Assets tab (cs2-catalogue.json).',
      `${plural(growablesInCrop, 'growable building')} in the crop are left to CS2 zoning either way.`,
    ]);
  }

  const matches = matchAssetTypes(assets.buildings, o.catalogue, { theme: o.theme, overrides: o.overrides });
  const byName = new Map(matches.map(m => [m.cs1, m]));
  const skipped = { outsideCrop: 0, subBuilding: 0, growable: 0, skip: 0, noEquivalent: 0, roadNotImported: 0 };
  const out: PlanBuildingItem[] = [];
  const conventions = Object.fromEntries(CONVENTIONS.map(c => [c, 0])) as Record<string, number>;
  const notPlaced = new Map<string, number>();
  const noRoad = new Map<string, number>();
  let withRoad = 0;
  for (const b of bs.buildings) {
    const [x, , z] = b.position;
    if (!ctx.inCrop(x, z)) { skipped.outsideCrop++; continue; }
    if (b.parent) { skipped.subBuilding++; continue; }
    const m = byName.get(b.info);
    if (m) m.countInCrop++;
    if (!m) { skipped.noEquivalent++; tally(notPlaced, b.info); continue; }
    if (m.decision !== 'place') { skipped[m.decision]++; if (m.decision !== 'growable') tally(notPlaced, b.info); continue; }
    const item: PlanBuildingItem = {
      id: b.id, cs1Info: b.info, type: 'BuildingPrefab', prefab: m.prefab!, score: m.score, family: m.family,
      x: round(x + ctx.dx), z: round(z + ctx.dz), lot: m.lot, cs1Lot: [b.width, b.length],
      accessSegment: b.accessSegment || 0, front: null, facing: null,
      // tried in order by the placer when the best one does not fit; none when the user chose the prefab
      alternatives: m.override ? [] : m.alternatives.map(a => a.prefab),
      ...(b.district ? { district: b.district } : {}),
    };
    const seg = b.accessSegment ? ctx.segments.get(b.accessSegment) : undefined;
    if (seg) {
      if (!ctx.planSegments.has(seg.id)) { skipped.roadNotImported++; tally(noRoad, b.info); continue; }
      const c = closestOnBezier(seg.bezier, [x, z]);
      const len = Math.hypot(c.point[0] - x, c.point[1] - z) || 1;
      item.front = [round(c.point[0] + ctx.dx), round(c.point[1] + ctx.dz)];
      item.facing = [round((c.point[0] - x) / len, 4), round((c.point[1] - z) / len, 4)];
      withRoad++;
      for (const conv of CONVENTIONS) {
        const f = facingFor(conv, b.angle);
        if (f[0] * item.facing[0] + f[1] * item.facing[1] > 0.9) conventions[conv]++;
      }
    }
    out.push(item);
  }
  const best = Object.entries(conventions).sort((a, b) => b[1] - a[1])[0];
  const angleOf = new Map(bs.buildings.map(b => [b.id, b.angle]));
  for (const it of out) {
    if (it.facing) continue;
    const f = facingFor(best[0], angleOf.get(it.id) ?? 0);
    it.facing = [round(f[0], 4), round(f[1], 4)];
  }

  // report
  const prefabs = new Set(out.map(i => i.prefab));
  const chosen = matches.filter(m => m.override && m.countInCrop);
  if (out.length) items.push({ level: 'info', kind: 'buildings', message: `${plural(out.length, 'building')} placed as ${plural(prefabs.size, 'CS2 building type')} (${withRoad} on an imported road)`, count: out.length });
  if (skipped.growable) items.push({ level: 'info', kind: 'buildings', message: `${plural(skipped.growable, 'growable building')} left to CS2 zoning (CS2 grows its own on the converted zones)`, count: skipped.growable });
  for (const [info, n] of [...notPlaced].sort((a, b) => b[1] - a[1])) {
    const m = byName.get(info);
    const d = m?.decision ?? 'noEquivalent';
    items.push({ level: d === 'noEquivalent' ? 'warning' : 'info', kind: 'buildings', message: `${m?.title ?? info}: ${plural(n, 'building')} not placed – ${m?.reason ?? 'not in assets.json'}`, count: n, cs1: { asset: info } });
  }
  if (skipped.roadNotImported) items.push({ level: 'warning', kind: 'buildings', message: `${plural(skipped.roadNotImported, 'building')} not placed because their access road is not in the plan (crop edge or a skipped road type): ${[...noRoad].slice(0, 8).map(([k, v]) => `${byName.get(k)?.title ?? k} ${v}`).join(', ')}`, count: skipped.roadNotImported });
  if (chosen.length) items.push({ level: 'info', kind: 'buildings', message: `${plural(chosen.length, 'building type')} mapped by you: ${chosen.map(m => `${m.title} → ${m.override}`).join(', ')}` });

  const noEq = matches.filter(m => m.decision === 'noEquivalent' && m.countInCrop).reduce((t, m) => t + m.countInCrop, 0);
  const section: PlanBuildings = {
    generator: { matcher: MATCHER_VERSION, web: BUILDINGS_ADAPTER_VERSION },
    catalogue: { game: o.catalogue.game?.version ?? null, prefabs: o.catalogue.prefabs.length },
    cs1AngleConvention: { facing: best[0], agreement: withRoad ? round(best[1] / withRoad, 3) : null, buildingsWithRoad: withRoad },
    items: out,
    stats: { items: out.length, withRoad, withoutRoad: out.length - withRoad, growablesInCrop, skipped },
  };
  return {
    section, items, matches,
    summary: {
      id: 'buildings', label,
      status: !out.length ? 'not-converted' : noEq || skipped.roadNotImported ? 'partial' : 'converted',
      summary: `${plural(out.length, 'building')} placed as ${plural(prefabs.size, 'CS2 type')}; ${plural(skipped.growable, 'growable')} left to zoning`,
      details: [
        'Service buildings, stations, parks and landmarks get the closest CS2 building: same kind of service, then capacity and lot size. Check and change each type in the Assets tab.',
        'Growable (zoned) buildings are not placed: CS2 grows its own on the converted zones.',
        ...(noEq ? [`${plural(noEq, 'building')} have no CS2 equivalent (e.g. libraries).`] : []),
        ...(skipped.roadNotImported ? [`${plural(skipped.roadNotImported, 'building')} stand on roads that are not in the plan.`] : []),
        'Pillars, pipe junctions, poles and sub-buildings are rebuilt by CS2 itself.',
      ],
    },
  };
}
