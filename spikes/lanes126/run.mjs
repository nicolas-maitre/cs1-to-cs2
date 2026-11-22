// #126 (docs/DECISIONS.md): lane connections at the six-arm junction and the turn check on whole cities, one CS2 session.
// For each package: import the roads at the flat test spot (offNode on), write the TM:PE lane connections (importer op
// "lanes"), keep its arm_check / turn_check / outcomes; for `--node` junctions also the Traffic mod's lane connector view
// (op "laneview") as screenshots.
//   node spikes/lanes126/run.mjs <out dir> <tag>=<package dir>[@node,node] ... [--keep]
// Takes cs2.lock (owner lanes126); RB configs of the packages must be installed before (cs1conv plan --install-rb).
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const argv = process.argv.slice(2);
const has = (k) => argv.includes(k);
const pos = argv.filter((a, i) => !a.startsWith('--') && argv[i - 1] !== '--saveload' && argv[i - 1] !== '--splits');
const out = path.resolve(pos[0]);
const pkgs = pos.slice(1).map((p) => { const [tag, rest] = p.split('='); const [dir, nodes] = rest.split('@'); return { tag, dir: path.resolve(dir), nodes: nodes ? nodes.split(',').map(Number) : [] }; });
const log = (...m) => console.error(`[lanes126 ${new Date().toISOString().slice(11, 19)}]`, ...m);
const OWNER = 'lanes126';
const OFF = { offsetX: 2000, offsetZ: -4000 };
fs.mkdirSync(out, { recursive: true });
execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', path.join(repo, 'cs2-importer')], { stdio: ['ignore', 'ignore', 'inherit'] });
const LIB = path.join(repo, 'cs2-importer', 'bin', 'Release', 'net48', 'Cs2Importer.dll');
execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', path.join(repo, 'spikes', 'nc-lane-align')], { stdio: ['ignore', 'ignore', 'inherit'] });
const SPIKE = path.join(repo, 'spikes', 'nc-lane-align', 'bin', 'Release', 'net48', 'NcAlign.dll');
// --splits <dir>: junction lane overshoot (NcAlign jlanes) at the small-angle junctions in <dir>/splits-<city>.json, city = tag before '-'
const splitsDir = argv.includes('--splits') ? path.resolve(argv[argv.indexOf('--splits') + 1]) : null;

