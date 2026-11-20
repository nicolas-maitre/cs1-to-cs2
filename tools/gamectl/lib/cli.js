// The gamectl command line. `main()` returns the exit code instead of exiting, so tests run it in-process.

import fs from 'node:fs';
import path from 'node:path';
import { EXIT_CODES, GamectlError } from './errors.js';
import { checkGame, gameConfig, resolveShotPath } from './config.js';
import { connect } from './bridge.js';
import { start, status, stop } from './lifecycle.js';
import { followFiles, tailFile } from './logs.js';

const BOOLEAN_FLAGS = new Set(['json', 'follow', 'force', 'help', 'quiet', 'smooth']);
const VALUE_FLAGS = new Set([
  'load', 'flags', 'timeout', 'load-timeout', 'lines', 'grep', 'type', 'method', 'args', 'phase', 'file', 'limit',
  'supersize', 'seconds', 'wait', 'y',
]);

export const USAGE = `gamectl — drive Cities: Skylines 1/2 through their bridge mods (tools/PROTOCOL.md)

usage: gamectl <cs1|cs2> <command> [args] [--json] [--quiet]

lifecycle
  start [--load <save>] [--flags "..."] [--timeout 300] [--load-timeout 900]
  stop [--force] [--wait 60]
  status
rpc
  call <method> [jsonParams] [--timeout s]
  state | methods | ping
  saves [--limit n]
  load <save> [--timeout 900]
  menu
  camera [x z [distance yaw pitch]] [--y n] [--smooth]   (no args = get)
  pause | resume | speed <n>
  wait <frames> [--seconds s]
  screenshot [file.png] [--supersize 1-4]      (default .gamectl/shots/<game>-<timestamp>.png)
  exec <dll> [--type T] [--method M] [--args json] [--phase main|tool]
logs
  log [--lines 50] [--grep regex] [--follow] [--file name|path]

exit codes: 0 ok, 1 error, 2 usage, 3 not running, 4 auth, 5 timeout, 6 start/stop failed`;

/**
 * @typedef {object} Io
 * @property {(s: string) => void} out    stdout line
 * @property {(s: string) => void} err    stderr line
 * @property {string} [cwd]
 * @property {AbortSignal} [signal]       ends `log --follow`
 */

/**
 * @param {string[]} argv
 * @returns {{pos: string[], opt: Record<string, string | true>}}
 */
export function parseArgs(argv) {
  /** @type {string[]} */
  const pos = [];
  /** @type {Record<string, string | true>} */
  const opt = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--') {
      pos.push(...argv.slice(i + 1));
      break;
    }
    if (a === '-h') { opt.help = true; continue; }
    if (!a.startsWith('--')) { pos.push(a); continue; } // includes negative numbers like -120.5
    const eq = a.indexOf('=');
    const name = eq >= 0 ? a.slice(2, eq) : a.slice(2);
    if (BOOLEAN_FLAGS.has(name)) {
      if (eq >= 0) throw new GamectlError('usage', `--${name} takes no value`);
      opt[name] = true;
    } else if (VALUE_FLAGS.has(name)) {
      if (eq >= 0) opt[name] = a.slice(eq + 1);
      else if (i + 1 < argv.length) opt[name] = argv[++i];
      else throw new GamectlError('usage', `--${name} needs a value`);
    } else {
      throw new GamectlError('usage', `unknown option --${name}`);
    }
  }
  return { pos, opt };
}

/** @param {string | true | undefined} v @param {string} name @returns {number | undefined} */
function num(v, name) {
  if (v === undefined) return undefined;
  const n = typeof v === 'string' && v.trim() !== '' ? Number(v) : NaN;
  if (!Number.isFinite(n)) throw new GamectlError('usage', `${name} must be a number, got "${v}"`);
  return n;
}

/** @param {string} text @param {string} what */
function json(text, what) {
  try {
    return JSON.parse(text);
  } catch (e) {
    throw new GamectlError('usage', `${what} is not valid JSON: ${/** @type {Error} */ (e).message}`);
  }
}

/** @param {any} v */
const pretty = (v) => (typeof v === 'string' ? v : JSON.stringify(v, null, 2));

/** @param {number | undefined} n */
const fmt = (n) => (typeof n === 'number' ? String(Math.round(n * 100) / 100) : String(n));

