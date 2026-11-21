// Road-conformed terrain (docs/DECISIONS.md #110–113, schema/README.md §2 "Road-conformed terrain"): burns the CS1
// vertical profile of every `conform` ground road into the CS2 heightmap, with smoothstep earth slopes back to the
// natural terrain. The contract is written so that terrain/lib (worker-4) gives byte-identical output: keep the
// arithmetic, order and tie rules exactly as in the schema text when changing anything here.

import type { PlanNode, PlanPrefab, PlanSegment } from '../types.ts';
import { sampleCs1 } from './heightmap.ts';
import type { Cs1TerrainMeta } from './heightmap.ts';

export const CONFORM = Object.freeze({ blendFactor: 2, minBlend: 3.5, maxBlend: 40, maxDifference: 30 });

export interface ConformWater {
  /** CS1 sea level (absolute metres); null without water data */
  seaLevel: number | null;
  /** CS1 water depth grid in metres (float32, like the terrain) on the CS1 lattice, or null */
  depthMeta?: Cs1TerrainMeta | null;
  depth?: Float32Array | null;
  /** plan transform: CS1 = plan − (dx, dz) */
  dx: number; dz: number;
}

export interface ConformInput {
  segments: PlanSegment[];
  nodes: PlanNode[];
  prefabs: Record<string, PlanPrefab>;
  water?: ConformWater | null;
}

export interface ConformStats {
  segments: number; skipped: number; nodes: number;
  blendFactor: number; minBlend: number; maxBlend: number; maxDifference: number;
  pixelsChanged: number; maxRaise: number; maxCut: number; belowSeaLevelPixels: number;
  before: { min: number; max: number };
  /** ids of skipped segments (first 50), for the report; not in terrain.json */
  skippedIds?: number[];
}

type V3 = [number, number, number];

function bez(a: number[], b: number[], c: number[], d: number[], t: number): V3 {
  const u = 1 - t;
  const b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
  return [b0 * a[0] + b1 * b[0] + b2 * c[0] + b3 * d[0], b0 * a[1] + b1 * b[1] + b2 * c[1] + b3 * d[1], b0 * a[2] + b1 * b[2] + b2 * c[2] + b3 * d[2]];
}

/** Sample points P_0…P_n of a plan bezier (16-piece chord length, n = max(1, ceil(L))). */
export function samplePoints(bz: number[][]): V3[] {
  const [a, b, c, d] = bz;
  let L = 0;
  let prev = bez(a, b, c, d, 0);
  for (let j = 1; j <= 16; j++) {
    const p = bez(a, b, c, d, j / 16);
    const ex = p[0] - prev[0], ey = p[1] - prev[1], ez = p[2] - prev[2];
    L += Math.sqrt(ex * ex + ey * ey + ez * ez);
    prev = p;
  }
  const n = Math.max(1, Math.ceil(L));
  const out: V3[] = [];
  for (let k = 0; k <= n; k++) out.push(bez(a, b, c, d, k / n));
  return out;
}

interface Acc { core: boolean; coreH: number; coreAbsD: number; sumW4: number; sumW4H: number; maxW: number }

/**
 * Burns the conform roads into `grid` (float32 metres, N², row 0 = minimum z) in place. Returns the stats, or null
 * when nothing burns.
 */
