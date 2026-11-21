#!/usr/bin/env node
// Loads a converted heightmap (terrain/convert.mjs output) into the running CS2 city through the bridge, then
// verifies it: game terrain samples vs the file (exact decode) and vs CS1 node terrain heights (crop-transformed).
//
//   node terrain/apply.mjs --terrain out/terrain [--cs1x city.cs1x] [--sea-level n|auto|keep] [--no-world | --remove-worldmap]
//                          [--shots dir] [--points 200] [--report out/terrain/apply-report.json]
//
// CS2 must be running with a city loaded (gamectl cs2 start --load ...). Build the exec library first:
//   dotnet build -c Release terrain/cs2-apply
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { resolve, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { connect } from '../tools/gamectl/lib/index.js';
import { sampleCs2 } from './lib/heightmap.mjs';
import { readZip } from './lib/zip.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
export const DLL = join(HERE, 'cs2-apply', 'bin', 'Release', 'net48', 'Cs1ConvTerrain.dll');

function parseArgs(argv) {
  const a = { seaLevel: 'auto', points: 200, removeWorldmap: false };
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i], v = () => argv[++i];
    switch (k) {
      case '--terrain': a.terrain = v(); break;
      case '--cs1x': a.cs1x = v(); break;
      case '--sea-level': a.seaLevel = v(); break;
      case '--remove-worldmap': a.removeWorldmap = true; break;
      case '--no-world': a.noWorld = true; break;
      case '--shots': a.shots = v(); break;
      case '--points': a.points = Number(v()); break;
      case '--report': a.report = v(); break;
      case '--no-apply': a.noApply = true; break;
      default: throw new Error('unknown argument ' + k);
    }
  }
  if (!a.terrain) throw new Error('--terrain <dir> is required');
  return a;
}

/** CS1 ground-node checkpoints from network.json, moved into CS2 space. expected = CS1 terrain height under the node. */
export function nodeCheckpoints(cs1xBuf, meta, max) {
  const zip = readZip(cs1xBuf);
  const ne = zip.get('network.json');
  if (!ne) return [];
  const { nodes = [] } = JSON.parse(ne.read().toString('utf8'));
  const half = meta.size / 2 - 50; // stay off the very edge
  const pts = [];
  for (const n of nodes) {
    if (!n.position || (n.elevation ?? 0) !== 0) continue;
    if (n.flags && /Underground|Elevated|Bridge/i.test(n.flags)) continue;
    const x = n.position[0] + meta.transform.dx, z = n.position[2] + meta.transform.dz;
    if (Math.abs(x) > half || Math.abs(z) > half) continue;
    pts.push({ id: n.id, x, z, cs1: [n.position[0], n.position[2]], expected: n.position[1] - (n.heightAboveTerrain ?? 0) });
  }
  if (pts.length <= max) return pts;
  const step = pts.length / max; // even subsample, deterministic
  return Array.from({ length: max }, (_, i) => pts[Math.floor(i * step)]);
}

/** A grid of checkpoints over the playable area (expected comes from the file). */
export function gridCheckpoints(n = 9, size = 14336) {
  const pts = [];
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    pts.push({ x: -size / 2 + (i + 0.5) * size / n + 13.3, z: -size / 2 + (j + 0.5) * size / n - 7.9 });
  }
  return pts;
}

function stats(errs) {
  if (!errs.length) return null;
  const abs = errs.map(Math.abs).sort((p, q) => p - q);
  const mean = errs.reduce((s, e) => s + e, 0) / errs.length;
  return { n: errs.length, mean: r4(mean), median: r4(abs[abs.length >> 1]), p95: r4(abs[Math.min(abs.length - 1, Math.floor(abs.length * 0.95))]), max: r4(abs[abs.length - 1]) };
}
const r4 = v => Math.round(v * 1e4) / 1e4;

