#!/usr/bin/env node
// Utility outside connections on a base map: what a fresh city has, and what MapPrep `clear` leaves (docs/QA.md §9).
//   node terrain/tools/oc-probe.mjs [--map "Corral Riches"] [--all] [--out .gamectl/mapprep/oc-probe.json] [--keep-running]
// Holds the shared cs2.lock (waits for another session's lock or game); starts CS2, new city, lists the outside-
// connection marker prefabs and the city's outside connections, runs `clear` ({all} as the importer mod's Prepare map
// does with --all), lists them again, saves cs1conv-ocprobe, quits CS2.
import { writeFileSync, existsSync, unlinkSync, mkdirSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../apply.mjs';
import { holdCs2, freeCs2 } from '../lib/cs2lock.mjs';

const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const flag = (n) => argv.includes(n);
const map = opt('--map', 'Corral Riches');
const outFile = resolve(opt('--out', '.gamectl/mapprep/oc-probe.json'));
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const SESSION = 'terrain worker (terrain/tools/oc-probe.mjs)', OWNER = SESSION;
const body = JSON.stringify({ session: 'terrain worker (terrain/tools/oc-probe.mjs)', since: new Date().toISOString() }) + '\n';
await holdCs2(OWNER, SESSION);
const out = { map, started: new Date().toISOString() };
const op = (b, args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTerrain.MapPrep', args, timeout: 600, ...o });
const summarize = (list) => ({
  total: list.length,
  electricity: list.filter(o => o.electricity).length, waterPipe: list.filter(o => o.waterPipe).length,
  byPrefab: list.reduce((t, o) => (t[o.prefab] = (t[o.prefab] || 0) + 1, t), {}),
  utility: list.filter(o => o.electricity || o.waterPipe).slice(0, 20),
});
try {
  const { bridge: b } = await start('cs2', { timeout: 400 });
  out.city = await b.newGame(map, { name: 'cs1conv-ocprobe' });
  await b.pause();
  await b.wait(120);
  out.prefabs = (await op(b, { op: 'oc.prefabs' })).prefabs.filter(p => p.utility || /Electric|Water|Sewage|Power/i.test(p.name));
  out.before = summarize(await op(b, { op: 'oc.list' }));
  out.clear = await op(b, { op: 'clear', frames: 10, all: flag('--all') }, { phase: 'tool', pump: 'tool' });
  await b.wait(120);
  out.after = summarize(await op(b, { op: 'oc.list' }));
  await b.saveWrite('cs1conv-ocprobe').catch(e => { out.saveError = e.message; });
  if (!flag('--keep-running')) await stop('cs2');
} catch (e) { out.error = e.stack || e.message; }
finally {
  if (!flag('--keep-running')) try { await freeCs2(OWNER); } catch { /* ignore */ }
  mkdirSync(dirname(outFile), { recursive: true });
  writeFileSync(outFile, JSON.stringify(out, null, 2) + '\n');
  console.log(outFile);
}
