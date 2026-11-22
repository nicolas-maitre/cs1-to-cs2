#!/usr/bin/env node
// Bus dispatch leads (a) + (b): on a loaded save (never saved), dump the bus depot's spawn locations, rebuild the depot
// the game's way (Temp definition + forced apply) at the same spot, dump again, reset the line requests, simulate, and
// report moving buses per line.   node transit/tools/bus-rebuild.mjs <save> [seconds=150] [out]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, secs = '150', outFile = '.gamectl/tracklink/bus-rebuild.json'] = process.argv.slice(2);
const SESSION = 'terrain worker (transit/tools/bus-rebuild.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = { save };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  out.spawnsBefore = await x({ op: 'depot.spawns' });
  out.rebuild = await x({ op: 'depot.rebuild' }, { phase: 'tool', pump: 'tool' });
  out.spawnsAfter = await x({ op: 'depot.spawns' });
  out.reset = await x({ op: 'requests.reset' });
  await b.call('sim.set', { paused: false, speed: 3 });
  await b.wait(undefined, Number(secs), { timeout: 900 });
  await b.pause();
  out.lines = (await x({ op: 'lines.list' })).map(l => ({ name: l.name, prefab: l.prefab, vehicles: l.vehicles, moving: l.movingVehicles }));
  out.counts = await x({ op: 'counts' });
  out.depots = await x({ op: 'depots.check' });
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
