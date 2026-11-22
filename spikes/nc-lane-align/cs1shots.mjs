// CS1 screenshots of Node Controller junctions, for comparison with their CS2 import. Read-only: loads the save, never
// saves. Takes cs1.lock (owner nc-align), quits CS1 and releases the lock at the end.
//   node spikes/nc-lane-align/cs1shots.mjs <save> <out dir> <cs1x> <node,node,...>
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { connect, start, stop, status } from '../../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';

const [save, outArg, cs1x, nodesArg] = process.argv.slice(2);
const out = path.resolve(outArg);
fs.mkdirSync(out, { recursive: true });
const ids = nodesArg.split(',').map(Number);
const log = (...m) => console.error(`[cs1shots ${new Date().toISOString().slice(11, 19)}]`, ...m);
const OWNER = 'nc-align';

// node positions from the export's network.json
const tmp = fs.mkdtempSync(path.join(out, 'cs1x-'));
const tar = path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'tar.exe');
execFileSync(tar, ['-xf', cs1x, '-C', tmp, 'network.json']);
const net = JSON.parse(fs.readFileSync(path.join(tmp, 'network.json'), 'utf8'));
fs.rmSync(tmp, { recursive: true, force: true });
const pos = new Map(net.nodes.map((n) => [n.id, n.position]));

let started = false;
try {
  for (let i = 0; ; i++) {
    await waitAndTakeLock('cs1', OWNER, 'NC offset comparison shots (spikes/nc-lane-align/cs1shots.mjs)', { log, timeoutMin: 120 });
    if (!(await status('cs1')).running) break;
    releaseLock('cs1', OWNER);
    log("CS1 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  started = true;
  const r = await start('cs1', { load: save, loadTimeout: 900 });
  const b = r.bridge ?? await connect('cs1');
  await b.call('sim.set', { paused: true }).catch(() => null);
  const rows = [];
  for (const id of ids) {
    const p = pos.get(id);
    if (!p) { rows.push({ id, error: 'no such node' }); continue; }
    const shots = [];
    for (const [name, cam] of [['top', { x: p[0], y: p[1], z: p[2], distance: 170, yaw: 0, pitch: 89 }], ['oblique', { x: p[0], y: p[1], z: p[2], distance: 190, yaw: 30, pitch: 40 }]]) {
      await b.cameraSet(cam);
      await b.wait(60, 2);
      const f = path.join(out, `cs1-${id}-${name}.png`);
      await b.screenshot(f);
      shots.push(path.basename(f));
    }
    rows.push({ id, pos: p, shots });
    log(id, 'done');
  }
  fs.writeFileSync(path.join(out, 'cs1shots.json'), JSON.stringify(rows, null, 1));
} finally {
  if (started) {
    try { await stop('cs1', {}); } catch (e) { log('stop: ' + e.message); }
    releaseLock('cs1', OWNER);
  }
}
