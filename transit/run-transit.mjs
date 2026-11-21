#!/usr/bin/env node
// In-game check of the transit step (holds the shared CS2 lock throughout):
//   load a city with imported roads (and buildings: stations, depots) → transit apply → [test bus depot] →
//   simulate and count vehicles → transport-overlay screenshots → save → stop.
//
//   node transit/run-transit.mjs --plan <cs1conv-plan.json> [--load cs1conv-m3-buildings] [--name cs1conv-m2-transit]
//        [--depot <buildings plan with a BusDepot item>] [--sim-seconds 180] [--out dir] [--keep-running]
import { readFileSync, writeFileSync, existsSync, unlinkSync, mkdirSync, copyFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { start, stop, status } from '../tools/gamectl/lib/index.js';
import { transit, DLL } from './transit.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(HERE, '..');
const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const BUILDINGS_DLL = join(REPO, 'buildings', 'bin', 'Release', 'net48', 'Cs2Buildings.dll');
const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const flag = n => argv.includes(n);
const save = opt('--load', 'cs1conv-m3-buildings');
const name = opt('--name', 'cs1conv-m2-transit');
const out = resolve(opt('--out', join(REPO, '.gamectl', 'transit', new Date().toISOString().replace(/[-:]/g, '').slice(0, 13))));
mkdirSync(out, { recursive: true });
const planFile = join(out, 'cs1conv-plan.json');
copyFileSync(resolve(opt('--plan')), planFile);
const S = { started: new Date().toISOString(), out, load: save, steps: [] };
const log = (...m) => console.error('[transit]', ...m);
const dump = () => writeFileSync(join(out, 'summary.json'), JSON.stringify(S, null, 2) + '\n');
async function step(n, f) {
  const t0 = Date.now(); log(n + ' ...');
  try { const r = await f(); S.steps.push({ step: n, ok: true, s: Math.round((Date.now() - t0) / 100) / 10 }); S[n] = r; dump(); return r; }
  catch (e) { S.steps.push({ step: n, ok: false, error: e.message }); dump(); throw e; }
}
async function takeLock() {
  const body = JSON.stringify({ session: 'terrain worker (transit/ folder)', since: new Date().toISOString(), purpose: 'transit check' }) + '\n';
  for (;;) {
    try {
      writeFileSync(LOCK, body, { flag: 'wx' });
      const st = await status('cs2').catch(() => null);
      if (!st?.running) return;
      unlinkSync(LOCK);
    } catch (e) { if (e.code !== 'EEXIST') throw e; }
    await new Promise(r => setTimeout(r, 10e3));
  }
}

let started = false;
try {
  await takeLock(); started = true;
  const { bridge: b } = await step('start', () => start('cs2', { load: save, timeout: 400, loadTimeout: 900 }));
  // Road Builder shows a modal ("this city contains roads that you did not have already, reload this save-game") the
  // first time a save with converted roads loads in a game session; reloading once, as it asks, makes it go away
  await step('reload', () => b.load(save, { timeout: 900 }));
  await b.pause();
  const x = (args, o = {}) => b.exec(DLL, { type: 'Cs1ConvTransit.Ops', args, timeout: 600, ...o });
  await step('prefabs', () => x({ op: 'prefabs' }));
  await step('before', async () => ({ counts: await x({ op: 'counts' }), subwayStops: await x({ op: 'stops.list', type: 'Subway' }) }));
  await step('apply', () => transit(['apply', '--plan', planFile]));

  if (opt('--depot') && existsSync(BUILDINGS_DLL)) {
    await step('depot', () => b.exec(BUILDINGS_DLL, { phase: 'tool', pump: 'tool', timeout: 600,
      args: { op: 'place', plan: resolve(opt('--depot')), offsetX: 0, offsetZ: 0, fallback: 'nearby', nearbyRadius: 600, report: join(out, 'depot-report.json') } }));
  }

  await step('simulate', async () => {
    await b.call('sim.set', { paused: false, speed: 3 });
    const secs = Number(opt('--sim-seconds', 180)), samples = [];
    for (let t = 0; t < secs; t += 45) { await b.wait(undefined, 45, { timeout: 200 }); samples.push(await x({ op: 'counts' })); }
    await b.pause();
    return { samples, lines: await x({ op: 'lines.list' }) };
  });

  await step('shots', async () => {
    const views = (await x({ op: 'infoviews' })).infoviews;
    const transport = views.find(v => /^Transport/i.test(v)) || views.find(v => /Transport/i.test(v));
    const shots = [];
    const stops = (await x({ op: 'stops.list', type: 'Bus' })).filter(s => /BusStop/.test(s.prefab));
    const shoot = async (n, cam, iv) => {
      await x({ op: 'infoview', name: iv ?? '' });
      await b.cameraSet(cam); await b.wait(90);
      const f = join(out, 'shots', n + '.png'); await b.screenshot(f); shots.push(f);
    };
    await shoot('overview-transport', { x: 0, z: 300, distance: 4200, yaw: 0, pitch: 70 }, transport);
    await shoot('oblique-transport', { x: -300, z: 700, distance: 2200, yaw: 25, pitch: 45 }, transport);
    if (stops[0]) await shoot('stop-close', { x: stops[0].pos[0], z: stops[0].pos[2], distance: 120, yaw: 30, pitch: 40 }, null);
    if (stops[5]) await shoot('stop-close-transport', { x: stops[5].pos[0], z: stops[5].pos[2], distance: 300, yaw: 60, pitch: 50 }, transport);
    await x({ op: 'infoview', name: '' });
    return { infoview: transport, shots };
  });
  if (!flag('--no-save')) await step('save', () => b.call('save.write', { name }, { timeout: 300 }));
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
