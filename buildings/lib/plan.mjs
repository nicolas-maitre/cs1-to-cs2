// Builds the `buildings` section of an import plan (proposal: buildings/README.md "Plan format") from the CS1 export,
// the CS2 catalogue and the road plan. Pure: no I/O.
//
// Per CS1 building inside the road plan's crop:
//   - sub-buildings are skipped (they come with their parent);
//   - the matcher decides place / growable / skip / noEquivalent per building type;
//   - a placed building whose access road (CS1 accessSegment) is not in the road plan is skipped ("roadNotImported"),
//     because CS2 needs a road to connect it to;
//   - the front point is the closest point of the access segment's CS1 curve to the building, and `facing` points from
//     the building to it. Both are in plan coordinates (CS1 + transform), like the road plan's nodes; the importer adds
//     its own offset. CS2 snaps the lot to the real imported road at placement time.
import { matchAssets, MATCHER_VERSION } from '../match/match.mjs';

export const PLAN_BUILDER_VERSION = '1.0.0';

function bezierPoint(b, t) {
  const u = 1 - t;
  return [0, 2].map((k) => u * u * u * b[0][k] + 3 * u * u * t * b[1][k] + 3 * u * t * t * b[2][k] + t * t * t * b[3][k]);
}

/** Closest point (x, z) of a cubic bezier ([[x,y,z]×4]) to p = [x, z]: 64 samples, then a local refinement. */
export function closestOnBezier(b, p) {
  let bestT = 0, bestD = Infinity;
  const d2 = (q) => (q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2;
  for (let i = 0; i <= 64; i++) { const t = i / 64; const d = d2(bezierPoint(b, t)); if (d < bestD) { bestD = d; bestT = t; } }
  let step = 1 / 64;
  for (let k = 0; k < 20; k++) {
    step /= 2;
    for (const t of [bestT - step, bestT + step]) {
      if (t < 0 || t > 1) continue;
      const d = d2(bezierPoint(b, t));
      if (d < bestD) { bestD = d; bestT = t; }
    }
  }
  return { point: bezierPoint(b, bestT), t: bestT, distance: Math.sqrt(bestD) };
}

const round = (v, n = 2) => Math.round(v * 10 ** n) / 10 ** n + 0; // + 0 turns −0 into 0

/**
 * @param {{network: object, buildings: object, assets: object, manifest?: object}} cs1 – parsed .cs1x sections
 * @param {object} catalogue – cs2-catalogue.json
 * @param {object} roadPlan  – cs1conv-plan.json (crop, transform, segments)
 * @param {{theme?: string, minScore?: number, fallback?: 'nearby'|'anywhere'|'skip', fallbackByType?: Object<string, 'nearby'|'anywhere'|'skip'>}} [opts]
 */
export function buildBuildingsPlan(cs1, catalogue, roadPlan, opts = {}) {
  if (!cs1.buildings || !cs1.assets || !cs1.network) throw new Error('the .cs1x needs ok buildings, assets and network sections (exporter 1.2+)');
  const match = matchAssets(cs1.assets, catalogue, { alternatives: 6, ...opts });
  const { centerX, centerZ, size } = roadPlan.crop;
  const { dx, dz } = roadPlan.transform;
  const half = size / 2;
  const segs = new Map(cs1.network.segments.map((s) => [s.id, s]));
  const planned = new Set((roadPlan.segments ?? []).map((s) => s.id));

  const items = [];
  const skipped = { outsideCrop: 0, subBuilding: 0, growable: 0, skip: 0, noEquivalent: 0, roadNotImported: 0 };
  const typesUsed = new Map();
  // Which CS1 angle convention points at the road? Learned from the buildings that have a road, used for the rest.
  const conventions = { 'cos,sin': 0, '-sin,cos': 0, 'sin,-cos': 0, '-cos,-sin': 0 };
  const facingFor = (conv, deg) => {
    const a = (deg * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a);
    return conv === 'cos,sin' ? [c, s] : conv === '-sin,cos' ? [-s, c] : conv === 'sin,-cos' ? [s, -c] : [-c, -s];
  };
  let withRoad = 0;

  for (const b of cs1.buildings.buildings) {
    const [x, , z] = b.position;
    if (Math.abs(x - centerX) > half || Math.abs(z - centerZ) > half) { skipped.outsideCrop++; continue; }
    if (b.parent) { skipped.subBuilding++; continue; }
    const m = match.byName[b.info];
    if (!m) { skipped.noEquivalent++; continue; }
    if (m.decision !== 'place') { skipped[m.decision] = (skipped[m.decision] ?? 0) + 1; continue; }

    const item = {
      id: b.id, cs1Info: b.info, prefab: m.cs2, score: m.score, family: m.family,
      x: round(x + dx), z: round(z + dz), cs1Lot: [b.width, b.length], lot: m.cs2Lot ?? null,
      accessSegment: b.accessSegment || 0, front: null, facing: null,
      alternatives: (m.alternatives ?? []).map((a) => a.cs2), // tried in order when the best one doesn't fit
    };
    // What the placer does when no slide or alternative fits: 'nearby' (free land within the run's radius) or 'skip'.
    // Only written when the caller chose one, so the default output stays unchanged.
    const fb = opts.fallbackByType?.[b.info] ?? opts.fallback;
    if (fb) item.fallback = fb;
    const seg = b.accessSegment ? segs.get(b.accessSegment) : null;
    if (seg) {
      if (!planned.has(seg.id)) { skipped.roadNotImported++; continue; }
      const c = closestOnBezier(seg.bezier, [x, z]);
      const len = Math.hypot(c.point[0] - x, c.point[1] - z) || 1;
      item.front = [round(c.point[0] + dx), round(c.point[1] + dz)];
      item.facing = [round((c.point[0] - x) / len, 4), round((c.point[1] - z) / len, 4)];
      withRoad++;
      for (const conv of Object.keys(conventions)) {
        const f = facingFor(conv, b.angle);
        if (f[0] * item.facing[0] + f[1] * item.facing[1] > 0.9) conventions[conv]++;
      }
    }
    items.push(item);
    typesUsed.set(b.info, (typesUsed.get(b.info) ?? 0) + 1);
  }

  const best = Object.entries(conventions).sort((a, b) => b[1] - a[1])[0];
  for (const it of items) {
    if (it.facing) continue;
    const b = cs1.buildings.buildings.find((x) => x.id === it.id);
    const f = facingFor(best[0], b.angle);
    it.facing = [round(f[0], 4), round(f[1], 4)];
  }

  const out = {
    format: 'cs1conv-buildings', formatVersion: 1,
    generator: { name: 'cs1conv-buildings', version: PLAN_BUILDER_VERSION, matcher: MATCHER_VERSION },
    catalogue: { game: catalogue.game?.version ?? null, prefabs: catalogue.prefabs?.length ?? 0 },
    crop: roadPlan.crop, transform: { dx, dz },
    cs1AngleConvention: { facing: best[0], agreement: withRoad ? round(best[1] / withRoad, 3) : null, buildingsWithRoad: withRoad },
    items,
    stats: { items: items.length, withRoad, withoutRoad: items.length - withRoad, skipped },
    types: [...typesUsed.entries()].map(([name, n]) => {
      const m = match.byName[name];
      return { cs1: name, count: n, cs2: m.cs2, score: m.score, reason: m.reason, alternatives: m.alternatives };
    }).sort((a, b) => b.count - a.count),
    notPlaced: match.results.filter((r) => r.decision !== 'place').map((r) => ({ cs1: r.cs1, decision: r.decision, reason: r.reason, count: r.count })),
  };
  if (opts.defaultDepots !== false && roadPlan.transit) addDefaultDepots(out, roadPlan.transit, catalogue);
  return out;
}

// ------------------------------------------------------------------ default depots for transit lines

/**
 * CS2 lines get their vehicles only from a depot of their own type, so a converted city with lines but no depot of
 * that type gets one: CS1 line type → CS2 depot prefab. Road depots only: bus and tram depots need just a road.
 * Metro and train yards are NOT added here (DECISIONS.md #83): a yard only dispatches over track connected to the
 * line, so they go at the crop edge where the line's track leaves, planned in game by transit/'s `yards.plan` (in this
 * item shape, placed by the same placer) and linked by its `tracklink`.
 */
export const DEPOT_FOR_LINE = {
  Bus: { prefab: 'BusDepot01', match: /^BusDepot/ },
  Tram: { prefab: 'TramDepot01', match: /^TramDepot/ },
};

/**
 * Adds one default bus or tram depot item to a buildings section (in place) for every such transport type that has
 * lines in plan.transit but no depot of that type among the items. Pure; the web tool calls it on its own buildings
 * section. The item sits on the first stop of the first such line with fallback "nearby", so the placer puts it on
 * the closest free roadside spot. Returns the list of added depots.
 * @param {{items: object[], stats?: object}} buildings
 * @param {{lines: {name: string, cs1Type: string, stops: {x: number, z: number, cs1Building?: number}[]}[]}} transit
 * @param {object} [catalogue] – for the CS2 lot size (optional)
 */
export function addDefaultDepots(buildings, transit, catalogue) {
  const added = [];
  const byType = new Map();
  for (const l of transit?.lines ?? []) {
    if (!DEPOT_FOR_LINE[l.cs1Type] || !(l.stops?.length)) continue;
    if (!byType.has(l.cs1Type)) byType.set(l.cs1Type, []);
    byType.get(l.cs1Type).push(l);
  }
  let nextId = -1;
  for (const [type, lines] of byType) {
    const d = DEPOT_FOR_LINE[type];
    if (buildings.items.some((it) => d.match.test(it.prefab ?? ''))) continue;
    const first = lines[0];
    const stop = first.stops[0];
    const names = lines.map((l) => l.name);
    const cat = catalogue?.prefabs?.find((p) => p.name === d.prefab);
    const item = {
      id: nextId--, cs1Info: null, type: 'BuildingPrefab', prefab: d.prefab, score: null, family: 'depot',
      x: round(stop.x), z: round(stop.z), cs1Lot: null, lot: cat?.lot ?? null, // lot in cells, like matched items
      accessSegment: 0, front: null, facing: [1, 0], alternatives: [], fallback: 'nearby',
      default: true,
      reason: `no CS1 ${type.toLowerCase()} depot; CS2 needs one for ${names.join(', ')}`,
    };
    buildings.items.push(item);
    added.push({ type, prefab: d.prefab, lines: names, at: [item.x, item.z] });
  }
  if (buildings.stats) buildings.stats.defaultDepots = added.length;
  if (added.length) buildings.defaultDepots = added;
  return added;
}
