// Experiment (not the product): Node Controller offsets as pulled-back edge ends. Copies a package and, for every plan
// segment end whose CS1 NC offset is more than `--min-extra` m above NC's default (max(style default, minOffset)), cuts
// the plan bezier back along its own curve by the NC offset (capped at `--cap` of the segment's length) and marks the
// segment offNode, so the CS2 edge ends there while the node stays at the CS1 centre (cs2-importer offNode mechanism).
//   node spikes/nc-lane-align/offsets.mjs <cs1x> <package in> <package out> [--min-extra 5] [--cap 0.45] [--only n,n]
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const argv = process.argv.slice(2);
const opt = (k, d) => { const i = argv.indexOf(k); return i >= 0 ? argv[i + 1] : d; };
const [cs1x, inDir, outDir] = argv.filter((a, i) => !a.startsWith('--') && !(i > 0 && argv[i - 1].startsWith('--')));
const minExtra = Number(opt('--min-extra', 5)), cap = Number(opt('--cap', 0.45));
const only = opt('--only') ? new Set(opt('--only').split(',').map(Number)) : null;
const DEFAULT = { Custom: 8, Bend: 8, Stretch: 8, Crossing: 2, Middle: 0, End: 0, UTurn: 8 };

const tmp = fs.mkdtempSync(path.join(path.dirname(path.resolve(outDir)), 'nc-'));
execFileSync(path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'tar.exe'), ['-xf', cs1x, '-C', tmp, 'nodecontroller.json']);
const nc = JSON.parse(fs.readFileSync(path.join(tmp, 'nodecontroller.json'), 'utf8'));
fs.rmSync(tmp, { recursive: true, force: true });
const offsetOf = new Map(); // "node:segment" -> offset (m), only changed ones
for (const n of nc.nodes) {
  if (only && !only.has(n.id)) continue;
  for (const e of n.segmentEnds ?? []) {
    if (e.offset == null || e.mode === 'FreeForm') continue;
    const def = Math.max(DEFAULT[n.type] ?? 8, e.minOffset ?? 0);
    if (e.offset - def > minExtra) offsetOf.set(`${n.id}:${e.segment}`, e.offset);
  }
}

fs.cpSync(inDir, outDir, { recursive: true });
const pf = path.join(outDir, 'cs1conv-plan.json');
const plan = JSON.parse(fs.readFileSync(pf, 'utf8'));
// --junctions-only: only at nodes with 3+ plan segments (at a 2-road node a CS1 offset only moves where the meshes meet;
// in CS2 a pulled-back end there opens a gap the node bends across)
const deg = new Map(); for (const s of plan.segments) for (const id of [s.start, s.end]) deg.set(id, (deg.get(id) ?? 0) + 1);
const junctionsOnly = argv.includes('--junctions-only');
const r3 = (v) => Math.round(v * 1000) / 1000;
const at = (b, t) => { const u = 1 - t; return [0, 1, 2].map((k) => u * u * u * b[0][k] + 3 * u * u * t * b[1][k] + 3 * u * t * t * b[2][k] + t * t * t * b[3][k]); };
const lerp = (a, b, t) => a.map((v, k) => v + (b[k] - v) * t);
// de Casteljau: the part of b between t0 and t1
function sub(b, t0, t1) {
  const split = (c, t) => { const p01 = lerp(c[0], c[1], t), p12 = lerp(c[1], c[2], t), p23 = lerp(c[2], c[3], t), a = lerp(p01, p12, t), bb = lerp(p12, p23, t), m = lerp(a, bb, t); return [[c[0], p01, a, m], [m, bb, p23, c[3]]]; };
  let right = split(b, t0)[1];
  const t = (t1 - t0) / (1 - t0 || 1);
  return t >= 1 ? right : split(right, t)[0];
}
function arcT(b, dist, fromEnd) { // parameter at arc length `dist` from the start (or the end)
  const N = 200; let acc = 0, prev = at(b, fromEnd ? 1 : 0);
  for (let i = 1; i <= N; i++) { const t = fromEnd ? 1 - i / N : i / N; const p = at(b, t); acc += Math.hypot(p[0] - prev[0], p[2] - prev[2]); prev = p; if (acc >= dist) return t; }
  return fromEnd ? 0 : 1;
}
const len = (b) => { let acc = 0, prev = at(b, 0); for (let i = 1; i <= 100; i++) { const p = at(b, i / 100); acc += Math.hypot(p[0] - prev[0], p[2] - prev[2]); prev = p; } return acc; };
let ends = 0, segs = 0, capped = 0;
const rows = [];
for (const s of plan.segments) {
  const ok = (id) => !junctionsOnly || (deg.get(id) ?? 0) >= 3;
  const o0 = ok(s.start) ? offsetOf.get(`${s.start}:${s.id}`) : undefined, o1 = ok(s.end) ? offsetOf.get(`${s.end}:${s.id}`) : undefined;
  if (!o0 && !o1) continue;
  const L = len(s.bezier);
  let c0 = o0 ?? 0, c1 = o1 ?? 0;
  if (c0 > cap * L) { c0 = cap * L; capped++; }
  if (c1 > cap * L) { c1 = cap * L; capped++; }
  const t0 = c0 ? arcT(s.bezier, c0, false) : 0, t1 = c1 ? arcT(s.bezier, c1, true) : 1;
  if (t1 - t0 < 0.05) continue;
  const nb = sub(s.bezier, t0, t1).map((p) => p.map(r3));
  // keep the CS1 end heights at the cut points (the importer takes y from the nodes anyway)
  s.bezier = nb; s.offNode = true; s.ncOffset = [r3(c0), r3(c1)];
  segs++; ends += (c0 ? 1 : 0) + (c1 ? 1 : 0);
  if (rows.length < 40) rows.push({ segment: s.id, length: r3(L), cut: [r3(c0), r3(c1)] });
}
fs.writeFileSync(pf, JSON.stringify(plan));
console.log(JSON.stringify({ changedOffsets: offsetOf.size, segments: segs, ends, capped, sample: rows.slice(0, 10) }, null, 1));
