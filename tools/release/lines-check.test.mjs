// node --test tools/release/lines-check.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { evaluateLines, lineKind } from './lines-check.mjs';

test('a bus line with no moving vehicle fails; parked buses do not count', () => {
  const r = evaluateLines(
    [{ name: 'Bus Line 1', prefab: 'Bus Line', movingVehicles: 0 }, { name: 'Bus Line 2', prefab: 'Bus Line', movingVehicles: 3 }],
    { Bus01: 3 },
  );
  assert.equal(r.ok, false);
  assert.deepEqual(r.idle, ['Bus Line 1 (Bus Line)']);
  assert.equal(r.summary[0], 'Bus lines with vehicles: 1 of 2 (3 running)');
  assert.equal(r.summary.at(-1), 'Moving vehicles: 3 bus');
});

test('rail lines are reported, never failed, while rail is opt-in', () => {
  const r = evaluateLines([{ name: 'Metro Line 3', prefab: 'Subway Line', movingVehicles: 0 }, { name: 'Bus Line 2', prefab: 'Bus Line', movingVehicles: 1 }], {});
  assert.equal(r.ok, true);
  assert.equal(r.summary.find((s) => s.startsWith('Metro')), 'Metro lines with vehicles: 0 of 1 (0 running), reported only');
});

test('trams must run too; a city without lines passes', () => {
  assert.equal(evaluateLines([{ name: 'T1', prefab: 'Tram Line', movingVehicles: 0 }], {}).ok, false);
  assert.equal(evaluateLines([], {}).ok, true);
});

test('an old transit library without movingVehicles is an error, not a pass', () => {
  const r = evaluateLines([{ name: 'Bus Line 1', prefab: 'Bus Line', vehicles: 5 }], {});
  assert.equal(r.ok, false);
  assert.match(r.error, /movingVehicles/);
});

test('line kinds', () => {
  assert.equal(lineKind('Train Line'), 'train');
  assert.equal(lineKind('Subway Line'), 'metro');
  assert.equal(lineKind('Bus Line'), 'bus');
});

test('idle bus lines while every bus depot is out of vehicles are a capacity limit, not a failure', () => {
  const lines = [{ name: 'Bus Line 2', prefab: 'Bus Line', movingVehicles: 0 }, { name: 'Bus Line 12', prefab: 'Bus Line', movingVehicles: 9 }];
  const full = evaluateLines(lines, { Bus01: 23 }, [{ prefab: 'BusDepot01', available: 0, ownedVehicles: 25 }, { prefab: 'Road Outside Connection - Oneway', available: 40, ownedVehicles: 10 }]);
  assert.equal(full.ok, true);
  assert.deepEqual(full.capacityLimited, ['Bus Line 2 (Bus Line)']);
  assert.match(full.summary.at(-1), /Bus depots out of vehicles \(25 owned, 0 available in 1 depot\(s\)\)/);
  // a depot with buses left and an idle line: a real failure
  const spare = evaluateLines(lines, {}, [{ prefab: 'BusDepot01', available: 5, ownedVehicles: 25 }]);
  assert.equal(spare.ok, false);
  // no depot at all: failure
  assert.equal(evaluateLines(lines, {}, []).ok, false);
});
