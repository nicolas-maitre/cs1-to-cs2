import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { startFakeBridge } from './fake-bridge.js';
import { cli, tmpDir, DEAD_PID } from './helpers.js';
import { parseArgs } from '../lib/cli.js';
import { status } from '../lib/lifecycle.js';

/** @type {import('./fake-bridge.js').FakeBridge} */
let fake;
let dir = '';
let root = '';
/** @type {Record<string, string | undefined>} */
const saved = {};

before(async () => {
  dir = tmpDir();
  root = tmpDir('gamectl-root-');
  for (const k of ['GAMECTL_BRIDGE_DIR', 'GAMECTL_REPO_ROOT']) saved[k] = process.env[k];
  process.env.GAMECTL_BRIDGE_DIR = dir;
  process.env.GAMECTL_REPO_ROOT = root;
  fake = await startFakeBridge({ game: 'cs2', dir });
});
after(async () => {
  await fake.close();
  for (const [k, v] of Object.entries(saved)) {
    if (v === undefined) delete process.env[k];
    else process.env[k] = v;
  }
});

/** Body of the last rpc request the fake bridge received. */
const lastRpc = () => fake.requests.filter((r) => r.url === '/rpc').at(-1)?.body;

test('parseArgs: flags, =values, negative numbers stay positional', () => {
  const { pos, opt } = parseArgs(['cs2', 'camera', '-120.5', '30', '--json', '--y=5', '--timeout', '9']);
  assert.deepEqual(pos, ['cs2', 'camera', '-120.5', '30']);
  assert.deepEqual(opt, { json: true, y: '5', timeout: '9' });
  assert.throws(() => parseArgs(['--bogus']), /unknown option/);
  assert.throws(() => parseArgs(['--timeout']), /needs a value/);
});

test('usage errors exit 2', async () => {
  assert.equal((await cli([])).code, 2);
  assert.equal((await cli(['cs3', 'status'])).code, 2);
  assert.equal((await cli(['cs2', 'frobnicate'])).code, 2);
  assert.equal((await cli(['cs2', 'camera', '1'])).code, 2);
  assert.equal((await cli(['cs2', 'call', 'state', '{bad'])).code, 2);
  const r = await cli(['cs2', 'wait', 'abc', '--json']);
  assert.equal(r.code, 2);
  assert.equal(r.json.ok, false);
  assert.equal(r.json.code, 'usage');
  assert.equal((await cli(['--help'])).code, 0);
});

test('call: raw rpc with params and timeout', async () => {
  const r = await cli(['cs2', 'call', 'camera.set', '{"x":5,"z":-6}', '--timeout', '12', '--json']);
  assert.equal(r.code, 0, r.err);
  assert.equal(r.json.ok, true);
  assert.equal(r.json.result.x, 5);
  assert.deepEqual(lastRpc(), { method: 'camera.set', params: { x: 5, z: -6 }, timeout_s: 12 });
  const h = await cli(['cs2', 'call', 'ping']);
  assert.equal(JSON.parse(h.out).mode, 'menu'); // raw call prints the result as JSON
  assert.match((await cli(['cs2', 'ping'])).out, /^mode=menu /); // sugar prints a summary
});

test('call: bridge 400 → exit 1, 504 → exit 5 with executed flag', async () => {
  const r = await cli(['cs2', 'call', 'nope', '--json']);
  assert.equal(r.code, 1);
  assert.equal(r.json.status, 400);
  const t = await cli(['cs2', 'call', 'test.block', '--timeout', '0.2', '--json']);
  assert.equal(t.code, 5);
  assert.equal(t.json.code, 'timeout');
  assert.equal(t.json.executed, false);
  const u = await cli(['cs2', 'call', 'test.sleep', '{"ms":1500}', '--timeout', '0.2']);
  assert.equal(u.code, 5);
  assert.match(u.err, /partly run/);
});

test('not running → exit 3; wrong token → exit 4', async () => {
  const r = await cli(['cs1', 'state', '--json']);
  assert.equal(r.code, 3);
  assert.equal(r.json.code, 'not_running');
  fs.writeFileSync(path.join(dir, 'cs1.json'), JSON.stringify({ game: 'cs1', port: 1, token: 't', pid: DEAD_PID }));
  assert.match((await cli(['cs1', 'state'])).err, /stale/);
  const other = await startFakeBridge({ game: 'cs1', dir, writeDiscovery: false });
  try {
    fs.writeFileSync(path.join(dir, 'cs1.json'), JSON.stringify({ game: 'cs1', port: other.port, token: 'wrong', pid: process.pid }));
    const a = await cli(['cs1', 'state', '--json']);
    assert.equal(a.code, 4);
    assert.equal(a.json.status, 401);
  } finally {
    await other.close();
    fs.rmSync(path.join(dir, 'cs1.json'));
  }
});

