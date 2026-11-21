// Crop square in CS1 coordinates → CS2 coordinates (schema/README.md §2: CS2 = CS1 + (dx, dz),
// the crop centre lands on the CS2 map origin; M1 has no rotation).

import type { Crop, Cs1Node } from './types.ts';

/** CS2 playable area: 441 tiles × 623.304 m ≈ 14 336 m square (−7168 … +7168). */
export const CS2_MAX_CROP = 14336;
/** CS1 81-tile map: 17 280 m square. */
export const CS1_MAP_SIZE = 17280;
export const MIN_CROP = 100;

export function cropBounds(c: Crop) {
  const h = c.size / 2;
  return { minX: c.centerX - h, maxX: c.centerX + h, minZ: c.centerZ - h, maxZ: c.centerZ + h };
}

export function inCrop(c: Crop, x: number, z: number): boolean {
  const h = c.size / 2;
  return Math.abs(x - c.centerX) <= h && Math.abs(z - c.centerZ) <= h;
}

export function cropTransform(c: Crop) {
  return { dx: 0 - c.centerX, dz: 0 - c.centerZ }; // no -0 in the JSON
}

/** Problems with a crop: errors make the package invalid, warnings are reported. */
export function validateCrop(c: Crop, mapSize = CS1_MAP_SIZE): { errors: string[]; warnings: string[] } {
  const errors: string[] = [], warnings: string[] = [];
  if (![c.centerX, c.centerZ, c.size].every(Number.isFinite)) errors.push('crop values must be numbers');
  else {
    if (c.size < MIN_CROP) errors.push(`crop size ${c.size} m is below the minimum ${MIN_CROP} m`);
    if (c.size > CS2_MAX_CROP) errors.push(`crop size ${c.size} m exceeds the CS2 playable area (${CS2_MAX_CROP} m)`);
    const b = cropBounds(c), h = mapSize / 2;
    if (b.minX < -h || b.maxX > h || b.minZ < -h || b.maxZ > h) warnings.push(`crop extends beyond the CS1 map (±${h} m); that part is empty`);
  }
  return { errors, warnings };
}

/** Clamp a crop to sane limits (used by the UI while dragging). */
export function clampCrop(c: Crop, mapSize = CS1_MAP_SIZE): Crop {
  const size = Math.round(Math.min(CS2_MAX_CROP, Math.max(MIN_CROP, c.size)));
  const h = mapSize / 2;
  const lim = Math.max(0, h - size / 2);
  return { centerX: Math.round(Math.min(lim, Math.max(-lim, c.centerX))), centerZ: Math.round(Math.min(lim, Math.max(-lim, c.centerZ))), size };
}

/**
 * Crop centre (on a `step` grid) that contains the most of the given segments, each given by its two
 * end points [x0, z0, x1, z1]; a segment counts when both ends are inside. Ties → closest to the map centre.
 */
export function bestCropCenter(segs: [number, number, number, number][], size: number, step = 250, mapSize = CS1_MAP_SIZE): { x: number; z: number; count: number } {
  const h = size / 2, lim = Math.max(0, mapSize / 2 - h);
  const n = Math.floor(lim / step);
  // Per segment, the centres that contain it form a rectangle; accumulate with a 2D difference grid.
  const dim = 2 * n + 1;
  const diff = new Int32Array((dim + 1) * (dim + 1));
  for (const [x0, z0, x1, z1] of segs) {
    const loX = Math.max(x0, x1) - h, hiX = Math.min(x0, x1) + h, loZ = Math.max(z0, z1) - h, hiZ = Math.min(z0, z1) + h;
    if (loX > hiX || loZ > hiZ) continue;
    const i0 = Math.max(-n, Math.ceil(loX / step)), i1 = Math.min(n, Math.floor(hiX / step));
    const j0 = Math.max(-n, Math.ceil(loZ / step)), j1 = Math.min(n, Math.floor(hiZ / step));
    if (i0 > i1 || j0 > j1) continue;
    const a = i0 + n, b = i1 + n + 1, c = j0 + n, d = j1 + n + 1;
    diff[a * (dim + 1) + c]++; diff[b * (dim + 1) + c]--; diff[a * (dim + 1) + d]--; diff[b * (dim + 1) + d]++;
  }
  let best = { x: 0, z: 0, count: -1, r: Infinity };
  const acc = new Int32Array((dim + 1) * (dim + 1));
  for (let i = 0; i < dim; i++) for (let j = 0; j < dim; j++) {
    const k = i * (dim + 1) + j;
    acc[k] = diff[k] + (i ? acc[k - dim - 1] : 0) + (j ? acc[k - 1] : 0) - (i && j ? acc[k - dim - 2] : 0);
    const x = (i - n) * step, z = (j - n) * step, r = x * x + z * z;
    if (acc[k] > best.count || (acc[k] === best.count && r < best.r)) best = { x, z, count: acc[k], r };
  }
  return { x: best.x, z: best.z, count: Math.max(0, best.count) };
}

/** Centre of the densest area of nodes (grid count + 3×3 smoothing), for a default crop. */
export function densestCenter(nodes: Pick<Cs1Node, 'position'>[], cell = 250): { x: number; z: number } {
  if (!nodes.length) return { x: 0, z: 0 };
  const counts = new Map<string, number>();
  for (const n of nodes) {
    const k = `${Math.floor(n.position[0] / cell)},${Math.floor(n.position[2] / cell)}`;
    counts.set(k, (counts.get(k) ?? 0) + 1);
  }
  let best = { score: -1, i: 0, j: 0 };
  for (const k of counts.keys()) {
    const [i, j] = k.split(',').map(Number);
    let score = 0;
    for (let di = -2; di <= 2; di++) for (let dj = -2; dj <= 2; dj++) score += counts.get(`${i + di},${j + dj}`) ?? 0;
    if (score > best.score) best = { score, i, j };
  }
  return { x: (best.i + 0.5) * cell, z: (best.j + 0.5) * cell };
}