export async function main(argv) {
  const a = parseArgs(argv);
  const dir = resolve(a.terrain);
  // convert.mjs writes terrain-cs2.json; the web tool's import package has the same file as terrain/terrain.json
  const metaFile = ['terrain-cs2.json', 'terrain.json'].map(f => join(dir, f)).find(f => existsSync(f));
  if (!metaFile) throw new Error('no terrain-cs2.json or terrain.json in ' + dir);
  const meta = JSON.parse(readFileSync(metaFile, 'utf8'));
  const r16 = readFileSync(join(dir, meta.file));
  const raw = new Uint16Array(r16.buffer, r16.byteOffset, meta.resolution * meta.resolution);
  if (!existsSync(DLL)) throw new Error('exec library not built: dotnet build -c Release terrain/cs2-apply');

  const nodes = a.cs1x ? nodeCheckpoints(readFileSync(a.cs1x), meta, a.points) : [];
  const grid = gridCheckpoints();
  const all = [...grid, ...nodes];
  const points = all.map(p => [p.x, p.z]);

  let seaLevel = null;
  if (a.seaLevel === 'auto') seaLevel = Math.floor(meta.stats.min) - 20; // below everything: a dry map until water is converted
  else if (a.seaLevel !== 'keep') seaLevel = Number(a.seaLevel);

  const bridge = await connect('cs2');
  const st = await bridge.state();
  if (!st.city_loaded) throw new Error('no city loaded in CS2');

  let applied;
  if (!a.noApply) {
    applied = await bridge.exec(DLL, {
      type: 'Cs1ConvTerrain.Ops',
      args: { op: 'apply', file: join(dir, meta.file), worldFile: meta.world && !a.noWorld ? join(dir, meta.world.file) : null, heightScale: meta.heightScale, heightOffset: meta.heightOffset, seaLevel, removeWorldmap: a.removeWorldmap, frames: 30, points },
      timeout: 300,
    });
  }
  // sample again after more frames: CPU heights are refreshed from the GPU cascades asynchronously
  await bridge.wait(60);
  const later = await bridge.exec(DLL, { type: 'Cs1ConvTerrain.Ops', args: { op: 'sample', points }, timeout: 120 });

  const sampled = later.points.map(p => p[2]);
  const fileErr = [], nodeErr = [], cs1FileErr = [];
  const rows = all.map((p, i) => {
    const file = sampleCs2(meta, raw, p.x, p.z);
    const game = sampled[i];
    fileErr.push(game - file);
    const row = { x: r4(p.x), z: r4(p.z), game: r4(game), file: r4(file) };
    if (p.expected != null) {
      nodeErr.push(game - p.expected); cs1FileErr.push(file - p.expected);
      Object.assign(row, { node: p.id, cs1: p.cs1, cs1Terrain: r4(p.expected) });
    }
    return row;
  });
  const report = {
    terrain: dir, city: st.city_name, save: st.save_name,
    applied: applied ? { ms: applied.ms, seaLevel: applied.seaLevel, worldFile: applied.worldFile, removeWorldmap: applied.removeWorldmap, refreshed: applied.refreshed, before: applied.before, after: applied.after } : null,
    verification: {
      gameVsFile: stats(fileErr),
      gameVsCs1Nodes: stats(nodeErr),
      fileVsCs1Nodes: stats(cs1FileErr),
      immediateVsLater: applied?.samples ? stats(applied.samples.points.map((p, i) => p[2] - sampled[i])) : null,
    },
    rows,
  };

  if (a.shots) {
    const shots = resolve(a.shots);
    mkdirSync(shots, { recursive: true });
    report.shots = [];
    const views = [
      { name: 'overview', x: 0, z: 0, distance: 9000, yaw: 0, pitch: 60 },
      { name: 'overview-oblique', x: 0, z: 0, distance: 6000, yaw: 30, pitch: 30 },
      ...nodes.slice(0, 3).map((p, i) => ({ name: `node-${p.id}`, x: p.x, z: p.z, distance: 900, yaw: 45 + i * 90, pitch: 35 })),
    ];
    for (const v of views) {
      await bridge.cameraSet({ x: v.x, z: v.z, distance: v.distance, yaw: v.yaw, pitch: v.pitch });
      await bridge.wait(90);
      const file = join(shots, `terrain-${v.name}.png`);
      await bridge.screenshot(file);
      report.shots.push(file);
    }
  }

  const out = a.report ? resolve(a.report) : join(dir, 'apply-report.json');
  writeFileSync(out, JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ report: out, ms: report.applied?.ms, verification: report.verification }, null, 2));
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).then(c => { process.exitCode = c; }, e => { console.error('error: ' + (e.stack || e.message)); process.exitCode = 1; });
}
