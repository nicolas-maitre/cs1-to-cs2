// CS1 water (.cs1x water.json + water.r16) → CS2 sea level + water sources (terrain/NOTES-cs2-terrain.md §8).
// Heights stay absolute (terrain/DECISIONS-terrain.md #2), so the sea level carries over as is.
import { CS2 } from './heightmap.mjs';

/**
 * water: parsed water.json; plan: {transform}; opts: {radius=20, minHeight=1, margin=50, types=['natural']}.
 * Returns {seaLevel, sources: [{x, z, height, radius, polluted, cs1}], stats}.
 * height = CS1 target water level − terrain at the source (CS2's m_Height is the water height above the source).
 */
export function convertWater(water, plan, _terrainMeta = null, opts = {}) {
  const { radius = 20, minHeight = 1, margin = 50, types = ['natural'] } = opts;
  const { dx, dz } = plan.transform;
  const half = CS2.size / 2 - margin;
  const sources = [];
  const stats = { cs1Sources: 0, byType: {}, outside: 0, kept: 0, seaLevel: water.seaLevel };
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

/**
 * Checkpoints from the CS1 depth grid (water.r16), moved into CS2 space: up to `n` wet and `n` dry points inside the
 * playable area, evenly subsampled. Each {x, z, cs1Depth}.
 */
export function depthCheckpoints(depthMeta, raw, plan, n = 100, margin = 100) {
  const res = depthMeta.resolution, size = depthMeta.size, sp = size / (res - 1), sc = depthMeta.heightScale;
  const { dx, dz } = plan.transform;
  const half = CS2.size / 2 - margin;
  const wet = [], dry = [];
  for (let row = 0; row < res; row++) {
    for (let col = 0; col < res; col++) {
      const x = -size / 2 + col * sp + dx, z = -size / 2 + row * sp + dz;
      if (Math.abs(x) > half || Math.abs(z) > half) continue;
      const d = raw.readUInt16LE((row * res + col) * 2) * sc;
      (d > 0.5 ? wet : d === 0 ? dry : []).push({ x: round2(x), z: round2(z), cs1Depth: round2(d) });
    }
  }
  const pick = (l) => l.length <= n ? l : Array.from({ length: n }, (_, i) => l[Math.floor(i * l.length / n)]);
  return { wet: pick(wet), dry: pick(dry), totals: { wet: wet.length, dry: dry.length } };
}

function round2(v) { return Math.round(v * 100) / 100; }

/**
 * Extra sources so CS1 rivers and lakes above sea level fill in CS2 (CS1 feeds many of them from sources outside the
 * playable area). Wet CS1 cells whose surface is above the sea level are grouped into 8-connected regions; per region of
 * at least `minCells`:
 *   - touching the playable edge band (`edge` m): a source at its deepest cell in the band (river inflow);
 *   - otherwise, if no existing source lies in it: a source at its deepest cell (lake);
 *   - a lake that has a source but at least `boostCells` cells (500 × 256 m² ≈ 0.13 km²) also gets one at its deepest
 *     cell ("lake-boost"), so it fills in minutes instead of hours;
 *   - big regions also get up to `maxSpread` more sources ("<kind>-spread", radius 40 m) spread over them by
 *     farthest-point sampling, one per `spreadCells` cells, so long rivers fill along their whole length.
 * height = CS1 water depth there; radius grows with the region (20–80 m).
 * terrain: {meta, heights: Float32Array} (CS1 grid, loadCs1xTerrain); existing: converted sources [{x,z}] (CS2 coords).
 */
export function regionSources(depthMeta, raw, terrain, plan, seaLevel, existing = [], opts = {}) {
  const { minCells = 4, edge = 300, margin = 50, minDepth = 1, seaTolerance = 4, boostCells = 500, spreadCells = 1500, maxSpread = 8 } = opts;
  const res = depthMeta.resolution, size = depthMeta.size, sp = size / (res - 1), sc = depthMeta.heightScale;
  const { dx, dz } = plan.transform;
  const half = CS2.size / 2 - margin;
  const wet = new Uint8Array(res * res);
  const depth = new Float32Array(res * res);
  for (let row = 0; row < res; row++) for (let col = 0; col < res; col++) {
    const i = row * res + col;
    const x = -size / 2 + col * sp + dx, z = -size / 2 + row * sp + dz;
    if (Math.abs(x) > half || Math.abs(z) > half) continue;
    const d = raw.readUInt16LE(i * 2) * sc;
    if (d < minDepth) continue;
    if (terrain.heights[i] + d <= seaLevel + seaTolerance) continue; // the sea fills these (CS1 sea surface varies by ±3 m)
    wet[i] = 1; depth[i] = d;
  }
  const seen = new Uint8Array(res * res);
  const out = [], regions = [];
  const stack = [];
  for (let s = 0; s < wet.length; s++) {
    if (!wet[s] || seen[s]) continue;
    const cells = [];
    stack.push(s); seen[s] = 1;
    while (stack.length) {
      const i = stack.pop(); cells.push(i);
      const r = Math.floor(i / res), c = i % res;
      for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) {
        const rr = r + a, cc = c + b;
        if (rr < 0 || cc < 0 || rr >= res || cc >= res) continue;
        const j = rr * res + cc;
        if (wet[j] && !seen[j]) { seen[j] = 1; stack.push(j); }
      }
    }
    if (cells.length < minCells) continue;
    const pos = i => [-size / 2 + (i % res) * sp + dx, -size / 2 + Math.floor(i / res) * sp + dz];
    const inBand = i => { const [x, z] = pos(i); return Math.max(Math.abs(x), Math.abs(z)) > half - edge; };
    const band = cells.filter(inBand);
    const radius = Math.max(20, Math.min(80, Math.sqrt(cells.length) * sp / 4));
    let kind, pick;
    if (band.length) { kind = 'inflow'; pick = band; }
    else {
      const [minX, minZ, maxX, maxZ] = cells.reduce((m, i) => { const [x, z] = pos(i); return [Math.min(m[0], x), Math.min(m[1], z), Math.max(m[2], x), Math.max(m[3], z)]; }, [Infinity, Infinity, -Infinity, -Infinity]);
      if (existing.some(e => e.x >= minX - sp && e.x <= maxX + sp && e.z >= minZ - sp && e.z <= maxZ + sp)) {
        // a big lake with one small CS1 source takes very long to fill in CS2 (one 20 m source per km² lake):
        // add a source at its deepest cell too
        if (cells.length < boostCells) { regions.push({ cells: cells.length, kind: 'has-source' }); continue; }
        kind = 'lake-boost';
      } else kind = 'lake';
      pick = cells;
    }
    const best = pick.reduce((p, i) => depth[i] > depth[p] ? i : p, pick[0]);
    const [x, z] = pos(best);
    out.push({ x: round2(x), z: round2(z), height: round2(depth[best]), radius: Math.round(radius), polluted: 0, cs1: { kind, cells: cells.length } });
    // long regions (rivers, chains of lakes) fill from one point far too slowly: spread more sources over the region
    // by farthest-point sampling, one per `spreadCells` cells, at most `maxSpread` (full-city Skibidi: a 3.5 km river)
    const extra = Math.min(maxSpread, Math.floor(cells.length / spreadCells));
    if (extra > 0) {
      const chosen = [best];
      const dmin = new Float64Array(cells.length).fill(Infinity);
      const upd = c => { const [cx, cz] = pos(c); for (let k = 0; k < cells.length; k++) { const [px, pz] = pos(cells[k]); const d = (px - cx) ** 2 + (pz - cz) ** 2; if (d < dmin[k]) dmin[k] = d; } };
      upd(best);
      for (let n = 0; n < extra; n++) {
        let far = 0;
        for (let k = 1; k < cells.length; k++) if (dmin[k] > dmin[far]) far = k;
        if (Math.sqrt(dmin[far]) < 400) break; // already within 400 m of a source everywhere
        const c = cells[far]; chosen.push(c); upd(c);
        const [sx, sz] = pos(c);
        out.push({ x: round2(sx), z: round2(sz), height: round2(depth[c]), radius: 40, polluted: 0, cs1: { kind: kind + '-spread', cells: cells.length } });
      }
    }
    regions.push({ cells: cells.length, kind, sources: 1 + Math.max(0, extra) });
  }
  return { sources: out, regions };
}