test('sugar: state, saves, load, menu, pause/resume/speed, wait, methods', async () => {
  assert.match((await cli(['cs2', 'state'])).out, /frame=\d+/);
  const s = await cli(['cs2', 'saves', '--limit', '1', '--json']);
  assert.equal(s.json.result.saves.length, 1);
  assert.deepEqual(lastRpc().params, { limit: 1 });
  assert.match((await cli(['cs2', 'saves'])).out, /Alpha {2}\(Alpha City\)/);
  const l = await cli(['cs2', 'load', 'Beta']);
  assert.equal(l.out, 'loaded "Beta" (city Beta City)');
  assert.equal(lastRpc().timeout_s, 900);
  assert.equal((await cli(['cs2', 'menu'])).out, 'at main menu');
  assert.equal((await cli(['cs2', 'pause'])).out, 'paused');
  assert.deepEqual(lastRpc(), { method: 'sim.set', params: { paused: true }, timeout_s: 60 });
  assert.match((await cli(['cs2', 'resume'])).out, /^running/);
  assert.match((await cli(['cs2', 'speed', '2'])).out, /speed 2/);
  const w = await cli(['cs2', 'wait', '4', '--json']);
  assert.ok(w.json.result.frames >= 4);
  assert.deepEqual(lastRpc().params, { frames: 4 });
  assert.match((await cli(['cs2', 'methods'])).out, /camera\.set \*/);
});

test('camera: get, set x z, set all five with negatives, --y', async () => {
  const g = await cli(['cs2', 'camera']);
  assert.match(g.out, /^x=.* distance=.* pitch=/);
  assert.equal(lastRpc().method, 'camera.get');
  await cli(['cs2', 'camera', '-100', '250']);
  assert.deepEqual(lastRpc(), { method: 'camera.set', params: { x: -100, z: 250 }, timeout_s: 60 });
  const r = await cli(['cs2', 'camera', '1', '2', '800', '-45', '30', '--y', '10', '--smooth', '--json']);
  assert.deepEqual(lastRpc().params, { x: 1, z: 2, distance: 800, yaw: -45, pitch: 30, y: 10, instant: false });
  assert.equal(r.json.result.yaw, -45);
});

test('screenshot: default path under <repo>/.gamectl/shots, relative path against cwd', async () => {
  const d = await cli(['cs2', 'screenshot', '--json']);
  assert.equal(d.code, 0, d.err);
  const p = d.json.result.path;
  assert.equal(path.dirname(p), path.join(root, '.gamectl', 'shots'));
  assert.match(path.basename(p), /^cs2-\d{8}-\d{6}-\d{3}\.png$/);
  assert.ok(fs.existsSync(p));
  const cwd = tmpDir();
  const r = await cli(['cs2', 'screenshot', 'shots/one', '--supersize', '2'], { cwd });
  assert.equal(r.out, path.join(cwd, 'shots', 'one.png'));
  assert.deepEqual(lastRpc().params, { path: path.join(cwd, 'shots', 'one.png'), supersize: 2 });
  assert.ok(fs.existsSync(path.join(cwd, 'shots', 'one.png')));
});

test('exec: dll resolved to absolute, options passed, --phase checked', async () => {
  const cwd = tmpDir();
  fs.mkdirSync(path.join(cwd, 'bin'));
  fs.writeFileSync(path.join(cwd, 'bin', 'Spike.dll'), '');
  const r = await cli(['cs2', 'exec', 'bin/Spike.dll', '--type', 'Spike.Entry', '--method', 'Go', '--args', '{"n":3}', '--phase', 'tool', '--json'], { cwd });
  assert.equal(r.code, 0, r.err);
  assert.deepEqual(lastRpc().params, { dll: path.join(cwd, 'bin', 'Spike.dll'), type: 'Spike.Entry', method: 'Go', args: { n: 3 }, phase: 'tool' });
  assert.equal((await cli(['cs2', 'exec', 'bin/Spike.dll', '--phase', 'sideways'], { cwd })).code, 2);
  assert.equal((await cli(['cs2', 'exec', 'bin/Missing.dll'], { cwd })).code, 2);
  assert.equal((await cli(['cs2', 'exec', 'bin/Spike.dll', '--args', 'nope'], { cwd })).code, 2);
});

test('status: running + ready → exit 0; stale → not running, exit 3', async () => {
  const r = await cli(['cs2', 'status', '--json']);
  assert.equal(r.code, 0, r.out);
  assert.equal(r.json.result.ready, true);
  assert.equal(r.json.result.bridge.port, fake.port);
  assert.equal(r.json.result.status.game, 'cs2');
  // via the module with no game processes, so a real Cities.exe on this machine (tasklist) can't interfere
  fs.writeFileSync(path.join(dir, 'cs1.json'), JSON.stringify({ game: 'cs1', port: 1, token: 't', pid: DEAD_PID }));
  try {
    const s = await status('cs1', { deps: { listPids: async () => [] } });
    assert.equal(s.running, false);
    assert.equal(s.ready, false);
    assert.equal(s.discovery.stale, true);
  } finally {
    fs.rmSync(path.join(dir, 'cs1.json'));
  }
});
