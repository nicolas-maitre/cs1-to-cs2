#!/usr/bin/env node
// Bus dispatch experiment (2026-11-22): on a loaded save (never saved), reset the line requests' fail counts, create one
// fresh 2-stop test line between the first two stops of an imported line (transit pipeline, after the depot exists),
// simulate, and dump buses + depots.   node transit/tools/bus-dispatch.mjs <save> [line="Bus Line 12"] [seconds=120] [out]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, lineName = 'Bus Line 12', secs = '120', outFile = '.gamectl/tracklink/bus-dispatch.json'] = process.argv.slice(2);
const SESSION = 'terrain worker (transit/tools/bus-dispatch.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = { save, lineName };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  out.reset = await x({ op: 'requests.reset' });
  const pos = await x({ op: 'lines.stoppos', name: lineName });
  out.stopPositions = pos.slice(0, 3);
  if (pos.length >= 2) {
    const line = { key: 'test-dispatch', name: 'Test Dispatch Line', prefab: 'Bus Line', color: '#ff00ff', stops: [{ x: pos[0][0], z: pos[0][2] }, { x: pos[1][0], z: pos[1][2] }] };
    out.create = await x({ op: 'lines.create', lines: [line], frames: 40, radius: 4 }, { phase: 'tool', pump: 'tool' });
    await b.wait(30);
    out.name = await x({ op: 'lines.name', routes: (out.create.lines ?? []).filter(l => l.route).map(l => ({ route: l.route, name: 'Test Dispatch Line', number: 99 })) });
  }
  await b.call('sim.set', { paused: false, speed: 3 });
  await b.wait(undefined, Number(secs), { timeout: 900 });
  await b.pause();
  out.buses = await x({ op: 'buses', match: 'Bus' });
  out.depots = await x({ op: 'depots.check' });
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
