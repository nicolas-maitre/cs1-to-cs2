// node --test tools/release/build.test.mjs   (game-free: checks versions, the zip writer and the notes)
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { checkVersions, zipFolder, latestChangelogEntry, releaseNotes, COMPONENTS } from './build.mjs';

test('every component version is SemVer and its declarations agree', () => {
  const v = checkVersions();
  assert.deepEqual(v.problems, []);
  assert.match(v.product, /^\d+\.\d+\.\d+/);
  for (const c of COMPONENTS) assert.ok(v.components[c.id], c.id);
});

test('zipFolder writes a valid zip (central directory, CRC, deflate) that round-trips', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'rel-'));
  fs.mkdirSync(path.join(dir, 'a', 'b'), { recursive: true });
  fs.writeFileSync(path.join(dir, 'a', 'b', 'big.txt'), 'x'.repeat(10000));
  fs.writeFileSync(path.join(dir, 'empty.txt'), '');
  fs.writeFileSync(path.join(dir, 'ü.txt'), 'unicode name');
  const zip = path.join(dir, '..', path.basename(dir) + '.zip');
  assert.equal(zipFolder(dir, zip, 'pkg/'), 3);
  const buf = fs.readFileSync(zip);
  const eocd = buf.length - 22;
  assert.equal(buf.readUInt32LE(eocd), 0x06054b50);
  let p = buf.readUInt32LE(eocd + 16);
  const got = {};
  for (let i = 0; i < buf.readUInt16LE(eocd + 10); i++) {
    const method = buf.readUInt16LE(p + 10), csize = buf.readUInt32LE(p + 20), nlen = buf.readUInt16LE(p + 28), local = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nlen);
    const start = local + 30 + buf.readUInt16LE(local + 26);
    const raw = buf.subarray(start, start + csize);
    got[name] = (method === 8 ? zlib.inflateRawSync(raw) : raw).toString('utf8');
    p += 46 + nlen;
  }
  assert.deepEqual(Object.keys(got).sort(), ['pkg/a/b/big.txt', 'pkg/empty.txt', 'pkg/ü.txt']);
  assert.equal(got['pkg/a/b/big.txt'].length, 10000);
  assert.equal(got['pkg/ü.txt'], 'unicode name');
});

test('release notes: top entry, component table, checksums', () => {
  assert.match(latestChangelogEntry('CHANGELOG.md'), /^## /);
  const v = checkVersions();
  const notes = releaseNotes(v, [{ id: 'web', file: `web-${v.components.web}.zip`, files: 3 }], ['abc  web.zip']);
  assert.match(notes, new RegExp(`# CS1 → CS2 converter ${v.product.replace(/\./g, '\\.')}`));
  assert.match(notes, /\| CS1 → CS2 web tool .*\| `web-.*\.zip` \|/);
  assert.match(notes, /inside the importer mod/);
  assert.match(notes, /abc {2}web\.zip/);
});
