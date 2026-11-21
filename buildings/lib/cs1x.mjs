// Minimal .cs1x (zip) reader: central directory → local headers → inflateRawSync. No dependencies.
import fs from 'node:fs';
import { inflateRawSync } from 'node:zlib';

/** @returns {Map<string, Buffer>} entry name → content */
export function readZip(file) {
  const buf = fs.readFileSync(file);
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 22 - 65535); i--) if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new Error(`${file}: not a zip`);
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const out = new Map();
  for (let i = 0; i < count; i++) {
    const method = buf.readUInt16LE(p + 10), csize = buf.readUInt32LE(p + 20);
    const nlen = buf.readUInt16LE(p + 28), xlen = buf.readUInt16LE(p + 30), clen = buf.readUInt16LE(p + 32), local = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nlen);
    p += 46 + nlen + xlen + clen;
    const start = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28);
    const raw = buf.subarray(start, start + csize);
    out.set(name, method === 8 ? inflateRawSync(raw) : raw);
  }
  return out;
}

/** Parsed JSON sections of a .cs1x that the buildings work needs; a missing / non-ok section is null. */
export function readCs1x(file) {
  const z = readZip(file);
  const json = (n) => (z.has(n) ? JSON.parse(z.get(n).toString('utf8')) : null);
  const manifest = json('manifest.json');
  const ok = (s) => manifest?.sections?.[s] === 'ok';
  return {
    manifest,
    network: ok('network') ? json('network.json') : null,
    buildings: ok('buildings') ? json('buildings.json') : null,
    assets: ok('assets') ? json('assets.json') : null,
    trees: ok('trees') ? json('trees.json') : null,
    props: ok('props') ? json('props.json') : null,
    imt: ok('imt') ? json('imt.json') : null,
    districts: ok('districts') ? json('districts.json') : null,
    districtRaster: ok('districts') && z.has('districts.u8') ? new Uint8Array(z.get('districts.u8')) : null,
    parkRaster: ok('districts') && z.has('parks.u8') ? new Uint8Array(z.get('parks.u8')) : null,
  };
}
