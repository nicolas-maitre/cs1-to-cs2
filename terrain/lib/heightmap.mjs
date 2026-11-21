// CS1 heightmap (from .cs1x) → CS2 playable-area heightmap. See terrain/NOTES-cs2-terrain.md for the CS2 side.
import { readZip } from './zip.mjs';
import { conformTerrain, conformInput } from './conform.mjs';

/** CS2 constants, read from Game.dll (TerrainSystem..cctor, GetHeightData). */
export const CS2 = Object.freeze({
  resolution: 4096,        // TerrainSystem.kDefaultHeightmapWidth/Height; IsValidHeightmapFormat requires it
  size: 14336,             // kDefaultMapSize (playable area, metres), centred on the origin
  cellSize: 14336 / 4096,  // 3.5 m
  worldSize: 57344,        // kDefaultWorldSize = 4 × map size (backdrop / world map)
  defaultHeightScale: 4096, // kDefaultHeightScaleOffset.x
});

/** CS1 constants (TerrainManager): 1081² samples over 17 280 m, height = raw × 1024/65536. */
export const CS1_MAX_HEIGHT = 1024;

/** Loads terrain.json + terrain.r16 from a .cs1x buffer. Returns {meta, heights: Float32Array (metres)}. */
export function loadCs1xTerrain(zipBuf) {
  const zip = readZip(zipBuf);
  const manifestEntry = zip.get('manifest.json');
  const manifest = manifestEntry ? JSON.parse(manifestEntry.read().toString('utf8')) : null;
  const status = manifest?.sections?.terrain;
  if (status && status !== 'ok') throw new Error(`.cs1x terrain section is "${status}"`);
  const tj = zip.get('terrain.json');
  if (!tj) throw new Error('.cs1x has no terrain.json');
  const meta = JSON.parse(tj.read().toString('utf8'));
  const r16 = zip.get(meta.file || 'terrain.r16');
  if (!r16) throw new Error(`.cs1x has no ${meta.file || 'terrain.r16'}`);
  return { meta, manifest, heights: decodeCs1Raw(meta, r16.read()) };
}

/** terrain.json meta + raw bytes → Float32Array of heights in metres (row 0 = south, col 0 = west). */
export function decodeCs1Raw(meta, raw) {
  const n = meta.resolution;
  if (raw.length !== n * n * 2) throw new Error(`terrain.r16 is ${raw.length} bytes, expected ${n * n * 2} for ${n}²`);
  if (meta.byteOrder && !/little/i.test(meta.byteOrder)) throw new Error('unsupported byteOrder ' + meta.byteOrder);
  if (meta.rowOrder && !/row 0 = minimum z/i.test(meta.rowOrder)) throw new Error('unsupported rowOrder ' + meta.rowOrder);
  const out = new Float32Array(n * n);
  const s = meta.heightScale;
  for (let i = 0; i < n * n; i++) out[i] = raw.readUInt16LE(i * 2) * s;
  return out;
}

