// CS1 heightmap → CS2 playable-area heightmap (+ world map). TypeScript port of terrain/lib/heightmap.mjs
// (the terrain worker's converter): same arithmetic in the same order, so the .r16 output is byte-identical
// (test/terrain.test.ts runs terrain/convert.mjs and compares). See terrain/NOTES-cs2-terrain.md for the CS2 side.

import type { Crop, Terrain } from '../types.ts';

/** CS2 constants, read from Game.dll (TerrainSystem..cctor, GetHeightData). */
export const CS2_TERRAIN = Object.freeze({
  resolution: 4096,        // TerrainSystem.kDefaultHeightmapWidth/Height; IsValidHeightmapFormat requires it
  size: 14336,             // kDefaultMapSize (playable area, metres), centred on the origin
  cellSize: 14336 / 4096,  // 3.5 m
  worldSize: 57344,        // kDefaultWorldSize = 4 × map size (backdrop / world map)
  defaultHeightScale: 4096,
});

/** CS1: 1081² samples over 17 280 m, height = raw × 1024/65536. */
export const CS1_MAX_HEIGHT = 1024;

export interface Cs1TerrainMeta { resolution: number; size: number; heightScale: number; min?: number; max?: number }

/** CS1 heights in metres as float32 (row 0 = south), exactly like terrain/lib/heightmap.mjs decodeCs1Raw. */
export function cs1Heights(t: Terrain): Float32Array {
  const n = t.resolution * t.resolution;
  const out = new Float32Array(n);
  const s = t.heightScale;
  for (let i = 0; i < n; i++) out[i] = t.heights[i] * s;
  return out;
}

/** Bilinear sample of the CS1 grid at CS1 world (x, z); outside the map the edge value is used. */
export function sampleCs1(meta: Cs1TerrainMeta, heights: Float32Array, x: number, z: number): number {
  const n = meta.resolution, half = meta.size / 2, sp = meta.size / (n - 1);
  let fx = (x + half) / sp, fz = (z + half) / sp;
  if (fx < 0) fx = 0; else if (fx > n - 1) fx = n - 1;
  if (fz < 0) fz = 0; else if (fz > n - 1) fz = n - 1;
  let x0 = Math.floor(fx), z0 = Math.floor(fz);
  if (x0 > n - 2) x0 = n - 2;
  if (z0 > n - 2) z0 = n - 2;
  const tx = fx - x0, tz = fz - z0;
  const i = z0 * n + x0;
  const a = heights[i], b = heights[i + 1], c = heights[i + n], d = heights[i + n + 1];
  return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
}

export interface HeightMapping { mode: string; heightScale: number; heightOffset: number }

/** "absolute" (default): scale 1024, offset 0, CS2 height == CS1 height. "fit": tight range (min 200). */
export function chooseHeightMapping(min: number, max: number, o: { mode?: string; margin?: number; heightScale?: number; heightOffset?: number } = {}): HeightMapping {
  const { mode = 'absolute', margin = 10, heightScale, heightOffset } = o;
  if (heightScale != null) return { mode: 'explicit', heightScale, heightOffset: heightOffset ?? 0 };
  if (mode === 'absolute') return { mode, heightScale: CS1_MAX_HEIGHT, heightOffset: 0 };
  if (mode === 'fit') {
    const off = Math.floor(min - margin);
    return { mode, heightScale: Math.max(200, Math.ceil(max + margin - off)), heightOffset: off };
  }
  throw new Error('unknown height mode ' + mode);
}

/** Resamples the CS1 grid onto an N² CS2 grid of `size` metres centred on the CS2 origin (pixel centres). */
export function resample(meta: Cs1TerrainMeta, heights: Float32Array, transform: { dx: number; dz: number }, size: number = CS2_TERRAIN.size, N: number = CS2_TERRAIN.resolution) {
  const out = new Float32Array(N * N);
  const cell = size / N, half1 = meta.size / 2;
  let min = Infinity, max = -Infinity, clamped = 0;
  for (let row = 0; row < N; row++) {
    const z1 = -size / 2 + (row + 0.5) * cell - transform.dz;
    const zOut = z1 < -half1 || z1 > half1;
    for (let col = 0; col < N; col++) {
      const x1 = -size / 2 + (col + 0.5) * cell - transform.dx;
      if (zOut || x1 < -half1 || x1 > half1) clamped++;
      const h = sampleCs1(meta, heights, x1, z1);
      out[row * N + col] = h;
      if (h < min) min = h;
      if (h > max) max = h;
    }
  }
  return { heights: out, min, max, clampedFraction: clamped / (N * N) };
}

/** Heights (m) → uint16 with the given mapping. */
export function quantize(heights: Float32Array, map: HeightMapping) {
  const raw = new Uint16Array(heights.length);
  const k = 65535 / map.heightScale;
  let maxQuantErr = 0, saturated = 0;
  for (let i = 0; i < raw.length; i++) {
    let v = Math.round((heights[i] - map.heightOffset) * k);
    if (v < 0) { v = 0; saturated++; } else if (v > 65535) { v = 65535; saturated++; }
    raw[i] = v;
    if (v > 0 && v < 65535) {
      const err = Math.abs(v / k + map.heightOffset - heights[i]);
      if (err > maxQuantErr) maxQuantErr = err;
    }
  }
  return { raw, saturated, maxQuantErr };
}

