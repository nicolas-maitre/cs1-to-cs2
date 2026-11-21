#!/usr/bin/env node
// Summarises and sanity-checks a .cs1x file (schema/README.md §1). Node >= 20, no dependencies.
//
//   node cs1-exporter/tools/inspect-cs1x.mjs <file.cs1x> [--json] [--expect nodes=N,segments=N,imt=N,netinfosMin=N]
//
// The zip is read by hand (end of central directory → central directory → local headers) and entries are inflated
// with node:zlib inflateRawSync; every entry's CRC-32 and size are checked. Exit code 0 = all checks passed,
// 1 = at least one check failed, 2 = usage / unreadable file.

import { readFileSync } from 'node:fs';
import { inflateRawSync } from 'node:zlib';
import * as zlib from 'node:zlib';

const args = process.argv.slice(2);
const file = args.find((a) => !a.startsWith('--') && !isOptionValue(a));
const asJson = args.includes('--json');
const expect = parseExpect(optionValue('--expect'));
if (!file) {
  console.error('usage: inspect-cs1x.mjs <file.cs1x> [--json] [--expect nodes=N,segments=N,imt=N,netinfos=N]');
  process.exit(2);
}

function optionValue(name) {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : undefined;
}
function isOptionValue(a) {
  const i = args.indexOf(a);
  return i > 0 && args[i - 1] === '--expect';
}
function parseExpect(s) {
  const out = {};
  if (!s) return out;
  for (const part of s.split(',')) {
    const [k, v] = part.split('=');
    if (k && v !== undefined) out[k.trim()] = Number(v);
  }
  return out;
}

// ---------------------------------------------------------------- checks bookkeeping

const checks = [];
function check(name, ok, detail = '') {
  checks.push({ name, ok: !!ok, detail });
  return !!ok;
}

// ---------------------------------------------------------------- zip

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  if (typeof zlib.crc32 === 'function') return zlib.crc32(buf) >>> 0;
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function readZip(buf) {
  // End of central directory: last 22 bytes + up to 64 KiB of comment.
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 22 - 65535); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('not a zip: no end-of-central-directory record');
  const count = buf.readUInt16LE(eocd + 10);
  const cdSize = buf.readUInt32LE(eocd + 12);
  const cdOffset = buf.readUInt32LE(eocd + 16);
  check('zip: central directory inside the file', cdOffset + cdSize <= eocd, `offset ${cdOffset} size ${cdSize} eocd ${eocd}`);

  const entries = new Map();
  let p = cdOffset;
  for (let i = 0; i < count; i++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error(`bad central directory header #${i} at ${p}`);
    const flags = buf.readUInt16LE(p + 8);
    const method = buf.readUInt16LE(p + 10);
    const crc = buf.readUInt32LE(p + 16);
    const csize = buf.readUInt32LE(p + 20);
    const size = buf.readUInt32LE(p + 24);
    const nlen = buf.readUInt16LE(p + 28);
    const xlen = buf.readUInt16LE(p + 30);
    const clen = buf.readUInt16LE(p + 32);
    const local = buf.readUInt32LE(p + 42);
    const name = buf.toString(flags & 0x800 ? 'utf8' : 'latin1', p + 46, p + 46 + nlen);
    p += 46 + nlen + xlen + clen;

    if (buf.readUInt32LE(local) !== 0x04034b50) throw new Error(`bad local header for ${name}`);
    const lnlen = buf.readUInt16LE(local + 26);
    const lxlen = buf.readUInt16LE(local + 28);
    const lname = buf.toString(flags & 0x800 ? 'utf8' : 'latin1', local + 30, local + 30 + lnlen);
    check(`zip: ${name} local name matches`, lname === name, lname);
    const start = local + 30 + lnlen + lxlen;
    const raw = buf.subarray(start, start + csize);
    let data;
    if (method === 0) data = raw;
    else if (method === 8) data = inflateRawSync(raw);
    else throw new Error(`${name}: unsupported compression method ${method}`);
    check(`zip: ${name} size`, data.length === size, `${data.length} vs ${size}`);
    check(`zip: ${name} crc32`, crc32(data) === crc, `${crc32(data).toString(16)} vs ${crc.toString(16)}`);
    entries.set(name, { name, method, csize, size, data });
  }
  return entries;
}

