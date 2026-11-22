#!/usr/bin/env node
// End-to-end terrain check in CS2: start the game (or reuse it) → new city → apply terrain → save as cs1conv-* →
// reload that save → sample again (does the terrain persist?) → stop the game. Holds the shared CS2 lock throughout.
//
//   node terrain/run-newcity.mjs --terrain <dir> --cs1x <file> [--map "<map name or id>"] [--name cs1conv-terrain-test]
//                                [--shots dir] [--keep-running]
//
// The terrain dir comes from terrain/convert.mjs; build terrain/cs2-apply first.
import { readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { start, stop, connect } from '../tools/gamectl/lib/index.js';
import { main as applyMain } from './apply.mjs';
import { holdCs2, freeCs2 } from './lib/cs2lock.mjs';

const LOCK = join(process.env.LOCALAPPDATA || '', 'cs1-to-cs2', 'cs2.lock');
const SESSION = 'terrain worker (terrain/run-newcity.mjs)', OWNER = SESSION;

function parseArgs(argv) {
  const a = { name: 'cs1conv-terrain-test', map: null, keep: false };
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i], v = () => argv[++i];
    switch (k) {
      case '--terrain': a.terrain = resolve(v()); break;
      case '--cs1x': a.cs1x = resolve(v()); break;
      case '--map': a.map = v(); break;
      case '--name': a.name = v(); break;
      case '--shots': a.shots = resolve(v()); break;
      case '--keep-running': a.keep = true; break;
      default: throw new Error('unknown argument ' + k);
    }
  }
  if (!a.terrain || !a.cs1x) throw new Error('--terrain and --cs1x are required');
  if (!a.name.startsWith('cs1conv-')) throw new Error('save name must start with cs1conv-');
  return a;
}

async function takeLock() {
  await holdCs2(OWNER, SESSION);
}

const log = (...m) => console.error('[terrain]', ...m);

async function run(a) {
  await takeLock();
  const { bridge } = await start('cs2', { timeout: 400 });
  const maps = (await bridge.call('maps.list', { limit: 200 })).maps.filter(m => m.playable !== false);
  const want = a.map ?? 'Sunshine Peninsula'; // DECISIONS-terrain.md #16
  const map = maps.find(m => m.id === want || m.name === want);
  if (!map) throw new Error('map not found: ' + want + '; available: ' + maps.map(m => m.name).join(', '));
  log('new city on map', map.name);
  const created = await bridge.call('game.new', { map: map.id, name: a.name, options: { unlockAll: true, unlimitedMoney: true, unlockMapTiles: true } }, { timeout: 900 });
  await bridge.pause();

  const shots = a.shots ?? join(a.terrain, 'shots');
  await applyMain(['--terrain', a.terrain, '--cs1x', a.cs1x, '--shots', shots, '--report', join(a.terrain, 'apply-report-newcity.json')]);
  const saved = await bridge.call('save.write', { name: a.name }, { timeout: 300 });
  log('saved', JSON.stringify(saved));

  const loaded = await bridge.load(a.name, { timeout: 900 });
  await bridge.pause();
  log('reloaded', JSON.stringify(loaded));
  await applyMain(['--terrain', a.terrain, '--cs1x', a.cs1x, '--no-apply', '--shots', join(shots, 'reloaded'), '--report', join(a.terrain, 'apply-report-reloaded.json')]);
  const info = await bridge.exec(join(import.meta.dirname, 'cs2-apply', 'bin', 'Release', 'net48', 'Cs1ConvTerrain.dll'), { type: 'Cs1ConvTerrain.Ops', args: { op: 'info' } });
  const summary = {
    map: map.name, created, saved, loaded,
    afterReload: { heightScaleOffset: info.heightScaleOffset, worldHeightmap: info.worldHeightmap, seaLevel: info.water.seaLevel, waterSources: info.water.sources.length },
    newCity: JSON.parse(readFileSync(join(a.terrain, 'apply-report-newcity.json'), 'utf8')).verification,
    reloaded: JSON.parse(readFileSync(join(a.terrain, 'apply-report-reloaded.json'), 'utf8')).verification,
  };
  writeFileSync(join(a.terrain, 'run-newcity.json'), JSON.stringify(summary, null, 2) + '\n');
  console.log(JSON.stringify(summary, null, 2));
}

const a = parseArgs(process.argv.slice(2));
try {
  await run(a);
} catch (e) {
  console.error('error: ' + (e.stack || e.message));
  process.exitCode = 1;
} finally {
  if (!a.keep) {
    try { await stop('cs2', { wait: 90 }); } catch (e) { console.error('stop: ' + e.message); }
    try { await freeCs2(OWNER); } catch { /* ignore */ }
  }
}
