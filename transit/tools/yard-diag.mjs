#!/usr/bin/env node
// Rail round 5 (2026-11-22): why does no train leave the linked yard? On a loaded save (never saved): Train Line 1's
// lane reach from the yard, the link's joints (found from the yard-link edges along the course), the depots' dispatch
// state and requests, the yard's spawn locations and trains; then reset the requests, simulate, and look again.
//   node transit/tools/yard-diag.mjs <save> "x0,z0;x1,z1" (link course ends) [seconds=150] [out]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, ends, secs = '150', outFile = '.gamectl/tracklink/yard-diag.json'] = process.argv.slice(2);
const [[x0, z0], [x1, z1]] = ends.split(';').map((p) => p.split(',').map(Number));
const SESSION = 'terrain worker (transit/tools/yard-diag.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = { save, ends: [[x0, z0], [x1, z1]] };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  out.laneReach = await x({ op: 'lanes.reach', name: 'Train Line 1', track: 'Train' });
  // the link's own edges (the yard's Twoway Train Track, unowned) along the straight course, and their nodes
  const pts = []; for (let i = 0; i <= 30; i++) pts.push([x0 + (x1 - x0) * i / 30, z0 + (z1 - z0) * i / 30]);
  const along = await x({ op: 'edges.at', points: pts, r: 40 });
  const link = new Map();
  for (const p of along) for (const e of p.edges) if (e.track && !e.owner) link.set(e.edge, e);
  out.courseEdges = [...link.values()];
  const nodes = new Map();
  for (const e of out.courseEdges) if (/Twoway Train Track/.test(e.prefab)) for (const [p, k] of [[e.start, e.startEdges], [e.end, e.endEdges]]) nodes.set(p.map(Math.round).join(','), { pos: p, edges: k });
  out.linkNodes = [...nodes.values()];
  const grid = [];
  for (const n of out.linkNodes) for (let dx = -2; dx <= 2; dx += 2) for (let dz = -2; dz <= 2; dz += 2) grid.push([n.pos[0] + dx, n.pos[2] + dz]);
  out.joints = grid.length ? await x({ op: 'lanes.joint', points: grid, yards: true }) : [];
  out.depots = await x({ op: 'depots.check' });
  out.spawns = await x({ op: 'depot.spawns' }).catch((e) => ({ error: e.message }));
  out.trains = await x({ op: 'vehicles', match: 'Train' }).catch((e) => ({ error: e.message }));
  out.reset = await x({ op: 'requests.reset' });
  await b.call('sim.set', { paused: false, speed: 3 });
  await b.wait(undefined, Number(secs), { timeout: 900 });
  await b.pause();
  out.after = {
    lines: (await x({ op: 'lines.list' })).filter((l) => /Train/.test(l.prefab)).map((l) => ({ name: l.name, vehicles: l.vehicles, moving: l.movingVehicles })),
    depots: await x({ op: 'depots.check' }),
    trains: await x({ op: 'vehicles', match: 'Train' }).catch((e) => ({ error: e.message })),
  };
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
