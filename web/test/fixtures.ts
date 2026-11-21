// Synthetic CS1 data shared by the tests (same road as spikes/m0-roadbuilder-configs/imt-lanes.test.mjs).
import type { Bezier, Cs1City, Cs1Node, Cs1Segment, ImtFiller, NetInfo, Vec3 } from '../src/core/types.ts';

export const lane = (p: number, w: number, t: string, v: string, d: string, trees: string[] = []) => ({
  fields: { m_position: p, m_width: w, m_laneType: t, m_vehicleType: v, m_direction: d, m_speedLimit: 1 },
  props: trees.map(tree => ({ prop: null, tree, finalProp: null, finalTree: tree })),
});

export const fiveLane: NetInfo = {
  name: 'Test Five-Lane', title: 'Test Five-Lane', ai: 'RoadAI', service: 'Road',
  fields: { m_halfWidth: 16, m_pavementWidth: 6.5 },
  aiFields: { m_trafficLights: true, m_enableZoning: true },
  lanes: [lane(-14, 4, 'Pedestrian', 'None', 'Both'), lane(14, 4, 'Pedestrian', 'None', 'Both'),
    ...[-6, -3, 0, 3, 6].map(p => lane(p, 3, 'Vehicle', 'Car', 'Forward')),
    lane(-8.5, 2, 'Parking', 'Car', 'Forward'), lane(8.5, 2, 'Parking', 'Car', 'Forward')],
};

export const straight = (id: number, info: string, x0: number, x1: number, z: number, inverted: boolean, startNode = id * 10, endNode = id * 10 + 1): Cs1Segment => ({
  id, info, inverted, length: Math.abs(x1 - x0), startNode, endNode, elevationKind: 'ground',
  bezier: [0, 1 / 3, 2 / 3, 1].map(t => [x0 + (x1 - x0) * t, 0, z]) as Bezier,
});

export const rect = (x0: number, x1: number, z0: number, z1: number): Vec3[] => [[x0, 0, z0], [x1, 0, z0], [x1, 0, z1], [x0, 0, z1]];

export const fill = (markingType: string, markingId: number, styleType: string, color: number[], polygon: Vec3[]): ImtFiller =>
  ({ markingType, markingId, styleClass: styleType + 'Style', styleType, color, polygon });

/** IMT-like inset fill z-range over lane p (width w) of a +X segment at z = base (position p lies at z = base − p). */
export const laneZ = (base: number, p: number, w: number): [number, number] => [base - p - w / 2 + 0.3, base - p + w / 2 - 0.3];

export const node = (id: number, x: number, z: number, extra: Partial<Cs1Node> = {}): Cs1Node =>
  ({ id, info: 'x', position: [x, 0, z], heightAboveTerrain: 0, elevation: 0, ...extra });

export function city(parts: Partial<Cs1City>): Cs1City {
  return {
    source: 'cs1x',
    manifest: { format: 'cs1x', formatVersion: 1, city: { name: 'Test', saveName: 'Test.01' }, exportedAt: '2026-11-21T00:00:00Z', mapSize: 17280 },
    nodes: [], segments: [], netInfos: [], imtFillers: [], terrain: null, report: [], sha256: null,
    ...parts,
  };
}
