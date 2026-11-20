// Synthetic test for the IMT → lane override rules (the Skibidi.02 dump has no segment-level
// pavement/grass islands, so the median rule can only be exercised here).
//   node imt-lanes.test.mjs
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { laneCoverage, wholeLaneOverrides } from './imt-lanes.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const lane = (p, w, t, v, d) => ({ fields: { m_position: p, m_width: w, m_laneType: t, m_vehicleType: v, m_direction: d, m_speedLimit: 1 }, props: [] });
const info = {
  name: 'Test Five-Lane', title: 'Test Five-Lane', ai: 'RoadAI', service: 'Road',
  fields: { m_halfWidth: 16, m_pavementWidth: 6.5 },
  aiFields: { m_trafficLights: true, m_enableZoning: true },
  lanes: [lane(-14, 4, 'Pedestrian', 'None', 'Both'), lane(14, 4, 'Pedestrian', 'None', 'Both'),
    ...[-6, -3, 0, 3, 6].map(p => lane(p, 3, 'Vehicle', 'Car', 'Forward')),
    lane(-8.5, 2, 'Parking', 'Car', 'Forward'), lane(8.5, 2, 'Parking', 'Car', 'Forward')],
};
// Segment 1 runs +X at z = 0; segment 2 is the same road drawn the other way with Invert set,
// at z = 100. In both, NetInfo position p lies at world z = base − p.
const straight = (id, x0, x1, z, inverted) => ({ id, info: info.name, inverted, length: Math.abs(x1 - x0),
  bezier: [0, 1 / 3, 2 / 3, 1].map(t => [x0 + (x1 - x0) * t, 0, z]) });
const rect = (x0, x1, z0, z1) => [[x0, 0, z0], [x1, 0, z0], [x1, 0, z1], [x0, 0, z1]];
const fill = (markingType, markingId, styleType, color, polygon) => ({ markingType, markingId, styleClass: styleType + 'Style', styleType, color, polygon });
const laneZ = (base, p, w) => [base - p - w / 2 + 0.3, base - p + w / 2 - 0.3]; // IMT-like inset

function dumpWith(fills) {
  return { netInfos: [info], segments: [straight(1, 0, 60, 0, false), straight(2, 60, 0, 100, true)], nodes: [], imtFillers: fills };
}
const slotsOf = i => i.lanes.map(l => l.fields).filter(f => /Vehicle|Parking/.test(f.m_laneType))
  .map(f => ({ pos: f.m_position, width: f.m_width, kind: /Parking/.test(f.m_laneType) ? 'parking' : 'car' }));

// 1. pavement island over lane +3 on both segments → median; inversion handled
{
  const [a, b] = laneZ(0, 3, 3), [c, d] = laneZ(100, 3, 3);
  const ov = wholeLaneOverrides(laneCoverage(dumpWith([
    fill('Segment', 1, 'FillerPavement', [136, 136, 136, 224], rect(12, 48, a, b)),
    fill('Segment', 2, 'FillerGrass', [80, 120, 60, 255], rect(12, 48, c, d)),
  ]), slotsOf));
  assert.deepEqual([...ov.get(1)], [[3, 'median']]);
  assert.deepEqual([...ov.get(2)], [[3, 'median-grass']]);
}
// 2. red paint over parking +8.5 and car +6 → both covered (generator then merges into one bus lane)
{
  const ov = wholeLaneOverrides(laneCoverage(dumpWith([fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, -9.2, -4.8))]), slotsOf));
  assert.deepEqual([...ov.get(1)].sort(), [[6, 'bus'], [8.5, 'bus']]);
}
// 3. partial / diagonal / foreign / node / grey fills → no override
{
  const tri = [[10, 0, -1.2], [10, 0, 1.2], [50, 0, 1.2]]; // taper: full lane 0 at x = 10, nothing at x = 50
  const ov = wholeLaneOverrides(laneCoverage(dumpWith([
    fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], tri),
    fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, -6.0, -4.6)), // half of lane 6 only
    fill('Segment', 2, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, ...laneZ(0, 0, 3))), // belongs to segment 2, lies on 1
    fill('Node', 7, 'FillerSolid', [136, 0, 0, 94], rect(-5, 60, ...laneZ(0, -6, 3))),
    fill('Segment', 1, 'FillerSolid', [136, 136, 136, 224], rect(10, 50, ...laneZ(0, -3, 3))), // grey paint
  ]), slotsOf));
  assert.equal(ov.size, 0, JSON.stringify([...ov]));
}
// 4. end to end through the generator: median + merged bus lane in the RB config
{
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'imt-test-'));
  const [a, b] = laneZ(0, -3, 3);
  fs.writeFileSync(path.join(dir, 'dump.json'), JSON.stringify(dumpWith([
    fill('Segment', 1, 'FillerPavement', [136, 136, 136, 224], rect(12, 48, a, b)),
    fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, -9.2, -4.8)),
  ])));
  const reports = JSON.parse(execFileSync(process.execPath, [path.join(here, 'wq-to-rb.mjs'), path.join(dir, 'dump.json'), '--out-imt', path.join(dir, 'out')], { encoding: 'utf8' }));
  assert.equal(reports.length, 1);
  const lanes = reports[0].rbLanes.map(l => l.split(' ')[0]);
  assert.deepEqual(lanes, ['Sidewalk←', 'Car→', 'Median→', 'Car→', 'Car→', 'Bus→', 'Sidewalk→'], reports[0].rbLanes.join('\n'));
  assert.match(reports[0].rbLanes.at(-1), /"Parking":""/); // right parking absorbed by the bus paint
  assert.match(reports[0].rbLanes[0], /"Parking":"P"/);    // left parking untouched
  fs.rmSync(dir, { recursive: true });
}
console.log('imt-lanes: all tests passed');