export interface TerrainMeta {
  format: 'cs1conv-terrain'; formatVersion: 1;
  resolution: number; size: number; cellSize: number; file: string; rowOrder: string; byteOrder: string;
  heightScale: number; heightOffset: number; heightMode: string; encoding: string;
  crop: Crop; transform: { dx: number; dz: number };
  source: { resolution: number; size: number; heightScale: number; min?: number; max?: number; file?: string };
  stats: { min: number; max: number; clampedFraction: number; saturatedPixels: number; maxQuantizationErrorM: number };
  world?: { file: string; resolution: number; size: number; cellSize: number; stats: { min: number; max: number; clampedFraction: number; saturatedPixels: number } };
  /** road-conformed terrain (schema §2, #110–113): the conform roads are burned into heightmap.r16 */
  roadConformed?: boolean;
  conform?: Record<string, unknown>;
  createdAt?: string;
}

/** Burns into the resampled playable-area grid (metres, in place); returns terrain.json's `conform` stats or null. */
export type TerrainBurn = (grid: Float32Array, N: number, size: number) => Record<string, unknown> | null;

/** Converts (crop in CS1 metres; transform defaults to {dx: −centerX, dz: −centerZ}). Same as terrain/lib/heightmap.mjs convert. */
export function convertTerrain(meta: Cs1TerrainMeta, heights: Float32Array, crop: Crop, opts: { transform?: { dx: number; dz: number }; mode?: string; heightScale?: number; heightOffset?: number; world?: boolean; burn?: TerrainBurn } = {}) {
  const transform = opts.transform ?? { dx: -crop.centerX, dz: -crop.centerZ };
  const N = CS2_TERRAIN.resolution;
  const r = resample(meta, heights, transform, CS2_TERRAIN.size, N);
  // road-conformed terrain: burn before the height mapping, which is then chosen from the burned grid
  let natural: Float32Array | undefined, conform: Record<string, unknown> | null = null;
  if (opts.burn) {
    natural = r.heights.slice();
    conform = opts.burn(r.heights, N, CS2_TERRAIN.size);
    if (conform) {
      let min = Infinity, max = -Infinity;
      for (let i = 0; i < r.heights.length; i++) { const h = r.heights[i]; if (h < min) min = h; if (h > max) max = h; }
      r.min = min; r.max = max;
    } else natural = undefined;
  }
  const map = chooseHeightMapping(r.min, r.max, opts);
  const q = quantize(r.heights, map);
  const out: TerrainMeta = {
    format: 'cs1conv-terrain', formatVersion: 1,
    resolution: N, size: CS2_TERRAIN.size, cellSize: CS2_TERRAIN.cellSize,
    file: 'heightmap.r16',
    rowOrder: 'row 0 = minimum z (south), column 0 = minimum x (west); pixel centres at -size/2 + (i + 0.5) * cellSize',
    byteOrder: 'little-endian uint16',
    heightScale: map.heightScale, heightOffset: map.heightOffset, heightMode: map.mode,
    encoding: 'height_m = raw / 65535 * heightScale + heightOffset (CS2 TerrainSystem.heightScaleOffset = (heightScale, heightOffset))',
    crop, transform,
    source: { resolution: meta.resolution, size: meta.size, heightScale: meta.heightScale, min: meta.min, max: meta.max },
    stats: { min: round3(r.min), max: round3(r.max), clampedFraction: round6(r.clampedFraction), saturatedPixels: q.saturated, maxQuantizationErrorM: round6(q.maxQuantErr) },
    ...(conform ? { roadConformed: true, conform } : {}),
  };
  let worldRaw: Uint16Array | undefined;
  if (opts.world) {
    const w = resample(meta, heights, transform, CS2_TERRAIN.worldSize, N);
    const wq = quantize(w.heights, map);
    out.world = {
      file: 'worldmap.r16', resolution: N, size: CS2_TERRAIN.worldSize, cellSize: CS2_TERRAIN.worldSize / N,
      stats: { min: round3(w.min), max: round3(w.max), clampedFraction: round6(w.clampedFraction), saturatedPixels: wq.saturated },
    };
    worldRaw = wq.raw;
  }
  return { raw: q.raw, worldRaw, meta: out, natural, burned: natural ? r.heights : undefined };
}

/** Raw uint16 grid → little-endian bytes. */
export function rawToBytes(raw: Uint16Array): Uint8Array {
  const b = new Uint8Array(raw.length * 2);
  for (let i = 0; i < raw.length; i++) { const v = raw[i]; b[i * 2] = v & 0xff; b[i * 2 + 1] = v >>> 8; }
  return b;
}

/** Decodes an output heightmap value at CS2 world (x, z) like CS2's TerrainUtils.SampleHeight. */
export function sampleCs2(meta: { resolution: number; size: number; heightScale: number; heightOffset: number }, raw: Uint16Array, x: number, z: number): number {
  const N = meta.resolution, scale = N / meta.size;
  const fx = (x + meta.size / 2) * scale - 0.5, fz = (z + meta.size / 2) * scale - 0.5;
  const x0 = Math.floor(fx), z0 = Math.floor(fz);
  const cx0 = clampI(x0, N), cx1 = clampI(x0 + 1, N), cz0 = clampI(z0, N), cz1 = clampI(z0 + 1, N);
  const tx = sat(fx - cx0), tz = sat(fz - cz0);
  const a = raw[cz0 * N + cx0], b = raw[cz0 * N + cx1], c = raw[cz1 * N + cx0], d = raw[cz1 * N + cx1];
  const v = (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
  return v / 65535 * meta.heightScale + meta.heightOffset;
}

function clampI(i: number, n: number) { return i < 0 ? 0 : i > n - 1 ? n - 1 : i; }
function sat(t: number) { return t < 0 ? 0 : t > 1 ? 1 : t; }
function round3(v: number) { return Math.round(v * 1000) / 1000; }
function round6(v: number) { return Math.round(v * 1e6) / 1e6; }
