// Imports one package with offNode at the flat test spot and dumps the nets around given plan nodes (NcAlign inspect),
// to see what CS2 built where an offNode node failed.
//   node spikes/nc-lane-align/probe.mjs <package dir> <planNodeId,...> [--keep] [--no-offnode]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const [pkgArg, idsArg] = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const pkg = path.resolve(pkgArg);
const ids = idsArg.split(',').map(Number);
const has = (k) => process.argv.includes(k);
const log = (...m) => console.error(`[nc-probe ${new Date().toISOString().slice(11, 19)}]`, ...m);
const OWNER = 'nc-align';
const OFF = { offsetX: 2000, offsetZ: -4000 };
for (const p of [here, path.join(repo, 'cs2-importer')]) execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', p], { stdio: ['ignore', 'ignore', 'inherit'] });
const SPIKE = path.join(here, 'bin', 'Release', 'net48', 'NcAlign.dll');
const LIB = path.join(repo, 'cs2-importer', 'bin', 'Release', 'net48', 'Cs2Importer.dll');
const out = path.join(repo, '.gamectl', 'nc-align', 'probe-' + new Date().toISOString().replace(/[-:]/g, '').slice(0, 15));
fs.mkdirSync(out, { recursive: true });

let started = false;
try {
  for (let i = 0; ; i++) {
    await waitAndTakeLock('cs2', OWNER, 'Node Controller probe (spikes/nc-lane-align/probe.mjs)', { log, timeoutMin: 180 });
    if (!(await status('cs2')).running) break;
    releaseLock('cs2', OWNER);
    log("CS2 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  started = true;
  const r = await start('cs2', {});
  const b = r.bridge ?? await connect('cs2');
  await b.newGame('Corral Riches', { name: 'cs1conv-nc-probe' });
  await b.pause();
  const plan = JSON.parse(fs.readFileSync(path.join(pkg, 'cs1conv-plan.json'), 'utf8'));
  const imp = await b.exec(LIB, { args: { op: 'import', plan: path.join(pkg, 'cs1conv-plan.json'), clear: true, heights: 'flat', offNode: !has('--no-offnode'), ...OFF, trace: path.join(out, 'trace.txt'), report: path.join(out, 'report.json') }, phase: 'tool', pump: 'tool', timeout: 3600 });
  await b.wait(60, 2);
  const res = { result: imp.result, failures: imp.failures_by_reason, nodes: [] };
  for (const id of ids) {
    const n = plan.nodes.find((x) => x.id === id);
    const segs = plan.segments.filter((s) => s.start === id || s.end === id).map((s) => ({ id: s.id, start: s.start, end: s.end, ncShift: s.ncShift, prefab: plan.prefabs[s.prefab].name, bezier: s.bezier }));
    const ins = await b.exec(SPIKE, { args: { op: 'inspect', x: n.x + OFF.offsetX, z: n.z + OFF.offsetZ, r: 60 }, timeout: 120 });
    res.nodes.push({ id, plan: n, segs, cs2: ins });
    await b.cameraSet({ x: n.x + OFF.offsetX, y: ins.nodes[0]?.pos[1], z: n.z + OFF.offsetZ, distance: 160, yaw: 0, pitch: 89 });
    await b.wait(40, 1.5);
    await b.screenshot(path.join(out, `node-${id}.png`));
  }
  fs.writeFileSync(path.join(out, 'probe.json'), JSON.stringify(res, null, 1));
  console.log(out);
} finally {
  if (started && !has('--keep')) {
    try { await stop('cs2', {}); } catch (e) { log('stop: ' + e.message); }
    releaseLock('cs2', OWNER);
  }
}
