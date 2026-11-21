#!/usr/bin/env node
// End to end, in one command: CS1 export → plan → CS2 new city → (terrain) → road import → screenshots → save.
//
//   node tools/e2e/run.mjs [--cs1x <file>] [--save Skibidi.02] [--auto-crop 3000 | --crop cx,cz,size]
//                          [--map <CS2 map>] [--apply-terrain] [--clear-with importer|mapprep [--keep-utilities]]
//                          [--keep-base-nets] [--name <tag>] [--no-save] [--keep-cs2]
//                          [--batch 400] [--settle 20 (s after terrain)] [--no-import] [--heights flat|exact]
//                          [--no-zoning] [--no-traffic] [--no-traffic-mod] [--simulate s] [--no-<plug-in>]
//                          [--save-name cs1conv-…]
//
// Steps (each one is timed and recorded in summary.json):
//   export   CS1: take %LOCALAPPDATA%\cs1-to-cs2\cs1.lock, `gamectl cs1 start --load <save>`, exec the exporter, stop CS1.
//            Skipped with --cs1x <file> (reuse an existing export).
//   plan     node web/cli/cs1conv.ts plan <cs1x> --auto-crop 3000 --out <run>/package --install-rb
//            (Road Builder configs go to ModsData before CS2 starts: Road Builder reads them once, at startup).
//   cs2      take cs2.lock, start CS2, game.new on --map, pause.
//   clear    bulldoze the base map's own nets in the playable area (its highway/rail would keep the old map's height
//            and pull the new terrain up into walls; terrain/README.md): the importer's clear op (top-level nets in the
//            playable area); --clear-with mapprep uses terrain/mapprep.mjs clear instead. --keep-base-nets skips it.
//   terrain  only with --apply-terrain: node terrain/convert.mjs + terrain/apply.mjs (sea level auto). Without it the
//            import uses the M1 placement (flat test spot on Corral Riches, offset 2000,-4000).
//   water    (with terrain, terrain/mapprep.mjs) CS1 water if the .cs1x has water.json, else a dry map: the base map's
//            water sources removed and the water reset to the lowered sea level. Optional (failure is recorded).
//   settle   a few seconds for CS2 to digest the new terrain and water.
//   import   the importer (offset 0 with terrain, else the M1 test spot), report.json, import-trace.txt.
//   connections  (with terrain, mapprep) outside connections rebuilt at the crop edge. Optional.
//   shots    overview, oblique and close-ups.
//   save     save.write cs1conv-e2e-<yyyymmdd-hhmm> (--no-save skips it).
//   stop     quit CS2 (--keep-cs2 leaves it running and keeps the lock), release the locks.
//
// Output: <repo>/.gamectl/e2e/<timestamp>/ with summary.json, the package, terrain files, report.json and screenshots.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { connect, start, stop, status } from '../gamectl/lib/index.js';
import {
  REPO, buildImporter, readPlan, planShots, summary, waitAndTakeLock, releaseLock,
} from '../../cs2-importer/lib/driver.mjs';

const OWNER = 'e2e';
const SESSION = 'e2e script (tools/e2e/run.mjs, cs2-importer worker)';
const PLAYABLE_HALF = 7168;

// ------------------------------------------------------------------ args

const argv = process.argv.slice(2);
const opt = (name, def) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : def; };
const flag = (name) => argv.includes(name);
if (flag('--help')) {
  console.log(fs.readFileSync(fileURLToPath(import.meta.url), 'utf8').split('\n').slice(1, 22).join('\n'));
  process.exit(0);
}
const stamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\..*/, '').replace('T', '-');
const tag = opt('--name', `e2e-${stamp}`);
const runDir = path.join(REPO, '.gamectl', 'e2e', stamp);
const MAPPREP = fs.existsSync(path.join(REPO, 'terrain', 'mapprep.mjs')) && fs.existsSync(path.join(REPO, 'terrain', 'cs2-apply', 'MapPrep.cs'));
const terrainAvailable = fs.existsSync(path.join(REPO, 'terrain', 'apply.mjs')) && fs.existsSync(path.join(REPO, 'terrain', 'convert.mjs'));
// Terrain is applied only with --apply-terrain (coordinator, 2026-11-21): terrain/ is the terrain worker's, and its
// map-prep steps are still being verified. Without it, roads go to the flat M1 test spot.
const useTerrain = flag('--apply-terrain') && !flag('--no-terrain') && terrainAvailable;
if (flag('--apply-terrain') && !terrainAvailable) throw new Error('--apply-terrain: terrain/apply.mjs or terrain/convert.mjs is missing');
// Hook for the base-map clear: "importer" (default) or "mapprep" (terrain/mapprep.mjs clear: every top-level net, like
// the importer's; --keep-utilities keeps power lines, seaways and pipes).
// With terrain: mapprep clear (every top-level net and, since 2026-11-21, the base map's top-level buildings, whose
// landmarks would keep the old map height). Without terrain: the importer's clear. The earlier crash with mapprep clear
// came from it keeping power lines (DECISIONS-importer.md #27); it now deletes every net by default.
const clearWith = opt('--clear-with', flag('--mapprep-clear') || (useTerrain && MAPPREP) ? 'mapprep' : 'importer');
// DECISIONS-importer.md M2: with terrain, a base map with little water (its water sources stay after the terrain swap);
// without terrain, the M1 test spot on Corral Riches.
// With terrain: Sunshine Peninsula, the base map with the fewest nets of all 12 (terrain worker's in-game survey);
// without: the M1 flat test spot on Corral Riches.
const map = opt('--map', useTerrain ? 'Sunshine Peninsula' : 'Corral Riches');
const flatOffset = { offsetX: 2000, offsetZ: -4000 };

