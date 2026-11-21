#!/usr/bin/env node
// M2 map preparation, checked in game, in one run (holds the shared CS2 lock throughout):
//   start CS2 → [survey every map, rank] → new city on the chosen map → clear base nets → terrain → road import
//   (cs2-importer, unchanged) → outside connections → [water] → simulate and count traffic → screenshots → save → stop.
//
//   node terrain/run-mapprep.mjs --cs1x f --plan dir|file [--map name] [--survey-maps] [--name cs1conv-mapprep-test]
//                                [--sim-seconds 90] [--out dir] [--keep-running]
import { readFileSync, writeFileSync, existsSync, unlinkSync, mkdirSync, copyFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { start, stop, connect, status } from '../tools/gamectl/lib/index.js';
import { mapprep } from './mapprep.mjs';
import { main as applyMain } from './apply.mjs';
import { readZip } from './lib/zip.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..');
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const IMPORTER_DLL = join(REPO, 'cs2-importer', 'bin', 'Release', 'net48', 'Cs2Importer.dll');
export const DEFAULT_MAP = 'Sunshine Peninsula'; // fewest base nets, see DECISIONS-terrain.md #16

const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const flag = n => argv.includes(n);
let planSrc = resolve(opt('--plan'));
if (!planSrc.endsWith('.json')) planSrc = join(planSrc, 'cs1conv-plan.json');
const name = opt('--name', 'cs1conv-mapprep-test');
const out = resolve(opt('--out', join(REPO, '.gamectl', 'mapprep', new Date().toISOString().replace(/[-:]/g, '').slice(0, 13))));
mkdirSync(out, { recursive: true });
// work on copies: the shared samples get replaced by other sessions while a run is going
const cs1x = join(out, 'source.cs1x');
copyFileSync(resolve(opt('--cs1x')), cs1x);
const planFile = join(out, 'cs1conv-plan.json');
copyFileSync(planSrc, planFile);
const S = { started: new Date().toISOString(), out, steps: [] };
const log = (...m) => console.error('[mapprep]', ...m);
const save = () => writeFileSync(join(out, 'summary.json'), JSON.stringify(S, null, 2) + '\n');

async function step(n, f) {
  const t0 = Date.now();
  log(n + ' ...');
  try {
    const r = await f();
    S.steps.push({ step: n, s: Math.round((Date.now() - t0) / 100) / 10, ok: true });
    S[n] = r; save();
    return r;
  } catch (e) {
    S.steps.push({ step: n, s: Math.round((Date.now() - t0) / 100) / 10, ok: false, error: e.message });
    save();
    throw e;
  }
}

/** Waits (up to --lock-wait minutes, default 120) until no other session holds cs2.lock, then creates it atomically. */
async function takeLock() {
  const deadline = Date.now() + Number(opt('--lock-wait', 120)) * 60e3;
  const body = JSON.stringify({ session: 'terrain worker (terrain/ folder)', since: new Date().toISOString(), purpose: 'M2 map prep check' }) + '\n';
  for (;;) {
    if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) { writeFileSync(LOCK, body); return; }
    try {
      writeFileSync(LOCK, body, { flag: 'wx' });
      // a game still running belongs to whoever just released the lock (between their runs): give it back and wait
      const st = await status('cs2').catch(() => null);
      if (!st || !(st.running ?? st.pids?.length)) return;
      unlinkSync(LOCK);
    } catch (e) { if (e.code !== 'EEXIST') throw e; }
    if (Date.now() > deadline) throw new Error('CS2 is locked: ' + readFileSync(LOCK, 'utf8').trim());
    await new Promise(r => setTimeout(r, 10e3));
  }
}

// Road Builder reads its road types once, when CS2 starts: install this package's *-cs1conv configs first (keeping
// other cities'), or every RB segment of the plan fails with prefab-missing (CacaoCity: 431 of 684 segments)
function installRoadBuilder() {
  const pkg = dirname(planSrc);
  if (!existsSync(join(pkg, 'roadbuilder'))) return { skipped: 'no roadbuilder/ next to the plan' };
  execFileSync(process.execPath, [join(REPO, 'web', 'cli', 'cs1conv.ts'), 'install-rb', pkg, '--keep-other-rb'], { stdio: ['ignore', 'ignore', 'inherit'] });
  return { installed: pkg };
}

