#!/usr/bin/env node
// Drives the M1 CS2 importer in a running CS2 through tools/gamectl (Cs2Bridge exec). Run from anywhere:
//
//   node cs2-importer/run.mjs start                      lock CS2 (%LOCALAPPDATA%\cs1-to-cs2\cs2.lock) and start it
//   node cs2-importer/run.mjs new [--map "<name|id>"] [--name cs1conv-city]
//   node cs2-importer/run.mjs check <plan dir|json>
//   node cs2-importer/run.mjs import <plan dir|json> [--clear] [--batch 400] [--tag name] [--heights flat|exact]
//   node cs2-importer/run.mjs verify <plan dir|json>
//   node cs2-importer/run.mjs shots <plan dir|json> [--tag name]   overview + close-ups of the plan's area
//   node cs2-importer/run.mjs save cs1conv-<name>
//   node cs2-importer/run.mjs stop                       quit CS2 and release the lock
//   node cs2-importer/run.mjs copy-rb <plan dir>         copy roadbuilder/*-cs1conv.json to ModsData (CS2 must be closed)
//
// Reports and screenshots go to <repo>/.gamectl/import/ (git-ignored). The importer DLL is built on demand
// (dotnet build -c Release); exec loads a fresh copy on every call, so a rebuild needs no game restart.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../tools/gamectl/lib/index.js';
import {
  buildImporter, planPath, readPlan, planShots, summary, CS2_MODSDATA_RB as modsDataRb,
  takeLock as takeGameLock, releaseLock as releaseGameLock, readLock as readGameLock,
} from './lib/driver.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..');
const outDir = path.join(repo, '.gamectl', 'import');
const LOCK_OWNER = 'cs2-importer';
// M1 test placement (DECISIONS-importer.md): Corral Riches, plan shifted to the flattest dry 3.2 km square of the map
const DEFAULT_MAP = process.env.CS1CONV_MAP ?? 'Corral Riches';
const DEFAULT_OFFSET = process.env.CS1CONV_OFFSET ?? '2000,-4000';

const argv = process.argv.slice(2);
const cmd = argv[0];
const VALUE_FLAGS = new Set(['--map', '--name', '--batch', '--tag', '--offset', '--heights']);
const pos = argv.slice(1).filter((a, i, all) => !a.startsWith('--') && !(i > 0 && VALUE_FLAGS.has(all[i - 1])));
const opt = (name, def) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : def; };
const flag = (name) => argv.includes(name);

function log(...a) { console.error('[run]', ...a); }

/**
 * --offset x,z (default DEFAULT_OFFSET; "0,0" = the plan's own coordinates) and --heights flat|exact (exact = the CS1
 * terrain has been applied, see terrain/; it implies --offset 0,0 unless one is given).
 */
function offset() {
  const heights = opt('--heights', 'flat');
  const [x, z] = String(opt('--offset', heights === 'exact' ? '0,0' : DEFAULT_OFFSET)).split(',').map(Number);
  if (!Number.isFinite(x) || !Number.isFinite(z)) throw new Error('--offset must be x,z');
  return { offsetX: x, offsetZ: z, heights };
}

async function bridge() { return connect('cs2'); }

function buildDll() { return buildImporter(); }
function takeLock() { takeGameLock('cs2', LOCK_OWNER, 'cs2-importer (cs2-importer/run.mjs)', { log }); }
function releaseLock() { releaseGameLock('cs2', LOCK_OWNER); }
function readLock() { return readGameLock('cs2'); }

async function shots(planArg, tag) {
  return planShots(await bridge(), readPlan(planArg), { dir: outDir, tag, ...offset(), log });
}

// ------------------------------------------------------------------ commands

async function main() {
  switch (cmd) {
    case 'start': {
      const st = await status('cs2');
      if (st.running && readLock()?.owner !== LOCK_OWNER) throw new Error('CS2 is already running and not locked by us');
      takeLock();
      const r = await start('cs2', {});
      log(`cs2 ready (pid ${r.pid})`);
      return r;
    }
    case 'stop': {
      try { await stop('cs2', {}); } finally { releaseLock(); }
      return { stopped: true };
    }
    case 'new': {
      const b = await bridge();
      return b.newGame(opt('--map', DEFAULT_MAP), { name: opt('--name', 'cs1conv-m1') });
    }
    case 'maps': return (await bridge()).maps();
    case 'save': {
      if (!pos[0]) throw new Error('usage: save cs1conv-<name>');
      return (await bridge()).saveWrite(pos[0]);
    }
    case 'check': {
      const b = await bridge();
      return b.exec(buildDll(), { args: { op: 'check', plan: planPath(pos[0]), ...offset() }, timeout: 300 });
    }
    case 'verify': {
      const b = await bridge();
      const r = await b.exec(buildDll(), { args: { op: 'verify', plan: planPath(pos[0]), ...offset() }, timeout: 300 });
      log('\n' + summary(r));
      return r;
    }
    case 'clear': {
      const b = await bridge();
      return b.exec(buildDll(), { phase: 'tool', pump: 'tool', args: { op: 'clear', plan: planPath(pos[0]), ...offset() }, timeout: 300 });
    }
    case 'import': {
      const b = await bridge();
      const tag = opt('--tag', path.basename(path.dirname(planPath(pos[0]))));
      fs.mkdirSync(outDir, { recursive: true });
      const report = path.join(outDir, `${tag}-report.json`);
      const args = { op: 'import', plan: planPath(pos[0]), clear: flag('--clear'), report, ...offset() };
      if (opt('--batch')) args.batchSize = Number(opt('--batch'));
      await b.pause().catch(() => {});
      const r = await b.exec(buildDll(), { phase: 'tool', pump: 'tool', args, timeout: 3600 });
      log(`report: ${report}\n${summary(r)}`);
      return r;
    }
    case 'shots': return shots(pos[0], opt('--tag', path.basename(path.dirname(planPath(pos[0])))));
    case 'copy-rb': {
      const st = await status('cs2');
      if (st.running) throw new Error('CS2 is running: Road Builder only reads configs at startup, copy before starting it');
      const dir = path.join(path.resolve(pos[0]), 'roadbuilder');
      fs.mkdirSync(modsDataRb, { recursive: true });
      const copied = [];
      for (const f of fs.readdirSync(dir)) {
        if (!f.endsWith('-cs1conv.json')) continue; // only ever our own generated configs
        fs.copyFileSync(path.join(dir, f), path.join(modsDataRb, f));
        copied.push(f);
      }
      return { copied, to: modsDataRb };
    }
    default:
      console.error(fs.readFileSync(fileURLToPath(import.meta.url), 'utf8').split('\n').slice(1, 14).join('\n'));
      process.exitCode = 2;
      return null;
  }
}

main().then((r) => { if (r != null) console.log(JSON.stringify(r, null, 1)); }).catch((e) => { console.error(`[run] ${e.stack ?? e}`); process.exitCode = 1; });
