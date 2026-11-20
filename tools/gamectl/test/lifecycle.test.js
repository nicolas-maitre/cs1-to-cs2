// start / stop against a fake game process (test/fake-bridge.js run as a script). Never touches the real games:
// Steam and the process list are injected, and the "exe" is node running the fake.
import { test, after } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { startFakeBridge } from './fake-bridge.js';
import { DEAD_PID, FAKE_BRIDGE, killQuietly, tmpDir } from './helpers.js';
import { start, stop } from '../lib/lifecycle.js';
import { GamectlError } from '../lib/errors.js';
import { isAlive } from '../lib/proc.js';

/** @type {number[]} */
const spawned = [];
after(() => { for (const p of spawned) killQuietly(p); });

/**
 * A sandbox: temp bridge dir + marker log that already holds the ready marker from a "previous run",
 * plus a stale discovery file from a dead process.
 * @param {string[]} fakeArgs  extra args for the fake game
 * @param {{steam?: boolean, game?: 'cs1' | 'cs2'}} [o]
 */
function sandbox(fakeArgs = [], o = {}) {
  const game = o.game ?? 'cs2';
  const d = tmpDir();
  const dir = path.join(d, 'bridge');
  fs.mkdirSync(dir);
  const log = path.join(d, 'SceneFlow.log');
  const marker = game === 'cs2' ? 'MainMenu reached' : 'Scene [MainMenu] loaded';
  fs.writeFileSync(log, `previous run line\n${'old noise\n'.repeat(50)}${marker}\n`);
  fs.writeFileSync(path.join(dir, `${game}.json`), JSON.stringify({ game, port: 1, token: 'old', pid: DEAD_PID }));
  const opts = {
    config: {
      exe: process.execPath,
      installDir: d,
      args: [FAKE_BRIDGE, '--game', game, '--dir', dir, '--log', log, ...fakeArgs],
      markerFile: log,
      bridgeDir: dir,
      image: 'fake-game-not-running.exe',
    },
    deps: {
      isSteamRunning: async () => o.steam ?? true,
      listPids: async () => [],
    },
  };
  return { d, dir, log, opts, game };
}

/** @param {string} log */
const envOf = (log) => JSON.parse(fs.readFileSync(`${log}.env.json`, 'utf8'));

test('start: refuses without Steam', async () => {
  const s = sandbox([], { steam: false });
  await assert.rejects(start('cs2', { ...s.opts, timeout: 5 }), (e) => e instanceof GamectlError && e.code === 'lifecycle' && /Steam is not running/.test(e.message));
  assert.ok(!fs.existsSync(`${s.log}.env.json`), 'nothing spawned');
});

test('start: an old ready marker does not count; waits for the new one, then the new pid\'s bridge', async () => {
  // the bridge comes up at once, the marker only after 1.2 s
  const s = sandbox(['--marker-delay', '1200']);
  const r = await start('cs2', { ...s.opts, flags: '-continuelastsave "-x y"', timeout: 20 });
  const doneAt = Date.now();
  spawned.push(r.pid);
  const env = envOf(s.log);
  assert.equal(r.pid, env.pid, 'bridge pid is the spawned process');
  assert.equal(r.spawned, true);
  assert.equal(r.reused, false);
  const markerAt = Number(fs.readFileSync(`${s.log}.marker-at`, 'utf8'));
  assert.ok(doneAt >= markerAt, 'start returned only after the new marker was written');
  assert.equal(env.SteamAppId, '949230');
  assert.equal(env.SteamGameId, '949230');
  assert.deepEqual(env.argv.slice(-2), ['-continuelastsave', '-x y']);
  // and the module's bridge works
  assert.equal((await r.bridge.state()).game, 'cs2');
  await stop('cs2', { ...s.opts, deps: { ...s.opts.deps } });
});

test('start: times out if only the previous run\'s marker is in the log', async () => {
  const s = sandbox(['--no-marker']);
  await assert.rejects(start('cs2', { ...s.opts, timeout: 2 }), (e) =>
    e instanceof GamectlError && e.code === 'lifecycle' && /ready marker .* not yet written/.test(e.message));
  spawned.push(envOf(s.log).pid);
});

test('start: a log recreated/truncated by the new run is read from the start (CS1 marker)', async () => {
  const s = sandbox(['--truncate', '--marker-delay', '300'], { game: 'cs1' });
  const r = await start('cs1', { ...s.opts, timeout: 20 });
  spawned.push(r.pid);
  assert.equal(r.pid, envOf(s.log).pid);
  assert.equal(envOf(s.log).SteamAppId, '255710');
});

test('start: waits for a discovery file of the new pid (stale file from an older run is ignored)', async () => {
  const s = sandbox(['--bridge-delay', '800']);
  const r = await start('cs2', { ...s.opts, timeout: 20 });
  spawned.push(r.pid);
  assert.notEqual(r.pid, DEAD_PID);
  assert.equal(r.pid, envOf(s.log).pid);
});

test('start: game exits during startup → lifecycle error', async () => {
  const s = sandbox(['--no-marker', '--no-bridge', '--exit-now']);
  await assert.rejects(start('cs2', { ...s.opts, timeout: 20, relaunchGraceS: 0.5 }), (e) =>
    e instanceof GamectlError && e.code === 'lifecycle' && /exited during startup/.test(e.message));
});

test('start: reuses a running bridge and loads a save', async () => {
  const s = sandbox();
  const fake = await startFakeBridge({ game: 'cs2', dir: s.dir });
  try {
    const r = await start('cs2', { ...s.opts, load: 'Alpha', timeout: 5 });
    assert.equal(r.reused, true);
    assert.equal(r.spawned, false);
    assert.equal(r.pid, process.pid);
    assert.equal(/** @type {any} */ (r.loaded).loaded, 'Alpha');
    assert.equal(/** @type {any} */ (r.loaded).state.city_loaded, true);
    assert.ok(!fs.existsSync(`${s.log}.env.json`), 'nothing spawned');
  } finally {
    await fake.close();
  }
});

test('start: game already running without a bridge → waits for it, then fails clearly', async () => {
  const s = sandbox();
  const opts = { ...s.opts, deps: { ...s.opts.deps, listPids: async () => [process.pid] } };
  await assert.rejects(start('cs2', { ...opts, timeout: 1 }), (e) => e instanceof GamectlError && e.code === 'lifecycle' && /not ready in time/.test(e.message));
});

test('stop: quit → process exits', async () => {
  const s = sandbox();
  const r = await start('cs2', { ...s.opts, timeout: 20 });
  spawned.push(r.pid);
  const st = await stop('cs2', { ...s.opts, wait: 10 });
  assert.equal(st.method, 'quit');
  assert.equal(isAlive(r.pid), false);
});

test('stop: quit ignored → killed after --wait', async () => {
  const s = sandbox(['--ignore-quit']);
  const r = await start('cs2', { ...s.opts, timeout: 20 });
  spawned.push(r.pid);
  const st = await stop('cs2', { ...s.opts, wait: 1 });
  assert.equal(st.method, 'kill');
  assert.match(String(st.note), /still running after 1 s/);
  assert.equal(isAlive(r.pid), false);
});

test('stop --force kills right away; not running → nothing to do', async () => {
  const s = sandbox();
  const r = await start('cs2', { ...s.opts, timeout: 20 });
  spawned.push(r.pid);
  const st = await stop('cs2', { ...s.opts, force: true });
  assert.equal(st.method, 'kill');
  assert.equal(isAlive(r.pid), false);
  const again = await stop('cs2', s.opts);
  assert.equal(again.was_running, false);
});
