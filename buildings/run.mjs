#!/usr/bin/env node
// CS1 → CS2 buildings (M3). Commands:
//
//   node buildings/run.mjs catalogue [--out <file>] [--keep]
//        Starts CS2 if needed (taking cs2.lock), dumps every building / object prefab to
//        %LOCALAPPDATA%\cs1-to-cs2\samples\cs2-catalogue.json (default --out), quits CS2 unless --keep.
//   node buildings/run.mjs slim [--catalogue <file>]
//        Writes cs2-catalogue-buildings.json next to it: placeable buildings with only the matching fields (web tool).
//   node buildings/run.mjs match --cs1x <file> [--catalogue <file>] [--json]
//        Matches every CS1 building type of the export against the catalogue and prints the table.
//   node buildings/run.mjs plan --cs1x <file> --plan <road plan dir|file> [--catalogue <file>] [--out <file>]
//        Writes the buildings plan (default: cs1conv-buildings.json next to the road plan).
//   node buildings/run.mjs place --buildings <file> --save <cs2 save> [--offset x,z] [--limit n] [--write-save <name>]
//                                [--shots] [--keep]
//        Loads the CS2 save (a city where the road plan was imported), places the buildings, verifies, writes the
//        report to .gamectl/buildings/, optionally saves as a cs1conv-* save and takes screenshots.
//
// CS2 is shared: every command that drives it takes %LOCALAPPDATA%\cs1-to-cs2\cs2.lock (waiting while another owner
// holds a lock younger than 2 h) and releases it at the end. CS2 is stopped at the end only when this script started it.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { connect, start, stop } from '../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock, LOCAL_DIR, REPO } from '../cs2-importer/lib/driver.mjs';
import { readCs1x } from './lib/cs1x.mjs';
import { buildBuildingsPlan } from './lib/plan.mjs';
import { matchAssets, slimCatalogue } from './match/match.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const DLL = path.join(HERE, 'bin', 'Release', 'net48', 'Cs2Buildings.dll');
const DEFAULT_CATALOGUE = path.join(LOCAL_DIR, 'samples', 'cs2-catalogue.json');
const OWNER = 'buildings';
const log = (...a) => console.error('[buildings]', ...a);

const argv = process.argv.slice(2);
const cmd = argv[0];
const opt = (name, def) => { const i = argv.indexOf(`--${name}`); return i >= 0 ? argv[i + 1] : def; };
const flag = (name) => argv.includes(`--${name}`);

function build() {
  execFileSync('dotnet', ['build', path.join(HERE, 'Cs2Buildings.csproj'), '-c', 'Release', '-nologo', '-v', 'q'], { stdio: ['ignore', 'ignore', 'inherit'] });
}

async function withCs2(fn, { keep = false } = {}) {
  await waitAndTakeLock('cs2', OWNER, 'buildings worker (buildings/run.mjs)', { log });
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
    if (!keep && started) { try { log('stopping CS2'); await stop('cs2'); } catch (e) { log('stop failed:', e.message); } }
    releaseLock('cs2', OWNER);
  }
}

const readJson = (f) => JSON.parse(fs.readFileSync(f, 'utf8'));

/** cs2-catalogue.json → cs2-catalogue-buildings.json (placeable buildings, matching fields only), for the web tool. */
function writeSlim(full) {
  const out = full.replace(/.json$/, '-buildings.json');
  const slim = slimCatalogue(readJson(full));
  fs.writeFileSync(out, JSON.stringify(slim));
  log(`slim catalogue: ${out} (${slim.prefabs.length} buildings, ${(fs.statSync(out).size / 1024).toFixed(0)} KiB)`);
}

