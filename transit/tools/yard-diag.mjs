#!/usr/bin/env node
// Load a save with a yard and dump yards.inspect (with detail).   node transit/tools/yard-diag.mjs <save> [out.json]
import { readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { start, stop, status } from '../../tools/gamectl/lib/index.js';
import { DLL } from '../transit.mjs';
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const save = process.argv[2], outFile = process.argv[3] || '.gamectl/tracklink/yard-diag.json';
const body = JSON.stringify({ session: 'terrain worker (transit/ yard diag)', since: new Date().toISOString() }) + '\n';
for (;;) { try { writeFileSync(LOCK, body, { flag: 'wx' }); const st = await status('cs2').catch(() => null); if (!st?.running) break; unlinkSync(LOCK); } catch (e) { if (e.code !== 'EEXIST') throw e; } await new Promise(r => setTimeout(r, 10e3)); }
try {
  const { bridge: b } = await start('cs2', { load: save, timeout: 400 });
  await b.load(save, { timeout: 900 }); await b.pause();
  const r = await b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args: { op: 'yards.inspect' }, timeout: 300 });
  writeFileSync(outFile, JSON.stringify(r, null, 1));
} catch (e) { writeFileSync(outFile, JSON.stringify({ error: e.message })); }
finally { try { await stop('cs2', { wait: 90 }); } catch {} if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) unlinkSync(LOCK); }