let started = false;
const S = { started: new Date().toISOString(), runs: [] };
const save = () => fs.writeFileSync(path.join(out, 'summary.json'), JSON.stringify(S, null, 1));
try {
  for (let i = 0; ; i++) {
    await waitAndTakeLock('cs2', OWNER, '#126 lane connections (spikes/lanes126/run.mjs, worker-5)', { log, timeoutMin: 240 });
    if (!(await status('cs2')).running) break;
    releaseLock('cs2', OWNER);
    log("CS2 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  started = true;
  const r = await start('cs2', {});
  const b = r.bridge ?? await connect('cs2');
  await b.newGame('Corral Riches', { name: 'cs1conv-lanes126' });
  await b.pause();
  const ex = (args, timeout = 1200) => b.exec(LIB, { args, phase: 'tool', pump: 'tool', timeout });
  async function aim(cam) {
    for (let k = 0; k < 6; k++) {
      await b.cameraSet(cam);
      await b.wait(30, 1);
      const got = await b.cameraGet();
      if (Math.hypot(got.x - cam.x, got.z - cam.z) < 2) return got;
    }
    return b.cameraGet();
  }
  for (const p of pkgs) {
    const plan = path.join(p.dir, 'cs1conv-plan.json');
    const planJson = JSON.parse(fs.readFileSync(plan, 'utf8'));
    // a left-hand CS1 city needs a left-hand CS2 city: a fresh city, its driving side set before any road (op drivingside)
    if (planJson.source?.leftHandTraffic === true) {
      log(p.tag, 'new left-hand city');
      await b.newGame('Corral Riches', { name: 'cs1conv-lanes126-lht' });
      await b.pause();
      log(p.tag, JSON.stringify(await b.exec(LIB, { args: { op: 'drivingside', plan }, timeout: 120 })));
    }
    log(p.tag, 'import');
    const imp = await ex({ op: 'import', plan, clear: true, heights: 'flat', offNode: true, ...OFF }, 3600);
    await b.wait(60, 2);
    const row = { tag: p.tag, built: imp.result?.segments_built, planned: imp.result?.segments_planned, failures: imp.failures_by_reason };
    if (splitsDir) {
      const sf = path.join(splitsDir, `splits-${p.tag.split('-')[0]}.json`);
      if (fs.existsSync(sf)) {
        const sp = JSON.parse(fs.readFileSync(sf, 'utf8'));
        const jl = await b.exec(SPIKE, { args: { op: 'jlanes', points: sp.map((x) => [x.node, x.x + OFF.offsetX, x.z + OFF.offsetZ]), r: 35 }, timeout: 300 });
        fs.writeFileSync(path.join(out, `${p.tag}-jlanes.json`), JSON.stringify(jl, null, 1));
        const js = jl.junctions.filter((j) => j.found);
        row.jlanes = { junctions: js.length, of: sp.length, maxBulge: Math.max(0, ...js.map((j) => j.maxBulge)), meanMaxBulge: js.length ? +(js.reduce((a, j) => a + j.maxBulge, 0) / js.length).toFixed(2) : 0, sLanes: js.reduce((a, j) => a + j.sLanes, 0), lanes: js.reduce((a, j) => a + j.lanes, 0) };
        log(p.tag, 'junction lanes', JSON.stringify(row.jlanes));
      }
    }
    for (const id of p.nodes) {
      // CS2's own lanes first, for the before shot
      row['before_' + id] = await view(id, 'cs2-default');
    }
    log(p.tag, 'lanes');
    const lanes = await ex({ op: 'lanes', plan, offNode: true, diagnostics: 80, ...OFF });
    fs.writeFileSync(path.join(out, `${p.tag}-lanes.json`), JSON.stringify(lanes, null, 1));
    row.lanes = { refs: lanes.lane_refs, matched: lanes.lane_refs_matched, nodes_done: lanes.nodes_done, match_rate: lanes.match_rate, outcomes: lanes.outcomes, arm_check: lanes.arm_check && { ok: lanes.arm_check.arms_ok, off: lanes.arm_check.arms_off, junctions_off: lanes.arm_check.junctions_off, junctions: lanes.arm_check.junctions }, turn_check: lanes.turn_check && { checked: lanes.turn_check.checked, ok: lanes.turn_check.ok, wrong: lanes.turn_check.wrong, none: lanes.turn_check.none, arrows: [lanes.turn_check.arrow_lanes_ok, lanes.turn_check.arrow_lanes_checked, lanes.turn_check.arrow_lanes_wrong], reverted: lanes.turn_check.junctions_reverted_to_cs2, junctions: lanes.turn_check.junctions } };
    log(p.tag, JSON.stringify(row.lanes));
    // what tools/release/acceptance.mjs's lane-connections step runs on the finished city (check only)
    const chk = await ex({ op: 'lanes', plan, checkOnly: true, offNode: true, diagnostics: 40, ...OFF });
    fs.writeFileSync(path.join(out, `${p.tag}-lanecheck.json`), JSON.stringify(chk, null, 1));
    row.acceptance = { match_rate: chk.match_rate, arms_off: chk.arm_check?.arms_off, turn_check: chk.turn_check && { checked: chk.turn_check.checked, ok: chk.turn_check.ok, wrong: chk.turn_check.wrong, arrows: [chk.turn_check.arrow_lanes_ok, chk.turn_check.arrow_lanes_checked], junctions: chk.turn_check.junctions } };
    log(p.tag, 'acceptance step', JSON.stringify(row.acceptance));
    // the Traffic mod's own loader checks, predicted on the live city (op lanedata); with --saveload <tag>, a real save + load
    const ld = await b.exec(LIB, { args: { op: 'lanedata' }, timeout: 300 });
    fs.writeFileSync(path.join(out, `${p.tag}-lanedata.json`), JSON.stringify(ld, null, 1));
    row.loader = { nodes_with_data: ld.nodes_with_data, would_fail: ld.would_fail, reasons: ld.reasons };
    log(p.tag, 'loader check', JSON.stringify(row.loader));
    if (argv.includes('--saveload') && argv[argv.indexOf('--saveload') + 1] === p.tag) {
      await b.saveWrite('cs1conv-lanes126-saveload');
      await b.load('cs1conv-lanes126-saveload');
      await b.pause();
      await b.wait(120, 4);
      const ld2 = await b.exec(LIB, { args: { op: 'lanedata' }, timeout: 300 });
      fs.writeFileSync(path.join(out, `${p.tag}-lanedata-after-load.json`), JSON.stringify(ld2, null, 1));
      row.afterLoad = { nodes_with_data: ld2.nodes_with_data, would_fail: ld2.would_fail };
      log(p.tag, 'after save/load', JSON.stringify(row.afterLoad));
    }
    for (const id of p.nodes) row['after_' + id] = await view(id, 'imported');
    S.runs.push(row);
    save();

    async function view(id, tag) {
      const n = planJson.nodes.find((x) => x.id === id);
      const lv = await ex({ op: 'laneview', plan, node: id, offNode: true, ...OFF }, 300);
      const x = n.x + OFF.offsetX, z = n.z + OFF.offsetZ;
      const shots = [];
      for (const [name, cam] of [['top', { x, z, distance: 90, yaw: 0, pitch: 85 }], ['oblique', { x, z, distance: 110, yaw: 20, pitch: 50 }]]) {
        await aim(cam);
        const f = path.join(out, `${p.tag}-${id}-${tag}-${name}.png`);
        await b.screenshot(f);
        shots.push(path.basename(f));
      }
      await ex({ op: 'view', plan, tool: 'default' }, 120).catch(() => null);
      return { laneview: lv, shots };
    }
  }
  console.log(JSON.stringify(S.runs.map((r) => ({ tag: r.tag, built: `${r.built}/${r.planned}`, lanes: r.lanes })), null, 1));
} finally {
  save();
  if (started && !has('--keep')) {
    try { await stop('cs2', {}); } catch (e) { log('stop: ' + e.message); }
    releaseLock('cs2', OWNER);
  }
}
