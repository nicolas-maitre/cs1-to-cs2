import { test } from 'node:test';
import assert from 'node:assert/strict';
import { transitJobs, bezierAt } from '../lib/plan.mjs';

test('bezierAt: straight bezier gives the linear point and a constant tangent', () => {
  const b = [[0, 0, 0], [10, 0, 0], [20, 0, 0], [30, 0, 0]];
  const { p, d } = bezierAt(b, 0.5);
  assert.deepEqual(p.map(v => Math.round(v * 1000) / 1000), [15, 0, 0]);
  assert.ok(d[0] > 0 && d[2] === 0);
});

test('transitJobs: shared kerbside stops, plan-frame direction, station lines kept as positions', () => {
  const plan = {
    segments: [
      { id: 1, bezier: [[0, 0, 0], [0, 0, 10], [0, 0, 20], [0, 0, 30]] },     // runs north
      { id: 2, bezier: [[0, 0, 30], [10, 0, 30], [20, 0, 30], [30, 0, 30]] }, // runs east
    ],
    transit: {
      lines: [
        { cs1Id: 6, name: 'Bus Line 5', number: 5, line: 'Bus Line', stopPrefab: 'Bus Stop', color: '#05fe00', vehicles: 5, requiresStations: false,
          stops: [{ cs1Node: 1, x: 9, z: 15, segment: 1, side: 'right', t: 0.5 }, { cs1Node: 2, x: 15, z: 21, segment: 2, side: 'left', t: 0.5 }, { cs1Node: 3, segment: 99, side: 'right', t: 0.5 }] },
        { cs1Id: 7, name: 'Bus Line 6', number: 6, line: 'Bus Line', stopPrefab: 'Bus Stop', color: '#ff0000', requiresStations: false,
          stops: [{ cs1Node: 1, x: 9, z: 15, segment: 1, side: 'right', t: 0.5 }, { cs1Node: 4, x: 0, z: 0, segment: 1, side: 'left', t: 0.1 }] },
        { cs1Id: 8, name: 'Metro Line 3', number: 3, line: 'Subway Line', stopPrefab: null, color: '#ea00ff', requiresStations: true,
          stops: [{ cs1Node: 9, x: 100, z: 200, segment: null, side: 'left', t: 0.5, cs1Building: 77 }, { cs1Node: 10, x: 300, z: 200, cs1Building: 78 }] },
      ],
    },
  };
  const j = transitJobs(plan);
  assert.deepEqual(j.stats, { lines: 3, bus: 2, station: 1, kerbStops: 3, sharedStops: 1 });
  assert.deepEqual(j.stops.map(s => [s.key, s.x, s.z, s.tx, s.tz, s.side]), [
    ['1:right:0.5', 0, 15, 0, 1, 'right'], ['2:left:0.5', 15, 30, 1, 0, 'left'], ['1:left:0.1', 0, 3, 0, 1, 'left'],
  ]);
  assert.deepEqual(j.lines[0].stops.map(s => s.key), ['1:right:0.5', '2:left:0.5']);
  assert.deepEqual(j.lines[1].stops.map(s => s.key), ['1:right:0.5', '1:left:0.1']);
  assert.equal(j.skipped.length, 1);
  assert.deepEqual(j.lines[2], { key: 'cs1-8', name: 'Metro Line 3', number: 3, prefab: 'Subway Line', color: '#ea00ff', requiresStations: true, vehicles: null, truncated: false,
    stops: [{ x: 100, z: 200, cs1Building: 77 }, { x: 300, z: 200, cs1Building: 78 }] });
  assert.equal(transitJobs(plan, { only: ['Metro Line 3'] }).lines.length, 1);
  assert.deepEqual(transitJobs({ transit: null }).lines, []);
});

test('stop prefabs: generic plan names map to the CS2 theme prefabs', () => {
  const plan = { segments: [{ id: 1, bezier: [[0, 0, 0], [0, 0, 10], [0, 0, 20], [0, 0, 30]] }],
    transit: { lines: [{ cs1Id: 1, name: 'L', line: 'Bus Line', stopPrefab: 'Bus Stop', stops: [{ segment: 1, side: 'right', t: 0.5 }] }] } };
  assert.equal(transitJobs(plan).stops[0].prefab, 'EU_BusStop01');
  assert.equal(transitJobs(plan, { theme: 'NA' }).stops[0].prefab, 'NA_BusStop01');
});
