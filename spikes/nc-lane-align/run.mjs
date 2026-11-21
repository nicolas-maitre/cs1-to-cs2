// Drives the NcAlign spike (NcAlign.cs) through gamectl: builds lane-drop and ramp cases with and without off-node
// edge ends, reads them back, takes screenshots. Findings: cs2-importer/NOTES-lane-alignment.md.
//   node spikes/nc-lane-align/run.mjs [--map "Corral Riches"] [--x 2000 --z -4000] [--keep] [--save]
// Takes %LOCALAPPDATA%\cs1-to-cs2\cs2.lock (owner nc-align), starts CS2 with a new city unless --reuse, and stops CS2 /
// releases the lock at the end unless --keep. Output: <repo>/.gamectl/nc-align/<ts>/ (git-ignored).
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const args = process.argv.slice(2);
const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
const has = (k) => args.includes(k);
const ts = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15);
const out = path.join(repo, '.gamectl', 'nc-align', ts);
fs.mkdirSync(out, { recursive: true });
const log = (...m) => console.error('[nc-align]', ...m);
const OWNER = 'nc-align';

execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', here], { stdio: ['ignore', 'ignore', 'inherit'] });
const dll = path.join(here, 'bin', 'Release', 'net48', 'NcAlign.dll');

const X = Number(opt('--x', 2000)), Z = Number(opt('--z', -4000));
const H3 = 'Highway Oneway - 3 lanes', H2 = 'Highway Oneway - 2 lanes', H1 = 'Highway Oneway - 1 lane';
const L = 150;

// The cases, each around its own centre (cx, cz); roads run west → east (+x); +z is north = left of travel.
// lane = lateral lane spacing of the CS2 highways, measured in the first case and used for the aligned ones.
function cases(lane) {
  const h = lane / 2;
  return [
    { name: 'drop-centred', note: '3 → 2 lanes, both curves through the node (the converter today)', dx: 0,
      courses: (cx, cz) => [{ prefab: H3, a: [cx - L, cz], b: [cx, cz] }, { prefab: H2, a: [cx, cz], b: [cx + L, cz] }] },
    { name: 'drop-aligned', note: '3 → 2 lanes, the 2-lane curve starts half a lane right of the node (right lanes aligned)', dx: 1,
      courses: (cx, cz) => [{ prefab: H3, a: [cx - L, cz], b: [cx, cz] }, { prefab: H2, a: [cx, cz], ca: [cx, cz - h], b: [cx + L, cz - h] }] },
    { name: 'drop-aligned-later', note: 'as drop-aligned, the 2-lane road built in a later frame onto the existing node', dx: 2, later: true,
      courses: (cx, cz) => [{ prefab: H3, a: [cx - L, cz], b: [cx, cz] }, { prefab: H2, a: [cx, cz], ca: [cx, cz - h], b: [cx + L, cz - h] }] },
    { name: 'drop-aligned-temp', note: 'as drop-aligned, built the tool way: Temp definitions + one forced apply', dx: 3, temp: true,
      courses: (cx, cz) => [{ prefab: H3, a: [cx - L, cz], b: [cx, cz] }, { prefab: H2, a: [cx, cz], ca: [cx, cz - h], b: [cx + L, cz - h] }] },
    { name: 'ramp-centred', note: '3 lanes → 2-lane highway + 1-lane exit ramp, all curves through the node', dx: 4,
      courses: (cx, cz) => [
        { prefab: H3, a: [cx - L, cz], b: [cx, cz] },
        { prefab: H2, a: [cx, cz], b: [cx + L, cz] },
        { prefab: H1, a: [cx, cz], b: [cx + L, cz - 45], p1: [cx + 50, cz], p2: [cx + 95, cz - 45] }] },
    { name: 'ramp-aligned', note: 'as ramp-centred, the 2-lane curve starts half a lane left, the ramp one lane right (lanes continue straight)', dx: 5,
      courses: (cx, cz) => [
        { prefab: H3, a: [cx - L, cz], b: [cx, cz] },
        { prefab: H2, a: [cx, cz], ca: [cx, cz + h], b: [cx + L, cz + h] },
        { prefab: H1, a: [cx, cz], ca: [cx, cz - lane], b: [cx + L, cz - 45], p1: [cx + 50, cz - lane], p2: [cx + 95, cz - 45] }] },
  ];
}

