// Road-conformed terrain (lib/conform.mjs, schema/README.md §2). Small grids: N = 256 over 896 m keeps the 3.5 m cell.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { conformTerrain, adjustWaterSources, samplePoints, CONFORM } from '../lib/conform.mjs';

const N = 256, SIZE = 896, CELL = SIZE / N;
const at = (x, z) => { const col = Math.round((x + SIZE / 2) / CELL - 0.5), row = Math.round((z + SIZE / 2) / CELL - 0.5); return row * N + col; };
const flat = (h) => new Float32Array(N * N).fill(h);
const line = (x0, y0, z0, x1, y1, z1) => [[x0, y0, z0], [x0 + (x1 - x0) / 3, y0 + (y1 - y0) / 3, z0 + (z1 - z0) / 3], [x0 + 2 * (x1 - x0) / 3, y0 + 2 * (y1 - y0) / 3, z0 + 2 * (z1 - z0) / 3], [x1, y1, z1]];
const prefabs = { road: { heightMode: 'conform' }, wall: { heightMode: 'walls' }, path: { heightMode: 'follow' } };
const seg = (id, bezier, extra = {}) => ({ id, prefab: 'road', kind: 'ground', halfWidth: 6, start: id * 10, end: id * 10 + 1, bezier, ...extra });
const burn = (grid, segments, nodes = [], water = null) => conformTerrain(grid, N, SIZE, { segments, nodes, prefabs, water });
const near = (a, b, eps = 1e-4) => Math.abs(a - b) < eps;

test('samplePoints: n = ceil(16-piece chord length), uniform in t, ends exact', () => {
  const P = samplePoints(line(0, 0, 0, 10.2, 0, 0));
  assert.equal(P.length, 12);
  assert.equal(P[0][0], 0); assert.ok(near(P[11][0], 10.2, 1e-12));
});

test('a road 5 m above flat ground: core at road height, smoothstep blend over b = 2|Δ|, natural beyond', () => {
  const g = flat(100);
  const st = burn(g, [seg(1, line(-200, 105, 0, 200, 105, 0))]);
  const h = (z) => g[at(0, z)];
  assert.equal(h(1.75), 105); assert.equal(h(-5.25), 105);           // core (|z| <= 6)
  const t = (8.75 - 6) / 10, s = t * t * (3 - 2 * t);
  assert.equal(h(8.75), Math.fround(100 + (1 - s) * 5));            // one road: w4 cancels, W = 1 − s
  assert.equal(h(19.25), 100);                                        // beyond w + b = 16
  assert.deepEqual([st.segments, st.skipped, st.maxRaise, st.maxCut], [1, 0, 5, 0]);
  assert.deepEqual(st.before, { min: 100, max: 100 });
  assert.equal(st.maxDifference, CONFORM.maxDifference);
  let prev = 105;
  for (let z = 1.75; z < 20; z += CELL) { assert.ok(h(z) <= prev); prev = h(z); }
});

test('the road profile follows the CS1 bezier height', () => {
  const g = flat(100);
  burn(g, [seg(1, line(-100, 95, 0, 100, 105, 0))]);
  assert.ok(near(g[at(50.75, 1.75)], 95 + 150.75 / 200 * 10));
});

test('only conform ground/slope segments burn; nothing burnt → null, grid untouched', () => {
  const b = line(-100, 105, 0, 100, 105, 0);
  for (const extra of [{ prefab: 'wall' }, { prefab: 'path' }, { kind: 'elevated' }, { kind: 'tunnel' }, { kind: 'bridge' }]) {
    const g = flat(100);
    assert.equal(burn(g, [seg(1, b, extra)]), null);
    assert.equal(g[at(0, 1.75)], 100);
  }
  assert.ok(burn(flat(100), [seg(1, b, { kind: 'slope' })]));
});

test('a core beats any blend; blends of two roads at different heights meet without a step', () => {
  const g = flat(100);
  burn(g, [seg(1, line(-200, 104, -15, 200, 104, -15)), seg(2, line(-200, 96, 15, 200, 96, 15))]);
  const col = (z) => g[at(0, z)];
  assert.equal(col(-15.75), 104); assert.equal(col(15.75), 96);
  let prev = col(-12.25), maxStep = 0;
  for (let z = -12.25 + CELL; z < 12.25; z += CELL) { maxStep = Math.max(maxStep, Math.abs(col(z) - prev)); prev = col(z); }
  assert.ok(maxStep < 3, 'max step ' + maxStep);
});

test('junction discs at the node height, radius = largest burned halfWidth', () => {
  const g = flat(100);
  const st = burn(g, [seg(1, line(0, 103, 0, 100, 103, 0))], [{ id: 10, x: 0, z: 0, y: 103 }, { id: 11, x: 100, z: 0, y: 103 }]);
  assert.equal(st.nodes, 2);
  assert.equal(g[at(-3.5, 1.75)], 103);
});

test('safety: a segment with a sample more than 30 m from H0 is skipped and counted', () => {
  const g = flat(100);
  const st = burn(g, [seg(1, line(-100, 131, 0, 100, 131, 0)), seg(2, line(-100, 102, 100, 100, 102, 100))]);
  assert.deepEqual([st.segments, st.skipped, st.skippedIds], [1, 1, [1]]);
  assert.equal(g[at(0, 1.75)], 100);
  assert.ok(near(g[at(0, 101.75)], 102));
});

test('wet pixels: b = 3.5 m, blend capped at max(sea, H0 + depth); cores still burn', () => {
  const H0 = flat(100);
  for (let row = 0; row < N; row++) { const z = -SIZE / 2 + (row + 0.5) * CELL; if (z < -8) for (let c = 0; c < N; c++) H0[row * N + c] = 98; }
  const dn = 65, depth = new Float32Array(dn * dn), sp = SIZE / (dn - 1);
  for (let r = 0; r < dn; r++) { const z = -SIZE / 2 + r * sp; if (z < -8 - sp) for (let c = 0; c < dn; c++) depth[r * dn + c] = 2; }
  const water = { seaLevel: 90, depthMeta: { resolution: dn, size: SIZE, heightScale: 1 }, depth, dx: 0, dz: 0 };
  burn(H0, [seg(1, line(-200, 104, 0, 200, 104, 0))], [], water);
  const h = (z) => H0[at(0, z)];
  assert.equal(h(-5.25), 104);
  for (let z = -40.25; z < -15; z += CELL) assert.ok(h(z) <= 100 + 1e-4, 'capped at z ' + z);
  assert.equal(h(-29.75), 98);
  assert.ok(h(12.25) > 100);
});

test('belowSeaLevelPixels counts core pixels under the sea level', () => {
  const st = burn(flat(102), [seg(1, line(-100, 100, 0, 100, 100, 0))], [], { seaLevel: 101, depthMeta: null, depth: null, dx: 0, dz: 0 });
  assert.ok(st.belowSeaLevelPixels > 0);
});

test('adjustWaterSources: a source on a changed pixel keeps its absolute level (> 1 mm, at least 1 m)', () => {
  const natural = flat(100), burned = flat(100);
  burned[at(10.25, 20.25)] = 104; burned[at(-10.25, 20.25)] = 100.0005; burned[at(50.75, 50.75)] = 112;
  const src = [{ x: 10.25, z: 20.25, height: 10 }, { x: -10.25, z: 20.25, height: 10 }, { x: 50.75, z: 50.75, height: 5 }];
  assert.equal(adjustWaterSources(src, natural, burned, N, SIZE), 2);
  assert.deepEqual(src.map(s => s.height), [6, 10, 1]);
});