export function conformTerrain(grid: Float32Array, N: number, size: number, input: ConformInput, out?: { cores?: Set<number> }): ConformStats | null {
  const cell = size / N, half = size / 2;
  const H0 = grid; // read before writing: results are written only at the end
  const pix = (v: number) => { let i = Math.floor((v + half) / cell); if (i < 0) i = 0; else if (i > N - 1) i = N - 1; return i; };
  const water = input.water ?? null;
  const sea = water?.seaLevel ?? null;
  const depthAt = (x: number, z: number) => (water?.depth && water.depthMeta ? sampleCs1(water.depthMeta, water.depth, x - water.dx, z - water.dz) : 0);

  let before = { min: Infinity, max: -Infinity };
  for (let i = 0; i < grid.length; i++) { const h = grid[i]; if (h < before.min) before.min = h; if (h > before.max) before.max = h; }

  const acc = new Map<number, Acc>();
  const wetInfo = new Map<number, { wet: boolean; surface: number }>();
  const wetOf = (idx: number, x: number, z: number) => {
    let w = wetInfo.get(idx);
    if (!w) {
      const h0 = H0[idx], dep = depthAt(x, z);
      const wet = dep > 0 || (sea != null && h0 < sea);
      w = { wet, surface: Math.max(sea ?? -Infinity, h0 + dep) };
      wetInfo.set(idx, w);
    }
    return w;
  };
  let belowSea = 0;
  const contribute = (idx: number, x: number, z: number, d: number, w: number, hRoad: number) => {
    const h0 = H0[idx];
    const delta = hRoad - h0, ad = Math.abs(delta);
    let b = Math.min(CONFORM.maxBlend, Math.max(CONFORM.minBlend, CONFORM.blendFactor * ad));
    if (d > w) {
      if (wetOf(idx, x, z).wet) b = CONFORM.minBlend;
      if (d > w + b) return;
    }
    let a = acc.get(idx);
    if (!a) { a = { core: false, coreH: 0, coreAbsD: Infinity, sumW4: 0, sumW4H: 0, maxW: 0 }; acc.set(idx, a); }
    if (d <= w) {
      if (!a.core || ad < a.coreAbsD) { a.core = true; a.coreH = hRoad; a.coreAbsD = ad; }
      return;
    }
    const t = (d - w) / b;
    const s = t * t * (3 - 2 * t);
    const wt = 1 - s;
    const w4 = (wt * wt) * (wt * wt);
    a.sumW4 += w4;
    a.sumW4H += w4 * hRoad;
    if (wt > a.maxW) a.maxW = wt;
  };
  const window = (minX: number, maxX: number, minZ: number, maxZ: number, R: number) => ({
    c0: Math.max(0, Math.floor((minX - R + half) / cell)), c1: Math.min(N - 1, Math.floor((maxX + R + half) / cell)),
    r0: Math.max(0, Math.floor((minZ - R + half) / cell)), r1: Math.min(N - 1, Math.floor((maxZ + R + half) / cell)),
  });

  const segs = input.segments
    // only ground segments (#124): a burned slope (tunnel approach) would cut the ground at the portal down to the
    // road, and CS2 then builds the first tunnel segment as an open cut (it needs ≥ 8 m of ground above a road tunnel)
    .filter(s => s.kind === 'ground' && input.prefabs[s.prefab]?.heightMode === 'conform')
    .sort((a, b) => a.id - b.id);
  const burnedAtNode = new Map<number, number>(); // node → largest halfWidth of burned segments there
  let burned = 0, skipped = 0;
  const skippedIds: number[] = [];
  for (const s of segs) {
    const w = s.halfWidth ?? 0;
    const P = samplePoints(s.bezier as unknown as number[][]);
    if (P.some(p => Math.abs(p[1] - H0[pix(p[2]) * N + pix(p[0])]) > CONFORM.maxDifference)) { skipped++; if (skippedIds.length < 50) skippedIds.push(s.id); continue; }
    burned++;
    for (const nid of [s.start, s.end]) burnedAtNode.set(nid, Math.max(burnedAtNode.get(nid) ?? 0, w));
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const p of P) { if (p[0] < minX) minX = p[0]; if (p[0] > maxX) maxX = p[0]; if (p[2] < minZ) minZ = p[2]; if (p[2] > maxZ) maxZ = p[2]; }
    const win = window(minX, maxX, minZ, maxZ, w + CONFORM.maxBlend + cell);
    for (let row = win.r0; row <= win.r1; row++) {
      const pz = -half + (row + 0.5) * cell;
      for (let col = win.c0; col <= win.c1; col++) {
        const px = -half + (col + 0.5) * cell;
        let best = Infinity, bestH = 0;
        for (let k = 0; k < P.length - 1; k++) {
          const p0 = P[k], p1 = P[k + 1];
          const ex = p1[0] - p0[0], ez = p1[2] - p0[2];
          const len2 = ex * ex + ez * ez;
          let u = len2 > 0 ? ((px - p0[0]) * ex + (pz - p0[2]) * ez) / len2 : 0;
          if (u < 0) u = 0; else if (u > 1) u = 1;
          const qx = p0[0] + u * ex, qz = p0[2] + u * ez;
          const ddx = px - qx, ddz = pz - qz;
          const d = Math.sqrt(ddx * ddx + ddz * ddz);
          if (d < best) { best = d; bestH = p0[1] + u * (p1[1] - p0[1]); }
        }
        if (best > w + CONFORM.maxBlend) continue;
        contribute(row * N + col, px, pz, best, w, bestH);
      }
    }
  }
  const nodeById = new Map(input.nodes.map(n => [n.id, n]));
  // no junction disc where a tunnel or slope segment meets the node (#124): keep the ground around portals natural
  const tunnelNodes = new Set(input.segments.filter(s => s.kind === 'tunnel' || s.kind === 'slope').flatMap(s => [s.start, s.end]));
  for (const nid of tunnelNodes) burnedAtNode.delete(nid);
  let discs = 0;
  for (const nid of [...burnedAtNode.keys()].sort((a, b) => a - b)) {
    const n = nodeById.get(nid);
    if (!n) continue;
    discs++;
    const r = burnedAtNode.get(nid)!;
    const win = window(n.x, n.x, n.z, n.z, r + CONFORM.maxBlend + cell);
    for (let row = win.r0; row <= win.r1; row++) {
      const pz = -half + (row + 0.5) * cell;
      for (let col = win.c0; col <= win.c1; col++) {
        const px = -half + (col + 0.5) * cell;
        const ddx = px - n.x, ddz = pz - n.z;
        const d = Math.sqrt(ddx * ddx + ddz * ddz);
        if (d > r + CONFORM.maxBlend) continue;
        contribute(row * N + col, px, pz, d, r, n.y);
      }
    }
  }
  if (!burned) return null;

  // write
  let changed = 0, maxRaise = 0, maxCut = 0;
  const res = new Map<number, number>();
  for (const [idx, a] of acc) {
    const h0 = H0[idx];
    let h: number;
    if (a.core) { h = a.coreH; if (sea != null && h < sea) belowSea++; out?.cores?.add(idx); }
    else if (a.maxW > 0 && a.sumW4 > 0) {
      const target = a.sumW4H / a.sumW4;
      h = h0 + a.maxW * (target - h0);
      const col = idx % N, row = Math.floor(idx / N);
      const wi = wetOf(idx, -half + (col + 0.5) * cell, -half + (row + 0.5) * cell);
      if (wi.wet && h > wi.surface) h = wi.surface;
    } else continue;
    res.set(idx, h);
  }
  for (const [idx, h] of res) {
    const h0 = H0[idx];
    grid[idx] = h;
    const diff = grid[idx] - h0;
    if (diff !== 0) { changed++; if (diff > maxRaise) maxRaise = diff; if (-diff > maxCut) maxCut = -diff; }
  }
  const r3 = (v: number) => Math.round(v * 1000) / 1000;
  return {
    segments: burned, skipped, nodes: discs, ...CONFORM,
    pixelsChanged: changed, maxRaise: r3(maxRaise), maxCut: r3(maxCut), belowSeaLevelPixels: belowSea,
    before: { min: r3(before.min), max: r3(before.max) },
    skippedIds,
  };
}

