#!/usr/bin/env node
// Bus Line 2 lead (2026-11-22): does the utility outside-connection stub at the avenue's dead end keep the bus from
// turning? On a loaded save (never saved): edges at the node, rebuild the bus depot the game's way (else no line runs),
// delete the utility nets there, edges again, road.reach, reset requests, simulate, moving vehicles per line.
//   node transit/tools/bus-line2.mjs <save> [x=618] [z=1498] [seconds=150] [out]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, xs = '618', zs = '1498', secs = '150', outFile = '.gamectl/tracklink/bus-line2.json'] = process.argv.slice(2);
const SESSION = 'terrain worker (transit/tools/bus-line2.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const x0 = Number(xs), z0 = Number(zs);
const out = { save, at: [x0, z0] };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  out.edgesBefore = await x({ op: 'edges.at', points: [[x0, z0]], r: 40 });
  out.rebuild = await x({ op: 'depot.rebuild' }, { phase: 'tool', pump: 'tool' });
  out.deleted = await x({ op: 'utility.deleteNear', x: x0, z: z0, r: 60 }, { phase: 'tool', pump: 'tool' });
  await b.wait(60);
  out.edgesAfter = await x({ op: 'edges.at', points: [[x0, z0]], r: 40 });
  const rr = await x({ op: 'road.reach' });
  out.reach = rr.lines.map(l => ({ name: l.name, reach: l.stopToStopReach, fails: l.failingPairs }));
  out.reset = await x({ op: 'requests.reset' });
  await b.call('sim.set', { paused: false, speed: 3 });
  await b.wait(undefined, Number(secs), { timeout: 900 });
  await b.pause();
  out.lines = (await x({ op: 'lines.list' })).map(l => ({ name: l.name, moving: l.movingVehicles }));
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
