// node --test buildings/lib/objects.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { rasterPolygons, buildDistrictsSection, buildObjectsSection, buildSurfacesSection } from './objects.mjs';

const roadPlan = { crop: { centerX: 0, centerZ: 0, size: 2000 }, transform: { dx: 100, dz: -100 } };

test('rasterPolygons: an L shape becomes one 6-corner counter-clockwise polygon', () => {
  const res = 8;
  const r = new Uint8Array(res * res);
  for (let z = 1; z <= 2; z++) for (let x = 1; x <= 4; x++) r[z * res + x] = 7; // bar along x
  for (let z = 3; z <= 5; z++) r[z * res + 1] = 7;                              // leg up at x = 1
  const [p] = rasterPolygons(r, res);
  assert.equal(p.id, 7);
  assert.equal(p.cells, 11);
  assert.equal(p.polygon.length, 6);
  const area = p.polygon.reduce((s, a, i) => { const b = p.polygon[(i + 1) % p.polygon.length]; return s + a[0] * b[1] - b[0] * a[1]; }, 0) / 2;
  assert.equal(area, 11, 'counter-clockwise, area = cell count');
});

test('rasterPolygons: separate pieces and holes', () => {
  const res = 10;
  const r = new Uint8Array(res * res);
  for (let z = 0; z < 5; z++) for (let x = 0; x < 5; x++) if (!(x === 2 && z === 2)) r[z * res + x] = 1; // ring with a hole
  r[8 * res + 8] = 1;                                                                                     // a separate cell
  const ps = rasterPolygons(r, res).sort((a, b) => b.cells - a.cells);
  assert.equal(ps.length, 2);
  assert.equal(ps[0].cells, 24);
  assert.equal(ps[0].holes, 1);
  assert.equal(ps[0].polygon.length, 4, 'outer square only');
});

test('districts: crop, transform, names, policy mapping', () => {
  const res = 100, cellSize = 20; // 2000 m grid centred on 0
  const raster = new Uint8Array(res * res);
  for (let z = 50; z < 55; z++) for (let x = 50; x < 60; x++) raster[z * res + x] = 3;
  raster[10 * res + 10] = 4; // a 1-cell speck: dropped
  const s = buildDistrictsSection({
    districts: { grid: { resolution: res, cellSize }, districts: [{ id: 3, name: 'Old Town', policies: ['HeavyTrafficBan', 'HighriseBan'] }, { id: 4, name: 'X' }], parks: [{ id: 1, name: 'Zoo', kind: 'park', type: 'Zoo' }] },
    districtRaster: raster,
  }, roadPlan);
  assert.equal(s.items.length, 1);
  const d = s.items[0];
  assert.equal(d.name, 'Old Town');
  assert.deepEqual(d.policies, ['Heavy Traffic Ban']);
  assert.deepEqual(s.stats.skipped.unmappedPolicies, { HighriseBan: 1 });
  assert.equal(s.stats.skipped.small, 1);
  assert.ok(d.polygon.some((p) => p[0] === 100 && p[1] === -100), 'corner (50,50) → world (0,0) → plan (+100, −100)');
  assert.equal(d.polygon.length, 4);
  assert.equal(s.parksNotConverted.length, 1);
});

test('objects: crop filter, mapping, deterministic adult ages', () => {
  const tree = (name, height) => ({ name, kind: 'object', uiGroup: 'Vegetation', height, components: { TreeObject: {} }, flags: [] });
  const catalogue = { prefabs: [tree('OakTree01', 15), tree('GreenBushWild01', 2.4)] };
  const cs1 = {
    assets: { trees: [{ name: 'Beech01low', title: 'Small Beech', height: 15.5 }, { name: 'Bush01', title: 'Small Bush', height: 1.3 }], props: [{ name: 'Bench' }] },
    trees: { infos: ['Beech01low', 'Bush01'], trees: [[0, 10, 0, 20, 0], [1, 5000, 0, 0, 0], [1, -30, 0, 40, 0]] },
    props: { infos: ['Bench'], props: [[0, 0, 0, 0, 90, 0]] },
  };
  const a = buildObjectsSection(cs1, catalogue, roadPlan);
  const b = buildObjectsSection(cs1, catalogue, roadPlan);
  assert.deepEqual(a, b);
  assert.deepEqual(a.prefabs, ['OakTree01', 'GreenBushWild01']);
  assert.equal(a.items.length, 2);
  assert.deepEqual(a.items[0].slice(0, 3), [0, 110, -80]);
  assert.ok(a.items.every((r) => r[4] >= 0.3 && r[4] <= 0.55));
  assert.deepEqual(a.stats.skipped, { outsideCrop: 1, noEquivalent: 1 });
});

test('surfaces: node fills only, unmapped styles reported', () => {
  const imt = { fillers: [
    { markingType: 'Node', markingId: 5, styleType: 'FillerPavement', color: [136, 136, 136, 224], polygon: [[0, 0, 0], [4, 0, 0], [4, 0, 4]] },
    { markingType: 'Node', markingId: 6, styleType: 'FillerSolid', color: [136, 0, 0, 94], polygon: [[0, 0, 0], [4, 0, 0], [4, 0, 4]] },
    { markingType: 'Segment', markingId: 7, styleType: 'FillerSolid', color: [136, 0, 0, 94], polygon: [[0, 0, 0], [4, 0, 0], [4, 0, 4]] },
  ] };
  const s = buildSurfacesSection(imt, roadPlan, { surfaceMap: { FillerPavement: 'Pavement Surface' } });
  assert.equal(s.items.length, 1);
  assert.deepEqual(s.items[0].polygon[1], [104, -100]);
  assert.deepEqual(s.notConverted, { 'FillerSolid rgba(136,0,0,94)': 1 });
});
