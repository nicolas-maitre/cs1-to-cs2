// Small-angle splits and merges in a plan: 3+-arm nodes where two arms leave within `maxDeg` of each other, with the
// CS1 Node Controller offset/shift of their ends. Prints JSON.  node spikes/nc-lane-align/splits.mjs <plan dir> <cs1x> [maxDeg=25]
import fs from 'node:fs'; import path from 'node:path'; import { execFileSync } from 'node:child_process';
const [dir, cs1x, maxDegArg] = process.argv.slice(2); const maxDeg = Number(maxDegArg ?? 25);
const plan = JSON.parse(fs.readFileSync(path.join(dir, 'cs1conv-plan.json'), 'utf8'));
const tmp = fs.mkdtempSync(path.join(path.resolve(dir), '..', 'nc-'));
execFileSync(path.join(process.env.SystemRoot ?? 'C:\Windows', 'System32', 'tar.exe'), ['-xf', cs1x, '-C', tmp, 'nodecontroller.json', 'netinfos.json']);
const nc = JSON.parse(fs.readFileSync(path.join(tmp, 'nodecontroller.json'), 'utf8')); fs.rmSync(tmp, { recursive: true, force: true });
const ncn = new Map(nc.nodes.map((n) => [n.id, n]));
const DEFAULT = { Custom: 8, Bend: 8, Stretch: 8, Crossing: 2, Middle: 0, End: 0, UTurn: 8 };
const arms = new Map();
for (const s of plan.segments) for (const [id, atStart] of [[s.start, true], [s.end, false]]) {
  const b = s.bezier; const p0 = atStart ? b[0] : b[3], p1 = atStart ? b[1] : b[2];
  if (!arms.has(id)) arms.set(id, []);
  arms.get(id).push({ seg: s.id, deg: Math.atan2(p1[0] - p0[0], p1[2] - p0[2]) * 180 / Math.PI, kind: s.kind, prefab: plan.prefabs[s.prefab]?.source });
}
const out = [];
for (const [id, a] of arms) {
  if (a.length < 3) continue;
  let best = 999, pair = null;
  for (let i = 0; i < a.length; i++) for (let j = i + 1; j < a.length; j++) { let d = Math.abs(a[i].deg - a[j].deg) % 360; if (d > 180) d = 360 - d; if (d < best) { best = d; pair = [a[i].seg, a[j].seg]; } }
  if (best > maxDeg) continue;
  const n = ncn.get(id); const node = plan.nodes.find((x) => x.id === id);
  const ends = a.map((x) => { const e = n?.segmentEnds?.find((y) => y.segment === x.seg); const def = e ? Math.max(DEFAULT[n.type] ?? 8, e.minOffset ?? 0) : null; return { seg: x.seg, prefab: x.prefab, kind: x.kind, offset: e?.offset ?? null, extra: e ? Math.round((e.offset - def) * 10) / 10 : null, shift: e?.shift ?? 0 }; });
  out.push({ node: id, x: node.x, z: node.z, angle: Math.round(best * 10) / 10, pair, nc: !!n, ends });
}
out.sort((p, q) => p.angle - q.angle);
console.log(JSON.stringify(out));
