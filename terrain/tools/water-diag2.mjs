#!/usr/bin/env node
// Diagnostic: does MapPrep water.set create sources? Load a save, set one source (with and without Deleted on the
// definition), list sources a few frames later.   node terrain/tools/water-diag2.mjs [save]
import { readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../apply.mjs';
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const save = process.argv[2] || 'cs1conv-mapprep-test';
const body = JSON.stringify({ session: 'terrain worker (terrain/ folder)', since: new Date().toISOString(), purpose: 'water diagnostic 2' }) + '\n';
for (;;) {
  try { writeFileSync(LOCK, body, { flag: 'wx' }); const st = await status('cs2').catch(() => null); if (!st?.running) break; unlinkSync(LOCK); } catch (e) { if (e.code !== 'EEXIST') throw e; }
  await new Promise(r => setTimeout(r, 10e3));
}
const out = {};
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.pause();
  const mp = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTerrain.MapPrep', args, ...o });
  const info = async () => (await b.exec(DLL, { type: 'Cs1ConvTerrain.Ops', args: { op: 'info' } })).water;
  out.start = await info();
  out.setA = await mp({ op: 'water.set', clear: false, sources: [{ x: -2790, z: 690, height: 30, radius: 80 }] }, { phase: 'tool' });
  await b.wait(30); out.afterA = await info();
  out.setB = await mp({ op: 'water.set', clear: false, keepDefinitions: true, sources: [{ x: -2854, z: 1474, height: 48, radius: 80 }] }, { phase: 'tool' });
  await b.wait(30); out.afterB = await info();
  out.setC = await mp({ op: 'water.set', clear: false, sources: [{ x: -2700, z: 300, height: 30, radius: 80 }] });
  await b.wait(30); out.afterC = await info();
  await b.call('sim.set', { paused: false, speed: 3 });
  await b.wait(undefined, 60, { timeout: 200 });
  await b.pause();
  const pts = (out.afterC.sources || []).map(s => [s.pos[0], s.pos[2]]);
  out.depth = (await mp({ op: 'water.sample', points: pts })).points;
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2', { wait: 90 }); } catch { /* ignore */ }
  if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) unlinkSync(LOCK);
  writeFileSync(join(process.cwd(), '.gamectl', 'mapprep', 'water-diag2.json'), JSON.stringify(out, null, 2));
  console.log(JSON.stringify(out).slice(0, 3000));
}
