#!/usr/bin/env node
// Step-by-step transit diagnostic: every step's result is written to disk before the next runs, so a native crash
// shows where it happened.   node transit/tools/diag.mjs <plan> [save] [steps=stops3,metro,bus1]
import { readFileSync, writeFileSync, existsSync, unlinkSync, appendFileSync } from 'node:fs';
import { join } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { transitJobs } from '../lib/plan.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';

const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const SESSION = 'terrain worker (transit/tools/diag.mjs)', OWNER = SESSION;
const plan = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const save = process.argv[3] || 'cs1conv-m3-buildings';
const steps = (process.argv[4] || 'stops3,metro,bus1').split(',');
const logf = join(process.cwd(), '.gamectl', 'transit', 'diag.log');
writeFileSync(logf, '');
const log = (k, v) => { appendFileSync(logf, k + ' ' + JSON.stringify(v).slice(0, 3000) + '\n'); console.error(k); };
const body = JSON.stringify({ session: 'terrain worker (transit/ folder)', since: new Date().toISOString(), purpose: 'transit diagnostic' }) + '\n';
await holdCs2(OWNER, SESSION);
const j = transitJobs(plan);
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  log('start', 'ok');
  for (const s of steps) {
    log('begin', s);
    if (s === 'stops3') {
      log('stops3', await x({ op: 'stops.create', stops: j.stops.slice(0, 3) }, { phase: 'tool' }));
      await b.wait(30);
      log('stops3.list', await x({ op: 'stops.list', type: 'Bus' }));
    } else if (s === 'stopsAll') {
      log('stopsAll', (await x({ op: 'stops.create', stops: j.stops }, { phase: 'tool' })).results.filter(r => r.error).length);
      await b.wait(30);
      log('stopsAll.list', (await x({ op: 'stops.list', type: 'Bus' })).length);
    } else if (s === 'metro') {
      const l = j.lines.filter(l => l.requiresStations);
      log('metro', await x({ op: 'lines.create', lines: l, frames: 40 }, { phase: 'tool', pump: 'tool' }));
      log('metro.list', await x({ op: 'lines.list' }));
    } else if (s === 'bus1') {
      const l = j.lines.filter(l => !l.requiresStations).slice(0, 1);
      const keys = new Set(l[0].stops.map(s => s.key));
      log('bus1.stops', (await x({ op: 'stops.create', stops: j.stops.filter(s => keys.has(s.key)) }, { phase: 'tool' })).results.filter(r => r.error));
      await b.wait(30);
      log('bus1', await x({ op: 'lines.create', lines: l, frames: 40 }, { phase: 'tool', pump: 'tool' }));
      log('bus1.list', await x({ op: 'lines.list' }));
    }
    log('end', s);
  }
} catch (e) { log('error', e.message); }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  await freeCs2(OWNER);
}