fs.mkdirSync(runDir, { recursive: true });
const summaryPath = path.join(runDir, 'summary.json');
const S = {
  started: new Date().toISOString(), dir: runDir, tag, map, terrain: useTerrain ? 'applied' : 'not requested (--apply-terrain)', clearWith,
  steps: [], ok: false,
};
let cs2Started = false;
const writeSummary = () => fs.writeFileSync(summaryPath, JSON.stringify(S, null, 1) + '\n');

function log(...a) { console.error(`[e2e ${new Date().toISOString().slice(11, 19)}]`, ...a); }

async function step(name, fn) {
  const t0 = Date.now();
  log(`${name} …`);
  const row = { step: name };
  S.steps.push(row);
  try {
    const r = await fn();
    row.ok = true;
    if (r !== undefined) row.result = r;
    return r;
  } catch (e) {
    row.ok = false;
    row.error = String(e?.message ?? e);
    throw e;
  } finally {
    row.seconds = Math.round((Date.now() - t0) / 100) / 10;
    log(`${name} ${row.ok ? 'ok' : 'FAILED'} (${row.seconds} s)${row.error ? ': ' + row.error : ''}`);
    writeSummary();
  }
}

function node(args, opts = {}) {
  return execFileSync(process.execPath, args, { cwd: REPO, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 << 20, ...opts });
}

// ------------------------------------------------------------------ hooks for other workers' steps
//
// Every tools/e2e/steps/*.mjs module is a step plug-in (transit/, buildings/, …), e.g.
//   export default { name: 'transit', after: 'import', enabled: (ctx) => !ctx.flag('--no-transit'),
//                    run: async (ctx) => { … return { counts } } };
// `after` is one of: import (after the road import and the M2 steps), connections (after the outside connections),
// shots (after the screenshots, before the save), end (just before the run ends). ctx = { bridge, repo, runDir, cs1x,
// package, planFile, placement {heights, offsetX, offsetZ}, importerDll, log, opt, flag }. A plug-in runs as an optional
// step named "<name>" (failure recorded, the run goes on); --no-<name> skips it.
const HOOKS = [];
const hookDir = path.join(path.dirname(fileURLToPath(import.meta.url)), 'steps');
if (fs.existsSync(hookDir)) {
  for (const f of fs.readdirSync(hookDir).filter((x) => x.endsWith('.mjs')).sort()) {
    try {
      const m = (await import(pathToFileURL(path.join(hookDir, f)).href)).default;
      if (m?.name && typeof m.run === 'function') HOOKS.push({ after: 'import', ...m, file: f });
    } catch (e) { console.error(`[e2e] step plug-in ${f} failed to load: ${e.message}`); }
  }
}

async function runHooks(after, ctx) {
  for (const h of HOOKS.filter((x) => x.after === after)) {
    if (flag(`--no-${h.name}`) || (h.enabled && !h.enabled(ctx))) continue;
    S[h.name] = await softStep(h.name, () => h.run(ctx));
  }
}

/** Like step(), but a failure is recorded and the run goes on (optional steps owned by other workers). */
async function softStep(name, fn) {
  try { return await step(name, fn); } catch { return undefined; }
}