// ---------------------------------------------------------------- main

let buf;
try {
  buf = readFileSync(file);
} catch (e) {
  console.error(`cannot read ${file}: ${e.message}`);
  process.exit(2);
}

let entries;
try {
  entries = readZip(buf);
} catch (e) {
  console.error(`${file}: ${e.message}`);
  process.exit(2);
}

const summary = { file, bytes: buf.length, entries: {} };
for (const e of entries.values()) summary.entries[e.name] = { size: e.size, compressed: e.csize, method: e.method === 8 ? 'deflate' : 'stored' };

function json(name) {
  const e = entries.get(name);
  if (!e) return undefined;
  try {
    const text = e.data.toString('utf8');
    check(`${name}: no BOM`, text.charCodeAt(0) !== 0xfeff);
    return JSON.parse(text);
  } catch (err) {
    check(`${name}: valid JSON`, false, err.message);
    return undefined;
  }
}

// manifest
const manifest = json('manifest.json');
check('manifest.json present', !!manifest);
const sections = manifest?.sections ?? {};
if (manifest) {
  check('manifest: format = cs1x', manifest.format === 'cs1x', manifest.format);
  check('manifest: formatVersion = 1', manifest.formatVersion === 1, manifest.formatVersion);
  for (const k of ['exporter', 'exportedAt', 'city', 'game', 'mods', 'coordinateSystem', 'mapSize', 'sections'])
    check(`manifest: has ${k}`, manifest[k] !== undefined);
  for (const [k, v] of Object.entries(sections)) check(`manifest: section ${k} status valid`, ['ok', 'failed', 'absent'].includes(v), v);
  summary.manifest = {
    exporter: manifest.exporter, exportedAt: manifest.exportedAt, city: manifest.city, game: manifest.game,
    mods: (manifest.mods ?? []).length, detectedMods: manifest.detectedMods, mapSize: manifest.mapSize,
    unlockedTiles: manifest.tiles?.unlocked?.length, sections,
  };
}
const report = json('report.json');
check('report.json present', !!report && Array.isArray(report.items));

const sectionFiles = { network: ['network.json'], netinfos: ['netinfos.json'], imt: ['imt.json'], terrain: ['terrain.json', 'terrain.r16'] };
for (const [s, files] of Object.entries(sectionFiles))
  if (sections[s] === 'ok') for (const f of files) check(`section ${s} ok → ${f} present`, entries.has(f));

