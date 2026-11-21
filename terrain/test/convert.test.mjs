import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { readZip, writeZip } from '../lib/zip.mjs';
import { encodeGray16, decodeGray16 } from '../lib/png.mjs';
import { CS2, loadCs1xTerrain, convert, sampleCs2, sampleCs1, chooseHeightMapping, cs2PixelCenter, flipRows } from '../lib/heightmap.mjs';
import { buildSyntheticCs1x, syntheticHeight, SYNTHETIC_NODES } from '../lib/synthetic.mjs';
import { main } from '../convert.mjs';

const QUANT = 1024 / 65535; // one CS2 step with the default (absolute) mapping

function planeCs1x(res, f) { return buildSyntheticCs1x({ res, heightFn: f, nodes: [] }); }

test('zip: stored and deflated entries round-trip, CRC checked', () => {
  const big = Buffer.alloc(5000, 7);
  const z = writeZip([{ name: 'a.json', data: '{"x":1}' }, { name: 'b.bin', data: big, store: true }, { name: 'c.bin', data: big }]);
  const e = readZip(z);
  assert.deepEqual(JSON.parse(e.get('a.json').read()), { x: 1 });
  assert.ok(e.get('b.bin').read().equals(big));
  assert.equal(e.get('c.bin').method, 8);
  assert.ok(e.get('c.bin').read().equals(big));
  const zz = Buffer.from(z); const off = zz.indexOf(Buffer.alloc(16, 7)); zz[off] = 8;
  assert.throws(() => readZip(zz).get('b.bin').read(), /CRC/);
});

test('png: 16-bit greyscale round trip', () => {
  const w = 37, h = 11, v = new Uint16Array(w * h);
  for (let i = 0; i < v.length; i++) v[i] = (i * 2654435761) & 0xffff;
  const d = decodeGray16(encodeGray16(w, h, v));
  assert.equal(d.width, w); assert.equal(d.height, h);
  assert.deepEqual(Array.from(d.values), Array.from(v));
});

test('a tilted plane survives exactly (bilinear is exact on planes), orientation and crop transform included', () => {
  const f = (x, z) => 300 + 0.01 * x - 0.02 * z; // different x and z slopes: a flip or transpose would show
  const { meta, heights } = loadCs1xTerrain(planeCs1x(217, f));
  const crop = { centerX: 1000, centerZ: -500, size: 4000 };
  const { raw, meta: m } = convert(meta, heights, crop);
  assert.equal(raw.length, CS2.resolution ** 2);
  assert.deepEqual(m.transform, { dx: -1000, dz: 500 });
  assert.equal(m.heightScale, 1024); assert.equal(m.heightOffset, 0);
  let worst = 0;
  for (const [x2, z2] of [[0, 0], [1234.5, -777], [-7000, 7000], [7100, -7100], [-3, 4]]) {
    const expected = f(x2 + 1000, z2 - 500); // CS1 = CS2 - (dx, dz)
    worst = Math.max(worst, Math.abs(sampleCs2(m, raw, x2, z2) - expected));
  }
  assert.ok(worst <= QUANT + 1e-3, 'worst error ' + worst);
  // row 0 is south: the first row must be at z = -7168 + 1.75 (CS2), i.e. CS1 z = -7166.25 - 500
  const [x00, z00] = cs2PixelCenter(0, 0);
  assert.deepEqual([x00, z00], [-7166.25, -7166.25]);
  assert.ok(Math.abs(raw[0] * 1024 / 65535 - f(x00 + 1000, z00 - 500)) <= QUANT);
  assert.equal(m.stats.clampedFraction, 0);
});

test('synthetic features land where they should (cone peak, trench, plateau)', () => {
  const { meta, heights } = loadCs1xTerrain(buildSyntheticCs1x({ res: 1081 }));
  const crop = { centerX: 500, centerZ: 1000, size: 6000 };
  const { raw, meta: m } = convert(meta, heights, crop);
  const at = (x1, z1) => sampleCs2(m, raw, x1 - 500, z1 - 1000);
  // CS1 grid is 16 m, so features are smoothed at that scale; compare against the CS1 grid sample, not the formula
  for (const [x, z] of SYNTHETIC_NODES) {
    if (Math.abs(x - 500) > 7000 || Math.abs(z - 1000) > 7000) continue;
    // exact on smooth ground; at a kink (trench bottom, cone tip) the second interpolation on 3.5 m pixels rounds
    // the corner by up to slope × cell / 2 ≈ 0.16 × 1.75 m
    assert.ok(Math.abs(at(x, z) - sampleCs1(meta, heights, x, z)) < 0.3, `node ${x},${z}: ${at(x, z)} vs ${sampleCs1(meta, heights, x, z)}`);
    assert.ok(Math.abs(at(x, z) - syntheticHeight(x, z)) < 3, `formula ${x},${z}`);
  }
  assert.ok(at(1500, 1000) > at(1500, 400) + 50, 'cone peak above its flank');
  assert.ok(at(-1200, 0) < at(-900, 0) - 20, 'trench below its edge');
  assert.ok(Math.abs(at(-2000, 2500) - 230) < 0.5, 'plateau at 230 m');
});

