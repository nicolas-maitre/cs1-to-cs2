// The web port of the terrain converter, water and outside connections must give exactly what the terrain
// worker's Node scripts give: byte-identical .r16 files, the same metadata, the same water.set sources.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';
import { deriveConnections as theirConnections } from '../../terrain/lib/connections.mjs';
import { loadCs1xTerrain } from '../../terrain/lib/heightmap.mjs';
import { buildSyntheticCs1x } from '../../terrain/lib/synthetic.mjs';
import { convertWater as theirWater, depthCheckpoints as theirCheckpoints, regionSources as theirRegions } from '../../terrain/lib/water.mjs';
import { readCityFile } from '../src/core/cs1x.ts';
import { buildPackage, completePackage, packageFiles } from '../src/core/plan.ts';
import { CS2_TERRAIN, convertTerrain, cs1Heights, sampleCs2 } from '../src/core/terrain/heightmap.ts';
import { deriveConnections } from '../src/core/terrain/connections.ts';
import type { Crop } from '../src/core/types.ts';

const REPO = path.resolve(import.meta.dirname, '..', '..');
const CONVERT = path.join(REPO, 'terrain', 'convert.mjs');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cs1conv-terrain-'));
afterAll(() => fs.rmSync(tmp, { recursive: true, force: true }));

/** Runs terrain/convert.mjs on a .cs1x file with a crop; returns its heightmap, world map and metadata. */
function theirs(cs1x: string, crop: Crop, name: string) {
  const out = path.join(tmp, name);
  execFileSync(process.execPath, [CONVERT, '--cs1x', cs1x, '--crop', `${crop.centerX},${crop.centerZ},${crop.size}`, '--out', out, '--no-png'], { stdio: 'pipe' });
  return {
    heightmap: fs.readFileSync(path.join(out, 'heightmap.r16')), worldmap: fs.readFileSync(path.join(out, 'worldmap.r16')),
    meta: JSON.parse(fs.readFileSync(path.join(out, 'terrain-cs2.json'), 'utf8')),
  };
}
const strip = (m: Record<string, unknown>) => { const { createdAt: _c, png: _p, pngRowOrder: _r, ...rest } = m; return { ...rest, source: { ...(rest.source as object), file: undefined } }; };

describe('terrain converter port', () => {
  it('is byte-identical to terrain/convert.mjs on the synthetic terrain', async () => {
    const file = path.join(tmp, 'synthetic.cs1x');
    fs.writeFileSync(file, buildSyntheticCs1x({ res: 433 }));
    const crop = { centerX: 1200, centerZ: -300, size: 4000 };
    const t = theirs(file, crop, 'syn');
    const city = await readCityFile(new Uint8Array(fs.readFileSync(file)), { hash: false });
    const pkg = buildPackage(city, { crop });
    const files = completePackage(city, pkg);
    const get = (p: string) => files.find(f => f.path === p)!.data;
    expect(Buffer.from(get('terrain/heightmap.r16')).equals(t.heightmap)).toBe(true);
    expect(Buffer.from(get('terrain/worldmap.r16')).equals(t.worldmap)).toBe(true);
    expect(strip(JSON.parse(new TextDecoder().decode(get('terrain/terrain.json'))))).toEqual(strip(t.meta));
    expect(pkg.plan.terrain).toMatchObject({ dir: 'terrain', meta: 'terrain/terrain.json', heightScale: 1024, heightOffset: 0, heightMode: 'absolute', world: true });
    expect(pkg.plan.terrain!.seaLevelDry).toBe(Math.floor(t.meta.stats.min) - 20);
    expect(pkg.summary.find(s => s.id === 'terrain')!.summary).toMatch(/4096² in the package/);
  }, 60_000);

  it('keeps heights: CS2 sample == CS1 sample, north stays north', () => {
    const n = 101, size = 17280, h = new Uint16Array(n * n);
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) h[r * n + c] = Math.round((200 + c * 0.5 + r * 1.5) / 0.015625); // rises east and (more) north
    const meta = { resolution: n, size, heightScale: 0.015625 };
    const crop = { centerX: 500, centerZ: -800, size: 3000 };
    const { raw, meta: m } = convertTerrain(meta, cs1Heights({ ...meta, heights: h }), crop, { world: false });
    expect(raw.length).toBe(CS2_TERRAIN.resolution ** 2);
    const at = (x: number, z: number) => sampleCs2(m, raw, x, z);
    expect(at(0, 1000)).toBeGreaterThan(at(0, -1000)); // north is higher
    expect(at(1000, 0)).toBeGreaterThan(at(-1000, 0)); // east is higher
    // the CS2 origin is the crop centre (500, −800) in CS1
    const sp = size / (n - 1), expect0 = 200 + ((500 + size / 2) / sp) * 0.5 + ((-800 + size / 2) / sp) * 1.5;
    expect(Math.abs(at(0, 0) - expect0)).toBeLessThan(0.05);
  });
});