/** terrain/mapprep.mjs (terrain worker): prints one JSON object. */
function mapprep(args, timeoutS = 900) {
  const out = node([path.join(REPO, 'terrain', 'mapprep.mjs'), ...args], { timeout: timeoutS * 1000 });
  try { return JSON.parse(out); } catch { return { output: out.slice(-2000) }; }
}

function buildTerrainLib() {
  execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', path.join(REPO, 'terrain', 'cs2-apply')], { stdio: ['ignore', 'ignore', 'inherit'] });
}

function gamectl(args, timeoutS = 1200) {
  return node([path.join(REPO, 'tools', 'gamectl', 'bin', 'gamectl.js'), ...args], { timeout: timeoutS * 1000 });
}

// ------------------------------------------------------------------ steps

async function exportCs1() {
  const given = opt('--cs1x');
  if (given) {
    if (!fs.existsSync(given)) throw new Error(`--cs1x ${given} not found`);
    return { cs1x: path.resolve(given), reused: true };
  }
  const save = opt('--save', 'Skibidi.02');
  const out = path.join(runDir, 'city.cs1x');
  const dll = path.join(REPO, 'cs1-exporter', 'bin', 'Release', 'net35', 'Cs1Exporter.dll');
  if (!fs.existsSync(dll)) throw new Error(`exporter not built: ${dll} (dotnet build -c Release cs1-exporter)`);
  await waitAndTakeLock('cs1', OWNER, SESSION, { log });
  try {
    // CS1 loads a save only once per game session (tools/PROTOCOL.md "CS1 notes"): start fresh with --load
    gamectl(['cs1', 'start', '--load', save, '--load-timeout', '900', '--quiet'], 1500);
    const r = JSON.parse(gamectl(['cs1', 'exec', dll, '--type', 'Cs1Exporter.Exporter', '--args', JSON.stringify({ path: out }), '--timeout', '600', '--json'], 700));
    if (!r.ok) throw new Error('export failed: ' + JSON.stringify(r));
    return { cs1x: out, save, bytes: fs.statSync(out).size, exporter: r.result };
  } finally {
    try { gamectl(['cs1', 'stop', '--quiet'], 300); } catch (e) { log('cs1 stop: ' + e.message); }
    releaseLock('cs1', OWNER);
  }
}

function makePlan(cs1x) {
  const pkg = path.join(runDir, 'package');
  const crop = opt('--crop');
  const args = [path.join(REPO, 'web', 'cli', 'cs1conv.ts'), 'plan', cs1x, ...(crop ? ['--crop', crop] : ['--auto-crop', opt('--auto-crop', '3000')]), '--out', pkg, '--install-rb'];
  const out = node(args);
  fs.writeFileSync(path.join(runDir, 'plan.log'), out);
  const plan = readPlan(pkg);
  return { package: pkg, crop: plan.crop, segments: plan.segments.length, nodes: plan.nodes.length, stats: plan.stats, rbConfigs: fs.readdirSync(path.join(pkg, 'roadbuilder')).length };
}

