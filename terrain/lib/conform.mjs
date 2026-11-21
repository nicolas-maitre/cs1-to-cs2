// Road-conformed terrain (docs/DECISIONS.md #110–113; contract: schema/README.md §2 "Road-conformed terrain").
// Burns the CS1 vertical profile of `conform` ground roads into the CS2 playable-area grid: the ground follows the
// road instead of the road following the ground. The web core (web/src/core/terrain/conform.ts) implements the same
// text and must stay byte-identical (web/test/terrain.test.ts compares): float64 arithmetic in the schema's order,
// results stored back into the float32 grid, Math.sqrt (never Math.hypot), strict `<` on ties.
// Unlike the web port, pixels are processed in 256-pixel tiles so the accumulators stay small on a full city; the
// contributions to any one pixel still arrive in processing order (segments by id, then discs by node id).
import { sampleCs1 } from './heightmap.mjs';

export const CONFORM = Object.freeze({ blendFactor: 2, minBlend: 3.5, maxBlend: 40, maxDifference: 30 });
const TILE = 256;

function bez(a, b, c, d, t) {
  const u = 1 - t;
  const b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
  return [b0 * a[0] + b1 * b[0] + b2 * c[0] + b3 * d[0], b0 * a[1] + b1 * b[1] + b2 * c[1] + b3 * d[1], b0 * a[2] + b1 * b[2] + b2 * c[2] + b3 * d[2]];
}

/** §2 step 2: L = 3-D length of the 16-piece chord polyline, n = max(1, ceil(L)), P_k = B(k/n), k = 0…n. */
export function samplePoints(bz) {
  const [a, b, c, d] = bz;
  let L = 0, prev = bez(a, b, c, d, 0);
  for (let j = 1; j <= 16; j++) {
    const p = bez(a, b, c, d, j / 16);
    const ex = p[0] - prev[0], ey = p[1] - prev[1], ez = p[2] - prev[2];
    L += Math.sqrt(ex * ex + ey * ey + ez * ez);
    prev = p;
  }
  const n = Math.max(1, Math.ceil(L));
  const out = [];
  for (let k = 0; k <= n; k++) out.push(bez(a, b, c, d, k / n));
  return out;
}

/** §2 step 3: [d, h_road] of the nearest polyline point (x/z), strict `<` so the lowest k wins a tie. */
function nearest(P, px, pz) {
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
  return [best, bestH];
}

/**
 * Burns the plan's conform roads into `grid` (Float32Array metres, N², row 0 = minimum z) in place.
 * water: {seaLevel|null, depthMeta, depth: Float32Array|null, dx, dz} or null. Returns the stats (terrain.json
 * `conform`, plus skippedIds for the report) or null when no segment burns (the grid is then untouched).
 */