async function main() {
  switch (cmd) {
    case 'catalogue': {
      build();
      const out = path.resolve(opt('out', DEFAULT_CATALOGUE));
      const r = await withCs2((b) => b.exec(DLL, { args: { op: 'catalogue', path: out }, timeout: 600 }), { keep: flag('keep') });
      writeSlim(out);
      console.log(JSON.stringify(r, null, 2));
      break;
    }
    case 'slim': {
      writeSlim(path.resolve(opt('catalogue', DEFAULT_CATALOGUE)));
      break;
    }
    case 'match': {
      const cs1 = readCs1x(opt('cs1x'));
      const m = matchAssets(cs1.assets, readJson(opt('catalogue', DEFAULT_CATALOGUE)));
      if (flag('json')) { console.log(JSON.stringify(m, null, 2)); break; }
      for (const r of m.results.filter((x) => x.decision !== 'growable').sort((a, b) => a.decision.localeCompare(b.decision) || b.count - a.count))
        console.log(`${r.decision.padEnd(12)} ${String(r.count).padStart(4)}  ${r.cs1.padEnd(42)} → ${(r.cs2 ?? '-').padEnd(34)} ${r.score ? r.score.toFixed(2) : '    '}  ${r.reason}`);
      console.log(JSON.stringify(m.stats));
      break;
    }
    case 'plan': {
      const planArg = opt('plan');
      const planFile = fs.statSync(planArg).isDirectory() ? path.join(planArg, 'cs1conv-plan.json') : planArg;
      const out = path.resolve(opt('out', path.join(path.dirname(planFile), 'cs1conv-buildings.json')));
      const bp = buildBuildingsPlan(readCs1x(opt('cs1x')), readJson(opt('catalogue', DEFAULT_CATALOGUE)), readJson(planFile), { theme: opt('theme') });
      fs.writeFileSync(out, JSON.stringify(bp, null, 1));
      console.log(JSON.stringify({ out, stats: bp.stats, cs1AngleConvention: bp.cs1AngleConvention, types: bp.types.length }, null, 2));
      break;
    }
    case 'place': {
      build();
      const bfile = path.resolve(opt('buildings'));
      const save = opt('save');
      const [ox, oz] = (opt('offset', '2000,-4000')).split(',').map(Number);
      const tag = new Date().toISOString().replace(/[-:]/g, '').replace(/\..*/, '').replace('T', '-');
      const dir = path.join(REPO, '.gamectl', 'buildings', tag);
      fs.mkdirSync(dir, { recursive: true });
      const r = await withCs2(async (b) => {
        if (save) { log(`loading ${save}`); await b.load(save); }
        await b.pause();
        const args = { op: 'place', plan: bfile, offsetX: ox, offsetZ: oz, report: path.join(dir, 'report.json') };
        if (opt('limit')) args.limit = Number(opt('limit'));
        const res = await b.exec(DLL, { phase: 'tool', pump: 'tool', args, timeout: 1800 });
        if (flag('shots')) await shots(b, bfile, ox, oz, dir);
        if (opt('write-save')) { log(`saving ${opt('write-save')}`); await b.saveWrite(opt('write-save')); }
        return res;
      }, { keep: flag('keep') });
      console.log(JSON.stringify(r, null, 2));
      log(`report: ${path.join(dir, 'report.json')}`);
      break;
    }
    default:
      console.error('usage: node buildings/run.mjs catalogue|match|plan|place … (see the header of this file)');
      process.exit(2);
  }
}

/** Overview of the placed area plus close-ups of the first placed buildings. */
async function shots(b, bfile, ox, oz, dir) {
  const report = readJson(path.join(dir, 'report.json'));
  const placed = report.results.filter((r) => r.status === 'placed');
  if (!placed.length) return;
  const cx = placed.reduce((s, r) => s + r.x, 0) / placed.length, cz = placed.reduce((s, r) => s + r.z, 0) / placed.length;
  await b.cameraSet({ x: cx, z: cz, distance: 2200, pitch: 70, yaw: 0 });
  await b.wait(60);
  await b.screenshot(path.join(dir, 'overview.png'));
  for (const [i, r] of placed.slice(0, 6).entries()) {
    await b.cameraSet({ x: r.x, z: r.z, distance: 160, pitch: 45, yaw: (r.yaw + 180) % 360 });
    await b.wait(45);
    await b.screenshot(path.join(dir, `close-${i}-${r.prefab}.png`));
  }
}

main().catch((e) => { console.error(e.stack ?? e.message); process.exit(1); });
