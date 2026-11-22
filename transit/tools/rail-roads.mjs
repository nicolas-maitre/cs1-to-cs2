#!/usr/bin/env node
// #161 round 2: does the mod's rail step (tracklink) change imported roads? Loads a save made WITHOUT the rail step,
// lists the edges at the given points, runs tracklink with the mod's parameters (plus overrides), lists them again.
//   node transit/tools/rail-roads.mjs <save> '<points json [[x,z],...]>' '<tracklink args json>' [out.json]
// Holds the CS2 lock (waits for another session's), stops CS2 and releases the lock at the end. Never saves.
import { writeFileSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
import { holdCs2, freeCs2 } from '../../terrain/lib/cs2lock.mjs';
const [save, pointsJson, argsJson = '{}', outFile = '.gamectl/tracklink/rail-roads.json'] = process.argv.slice(2);
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const SESSION = 'terrain worker (transit/tools/rail-roads.mjs)', OWNER = SESSION;
const body = JSON.stringify({ session: 'terrain worker (transit/tools/rail-roads.mjs)', since: new Date().toISOString() }) + '\n';
await holdCs2(OWNER, SESSION);
const out = { save };
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause(); await b.wait(60);
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  const points = JSON.parse(pointsJson);
  out.before = await x({ op: 'edges.at', points, r: 8 });
  const args = { op: 'tracklink', maxDistance: 2500, tangentLinks: true, yardLinkPrefab: 'yard', avoidRoads: true, ...JSON.parse(argsJson) };
  out.args = args;
  out.link = await x(args, { phase: 'tool', pump: 'tool' });
  await b.wait(120);
  out.after = await x({ op: 'edges.at', points, r: 8 });
} catch (e) { out.error = e.stack || e.message; }
finally {
  try { await stop('cs2'); } catch { /* ignore */ }
  try { await freeCs2(OWNER); } catch { /* ignore */ }
  writeFileSync(outFile, JSON.stringify(out, null, 1));
  const sum = (l) => (l ?? []).map(p => `${JSON.stringify(p.at)}: ` + p.edges.filter(e => e.road).map(e => `${e.prefab.slice(0, 10)} ${e.start.map(Math.round).join(',')}->${e.end.map(Math.round).join(',')}`).join(' | ')).join('\n  ');
  console.log('before\n  ' + sum(out.before) + '\nafter\n  ' + sum(out.after) + (out.error ? '\nERROR ' + out.error : ''));
}