let started = false;
try {
  S.roadbuilder = installRoadBuilder();
  await takeLock();
  started = true;
  const { bridge: b0 } = await step('start', () => start('cs2', { timeout: 400 }));
  const b = b0 ?? await connect('cs2');
  await step('ocPrefabs', () => mapprep(['oc-prefabs']));
  let map = opt('--map', DEFAULT_MAP);
  if (flag('--survey-maps')) {
    const sv = await step('surveyMaps', () => mapprep(['survey-maps', '--out', join(out, 'survey-maps.json')]));
    if (!opt('--map')) map = sv.ranking[0].map;
  }
  S.map = map;
  await step('newGame', async () => { const r = await b.call('game.new', { map, name, options: { unlockAll: true, unlimitedMoney: true, unlockMapTiles: true } }, { timeout: 900 }); await b.pause(); return r; });
  await step('surveyBefore', () => mapprep(['survey']));
  await step('clear', () => mapprep(['clear']));

  const tdir = join(out, 'terrain');
  await step('terrain', async () => {
    execFileSync(process.execPath, [join(HERE, 'convert.mjs'), '--cs1x', cs1x, '--plan', planFile, '--out', tdir, '--no-png'], { stdio: ['ignore', 'ignore', 'inherit'] });
    await applyMain(['--terrain', tdir, '--cs1x', cs1x, '--shots', join(out, 'shots-terrain')]);
    return JSON.parse(readFileSync(join(tdir, 'apply-report.json'), 'utf8')).verification;
  });
  await step('settle', () => b.wait(600, 20, { timeout: 600 }));

  await step('import', async () => {
    if (!existsSync(IMPORTER_DLL)) execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q'], { cwd: join(REPO, 'cs2-importer'), stdio: ['ignore', 'ignore', 'inherit'] });
    const r = await b.exec(IMPORTER_DLL, { phase: 'tool', pump: 'tool', args: { op: 'import', plan: planFile, clear: true, report: join(out, 'import-report.json'), heights: 'flat', offsetX: 0, offsetZ: 0 }, timeout: 3600 });
    return { result: r.result, seconds: r.seconds, temp_after: r.temp_after };
  });

  await step('countsBeforeConnections', () => mapprep(['counts']));
  await step('connections', () => mapprep(['connections', '--cs1x', cs1x, '--plan', planFile]));
  await step('countsAfterConnections', async () => { await b.wait(120); return mapprep(['counts']); });

  const hasWater = readZip(readFileSync(cs1x)).has('water.json');
  if (hasWater) await step('water', () => mapprep(['water', '--cs1x', cs1x, '--plan', planFile, '--terrain', tdir]));
  else S.water = 'skipped: no water.json in the .cs1x';

  await step('simulate', async () => {
    const c0 = await mapprep(['counts']);
    await b.call('sim.set', { paused: false, speed: 3 });
    const secs = Number(opt('--sim-seconds', 90));
    const samples = [];
    for (let t = 0; t < secs; t += 30) { await b.wait(undefined, 30, { timeout: 120 }); samples.push(await mapprep(['counts'])); }
    await b.pause();
    return { before: c0, samples };
  });
  if (hasWater) await step('waterCheck', async () => { const r = await mapprep(['water-check', '--cs1x', cs1x, '--plan', planFile]); writeFileSync(join(out, 'water-check.json'), JSON.stringify(r, null, 2)); delete r.rows; return r; });

  await step('shots', async () => {
    const shots = [];
    const views = [{ n: 'overview', x: 0, z: 0, d: 4500, yaw: 0, p: 60 }, { n: 'oblique', x: 0, z: 0, d: 3500, yaw: 30, p: 35 }];
    const conns = (S.connections?.connections ?? []).filter(c => c.pos).slice(0, 4);
    conns.forEach((c, i) => views.push({ n: `oc-${i}-${c.type ?? ''}`, x: c.pos[0], z: c.pos[2], d: 500, yaw: 45, p: 40 }));
    for (const v of views) {
      await b.cameraSet({ x: v.x, z: v.z, distance: v.d, yaw: v.yaw, pitch: v.p });
      await b.wait(90);
      const f = join(out, 'shots', `${v.n}.png`);
      await b.screenshot(f);
      shots.push(f);
    }
    return shots;
  });
  if (!flag('--no-save')) await step('save', () => b.call('save.write', { name }, { timeout: 300 }));
  S.ok = true;
} catch (e) {
  log('failed: ' + (e.stack || e.message));
  S.ok = false; S.error = e.message;
  process.exitCode = 1;
} finally {
  save();
  if (started && !flag('--keep-running')) {
    try { await stop('cs2', { wait: 90 }); } catch (e) { log('stop: ' + e.message); }
    try { if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) unlinkSync(LOCK); } catch { /* ignore */ }
  }
  console.log(join(out, 'summary.json'));
}
