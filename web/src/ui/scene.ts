// Pre-built drawing data for the map: Path2D buckets per colour/width, IMT fill polygons, a terrain
// hillshade image and a coarse spatial grid for hover hit-tests. World units: CS1 metres, screen y = −z.

import { bezierPoint } from '../core/imt.ts';
import { categorize } from '../core/mapping.ts';
import type { NetCategory } from '../core/mapping.ts';
import type { Cs1City, Cs1Segment, NetInfo, Terrain } from '../core/types.ts';

export const CATEGORY_STYLE: Record<NetCategory, { color: string; label: string; minPx: number; dash?: number[] }> = {
  road: { color: '#e9e4d8', label: 'Road', minPx: 1 },
  highway: { color: '#f2a03d', label: 'Highway', minPx: 1.5 },
  train: { color: '#c77dff', label: 'Train', minPx: 1.2 },
  metro: { color: '#4cc9f0', label: 'Metro', minPx: 1.2, dash: [6, 4] },
  tram: { color: '#ff70a6', label: 'Tram', minPx: 1.2 },
  monorail: { color: '#ff595e', label: 'Monorail', minPx: 1 },
  pedestrian: { color: '#80ed99', label: 'Path', minPx: 0.8 },
  bike: { color: '#57cc99', label: 'Bike path', minPx: 0.8 },
  utility: { color: '#5c677d', label: 'Utility (skipped)', minPx: 0.5 },
  'transport-line': { color: '#3d4a5c', label: 'Transport line (skipped)', minPx: 0.5 },
  building: { color: '#7d6b91', label: 'Building net (skipped)', minPx: 0.6 },
  other: { color: '#888888', label: 'Other', minPx: 0.6 },
};

/** Categories drawn under the roads, thin and dim; hidden unless "show skipped" is on. */
export const SKIPPED_CATS = new Set<NetCategory>(['utility', 'transport-line', 'building', 'monorail', 'other']);

export interface Bucket { cat: NetCategory; width: number; path: Path2D; tunnel: boolean; elevated: boolean }

export interface SegInfo { seg: Cs1Segment; base: string; cat: NetCategory; pts: [number, number][]; halfWidth: number }

export interface Scene {
  buckets: Bucket[];
  fills: { path: Path2D; color: string; stroke: string; node: boolean }[];
  segs: SegInfo[];
  grid: Map<string, number[]>;
  cell: number;
  extent: { minX: number; maxX: number; minZ: number; maxZ: number };
  terrain: HTMLCanvasElement | null;
  terrainSize: number;
}

const SAMPLES = 8;

export function buildScene(city: Cs1City): Scene {
  const infos = new Map(city.netInfos.map(i => [i.name, i]));
  const nodes = new Map(city.nodes.map(n => [n.id, n]));
  const baseOf = (name: string) => { const p = infos.get(name)?.parent; return p && infos.has(p) ? p : name; };
  const catCache = new Map<string, NetCategory>();
  const catOf = (base: string, info: NetInfo | undefined) => {
    let c = catCache.get(base);
    if (!c) { c = info ? categorize(info) : 'other'; catCache.set(base, c); }
    return c;
  };
  const buckets = new Map<string, Bucket>();
  const segs: SegInfo[] = [];
  const grid = new Map<string, number[]>();
  const cell = 200;
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (const s of city.segments) {
    if (!nodes.has(s.startNode) || !nodes.has(s.endNode)) continue;
    const base = baseOf(s.info);
    const info = infos.get(base);
    let cat = catOf(base, info);
    if (/Untouchable/.test(s.flags ?? '') && !SKIPPED_CATS.has(cat)) cat = 'building';
    const hw = Number(infos.get(s.info)?.fields.m_halfWidth ?? 4) || 4;
    const kind = s.elevationKind ?? 'ground';
    const tunnel = kind === 'tunnel' || kind === 'underground';
    const elevated = kind === 'elevated' || kind === 'bridge';
    const key = `${cat}|${Math.round(hw)}|${tunnel}|${elevated}`;
    let b = buckets.get(key);
    if (!b) { b = { cat, width: Math.round(hw) * 2, path: new Path2D(), tunnel, elevated }; buckets.set(key, b); }
    const p = s.bezier;
    b.path.moveTo(p[0][0], -p[0][2]);
    b.path.bezierCurveTo(p[1][0], -p[1][2], p[2][0], -p[2][2], p[3][0], -p[3][2]);
    const pts: [number, number][] = [];
    for (let i = 0; i <= SAMPLES; i++) pts.push(bezierPoint(p, i / SAMPLES));
    const idx = segs.length;
    segs.push({ seg: s, base, cat, pts, halfWidth: hw });
    const seen = new Set<string>();
    for (const [x, z] of pts) {
      if (!SKIPPED_CATS.has(cat)) { minX = Math.min(minX, x); maxX = Math.max(maxX, x); minZ = Math.min(minZ, z); maxZ = Math.max(maxZ, z); }
      const k = `${Math.floor(x / cell)},${Math.floor(z / cell)}`;
      if (seen.has(k)) continue;
      seen.add(k);
      let g = grid.get(k);
      if (!g) { g = []; grid.set(k, g); }
      g.push(idx);
    }
  }
  // Fit to the 3rd–97th percentile of placeable network points: outside connections stretch to the map edge.
  const xs: number[] = [], zs: number[] = [];
  for (const s of segs) if (!SKIPPED_CATS.has(s.cat)) { xs.push(s.pts[0][0]); zs.push(s.pts[0][1]); }
  if (xs.length > 20) {
    xs.sort((a, b) => a - b); zs.sort((a, b) => a - b);
    const q = (a: number[], f: number) => a[Math.floor(f * (a.length - 1))];
    minX = q(xs, 0.03); maxX = q(xs, 0.97); minZ = q(zs, 0.03); maxZ = q(zs, 0.97);
  }
  if (!Number.isFinite(minX)) { minX = -1000; maxX = 1000; minZ = -1000; maxZ = 1000; }
  const fills = city.imtFillers.filter(f => f.polygon?.length > 2).map(f => {
    const path = new Path2D();
    f.polygon.forEach((p, i) => (i ? path.lineTo(p[0], -p[2]) : path.moveTo(p[0], -p[2])));
    path.closePath();
    const [r, g, b, a] = (f.color ?? [255, 0, 255, 128]) as number[];
    return { path, color: `rgba(${r},${g},${b},${Math.min(1, Math.max(0.55, (a ?? 255) / 255))})`, stroke: `rgb(${Math.min(255, r + 60)},${Math.min(255, g + 60)},${Math.min(255, b + 60)})`, node: f.markingType === 'Node' };
  });
  // Draw order: skipped stuff first, then paths, tracks, roads, highways.
  const order: NetCategory[] = ['transport-line', 'utility', 'building', 'other', 'monorail', 'pedestrian', 'bike', 'metro', 'train', 'tram', 'road', 'highway'];
  const sorted = [...buckets.values()].sort((a, b) => (a.tunnel === b.tunnel ? 0 : a.tunnel ? -1 : 1) || order.indexOf(a.cat) - order.indexOf(b.cat) || (a.elevated === b.elevated ? 0 : a.elevated ? 1 : -1) || a.width - b.width);
  return { buckets: sorted, fills, segs, grid, cell, extent: { minX, maxX, minZ, maxZ }, terrain: city.terrain ? hillshade(city.terrain) : null, terrainSize: city.terrain?.size ?? 0 };
}

