import { test } from 'node:test';
import assert from 'node:assert/strict';
import { convertWater, depthCheckpoints } from '../lib/water.mjs';

test('convertWater: natural sources inside the playable area, height above ground, sea level as is', () => {
  const water = {
    seaLevel: 40,
    sources: [
      { id: 1, type: 'natural', outputPosition: [1000, 120, 500], targetHeight: 130, terrainHeightAtOutput: 120, inputRate: 0, outputRate: 5000, pollution: 0 },
      { id: 2, type: 'facility', outputPosition: [1100, 100, 500], targetHeight: 110, terrainHeightAtOutput: 100 },
      { id: 3, type: 'natural', outputPosition: [9000, 80, 0], targetHeight: 90, terrainHeightAtOutput: 80 },   // CS2 x = 8000: outside
      { id: 4, type: 'natural', outputPosition: [0, 200, 0], targetHeight: 199, terrainHeightAtOutput: 200, pollution: 500 },
    ],
  };
  const plan = { transform: { dx: -1000, dz: 0 } };
  const r = convertWater(water, plan);
  assert.equal(r.seaLevel, 40);
  assert.deepEqual(r.stats.byType, { natural: 3, facility: 1 });
  assert.equal(r.stats.outside, 1);
  assert.deepEqual(r.sources.map(s => [s.cs1.id, s.x, s.z, s.height, s.polluted]), [[1, 0, 500, 10, 0], [4, -1000, 0, 1, 0.5]]);
});

test('depthCheckpoints: wet and dry samples in CS2 space', () => {
  const res = 5, size = 400; // spacing 100 m, CS1 -200..200
  const raw = Buffer.alloc(res * res * 2);
  raw.writeUInt16LE(640, (2 * res + 2) * 2); // centre: 10 m deep
  raw.writeUInt16LE(16, (2 * res + 3) * 2);  // 0.25 m: neither wet nor dry
  const r = depthCheckpoints({ resolution: res, size, heightScale: 1 / 64 }, raw, { transform: { dx: 50, dz: -50 } }, 100, 100);
  assert.deepEqual(r.wet, [{ x: 50, z: -50, cs1Depth: 10 }]);
  assert.equal(r.totals.dry, 23);
});

test('regionSources: inflow at the playable edge, lakes without a source, sea and sourced regions skipped', async () => {
  const { regionSources } = await import('../lib/water.mjs');
  // CS1 grid 1081² over 17 280 m (16 m), transform 0: CS2 playable edge at ±7168 → margin 50 → ±7118, edge band 300
  const res = 1081, sp = 16, sc = 1 / 64;
  const raw = Buffer.alloc(res * res * 2), heights = new Float32Array(res * res).fill(100);
  const cell = (x, z) => Math.round((z + 8640) / sp) * res + Math.round((x + 8640) / sp);
  const wet = (x, z, d, ground = 100) => { const i = cell(x, z); raw.writeUInt16LE(Math.round(d / sc), i * 2); heights[i] = ground; };
  for (let k = 0; k < 6; k++) wet(-1000 + k * 16, 0, 2 + k);            // lake, 6 cells, deepest 7 m at x = -920
  for (let k = 0; k < 6; k++) wet(7000 - k * 16, 500, 3);               // river reaching the east edge band
  for (let k = 0; k < 6; k++) wet(2000 + k * 16, 2000, 4);              // lake with an existing source
  for (let k = 0; k < 6; k++) wet(3000 + k * 16, -3000, 10, 72);        // sea: surface 82 ≈ sea level 80
  for (let k = 0; k < 2; k++) wet(-3000 + k * 16, -3000, 5);            // too small
  const r = regionSources({ resolution: res, size: 17280, heightScale: sc }, raw, { heights }, { transform: { dx: 0, dz: 0 } }, 80, [{ x: 2040, z: 2000 }]);
  const kinds = r.sources.map(s => [s.cs1.kind, s.x, s.z, s.height]).sort();
  // positions snap to the 16 m CS1 grid; equal-depth river cells: the first in scan order
  assert.deepEqual(kinds, [['inflow', 6928, 496, 3], ['lake', -912, 0, 7]]);
  assert.deepEqual(r.regions.map(x => x.kind).sort(), ['has-source', 'inflow', 'lake']);
});
