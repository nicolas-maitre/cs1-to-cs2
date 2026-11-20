// Drives the M0Junctions exec library through gamectl (CS2 must be running with a city loaded).
//   node run.mjs find <x> <z> [need]          free, flat spots near (x,z)
//   node run.mjs scan <x> <z> [r]
//   node run.mjs junctions <x,z> <x,z> <x,z> <x,z>   tests x1 / x2 / t2 / temp, one free spot each
//   node run.mjs tempx <x> <z> [apply]        Temp-definition X with a per-frame timeline
//   node run.mjs grid <x0> <z0> [n] [cell]    n×n street grid (Small/Medium Road) in one frame, then check every node
//   node run.mjs rb <x> <z>                   spike 3: place every *-cs1conv RB road, measure widths
// Results go to <repo>/.gamectl/m0/*.json, screenshots to <repo>/.gamectl/m0/*.png (git-ignored).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { connect } from '../../tools/gamectl/lib/index.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../..');
const out = path.join(repo, '.gamectl', 'm0');
fs.mkdirSync(out, { recursive: true });
const dll = path.join(here, 'bin', 'Release', 'net48', 'M0Junctions.dll');
const cs2 = await connect('cs2');

const op = (args, phase = 'main') => cs2.exec(dll, { type: 'M0Junctions.Ops', args, phase, timeout: 120 });
const save = (name, data) => fs.writeFileSync(path.join(out, name + '.json'), JSON.stringify(data, null, 2));
async function shot(name, x, z, distance = 260, pitch = 60, yaw = 0) {
  await cs2.cameraSet({ x, z, distance, yaw, pitch });
  await cs2.wait(45);
  const file = path.join(out, name + '.png');
  await cs2.screenshot(file);
  return file;
}

const [cmd, ...rest] = process.argv.slice(2);
const num = s => Number(s);
const xz = s => s.split(',').map(Number);

if (cmd === 'find') {
  console.log(JSON.stringify(await op({ op: 'find', x: num(rest[0]), z: num(rest[1]), need: num(rest[2] ?? 150) }), null, 2));
} else if (cmd === 'scan') {
  console.log(JSON.stringify(await op({ op: 'scan', x: num(rest[0]), z: num(rest[1]), r: num(rest[2] ?? 150) }), null, 2));
} else if (cmd === 'junctions') {
  const names = ['x1', 'x2', 't2', 'temp'];
  const results = {};
  for (let i = 0; i < names.length && i < rest.length; i++) {
    const [x, z] = xz(rest[i]);
    const name = names[i];
    const steps = [];
    steps.push(await op({ op: 'scan', x, z, r: 100 }));
    if (name === 'x1') {
      steps.push(await op({ op: 'x1', x, z }, 'tool'));
    } else if (name === 'x2' || name === 't2') {
      steps.push(await op({ op: 'arm', x, z }, 'tool'));
      await cs2.wait(5);
      steps.push(await op({ op: 'inspect', x, z }));
      steps.push(await op({ op: 'attach', x, z, arms: name === 'x2' ? 'ens' : 'en' }, 'tool'));
    } else {
      steps.push(await op({ op: 'x1', x, z, temp: true }, 'tool'));
      await cs2.wait(5);
      steps.push(await op({ op: 'inspect', x, z }));
      steps.push(await op({ op: 'apply' }, 'tool'));
    }
    steps.push(await op({ op: 'inspect', x, z, frames: 30 }));
    const shotFile = await shot('junction-' + name, x, z);
    steps.push(await op({ op: 'inspect', x, z })); // again after the screenshot frames
    results[name] = { centre: [x, z], steps, screenshot: shotFile };
    const last = steps.at(-1);
    console.log(name, JSON.stringify({ centre_nodes: last.centre_nodes.map(n => ({ temp: n.temp, edges: n.connected_edges, dist: n.dist })), area: last.area, world: last.world }));
  }
  save('junctions', results);
} else if (cmd === 'tempx') {
  const r = await op({ op: 'tempx', x: num(rest[0]), z: num(rest[1]), apply: rest[2] === 'apply' }, 'tool');
  save('tempx', r);
  console.log(JSON.stringify(r, null, 2));
} else if (cmd === 'grid') {
  const args = { x: num(rest[0]), z: num(rest[1]), n: num(rest[2] ?? 6), cell: num(rest[3] ?? 60) };
  const placed = await op({ op: 'grid', ...args }, 'tool');
  const check = await op({ op: 'gridcheck', frames: 30, ...args });
  const span = args.n * args.cell;
  const widths = await op({ op: 'rbwidth', x: args.x + span / 2, z: args.z + span / 2, r: span * 0.75 });
  const trees = { live: widths.edges.reduce((s, e) => s + e.trees_on_road, 0), overridden: widths.edges.reduce((s, e) => s + e.overridden_trees_on_road, 0) };
  const shotFile = await shot('grid', args.x + span / 2, args.z + span / 2, span * 1.45, 70);
  save('grid', { placed, check, trees, screenshot: shotFile });
  console.log(JSON.stringify({ placed, check, trees }, null, 2));
} else if (cmd === 'rb') {
  const [x, z] = [num(rest[0]), num(rest[1])];
  const placed = await op({ op: 'rb', x, z }, 'tool');
  const widths = await op({ op: 'rbwidth', x, z: z + 100, r: 160, frames: 30 });
  const shots = [];
  for (const [i, r] of placed.roads.entries()) shots.push(await shot('rb-' + i, x, r.z, 90, 50));
  shots.push(await shot('rb-overview', x, z + 90, 330, 65));
  save('rb', { placed, widths, shots });
  console.log(JSON.stringify({ placed, widths }, null, 2));
} else {
  console.error('usage: node run.mjs find|scan|junctions|rb ...');
  process.exit(2);
}
