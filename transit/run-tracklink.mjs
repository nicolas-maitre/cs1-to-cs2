#!/usr/bin/env node
// In-game check: rail/subway yards get linked to the converted lines and metros/trains run.
//   load a converted save (transit lines on placed stations) → [place default yards with the buildings placer] →
//   yards.inspect → tracklink → yards.inspect → simulate (vehicle counts per line) → screenshots → save → stop.
//
//   node transit/run-tracklink.mjs --load <save> [--plan <cs1conv-plan.json> (edge yards, DECISIONS #83) |
//        --yards <buildings plan with yards>] [--name <save>]
//        [--sim-seconds 300] [--out dir] [--max-distance 2500]
import { readFileSync, writeFileSync, existsSync, unlinkSync, mkdirSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { start, stop, status } from '../tools/gamectl/lib/index.js';
import { DLL, transit } from './transit.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..');
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const BUILDINGS_DLL = join(REPO, 'buildings', 'bin', 'Release', 'net48', 'Cs2Buildings.dll');
const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const flag = n => argv.includes(n);
const save = opt('--load');
if (!save) throw new Error('--load <save> is required');
const out = resolve(opt('--out', join(REPO, '.gamectl', 'tracklink', new Date().toISOString().replace(/[-:]/g, '').slice(0, 13))));
mkdirSync(out, { recursive: true });
const S = { started: new Date().toISOString(), out, load: save, steps: [] };
const log = (...m) => console.error('[tracklink]', ...m);
const dump = () => writeFileSync(join(out, 'summary.json'), JSON.stringify(S, null, 2) + '\n');
async function step(n, f) {
  const t0 = Date.now(); log(n + ' ...');
  try { const r = await f(); S.steps.push({ step: n, ok: true, s: Math.round((Date.now() - t0) / 100) / 10 }); S[n] = r; dump(); return r; }
  catch (e) { S.steps.push({ step: n, ok: false, error: e.message }); dump(); throw e; }
}
async function takeLock() {
  const body = JSON.stringify({ session: 'terrain worker (transit/ tracklink)', since: new Date().toISOString(), purpose: 'yard track link check' }) + '\n';
  for (;;) {
    try { writeFileSync(LOCK, body, { flag: 'wx' }); const st = await status('cs2').catch(() => null); if (!st?.running) return; unlinkSync(LOCK); }
    catch (e) { if (e.code !== 'EEXIST') throw e; }
    await new Promise(r => setTimeout(r, 10e3));
  }
}

let started = false;
try {
  await takeLock(); started = true;
  const { bridge: b } = await step('start', () => start('cs2', { load: save, timeout: 400, loadTimeout: 900 }));
  await step('reload', () => b.load(save, { timeout: 900 })); // dismisses Road Builder's one-time dialog
  await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  const lines = async () => (await x({ op: 'lines.list' })).filter(l => /Subway|Train/.test(l.prefab)).map(l => ({ name: l.name, prefab: l.prefab, complete: l.complete, stops: l.waypoints, vehicles: l.vehicles, pathElements: l.pathElements, failedSegments: l.failedSegments }));
  await step('linesBefore', lines);
  if (opt('--yards')) {
    await step('yards', () => b.exec(BUILDINGS_DLL, { phase: 'tool', pump: 'tool', timeout: 600,
      args: { op: 'place', plan: resolve(opt('--yards')), offsetX: 0, offsetZ: 0, fallback: 'nearby', nearbyRadius: 1500, report: join(out, 'yards-report.json') } }));
    await b.wait(60);
  }
  await step('inspectBefore', () => x({ op: 'yards.inspect' }));
  if (opt('--plan')) {
    // the DECISIONS.md #83 flow: yards.plan (crop edge) → buildings placer → tracklink, or report entries
    await step('depots', () => transit(['depots', '--plan', resolve(opt('--plan')), '--work-dir', out, ...(flag('--recreate') ? ['--recreate'] : []), ...(flag('--interior-yards') ? ['--interior-yards'] : [])]));
  } else {
    await step('tracklink', () => x({ op: 'tracklink', maxDistance: Number(opt('--max-distance', 2500)) }, { phase: 'tool', pump: 'tool' }));
    await b.wait(60);
  }
  await step('inspectAfter', () => x({ op: 'yards.inspect' }));
  // #159: can a vehicle drive from each depot onto its lines (directed track-lane graph)?
  await step('lanereach', async () => ({ subway: await x({ op: 'lanes.reach', track: 'Subway' }), train: await x({ op: 'lanes.reach', track: 'Train' }) }));
  await step('simulate', async () => {
    await b.call('sim.set', { paused: false, speed: 3 });
    const secs = Number(opt('--sim-seconds', 300)), samples = [];
    for (let t = 0; t < secs; t += 60) { await b.wait(undefined, 60, { timeout: 200 }); samples.push({ t: t + 60, counts: (await x({ op: 'counts' })).byPrefab, lines: await lines() }); }
    await b.pause();
    return samples;
  });
  await step('shots', async () => {
    const shots = [];
    const views = (await x({ op: 'infoviews' })).infoviews;
    const transport = views.find(v => /^Transport/i.test(v));
    for (const y of (S.inspectAfter?.yards ?? []).slice(0, 2)) {
      if (!y.pos) continue;
      for (const [n, iv] of [['yard', ''], ['yard-transport', transport]]) {
        await x({ op: 'infoview', name: iv ?? '' });
        await b.cameraSet({ x: y.pos[0], z: y.pos[2], distance: 700, yaw: 30, pitch: 45 }); await b.wait(90);
        const f = join(out, 'shots', `${n}-${y.prefab}.png`); await b.screenshot(f); shots.push(f);
      }
    }
    await x({ op: 'infoview', name: '' });
    return shots;
  });
  if (opt('--name')) await step('save', () => b.call('save.write', { name: opt('--name') }, { timeout: 300 }));
  S.ok = true;
} catch (e) {
  log('failed: ' + (e.stack || e.message)); S.ok = false; S.error = e.message; process.exitCode = 1;
} finally {
  dump();
  if (started && !flag('--keep-running')) {
    try { await stop('cs2', { wait: 90 }); } catch (e) { log('stop: ' + e.message); }
    try { if (existsSync(LOCK) && /terrain worker/.test(readFileSync(LOCK, 'utf8'))) unlinkSync(LOCK); } catch { /* ignore */ }
  }
  console.log(join(out, 'summary.json'));
}