/** Bilinear sample of the CS1 grid at CS1 world (x, z). Outside the map the edge value is used (clamp). */
export function sampleCs1(meta, heights, x, z) {
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

/** World position of CS2 heightmap pixel (col, row): pixel centres, row 0 = minimum z. */
export function cs2PixelCenter(col, row) {
  return [-CS2.size / 2 + (col + 0.5) * CS2.cellSize, -CS2.size / 2 + (row + 0.5) * CS2.cellSize];
}

/**
 * Picks heightScale / heightOffset.
 *  - "absolute" (default): scale 1024, offset 0, so CS2 height == CS1 height (CS1's own range and precision).
 *  - "fit": offset just below the minimum, scale just above the range (at least 200, the editor's minimum).
 */
export function chooseHeightMapping(min, max, { mode = 'absolute', margin = 10, heightScale, heightOffset } = {}) {
  if (heightScale != null) return { mode: 'explicit', heightScale, heightOffset: heightOffset ?? 0 };
  if (mode === 'absolute') return { mode, heightScale: CS1_MAX_HEIGHT, heightOffset: 0 };
  if (mode === 'fit') {
    const off = Math.floor(min - margin);
    return { mode, heightScale: Math.max(200, Math.ceil(max + margin - off)), heightOffset: off };
  }
  throw new Error('unknown height mode ' + mode);
}

/** Resamples the CS1 grid onto an N² CS2 grid of `size` metres centred on the CS2 origin (pixel centres). */
export function resample(cs1Meta, cs1Heights, transform, size = CS2.size, N = CS2.resolution) {
  const heights = new Float32Array(N * N);
  const cell = size / N, half1 = cs1Meta.size / 2;
  let min = Infinity, max = -Infinity, clamped = 0;
  for (let row = 0; row < N; row++) {
    const z1 = -size / 2 + (row + 0.5) * cell - transform.dz;
    const zOut = z1 < -half1 || z1 > half1;
    for (let col = 0; col < N; col++) {
      const x1 = -size / 2 + (col + 0.5) * cell - transform.dx;
      if (zOut || x1 < -half1 || x1 > half1) clamped++;
      const h = sampleCs1(cs1Meta, cs1Heights, x1, z1);
      heights[row * N + col] = h;
      if (h < min) min = h;
      if (h > max) max = h;
    }
  }
  return { heights, min, max, clampedFraction: clamped / (N * N) };
}

/** Heights (m) → uint16 with the given mapping. */
export function quantize(heights, map) {
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

/**
 * Converts. crop = {centerX, centerZ, size} in CS1 metres; transform defaults to {dx: -centerX, dz: -centerZ}.
 * Returns {raw: Uint16Array (4096², row 0 = south), meta}, plus {worldRaw} with opts.world: the 57 344 m world
 * (backdrop) map around it, from the same CS1 data (edge-clamped outside the CS1 map), same height mapping.
 * opts.plan: burns the plan's `conform` ground roads into the playable grid (conform.mjs, schema §2) before the
 * height mapping, which is then chosen from the burned grid (stats.min/max too); opts.water ({seaLevel, depthMeta,
 * depth}) for the wet-pixel rule. The world map is never burned. With a burn, the result also has {natural, burned}
 * (float32 grids) for adjustWaterSources.
 */
export function convert(cs1Meta, cs1Heights, crop, opts = {}) {
  const transform = opts.transform ?? { dx: -crop.centerX, dz: -crop.centerZ };
  const N = CS2.resolution;
  const r = resample(cs1Meta, cs1Heights, transform, CS2.size, N);
  // road-conformed terrain: burn before the height mapping, which is then chosen from the burned grid
  const input = opts.plan ? conformInput(opts.plan, opts.water ? { ...opts.water, dx: transform.dx, dz: transform.dz } : null) : null;
  let natural = null, conform = null;
  if (input) {
    natural = r.heights.slice();
    conform = conformTerrain(r.heights, N, CS2.size, input);
    if (conform) {
      let min = Infinity, max = -Infinity;
      for (let i = 0; i < r.heights.length; i++) { const h = r.heights[i]; if (h < min) min = h; if (h > max) max = h; }
      r.min = min; r.max = max;
    } else natural = null;
  }
  const map = chooseHeightMapping(r.min, r.max, opts);
  const q = quantize(r.heights, map);
  const meta = {
    format: 'cs1conv-terrain', formatVersion: 1,
    resolution: N, size: CS2.size, cellSize: CS2.cellSize,
    file: 'heightmap.r16',
    rowOrder: 'row 0 = minimum z (south), column 0 = minimum x (west); pixel centres at -size/2 + (i + 0.5) * cellSize',
    byteOrder: 'little-endian uint16',
    heightScale: map.heightScale, heightOffset: map.heightOffset, heightMode: map.mode,
    encoding: 'height_m = raw / 65535 * heightScale + heightOffset (CS2 TerrainSystem.heightScaleOffset = (heightScale, heightOffset))',
    crop, transform,
    source: { resolution: cs1Meta.resolution, size: cs1Meta.size, heightScale: cs1Meta.heightScale, min: cs1Meta.min, max: cs1Meta.max },
    stats: {
      min: round3(r.min), max: round3(r.max),
      clampedFraction: round6(r.clampedFraction),
      saturatedPixels: q.saturated,
      maxQuantizationErrorM: round6(q.maxQuantErr),
    },
  };
  if (conform) { const { skippedIds, ...json } = conform; meta.roadConformed = true; meta.conform = json; }
  const out = { raw: q.raw, meta };
  if (conform) { out.natural = natural; out.burned = r.heights; out.conform = conform; }
  if (opts.world) {
    const w = resample(cs1Meta, cs1Heights, transform, CS2.worldSize, N);
    const wq = quantize(w.heights, map);
    meta.world = {
      file: 'worldmap.r16', resolution: N, size: CS2.worldSize, cellSize: CS2.worldSize / N,
      stats: { min: round3(w.min), max: round3(w.max), clampedFraction: round6(w.clampedFraction), saturatedPixels: wq.saturated },
    };
    out.worldRaw = wq.raw;
  }
  return out;
}

/** Decodes an output heightmap value at CS2 world (x, z) the way TerrainUtils.SampleHeight does (floor + bilinear). */
export function sampleCs2(meta, raw, x, z) {
  const N = meta.resolution, scale = N / meta.size;
  const fx = (x + meta.size / 2) * scale - 0.5, fz = (z + meta.size / 2) * scale - 0.5;
  const x0 = Math.floor(fx), z0 = Math.floor(fz);
  const cx0 = clampI(x0, N), cx1 = clampI(x0 + 1, N), cz0 = clampI(z0, N), cz1 = clampI(z0 + 1, N);
  const tx = sat(fx - cx0), tz = sat(fz - cz0);
  const a = raw[cz0 * N + cx0], b = raw[cz0 * N + cx1], c = raw[cz1 * N + cx0], d = raw[cz1 * N + cx1];
  const v = (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
  return v / 65535 * meta.heightScale + meta.heightOffset;
}

/** Raw uint16 grid → little-endian bytes. */
export function rawToBytes(raw) {
  const b = Buffer.alloc(raw.length * 2);
  for (let i = 0; i < raw.length; i++) b.writeUInt16LE(raw[i], i * 2);
  return b;
}

/** Row 0 = south grid → image order (top row = north), for PNG export. */
export function flipRows(raw, N) {
  const out = new Uint16Array(raw.length);
  for (let r = 0; r < N; r++) out.set(raw.subarray(r * N, (r + 1) * N), (N - 1 - r) * N);
  return out;
}

function clampI(i, n) { return i < 0 ? 0 : i > n - 1 ? n - 1 : i; }
function sat(t) { return t < 0 ? 0 : t > 1 ? 1 : t; }
function round3(v) { return Math.round(v * 1000) / 1000; }
function round6(v) { return Math.round(v * 1e6) / 1e6; }
