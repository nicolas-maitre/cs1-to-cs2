// Release acceptance test: the real user flow with the release artefacts, no exec of import code.
//
//   node tools/release/acceptance.mjs [--cs1x <file>] [--map "Corral Riches"] [--auto-crop 3000 | --crop cx,cz,size] [--simulate 60] [--skip-build] [--keep-mod]
//
// 1. build      node tools/release/build.mjs (unless --skip-build)
// 2. install    the importer mod from release/<v>/cs2-importer-<v>.zip into CS2's Mods folder (the dev build there is
//               backed up and put back at the end, unless --keep-mod)
// 3. package    node web/cli/cs1conv.ts plan <cs1x> --auto-crop <m> --out <run>/package   (what the web page downloads)
// 4. cs2        cs2.lock, start CS2 (main menu)
// 5. rb-configs Options → "Install Road Builder configs" (the mod's own button), then quit: Road Builder reads them at start
// 6. restart    start CS2 again, new city on a flat map, paused
// 7. check      Options → package folder, "Clear the area" on, press Check; then "Prepare map" (terrain, water) and 20 s to settle
// 8. import     press "Import into this city", wait for the mod to finish
// 9. verify     lane directions (op "lanedirs" vs the export's CS1 lanes: tools/release/lanedirs-compare.mjs), simulate
//               (--simulate s), public-transport vehicles by kind (transit exec library, read-only), utilities supply
//               (tools/qa, read-only), screenshots, the mod's
//               import report, save as cs1conv-acceptance-<date>, quit, release the lock
//
// Pressing the Options buttons from a script: the Cs2Importer exec library's op "mod" sets the Options values and calls
// the same Actions methods the buttons call (no other path into the settings UI). Everything else is the mod's own code.
// Writes <repo>/.gamectl/acceptance/<timestamp>/summary.json.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { execFileSync } from 'node:child_process';
import { connect, start, stop, status } from '../gamectl/lib/index.js';
import { REPO, LOCAL_DIR, waitAndTakeLock, releaseLock, planShots } from '../../cs2-importer/lib/driver.mjs';
import { checkVersions } from './build.mjs';
import { buildQa, measureUtilities, summariseUtilities } from '../qa/lib/utilities.mjs';

const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 ? argv[i + 1] : d; };
const flag = (n) => argv.includes('--' + n);
const log = (...a) => console.error(`[acceptance ${new Date().toISOString().slice(11, 19)}]`, ...a);
const OWNER = 'acceptance', SESSION = 'release acceptance test (tools/release/acceptance.mjs, buildings worker)';

const tag = new Date().toISOString().replace(/[-:]/g, '').replace(/\..*/, '').replace('T', '-');
const runDir = path.join(REPO, '.gamectl', 'acceptance', tag);
fs.mkdirSync(runDir, { recursive: true });
const cs1x = path.resolve(opt('cs1x', path.join(LOCAL_DIR, 'samples', 'skibidi.cs1x')));
const map = opt('map', 'Corral Riches');
const MODS = path.join(os.homedir(), 'AppData', 'LocalLow', 'Colossal Order', 'Cities Skylines II', 'Mods', 'Cs2Importer');
const LIB = path.join(REPO, 'cs2-importer', 'bin', 'Release', 'net48', 'Cs2Importer.dll');
const TRANSIT_LIB = path.join(REPO, 'transit', 'cs2-exec', 'bin', 'Release', 'net48', 'Cs1ConvTransit.dll');
const TRANSIT_TYPE = 'Cs1ConvTransit.Ops';

