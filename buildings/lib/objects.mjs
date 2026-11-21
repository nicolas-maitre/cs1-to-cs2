// Plan sections for trees / props, districts and IMT surfaces (cs1conv-objects.json, see buildings/README.md).
// Pure ES module: no I/O, deterministic. Coordinates are plan coordinates (CS1 + transform), like the road plan.

import { matchVegetation, VEGETATION_VERSION } from '../match/vegetation.mjs';

const round = (v, n = 2) => Math.round(v * 10 ** n) / 10 ** n + 0;

/** Deterministic 0..1 hash of an integer (for tree age and rotation). */
function hash01(i, salt) {
  let h = (i * 374761393 + salt * 668265263) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

function inCrop(x, z, crop) {
  return Math.abs(x - crop.centerX) <= crop.size / 2 && Math.abs(z - crop.centerZ) <= crop.size / 2;
}

/**
 * objects section: trees and props of the crop, mapped with matchVegetation.
 * Rows are compact: [prefabIndex, x, z, angleDegrees, age] (age = CS2 ObjectDefinition.m_Age, 0.3–0.55 = adult).
 * @param {{trees?: object, props?: object, assets: object}} cs1 – trees.json, props.json, assets.json
 */
export function buildObjectsSection(cs1, catalogue, roadPlan, opts = {}) {
  const match = matchVegetation(cs1.assets, catalogue, opts);
  const { dx, dz } = roadPlan.transform;
  const prefabs = [];
  const index = new Map();
  const items = [];
  const counts = {};
  const skipped = { outsideCrop: 0, noEquivalent: 0 };
  const add = (cs1Name, x, z, angle, age) => {
    const m = match.byName[cs1Name];
    const c = (counts[cs1Name] ??= { cs2: m?.cs2 ?? null, decision: m?.decision ?? 'noEquivalent', reason: m?.reason ?? 'not in assets.json', count: 0 });
    c.count++;
    if (!m || m.decision !== 'place') { skipped.noEquivalent++; return; }
    if (!index.has(m.cs2)) { index.set(m.cs2, prefabs.length); prefabs.push(m.cs2); }
    items.push([index.get(m.cs2), round(x + dx), round(z + dz), round(angle, 1), round(age, 3)]);
  };
  const trees = cs1.trees?.trees ?? [];
  trees.forEach((r, i) => {
    if (!inCrop(r[1], r[3], roadPlan.crop)) { skipped.outsideCrop++; return; }
    add(cs1.trees.infos[r[0]], r[1], r[3], hash01(i, 1) * 360, 0.3 + 0.25 * hash01(i, 2));
  });
  const props = cs1.props?.props ?? [];
  props.forEach((r, i) => {
    if (!inCrop(r[1], r[3], roadPlan.crop)) { skipped.outsideCrop++; return; }
    add(cs1.props.infos[r[0]], r[1], r[3], r[4] ?? 0, 0.45);
  });
  return {
    generator: { vegetation: VEGETATION_VERSION },
    prefabs, items,
    stats: { items: items.length, skipped, byType: counts },
  };
}

// ------------------------------------------------------------------ districts

/**
 * Outer boundary polygons of the 4-connected components of each id in a byte raster (row 0 = minimum z).
 * Only cells whose centre lies inside `crop` count. Holes are dropped (reported), collinear points removed.
 * @returns {Array<{id: number, cells: number, polygon: number[][], holes: number}>} polygon in raster corner units
 */
export function rasterPolygons(raster, res, keep = () => true) {
  const seen = new Uint8Array(res * res);
  const out = [];
  for (let start = 0; start < raster.length; start++) {
    const id = raster[start];
    if (!id || seen[start] || !keep(start % res, (start / res) | 0)) continue;
    // Flood fill (4-connected).
    const cells = [];
    const stack = [start];
    seen[start] = 1;
    while (stack.length) {
      const c = stack.pop();
      cells.push(c);
      const x = c % res, z = (c / res) | 0;
      for (const [nx, nz] of [[x + 1, z], [x - 1, z], [x, z + 1], [x, z - 1]]) {
        if (nx < 0 || nz < 0 || nx >= res || nz >= res) continue;
        const n = nz * res + nx;
        if (seen[n] || raster[n] !== id || !keep(nx, nz)) continue;
        seen[n] = 1;
        stack.push(n);
      }
    }
    const inside = new Set(cells);
    const has = (x, z) => x >= 0 && z >= 0 && x < res && z < res && inside.has(z * res + x);
    // Directed boundary edges, counter-clockwise around the cells (x right, z up): start vertex → end vertex.
    const next = new Map();
    const key = (x, z) => x * 100003 + z;
    for (const c of cells) {
      const x = c % res, z = (c / res) | 0;
      if (!has(x, z - 1)) next.set(key(x, z), [x + 1, z]);          // bottom: left → right
      if (!has(x + 1, z)) next.set(key(x + 1, z), [x + 1, z + 1]);  // right: bottom → top
      if (!has(x, z + 1)) next.set(key(x + 1, z + 1), [x, z + 1]);  // top: right → left
      if (!has(x - 1, z)) next.set(key(x, z + 1), [x, z]);          // left: top → bottom
    }
    // Walk the loops; the largest |area| is the outer boundary.
    const used = new Set();
    const loops = [];
    for (const [k, first] of next) {
      if (used.has(k)) continue;
      const loop = [];
      let cur = k, guard = 0;
      while (!used.has(cur) && next.has(cur) && guard++ < 1e6) {
        used.add(cur);
        const [x, z] = next.get(cur);
        loop.push([x, z]);
        cur = key(x, z);
      }
      if (loop.length >= 4) loops.push(loop);
    }
    const area = (l) => l.reduce((s, p, i) => { const q = l[(i + 1) % l.length]; return s + p[0] * q[1] - q[0] * p[1]; }, 0) / 2;
    loops.sort((a, b) => Math.abs(area(b)) - Math.abs(area(a)));
    if (!loops.length) continue;
    out.push({ id, cells: cells.length, polygon: dropCollinear(loops[0]), holes: loops.length - 1 });
  }
  return out;
}

function dropCollinear(loop) {
  const r = [];
  for (let i = 0; i < loop.length; i++) {
    const a = loop[(i - 1 + loop.length) % loop.length], b = loop[i], c = loop[(i + 1) % loop.length];
    if ((b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]) !== 0) r.push(b);
  }
  return r;
}