test('outside the CS1 map the edge value is used and counted', () => {
  const f = (x, z) => 200 + 0.005 * x;
  const { meta, heights } = loadCs1xTerrain(planeCs1x(109, f));
  const { raw, meta: m } = convert(meta, heights, { centerX: 7000, centerZ: 0, size: 2000 });
  assert.ok(m.stats.clampedFraction > 0.3 && m.stats.clampedFraction < 0.45, String(m.stats.clampedFraction));
  // CS2 x = 7000 → CS1 x = 14000, beyond +8640: clamped to f(8640)
  assert.ok(Math.abs(sampleCs2(m, raw, 7000, 0) - f(8640, 0)) <= QUANT + 1e-3);
});

test('world map: 57 344 m around the playable map, same data and mapping, edge-clamped', () => {
  const f = (x, z) => 250 + 0.01 * x + 0.005 * z;
  const { meta, heights } = loadCs1xTerrain(planeCs1x(109, f));
  const { raw, worldRaw, meta: m } = convert(meta, heights, { centerX: 1000, centerZ: 0, size: 3000 }, { world: true });
  assert.equal(m.world.size, CS2.worldSize); assert.equal(m.world.cellSize, 14);
  const wm = { ...m, size: m.world.size, resolution: m.world.resolution };
  // inside the CS1 map the world map is the same plane (CS1 = CS2 + 1000 in x)
  for (const [x, z] of [[0, 0], [5000, -3000], [-7000, 6000]]) {
    assert.ok(Math.abs(sampleCs2(wm, worldRaw, x, z) - f(x + 1000, z)) <= QUANT + 1e-3, `${x},${z}`);
    assert.ok(Math.abs(sampleCs2(wm, worldRaw, x, z) - sampleCs2(m, raw, x, z)) <= 2 * QUANT + 1e-3);
  }
  // far outside: clamped to the CS1 map edge (x1 = 8640, z1 = -8640)
  assert.ok(Math.abs(sampleCs2(wm, worldRaw, 25000, -25000) - f(8640, -8640)) <= QUANT + 1e-3);
  assert.ok(m.world.stats.clampedFraction > 0.85);
});

test('height mapping modes', () => {
  assert.deepEqual(chooseHeightMapping(40, 600), { mode: 'absolute', heightScale: 1024, heightOffset: 0 });
  assert.deepEqual(chooseHeightMapping(40.2, 612.7, { mode: 'fit' }), { mode: 'fit', heightScale: 593, heightOffset: 30 });
  assert.deepEqual(chooseHeightMapping(100, 120, { mode: 'fit' }), { mode: 'fit', heightScale: 200, heightOffset: 90 });
  assert.deepEqual(chooseHeightMapping(0, 1, { heightScale: 4096 }), { mode: 'explicit', heightScale: 4096, heightOffset: 0 });
  const f = (x, z) => 400 + 0.01 * z;
  const { meta, heights } = loadCs1xTerrain(planeCs1x(109, f));
  const { raw, meta: m } = convert(meta, heights, { centerX: 0, centerZ: 0, size: 1000 }, { mode: 'fit' });
  assert.ok(m.heightOffset < m.stats.min && m.heightOffset + m.heightScale > m.stats.max);
  assert.ok(Math.abs(sampleCs2(m, raw, 0, 3000) - f(0, 3000)) < m.heightScale / 65535 + 1e-3);
  assert.equal(m.stats.saturatedPixels, 0);
});

test('CLI writes r16, png and metadata that agree with each other', () => {
  const dir = mkdtempSync(join(tmpdir(), 'terrain-test-'));
  try {
    const cs1x = join(dir, 'syn.cs1x');
    writeFileSync(cs1x, buildSyntheticCs1x({ res: 271 }));
    const out = join(dir, 'out');
    assert.equal(main(['--cs1x', cs1x, '--crop', '200,-100,5000', '--out', out]), 0);
    const m = JSON.parse(readFileSync(join(out, 'terrain-cs2.json'), 'utf8'));
    assert.equal(m.resolution, 4096); assert.equal(m.size, 14336); assert.equal(m.png, 'heightmap.png');
    const r16 = readFileSync(join(out, 'heightmap.r16'));
    assert.equal(r16.length, 4096 * 4096 * 2);
    const raw = new Uint16Array(r16.buffer, r16.byteOffset, 4096 * 4096);
    const png = decodeGray16(readFileSync(join(out, 'heightmap.png')));
    assert.deepEqual([png.width, png.height], [4096, 4096]);
    // PNG top row = north = last r16 row
    assert.deepEqual(Array.from(png.values.subarray(0, 4096)), Array.from(raw.subarray(4095 * 4096)));
    assert.deepEqual(Array.from(flipRows(png.values, 4096).subarray(0, 64)), Array.from(raw.subarray(0, 64)));
    // plan input gives the same result
    const plan = join(dir, 'plan.json');
    writeFileSync(plan, JSON.stringify({ crop: { centerX: 200, centerZ: -100, size: 5000 }, transform: { dx: -200, dz: 100, heightMode: 'terrain-relative' } }));
    const out2 = join(dir, 'out2');
    assert.equal(main(['--cs1x', cs1x, '--plan', plan, '--out', out2, '--no-png']), 0);
    assert.ok(readFileSync(join(out2, 'heightmap.r16')).equals(r16));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
