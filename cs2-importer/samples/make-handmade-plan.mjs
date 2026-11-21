#!/usr/bin/env node
// Writes the hand-made M1 test plan (cs1conv-plan.json, schema/README.md §2) to samples/m1-handmade/.
//
//   node samples/make-handmade-plan.mjs [--at x,z] [--out dir]
//
// Layout, relative to the origin (or --at), in CS2 metres (x east, z north):
//   grid    3×3 nodes, 80 m apart, at x,z ∈ {0, 80, 160}: rows alternate Small/Medium Road, columns Small Road.
//           X junction in the middle, T junctions on the sides, corners.
//   rb      Road Builder road (M0 config "Four-Lane Road with Decorative Trees") east from the grid's (160, 80) node.
//   curve   quarter circle (r = 100 m) from the grid's (160, 160) corner to (260, 260), then straight on east.
//   bridge  Medium Road along x = -120: ramp up to +12 m, a span that crosses a ground Small Road (z = 80) with no
//           shared node, ramp down. The ground road joins the grid's (0, 80) node.
//   tunnel  Small Road along x = 480: slope down to −12 m, a tunnel section, slope up.
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : def; };
const [ox, oz] = opt('--at', '0,0').split(',').map(Number);
const outDir = resolve(opt('--out', join(here, 'm1-handmade')));

const RB_TREES = 'r5266315b-039f-59b4-8053-737c2f4c07e5-cs1conv'; // spikes/m0-roadbuilder-configs, deployed in M0
const prefabs = {
  'v:Small Road': { type: 'RoadPrefab', name: 'Small Road', source: 'Basic Road' },
  'v:Medium Road': { type: 'RoadPrefab', name: 'Medium Road', source: 'Medium Road' },
  'rb:5266315b': { type: 'RoadBuilderPrefab', name: RB_TREES, source: 'Four-Lane Road with Decorative Trees' },
};

const nodes = [];
const segments = [];
let nextNode = 1, nextSeg = 100;
const r2 = (v) => Math.round(v * 1000) / 1000;

function node(x, z, h = 0) {
  const n = { id: nextNode++, x: r2(ox + x), z: r2(oz + z), y: r2(h), heightAboveTerrain: h, elevation: Math.round(h) };
  nodes.push(n);
  return n;
}

// Straight unless inner control points (relative coordinates) are given.
function seg(prefab, a, b, kind = 'ground', inner = null) {
  const P = (n) => [n.x, n.y, n.z];
  const lerp = (t) => [r2(a.x + (b.x - a.x) * t), r2(a.y + (b.y - a.y) * t), r2(a.z + (b.z - a.z) * t)];
  const bez = inner
    ? [P(a), [r2(ox + inner[0][0]), r2(a.y + (b.y - a.y) / 3), r2(oz + inner[0][1])], [r2(ox + inner[1][0]), r2(a.y + (b.y - a.y) * 2 / 3), r2(oz + inner[1][1])], P(b)]
    : [P(a), lerp(1 / 3), lerp(2 / 3), P(b)];
  segments.push({ id: nextSeg++, prefab, start: a.id, end: b.id, bezier: bez, elevationStart: a.heightAboveTerrain, elevationEnd: b.heightAboveTerrain, kind });
}

// grid
const g = [];
for (let i = 0; i < 3; i++) { g.push([]); for (let j = 0; j < 3; j++) g[i].push(node(j * 80, i * 80)); } // g[row z][col x]
for (let i = 0; i < 3; i++) for (let j = 0; j < 2; j++) seg(i % 2 === 0 ? 'v:Small Road' : 'v:Medium Road', g[i][j], g[i][j + 1]);
for (let j = 0; j < 3; j++) for (let i = 0; i < 2; i++) seg('v:Small Road', g[i][j], g[i + 1][j]);

// Road Builder road
const rb1 = node(260, 80), rb2 = node(360, 80);
seg('rb:5266315b', g[1][2], rb1);
seg('rb:5266315b', rb1, rb2);

// curve: quarter circle from (160,160) heading north to (260,260) heading east
const k = 0.5523 * 100;
const c1 = node(260, 260), c2 = node(360, 260);
seg('v:Small Road', g[2][2], c1, 'ground', [[160, 160 + k], [260 - k, 260]]);
seg('v:Small Road', c1, c2);

// ground road under the bridge
const w = node(-220, 80);
seg('v:Small Road', w, g[1][0]);
// bridge
const b0 = node(-120, -180, 0), b1 = node(-120, -60, 12), b2 = node(-120, 30, 12), b3 = node(-120, 130, 12), b4 = node(-120, 250, 0);
seg('v:Medium Road', b0, b1, 'elevated');
seg('v:Medium Road', b1, b2, 'elevated');
seg('v:Medium Road', b2, b3, 'bridge');
seg('v:Medium Road', b3, b4, 'elevated');

// tunnel
const t0 = node(480, -200, 0), t1 = node(480, -80, -12), t2 = node(480, 80, -12), t3 = node(480, 200, 0);
seg('v:Small Road', t0, t1, 'slope');
seg('v:Small Road', t1, t2, 'tunnel');
seg('v:Small Road', t2, t3, 'slope');

const xs = nodes.map((n) => n.x), zs = nodes.map((n) => n.z);
const plan = {
  format: 'cs1conv-plan', formatVersion: 1,
  source: { city: 'hand-made', saveName: null, exportedAt: null, cs1xSha256: null },
  crop: { centerX: r2((Math.min(...xs) + Math.max(...xs)) / 2), centerZ: r2((Math.min(...zs) + Math.max(...zs)) / 2), size: Math.ceil(Math.max(Math.max(...xs) - Math.min(...xs), Math.max(...zs) - Math.min(...zs))) },
  transform: { dx: 0, dz: 0, heightMode: 'terrain-relative' },
  prefabs, nodes, segments,
  stats: { segments: segments.length, skipped: 0 },
};
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'cs1conv-plan.json'), JSON.stringify(plan, null, 1) + '\n');
console.log(`wrote ${join(outDir, 'cs1conv-plan.json')}: ${nodes.length} nodes, ${segments.length} segments, at (${ox}, ${oz})`);
