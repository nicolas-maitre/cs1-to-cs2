import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deriveConnections, connectionType } from '../lib/connections.mjs';

const infos = {
  netInfos: [
    { name: 'Highway', service: 'Road', subService: 'None', className: 'Highway', ai: 'RoadAI' },
    { name: 'Highway Elevated', service: 'Road', subService: 'None', className: 'Highway', ai: 'RoadBridgeAI' },
    { name: 'Basic Road', service: 'Road', subService: 'None', className: 'Small Road', ai: 'RoadAI' },
    { name: 'Train Track', service: 'PublicTransport', subService: 'PublicTransportTrain', className: 'Train Track', ai: 'TrainTrackAI' },
    { name: 'Train Line', service: 'PublicTransport', subService: 'PublicTransportTrain', className: 'Train Line', ai: 'TransportLineAI' },
    { name: 'Water Pipe', service: 'Water', subService: 'None', className: 'Water Pipe', ai: 'WaterPipeAI' },
  ],
};

test('connectionType: highways and train tracks only, unless allRoads', () => {
  const by = n => infos.netInfos.find(i => i.name === n);
  assert.equal(connectionType(by('Highway')), 'road');
  assert.equal(connectionType(by('Highway Elevated')), 'road');
  assert.equal(connectionType(by('Train Track')), 'train');
  assert.equal(connectionType(by('Train Line')), null);
  assert.equal(connectionType(by('Basic Road')), null);
  assert.equal(connectionType(by('Basic Road'), { allRoads: true }), 'road');
  assert.equal(connectionType(by('Water Pipe'), { allRoads: true }), null);
  assert.equal(connectionType(undefined), null);
});

test('deriveConnections: imported inner end of each crossing highway/track, deduplicated, in CS2 coordinates', () => {
  // crop centre (1000, 0), size 200: inside is x 900..1100, z -100..100. Transform dx = -1000.
  const P = (id, x, z) => ({ id, position: [x, 50, z] });
  const network = {
    nodes: [P(1, 1050, 0), P(2, 1300, 0), P(3, 950, 50), P(4, 950, 400), P(5, 1000, -50), P(6, 1000, -500), P(7, 1000, 0), P(8, 1080, 0), P(9, 1500, 30)],
    segments: [
      { id: 10, info: 'Highway', startNode: 1, endNode: 2 },      // crosses east → road at node 1
      { id: 11, info: 'Highway', startNode: 9, endNode: 1 },      // also crosses from node 1 → same connection
      { id: 12, info: 'Train Track', startNode: 4, endNode: 3 },  // crosses north → train at node 3
      { id: 13, info: 'Basic Road', startNode: 5, endNode: 6 },   // small road: not a connection by default
      { id: 14, info: 'Highway', startNode: 7, endNode: 8 },      // fully inside
      { id: 15, info: 'Water Pipe', startNode: 5, endNode: 6 },
      { id: 16, info: 'Highway', startNode: 6, endNode: 5 },      // crosses, but node 5 is not imported
    ],
  };
  const plan = {
    crop: { centerX: 1000, centerZ: 0, size: 200 }, transform: { dx: -1000, dz: 0 },
    nodes: [{ id: 1, x: 50, z: 0 }, { id: 3, x: -50, z: 50 }, { id: 7, x: 0, z: 0 }, { id: 8, x: 80, z: 0 }],
  };
  const r = deriveConnections({ network, netinfos: infos }, plan);
  assert.equal(r.counts.crossing, 6);
  assert.deepEqual(r.connections.map(c => [c.node, c.type, c.x, c.z]), [[1, 'road', 50, 0], [3, 'train', -50, 50]]);
  assert.deepEqual(r.connections[0].cs1Segments, [10, 11]);
  assert.deepEqual(r.connections[0].outward, [1, 0]);
  assert.deepEqual(r.connections[1].outward, [0, 1]);
  assert.equal(r.skipped.length, 1);
  assert.equal(r.skipped[0].reason, 'inner node not imported');
  const all = deriveConnections({ network, netinfos: infos }, { ...plan, nodes: [...plan.nodes, { id: 5, x: 0, z: -50 }] }, { allRoads: true });
  assert.deepEqual(all.connections.map(c => [c.node, c.type]).sort(), [[1, 'road'], [3, 'train'], [5, 'road']]);
});
