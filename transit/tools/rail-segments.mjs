#!/usr/bin/env node
// Rail lines per segment (2026-11-22): load each save in turn (never saved) and list every train / metro line's route
// segments with their path state and element count (lines.debug), so "a path on every segment" can be checked.
//   node transit/tools/rail-segments.mjs <out.json> <save> [<save> ...]
import { writeFileSync } from 'node:fs';
import { start, stop } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [outFile, ...saves] = process.argv.slice(2);
const SESSION = 'terrain worker (transit/tools/rail-segments.mjs)', OWNER = SESSION;
await holdCs2(OWNER, SESSION, { log: (m) => console.error(m) });
const out = {};
try {
  const { bridge: b } = await start('cs2', { load: saves[0], timeout: 400 });
  for (const save of saves) {
    await b.load(save, { timeout: 900 }); await b.pause();
    const x = (args) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600 });
    const lines = (await x({ op: 'lines.list' })).filter((l) => /Train|Subway/.test(l.prefab));
    out[save] = [];
    for (const l of lines) {
      const d = (await x({ op: 'lines.debug', name: l.name }))[0];
      out[save].push({ name: l.name, prefab: l.prefab, moving: l.movingVehicles, segments: (d?.segments ?? []).map((s) => ({ state: s.state, elements: s.elements ?? 0 })) });
    }
  }
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  console.log(outFile);
}