/** Hillshade + hypsometric tint of the raw heightmap; image row 0 = north (screen top). */
export function hillshade(t: Terrain): HTMLCanvasElement {
  const n = t.resolution;
  const canvas = document.createElement('canvas');
  canvas.width = n; canvas.height = n;
  const ctx = canvas.getContext('2d')!;
  const img = ctx.createImageData(n, n);
  const hs = t.heightScale, spacing = t.size / (n - 1);
  let lo = Infinity, hi = -Infinity;
  for (let i = 0; i < t.heights.length; i += 7) { const v = t.heights[i] * hs; if (v < lo) lo = v; if (v > hi) hi = v; }
  const range = Math.max(1, hi - lo);
  const H = (c: number, r: number) => t.heights[Math.min(n - 1, Math.max(0, r)) * n + Math.min(n - 1, Math.max(0, c))] * hs;
  // light from the north-west
  const lx = -0.6, lz = 0.6, ly = 0.55;
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      const dzdx = (H(c + 1, r) - H(c - 1, r)) / (2 * spacing);
      const dzdz = (H(c, r + 1) - H(c, r - 1)) / (2 * spacing); // row increases northwards
      const nx = -dzdx * 3, nz = -dzdz * 3, ny = 1;
      const len = Math.hypot(nx, ny, nz);
      const shade = Math.max(0, (nx * lx + ny * ly + nz * lz) / len);
      const h = (H(c, r) - lo) / range;
      const base = [28 + 40 * h, 36 + 34 * h, 32 + 22 * h];
      const k = 0.55 + 0.75 * shade;
      const o = ((n - 1 - r) * n + c) * 4;
      img.data[o] = Math.min(255, base[0] * k);
      img.data[o + 1] = Math.min(255, base[1] * k);
      img.data[o + 2] = Math.min(255, base[2] * k);
      img.data[o + 3] = 255;
    }
  }
  ctx.putImageData(img, 0, 0);
  return canvas;
}

/** Nearest segment to a world point within `maxDist` metres. */
export function hitSegment(scene: Scene, x: number, z: number, maxDist: number, showSkipped: boolean): SegInfo | null {
  const c = scene.cell;
  let best: SegInfo | null = null, bestD = maxDist;
  const r = Math.ceil(maxDist / c);
  const ci = Math.floor(x / c), cj = Math.floor(z / c);
  for (let i = ci - r; i <= ci + r; i++) for (let j = cj - r; j <= cj + r; j++) {
    for (const idx of scene.grid.get(`${i},${j}`) ?? []) {
      const s = scene.segs[idx];
      if (!showSkipped && SKIPPED_CATS.has(s.cat)) continue;
      for (let k = 1; k < s.pts.length; k++) {
        const d = distToSeg(x, z, s.pts[k - 1], s.pts[k]) - s.halfWidth * 0.5;
        if (d < bestD) { bestD = d; best = s; }
      }
    }
  }
  return best;
}

function distToSeg(x: number, z: number, a: [number, number], b: [number, number]) {
  const dx = b[0] - a[0], dz = b[1] - a[1];
  const l2 = dx * dx + dz * dz || 1;
  const t = Math.max(0, Math.min(1, ((x - a[0]) * dx + (z - a[1]) * dz) / l2));
  return Math.hypot(x - (a[0] + t * dx), z - (a[1] + t * dz));
}