let cs2Started = false;
let b;
try {
  if (!has('--reuse')) {
    for (let i = 0; ; i++) {
      await waitAndTakeLock('cs2', OWNER, 'Node Controller lane-alignment spike (spikes/nc-lane-align)', { log, timeoutMin: 120 });
      const st = await status('cs2');
      if (!st.running) break;
      releaseLock('cs2', OWNER);
      if (i > 240) throw new Error('CS2 kept running without a lock for an hour');
      log("CS2 is running without a lock (someone else's game); waiting");
      await new Promise((r) => setTimeout(r, 15000));
    }
    cs2Started = true;
    const r = await start('cs2', {});
    b = r.bridge ?? await connect('cs2');
    log('new city');
    await b.newGame(opt('--map', 'Corral Riches'), { name: 'cs1conv-nc-align' });
    await b.pause();
  } else {
    b = await connect('cs2');
  }
  const op = (a, phase = 'main', pump) => b.exec(dll, { type: 'NcAlign.Ops', args: a, phase, pump, timeout: 120 });
  const save = (name, data) => fs.writeFileSync(path.join(out, name + '.json'), JSON.stringify(data, null, 2));
  let camY;
  async function shot(name, x, z, distance = 110, pitch = 89, yaw = 0) {
    await b.cameraSet({ x, y: camY, z, distance, yaw, pitch });
    await b.wait(40);
    const file = path.join(out, name + '.png');
    await b.screenshot(file);
    return file;
  }

  save('info', await op({ op: 'info', prefabs: [H3, H2, H1, 'Small Road', 'Medium Road'] }));

  // measure the 3-lane highway's lane spacing on a probe
  const px = X, pz = Z + 600;
  await op({ op: 'courses', courses: [{ prefab: H3, a: [px - L, pz], b: [px, pz] }] }, 'tool', 'tool');
  const probe = await op({ op: 'inspect', x: px - L / 2, z: pz, r: 50, frames: 10 });
  save('probe', probe);
  const zs = [...new Set((probe.edges[0]?.lanes ?? []).map((l) => Math.round(((l.a[1] + l.d[1]) / 2) * 10) / 10))].sort((p, q) => p - q); // each lane is listed twice (both directions of the master/slave pair)
  const lane = zs.length >= 2 ? (zs[zs.length - 1] - zs[0]) / (zs.length - 1) : 4;
  log('3-lane lane offsets', zs.map((v) => (v - pz).toFixed(2)).join(' '), '→ spacing', lane.toFixed(2));
  // (no clear: deleting nodes by radius removed live edges' end nodes and crashed CS2; the cases are 400 m apart on a fresh city)

  const results = { lane, cases: [] };
  for (const c of cases(lane)) {
    const cx = X + c.dx * 400, cz = Z;

    await b.wait(5);
    const list = c.courses(cx, cz);
    if (c.later) {
      await op({ op: 'courses', courses: [list[0]] }, 'tool', 'tool');
      await b.wait(10);
      await op({ op: 'courses', courses: list.slice(1) }, 'tool', 'tool');
    } else if (c.temp) {
      await op({ op: 'courses', courses: list, temp: true }, 'tool', 'tool');
      await b.wait(2);
      await op({ op: 'apply' }, 'tool', 'tool');
    } else {
      await op({ op: 'courses', courses: list }, 'tool', 'tool');
    }
    const ins = await op({ op: 'inspect', x: cx, z: cz, r: 200, frames: 30 });
    save(c.name, ins);
    const node = ins.nodes.find((n) => Math.hypot(n.pos[0] - cx, n.pos[2] - cz) < 4) // CS2 moves the node to the centroid of its edges' ends;
    camY = node?.pos[1] ?? ins.edges[0]?.curve[0][1];
    const row = {
      name: c.name, note: c.note, edges: ins.edges.length, nodes: ins.nodes.length,
      offNode: ins.edges.map((e) => [e.prefab, e.startOffNode, e.endOffNode]),
      nodeLanes: node ? node.lanes.map((l) => ({ lateral: l.lateral, turnDeg: l.turnDeg })) : null,
      shots: [await shot(c.name, cx + 10, cz, 120), await shot(c.name + '-oblique', cx - 40, cz, 140, 45, 90)],
    };
    results.cases.push(row);
    log(c.name, JSON.stringify({ offNode: row.offNode, nodeLanes: row.nodeLanes }));
  }
  if (has('--save')) {
    await b.saveWrite('cs1conv-nc-align');
    await b.load('cs1conv-nc-align');
    const after = [];
    for (const c of cases(lane)) after.push({ name: c.name, ins: await op({ op: 'inspect', x: X + c.dx * 400, z: Z, r: 200, frames: 10 }) });
    save('after-load', after);
    results.afterLoad = after.map((a) => ({ name: a.name, offNode: a.ins.edges.map((e) => [e.prefab, e.startOffNode, e.endOffNode]) }));
  }
  save('summary', results);
  console.log(JSON.stringify(results, null, 2));
} finally {
  if (cs2Started && !has('--keep')) {
    try { await stop('cs2', {}); } catch (e) { log('stop: ' + e.message); }
    releaseLock('cs2', OWNER);
  }
}