/**
 * CS1 → CS2 district policy names (CS2 PolicyTogglePrefab / PolicySliderPrefab names, from the game's prefab list).
 * Only policies with a real counterpart; the rest are counted as unmapped.
 */
export const DISTRICT_POLICY_MAP = {
  HeavyTrafficBan: 'Heavy Traffic Ban',
  CombustionEngineBan: 'Combustion Engine Ban',
  SpeedBumps: 'Speed Bumps',
  Recycling: 'Recycling',
  PowerSaving: 'Energy Consumption Awareness',
  EncourageBiking: 'Urban Cycling Initiative',
  BikeBan: 'Bicycle Traffic Restriction',
  ParkingFees: 'Roadside Parking Fee',
  PaidParking: 'Roadside Parking Fee',
};

/** IMT junction fill styles with a CS2 surface (SurfacePrefab names). Paint (FillerSolid, stripes, …) has none. */
export const DEFAULT_SURFACE_MAP = { FillerPavement: 'Pavement Surface 01', FillerGrass: 'Grass Surface 01', FillerGravel: 'Sand Surface 01' };

/**
 * districts section: one CS2 district area per 4-connected CS1 district component inside the crop (components under
 * `minCells` cells are noise from CS1's blending and are dropped). Park / industry / campus areas have no CS2 district
 * counterpart and are only counted.
 * @param {{districts: object, districtRaster: Uint8Array|number[]}} cs1 – districts.json + districts.u8
 */
export function buildDistrictsSection(cs1, roadPlan, opts = {}) {
  const prefab = opts.districtPrefab ?? 'District Area';
  const minCells = opts.minCells ?? 4;
  const policyMap = { ...DISTRICT_POLICY_MAP, ...(opts.policyMap ?? {}) };
  const g = cs1.districts.grid;
  const res = g.resolution, cell = g.cellSize, half = (res * cell) / 2;
  const { dx, dz } = roadPlan.transform;
  const keep = (x, z) => inCrop(-half + (x + 0.5) * cell, -half + (z + 0.5) * cell, roadPlan.crop);
  const byId = new Map(cs1.districts.districts.map((d) => [d.id, d]));
  const polys = rasterPolygons(cs1.districtRaster, res, keep);
  const items = [];
  const skipped = { small: 0, holesDropped: 0, unmappedPolicies: {} };
  for (const p of polys) {
    if (p.cells < minCells) { skipped.small++; continue; }
    skipped.holesDropped += p.holes;
    const d = byId.get(p.id);
    const cs1Policies = d?.policies ?? [];
    const policies = cs1Policies.map((x) => policyMap[x]).filter(Boolean);
    for (const x of cs1Policies) if (!policyMap[x]) skipped.unmappedPolicies[x] = (skipped.unmappedPolicies[x] ?? 0) + 1;
    items.push({
      cs1Id: p.id, name: d?.name ?? `District ${p.id}`, type: 'DistrictPrefab', prefab,
      polygon: p.polygon.map(([x, z]) => [round(-half + x * cell + dx), round(-half + z * cell + dz)]),
      cells: p.cells, policies, cs1Policies,
    });
  }
  const parks = (cs1.districts.parks ?? []).map((p) => ({ cs1Id: p.id, name: p.name, kind: p.kind, type: p.type }));
  return { items, parksNotConverted: parks, stats: { items: items.length, skipped } };
}

// ------------------------------------------------------------------ IMT node fills → surfaces

/**
 * surfaces section: IMT junction (node) fills as CS2 surface areas. Only fill styles with a CS2 surface are
 * converted (`surfaceMap`: styleType → CS2 surface prefab); paint fills have none and are reported.
 */
export function buildSurfacesSection(imt, roadPlan, opts = {}) {
  const surfaceMap = opts.surfaceMap ?? DEFAULT_SURFACE_MAP;
  const { dx, dz } = roadPlan.transform;
  const items = [];
  const notConverted = {};
  for (const f of imt?.fillers ?? []) {
    if (f.markingType !== 'Node') continue; // segment fills become lane overrides (road plan)
    const c = f.polygon.reduce((s, p) => [s[0] + p[0] / f.polygon.length, s[1] + p[2] / f.polygon.length], [0, 0]);
    if (!inCrop(c[0], c[1], roadPlan.crop)) continue;
    const prefab = surfaceMap[f.styleType];
    const key = `${f.styleType} rgba(${(f.color ?? []).join(',')})`;
    if (!prefab) { notConverted[key] = (notConverted[key] ?? 0) + 1; continue; }
    items.push({ cs1Node: f.markingId, styleType: f.styleType, type: 'SurfacePrefab', prefab, polygon: f.polygon.map((p) => [round(p[0] + dx), round(p[2] + dz)]) });
  }
  return { items, notConverted, stats: { items: items.length } };
}