/** One-line summary of a state object. @param {any} s */
function stateLine(s) {
  if (!s) return 'no status yet';
  const parts = [`mode=${s.mode}`];
  if (s.loading) parts.push('loading');
  if (s.city_loaded) parts.push(`city="${s.city_name ?? ''}"`);
  if (s.save_name) parts.push(`save="${s.save_name}"`);
  if (s.paused != null) parts.push(s.paused ? 'paused' : `speed=${s.speed}`);
  parts.push(`frame=${s.frame}`);
  return parts.join(' ');
}

/** @param {any} c */
const cameraLine = (c) => ['x', 'y', 'z', 'distance', 'yaw', 'pitch'].map((k) => `${k}=${fmt(c?.[k])}`).join(' ');

/**
 * @param {string[]} pos @param {number} min @param {number} max @param {string} usage
 */
function arity(pos, min, max, usage) {
  if (pos.length < min || pos.length > max) throw new GamectlError('usage', `usage: gamectl <game> ${usage}`);
}

/**
 * Resolve `log --file`: absolute or ./relative paths against cwd, bare names against the game's log folders.
 * @param {import('./config.js').GameConfig} cfg @param {string} name @param {string} cwd
 */
function resolveLogFile(cfg, name, cwd) {
  if (path.isAbsolute(name) || /^\.\.?[\\/]/.test(name)) return path.resolve(cwd, name);
  /** @type {string[]} */
  const candidates = [];
  if (cfg.logDir) candidates.push(path.join(cfg.logDir, name), path.join(path.dirname(cfg.logDir), name));
  candidates.push(path.resolve(cwd, name));
  return candidates.find((f) => fs.existsSync(f)) ?? candidates[0];
}

/**
 * Run one command. Returns {result, text}: `result` is what --json prints, `text` the human output.
 * @param {import('./config.js').GameId} game @param {string} cmd @param {string[]} pos
 * @param {Record<string, string | true>} opt @param {Io} io
 * @returns {Promise<{result: any, text?: string | null} | null>}  null when the command streamed its own output
 */
