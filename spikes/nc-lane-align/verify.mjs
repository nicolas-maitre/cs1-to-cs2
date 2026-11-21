// Before/after check of the Node Controller alignment (cs2-importer/DECISIONS-nodecontroller.md) on real cities, in one
// CS2 session: for each package pair <name>-before / <name>-after (same crop; "before" = the plan without the NC shifts),
// import the roads at the flat test spot, then measure roads built, lane directions (lanedirs-compare), the TM:PE lane
// connection match (op lanes, checkOnly), read back the edges at every NC-shifted node, and screenshot those nodes.
//
//   node spikes/nc-lane-align/verify.mjs <dir with <name>-before/-after packages> <name>=<cs1x> … [--keep]
//
// The packages' Road Builder configs must be installed (cs1conv plan --install-rb) before this starts CS2.
// Takes cs2.lock (owner nc-align); output: <dir>/<ts>/ (summary.json, json dumps, screenshots).
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { connect, start, stop, status } from '../../tools/gamectl/lib/index.js';
import { waitAndTakeLock, releaseLock } from '../../cs2-importer/lib/driver.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const args = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const has = (k) => process.argv.includes(k);
const [dirArg, ...pairs] = args;
const dir = path.resolve(dirArg);
const cities = pairs.map((p) => { const [name, cs1x] = p.split('='); return { name, cs1x }; });
const ts = new Date().toISOString().replace(/[-:]/g, '').replace('T', '-').slice(0, 15);
const out = path.join(dir, ts);
fs.mkdirSync(out, { recursive: true });
const log = (...m) => console.error(`[nc-verify ${new Date().toISOString().slice(11, 19)}]`, ...m);
const OWNER = 'nc-align';
const OFF = { offsetX: 2000, offsetZ: -4000 }; // the flat M1 test spot on Corral Riches (tools/e2e flatOffset)

const build = (p) => execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', p], { stdio: ['ignore', 'ignore', 'inherit'] });
build(here);
build(path.join(repo, 'cs2-importer'));
const SPIKE = path.join(here, 'bin', 'Release', 'net48', 'NcAlign.dll');
const LIB = path.join(repo, 'cs2-importer', 'bin', 'Release', 'net48', 'Cs2Importer.dll');
const save = (name, data) => fs.writeFileSync(path.join(out, name + '.json'), JSON.stringify(data, null, 1));

/** The NC-shifted nodes of an "after" plan, junctions first, with their shifted segment ends. */
function caseNodes(plan) {
  const deg = new Map();
  for (const s of plan.segments) for (const id of [s.start, s.end]) deg.set(id, (deg.get(id) ?? 0) + 1);
  const m = new Map();
  for (const s of plan.segments) {
    if (!s.offNode) continue;
    const [a, b] = s.ncShift ?? [0, 0];
    for (const [id, sh] of [[s.start, a], [s.end, b]]) if (sh) m.set(id, [...(m.get(id) ?? []), { segment: s.id, shift: sh, source: plan.prefabs[s.prefab]?.source }]);
  }
  return [...m].map(([id, ends]) => { const n = plan.nodes.find((x) => x.id === id); return { id, deg: deg.get(id), x: n.x + OFF.offsetX, z: n.z + OFF.offsetZ, ends }; })
    .sort((p, q) => q.deg - p.deg || p.id - q.id);
}

