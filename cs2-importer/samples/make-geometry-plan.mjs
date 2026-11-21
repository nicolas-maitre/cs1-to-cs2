#!/usr/bin/env node
// Writes the M2 geometry test plan (samples/m2-geometry/cs1conv-plan.json): the cases the M0 report left for M2.
//
//   node samples/make-geometry-plan.mjs [--at x,z] [--out dir]
//
// Every case has an id prefix (segments: case*100+n) so the import report's failures name the case:
//   1  roundabout        8 arc segments (r = 40 m, one-way ring is not needed for geometry) + 4 approach roads
//   2  short segments    a straight road cut into 6, 3, 2, 1.2 and 0.7 m pieces, plus a junction pair 2.5 m apart
//   3  overpass 45°      Medium Road bridge (+12 m) crossing a ground Medium Road at 45°, no shared node
//   4  underpass         Small Road tunnel (−12 m) under a ground road, no shared node
//   5  rail over road    elevated Double Train Track (+10 m) over a ground road
//   6  tight curve       r = 15 m quarter circle
//   7  long curve        one 400 m sweeping segment (CS2 splits long courses)
//   8  S-curve           two opposite arcs, 60 m radius
//   9  acute Y           two roads leaving one node 15° apart
//   10 6-way junction    six roads into one node
//   11 near-parallel     two roads 12 m apart side by side (no junction), and two segments between the same nodes
//   12 ramp chain        ground → 4 m → 8 m → 12 m → 8 m → 4 m → ground, 40 m steps (quay → bridge → quay)
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : def; };
const [ox, oz] = opt('--at', '-900,800').split(',').map(Number);
const outDir = resolve(opt('--out', join(here, 'm2-geometry')));

const prefabs = {
  'v:Small Road': { type: 'RoadPrefab', name: 'Small Road', source: 'Basic Road' },
  'v:Medium Road': { type: 'RoadPrefab', name: 'Medium Road', source: 'Medium Road' },
  'v:Double Train Track': { type: 'TrackPrefab', name: 'Double Train Track', source: 'Train Track' },
};
const nodes = [], segments = [];
let nextNode = 1;
const r3 = (v) => Math.round(v * 1000) / 1000;
const K = 0.5523; // cubic bezier quarter-circle constant

function node(x, z, h = 0) {
  const n = { id: nextNode++, x: r3(ox + x), z: r3(oz + z), y: r3(h), heightAboveTerrain: h, elevation: Math.round(h) };
  nodes.push(n);
  return n;
}

const caseSeg = {};
/** inner = two relative [x, z] control points (else straight). */
function seg(c, prefab, a, b, kind = 'ground', inner = null) {
  caseSeg[c] = (caseSeg[c] ?? 0) + 1;
  const P = (n) => [n.x, n.y, n.z];
  const L = (t) => [r3(a.x + (b.x - a.x) * t), r3(a.y + (b.y - a.y) * t), r3(a.z + (b.z - a.z) * t)];
  const bez = inner
    ? [P(a), [r3(ox + inner[0][0]), r3(a.y + (b.y - a.y) / 3), r3(oz + inner[0][1])], [r3(ox + inner[1][0]), r3(a.y + (b.y - a.y) * 2 / 3), r3(oz + inner[1][1])], P(b)]
    : [P(a), L(1 / 3), L(2 / 3), P(b)];
  segments.push({ id: c * 100 + caseSeg[c], prefab, start: a.id, end: b.id, bezier: bez, elevationStart: a.heightAboveTerrain, elevationEnd: b.heightAboveTerrain, kind });
}

/** Arc segment between angles (degrees) on a circle, as one cubic (≤ 90°). */
function arc(c, prefab, cx, cz, r, a0, a1, n0, n1) {
  const t0 = a0 * Math.PI / 180, t1 = a1 * Math.PI / 180;
  const k = (4 / 3) * Math.tan((t1 - t0) / 4) * r;
  const p0 = [cx + r * Math.cos(t0), cz + r * Math.sin(t0)], p3 = [cx + r * Math.cos(t1), cz + r * Math.sin(t1)];
  const p1 = [p0[0] - k * Math.sin(t0), p0[1] + k * Math.cos(t0)], p2 = [p3[0] + k * Math.sin(t1), p3[1] - k * Math.cos(t1)];
  seg(c, prefab, n0, n1, 'ground', [p1, p2]);
}