async function run(game, cmd, pos, opt, io) {
  const cwd = io.cwd ?? process.cwd();
  const timeout = num(opt.timeout, '--timeout');
  const progress = opt.quiet || opt.json ? undefined : (/** @type {string} */ m) => io.err(m);

  switch (cmd) {
    case 'start': {
      arity(pos, 0, 0, 'start [--load <save>] [--flags "..."] [--timeout 300]');
      const r = await start(game, {
        load: typeof opt.load === 'string' ? opt.load : undefined,
        flags: typeof opt.flags === 'string' ? opt.flags : undefined,
        timeout,
        loadTimeout: num(opt['load-timeout'], '--load-timeout'),
        onProgress: progress,
      });
      const { bridge, ...result } = r;
      let text = `${game} ready: pid ${r.pid}, port ${r.port} (${r.spawned ? 'started' : 'reused'}, ${fmt(r.ready_s)} s)`;
      if (r.loaded) text += `\nloaded "${/** @type {any} */ (r.loaded).loaded}": ${stateLine(/** @type {any} */ (r.loaded).state)}`;
      return { result, text };
    }
    case 'stop': {
      arity(pos, 0, 0, 'stop [--force] [--wait 60]');
      const r = await stop(game, { force: !!opt.force, wait: num(opt.wait, '--wait'), onProgress: progress });
      return { result: r, text: r.was_running ? `${game} stopped (${r.method})` : `${game} was not running` };
    }
    case 'status': {
      arity(pos, 0, 0, 'status');
      const r = await status(game);
      let text;
      if (!r.running) text = `${game}: not running${r.discovery?.stale ? ` (stale discovery file, pid ${r.discovery.pid})` : ''}`;
      else if (r.bridge) text = `${game}: running pid ${r.pids.join(', ')}; bridge ${r.bridge.bridge_version} on port ${r.bridge.port}; ${stateLine(r.status)}`;
      else text = `${game}: running pid ${r.pids.join(', ')}; bridge not answering${r.error ? ` (${r.error})` : ''}`;
      return { result: r, text, exit: r.ready ? 0 : EXIT_CODES.not_running };
    }
    case 'log': {
      arity(pos, 0, 0, 'log [--lines 50] [--grep re] [--follow] [--file name]');
      const cfg = gameConfig(game);
      const files = typeof opt.file === 'string' ? [resolveLogFile(cfg, opt.file, cwd)] : cfg.logFiles();
      if (!files.length) throw new GamectlError('error', `no log files known for ${game} (install folder unknown?)`);
      let grep;
      try {
        grep = typeof opt.grep === 'string' ? new RegExp(opt.grep, 'i') : undefined;
      } catch (e) {
        throw new GamectlError('usage', `--grep: ${/** @type {Error} */ (e).message}`);
      }
      const lines = num(opt.lines, '--lines') ?? (opt.follow ? 10 : 50);
      const tails = files.map((file) => ({ file, exists: fs.existsSync(file), lines: tailFile(file, { lines, grep }) }));
      if (!opt.follow) {
        const text = tails
          .map((t) => (files.length > 1 ? `==> ${t.file}${t.exists ? '' : ' (missing)'} <==\n` : '') + t.lines.join('\n'))
          .join('\n\n');
        return { result: { files: tails }, text };
      }
      const many = files.length > 1;
      /** @param {string} file @param {string} line */
      const emit = (file, line) => {
        if (grep && !grep.test(line)) return;
        if (opt.json) io.out(JSON.stringify({ file, line }));
        else io.out(many ? `[${path.basename(file)}] ${line}` : line);
      };
      for (const t of tails) for (const l of t.lines) emit(t.file, l);
      await followFiles(files, emit, { signal: io.signal });
      return null;
    }
  }

  // everything below needs the bridge
  if (cmd === 'call') arity(pos, 1, 2, 'call <method> [jsonParams] [--timeout s]');
  const known = ['call', 'state', 'methods', 'ping', 'saves', 'load', 'menu', 'camera', 'pause', 'resume', 'speed', 'wait', 'screenshot', 'exec'];
  if (!known.includes(cmd)) throw new GamectlError('usage', `unknown command "${cmd}"\n\n${USAGE}`);
  const b = await connect(game);
  const t = timeout !== undefined ? { timeout } : {};

  switch (cmd) {
    case 'call': {
      const params = pos[1] !== undefined ? json(pos[1], 'jsonParams') : {};
      if (params === null || typeof params !== 'object' || Array.isArray(params)) throw new GamectlError('usage', 'jsonParams must be a JSON object');
      const r = await b.call(pos[0], params, t);
      return { result: r, text: pretty(r) };
    }
    case 'state':
    case 'ping': {
      arity(pos, 0, 0, cmd);
      const r = await b.call(cmd, {}, t);
      return { result: r, text: stateLine(r) };
    }
    case 'methods': {
      arity(pos, 0, 0, 'methods');
      const r = /** @type {any[]} */ (await b.methods());
      return { result: r, text: r.map((m) => `${m.name}${m.while_loading ? ' *' : ''}  ${m.help ?? ''}`).join('\n') + '\n(* = runs while loading)' };
    }
    case 'saves': {
      arity(pos, 0, 0, 'saves [--limit n]');
      const r = /** @type {any} */ (await b.saves(num(opt.limit, '--limit')));
      const text = (r?.saves ?? []).map((/** @type {any} */ s) => `${s.modified ?? ''}  ${s.name}${s.city && s.city !== s.name ? `  (${s.city})` : ''}${s.autosave ? '  [auto]' : ''}${s.cloud ? '  [cloud]' : ''}`).join('\n') || '(no saves)';
      return { result: r, text };
    }
    case 'load': {
      arity(pos, 1, 1, 'load <save> [--timeout 900]');
      const r = /** @type {any} */ (await b.load(pos[0], t));
      return { result: r, text: `loaded "${r?.loaded ?? pos[0]}"${r?.city ? ` (city ${r.city})` : ''}` };
    }
    case 'menu': {
      arity(pos, 0, 0, 'menu');
      const r = await b.menu(t);
      return { result: r, text: 'at main menu' };
    }
    case 'camera': {
      if (![0, 2, 5].includes(pos.length)) throw new GamectlError('usage', 'usage: gamectl <game> camera [x z [distance yaw pitch]] [--y n] [--smooth]');
      if (pos.length === 0 && opt.y === undefined) {
        const r = await b.cameraGet();
        return { result: r, text: cameraLine(r) };
      }
      /** @type {Record<string, any>} */
      const cam = {};
      const keys = ['x', 'z', 'distance', 'yaw', 'pitch'];
      pos.forEach((v, i) => { cam[keys[i]] = num(v, keys[i]); });
      if (opt.y !== undefined) cam.y = num(opt.y, '--y');
      if (opt.smooth) cam.instant = false;
      const r = await b.cameraSet(cam);
      return { result: r, text: cameraLine(r) };
    }
    case 'pause':
    case 'resume': {
      arity(pos, 0, 0, cmd);
      const r = /** @type {any} */ (cmd === 'pause' ? await b.pause() : await b.resume());
      return { result: r, text: r?.paused ? 'paused' : `running (speed ${r?.speed})` };
    }
    case 'speed': {
      arity(pos, 1, 1, 'speed <n>');
      const r = /** @type {any} */ (await b.speed(/** @type {number} */ (num(pos[0], 'speed'))));
      return { result: r, text: `${r?.paused ? 'paused, ' : ''}speed ${r?.speed}` };
    }
    case 'wait': {
      arity(pos, 0, 1, 'wait <frames> [--seconds s]');
      const frames = num(pos[0], 'frames');
      const seconds = num(opt.seconds, '--seconds');
      if (frames === undefined && seconds === undefined) throw new GamectlError('usage', 'usage: gamectl <game> wait <frames> [--seconds s]');
      const r = /** @type {any} */ (await b.wait(frames, seconds, t));
      return { result: r, text: `waited ${r?.frames} frames (${fmt(r?.seconds)} s)` };
    }
    case 'screenshot': {
      arity(pos, 0, 1, 'screenshot [file.png] [--supersize n]');
      const file = resolveShotPath(game, pos[0], { cwd });
      const r = /** @type {any} */ (await b.screenshot(file, { supersize: num(opt.supersize, '--supersize'), ...t }));
      return { result: r, text: r?.path ?? file };
    }
    case 'exec': {
      arity(pos, 1, 1, 'exec <dll> [--type T] [--method M] [--args json] [--phase tool]');
      const phase = opt.phase;
      if (phase !== undefined && phase !== 'main' && phase !== 'tool') throw new GamectlError('usage', '--phase must be main or tool');
      const r = await b.exec(path.resolve(cwd, pos[0]), {
        type: typeof opt.type === 'string' ? opt.type : undefined,
        method: typeof opt.method === 'string' ? opt.method : undefined,
        args: typeof opt.args === 'string' ? json(opt.args, '--args') : undefined,
        phase,
        ...t,
      });
      return { result: r, text: pretty(r) };
    }
  }
  return null;
}