// network
const network = sections.network === 'ok' ? json('network.json') : undefined;
const nodeById = new Map();
const segById = new Map();
if (network) {
  for (const n of network.nodes) nodeById.set(n.id, n);
  for (const s of network.segments) segById.set(s.id, s);
  check('network: node ids unique', nodeById.size === network.nodes.length);
  check('network: segment ids unique', segById.size === network.segments.length);
  let badEnds = 0, badBezier = 0, badKind = 0, badNodeSegs = 0;
  const kinds = {};
  for (const s of network.segments) {
    if (!nodeById.has(s.startNode) || !nodeById.has(s.endNode)) badEnds++;
    if (!Array.isArray(s.bezier) || s.bezier.length !== 4 || s.bezier.some((p) => p.length !== 3 || p.some((v) => typeof v !== 'number'))) badBezier++;
    if (!['ground', 'elevated', 'bridge', 'slope', 'tunnel'].includes(s.elevationKind)) badKind++;
    kinds[s.elevationKind] = (kinds[s.elevationKind] ?? 0) + 1;
  }
  for (const n of network.nodes) if (n.segments.some((id) => !segById.has(id))) badNodeSegs++;
  check('network: every segment end node exists', badEnds === 0, `${badEnds} bad`);
  check('network: every bezier has 4 numeric points', badBezier === 0, `${badBezier} bad`);
  check('network: elevationKind valid', badKind === 0, `${badKind} bad`);
  check('network: every node segment id exists', badNodeSegs === 0, `${badNodeSegs} bad`);
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity, outsideBad = 0;
  for (const n of network.nodes) {
    // Outside-connection nodes (flag Outside) sit a few metres past the map edge; nothing else may.
    const beyond = Math.abs(n.position[0]) > 8640 || Math.abs(n.position[2]) > 8640;
    if (beyond && !n.flags.split(', ').includes('Outside')) outsideBad++;
    minX = Math.min(minX, n.position[0]); maxX = Math.max(maxX, n.position[0]);
    minZ = Math.min(minZ, n.position[2]); maxZ = Math.max(maxZ, n.position[2]);
  }
  check('network: only Outside-connection nodes beyond ±8640 m', outsideBad === 0 && minX >= -8704 && maxX <= 8704 && minZ >= -8704 && maxZ <= 8704, `${outsideBad} bad; x ${minX.toFixed(0)}..${maxX.toFixed(0)} z ${minZ.toFixed(0)}..${maxZ.toFixed(0)}`);
  summary.network = { nodes: network.nodes.length, segments: network.segments.length, elevationKinds: kinds,
    bounds: { minX: +minX.toFixed(1), maxX: +maxX.toFixed(1), minZ: +minZ.toFixed(1), maxZ: +maxZ.toFixed(1) } };
}

// netinfos
const netinfos = sections.netinfos === 'ok' ? json('netinfos.json') : undefined;
if (netinfos) {
  const byName = new Map(netinfos.netInfos.map((i) => [i.name, i]));
  check('netinfos: names unique', byName.size === netinfos.netInfos.length);
  if (network) {
    const used = new Set([...network.nodes, ...network.segments].map((x) => x.info).filter((x) => x != null));
    const missing = [...used].filter((n) => !byName.has(n));
    check('netinfos: every used NetInfo present', missing.length === 0, missing.slice(0, 5).join('; '));
    summary.netinfos = { total: byName.size, used: used.size };
  }
  let badParent = 0, badVariant = 0, variants = 0, grounds = 0, orphanVariants = 0;
  for (const i of netinfos.netInfos) {
    if (i.parent != null) {
      variants++;
      if (!byName.has(i.parent)) badParent++;
    } else {
      grounds++;
      for (const v of Object.values(i.variants ?? {})) if (!byName.has(v)) badVariant++;
    }
  }
  // A used elevated/bridge/slope/tunnel segment must lead to a ground NetInfo (the M0 bug).
  if (network) {
    for (const s of network.segments) {
      if (s.elevationKind === 'ground' || s.info == null) continue;
      const i = byName.get(s.info);
      if (i && i.parent == null && !Object.keys(i.variants ?? {}).length) orphanVariants++;
    }
  }
  check('netinfos: every parent present', badParent === 0, `${badParent} missing`);
  check('netinfos: every variant present', badVariant === 0, `${badVariant} missing`);
  Object.assign((summary.netinfos ??= {}), { total: byName.size, ground: grounds, variants });
  summary.netinfos.nonGroundSegmentsWithoutParent = orphanVariants;
}

// imt
const imt = sections.imt === 'ok' ? json('imt.json') : undefined;
if (imt) {
  let badPoly = 0, badRef = 0;
  const styles = {};
  let node = 0, seg = 0;
  for (const f of imt.fillers) {
    if (!Array.isArray(f.polygon) || f.polygon.length < 3) badPoly++;
    if (f.markingType === 'Node') { node++; if (network && !nodeById.has(f.markingId)) badRef++; }
    else if (f.markingType === 'Segment') { seg++; if (network && !segById.has(f.markingId)) badRef++; }
    styles[f.styleType] = (styles[f.styleType] ?? 0) + 1;
  }
  check('imt: every polygon has ≥ 3 points', badPoly === 0, `${badPoly} bad`);
  check('imt: every markingId exists in the network', badRef === 0, `${badRef} bad`);
  summary.imt = { fillers: imt.fillers.length, node, segment: seg, styles };
}

