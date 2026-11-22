#!/usr/bin/env node
// Metro no-path diagnostic (2026-11-22): on a loaded save (never saved), for one line: lines.debug, stop positions,
// edge reach and directed lane reach between its waypoints, the subway edges around each stop (station-owned or
// imported, elevation) and the track joints at the station platform ends.
//   node transit/tools/metro-diag.mjs <save> ["Metro Line 3"] [out]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, line = 'Metro Line 3', outFile = '.gamectl/tracklink/metro-diag.json'] = process.argv.slice(2);
const SESSION = 'terrain worker (transit/tools/metro-diag.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = { save, line };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  out.debug = await x({ op: 'lines.debug', name: line });
  out.stopPos = await x({ op: 'lines.stoppos', name: line });
  out.edgeReach = await x({ op: 'lines.reach', name: line });
  out.laneReach = await x({ op: 'lanes.reach', name: line, track: 'Subway' });
  const pts = (out.stopPos ?? []).map((p) => [p[0], p[2]]);
  out.edges = pts.length ? await x({ op: 'edges.at', points: pts, r: 150 }) : [];
  // platform ends: end nodes of station-owned subway edges near the stops
  const ends = new Map();
  for (const p of out.edges) for (const e of p.edges) if (e.track && e.owner && /Subway/.test(e.prefab)) for (const q of [e.start, e.end]) ends.set(q.map(Math.round).join(','), q);
  out.platformEnds = [...ends.values()];
  const grid = [];
  for (const q of out.platformEnds) for (let dx = -2; dx <= 2; dx += 2) for (let dz = -2; dz <= 2; dz += 2) grid.push([q[0] + dx, q[2] + dz]);
  out.joints = grid.length ? await x({ op: 'lanes.joint', points: grid, yards: false }) : [];
  out.stations = await x({ op: 'stops.list', type: 'Subway' }).catch((e) => ({ error: e.message }));
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
