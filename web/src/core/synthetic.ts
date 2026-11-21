// Synthetic CS1 city: an n × n street grid (two road types, a highway ring, curved segments), for
// performance checks of the UI (`?synthetic=n`) and the package builder on cities much larger than
// Skibidi. 2·n·(n−1) + ring segments.

import type { Bezier, Cs1City, Cs1Node, Cs1Segment, NetInfo } from './types.ts';

const lane = (p: number, w: number, t: string, v: string, d: string) => ({ fields: { m_position: p, m_width: w, m_laneType: t, m_vehicleType: v, m_direction: d, m_speedLimit: 1 }, props: [] });

export const BASIC: NetInfo = {
  name: 'Basic Road', title: 'Two-Lane Road', ai: 'RoadAI', service: 'Road', parent: null, fields: { m_halfWidth: 8, m_pavementWidth: 3 }, aiFields: { m_trafficLights: false },
  lanes: [lane(-6.5, 3, 'Pedestrian', 'None', 'Both'), lane(6.5, 3, 'Pedestrian', 'None', 'Both'), lane(-4, 2, 'Parking', 'Car', 'Backward'), lane(4, 2, 'Parking', 'Car', 'Forward'), lane(-1.5, 3, 'Vehicle', 'Car', 'Backward'), lane(1.5, 3, 'Vehicle', 'Car', 'Forward')],
};
export const MEDIUM: NetInfo = {
  name: 'Medium Road', title: 'Four-Lane Road', ai: 'RoadAI', service: 'Road', parent: null, fields: { m_halfWidth: 16, m_pavementWidth: 4.8 }, aiFields: { m_trafficLights: true },
  lanes: [lane(-13.6, 4.8, 'Pedestrian', 'None', 'Both'), lane(13.6, 4.8, 'Pedestrian', 'None', 'Both'), ...[-7.5, -4.5].map(p => lane(p, 3, 'Vehicle', 'Car', 'Backward')), ...[4.5, 7.5].map(p => lane(p, 3, 'Vehicle', 'Car', 'Forward'))],
};
export const HIGHWAY: NetInfo = {
  name: 'Highway', title: 'Highway', ai: 'RoadAI', service: 'Road', parent: null, fields: { m_halfWidth: 11, m_pavementWidth: 2 }, aiFields: { m_highwayRules: true, m_enableZoning: false },
  lanes: [-4, 0, 4].map(p => lane(p, 4, 'Vehicle', 'Car', 'Forward')),
};

export function syntheticCity(n: number, spacing = 90): Cs1City {
  const nodes: Cs1Node[] = [];
  const segments: Cs1Segment[] = [];
  const off = -((n - 1) * spacing) / 2;
  const id = (i: number, j: number) => i * n + j + 1;
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    // a gentle wave so segments are real curves
    const x = off + i * spacing + Math.sin(j * 0.3) * 6, z = off + j * spacing + Math.cos(i * 0.3) * 6;
    nodes.push({ id: id(i, j), info: BASIC.name, position: [x, 100, z], heightAboveTerrain: 0, elevation: 0, segments: [] });
  }
  let sid = 1;
  const seg = (a: Cs1Node, b: Cs1Node, info: NetInfo, kind = 'ground'): Cs1Segment => {
    const [x0, , z0] = a.position, [x1, , z1] = b.position;
    const bend = ((sid % 7) - 3) * 2;
    const nx = -(z1 - z0), nz = x1 - x0, l = Math.hypot(nx, nz) || 1;
    const bez: Bezier = [[x0, 100, z0], [x0 + (x1 - x0) / 3 + (nx / l) * bend, 100, z0 + (z1 - z0) / 3 + (nz / l) * bend], [x0 + (2 * (x1 - x0)) / 3 + (nx / l) * bend, 100, z0 + (2 * (z1 - z0)) / 3 + (nz / l) * bend], [x1, 100, z1]];
    return { id: sid++, info: info.name, startNode: a.id, endNode: b.id, bezier: bez, length: Math.hypot(x1 - x0, z1 - z0), inverted: sid % 5 === 0, elevationKind: kind, flags: 'Created' };
  };
  const byId = (k: number) => nodes[k - 1];
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    const main = i % 8 === 0 || j % 8 === 0;
    if (i + 1 < n) segments.push(seg(byId(id(i, j)), byId(id(i + 1, j)), j % 8 === 0 ? MEDIUM : BASIC));
    if (j + 1 < n) segments.push(seg(byId(id(i, j)), byId(id(i, j + 1)), main && i % 8 === 0 ? MEDIUM : BASIC));
  }
  // highway ring outside the grid
  const r = (n * spacing) / 2 + 300, ring: Cs1Node[] = [];
  const k = Math.max(16, Math.round((2 * Math.PI * r) / 120));
  for (let i = 0; i < k; i++) {
    const a = (i / k) * 2 * Math.PI;
    const node: Cs1Node = { id: n * n + 1 + i, info: HIGHWAY.name, position: [Math.cos(a) * r, 100, Math.sin(a) * r], heightAboveTerrain: 0, elevation: 0, segments: [] };
    nodes.push(node); ring.push(node);
  }
  for (let i = 0; i < k; i++) segments.push(seg(ring[i], ring[(i + 1) % k], HIGHWAY));
  // two districts over the grid (an L-shaped "Old Town" and a rectangle "Harbour"), on the CS1 district raster
  const res = 900, cs = 19.2, half = (res * cs) / 2, districtGrid = new Uint8Array(res * res);
  const ext = (n * spacing) / 2;
  const cell = (v: number) => Math.floor((v + half) / cs);
  for (let r = cell(-ext); r < cell(0); r++) for (let c = cell(-ext); c < cell(0); c++) districtGrid[r * res + c] = 1;
  for (let r = cell(0); r < cell(ext / 2); r++) for (let c = cell(-ext); c < cell(-ext / 2); c++) districtGrid[r * res + c] = 1;
  for (let r = cell(ext / 4); r < cell(ext); r++) for (let c = cell(ext / 4); c < cell(ext); c++) districtGrid[r * res + c] = 2;
  return {
    source: 'cs1x',
    manifest: { format: 'cs1x', formatVersion: 1, city: { name: `Synthetic ${n}×${n}`, saveName: 'synthetic' }, mapSize: 17280, sections: { network: 'ok', netinfos: 'ok', imt: 'absent', terrain: 'absent', districts: 'ok' } },
    nodes, segments, netInfos: [BASIC, MEDIUM, HIGHWAY], imtFillers: [], terrain: null, report: [], sha256: null,
    districts: {
      grid: { resolution: res, cellSize: cs, size: res * cs }, districtGrid,
      city: { policies: ['Recycling'] },
      districts: [
        { id: 1, name: 'Old Town', policies: ['HeavyTrafficBan', 'Tourist'], nameLocation: [-ext / 2, 0, -ext / 2] },
        { id: 2, name: 'Harbour', policies: ['CombustionEngineBan'], nameLocation: [(5 * ext) / 8, 0, (5 * ext) / 8] },
      ],
      parks: [],
    },
  };
}