// terrain
const terrain = sections.terrain === 'ok' ? json('terrain.json') : undefined;
if (terrain) {
  const r16 = entries.get(terrain.file ?? 'terrain.r16')?.data;
  const res = terrain.resolution;
  check('terrain: r16 size = resolution² × 2', r16 && r16.length === res * res * 2, `${r16?.length} vs ${res * res * 2}`);
  if (r16 && r16.length === res * res * 2) {
    const h = (row, col) => r16.readUInt16LE(2 * (row * res + col)) * terrain.heightScale;
    let lo = Infinity, hi = -Infinity;
    for (let i = 0; i < res * res; i++) {
      const v = r16.readUInt16LE(2 * i);
      if (v < lo) lo = v;
      if (v > hi) hi = v;
    }
    lo *= terrain.heightScale; hi *= terrain.heightScale;
    check('terrain: min/max match the data', Math.abs(lo - terrain.min) < 1e-3 && Math.abs(hi - terrain.max) < 1e-3, `${lo}/${hi} vs ${terrain.min}/${terrain.max}`);
    const spacing = terrain.size / (res - 1);
    const sample = (x, z, transpose = false) => {
      let fc = Math.min(Math.max((x + terrain.size / 2) / spacing, 0), res - 1);
      let fr = Math.min(Math.max((z + terrain.size / 2) / spacing, 0), res - 1);
      if (transpose) [fc, fr] = [fr, fc];
      const c0 = Math.min(Math.floor(fc), res - 2), r0 = Math.min(Math.floor(fr), res - 2);
      const tx = fc - c0, tz = fr - r0;
      const a = h(r0, c0) * (1 - tx) + h(r0, c0 + 1) * tx;
      const b = h(r0 + 1, c0) * (1 - tx) + h(r0 + 1, c0 + 1) * tx;
      return a * (1 - tz) + b * tz;
    };
    // Orientation: ground nodes' terrain height (position.y − heightAboveTerrain, from the game's own sampler)
    // must match a bilinear sample of the grid at (x, z) – and must NOT match the transposed / flipped layouts.
    if (network) {
      const layouts = {
        spec: (x, z) => sample(x, z),
        transposed: (x, z) => sample(x, z, true),
        flipZ: (x, z) => sample(x, -z),
        flipX: (x, z) => sample(-x, z),
      };
      const errs = Object.fromEntries(Object.keys(layouts).map((k) => [k, []]));
      for (const n of network.nodes) {
        if (n.elevation !== 0 || !/OnGround/.test(n.flags) || typeof n.heightAboveTerrain !== 'number') continue;
        const t = n.position[1] - n.heightAboveTerrain;
        for (const [k, f] of Object.entries(layouts)) errs[k].push(Math.abs(f(n.position[0], n.position[2]) - t));
      }
      const stat = (a) => {
        const s = [...a].sort((x, y) => x - y);
        return { n: s.length, median: +(s[s.length >> 1] ?? NaN).toFixed(3), p99: +(s[Math.floor(s.length * 0.99)] ?? NaN).toFixed(3), max: +(s[s.length - 1] ?? NaN).toFixed(3) };
      };
      const stats = Object.fromEntries(Object.entries(errs).map(([k, a]) => [k, stat(a)]));
      const best = Object.entries(stats).sort((a, b) => a[1].median - b[1].median)[0][0];
      check('terrain: orientation matches node heights (spec layout is best and median error < 0.5 m)',
        best === 'spec' && stats.spec.median < 0.5, `spec median ${stats.spec.median} m over ${stats.spec.n} nodes; best layout: ${best}`);
      summary.terrainOrientation = stats;
    }
    summary.terrain = { resolution: res, size: terrain.size, heightScale: terrain.heightScale, min: +lo.toFixed(3), max: +hi.toFixed(3),
      centre: +h(res >> 1, res >> 1).toFixed(3), verification: terrain.verification };
  }
}

