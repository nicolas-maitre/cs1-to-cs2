#!/usr/bin/env node
// Load a save and dump lines.debug for the named lines.   node transit/tools/line-diag.mjs <save> "<line>,<line>" [out.json]
import { readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const [save, names, outFile = '.gamectl/tracklink/line-diag.json'] = process.argv.slice(2);
const body = JSON.stringify({ session: 'terrain worker (transit/ line diag)', since: new Date().toISOString() }) + '\n';
for (;;) { try { writeFileSync(LOCK, body, { flag: 'wx' }); const st = await status('cs2').catch(() => null); if (!st?.running) break; unlinkSync(LOCK); } catch (e) { if (e.code !== 'EEXIST') throw e; } await new Promise(r => setTimeout(r, 10e3)); }
const out = {};
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  for (const n of names.split(',')) out[n] = await b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args: { op: process.env.OP || "lines.debug", name: n, ...(process.env.ARGS ? JSON.parse(process.env.ARGS) : {}) }, timeout: 300 });
} catch (e) { out.error = e.message; }
finally { writeFileSync(outFile, JSON.stringify(out, null, 1)); try { await stop('cs2', { wait: 90 }); } catch {} if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) unlinkSync(LOCK); }