// 1 roundabout at (0, 0)
{
  const ring = [];
  for (let i = 0; i < 8; i++) ring.push(node(40 * Math.cos(i * Math.PI / 4), 40 * Math.sin(i * Math.PI / 4)));
  for (let i = 0; i < 8; i++) arc(1, 'v:Small Road', 0, 0, 40, i * 45, (i + 1) * 45, ring[i], ring[(i + 1) % 8]);
  for (let i = 0; i < 8; i += 2) {
    const a = i * Math.PI / 4;
    seg(1, 'v:Small Road', ring[i], node(140 * Math.cos(a), 140 * Math.sin(a)));
  }
}
// 2 short segments along z = -250, x 200..
{
  let x = 200;
  let prev = node(x, -250);
  for (const len of [60, 6, 3, 2, 1.2, 0.7, 60]) { x += len; const n = node(x, -250); seg(2, 'v:Small Road', prev, n); prev = n; }
  // a junction pair 2.5 m apart, each with its own crossing roads
  const p1 = node(200, -320), p2 = node(202.5, -320);
  seg(2, 'v:Small Road', p1, p2);
  seg(2, 'v:Small Road', node(200, -400), p1);
  seg(2, 'v:Small Road', p2, node(202.5, -285)); // north, so the pair is a real 2.5 m dogleg
  seg(2, 'v:Small Road', node(140, -320), p1);
  seg(2, 'v:Small Road', p2, node(262.5, -320));
}
// 3 overpass at 45° around (450, 0)
{
  seg(3, 'v:Medium Road', node(330, 0), node(570, 0));
  const a = node(450 - 170, -170), b = node(450 - 85, -85, 12), c = node(450 + 85, 85, 12), d = node(450 + 170, 170);
  seg(3, 'v:Medium Road', a, b, 'elevated'); seg(3, 'v:Medium Road', b, c, 'bridge'); seg(3, 'v:Medium Road', c, d, 'elevated');
}
// 4 underpass around (0, 350)
{
  seg(4, 'v:Medium Road', node(-120, 350), node(120, 350));
  const a = node(0, 180), b = node(0, 280, -12), c = node(0, 420, -12), d = node(0, 520);
  seg(4, 'v:Small Road', a, b, 'slope'); seg(4, 'v:Small Road', b, c, 'tunnel'); seg(4, 'v:Small Road', c, d, 'slope');
}
// 5 rail over road around (450, 350)
{
  seg(5, 'v:Small Road', node(450, 230), node(450, 470));
  const a = node(300, 350), b = node(390, 350, 10), c = node(510, 350, 10), d = node(600, 350);
  seg(5, 'v:Double Train Track', a, b, 'elevated'); seg(5, 'v:Double Train Track', b, c, 'bridge'); seg(5, 'v:Double Train Track', c, d, 'elevated');
}
// 6 tight curve r = 15 at (-300, 0)
{
  const a = node(-300, 0), b = node(-285, 15);
  seg(6, 'v:Small Road', node(-360, 0), a);
  arc(6, 'v:Small Road', -300, 15, 15, -90, 0, a, b);
  seg(6, 'v:Small Road', b, node(-285, 75));
}
// 7 long curve: one 400 m segment (quarter circle r = 255) at (-600, -400)
{
  const a = node(-600, -400), b = node(-345, -145);
  arc(7, 'v:Small Road', -345, -400, 255, 180, 90, a, b);
}
// 8 S-curve at (-300, -300)
{
  const a = node(-300, -300), m = node(-240, -240), b = node(-180, -180);
  arc(8, 'v:Small Road', -240, -300, 60, 180, 90, a, m);
  arc(8, 'v:Small Road', -240, -180, 60, 270, 360, m, b);
}
// 9 acute Y at (-300, 300)
{
  const c = node(-300, 300);
  seg(9, 'v:Small Road', node(-400, 300), c);
  const a15 = 7.5 * Math.PI / 180;
  seg(9, 'v:Small Road', c, node(-300 + 120 * Math.cos(a15), 300 + 120 * Math.sin(a15)));
  seg(9, 'v:Small Road', c, node(-300 + 120 * Math.cos(-a15), 300 + 120 * Math.sin(-a15)));
}
// 10 six-way junction at (-600, 300)
{
  const c = node(-600, 300);
  for (let i = 0; i < 6; i++) seg(10, 'v:Small Road', c, node(-600 + 90 * Math.cos(i * Math.PI / 3), 300 + 90 * Math.sin(i * Math.PI / 3)));
}
// 11 near-parallel at (-600, 0): two roads 12 m apart; two segments between the same nodes
{
  seg(11, 'v:Small Road', node(-700, 0), node(-500, 0));
  seg(11, 'v:Small Road', node(-700, 12), node(-500, 12));
  const a = node(-700, 80), b = node(-500, 80);
  seg(11, 'v:Small Road', a, b, 'ground', [[-633, 110], [-567, 110]]);
  seg(11, 'v:Small Road', a, b, 'ground', [[-633, 50], [-567, 50]]);
}
// 12 ramp chain at z = -500
{
  const hs = [0, 4, 8, 12, 8, 4, 0];
  let prev = null;
  hs.forEach((h, i) => {
    const n = node(150 + i * 40, -500, h);
    if (prev) seg(12, 'v:Medium Road', prev, n, Math.max(h, prev.heightAboveTerrain) >= 8 ? 'bridge' : 'elevated');
    prev = n;
  });
}

const xs = nodes.map((n) => n.x), zs = nodes.map((n) => n.z);
const plan = {
  format: 'cs1conv-plan', formatVersion: 1,
  source: { city: 'geometry test', saveName: null },
  crop: { centerX: r3((Math.min(...xs) + Math.max(...xs)) / 2), centerZ: r3((Math.min(...zs) + Math.max(...zs)) / 2), size: Math.ceil(Math.max(Math.max(...xs) - Math.min(...xs), Math.max(...zs) - Math.min(...zs))) },
  transform: { dx: 0, dz: 0, heightMode: 'terrain-relative' },
  prefabs, nodes, segments,
  stats: { segments: segments.length, skipped: 0, cases: caseSeg },
};
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'cs1conv-plan.json'), JSON.stringify(plan, null, 1) + '\n');
console.log(`wrote ${join(outDir, 'cs1conv-plan.json')}: ${nodes.length} nodes, ${segments.length} segments, cases ${JSON.stringify(caseSeg)}`);
