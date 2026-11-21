// node --test buildings/lib/plan.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { buildBuildingsPlan, closestOnBezier, addDefaultDepots } from './plan.mjs';

const straight = (x0, z0, x1, z1) => [[x0, 0, z0], [x0 + (x1 - x0) / 3, 0, z0 + (z1 - z0) / 3], [x0 + 2 * (x1 - x0) / 3, 0, z0 + 2 * (z1 - z0) / 3], [x1, 0, z1]];

const cs1 = {
  network: { segments: [
    { id: 10, bezier: straight(-100, 0, 100, 0) },   // east-west road through the origin
    { id: 11, bezier: straight(500, -100, 500, 100) }, // a road the plan does not import
  ] },
  assets: { buildings: [
    { name: 'Hospital', ai: 'HospitalAI', service: 'HealthCare', count: 1, footprint: [4, 4], aiFields: { m_patientCapacity: 100 }, tags: [] },
    { name: 'House', ai: 'ResidentialBuildingAI', growable: true, count: 1, footprint: [2, 2], tags: [] },
    { name: 'Park', ai: 'ParkAI', service: 'Beautification', count: 3, footprint: [2, 2], tags: [] },
  ] },
  buildings: { buildings: [
    { id: 1, info: 'Hospital', position: [20, 0, 30], angle: 0, width: 4, length: 4, parent: 0, accessSegment: 10 },  // north of the road
    { id: 2, info: 'House', position: [0, 0, -20], angle: 0, width: 2, length: 2, parent: 0, accessSegment: 10 },
    { id: 3, info: 'Park', position: [510, 0, 0], angle: 0, width: 2, length: 2, parent: 0, accessSegment: 11 },     // road not in plan
    { id: 4, info: 'Park', position: [5000, 0, 0], angle: 0, width: 2, length: 2, parent: 0, accessSegment: 10 },   // outside crop
    { id: 5, info: 'Park', position: [0, 0, 40], angle: 0, width: 2, length: 2, parent: 1, accessSegment: 10 },     // sub-building
  ] },
};
const catalogue = { game: { version: 'test' }, prefabs: [
  { name: 'Clinic01', kind: 'building', placeable: true, lot: [4, 4], components: { Hospital: {} }, capacity: { patients: 100 } },
  { name: 'Park01', kind: 'building', placeable: true, lot: [2, 2], components: { Park: {} } },
] };
const roadPlan = { crop: { centerX: 0, centerZ: 0, size: 2000 }, transform: { dx: 1000, dz: -500 }, segments: [{ id: 10 }] };

test('closestOnBezier finds the foot point on a straight segment', () => {
  const c = closestOnBezier(straight(-100, 0, 100, 0), [20, 30]);
  assert.ok(Math.abs(c.point[0] - 20) < 1e-3 && Math.abs(c.point[1]) < 1e-3);
  assert.ok(Math.abs(c.distance - 30) < 1e-3);
});

test('builds items in plan coordinates, with front point and facing toward the road', () => {
  const p = buildBuildingsPlan(cs1, catalogue, roadPlan);
  assert.equal(p.items.length, 1);
  const h = p.items[0];
  assert.equal(h.prefab, 'Clinic01');
  assert.deepEqual([h.x, h.z], [1020, -470]);          // CS1 (20, 30) + (1000, −500)
  assert.deepEqual(h.front, [1020, -500]);             // foot point on the road, transformed
  assert.deepEqual(h.facing, [0, -1]);                 // from the building towards the road (south)
  assert.equal(h.accessSegment, 10);
});

test('skips are counted by reason', () => {
  const p = buildBuildingsPlan(cs1, catalogue, roadPlan);
  assert.deepEqual(p.stats.skipped, { outsideCrop: 1, subBuilding: 1, growable: 1, skip: 0, noEquivalent: 0, roadNotImported: 1 });
  assert.equal(p.stats.withRoad, 1);
});

test('refuses an export without the M3 sections', () => {
  assert.throws(() => buildBuildingsPlan({ network: cs1.network, buildings: null, assets: null }, catalogue, roadPlan), /exporter 1.2/);
});

test('fallback is written only when chosen, per type or for all', () => {
  assert.equal(buildBuildingsPlan(cs1, catalogue, roadPlan).items[0].fallback, undefined);
  assert.equal(buildBuildingsPlan(cs1, catalogue, roadPlan, { fallback: 'skip' }).items[0].fallback, 'skip');
  assert.equal(buildBuildingsPlan(cs1, catalogue, roadPlan, { fallback: 'skip', fallbackByType: { Hospital: 'nearby' } }).items[0].fallback, 'nearby');
});

test('default depots: one bus/tram depot per type with lines and none matched; no metro/train yards (DECISIONS #83)', () => {
  const transit = { lines: [
    { name: 'Bus 1', cs1Type: 'Bus', stops: [{ x: 10, z: 20 }] },
    { name: 'Tram 1', cs1Type: 'Tram', stops: [{ x: 50, z: 60 }, { x: 70, z: 60 }] },
    { name: 'Tram 2', cs1Type: 'Tram', stops: [{ x: 5, z: 5 }] },
    { name: 'Metro 1', cs1Type: 'Metro', stops: [{ x: 1, z: 1, cs1Building: 7 }] },
    { name: 'Ferry', cs1Type: 'Ship', stops: [{ x: 0, z: 0 }] },
  ] };
  const b = { items: [{ prefab: 'BusDepot01' }], stats: {} };
  const added = addDefaultDepots(b, transit, { prefabs: [{ name: 'TramDepot01', lot: [19, 17], size: [150, 12, 134] }] });
  assert.deepEqual(added.map((d) => d.prefab), ['TramDepot01']); // the bus depot exists; metro yards come from transit/; ships have no mapping
  const depot = b.items.at(-1);
  assert.equal(depot.default, true);
  assert.equal(depot.fallback, 'nearby');
  assert.equal(depot.needsTrackLink, undefined);
  assert.deepEqual([depot.x, depot.z, depot.lot], [50, 60, [19, 17]]); // first stop of the first tram line, lot in cells
  assert.match(depot.reason, /Tram 1, Tram 2/);
  assert.equal(b.stats.defaultDepots, 1);
  assert.deepEqual(addDefaultDepots({ items: [], stats: {} }, { lines: [] }), []);
});
