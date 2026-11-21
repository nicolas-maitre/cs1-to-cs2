#!/usr/bin/env node
// QA measurements in converted CS2 cities (tools/qa/README.md).
//
//   node tools/qa/run.mjs utilities --save <cs2 save>[,<save>…] [--minutes 3] [--list 60] [--keep]
//        For each save: load it, let it simulate --minutes (real time, top speed; 0 = as saved), pause, measure the
//        electricity / water / sewage supply (docs/DECISIONS.md #109) and write .gamectl/qa/utilities-<save>.json.
//        Prints a summary per save. Read-only: nothing is saved back.
//
// CS2 is shared: takes %LOCALAPPDATA%\cs1-to-cs2\cs2.lock (waits while another owner holds a lock younger than 2 h)
// and releases it at the end; stops CS2 at the end only when this script started it (unless --keep).
import fs from 'node:fs';
import path from 'node:path';
import { connect, start, stop } from '../gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock, REPO } from '../../cs2-importer/lib/driver.mjs';
import { buildQa, measureUtilities, summariseUtilities } from './lib/utilities.mjs';

const OWNER = 'qa';
const log = (...a) => console.error('[qa]', ...a);
const argv = process.argv.slice(2);
const cmd = argv[0];
const opt = (name, def) => { const i = argv.indexOf(`--${name}`); return i >= 0 ? argv[i + 1] : def; };
const flag = (name) => argv.includes(`--${name}`);

async function withCs2(fn) {
  await waitAndTakeLock('cs2', OWNER, 'QA checks (tools/qa/run.mjs, web worker)', { log, timeoutMin: 120 });
  let started = false;
  try {
    let bridge;
    try { bridge = await connect('cs2'); await bridge.ping(); } catch {
      log('starting CS2');
      ({ bridge } = await start('cs2', { onProgress: log }));
      started = true;
    }
    return await fn(bridge);
  } finally {
    if (started && !flag('keep')) { try { log('stopping CS2'); await stop('cs2'); } catch (e) { log('stop failed:', e.message); } }
    releaseLock('cs2', OWNER);
  }
}

if (cmd === 'utilities') {
  const saves = (opt('save') ?? '').split(',').filter(Boolean);
  if (!saves.length) { console.error('usage: node tools/qa/run.mjs utilities --save <cs2 save>[,<save>…] [--minutes 3] [--list 60] [--keep]'); process.exit(2); }
  const minutes = Number(opt('minutes', 3)), listLimit = Number(opt('list', 60));
  buildQa();
  const outDir = path.join(REPO, '.gamectl', 'qa');
  fs.mkdirSync(outDir, { recursive: true });
  let allOk = true;
  await withCs2(async (b) => {
    for (const save of saves) {
      log(`loading ${save}`);
      const t0 = Date.now();
      await b.load(save, { timeout: 900 });
      log(`loaded in ${Math.round((Date.now() - t0) / 1000)} s`);
      const r = await measureUtilities(b, { minutes, listLimit, log });
      const s = summariseUtilities(r);
      const file = path.join(outDir, `utilities-${save}.json`);
      fs.writeFileSync(file, JSON.stringify({ save, minutes, measuredAt: new Date().toISOString(), summary: s, result: r }, null, 1));
      console.log(`== ${save} (${minutes} min simulated) ok=${s.ok} → ${file}`);
      for (const l of s.lines) console.log(`   ${l}`);
      allOk &&= s.ok;
    }
  });
  process.exitCode = allOk ? 0 : 1;
} else {
  console.error('usage: node tools/qa/run.mjs utilities --save <cs2 save>[,…] [--minutes 3] [--list 60] [--keep]');
  process.exit(2);
}