/** Walls burn (docs/DECISIONS.md #129, schema §2 "Walls burn"): constants. */
export const WALLS = Object.freeze({ below: 7, above: 11, footprint: 2, blendFactor: 2, minBlend: 3.5, maxBlend: 40, maxDifference: 30 });

export interface WallsStats { segments: number; skipped: number; pixelsChanged: number; maxRaise: number; maxCut: number; skippedIds?: number[] }

/**
 * Walls burn (#129): a `walls` ground road keeps its CS1 height, and CS2 can only hold it with retaining walls up to
 * 7.5 m above and 11.5 m below the terrain. So the ground beside it is brought to at most WALLS.below (7 m) below
 * the road and at most WALLS.above (11 m) above it. Runs after conformTerrain on the same grid (`n` = the
 * conform-burned height); `natural` is the grid before any burn (for the wet rule); `cores` = conformTerrain's core
 * pixels, which the walls burn never changes.
 */
export function wallsTerrain(grid: Float32Array, N: number, size: number, input: ConformInput, natural: Float32Array, cores: Set<number>): WallsStats | null {
  const cell = size / N, half = size / 2;
  const pix = (v: number) => { let i = Math.floor((v + half) / cell); if (i < 0) i = 0; else if (i > N - 1) i = N - 1; return i; };
  const water = input.water ?? null;
  const sea = water?.seaLevel ?? null;
  const depthAt = (x: number, z: number) => (water?.depth && water.depthMeta ? sampleCs1(water.depthMeta, water.depth, x - water.dx, z - water.dz) : 0);
  const wetInfo = new Map<number, { wet: boolean; surface: number }>();
  const wetOf = (idx: number, x: number, z: number) => {
    let w = wetInfo.get(idx);
    if (!w) {
      const h0 = natural[idx], dep = depthAt(x, z);
      w = { wet: dep > 0 || (sea != null && h0 < sea), surface: Math.max(sea ?? -Infinity, h0 + dep) };
      wetInfo.set(idx, w);
    }
    return w;
  };
  const target = (n: number, h: number) => { const lo = h - WALLS.below, hi = h + WALLS.above; return n < lo ? lo : n > hi ? hi : n; };
  const acc = new Map<number, Acc>();
  const segs = input.segments.filter(s => s.kind === 'ground' && input.prefabs[s.prefab]?.heightMode === 'walls').sort((a, b) => a.id - b.id);
  let burned = 0, skipped = 0;
  const skippedIds: number[] = [];
  for (const s of segs) {
    const wc = (s.halfWidth ?? 0) + WALLS.footprint;
    const P = samplePoints(s.bezier as unknown as number[][]);
    if (P.some(p => { const n = grid[pix(p[2]) * N + pix(p[0])]; return Math.abs(target(n, p[1]) - n) > WALLS.maxDifference; })) { skipped++; if (skippedIds.length < 50) skippedIds.push(s.id); continue; }
    burned++;
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const p of P) { if (p[0] < minX) minX = p[0]; if (p[0] > maxX) maxX = p[0]; if (p[2] < minZ) minZ = p[2]; if (p[2] > maxZ) maxZ = p[2]; }
    const R = wc + WALLS.maxBlend + cell;
    const c0 = Math.max(0, Math.floor((minX - R + half) / cell)), c1 = Math.min(N - 1, Math.floor((maxX + R + half) / cell));
    const r0 = Math.max(0, Math.floor((minZ - R + half) / cell)), r1 = Math.min(N - 1, Math.floor((maxZ + R + half) / cell));
    for (let row = r0; row <= r1; row++) {
      const pz = -half + (row + 0.5) * cell;
      for (let col = c0; col <= c1; col++) {
        const idx = row * N + col;
        if (cores.has(idx)) continue;
        const px = -half + (col + 0.5) * cell;
        let best = Infinity, bestH = 0;
        for (let k = 0; k < P.length - 1; k++) {
          const p0 = P[k], p1 = P[k + 1];
          const ex = p1[0] - p0[0], ez = p1[2] - p0[2];
          const len2 = ex * ex + ez * ez;
          let u = len2 > 0 ? ((px - p0[0]) * ex + (pz - p0[2]) * ez) / len2 : 0;
          if (u < 0) u = 0; else if (u > 1) u = 1;
          const qx = p0[0] + u * ex, qz = p0[2] + u * ez;
          const ddx = px - qx, ddz = pz - qz;
          const d = Math.sqrt(ddx * ddx + ddz * ddz);
          if (d < best) { best = d; bestH = p0[1] + u * (p1[1] - p0[1]); }
        }
        if (best > wc + WALLS.maxBlend) continue;
        const n = grid[idx], t = target(n, bestH);
        if (t === n) continue;
        const ad = Math.abs(t - n);
        let b = Math.min(WALLS.maxBlend, Math.max(WALLS.minBlend, WALLS.blendFactor * ad));
        if (best > wc) {
          if (wetOf(idx, px, pz).wet) b = WALLS.minBlend;
          if (best > wc + b) continue;
        }
        let a = acc.get(idx);
        if (!a) { a = { core: false, coreH: 0, coreAbsD: Infinity, sumW4: 0, sumW4H: 0, maxW: 0 }; acc.set(idx, a); }
        if (best <= wc) {
          if (!a.core || ad < a.coreAbsD) { a.core = true; a.coreH = t; a.coreAbsD = ad; }
          continue;
        }
        const tt = (best - wc) / b;
        const sm = tt * tt * (3 - 2 * tt);
        const wt = 1 - sm;
        const w4 = (wt * wt) * (wt * wt);
        a.sumW4 += w4;
        a.sumW4H += w4 * t;
        if (wt > a.maxW) a.maxW = wt;
      }
    }
  }
  if (!burned) return skipped ? { segments: 0, skipped, pixelsChanged: 0, maxRaise: 0, maxCut: 0, skippedIds } : null;
  const res = new Map<number, number>();
  for (const [idx, a] of acc) {
    const n = grid[idx];
    let h: number;
    if (a.core) h = a.coreH;
    else if (a.maxW > 0 && a.sumW4 > 0) h = n + a.maxW * (a.sumW4H / a.sumW4 - n);
    else continue;
    // a wet pixel is never raised above the CS1 water surface
    const col = idx % N, row = Math.floor(idx / N);
    if (h > n) { const wi = wetOf(idx, -half + (col + 0.5) * cell, -half + (row + 0.5) * cell); if (wi.wet && h > wi.surface) h = Math.max(n, wi.surface); }
    res.set(idx, h);
  }
  let changed = 0, maxRaise = 0, maxCut = 0;
  for (const [idx, h] of res) {
    const n = grid[idx];
    grid[idx] = h;
    const diff = grid[idx] - n;
    if (diff !== 0) { changed++; if (diff > maxRaise) maxRaise = diff; if (-diff > maxCut) maxCut = -diff; }
  }
  const r3 = (v: number) => Math.round(v * 1000) / 1000;
  return { segments: burned, skipped, pixelsChanged: changed, maxRaise: r3(maxRaise), maxCut: r3(maxCut), skippedIds };
}

/** Keeps each water source's absolute level on the conformed terrain (schema §2): height += H0 − H at its pixel. */
export function adjustWaterSources<T extends { x: number; z: number; height: number }>(sources: T[], natural: Float32Array, burned: Float32Array, N: number, size: number): number {
  const cell = size / N, half = size / 2;
  const pix = (v: number) => { let i = Math.floor((v + half) / cell); if (i < 0) i = 0; else if (i > N - 1) i = N - 1; return i; };
  let n = 0;
  for (const s of sources) {
    const idx = pix(s.z) * N + pix(s.x);
    const diff = natural[idx] - burned[idx];
    if (Math.abs(diff) <= 0.001) continue;
    s.height = Math.round(Math.max(1, s.height + diff) * 100) / 100;
    n++;
  }
  return n;
}
