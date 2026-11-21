#!/usr/bin/env node
// CS1 terrain (.cs1x) → CS2 heightmap for a crop. Node >= 20, no dependencies.
//
//   node terrain/convert.mjs --cs1x city.cs1x --crop 1200,-300,4000 --out out/terrain [--mode absolute|fit] [--no-png]
//   node terrain/convert.mjs --cs1x city.cs1x --plan package/cs1conv-plan.json --out out/terrain
//   node terrain/convert.mjs --terrain-json terrain.json --r16 terrain.r16 --crop 0,0,14336 --out out/terrain
//
// Writes <out>/heightmap.r16 (what terrain/apply.mjs loads; 14 336 m playable area), <out>/worldmap.r16 (the 57 344 m
// world/backdrop map around it, same CS1 data; skip with --no-world), <out>/heightmap.png (16-bit greyscale, north up,
// for the map editor's Import Heightmap) and <out>/terrain-cs2.json (scale, offset, transform, stats).
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { loadCs1xTerrain, decodeCs1Raw, convert, rawToBytes, flipRows, CS2 } from './lib/heightmap.mjs';
import { encodeGray16 } from './lib/png.mjs';

function parseArgs(argv) {
  const a = { png: true, world: true };
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i];
    const v = () => { if (i + 1 >= argv.length) throw new Error(k + ' needs a value'); return argv[++i]; };
    switch (k) {
      case '--cs1x': a.cs1x = v(); break;
      case '--terrain-json': a.terrainJson = v(); break;
      case '--r16': a.r16 = v(); break;
      case '--crop': a.crop = v(); break;
      case '--plan': a.plan = v(); break;
      case '--out': a.out = v(); break;
      case '--mode': a.mode = v(); break;
      case '--height-scale': a.heightScale = Number(v()); break;
      case '--height-offset': a.heightOffset = Number(v()); break;
      case '--no-png': a.png = false; break;
      case '--no-world': a.world = false; break;
      case '-h': case '--help': a.help = true; break;
      default: throw new Error('unknown argument ' + k);
    }
  }
  return a;
}

export function main(argv) {
  const a = parseArgs(argv);
  if (a.help || !a.out || !(a.cs1x || (a.terrainJson && a.r16)) || !(a.crop || a.plan)) {
    console.error('usage: convert.mjs (--cs1x f | --terrain-json f --r16 f) (--crop cx,cz,size | --plan cs1conv-plan.json) --out dir [--mode absolute|fit] [--height-scale s --height-offset o] [--no-png] [--no-world]');
    return a.help ? 0 : 2;
  }
  let cs1;
  if (a.cs1x) cs1 = loadCs1xTerrain(readFileSync(a.cs1x));
  else {
    const meta = JSON.parse(readFileSync(a.terrainJson, 'utf8'));
    cs1 = { meta, heights: decodeCs1Raw(meta, readFileSync(a.r16)) };
  }
  let crop, transform;
  if (a.plan) {
    const plan = JSON.parse(readFileSync(a.plan, 'utf8'));
    crop = plan.crop; transform = plan.transform ? { dx: plan.transform.dx, dz: plan.transform.dz } : undefined;
  } else {
    const [centerX, centerZ, size] = a.crop.split(',').map(Number);
    crop = { centerX, centerZ, size };
  }
  if (![crop.centerX, crop.centerZ, crop.size].every(Number.isFinite)) throw new Error('bad crop ' + JSON.stringify(crop));
  if (crop.size > CS2.size) console.error(`warning: crop size ${crop.size} m is larger than the CS2 playable area ${CS2.size} m; the edges are cut`);

  const t0 = Date.now();
  const { raw, worldRaw, meta } = convert(cs1.meta, cs1.heights, crop, { transform, mode: a.mode, heightScale: a.heightScale, heightOffset: a.heightOffset, world: a.world });
  const out = resolve(a.out);
  mkdirSync(out, { recursive: true });
  writeFileSync(join(out, 'heightmap.r16'), rawToBytes(raw));
  if (worldRaw) writeFileSync(join(out, meta.world.file), rawToBytes(worldRaw));
  if (a.png) {
    writeFileSync(join(out, 'heightmap.png'), encodeGray16(CS2.resolution, CS2.resolution, flipRows(raw, CS2.resolution)));
    meta.png = 'heightmap.png';
    meta.pngRowOrder = 'PNG top row = maximum z (north), as an image viewed north-up';
  }
  meta.source.file = a.cs1x ? resolve(a.cs1x) : resolve(a.terrainJson);
  meta.createdAt = new Date().toISOString();
  writeFileSync(join(out, 'terrain-cs2.json'), JSON.stringify(meta, null, 2) + '\n');
  console.log(JSON.stringify({ out, heightScale: meta.heightScale, heightOffset: meta.heightOffset, stats: meta.stats, ms: Date.now() - t0 }));
  return 0;
}

if (import.meta.url === `file:///${process.argv[1].replace(/\\/g, '/')}` || process.argv[1]?.endsWith('convert.mjs')) {
  try { process.exitCode = main(process.argv.slice(2)); } catch (e) { console.error('error: ' + e.message); process.exitCode = 1; }
}