export function conformTerrain(grid, N, size, { segments = [], nodes = [], prefabs = {}, water = null } = {}) {
  const cell = size / N, half = size / 2;
  const H0 = grid; // read only until the write pass
  const pix = (v) => { let i = Math.floor((v + half) / cell); if (i < 0) i = 0; else if (i > N - 1) i = N - 1; return i; };
  const sea = water?.seaLevel ?? null;
  const depthAt = (x, z) => (water?.depth && water.depthMeta ? sampleCs1(water.depthMeta, water.depth, x - water.dx, z - water.dz) : 0);
  const wetAt = (h0, x, z) => { const dep = depthAt(x, z); return { wet: dep > 0 || (sea != null && h0 < sea), surface: Math.max(sea ?? -Infinity, h0 + dep) }; };

  let bmin = Infinity, bmax = -Infinity;
  for (let i = 0; i < grid.length; i++) { const h = grid[i]; if (h < bmin) bmin = h; if (h > bmax) bmax = h; }
  const win = (minX, maxX, minZ, maxZ, R) => ({
    c0: Math.max(0, Math.floor((minX - R + half) / cell)), c1: Math.min(N - 1, Math.floor((maxX + R + half) / cell)),
    r0: Math.max(0, Math.floor((minZ - R + half) / cell)), r1: Math.min(N - 1, Math.floor((maxZ + R + half) / cell)),
  });

  // contributions in processing order: burned segments by id, then discs by node id
  const segs = segments.filter(s => (s.kind === 'ground' || s.kind === 'slope') && prefabs[s.prefab]?.heightMode === 'conform').sort((a, b) => a.id - b.id);
  const items = [], burnedAtNode = new Map(), skippedIds = [];
  let burned = 0, skipped = 0;
  for (const s of segs) {
    const w = s.halfWidth ?? 0;
    const P = samplePoints(s.bezier);
    if (P.some(p => Math.abs(p[1] - H0[pix(p[2]) * N + pix(p[0])]) > CONFORM.maxDifference)) { skipped++; if (skippedIds.length < 50) skippedIds.push(s.id); continue; }
    burned++;
    for (const nid of [s.start, s.end]) burnedAtNode.set(nid, Math.max(burnedAtNode.get(nid) ?? 0, w));
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const p of P) { if (p[0] < minX) minX = p[0]; if (p[0] > maxX) maxX = p[0]; if (p[2] < minZ) minZ = p[2]; if (p[2] > maxZ) maxZ = p[2]; }
    items.push({ P, w, ...win(minX, maxX, minZ, maxZ, w + CONFORM.maxBlend + cell) });
  }
  if (!burned) return null;
  const nodeById = new Map(nodes.map(n => [n.id, n]));
  let discs = 0;
  for (const nid of [...burnedAtNode.keys()].sort((a, b) => a - b)) {
    const n = nodeById.get(nid);
    if (!n) continue;
    discs++;
    const r = burnedAtNode.get(nid);
    items.push({ disc: n, w: r, ...win(n.x, n.x, n.z, n.z, r + CONFORM.maxBlend + cell) });
  }

  const T2 = TILE * TILE;
  const core = new Uint8Array(T2), touched = new Uint8Array(T2);
  const coreH = new Float64Array(T2), coreAbsD = new Float64Array(T2), sumW4 = new Float64Array(T2), sumW4H = new Float64Array(T2), maxW = new Float64Array(T2);
  const writes = []; // [idx, h] per tile, applied after every tile has read H0
  let changed = 0, maxRaise = 0, maxCut = 0, belowSea = 0;
  for (let tr = 0; tr < N; tr += TILE) for (let tc = 0; tc < N; tc += TILE) {
    const tr1 = Math.min(N, tr + TILE) - 1, tc1 = Math.min(N, tc + TILE) - 1;
    const here = items.filter(it => it.c1 >= tc && it.c0 <= tc1 && it.r1 >= tr && it.r0 <= tr1);
    if (!here.length) continue;
    core.fill(0); touched.fill(0); coreAbsD.fill(Infinity); sumW4.fill(0); sumW4H.fill(0); maxW.fill(0);
    for (const it of here) {
      const rA = Math.max(tr, it.r0), rB = Math.min(tr1, it.r1), cA = Math.max(tc, it.c0), cB = Math.min(tc1, it.c1);
      const w = it.w;
      for (let row = rA; row <= rB; row++) {
        const pz = -half + (row + 0.5) * cell;
        for (let col = cA; col <= cB; col++) {
          const px = -half + (col + 0.5) * cell;
          let d, hRoad;
          if (it.disc) { const ddx = px - it.disc.x, ddz = pz - it.disc.z; d = Math.sqrt(ddx * ddx + ddz * ddz); hRoad = it.disc.y; }
          else [d, hRoad] = nearest(it.P, px, pz);
          if (d > w + CONFORM.maxBlend) continue;
          // contribute (§2 step 4, 5)
          const idx = row * N + col, h0 = H0[idx];
          const ad = Math.abs(hRoad - h0);
          let b = Math.min(CONFORM.maxBlend, Math.max(CONFORM.minBlend, CONFORM.blendFactor * ad));
          if (d > w) {
            if (wetAt(h0, px, pz).wet) b = CONFORM.minBlend;
            if (d > w + b) continue;
          }
          const li = (row - tr) * TILE + (col - tc);
          touched[li] = 1;
          if (d <= w) {
            if (!core[li] || ad < coreAbsD[li]) { core[li] = 1; coreH[li] = hRoad; coreAbsD[li] = ad; }
            continue;
          }
          const t = (d - w) / b;
          const s = t * t * (3 - 2 * t);
          const wt = 1 - s;
          const w4 = (wt * wt) * (wt * wt);
          sumW4[li] += w4;
          sumW4H[li] += w4 * hRoad;
          if (wt > maxW[li]) maxW[li] = wt;
        }
      }
    }
    for (let row = tr; row <= tr1; row++) for (let col = tc; col <= tc1; col++) {
      const li = (row - tr) * TILE + (col - tc);
      if (!touched[li]) continue;
      const idx = row * N + col, h0 = H0[idx];
      let h;
      if (core[li]) { h = coreH[li]; if (sea != null && h < sea) belowSea++; }
      else if (maxW[li] > 0 && sumW4[li] > 0) {
        const target = sumW4H[li] / sumW4[li];
        h = h0 + maxW[li] * (target - h0);
        const wi = wetAt(h0, -half + (col + 0.5) * cell, -half + (row + 0.5) * cell);
        if (wi.wet && h > wi.surface) h = wi.surface;
      } else continue;
      writes.push(idx, h);
    }
  }
  for (let k = 0; k < writes.length; k += 2) {
    const idx = writes[k], h0 = H0[idx];
    grid[idx] = writes[k + 1];
    const diff = grid[idx] - h0;
    if (diff !== 0) { changed++; if (diff > maxRaise) maxRaise = diff; if (-diff > maxCut) maxCut = -diff; }
  }
  const r3 = (v) => Math.round(v * 1000) / 1000;
  return {
    segments: burned, skipped, nodes: discs, ...CONFORM,
    pixelsChanged: changed, maxRaise: r3(maxRaise), maxCut: r3(maxCut), belowSeaLevelPixels: belowSea,
    before: { min: r3(bmin), max: r3(bmax) },
    skippedIds,
  };
}

/** Keeps each water source's absolute level on the conformed terrain (schema §2): height += H0 − H at its pixel,
 *  when that pixel changed by more than 1 mm. Mutates the sources; returns how many were adjusted. */
export function adjustWaterSources(sources, natural, burned, N, size) {
  const cell = size / N, half = size / 2;
  const pix = (v) => { let i = Math.floor((v + half) / cell); if (i < 0) i = 0; else if (i > N - 1) i = N - 1; return i; };
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

/** The plan's conform input for conformTerrain, or null when no prefab is `conform`. */
export function conformInput(plan, water) {
  if (!plan?.segments?.length || !plan.prefabs || !Object.values(plan.prefabs).some(p => p.heightMode === 'conform')) return null;
  return { segments: plan.segments, nodes: plan.nodes ?? [], prefabs: plan.prefabs, water };
}
