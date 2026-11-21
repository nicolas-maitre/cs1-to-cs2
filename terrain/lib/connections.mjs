// Outside connections for the imported crop: where the CS1 network leaves the crop, the imported end node becomes a
// CS2 outside connection (terrain/NOTES-cs2-terrain.md §7). Highways → road connections, train tracks → train
// connections; other roads only with {allRoads: true}.
import { readZip } from './zip.mjs';

/** CS1 NetInfo → connection type, or null. */
export function connectionType(info, { allRoads = false } = {}) {
  if (!info) return null;
  const ai = info.ai || '';
  if (info.service === 'PublicTransport' && info.subService === 'PublicTransportTrain' && /^TrainTrack/.test(ai)) return 'train';
  if (info.service === 'Road' && /^Road/.test(ai)) {
    if (info.className === 'Highway') return 'road';
    if (allRoads) return 'road';
  }
  return null;
}

/**
 * cs1x: {network, netinfos} (parsed) or a .cs1x Buffer; plan: parsed cs1conv-plan.json.
 * Returns {connections: [{node, x, z, type, cs1Segment, info, outward:[dx,dz]}], skipped: [...], counts}.
 * x, z are CS2 coordinates (the plan node's). One connection per (node, type).
 */
export function deriveConnections(cs1x, plan, opts = {}) {
  let network = cs1x.network, netinfos = cs1x.netinfos;
  if (Buffer.isBuffer(cs1x)) {
    const z = readZip(cs1x);
    network = JSON.parse(z.get('network.json').read().toString('utf8'));
    netinfos = JSON.parse(z.get('netinfos.json').read().toString('utf8'));
  }
  const info = new Map((netinfos.netInfos || []).map(n => [n.name, n]));
  const nodes = new Map(network.nodes.map(n => [n.id, n]));
  const planNodes = new Map(plan.nodes.map(n => [n.id, n]));
  const { centerX, centerZ, size } = plan.crop;
  const h = size / 2;
  const inside = n => Math.abs(n.position[0] - centerX) <= h && Math.abs(n.position[2] - centerZ) <= h;

  const byKey = new Map();
  const skipped = [];
  const counts = { crossing: 0, typed: 0, connections: 0 };
  for (const s of network.segments) {
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
    if (byKey.has(key)) { byKey.get(key).cs1Segments.push(s.id); continue; }
    const dx = outer.position[0] - inner.position[0], dz = outer.position[2] - inner.position[2], len = Math.hypot(dx, dz) || 1;
    byKey.set(key, { node: inner.id, x: pn.x, z: pn.z, type, info: s.info, cs1Segments: [s.id], outward: [round3(dx / len), round3(dz / len)] });
  }
  const connections = [...byKey.values()];
  counts.connections = connections.length;
  counts.road = connections.filter(c => c.type === 'road').length;
  counts.train = connections.filter(c => c.type === 'train').length;
  return { connections, skipped, counts };
}

function round3(v) { return Math.round(v * 1000) / 1000; }
