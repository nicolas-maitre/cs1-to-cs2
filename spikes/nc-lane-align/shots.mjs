// Before/after screenshots of chosen plan nodes: imports each package in turn at the flat test spot (offNode on) and shoots
// the same camera at each node, checking the camera really got there (camera.get) before each shot.
//   node spikes/nc-lane-align/shots.mjs <out dir> <tag>=<package dir> ... --nodes 1745,3071 [--keep]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const argv = process.argv.slice(2);
const opt = (k) => { const i = argv.indexOf(k); return i >= 0 ? argv[i + 1] : null; };
const has = (k) => argv.includes(k);
const pos = argv.filter((a, i) => !a.startsWith('--') && !(i > 0 && argv[i - 1].startsWith('--') && argv[i - 1] !== '--keep'));
const out = path.resolve(pos[0]);
const pkgs = pos.slice(1).map((p) => { const [tag, dir] = p.split('='); return { tag, dir: path.resolve(dir) }; });
const nodeIds = opt('--nodes').split(',').map(Number);
const log = (...m) => console.error(`[nc-shots ${new Date().toISOString().slice(11, 19)}]`, ...m);
const OWNER = 'nc-align';
const OFF = { offsetX: 2000, offsetZ: -4000 };
fs.mkdirSync(out, { recursive: true });
for (const p of [here, path.join(repo, 'cs2-importer')]) execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', p], { stdio: ['ignore', 'ignore', 'inherit'] });
const SPIKE = path.join(here, 'bin', 'Release', 'net48', 'NcAlign.dll');
const LIB = path.join(repo, 'cs2-importer', 'bin', 'Release', 'net48', 'Cs2Importer.dll');

let started = false;
const rows = [];
try {
  for (let i = 0; ; i++) {
    await waitAndTakeLock('cs2', OWNER, 'Node Controller before/after shots (spikes/nc-lane-align/shots.mjs)', { log, timeoutMin: 180 });
    if (!(await status('cs2')).running) break;
    releaseLock('cs2', OWNER);
    log("CS2 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  started = true;
  const r = await start('cs2', {});
  const b = r.bridge ?? await connect('cs2');
  await b.newGame('Corral Riches', { name: 'cs1conv-nc-shots' });
  await b.pause();
  async function aim(cam) {
    for (let k = 0; k < 6; k++) {
      await b.cameraSet(cam);
      await b.wait(30, 1);
      const got = await b.cameraGet();
      if (Math.hypot(got.x - cam.x, got.z - cam.z) < 2) return got;
    }
    return await b.cameraGet();
  }
  for (const p of pkgs) {
    const planFile = path.join(p.dir, 'cs1conv-plan.json');
    const plan = JSON.parse(fs.readFileSync(planFile, 'utf8'));
    log(p.tag, 'import');
    const imp = await b.exec(LIB, { args: { op: 'import', plan: planFile, clear: true, heights: 'flat', offNode: true, ...OFF }, phase: 'tool', pump: 'tool', timeout: 3600 });
    await b.wait(90, 3);
    for (const id of nodeIds) {
      const n = plan.nodes.find((x) => x.id === id);
      if (!n) continue;
      const x = n.x + OFF.offsetX, z = n.z + OFF.offsetZ;
      const ins = await b.exec(SPIKE, { args: { op: 'inspect', x, z, r: 8 }, timeout: 120 });
      const y = ins.nodes[0]?.pos[1];
      const shots = [];
      for (const [name, cam] of [['top', { x, y, z, distance: 150, yaw: 0, pitch: 89 }], ['oblique', { x, y, z, distance: 170, yaw: 30, pitch: 40 }]]) {
        const got = await aim(cam);
        const file = path.join(out, `${p.tag}-${id}-${name}.png`);
        await b.screenshot(file);
        shots.push({ file: path.basename(file), camera: got });
      }
      rows.push({ tag: p.tag, node: id, built: imp.result?.segments_built, planned: imp.result?.segments_planned, shots });
      log(p.tag, id, JSON.stringify(shots.map((s) => [Math.round(s.camera.x), Math.round(s.camera.z)])));
    }
  }
  fs.writeFileSync(path.join(out, 'shots.json'), JSON.stringify(rows, null, 1));
} finally {
  if (started && !has('--keep')) {
    try { await stop('cs2', {}); } catch (e) { log('stop: ' + e.message); }
    releaseLock('cs2', OWNER);
  }
}