const SAMPLE = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local'), 'cs1-to-cs2', 'samples', 'skibidi.cs1x');
describe.skipIf(!fs.existsSync(SAMPLE))('Skibidi: same terrain, water and connections as the terrain scripts', () => {
  it('heightmap, world map, water.set sources, checkpoints and outside connections', async () => {
    const buf = fs.readFileSync(SAMPLE);
    const city = await readCityFile(new Uint8Array(buf), { hash: false });
    const crop = { centerX: 3750, centerZ: -1250, size: 3000 };
    const pkg = buildPackage(city, { crop });
    const files = completePackage(city, pkg);
    const t = theirs(SAMPLE, crop, 'skibidi');
    expect(Buffer.from(files.find(f => f.path === 'terrain/heightmap.r16')!.data).equals(t.heightmap)).toBe(true);
    expect(Buffer.from(files.find(f => f.path === 'terrain/worldmap.r16')!.data).equals(t.worldmap)).toBe(true);

    // water: what `terrain/mapprep.mjs water` sends to water.set
    const zip = (await import('../../terrain/lib/zip.mjs')).readZip(buf);
    const wj = JSON.parse(zip.get('water.json').read().toString('utf8'));
    const plan = JSON.parse(packageFiles(pkg).find(f => f.path === 'cs1conv-plan.json')!.text);
    const w = theirWater(wj, plan);
    const rs = theirRegions(wj.depth, zip.get(wj.depth.file).read(), loadCs1xTerrain(buf), plan, wj.seaLevel, w.sources);
    expect(plan.water.apply).toEqual(JSON.parse(JSON.stringify({ seaLevel: w.seaLevel, sources: [...w.sources, ...rs.sources] })));
    expect(plan.water.checkpoints).toEqual(theirCheckpoints(wj.depth, zip.get(wj.depth.file).read(), plan));

    // outside connections: `terrain/mapprep.mjs connections`
    expect(plan.outsideConnections).toEqual(JSON.parse(JSON.stringify(theirConnections(buf, plan))));
    expect(deriveConnections(city, pkg.plan).counts.connections).toBeGreaterThan(0);
  }, 120_000);
});

describe('package zip', () => {
  it('contains the terrain files, deflated, and the plan points at them', async () => {
    const { packageZip } = await import('../src/core/zip.ts');
    const { unzipSync, strFromU8 } = await import('fflate');
    const city = await readCityFile(new Uint8Array(buildSyntheticCs1x({ res: 217 })), { hash: false });
    const r = packageZip(city, { crop: { centerX: 0, centerZ: 0, size: 3000 } });
    expect(r.terrain).toBe(true);
    expect(r.zip.length).toBeLessThan(40 * 1048576); // 64 MB of heightmaps deflate well
    const files = unzipSync(r.zip);
    expect(files['terrain/heightmap.r16'].length).toBe(4096 * 4096 * 2);
    expect(files['terrain/worldmap.r16'].length).toBe(4096 * 4096 * 2);
    const plan = JSON.parse(strFromU8(files['cs1conv-plan.json']));
    expect(plan.terrain).toMatchObject({ dir: 'terrain', meta: 'terrain/terrain.json', world: true });
    expect(JSON.parse(strFromU8(files['terrain/terrain.json']))).toMatchObject({ file: 'heightmap.r16', world: { file: 'worldmap.r16' } });
    expect(strFromU8(files['README.txt'])).toContain('terrain\\            The CS1 terrain');
  }, 60_000);
});
