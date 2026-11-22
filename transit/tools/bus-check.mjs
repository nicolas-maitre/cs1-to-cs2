#!/usr/bin/env node
// Are the imported bus lines served? Loads a save (never saves), simulates, then dumps the Transit op `buses`: every
// bus (parked or moving, state, CurrentRoute, Target, owner, passengers) and per line its waypoints, waiting passengers
// and vehicles.   node transit/tools/bus-check.mjs <save> [seconds=120] [out.json] [match=Bus]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, secs = '120', outFile = '.gamectl/tracklink/bus-check.json', match = 'Bus'] = process.argv.slice(2);
const SESSION = 'terrain worker (transit/tools/bus-check.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = { save, seconds: Number(secs) };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 });
  const x = (args) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600 });
  out.before = (await x({ op: 'buses', match })).byState;
  await b.call('sim.set', { paused: false, speed: 3 });
  await b.wait(undefined, Number(secs), { timeout: 900 });
  await b.pause();
  out.after = await x({ op: 'buses', match });
  out.depots = await x({ op: 'depots.check' });
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
