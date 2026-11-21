#!/usr/bin/env node
// Diagnostic: load a saved cs1conv-* city, list its water sources and sample the water depth at and around each,
// before and after simulating. Holds the CS2 lock.   node terrain/tools/water-diag.mjs [save] [seconds]
import { readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../apply.mjs';

const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const save = process.argv[2] || 'cs1conv-mapprep-test';
const secs = Number(process.argv[3] || 120);
const body = JSON.stringify({ session: 'terrain worker (terrain/ folder)', since: new Date().toISOString(), purpose: 'water diagnostic' }) + '\n';
for (;;) {
  try { writeFileSync(LOCK, body, { flag: 'wx' }); const st = await status('cs2').catch(() => null); if (!st?.running) break; unlinkSync(LOCK); } catch (e) { if (e.code !== 'EEXIST') throw e; }
  await new Promise(r => setTimeout(r, 10e3));
}
const out = {};
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: args.op.startsWith('water') || args.op === 'counts' ? 'Cs1ConvTerrain.MapPrep' : 'Cs1ConvTerrain.Ops', args, ...o });
  const info = await x({ op: 'info' });
  out.water = { seaLevel: info.water.seaLevel, sources: info.water.sources };
  const pts = [];
  for (const s of info.water.sources) for (const [dx, dz] of [[0, 0], [30, 0], [0, 30], [-60, 0], [0, -60], [120, 0]]) pts.push([s.pos[0] + dx, s.pos[2] + dz]);
  out.before = (await x({ op: 'water.sample', points: pts })).points;
  await b.call('sim.set', { paused: false, speed: 3 });
  await b.wait(undefined, secs, { timeout: secs + 120 });
  await b.pause();
  out.after = (await x({ op: 'water.sample', points: pts })).points;
  out.terrain = (await x({ op: 'sample', points: pts })).points;
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) unlinkSync(LOCK);
  writeFileSync(join(process.cwd(), '.gamectl', 'mapprep', 'water-diag.json'), JSON.stringify(out, null, 2));
  console.log(JSON.stringify(out).slice(0, 4000));
}
