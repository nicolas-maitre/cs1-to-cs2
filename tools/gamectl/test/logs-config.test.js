import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { cli, tmpDir, withEnv } from './helpers.js';
import { LogCursor, followFiles, snapshot, tailFile } from '../lib/logs.js';
import { gameConfig, installDir, readLocalProps, resolveShotPath } from '../lib/config.js';
import { splitFlags } from '../lib/lifecycle.js';

test('tailFile: last n lines, grep, missing file, CRLF', () => {
  const f = path.join(tmpDir(), 'a.log');
  fs.writeFileSync(f, Array.from({ length: 100 }, (_, i) => `line ${i}${i % 10 === 0 ? ' ERROR' : ''}`).join('\r\n') + '\r\n');
  assert.deepEqual(tailFile(f, { lines: 3 }), ['line 97', 'line 98', 'line 99']);
  assert.deepEqual(tailFile(f, { lines: 2, grep: /error/i }), ['line 80 ERROR', 'line 90 ERROR']);
  assert.deepEqual(tailFile(path.join(tmpDir(), 'none.log')), []);
});

test('tailFile: reads backwards across chunk boundaries on big files', () => {
  const f = path.join(tmpDir(), 'big.log');
  const line = 'x'.repeat(1000);
  fs.writeFileSync(f, `FIRST MATCH\n${`${line}\n`.repeat(3000)}last\n`); // ~3 MB
  assert.deepEqual(tailFile(f, { lines: 1 }), ['last']);
  assert.deepEqual(tailFile(f, { lines: 5, grep: /MATCH/ }), ['FIRST MATCH']);
});

test('LogCursor: only content after the snapshot counts', () => {
  const f = path.join(tmpDir(), 'm.log');
  fs.writeFileSync(f, 'old run\nMainMenu reached\n');
  const c = new LogCursor(f, snapshot(f));
  assert.deepEqual(c.read(), []);
  fs.appendFileSync(f, 'new run\nhalf');
  assert.deepEqual(c.read(), ['new run']);
  assert.equal(c.pending(), 'half');
  fs.appendFileSync(f, ' line\n');
  assert.deepEqual(c.read(), ['half line']);
});

test('LogCursor: truncated / recreated file is read from the start', () => {
  const f = path.join(tmpDir(), 'm.log');
  fs.writeFileSync(f, 'a long old log line\nMainMenu reached\n');
  const c = new LogCursor(f, snapshot(f));
  fs.writeFileSync(f, 'new\n'); // truncate in place (smaller)
  assert.deepEqual(c.read(), ['new']);
  const g = path.join(tmpDir(), 'n.log');
  const c2 = new LogCursor(g, snapshot(g)); // file did not exist yet
  fs.writeFileSync(g, 'MainMenu reached\n');
  assert.deepEqual(c2.read(), ['MainMenu reached']);
});

test('followFiles: emits appended lines until aborted', async () => {
  const f = path.join(tmpDir(), 'f.log');
  fs.writeFileSync(f, 'before\n');
  /** @type {string[]} */
  const got = [];
  const ac = new AbortController();
  const p = followFiles([f], (_file, l) => { got.push(l); if (got.length === 2) ac.abort(); }, { signal: ac.signal, intervalMs: 20 });
  await new Promise((r) => setTimeout(r, 50));
  fs.appendFileSync(f, 'one\ntwo\n');
  await p;
  assert.deepEqual(got, ['one', 'two']);
});

test('CLI log: --file, --lines, --grep, --json, --follow', async () => {
  const d = tmpDir();
  const f = path.join(d, 'output_log.txt');
  fs.writeFileSync(f, 'a\nb [bridge] up\nc\n');
  const r = await cli(['cs1', 'log', '--file', f, '--lines', '2']);
  assert.equal(r.out, 'b [bridge] up\nc');
  const j = await cli(['cs1', 'log', '--file', f, '--grep', 'BRIDGE', '--json']);
  assert.deepEqual(j.json.result.files[0].lines, ['b [bridge] up']);
  const ac = new AbortController();
  /** @type {Promise<any>} */
  const p = cli(['cs1', 'log', '--file', f, '--lines', '1', '--follow'], { signal: ac.signal });
  await new Promise((r) => setTimeout(r, 100));
  fs.appendFileSync(f, 'd\n');
  await new Promise((r) => setTimeout(r, 600));
  ac.abort();
  const fr = await p;
  assert.equal(fr.code, 0);
  assert.equal(fr.out, 'c\nd');
});

