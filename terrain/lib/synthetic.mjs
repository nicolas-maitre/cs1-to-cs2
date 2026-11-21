// Synthetic CS1 terrain + nodes, for tests and for the in-game check before a real .cs1x exists.
import { writeZip } from './zip.mjs';

/**
 * A terrain that is easy to recognise and impossible to mistake for its mirror or transpose:
 * a tilted base plane (rises to the north-east), a cone hill at (+1500, +1000), a valley trench along x = -1200,
 * and a plateau square at (-2000, +2500). Heights in metres, all within 0..1024.
 */
export function syntheticHeight(x, z) {
  let h = 120 + 0.004 * x + 0.008 * z;                       // tilt: +x and +z differ, so flips and transposes show
  const dh = Math.hypot(x - 1500, z - 1000);
  if (dh < 900) h += 180 * (1 - dh / 900);                   // cone, 180 m high
  const dv = Math.abs(x + 1200);
  if (dv < 250 && z > -3000 && z < 3000) h -= 40 * (1 - dv / 250); // trench, 40 m deep
  if (Math.abs(x + 2000) < 400 && Math.abs(z - 2500) < 400) h = Math.max(h, 230); // plateau at 230 m
  return Math.min(1023, Math.max(0, h));
}

export const SYNTHETIC_NODES = [
  [0, 0], [1500, 1000], [1500, 400], [-1200, 0], [-1200, 2000], [-2000, 2500], [3000, -3000], [-3000, 3000],
  [600, -2200], [2400, 2400], [-500, 1500], [4000, 500],
];

/** Builds a .cs1x Buffer: manifest, terrain.json + terrain.r16 (resolution res over 17 280 m), and network nodes. */
export function buildSyntheticCs1x({ res = 1081, size = 17280, heightFn = syntheticHeight, nodes = SYNTHETIC_NODES } = {}) {
  const heightScale = 1024 / 65536;
  const sp = size / (res - 1);
  const r16 = Buffer.alloc(res * res * 2);
  let min = Infinity, max = -Infinity;
  for (let row = 0; row < res; row++) {
    for (let col = 0; col < res; col++) {
      const h = heightFn(-size / 2 + col * sp, -size / 2 + row * sp);
      const v = Math.max(0, Math.min(65535, Math.round(h / heightScale)));
      r16.writeUInt16LE(v, (row * res + col) * 2);
      const hq = v * heightScale;
      if (hq < min) min = hq;
      if (hq > max) max = hq;
    }
  }
  const terrain = {
    resolution: res, size, heightScale, file: 'terrain.r16',
    rowOrder: 'row 0 = minimum z (south), column 0 = minimum x (west)', byteOrder: 'little-endian uint16',
    source: 'synthetic', seaLevel: null, min, max,
  };
  const network = {
    nodes: nodes.map(([x, z], i) => ({ id: i + 1, info: 'Basic Road', position: [x, heightFn(x, z), z], flags: 'Created, OnGround', elevation: 0, heightAboveTerrain: 0, building: 0, segments: [] })),
    segments: [],
  };
  const manifest = {
    format: 'cs1x', formatVersion: 1, exporter: { name: 'terrain/lib/synthetic.mjs', version: '1' },
    exportedAt: new Date().toISOString(), city: { name: 'Synthetic', saveName: null }, mapSize: size,
    coordinateSystem: 'CS1 world space, metres, Y up, origin at map centre',
    sections: { network: 'ok', netinfos: 'absent', imt: 'absent', terrain: 'ok' },
  };
  return writeZip([
    { name: 'manifest.json', data: JSON.stringify(manifest) },
    { name: 'terrain.json', data: JSON.stringify(terrain) },
    { name: 'terrain.r16', data: r16, store: true },
    { name: 'network.json', data: JSON.stringify(network) },
    { name: 'report.json', data: JSON.stringify({ items: [] }) },
  ]);
}

// CLI: node terrain/lib/synthetic.mjs out.cs1x   (writes the synthetic .cs1x used by the in-game check)
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (!process.argv[2]) { console.error('usage: synthetic.mjs out.cs1x'); process.exit(2); }
  writeFileSync(process.argv[2], buildSyntheticCs1x());
  console.log(resolve(process.argv[2]));
}