// expectations
const actual = {
  nodes: summary.network?.nodes, segments: summary.network?.segments, imt: summary.imt?.fillers, netinfos: summary.netinfos?.total,
};
for (const [k, v] of Object.entries(expect)) if (k !== 'netinfosMin') check(`expect ${k} = ${v}`, actual[k] === v, `got ${actual[k]}`);
if (expect.netinfosMin !== undefined) check(`expect netinfos > ${expect.netinfosMin}`, actual.netinfos > expect.netinfosMin, `got ${actual.netinfos}`);

// report
const items = report?.items ?? [];
summary.report = { errors: items.filter((i) => i.level === 'error').length, warnings: items.filter((i) => i.level === 'warning').length, info: items.filter((i) => i.level === 'info').length };
check('report: no error items', summary.report.errors === 0, `${summary.report.errors} errors`);

const failed = checks.filter((c) => !c.ok);
summary.checks = { passed: checks.length - failed.length, failed: failed.length };

if (asJson) {
  console.log(JSON.stringify({ ok: failed.length === 0, summary, failures: failed, report: items }, null, 2));
} else {
  const kb = (n) => `${(n / 1024).toFixed(0)} KiB`;
  console.log(`${file}  (${kb(buf.length)})`);
  if (summary.manifest) {
    const m = summary.manifest;
    console.log(`  city ${m.city?.name} (save ${m.city?.saveName}), CS1 ${m.game?.version}, exporter ${m.exporter?.name} ${m.exporter?.version}, ${m.exportedAt}`);
    console.log(`  mods: ${m.mods} enabled; detected ${JSON.stringify(m.detectedMods)}; unlocked tiles: ${m.unlockedTiles}`);
    console.log(`  sections: ${Object.entries(sections).map(([k, v]) => `${k}=${v}`).join(' ')}`);
  }
  console.log(`  entries: ${Object.entries(summary.entries).map(([k, v]) => `${k} ${kb(v.size)}${v.method === 'deflate' ? ` → ${kb(v.compressed)}` : ' (stored)'}`).join(', ')}`);
  if (summary.network) console.log(`  network: ${summary.network.nodes} nodes, ${summary.network.segments} segments, kinds ${JSON.stringify(summary.network.elevationKinds)}`);
  if (summary.netinfos) console.log(`  netinfos: ${summary.netinfos.total} (${summary.netinfos.used} used, ${summary.netinfos.ground} ground, ${summary.netinfos.variants} variants)`);
  if (summary.imt) console.log(`  imt: ${summary.imt.fillers} fillers (${summary.imt.node} node, ${summary.imt.segment} segment) ${JSON.stringify(summary.imt.styles)}`);
  if (summary.terrain) console.log(`  terrain: ${summary.terrain.resolution}² over ${summary.terrain.size} m, ×${summary.terrain.heightScale}, min ${summary.terrain.min} m, max ${summary.terrain.max} m, centre ${summary.terrain.centre} m`);
  if (summary.terrainOrientation) {
    const o = summary.terrainOrientation;
    console.log(`  terrain vs ${o.spec.n} ground nodes, median |error|: spec ${o.spec.median} m, transposed ${o.transposed.median} m, flipZ ${o.flipZ.median} m, flipX ${o.flipX.median} m`);
  }
  console.log(`  report: ${summary.report.errors} errors, ${summary.report.warnings} warnings, ${summary.report.info} info`);
  for (const i of items) if (i.level !== 'info') console.log(`    [${i.level}] ${i.section}: ${i.message}`);
  console.log(`  checks: ${summary.checks.passed} passed, ${summary.checks.failed} failed`);
  for (const f of failed) console.log(`    FAIL ${f.name}${f.detail ? ` (${f.detail})` : ''}`);
  console.log(failed.length === 0 ? 'OK' : 'FAILED');
}
process.exit(failed.length === 0 ? 0 : 1);
