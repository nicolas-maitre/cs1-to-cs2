// CS1 water → CS2 sea level + water sources, as terrain/mapprep.mjs `water` sends them to MapPrep `water.set`.
// TypeScript port of terrain/lib/water.mjs (convertWater, regionSources, depthCheckpoints); same defaults and
// arithmetic, so the plan carries exactly what the scripted flow applies (test/terrain.test.ts compares).

import type { WaterSection } from '../types.ts';
import { CS2_TERRAIN } from './heightmap.ts';

export interface Cs2WaterSource { x: number; z: number; height: number; radius: number; polluted: number; cs1: Record<string, unknown> }
type Transform = { dx: number; dz: number };

function round2(v: number) { return Math.round(v * 100) / 100; }

/** Natural CS1 sources → CS2 sources (height = CS1 target level − terrain at the source). */
export function convertWater(water: WaterSection, plan: { transform: Transform }, opts: { radius?: number; minHeight?: number; margin?: number; types?: string[] } = {}) {
  const { radius = 20, minHeight = 1, margin = 50, types = ['natural'] } = opts;
  const { dx, dz } = plan.transform;
  const half = CS2_TERRAIN.size / 2 - margin;
  const sources: Cs2WaterSource[] = [];
  const stats = { cs1Sources: 0, byType: {} as Record<string, number>, outside: 0, kept: 0, seaLevel: water.seaLevel };
  for (const s of water.sources || []) {
    stats.cs1Sources++;
    stats.byType[s.type] = (stats.byType[s.type] || 0) + 1;
    if (!types.includes(s.type)) continue;
    const p = s.outputPosition || s.inputPosition;
    if (!p) continue;
    const x = p[0] + dx, z = p[2] + dz;
    if (Math.abs(x) > half || Math.abs(z) > half) { stats.outside++; continue; }
    const ground = s.terrainHeightAtOutput ?? p[1];
    const height = Math.max(minHeight, (s.targetHeight ?? ground) - ground);
    sources.push({
      x: round2(x), z: round2(z), height: round2(height), radius,
      polluted: s.pollution ? Math.min(1, s.pollution / 1000) : 0,
      cs1: { id: s.id, type: s.type, targetHeight: s.targetHeight, inputRate: s.inputRate, outputRate: s.outputRate },
    });
  }
  stats.kept = sources.length;
  return { seaLevel: water.seaLevel, sources, stats };
}

interface DepthMeta { resolution: number; size: number; heightScale: number }

/** Up to n wet and n dry checkpoints of the CS1 depth grid inside the playable area (CS2 coordinates). */
export function depthCheckpoints(depthMeta: DepthMeta, raw: Uint16Array, plan: { transform: Transform }, n = 100, margin = 100) {
  const res = depthMeta.resolution, size = depthMeta.size, sp = size / (res - 1), sc = depthMeta.heightScale;
  const { dx, dz } = plan.transform;
  const half = CS2_TERRAIN.size / 2 - margin;
  const wet: { x: number; z: number; cs1Depth: number }[] = [], dry: { x: number; z: number; cs1Depth: number }[] = [];
  for (let row = 0; row < res; row++) {
    for (let col = 0; col < res; col++) {
      const x = -size / 2 + col * sp + dx, z = -size / 2 + row * sp + dz;
      if (Math.abs(x) > half || Math.abs(z) > half) continue;
      const d = raw[row * res + col] * sc;
      (d > 0.5 ? wet : d === 0 ? dry : []).push({ x: round2(x), z: round2(z), cs1Depth: round2(d) });
    }
  }
  const pick = <T,>(l: T[]) => (l.length <= n ? l : Array.from({ length: n }, (_, i) => l[Math.floor((i * l.length) / n)]));
  return { wet: pick(wet), dry: pick(dry), totals: { wet: wet.length, dry: dry.length } };
}

/**
 * Extra sources so CS1 rivers and lakes above sea level fill in CS2 (port of terrain/lib/water.mjs regionSources).
 * Wet regions above the sea (8-connected), per region of at least `minCells`:
 *   - touching the playable edge band: an inflow source at their deepest band cell;
 *   - enclosed without a source: a lake source at their deepest cell;
 *   - enclosed with a source but at least `boostCells` cells: one more at the deepest cell ("lake-boost");
 *   - big regions: up to `maxSpread` more ("<kind>-spread", radius 40 m) by farthest-point sampling, one per
 *     `spreadCells` cells, so long rivers fill along their whole length.
 * heights: the CS1 terrain grid in metres (same lattice as the depth grid).
 */
