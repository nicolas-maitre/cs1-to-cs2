import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { startFakeBridge } from './fake-bridge.js';
import { DEAD_PID, tmpDir } from './helpers.js';
import { connect, readDiscovery, rpc, health, GamectlError } from '../lib/index.js';

/** @type {import('./fake-bridge.js').FakeBridge} */
let fake;
let dir = '';

before(async () => {
  dir = tmpDir();
  fake = await startFakeBridge({ game: 'cs2', dir });
});
after(() => fake.close());

test('discovery: reads port/token/pid of a live process', () => {
  const info = readDiscovery('cs2', { dir });
  assert.equal(info.port, fake.port);
  assert.equal(info.token, fake.token);
  assert.equal(info.pid, process.pid);
});

test('discovery: missing file → not_running', () => {
  assert.throws(() => readDiscovery('cs1', { dir }), (e) => e instanceof GamectlError && e.code === 'not_running' && /no discovery file/.test(e.message));
});

test('discovery: stale pid is rejected', () => {
  const d = tmpDir();
  fs.writeFileSync(path.join(d, 'cs1.json'), JSON.stringify({ game: 'cs1', port: 1, token: 'x', pid: DEAD_PID }));
  assert.throws(() => readDiscovery('cs1', { dir: d }), (e) => e instanceof GamectlError && e.code === 'not_running' && /stale/.test(e.message));
});

test('discovery: garbage and wrong game are rejected', () => {
  const d = tmpDir();
  fs.writeFileSync(path.join(d, 'cs1.json'), '{not json');
  assert.throws(() => readDiscovery('cs1', { dir: d }), /not valid JSON/);
  fs.writeFileSync(path.join(d, 'cs2.json'), JSON.stringify({ game: 'cs1', port: 1, token: 'x', pid: process.pid }));
  assert.throws(() => readDiscovery('cs2', { dir: d }), /is for "cs1"/);
});

test('connect: health pid must match the discovery pid', async () => {
  const d = tmpDir();
  const other = await startFakeBridge({ game: 'cs2', dir: d, pid: 4242, writeDiscovery: false });
  try {
    // discovery claims our (alive) pid, but the bridge on that port answers for pid 4242
    fs.writeFileSync(path.join(d, 'cs2.json'), JSON.stringify({ game: 'cs2', port: other.port, token: other.token, pid: process.pid }));
    await assert.rejects(connect('cs2', { dir: d }), /answers for pid 4242/);
  } finally {
    await other.close();
  }
});

test('connect + call: sends the Bearer token and JSON, no browser headers', async () => {
  const b = await connect('cs2', { dir });
  const n = fake.requests.length;
  const r = await b.call('camera.set', { x: 1, z: 2 }, { timeout: 7 });
  assert.equal(r.x, 1);
  const req = fake.requests[n];
  assert.equal(req.headers.authorization, `Bearer ${fake.token}`);
  assert.match(String(req.headers['content-type']), /^application\/json/);
  assert.equal(req.headers.origin, undefined);
  assert.equal(req.headers['sec-fetch-mode'], undefined);
  assert.deepEqual(req.body, { method: 'camera.set', params: { x: 1, z: 2 }, timeout_s: 7 });
});

test('health: no token needed, returns status', async () => {
  const h = await health(fake.port);
  assert.equal(h.ok, true);
  assert.equal(h.status.game, 'cs2');
});

test('401: wrong token → auth error', async () => {
  await assert.rejects(
    rpc({ port: fake.port, token: 'wrong' }, 'state'),
    (e) => e instanceof GamectlError && e.code === 'auth' && e.status === 401 && e.exitCode === 4,
  );
});

test('400: unknown method and handler errors', async () => {
  const b = await connect('cs2', { dir });
  await assert.rejects(b.call('nope'), (e) => e instanceof GamectlError && e.code === 'error' && e.status === 400 && /unknown method nope/.test(e.message));
  await assert.rejects(b.load('Missing'), /no save named Missing/);
});

test('504 executed:false → timeout, "did not run"', async () => {
  const b = await connect('cs2', { dir });
  await assert.rejects(b.call('test.block', {}, { timeout: 0.2 }), (e) =>
    e instanceof GamectlError && e.code === 'timeout' && e.status === 504 && e.executed === false && /did not run/.test(e.message) && e.exitCode === 5);
});

test('504 executed:"unknown" → timeout, "may have partly run"', async () => {
  const b = await connect('cs2', { dir });
  await assert.rejects(b.call('test.sleep', { ms: 2000 }, { timeout: 0.2 }), (e) =>
    e instanceof GamectlError && e.code === 'timeout' && e.executed === 'unknown' && /partly run/.test(e.message));
});

test('client-side timeout when the bridge never answers', async () => {
  // test.silent never answers; the client gives up after timeout_s + grace = 0.2 s
  const t0 = Date.now();
  await assert.rejects(rpc({ port: fake.port, token: fake.token }, 'test.silent', {}, { timeout: 0.1, graceS: 0.1 }), (e) =>
    e instanceof GamectlError && e.code === 'timeout' && e.status === undefined && e.executed === 'unknown');
  assert.ok(Date.now() - t0 < 1500);
});

test('unreachable port → not_running', async () => {
  const d = tmpDir();
  const tmp = await startFakeBridge({ game: 'cs1', dir: d });
  const port = tmp.port;
  await tmp.close();
  await assert.rejects(rpc({ port, token: 'x' }, 'state'), (e) => e instanceof GamectlError && e.code === 'not_running');
  await assert.rejects(connect('cs1', { dir: d }), (e) => e instanceof GamectlError && e.code === 'not_running');
});

test('sugar: wait, screenshot, exec, saves, load, menu, pause/resume', async () => {
  const b = await connect('cs2', { dir });
  const w = await b.wait(3);
  assert.ok(w.frames >= 3);
  const shot = path.join(tmpDir(), 'sub', 'a.png');
  const s = await b.screenshot(shot);
  assert.equal(s.path, shot);
  assert.ok(fs.existsSync(shot));
  assert.throws(() => b.screenshot('rel.png'), /must be absolute/);
  const dll = path.join(tmpDir(), 'x.dll');
  fs.writeFileSync(dll, '');
  const e = await b.exec(dll, { type: 'T', args: { a: 1 }, phase: 'tool' });
  assert.deepEqual(e.echo, { dll, type: 'T', args: { a: 1 }, phase: 'tool' });
  assert.throws(() => b.exec(path.join(dir, 'missing.dll')), /dll not found/);
  assert.equal((await b.saves(1)).saves.length, 1);
  const l = await b.load('Alpha');
  assert.equal(l.loaded, 'Alpha');
  assert.equal((await b.state()).city_loaded, true);
  assert.deepEqual(await b.menu(), { menu: true });
  assert.equal((await b.pause()).paused, true);
  assert.equal((await b.resume()).paused, false);
  assert.equal((await b.speed(3)).speed, 3);
});