async function startCs2() {
  // A CS2 that is already running belongs to someone else (a lock can vanish while its game is still up): never reuse
  // it, wait until it is gone (same guard as terrain/run-mapprep.mjs).
  for (let i = 0; ; i++) {
    await waitAndTakeLock('cs2', OWNER, SESSION, { log });
    const st = await status('cs2');
    if (!st.running) break;
    releaseLock('cs2', OWNER);
    if (i > 240) throw new Error('CS2 kept running without a lock for an hour');
    log("CS2 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  cs2Started = true; // from here on, stop CS2 and release the lock on the way out
  const r = await start('cs2', {});
  const b = r.bridge ?? await connect('cs2');
  const city = await b.newGame(map, { name: tag.startsWith('cs1conv-') ? tag : `cs1conv-${tag}` });
  await b.pause();
  return { pid: r.pid, ready_s: r.ready_s, city };
}

async function clearBase(b, dll) {
  const h = PLAYABLE_HALF + 64;
  return b.exec(dll, { phase: 'tool', pump: 'tool', args: { op: 'clear', x0: -h, z0: -h, x1: h, z1: h }, timeout: 600 });
}

function terrain(cs1x, pkg) {
  const tdir = path.join(runDir, 'terrain');
  node([path.join(REPO, 'terrain', 'convert.mjs'), '--cs1x', cs1x, '--plan', path.join(pkg, 'cs1conv-plan.json'), '--out', tdir, '--no-png']);
  const lib = path.join(REPO, 'terrain', 'cs2-apply', 'bin', 'Release', 'net48', 'Cs1ConvTerrain.dll');
  if (!fs.existsSync(lib)) buildTerrainLib();
  node([path.join(REPO, 'terrain', 'apply.mjs'), '--terrain', tdir, '--cs1x', cs1x, '--sea-level', 'auto', '--shots', path.join(runDir, 'terrain-shots')], { timeout: 900e3 });
  const rep = JSON.parse(fs.readFileSync(path.join(tdir, 'apply-report.json'), 'utf8'));
  const pick = {};
  for (const k of Object.keys(rep)) if (/compare|vs|stats|sea|scale|error/i.test(k)) pick[k] = rep[k];
  return { dir: tdir, report: pick };
}

/**
 * Water after the terrain swap: the base map's rivers and lakes keep flowing over the new terrain (seen on Plains: the
 * river flooded the imported roads). CS1 water (.cs1x water.json) when the exporter provides it, otherwise a dry map:
 * every base-map water source removed and the simulation reset to the (lowered) sea level.
 */
async function water(b, cs1x, pkg) {
  const plan = path.join(pkg, 'cs1conv-plan.json');
  try {
    return { mode: 'cs1', ...mapprep(['water', '--cs1x', cs1x, '--plan', plan, '--terrain', path.join(runDir, 'terrain')]) };
  } catch (e) {
    if (!/water\.json/.test(String(e.stderr ?? e.message))) throw e;
  }
  const lib = path.join(REPO, 'terrain', 'cs2-apply', 'bin', 'Release', 'net48', 'Cs1ConvTerrain.dll');
  const set = await b.exec(lib, { type: 'Cs1ConvTerrain.MapPrep', phase: 'tool', args: { op: 'water.set', clear: true, sources: [] }, timeout: 300 });
  const reset = await b.exec(lib, { type: 'Cs1ConvTerrain.MapPrep', args: { op: 'water.reset', mode: 'sea' }, timeout: 300 });
  return { mode: 'dry (no water.json in the .cs1x)', set, reset };
}

async function importRoads(b, dll, pkg) {
  // With the CS1 terrain applied, "flat" heights (CS2 terrain + composition elevation, terrain features kept) at offset 0
  // place every node within a few metres of CS1. "exact" (--heights exact) crashed CS2 natively on freshly converted
  // terrain in every run (DECISIONS-importer.md M2), so it is opt-in.
  const placement = useTerrain ? { heights: opt('--heights', 'flat'), offsetX: 0, offsetZ: 0 } : { heights: 'flat', ...flatOffset };
  const report = path.join(runDir, 'report.json');
  const args = { op: 'import', plan: path.join(pkg, 'cs1conv-plan.json'), clear: true, report, trace: path.join(runDir, 'import-trace.txt'), ...placement };
  if (opt('--batch')) args.batchSize = Number(opt('--batch'));
  const r = await b.exec(dll, { phase: 'tool', pump: 'tool', args, timeout: 3600 });
  log('\n' + summary(r));
  return { placement, report, result: r.result, failures_by_reason: r.failures_by_reason, warnings_by_kind: r.warnings_by_kind, temp_after: r.temp_after, seconds: r.seconds, batches: r.batches?.length };
}

// ------------------------------------------------------------------ main

try {
  const ex = await step('export', exportCs1);
  S.cs1x = ex.cs1x;
  const plan = await step('plan', () => makePlan(ex.cs1x));
  const dll = buildImporter();
  await step('cs2', startCs2);
  const b = await connect('cs2');
  if (MAPPREP) await softStep('mapprep-build', buildTerrainLib);
  if (!flag('--keep-base-nets')) {
    // the terrain worker's clear also removes owned nets and outside-connection markers; ours is the fallback
    await step('clear', async () => {
      // mapprep's clear (roads/tracks + their outside-connection markers; keeps power lines, seaways and pipes)
      if (clearWith === 'mapprep') {
        if (!MAPPREP) throw new Error('--clear-with mapprep: terrain/mapprep.mjs is missing');
        return { by: 'terrain/mapprep.mjs', ...mapprep(flag('--keep-utilities') ? ['clear', '--keep-utilities'] : ['clear']) };
      }
      return { by: 'importer', ...(await clearBase(b, dll)) };
    });
  }
  if (useTerrain) {
    await step('terrain', () => terrain(ex.cs1x, plan.package));
    if (MAPPREP) await softStep('water', () => water(b, ex.cs1x, plan.package));
    // let CS2 finish processing the new heightmap before roads start modifying the terrain
    await step('settle', () => b.wait(600, Number(opt('--settle', '20')), { timeout: 600 }));
  }
  if (flag('--no-import')) throw new Error('stopped before the import (--no-import)');
  const imp = await step('import', () => importRoads(b, dll, plan.package));
  const placement = imp.placement;
  const planFile = path.join(plan.package, 'cs1conv-plan.json');
  const ctx = { bridge: b, repo: REPO, runDir, cs1x: ex.cs1x, package: plan.package, planFile, placement, importerDll: dll, log, opt, flag };
  // M2 plan additions, each switchable; optional steps (a failure is recorded, the run goes on)
  const m2 = (op) => b.exec(dll, { phase: 'tool', pump: 'tool', args: { op, plan: planFile, ...placement }, timeout: 900 });
  // zoning last: the traffic steps mark edges Updated, and the zone blocks must already be final when they're painted
  if (!flag('--no-traffic')) S.traffic = await softStep('traffic', () => m2('traffic'));
  if (!flag('--no-traffic-mod')) S.trafficMod = await softStep('trafficmod', () => m2('trafficmod'));
  if (!flag('--no-zoning')) S.zoning = await softStep('zoning', () => m2('zoning'));
  await runHooks('import', ctx);
  if (MAPPREP && useTerrain) {
    await softStep('connections', () => {
      const r = mapprep(['connections', '--cs1x', ex.cs1x, '--plan', path.join(plan.package, 'cs1conv-plan.json')]);
      delete r.connections;
      return r;
    });
    // optional checks (terrain worker): outside-connection lanes and vehicles, CS2 vs CS1 water depth
    await softStep('counts', () => mapprep(['counts']));
  }
  await runHooks('connections', ctx);
  if (opt('--simulate')) {
    // let the city run (buildings grow on the zones, traffic uses the junction settings), then pause for the shots
    await softStep('simulate', async () => {
      await b.speed(3); await b.resume();
      const r = await b.wait(0, Number(opt('--simulate')), { timeout: Number(opt('--simulate')) + 120 });
      await b.pause();
      return r;
    });
  }
  if (MAPPREP && useTerrain) {
    // after the simulation: lakes above the sea level only fill while the simulation runs (3–5 simulated minutes,
    // terrain worker); ResetToSealevel() fills only the sea at once
    await softStep('water-check', () => mapprep(['water-check', '--cs1x', ex.cs1x, '--plan', path.join(plan.package, 'cs1conv-plan.json')]));
  }
  S.shots = await step('shots', () => planShots(b, readPlan(plan.package), { dir: runDir, tag: 'roads', offsetX: placement.offsetX, offsetZ: placement.offsetZ, log }));
  if (!flag('--no-zoning')) {
    // zoned cells are only drawn while the zone tool is active
    await softStep('zone-shots', async () => {
      await b.exec(dll, { args: { op: 'view', tool: 'zone' } });
      const files = await planShots(b, readPlan(plan.package), { dir: runDir, tag: 'zones', offsetX: placement.offsetX, offsetZ: placement.offsetZ, log });
      await b.exec(dll, { args: { op: 'view', tool: 'default' } });
      return files;
    });
  }
  if (!flag('--no-zoning')) {
    // the zones as they are at the end (after the simulation): what persisted
    S.zoningCheck = await softStep('zoning-check', () => b.exec(dll, { phase: 'tool', pump: 'tool', args: { op: 'zoning', plan: planFile, checkOnly: true, ...placement }, timeout: 600 }));
  }
  await runHooks('shots', ctx);
  if (!flag('--no-save')) {
    const d = new Date(), p = (n) => String(n).padStart(2, '0');
    const name = opt('--save-name') ?? `cs1conv-e2e-${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}`;
    S.save = await step('save', () => b.saveWrite(name));
  }
  await runHooks('end', ctx);
  const res = imp.result ?? {};
  S.ok = res.segments_failed === 0 && res.segments_built === res.segments_planned && (imp.temp_after?.temp_any ?? 0) === 0;
} catch (e) {
  log('run failed: ' + (e?.stack ?? e));
} finally {
  if (cs2Started && !flag('--keep-cs2')) {
    await step('stop', async () => { await stop('cs2', {}); }).catch(() => {});
    releaseLock('cs2', OWNER);
  }
  S.finished = new Date().toISOString();
  writeSummary();
  log(`summary: ${summaryPath} (ok=${S.ok})`);
  process.exitCode = S.ok ? 0 : 1;
}
