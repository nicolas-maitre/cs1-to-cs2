// Outside connections for the imported crop: where the CS1 network leaves the crop, the imported end node becomes a
// CS2 outside connection. TypeScript port of terrain/lib/connections.mjs (highways → road connections, train
// tracks → train connections; other roads only with {allRoads: true}).

import type { Cs1City, NetInfo, Plan } from '../types.ts';

export function connectionType(info: NetInfo | undefined, { allRoads = false } = {}): 'road' | 'train' | null {
  if (!info) return null;
  const ai = info.ai || '';
  if (info.service === 'PublicTransport' && info.subService === 'PublicTransportTrain' && /^TrainTrack/.test(ai)) return 'train';
  if (info.service === 'Road' && /^Road/.test(ai)) {
    if (info.className === 'Highway') return 'road';
    if (allRoads) return 'road';
  }
  return null;
}

export interface OutsideConnection { node: number; x: number; z: number; type: 'road' | 'train'; info: string; cs1Segments: number[]; outward: [number, number] }

export function deriveConnections(city: Pick<Cs1City, 'nodes' | 'segments' | 'netInfos'>, plan: Pick<Plan, 'nodes' | 'crop'>, opts: { allRoads?: boolean } = {}) {
  const info = new Map(city.netInfos.map(n => [n.name, n]));
  const nodes = new Map(city.nodes.map(n => [n.id, n]));
  const planNodes = new Map(plan.nodes.map(n => [n.id, n]));
  const { centerX, centerZ, size } = plan.crop;
  const h = size / 2;
  const inside = (n: { position: number[] }) => Math.abs(n.position[0] - centerX) <= h && Math.abs(n.position[2] - centerZ) <= h;
  const byKey = new Map<string, OutsideConnection>();
  const skipped: { cs1Segment: number; node: number; info: string; reason: string }[] = [];
  const counts = { crossing: 0, typed: 0, connections: 0, road: 0, train: 0 };
  for (const s of city.segments) {
    const a = nodes.get(s.startNode), b = nodes.get(s.endNode);
    if (!a || !b) continue;
    const ia = inside(a), ib = inside(b);
    if (ia === ib) continue;
    counts.crossing++;
    const type = connectionType(info.get(s.info), opts);
    if (!type) continue;
    counts.typed++;
    const inner = ia ? a : b, outer = ia ? b : a;
    const pn = planNodes.get(inner.id);
    if (!pn) { skipped.push({ cs1Segment: s.id, node: inner.id, info: s.info, reason: 'inner node not imported' }); continue; }
    const key = inner.id + ':' + type;
    const e = byKey.get(key);
    if (e) { e.cs1Segments.push(s.id); continue; }
    const dx = outer.position[0] - inner.position[0], dz = outer.position[2] - inner.position[2], len = Math.hypot(dx, dz) || 1;
    byKey.set(key, { node: inner.id, x: pn.x, z: pn.z, type, info: s.info, cs1Segments: [s.id], outward: [round3(dx / len), round3(dz / len)] });
  }
  const connections = [...byKey.values()];
  counts.connections = connections.length;
  counts.road = connections.filter(c => c.type === 'road').length;
  counts.train = connections.filter(c => c.type === 'train').length;
  return { connections, skipped, counts };
}

function round3(v: number) { return Math.round(v * 1000) / 1000; }
