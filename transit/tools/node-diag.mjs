#!/usr/bin/env node
// Load a save and dump node.at for the link joints of a tracklink run.   node transit/tools/node-diag.mjs <save> <run summary.json> [out]
import { readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const [save, summary, outFile = '.gamectl/tracklink/node-diag.json'] = process.argv.slice(2);
const S = JSON.parse(readFileSync(summary, 'utf8'));
const pts = [];
const only = process.argv[5]; for (const st of S.depots.link.stations) { if (only && !st.prefab.includes(only)) continue; for (const l of st.links) if (l.link) { pts.push(l.link.fromPos, l.link.toPos); } }
const body = JSON.stringify({ session: 'terrain worker (transit/ node diag)', since: new Date().toISOString() }) + '\n';
for (;;) { try { writeFileSync(LOCK, body, { flag: 'wx' }); const st = await status('cs2').catch(() => null); if (!st?.running) break; unlinkSync(LOCK); } catch (e) { if (e.code !== 'EEXIST') throw e; } await new Promise(r => setTimeout(r, 10e3)); }
let out;
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  out = await b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args: { op: 'node.at', points: pts.slice(0, 12), r: 2 }, timeout: 300 });
} catch (e) { out = { error: e.message }; }
finally { writeFileSync(outFile, JSON.stringify(out, null, 1)); try { await stop('cs2', { wait: 90 }); } catch {} if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) unlinkSync(LOCK); }
