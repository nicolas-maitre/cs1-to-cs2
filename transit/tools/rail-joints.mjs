#!/usr/bin/env node
// Rail round (2026-11-22): on a loaded save (never saved), the track joints at given points (lanes.joint, no yards)
// and the edges around them (edges.at).   node transit/tools/rail-joints.mjs <save> "x,z;x,z" [r=40] [out]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, pts, rs = '40', outFile = '.gamectl/tracklink/rail-joints.json'] = process.argv.slice(2);
const points = pts.split(';').map(p => p.split(',').map(Number));
const SESSION = 'terrain worker (transit/tools/rail-joints.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = { save, points };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  out.edges = await x({ op: 'edges.at', points, r: Number(rs) });
  // every node within 12 m of each point (lanes.joint takes the nearest within 3 m, so sample a small grid)
  const grid = [];
  for (const [px, pz] of points) for (let dx = -12; dx <= 12; dx += 3) for (let dz = -12; dz <= 12; dz += 3) grid.push([px + dx, pz + dz]);
  out.joints = await x({ op: 'lanes.joint', points: grid, yards: false });
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
