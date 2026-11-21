// Release acceptance test: the real user flow with the release artefacts, no exec of import code.
//
//   node tools/release/acceptance.mjs [--cs1x <file>] [--map "Corral Riches"] [--auto-crop 3000 | --crop cx,cz,size] [--simulate 60] [--skip-build] [--keep-mod] [--mod-set '{"AddTransformers":true}']
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
// 9. verify     report completeness (every registered and enabled step has an entry), heights and tunnels (op heights,
//               CS1 tunnel count), utility stubs (terrain op oc.stubcheck, step utility-stubs), lane directions (op "lanedirs" vs the export's CS1 lanes: tools/release/lanedirs-compare.mjs), simulate
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
import { DLL as TERRAIN_DLL } from '../../terrain/apply.mjs';

const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 ? argv[i + 1] : d; };
const flag = (n) => argv.includes('--' + n);
// --mod-set '{"AddTransformers":true}': extra Options values for this run (opt-ins); booleans are switched back after the save
const MOD_SET = JSON.parse(opt('mod-set', '{}'));
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
  await step('build-exec-lib', () => ['cs2-importer/Cs2Importer.csproj', 'terrain/cs2-apply/TerrainApply.csproj', 'transit/cs2-exec/Cs1ConvTransit.csproj'].map((p) => execFileSync('dotnet', ['build', path.join(REPO, p), '-c', 'Release', '-nologo', '-v', 'q'], { encoding: 'utf8' }).slice(-200)).join(' | '));

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
    const r = await modExec(b, { set: { PackageFolder: S.package, ClearArea: true, ApplyZoning: true, ApplyTrafficSettings: true, ApplyTrafficMod: true, ApplyLaneConnections: true, ApplyBuildings: true, ApplyTransit: true, ApplyConnections: true, OffsetX: 0, OffsetZ: 0, ...MOD_SET }, press: 'Check' });
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
  // Report completeness: every step this launch of the mod registered (its log's "import step registered: X" lines:
  // buildings, objects, districts, surfaces, utilities, powerhook, transit, …) and every built-in step the Options above
  // enable must have an entry in the import report. A missing one means that step never ran (coordinator, RC3).
  await step('report-complete', async () => {
    const report = fs.existsSync(path.join(runDir, 'import-report.json')) ? JSON.parse(fs.readFileSync(path.join(runDir, 'import-report.json'), 'utf8')) : null;
    if (!report) throw new Error('no import report');
    const modLog = path.join(os.homedir(), 'AppData', 'LocalLow', 'Colossal Order', 'Cities Skylines II', 'Logs', 'Cs2ImporterMod.log');
    if (fs.existsSync(modLog)) fs.copyFileSync(modLog, path.join(runDir, 'Cs2ImporterMod.log')); // this game session's log (rewritten at every start)
    const registered = fs.existsSync(modLog) ? [...fs.readFileSync(modLog, 'utf8').matchAll(/import step registered: (\S+)/g)].map((m) => m[1]) : [];
    if (!registered.length) throw new Error(`no "import step registered" lines in ${modLog}`);
    // built-in steps with every Apply* option on (the check step sets them all): Actions.Import
    const builtIn = ['import', 'traffic', 'trafficmod', 'lanes', 'zoning', 'connections', 'settle', 'lanesrepair'];
    const expected = [...new Set([...builtIn, ...registered])];
    const missing = expected.filter((k) => !(k in report));
    const res = { expected, registered, present: Object.keys(report), missing };
    if (missing.length) throw new Error(`import report lacks ${missing.length} step(s): ${missing.join(', ')} (${JSON.stringify(res).slice(0, 1200)})`);
    return res;
  }).catch(() => {});
  // Heights and tunnels (RC3): the importer's op "heights" finds a CS2 road for every plan segment the import built (its
  // "missing" equals the import's failures), and every CS1 tunnel segment was built as a tunnel.
  await step('heights-tunnels', async () => {
    const h = await b.exec(LIB, { args: { op: 'heights', plan: path.join(S.package, 'cs1conv-plan.json'), out: path.join(runDir, 'heights.json') }, timeout: 900 });
    const report = JSON.parse(fs.readFileSync(path.join(runDir, 'import-report.json'), 'utf8'));
    const imp = report.import ?? {};
    const res = { segments: h.segments, missing: h.missing, importFailed: imp.segments_failed, tunnelPlanned: imp.tunnel_planned, cs1TunnelsBuilt: imp.cs1_tunnel_segments_built, tunnelBuilt: imp.tunnel_built };
    const bad = [];
    if (h.missing > (imp.segments_failed ?? 0)) bad.push(`heights: ${h.missing} plan segments without a CS2 road, the import failed ${imp.segments_failed ?? 0}`);
    if (imp.tunnel_planned != null && imp.cs1_tunnel_segments_built !== imp.tunnel_planned) bad.push(`tunnels: ${imp.cs1_tunnel_segments_built} of ${imp.tunnel_planned} CS1 tunnel segments built as tunnels`);
    if (bad.length) throw new Error(bad.join('; ') + ` (${JSON.stringify(res)})`);
    return res;
  }).catch(() => {});
  // Utility outside connections (worker-2, docs/DECISIONS.md #136/#144): every stub the import's Outside connections step
  // built must leave its road junction whole (by real edge endpoints, #148) and carry its marker. MapPrep oc.stubcheck,
  // read-only. A split, or no electricity / water outside connection, fails the step; stubs not touching a road are listed.
  await step('utility-stubs', async () => {
    if (!fs.existsSync(TERRAIN_DLL)) throw new Error('terrain exec library not built: dotnet build -c Release terrain/cs2-apply');
    const r = await b.exec(TERRAIN_DLL, { type: 'Cs1ConvTerrain.MapPrep', args: { op: 'oc.stubcheck' }, timeout: 300 });
    fs.writeFileSync(path.join(runDir, 'stubcheck.json'), JSON.stringify(r, null, 1));
    const res = { markers: r.markers, split: r.split, ok: r.ok, notTouchingRoad: r.notTouchingRoad };
    if (r.split > 0) throw new Error(`${r.split} utility stub(s) split a road junction: ${JSON.stringify((r.stubs ?? []).filter((x) => x.split).slice(0, 3)).slice(0, 1500)}`);
    if (!r.markers) throw new Error('no electricity or water outside connection after the import');
    return res;
  }).catch(() => {});
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
    // the city drives on the CS1 city's side (DECISIONS.md #138): a left-hand plan in a right-hand city is a failed run
    const planLht = JSON.parse(fs.readFileSync(path.join(S.package, 'cs1conv-plan.json'), 'utf8')).source?.leftHandTraffic;
    if (typeof planLht === 'boolean' && r.cs2_left_hand_traffic !== planLht)
      throw new Error(`the city drives on the ${r.cs2_left_hand_traffic ? 'left' : 'right'}, the CS1 city on the ${planLht ? 'left' : 'right'}`);
    return { segments: r.segments, missing: r.missing, cs1LeftHandTraffic: planLht ?? null, cs2LeftHandTraffic: r.cs2_left_hand_traffic, byKind: c.byKind, reversed: c.reversed, mirrored: c.mirrored, other: c.other, examples: c.examples.slice(0, 5), ok: c.ok, out: out ? undefined : 'no output' };
  });
  // TM:PE lane connections (docs/DECISIONS.md #126): re-measure what the mod wrote, read-only (op "lanes" checkOnly):
  // the geometric arm check (every connection's arms against CS1's directions), the written-vs-intended match, and the
  // junctions left to CS2 because they couldn't be mapped confidently. A wrong arm or turn fails the step.
  // a failed lane check is recorded, the run goes on (the reload check and the rest still run)
  await step('lane-connections', async () => {
    const r = await b.exec(LIB, { phase: 'tool', pump: 'tool', args: { op: 'lanes', plan: path.join(S.package, 'cs1conv-plan.json'), checkOnly: true, diagnostics: 40 }, timeout: 900 });
    fs.writeFileSync(path.join(runDir, 'lanecheck.json'), JSON.stringify(r, null, 1));
    if (r.skipped) return { skipped: r.skipped };
    const ac = r.arm_check ?? {}, tc = r.turn_check ?? {};
    const res = { refs: r.lane_refs, refsMatched: r.lane_refs_matched, matchRate: r.match_rate, outcomes: r.outcomes, arm_check: { arms_ok: ac.arms_ok, arms_off: ac.arms_off, junctions_off: ac.junctions_off, junctions: ac.junctions }, turn_check: tc.checked == null ? undefined : { checked: tc.checked, ok: tc.ok, wrong: tc.wrong, examples: (tc.wrong_examples ?? []).slice(0, 5) } };
    const wrongTurns = tc.wrong ?? 0;
    if (wrongTurns > 0) throw new Error(`${wrongTurns} written lane connection(s) go to a different arm than in CS1: ${JSON.stringify(res).slice(0, 1500)}`);
    return res;
  }).catch(() => {});
  // The roads must be the same after a save and reload (DECISIONS-importer.md #64): per plan segment, the car lanes
  // (op lanedirs) and each edge's prefab and composition (op edgestate) before saving vs after reloading. A road whose
  // composition CS2 only re-selects on load (bus stops placed without updating the edge) gains a bus bay on reload, and
  // the lane connections written for the old lanes break.
  await step('roads-reload', async () => {
    const plan = path.join(S.package, 'cs1conv-plan.json');
    const dump = async () => ({
      lanes: (await b.exec(LIB, { args: { op: 'lanedirs', plan }, timeout: 900 })).lanes ?? {},
      edges: (await b.exec(LIB, { args: { op: 'edgestate', plan, lanes: false }, timeout: 900 })).edges ?? {},
    });
    const key = (es) => (es ?? []).map((e) => `${e.prefab} | ${e.comp_flags} | ${e.comp_lanes}`).join(' ; ');
    const before = await dump();
    const name = `cs1conv-acceptance-${tag}-roads`;
    await b.saveWrite(name);
    await b.load(name);
    await b.pause();
    await b.wait(120, 4, { timeout: 300 });
    const after = await dump();
    const changed = [];
    for (const id of Object.keys(before.lanes)) {
      const lb = before.lanes[id], la = after.lanes[id], cb = key(before.edges[id]), ca = key(after.edges[id]);
      if (la === undefined) changed.push(`${id}: gone after reload`);
      else if (lb !== la || cb !== ca) changed.push(`${id}: ${lb} → ${la}${cb !== ca ? ` | ${cb} → ${ca}` : ''}`);
    }
    fs.writeFileSync(path.join(runDir, 'roads-reload.json'), JSON.stringify({ changed, before, after }, null, 1));
    const res = { segments: Object.keys(before.lanes).length, changed: changed.length, examples: changed.slice(0, 10) };
    if (changed.length > 0) throw new Error(`${changed.length} road(s) differ after a save and reload: ${JSON.stringify(res).slice(0, 1500)}`);
    return res;
  }).catch(() => {});
  // The lane connections must survive a save and reload: the Traffic mod's loader drops a junction's data when it fails
  // its checks ("couldn't load data from N of M intersections"). Predicted before saving (op lanedata), then measured
  // after a real save + load: nodes with data, loader failures, and the turn check again.
  await step('lane-connections-reload', async () => {
    const before = await b.exec(LIB, { args: { op: 'lanedata' }, timeout: 300 });
    if (before.skipped) return { skipped: before.skipped };
    const name = `cs1conv-acceptance-${tag}-lanes`;
    await b.saveWrite(name);
    await b.load(name);
    await b.pause();
    await b.wait(120, 4, { timeout: 300 });
    const after = await b.exec(LIB, { args: { op: 'lanedata' }, timeout: 300 });
    const r = await b.exec(LIB, { phase: 'tool', pump: 'tool', args: { op: 'lanes', plan: path.join(S.package, 'cs1conv-plan.json'), checkOnly: true, diagnostics: 40 }, timeout: 900 });
    fs.writeFileSync(path.join(runDir, 'lanecheck-after-reload.json'), JSON.stringify({ before, after, lanes: r }, null, 1));
    const tc = r.turn_check ?? {};
    const res = { before: { nodes: before.nodes_with_data, wouldFail: before.would_fail, reasons: before.reasons }, after: { nodes: after.nodes_with_data, wouldFail: after.would_fail }, turn_check: { checked: tc.checked, ok: tc.ok, wrong: tc.wrong } };
    // per junction: roads whose lane layout changed on reload (the Traffic mod drops lane data that points at the old lanes)
    const changed = (r.problem_sites ?? []).filter((x) => x.problem === 'lane_count_differs');
    res.laneLayoutChangedOnReload = { junctions: [...new Set(changed.map((x) => x.node))], roads: changed.slice(0, 20).map((x) => `${x.node}/${x.segment} ${x.prefab} ${x.cs1_lanes}→${x.cs2_lanes}`) };
    const lost = (before.nodes_with_data ?? 0) - (after.nodes_with_data ?? 0);
    if ((before.would_fail ?? 0) > 0 || lost > 0 || (tc.wrong ?? 0) > 0) throw new Error(`lane connections don't survive a reload: ${JSON.stringify({ ...res, lost }).slice(0, 1500)}`);
    return res;
  }).catch(() => {});
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
  if (Object.keys(MOD_SET).length) await step('mod-set-restore', () => modExec(b, { set: Object.fromEntries(Object.entries(MOD_SET).filter(([, v]) => typeof v === 'boolean').map(([k, v]) => [k, !v])) }).then(() => ({ restored: Object.keys(MOD_SET) })));
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