const S = { started: new Date().toISOString(), dir: runDir, cs1x, map, steps: [] };
const save = () => fs.writeFileSync(path.join(runDir, 'summary.json'), JSON.stringify(S, null, 1));
async function step(name, fn) {
  const t0 = Date.now();
  log(`${name} …`);
  try {
    const result = await fn();
    S.steps.push({ step: name, ok: true, seconds: (Date.now() - t0) / 1000, result });
    log(`${name} ok (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
    save();
    return result;
  } catch (e) {
    S.steps.push({ step: name, ok: false, seconds: (Date.now() - t0) / 1000, error: String(e.message ?? e).slice(0, 2000) });
    save();
    throw e;
  }
}
const node = (args, o = {}) => execFileSync(process.execPath, args, { cwd: REPO, encoding: 'utf8', maxBuffer: 1 << 26, ...o });

/** Minimal zip reader (stored / deflate) for the release zip, so no extra tool is needed. */
function unzip(file, outDir) {
  const zlib = awaitZlib;
  const buf = fs.readFileSync(file);
  const eocd = buf.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  let p = buf.readUInt32LE(eocd + 16);
  const out = [];
  for (let i = 0; i < buf.readUInt16LE(eocd + 10); i++) {
    const method = buf.readUInt16LE(p + 10), csize = buf.readUInt32LE(p + 20), nlen = buf.readUInt16LE(p + 28);
    const xlen = buf.readUInt16LE(p + 30), clen = buf.readUInt16LE(p + 32), local = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nlen);
    const start = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28);
    const raw = buf.subarray(start, start + csize);
    if (!name.endsWith('/')) {
      const dest = path.join(outDir, name);
      fs.mkdirSync(path.dirname(dest), { recursive: true });
      fs.writeFileSync(dest, method === 8 ? zlib.inflateRawSync(raw) : raw);
      out.push(dest);
    }
    p += 46 + nlen + xlen + clen;
  }
  return out;
}
const awaitZlib = await import('node:zlib');

const modExec = (b, args, o = {}) => b.exec(LIB, { args: { op: 'mod', ...args }, timeout: 600, ...o });

let locked = false, backup = null, installed = false;
try {
  const v = checkVersions();
  S.versions = { product: v.product, components: v.components };
  const rel = path.join(REPO, 'release', v.product);

  if (!flag('skip-build')) await step('build', () => node([path.join(REPO, 'tools', 'release', 'build.mjs')], { timeout: 900e3 }).split('\n').slice(-3).join('\n'));
  await step('build-exec-lib', () => execFileSync('dotnet', ['build', path.join(REPO, 'cs2-importer', 'Cs2Importer.csproj'), '-c', 'Release', '-nologo', '-v', 'q'], { encoding: 'utf8' }).slice(-300));

  await step('package', () => {
    const pkg = path.join(runDir, 'package');
    const cropArgs = opt('crop') ? ['--crop', opt('crop')] : ['--auto-crop', opt('auto-crop', '3000')]; // --crop cx,cz,size: e.g. a square a train line leaves
    const outText = node([path.join(REPO, 'web', 'cli', 'cs1conv.ts'), 'plan', cs1x, ...cropArgs, '--out', pkg], { timeout: 600e3 });
    const plan = JSON.parse(fs.readFileSync(path.join(pkg, 'cs1conv-plan.json'), 'utf8'));
    S.package = pkg;
    return {
      segments: plan.stats?.segments, rbConfigs: plan.stats?.rbConfigs,
      sections: Object.keys(plan).filter((k) => plan[k] && typeof plan[k] === 'object' && !['crop', 'transform', 'bounds', 'prefabs', 'nodes', 'segments', 'stats', 'generator', 'source', 'requires'].includes(k)),
      buildings: plan.buildings?.items?.length ?? 0, objects: plan.objects?.items?.length ?? 0, surfaces: plan.surfaces?.items?.length ?? 0,
      districts: plan.districts?.items?.length ?? 0, summary: outText.split('\n').filter((l) => /^\s{2}\S/.test(l)).slice(-16),
    };
  });

  // Lock first: the Mods folder is shared with whoever runs CS2.
  for (let i = 0; ; i++) {
    await waitAndTakeLock('cs2', OWNER, SESSION, { log });
    if (!(await status('cs2')).running) break;
    releaseLock('cs2', OWNER);
    if (i > 240) throw new Error('CS2 kept running without a lock for an hour');
    log("CS2 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  locked = true;

  await step('install-mod', () => {
    const zip = path.join(rel, `cs2-importer-${v.components['cs2-importer']}.zip`);
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cs2imp-'));
    const files = unzip(zip, tmp);
    const dll = files.find((f) => f.endsWith('Cs2ImporterMod.dll'));
    const target = path.join(MODS, 'Cs2ImporterMod.dll');
    if (fs.existsSync(target)) { backup = path.join(runDir, 'Cs2ImporterMod.dev-backup.dll'); fs.copyFileSync(target, backup); }
    fs.mkdirSync(MODS, { recursive: true });
    fs.copyFileSync(dll, target);
    installed = true;
    return { zip, files: files.map((f) => path.relative(tmp, f)), installedTo: MODS, backedUp: !!backup, bytes: fs.statSync(target).size };
  });

  let b = null;
  await step('cs2-start', async () => { const r = await start('cs2', {}); b = r.bridge ?? await connect('cs2'); return { pid: r.pid, ready_s: r.ready_s }; });
  await step('rb-configs', async () => {
    const r = await modExec(b, { set: { PackageFolder: S.package }, press: 'InstallRoadBuilderConfigs' });
    return { status: r.StatusText ?? r.status, result: r };
  });
  await step('cs2-restart', async () => {
    await stop('cs2', {});
    const r = await start('cs2', {});
    b = r.bridge ?? await connect('cs2');
    const city = await b.newGame(map, { name: `cs1conv-acceptance-${tag}` });
    await b.pause();
    return { pid: r.pid, ready_s: r.ready_s, city };
  });
  await step('check', async () => {
    const r = await modExec(b, { set: { PackageFolder: S.package, ClearArea: true, ApplyZoning: true, ApplyTrafficSettings: true, ApplyTrafficMod: true, ApplyBuildings: true, ApplyTransit: true, OffsetX: 0, OffsetZ: 0 }, press: 'Check' });
    return { status: r.StatusText, result: r };
  });
  // Check tells a new city to press "Prepare map" first (terrain, water, map clearing from the package).
  if (!flag('no-prepare')) {
    await step('prepare-map', async () => {
      const r = await modExec(b, { press: 'PrepareMap', wait: true }, { phase: 'tool', pump: 'tool', timeout: 3000 });
      return { status: r.StatusText, progress_seen: r.progress_seen, reportPath: r.LastReportPath };
    });
    await step('settle', async () => { await b.call('sim.set', { paused: false, speed: 1 }); await b.wait(1, 20, { timeout: 120 }); await b.pause(); return { seconds: 20 }; });
  }
  S.import = await step('import', async () => {
    const r = await modExec(b, { press: 'Import', wait: true }, { phase: 'tool', pump: 'tool', timeout: 3000 });
    let report = null;
    if (r.LastReportPath && fs.existsSync(r.LastReportPath)) {
      report = JSON.parse(fs.readFileSync(r.LastReportPath, 'utf8'));
      fs.copyFileSync(r.LastReportPath, path.join(runDir, 'import-report.json'));
    }
    return { status: r.StatusText, progress_seen: r.progress_seen, reportPath: r.LastReportPath, report: report && Object.fromEntries(Object.entries(report).map(([k, x]) => [k, JSON.stringify(x).slice(0, 1500)])) };
  });
  // One-way and asymmetric roads must run the way they did in CS1 (left-hand cities included): dump every plan
  // segment's CS2 car-lane directions (importer op "lanedirs") and compare them with the export's CS1 lanes.
  await step('lane-directions', async () => {
    const r = await b.exec(LIB, { args: { op: 'lanedirs', plan: S.package }, timeout: 600 });
    const dump = path.join(runDir, 'lanedirs.json');
    fs.writeFileSync(dump, JSON.stringify(r, null, 1));
    let out;
    try {
      out = node([path.join(REPO, 'tools', 'release', 'lanedirs-compare.mjs'), cs1x, path.join(S.package, 'cs1conv-plan.json'), dump, '--json', path.join(runDir, 'lanedirs-compare.json')]);
    } catch (e) { out = String(e.stdout ?? ''); }
    const c = JSON.parse(fs.readFileSync(path.join(runDir, 'lanedirs-compare.json'), 'utf8'));
    return { segments: r.segments, missing: r.missing, cs2LeftHandTraffic: r.cs2_left_hand_traffic, byKind: c.byKind, reversed: c.reversed, mirrored: c.mirrored, other: c.other, examples: c.examples.slice(0, 5), ok: c.ok, out: out ? undefined : 'no output' };
  });
  await step('simulate', async () => { await b.call('sim.set', { paused: false, speed: 3 }); await b.wait(1, Number(opt('simulate', '60')), { timeout: 600 }); await b.pause(); return { seconds: Number(opt('simulate', '60')) }; });
  // Public-transport vehicles after the simulation (read-only count from the transit exec library): are buses,
  // trams, metros and trains actually running? Lines without vehicles show up here, not in the import status.
  if (fs.existsSync(TRANSIT_LIB)) {
    await step('vehicles', async () => {
      const r = await b.exec(TRANSIT_LIB, { type: TRANSIT_TYPE, args: { op: 'counts' }, timeout: 300 });
      const by = r.byPrefab ?? {};
      const kind = (n) => /subway|metro/i.test(n) ? 'metro' : /train|engine|locomotive/i.test(n) ? 'train' : /tram/i.test(n) ? 'tram' : /bus/i.test(n) ? 'bus' : 'other';
      const byKind = {};
      for (const [n, c] of Object.entries(by)) { const k = kind(n); byKind[k] = (byKind[k] ?? 0) + (typeof c === 'number' ? c : (c?.count ?? 0)); }
      return { total: r.publicTransportVehicles, byKind, byPrefab: by, depots: r.depots, routes: r.routes };
    });
  }
  // Utilities (docs/DECISIONS.md #109, tools/qa): electricity / water / sewage supply after the simulation, read-only.
  // Informational: its ok=false doesn't fail the acceptance (CS2's economy needs time); the lines go into summary.json.
  // An error in the check itself (the QA library doesn't build, the op throws) is recorded, not thrown, so the run
  // still takes its screenshots and saves.
  await step('utilities', async () => {
    try {
      buildQa();
      const r = await measureUtilities(b, { minutes: 0, listLimit: 20 });
      const s = summariseUtilities(r);
      S.utilities = { ok: s.ok, share: s.share, problems: s.problems, lines: s.lines };
    } catch (e) {
      S.utilities = { ok: false, error: String(e.message ?? e).slice(0, 1000) };
    }
    return S.utilities;
  });
  await step('shots', async () => {
    const plan = JSON.parse(fs.readFileSync(path.join(S.package, 'cs1conv-plan.json'), 'utf8'));
    return planShots(b, plan, { dir: runDir, tag: 'acceptance', log });
  });
  await step('save', () => b.saveWrite(`cs1conv-acceptance-${tag}`));
  await step('stop', () => stop('cs2', {}));
  S.ok = S.steps.every((s) => s.ok);
} catch (e) {
  log('FAILED:', e.message);
  S.ok = false;
  S.error = String(e.message ?? e);
  try { if ((await status('cs2')).running && locked) await stop('cs2', {}); } catch {}
} finally {
  if (installed && backup && !flag('keep-mod')) {
    try { fs.copyFileSync(backup, path.join(MODS, 'Cs2ImporterMod.dll')); S.modRestored = true; } catch (e) { S.modRestored = String(e.message); }
  }
  if (locked) releaseLock('cs2', OWNER);
  S.finished = new Date().toISOString();
  save();
  log(`summary: ${path.join(runDir, 'summary.json')} (ok=${S.ok})`);
}