test('CLI log: default CS2 files are Player.log + bridge-relevant Logs/*.log', async () => {
  const data = tmpDir();
  fs.mkdirSync(path.join(data, 'Logs'));
  fs.writeFileSync(path.join(data, 'Player.log'), 'player\n');
  fs.writeFileSync(path.join(data, 'Logs', 'SceneFlow.log'), 'MainMenu reached\n');
  fs.writeFileSync(path.join(data, 'Logs', 'Cs2Bridge.log'), 'listening\n');
  fs.writeFileSync(path.join(data, 'Logs', 'Rendering.log'), 'noise\n');
  await withEnv({ GAMECTL_CS2_DATA_DIR: data }, async () => {
    const r = await cli(['cs2', 'log', '--json']);
    assert.deepEqual(r.json.result.files.map((/** @type {any} */ f) => path.basename(f.file)), ['Player.log', 'Cs2Bridge.log', 'SceneFlow.log']);
    const s = await cli(['cs2', 'log', '--file', 'SceneFlow.log']);
    assert.equal(s.out, 'MainMenu reached');
    const p = await cli(['cs2', 'log', '--file', 'Player.log']);
    assert.equal(p.out, 'player');
  });
});

test('local.props → install folders; env overrides', async () => {
  const d = tmpDir();
  const props = path.join(d, 'local.props');
  fs.writeFileSync(props, `<Project><!-- <CS1Managed>commented</CS1Managed> -->
  <PropertyGroup>
    <CS1Managed>E:\\Steam\\common\\Cities_Skylines\\Cities_Data\\Managed</CS1Managed>
    <CS2Managed>E:\\Steam\\common\\Cities Skylines II\\Cities2_Data\\Managed</CS2Managed>
  </PropertyGroup></Project>`);
  const p = readLocalProps(props);
  await withEnv({ CS1_DIR: undefined, CS2_DIR: undefined }, async () => {
    assert.equal(installDir('cs1', p), path.resolve('E:\\Steam\\common\\Cities_Skylines'));
    assert.equal(installDir('cs2', p), path.resolve('E:\\Steam\\common\\Cities Skylines II'));
  });
  await withEnv({ CS2_DIR: 'D:\\Games\\CS2' }, async () => {
    assert.equal(installDir('cs2', p), path.resolve('D:\\Games\\CS2'));
    const cfg = gameConfig('cs2');
    assert.equal(cfg.exe, path.join(path.resolve('D:\\Games\\CS2'), 'Cities2.exe'));
    assert.deepEqual(cfg.args, ['-developerMode']);
    assert.equal(cfg.appId, '949230');
  });
  await withEnv({ CS1_DIR: 'D:\\Games\\CS1' }, async () => {
    const cfg = gameConfig('cs1');
    assert.equal(cfg.appId, '255710');
    assert.equal(cfg.markerFile, path.join(path.resolve('D:\\Games\\CS1'), 'Cities_Data', 'output_log.txt'));
    assert.ok(cfg.markerRe.test('Scene [MainMenu] loaded'));
  });
  assert.deepEqual(readLocalProps(path.join(d, 'missing.props')), {});
});

test('resolveShotPath', () => {
  const now = new Date(2026, 10, 20, 18, 5, 9, 7);
  const root = tmpDir();
  assert.equal(resolveShotPath('cs1', undefined, { root, now }), path.join(root, '.gamectl', 'shots', 'cs1-20261120-180509-007.png'));
  const cwd = tmpDir();
  assert.equal(resolveShotPath('cs2', 'x.png', { cwd }), path.join(cwd, 'x.png'));
  assert.equal(resolveShotPath('cs2', 'x', { cwd }), path.join(cwd, 'x.png'));
  assert.equal(resolveShotPath('cs2', 'dir/', { cwd, now }), path.join(cwd, 'dir', 'cs2-20261120-180509-007.png'));
  assert.equal(resolveShotPath('cs2', cwd, { now }), path.join(cwd, 'cs2-20261120-180509-007.png')); // existing folder
});

test('splitFlags keeps quoted args together', () => {
  assert.deepEqual(splitFlags('-continuelastsave "-a b" -noWorkshop'), ['-continuelastsave', '-a b', '-noWorkshop']);
  assert.deepEqual(splitFlags(undefined), []);
});
