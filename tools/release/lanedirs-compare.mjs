#!/usr/bin/env node
// Compares the importer's op "lanedirs" output (CS2 car lanes per plan segment, left → right, F/B in the plan frame)
// with the CS1 lanes from a .cs1x export: are one-way, two-way and asymmetric roads running the way they did in CS1?
//
//   node tools/release/lanedirs-compare.mjs <export.cs1x | unpacked folder> <cs1conv-plan.json> <lanedirs.json> [--json out.json]
//
// CS1 side: each segment's car lanes (m_laneType has Vehicle, m_vehicleType has Car) from netinfos.json, in the
// segment's start → end frame. A segment with the Invert flag has its lanes mirrored and reversed (that is how CS1
// builds it; a left-hand CS1 city sets Invert where a right-hand one doesn't). Plan segment ids are CS1 segment ids;
// the web tool may run a plan segment end → start (`reversed`), so the CS1 lanes are turned into the plan's frame by
// comparing the plan segment's start node with CS1's.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const [src, planFile, dirsFile] = process.argv.slice(2).filter((a, i, all) => !a.startsWith('--') && all[i - 1] !== '--json');
const jsonOut = process.argv.includes('--json') ? process.argv[process.argv.indexOf('--json') + 1] : null;
if (!src || !planFile || !dirsFile) { console.error('usage: lanedirs-compare.mjs <export.cs1x|folder> <cs1conv-plan.json> <lanedirs.json> [--json out.json]'); process.exit(2); }

let folder = src;
if (fs.statSync(src).isFile()) {
  folder = fs.mkdtempSync(path.join(os.tmpdir(), 'cs1x-'));
  const tar = process.platform === 'win32' ? path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'tar.exe') : 'tar'; // bsdtar reads zips
  execFileSync(tar, ['-xf', src, '-C', folder, 'netinfos.json', 'network.json']);
}
const read = (f) => JSON.parse(fs.readFileSync(path.join(folder, f), 'utf8'));
const infos = new Map(read('netinfos.json').netInfos.map((n) => [n.name, n]));
const segs = new Map(read('network.json').segments.map((s) => [String(s.id), s]));
const dirs = JSON.parse(fs.readFileSync(dirsFile, 'utf8'));
const planSegs = new Map(JSON.parse(fs.readFileSync(planFile, 'utf8')).segments.map((s) => [String(s.id), s]));

const isCar = (l) => /Vehicle/.test(l.fields.m_laneType) && /Car/.test(l.fields.m_vehicleType);
function cs1Lanes(seg) {
  const info = infos.get(seg.info);
  if (!info) return null;
  const inv = !!seg.inverted;
  const out = [];
  for (const l of info.lanes ?? []) {
    if (!isCar(l)) continue;
    const d = l.fields.m_finalDirection ?? l.fields.m_direction;
    let c = /^Forward$/.test(d) ? 'F' : /^Backward$/.test(d) ? 'B' : null;
    if (!c) continue; // Both / None / AvoidBoth: not a one-direction car lane
    let p = l.fields.m_position;
    if (inv) { p = -p; c = c === 'F' ? 'B' : 'F'; }
    out.push([p, c]);
  }
  out.sort((a, b) => a[0] - b[0]);
  return out.map((x) => x[1]).join('');
}
const kind = (s) => !s ? 'none' : /^F+$|^B+$/.test(s) ? 'oneWay' : /^(B+F+|F+B+)$/.test(s) && (s.match(/B/g).length === s.match(/F/g).length) ? 'twoWay' : 'asymmetric';
const rev = (s) => [...s].reverse().map((c) => (c === 'F' ? 'B' : 'F')).join('');
// CS2 may list a lane twice (same direction); compare the run-length shape when the counts differ
const shape = (s) => s.replace(/(.)\1+/g, '$1');

const res = { planReversed: 0, segments: 0, compared: 0, byKind: {}, same: 0, sameShape: 0, reversed: 0, mirrored: 0, other: 0, noCs1: 0, noCarLanes: 0, examples: [] };
for (const [id, row] of Object.entries(dirs.lanes ?? {})) {
  res.segments++;
  const cs2 = String(row).split(' ')[0];
  const seg = segs.get(id);
  if (!seg) { res.noCs1++; continue; }
  let cs1 = cs1Lanes(seg);
  if (cs1 == null) { res.noCs1++; continue; }
  const ps = planSegs.get(id);
  if (ps && String(ps.start) !== String(seg.startNode)) { cs1 = rev(cs1); res.planReversed++; } // the plan runs this segment end → start
  if (!cs1 && !cs2) { res.noCarLanes++; continue; }
  res.compared++;
  const k = kind(cs1);
  const b = (res.byKind[k] ??= { compared: 0, same: 0, reversed: 0, mirrored: 0, other: 0 });
  b.compared++;
  let verdict;
  if (cs1 === cs2) verdict = 'same';
  else if (cs1.length !== cs2.length && shape(cs1) === shape(cs2)) verdict = 'sameShape';
  else if (rev(cs1) === cs2 || shape(rev(cs1)) === shape(cs2)) verdict = 'reversed';
  else if ([...cs1].reverse().join('') === cs2) verdict = 'mirrored';
  else verdict = 'other';
  res[verdict]++;
  b[verdict === 'sameShape' ? 'same' : verdict]++;
  if (verdict !== 'same' && verdict !== 'sameShape' && res.examples.length < 15) res.examples.push({ id, cs1, cs2: row, cs1Info: seg.info, inverted: seg.inverted });
}
res.cs2LeftHandTraffic = dirs.cs2_left_hand_traffic;
res.ok = res.reversed === 0 && res.mirrored === 0 && res.other === 0;
console.log(JSON.stringify(res, null, 1));
if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(res, null, 1));
process.exit(res.ok ? 0 : 1);