export function regionSources(depthMeta: DepthMeta, raw: Uint16Array, heights: Float32Array, plan: { transform: Transform }, seaLevel: number, existing: { x: number; z: number }[] = [], opts: { minCells?: number; edge?: number; margin?: number; minDepth?: number; seaTolerance?: number; boostCells?: number; spreadCells?: number; maxSpread?: number } = {}) {
  const { minCells = 4, edge = 300, margin = 50, minDepth = 1, seaTolerance = 4, boostCells = 500, spreadCells = 1500, maxSpread = 8 } = opts;
  const res = depthMeta.resolution, size = depthMeta.size, sp = size / (res - 1), sc = depthMeta.heightScale;
  const { dx, dz } = plan.transform;
  const half = CS2_TERRAIN.size / 2 - margin;
  const wet = new Uint8Array(res * res);
  const depth = new Float32Array(res * res);
  for (let row = 0; row < res; row++) for (let col = 0; col < res; col++) {
    const i = row * res + col;
    const x = -size / 2 + col * sp + dx, z = -size / 2 + row * sp + dz;
    if (Math.abs(x) > half || Math.abs(z) > half) continue;
    const d = raw[i] * sc;
    if (d < minDepth) continue;
    if (heights[i] + d <= seaLevel + seaTolerance) continue;
    wet[i] = 1; depth[i] = d;
  }
  const seen = new Uint8Array(res * res);
  const out: Cs2WaterSource[] = [], regions: { cells: number; kind: string; sources?: number }[] = [];
  const stack: number[] = [];
  for (let s = 0; s < wet.length; s++) {
    if (!wet[s] || seen[s]) continue;
    const cells: number[] = [];
    stack.push(s); seen[s] = 1;
    while (stack.length) {
      const i = stack.pop()!; cells.push(i);
      const r = Math.floor(i / res), c = i % res;
      for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) {
        const rr = r + a, cc = c + b;
        if (rr < 0 || cc < 0 || rr >= res || cc >= res) continue;
        const j = rr * res + cc;
        if (wet[j] && !seen[j]) { seen[j] = 1; stack.push(j); }
      }
    }
    if (cells.length < minCells) continue;
    const pos = (i: number) => [-size / 2 + (i % res) * sp + dx, -size / 2 + Math.floor(i / res) * sp + dz];
    const inBand = (i: number) => { const [x, z] = pos(i); return Math.max(Math.abs(x), Math.abs(z)) > half - edge; };
    const band = cells.filter(inBand);
    const radius = Math.max(20, Math.min(80, (Math.sqrt(cells.length) * sp) / 4));
    let kind: string, pick: number[];
    if (band.length) { kind = 'inflow'; pick = band; }
    else {
      const [minX, minZ, maxX, maxZ] = cells.reduce((m, i) => { const [x, z] = pos(i); return [Math.min(m[0], x), Math.min(m[1], z), Math.max(m[2], x), Math.max(m[3], z)]; }, [Infinity, Infinity, -Infinity, -Infinity]);
      if (existing.some(e => e.x >= minX - sp && e.x <= maxX + sp && e.z >= minZ - sp && e.z <= maxZ + sp)) {
        // a big lake with one small CS1 source fills very slowly in CS2: add a source at its deepest cell too
        if (cells.length < boostCells) { regions.push({ cells: cells.length, kind: 'has-source' }); continue; }
        kind = 'lake-boost';
      } else kind = 'lake';
      pick = cells;
    }
    const best = pick.reduce((p, i) => (depth[i] > depth[p] ? i : p), pick[0]);
    const [x, z] = pos(best);
    out.push({ x: round2(x), z: round2(z), height: round2(depth[best]), radius: Math.round(radius), polluted: 0, cs1: { kind, cells: cells.length } });
    // long regions (rivers, chains of lakes) fill from one point far too slowly: spread more sources over them
    const extra = Math.min(maxSpread, Math.floor(cells.length / spreadCells));
    if (extra > 0) {
      const dmin = new Float64Array(cells.length).fill(Infinity);
      const upd = (c: number) => { const [cx, cz] = pos(c); for (let k = 0; k < cells.length; k++) { const [px, pz] = pos(cells[k]); const d = (px - cx) ** 2 + (pz - cz) ** 2; if (d < dmin[k]) dmin[k] = d; } };
      upd(best);
      for (let n = 0; n < extra; n++) {
        let far = 0;
        for (let k = 1; k < cells.length; k++) if (dmin[k] > dmin[far]) far = k;
        if (Math.sqrt(dmin[far]) < 400) break; // already within 400 m of a source everywhere
        const c = cells[far]; upd(c);
        const [sx, sz] = pos(c);
        out.push({ x: round2(sx), z: round2(sz), height: round2(depth[c]), radius: 40, polluted: 0, cs1: { kind: kind + '-spread', cells: cells.length } });
      }
    }
    regions.push({ cells: cells.length, kind, sources: 1 + Math.max(0, extra) });
  }
  return { sources: out, regions };
}