let started = false;
const S = { started: new Date().toISOString(), cities: [] };
let last = [];
try {
  // --reuse: CS2 and the lock are already ours (run.mjs --keep)
  if (!has('--reuse')) for (let i = 0; ; i++) {
    await waitAndTakeLock('cs2', OWNER, 'Node Controller alignment before/after (spikes/nc-lane-align/verify.mjs)', { log, timeoutMin: 180 });
    const st = await status('cs2');
    if (!st.running) break;
    releaseLock('cs2', OWNER);
    if (i > 480) throw new Error('CS2 kept running without a lock for two hours');
    log("CS2 is running without a lock (someone else's game); waiting");
    await new Promise((r) => setTimeout(r, 15000));
  }
  started = true;
  const r = has('--reuse') ? {} : await start('cs2', {});
  const b = r.bridge ?? await connect('cs2');
  await b.newGame('Corral Riches', { name: 'cs1conv-nc-verify' });
  await b.pause();
  const exec = (dll, a, phase = 'main', pump, timeout = 600) => b.exec(dll, { args: a, phase, pump, timeout });
  let camY;
  async function shot(file, x, z, distance, pitch, yaw = 0) {
    await b.cameraSet({ x, y: camY, z, distance, yaw, pitch });
    await b.wait(40, 1.5);
    await b.screenshot(file);
    return path.basename(file);
  }

  for (const c of cities) {
    const after = JSON.parse(fs.readFileSync(path.join(dir, `${c.name}-after`, 'cs1conv-plan.json'), 'utf8'));
    const nodes = caseNodes(after);
    last = nodes;
    const row = { name: c.name, caseNodes: nodes.length, phases: {} };
    // after-snap = the after package with offNode off (ends snapped to the nodes), for the node failure comparison
    for (const phase of ['before', 'after-snap', 'after']) {
      const on = phase === 'after';
      const pkg = path.join(dir, `${c.name}-${phase === 'before' ? 'before' : 'after'}`);
      const plan = path.join(pkg, 'cs1conv-plan.json');
      log(c.name, phase, 'import');
      const imp = await exec(LIB, { op: 'import', plan, clear: true, heights: 'flat', offNode: on, ...OFF, report: path.join(out, `${c.name}-${phase}-report.json`) }, 'tool', 'tool', 3600);
      await b.wait(60, 2);
      const ph = { result: imp.result, failures_by_reason: imp.failures_by_reason, temp_after: imp.temp_after };
      const dirs = await exec(LIB, { op: 'lanedirs', plan, offNode: on, ...OFF });
      const dumpFile = path.join(out, `${c.name}-${phase}-lanedirs.json`);
      fs.writeFileSync(dumpFile, JSON.stringify(dirs));
      try {
        const cmpFile = path.join(out, `${c.name}-${phase}-lanedirs-compare.json`);
        try { execFileSync(process.execPath, [path.join(repo, 'tools', 'release', 'lanedirs-compare.mjs'), c.cs1x, plan, dumpFile, '--json', cmpFile], { stdio: ['ignore', 'ignore', 'inherit'] }); } catch { /* exit 1 = differences */ }
        const cmp = JSON.parse(fs.readFileSync(cmpFile, 'utf8'));
        ph.lanedirs = { segments: dirs.segments, missing: dirs.missing, byKind: cmp.byKind, reversed: cmp.reversed, mirrored: cmp.mirrored, other: cmp.other, ok: cmp.ok };
      } catch (e) { ph.lanedirs = { error: String(e.message).slice(0, 300) }; }
      try {
        const lanes = await exec(LIB, { op: 'lanes', plan, checkOnly: true, offNode: on, ...OFF }, 'tool', 'tool');
        ph.laneConnections = { refs: lanes.lane_refs, matched: lanes.lane_refs_matched, skipped: lanes.skipped, arm_check: lanes.arm_check };
      } catch (e) { ph.laneConnections = { error: String(e.message).slice(0, 300) }; }
      ph.nodes = [];
      for (const n of phase === 'after-snap' ? [] : nodes) {
        const ins = await exec(SPIKE, { op: 'inspect', x: n.x, z: n.z, r: 45 });
        const node = ins.nodes.find((k) => Math.hypot(k.pos[0] - n.x, k.pos[2] - n.z) < 6);
        camY = node?.pos[1] ?? ins.edges[0]?.curve[0][1];
        const shots = [await shot(path.join(out, `${c.name}-${n.id}-${phase}.png`), n.x, n.z, n.deg >= 3 ? 160 : 110, 89)];
        if (n.deg >= 3) shots.push(await shot(path.join(out, `${c.name}-${n.id}-${phase}-oblique.png`), n.x, n.z, 170, 40, 30));
        ph.nodes.push({ id: n.id, deg: n.deg, ends: n.ends, found: !!node,
          offNode: ins.edges.filter((e) => e.startOffNode > 0.05 || e.endOffNode > 0.05).map((e) => [e.prefab, e.startOffNode, e.endOffNode]),
          nodeLanes: node?.lanes?.map((l) => [l.lateral, l.turnDeg]) ?? null, shots });
      }
      row.phases[phase] = ph;
      save('summary', { ...S, cities: [...S.cities, row] });
      log(c.name, phase, JSON.stringify({ result: ph.result, lanedirs: ph.lanedirs, laneConnections: ph.laneConnections }));
    }
    S.cities.push(row);
    save('summary', S);
  }
  // persistence: the off-node edge ends survive save + load
  try {
    const before = [];
    for (const n of last) before.push((await exec(SPIKE, { op: "inspect", x: n.x, z: n.z, r: 45 })).edges.map((e) => [e.id, e.startOffNode, e.endOffNode]));
    await b.saveWrite("cs1conv-nc-verify");
    await b.load("cs1conv-nc-verify");
    await b.pause();
    const after = [];
    for (const n of last) after.push((await exec(SPIKE, { op: "inspect", x: n.x, z: n.z, r: 45 })).edges.map((e) => [e.id, e.startOffNode, e.endOffNode]));
    S.persistence = { nodes: last.length, same: JSON.stringify(before.map((r) => r.map((e) => [e[1], e[2]]))) === JSON.stringify(after.map((r) => r.map((e) => [e[1], e[2]]))), before, after };
  } catch (e) { S.persistence = { error: String(e.message).slice(0, 500) }; }
  save("summary", S);
  console.log(JSON.stringify({ persistence: { same: S.persistence.same, error: S.persistence.error } }));
  console.log(JSON.stringify(S.cities.map((c) => ({ name: c.name, caseNodes: c.caseNodes,
    phases: Object.fromEntries(Object.entries(c.phases).map(([k, p]) => [k, { result: p.result, failures: p.failures_by_reason, lanedirs: p.lanedirs, lanes: p.laneConnections }])) })), null, 1));
} finally {
  if (started && !has('--keep')) {
    try { await stop('cs2', {}); } catch (e) { log('stop: ' + e.message); }
    releaseLock('cs2', OWNER);
  }
}