/**
 * @param {string[]} argv  arguments after `gamectl`
 * @param {Io} [io]
 * @returns {Promise<number>} exit code
 */
export async function main(argv, io = { out: (s) => process.stdout.write(s + '\n'), err: (s) => process.stderr.write(s + '\n') }) {
  let asJson = argv.includes('--json');
  try {
    const { pos, opt } = parseArgs(argv);
    asJson = !!opt.json;
    if (opt.help || pos.length === 0 || pos[0] === 'help') {
      io.out(USAGE);
      return pos.length === 0 && !opt.help ? EXIT_CODES.usage : 0;
    }
    const game = checkGame(pos[0]);
    if (pos.length < 2) throw new GamectlError('usage', `missing command\n\n${USAGE}`);
    const r = /** @type {any} */ (await run(game, pos[1], pos.slice(2), opt, io));
    if (r) {
      if (asJson) io.out(JSON.stringify({ ok: true, result: r.result ?? null }));
      else if (r.text != null && r.text !== '') io.out(r.text);
      return r.exit ?? 0;
    }
    return 0;
  } catch (e) {
    const err = e instanceof GamectlError ? e : new GamectlError('error', /** @type {Error} */ (e)?.stack ?? String(e));
    if (asJson) io.out(JSON.stringify(err.toJSON()));
    else io.err(`gamectl: ${err.message}`);
    return err.exitCode;
  }
}
