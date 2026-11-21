// Loads a saved converted city and reports, for #126: which planned junctions have no Traffic mod lane data after the
// load (op lanes checkOnly → junctions_without_data, with their CS2 road prefabs), the turn check, and the nets at given
// plan nodes (NcAlign inspect). Read-only; never saves.
//   node spikes/lanes126/probe-save.mjs <save name> <package dir> [planNode,…] [--keep]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const argv = process.argv.slice(2);
const [save, pkgArg, nodesArg] = argv.filter((a, i) => !a.startsWith('--') && argv[i - 1] !== '--segs');
const pkg = path.resolve(pkgArg);
const ids = nodesArg ? nodesArg.split(',').map(Number) : [];
const log = (...m) => console.error(`[probe-save ${new Date().toISOString().slice(11, 19)}]`, ...m);
const OWNER = 'lanes126';
const out = path.join(repo, '.gamectl', 'lanes126', 'probe-' + save);
fs.mkdirSync(out, { recursive: true });
for (const p of [path.join(repo, 'cs2-importer'), path.join(repo, 'spikes', 'nc-lane-align')]) execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', p], { stdio: ['ignore', 'ignore', 'inherit'] });
const LIB = path.join(repo, 'cs2-importer', 'bin', 'Release', 'net48', 'Cs2Importer.dll');
const SPIKE = path.join(repo, 'spikes', 'nc-lane-align', 'bin', 'Release', 'net48', 'NcAlign.dll');
let started = false;
try {
  for (let i = 0; ; i++) {
    await waitAndTakeLock('cs2', OWNER, '#126 probe of a saved city (spikes/lanes126/probe-save.mjs)', { log, timeoutMin: 240 });
    if (!(await status('cs2')).running) break;
    releaseLock('cs2', OWNER);
    log("CS2 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  started = true;
  const r = await start('cs2', { load: save });
  const b = r.bridge ?? await connect('cs2');
  await b.pause();
  await b.wait(120, 4, { timeout: 300 });
  const plan = path.join(pkg, 'cs1conv-plan.json');
  const planJson = JSON.parse(fs.readFileSync(plan, 'utf8'));
  const lanes = await b.exec(LIB, { args: { op: 'lanes', plan, checkOnly: true, diagnostics: 80 }, phase: 'tool', pump: 'tool', timeout: 900 });
  fs.writeFileSync(path.join(out, 'lanecheck.json'), JSON.stringify(lanes, null, 1));
  const ld = await b.exec(LIB, { args: { op: 'lanedata' }, timeout: 300 });
  fs.writeFileSync(path.join(out, 'lanedirs.json'), JSON.stringify(await b.exec(LIB, { args: { op: 'lanedirs', plan }, timeout: 600 })));
  fs.writeFileSync(path.join(out, 'lanedata.json'), JSON.stringify(ld, null, 1));
  const want = [...new Set([...ids, ...(lanes.junctions_without_data ?? []).map((j) => j.node)])];
  const nodes = {};
  for (const id of want) {
    const n = planJson.nodes.find((x) => x.id === id);
    if (!n) continue;
    nodes[id] = await b.exec(SPIKE, { args: { op: 'inspect', x: n.x, z: n.z, r: 12 }, timeout: 120 });
  }
  fs.writeFileSync(path.join(out, 'nodes.json'), JSON.stringify(nodes, null, 1));
  // --segs a,b: the nets around those plan segments' midpoints (edges, car lanes)
  const si = argv.indexOf('--segs');
  if (si >= 0) {
    const segs = {};
    for (const id of argv[si + 1].split(',').map(Number)) {
      const sg = planJson.segments.find((x) => x.id === id);
      if (!sg) continue;
      const bz = sg.bezier; const mx = (bz[0][0] + 3 * bz[1][0] + 3 * bz[2][0] + bz[3][0]) / 8, mz = (bz[0][2] + 3 * bz[1][2] + 3 * bz[2][2] + bz[3][2]) / 8;
      segs[id] = await b.exec(SPIKE, { args: { op: 'inspect', x: mx, z: mz, r: 25 }, timeout: 120 });
    }
    fs.writeFileSync(path.join(out, 'segs.json'), JSON.stringify(segs, null, 1));
  }
  console.log(JSON.stringify({ with_data: lanes.nodes_with_lane_data, without: lanes.junctions_without_data, turn: lanes.turn_check && { checked: lanes.turn_check.checked, ok: lanes.turn_check.ok, wrong: lanes.turn_check.wrong }, loader: { nodes: ld.nodes_with_data, would_fail: ld.would_fail } }, null, 1));
} finally {
  if (started && !argv.includes('--keep')) {
    try { await stop('cs2', {}); } catch (e) { log('stop: ' + e.message); }
    releaseLock('cs2', OWNER);
  }
}
