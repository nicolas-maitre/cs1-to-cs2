// Bridge client: discovery file, GET /health, POST /rpc (see tools/PROTOCOL.md).
//
// Uses node:http rather than fetch(): undici's fetch adds `Sec-Fetch-Mode: cors`, which the bridge refuses (403) as a
// browser request, and it gives up on response headers after 300 s, shorter than a long save.load.

import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { GamectlError } from './errors.js';
import { bridgeDir, checkGame } from './config.js';
import { isAlive as defaultIsAlive } from './proc.js';

/**
 * @typedef {object} DiscoveryInfo
 * @property {string} game
 * @property {number} port
 * @property {string} token
 * @property {number} pid
 * @property {string} [bridge_version]
 * @property {number} [protocol]
 * @property {string} [started]
 * @property {string} file      the discovery file it was read from
 * @property {number} mtimeMs   the discovery file's mtime
 */

/** Extra seconds the client waits beyond `timeout_s` before giving up on the bridge itself. */
export const CLIENT_GRACE_S = 15;
const DEFAULT_RPC_TIMEOUT_S = 60;

/** @param {string} game @param {string} [dir] */
export function discoveryPath(game, dir = bridgeDir()) {
  return path.join(dir, `${checkGame(game)}.json`);
}

/**
 * Read `<dir>/<game>.json` without judging it. Returns null if there is no file.
 * @param {string} game
 * @param {{dir?: string}} [opts]
 * @returns {DiscoveryInfo | null}
 */
export function readDiscoveryRaw(game, opts = {}) {
  const file = discoveryPath(game, opts.dir);
  let text, st;
  try {
    st = fs.statSync(file);
    text = fs.readFileSync(file, 'utf8');
  } catch {
    return null;
  }
  let info;
  try {
    info = JSON.parse(text.replace(/^﻿/, ''));
  } catch (e) {
    throw new GamectlError('not_running', `discovery file ${file} is not valid JSON (${/** @type {Error} */ (e).message})`);
  }
  if (!info || typeof info.port !== 'number' || typeof info.token !== 'string' || typeof info.pid !== 'number') {
    throw new GamectlError('not_running', `discovery file ${file} lacks port/token/pid`);
  }
  if (info.game && info.game !== game) throw new GamectlError('not_running', `discovery file ${file} is for "${info.game}", not ${game}`);
  return { ...info, file, mtimeMs: st.mtimeMs };
}

/**
 * Read the discovery file and check its pid is alive (stale files survive a crash).
 * @param {string} game
 * @param {{dir?: string, isAlive?: (pid: number) => boolean}} [opts]
 * @returns {DiscoveryInfo}
 */
export function readDiscovery(game, opts = {}) {
  const info = readDiscoveryRaw(game, opts);
  const file = discoveryPath(game, opts.dir);
  if (!info) throw new GamectlError('not_running', `${game} bridge not running (no discovery file ${file})`);
  if (!(opts.isAlive ?? defaultIsAlive)(info.pid)) {
    throw new GamectlError('not_running', `${game} bridge not running (stale discovery file: pid ${info.pid} is gone)`);
  }
  return info;
}

/**
 * One HTTP request to the bridge. Resolves with {status, body} for any HTTP answer; rejects on network errors.
 * @param {{port: number, method: 'GET' | 'POST', path: string, headers?: Record<string, string>, body?: string, timeoutMs: number}} req
 * @returns {Promise<{status: number, body: any}>}
 */
export function httpJson(req) {
  return new Promise((resolve, reject) => {
    /** @type {Record<string, string | number>} */
    const headers = { Accept: 'application/json', ...(req.headers ?? {}) };
    if (req.body !== undefined) headers['Content-Length'] = Buffer.byteLength(req.body);
    const r = http.request(
      { host: '127.0.0.1', port: req.port, method: req.method, path: req.path, headers, agent: false },
      (res) => {
        /** @type {Buffer[]} */
        const chunks = [];
        res.on('data', (c) => chunks.push(c));
        res.on('error', (e) => { clearTimeout(timer); reject(e); });
        res.on('end', () => {
          clearTimeout(timer);
          const text = Buffer.concat(chunks).toString('utf8');
          let body;
          try {
            body = text ? JSON.parse(text) : null;
          } catch {
            body = { ok: false, error: `non-JSON response: ${text.slice(0, 200)}` };
          }
          resolve({ status: res.statusCode ?? 0, body });
        });
      },
    );
    const timer = setTimeout(() => {
      const e = /** @type {NodeJS.ErrnoException} */ (new Error(`no answer within ${req.timeoutMs} ms`));
      e.code = 'GAMECTL_CLIENT_TIMEOUT';
      r.destroy(e);
    }, req.timeoutMs);
    r.on('error', (e) => { clearTimeout(timer); reject(e); });
    if (req.body !== undefined) r.write(req.body);
    r.end();
  });
}

/**
 * GET /health (no token). Throws not_running if nothing answers.
 * @param {number} port
 * @param {{timeoutMs?: number}} [opts]
 */
export async function health(port, opts = {}) {
  let res;
  try {
    res = await httpJson({ port, method: 'GET', path: '/health', timeoutMs: opts.timeoutMs ?? 3000 });
  } catch (e) {
    throw new GamectlError('not_running', `bridge on port ${port} not answering /health (${/** @type {Error} */ (e).message})`, { cause: e });
  }
  if (res.status !== 200 || !res.body?.ok) {
    throw new GamectlError('error', `bridge /health returned ${res.status}: ${res.body?.error ?? JSON.stringify(res.body)}`, { status: res.status, body: res.body });
  }
  return res.body;
}

/**
 * POST /rpc. Returns `result` on 200, throws GamectlError otherwise.
 * @param {{port: number, token: string}} info
 * @param {string} method
 * @param {Record<string, unknown>} [params]
 * @param {{timeout?: number, graceS?: number}} [opts]  timeout in seconds, sent as timeout_s (bridge default 60, max 3600);
 *   graceS: extra seconds to wait for the HTTP answer beyond timeout_s (default CLIENT_GRACE_S)
 */
export async function rpc(info, method, params = {}, opts = {}) {
  const timeoutS = opts.timeout ?? DEFAULT_RPC_TIMEOUT_S;
  const graceS = opts.graceS ?? CLIENT_GRACE_S;
  if (!(timeoutS > 0)) throw new GamectlError('usage', `timeout must be > 0 seconds, got ${opts.timeout}`);
  const body = JSON.stringify({ method, params, timeout_s: timeoutS });
  let res;
  try {
    res = await httpJson({
      port: info.port,
      method: 'POST',
      path: '/rpc',
      headers: { Authorization: `Bearer ${info.token}`, 'Content-Type': 'application/json' },
      body,
      timeoutMs: (Math.min(timeoutS, 3600) + graceS) * 1000,
    });
  } catch (e) {
    const err = /** @type {NodeJS.ErrnoException} */ (e);
    if (err.code === 'GAMECTL_CLIENT_TIMEOUT') {
      throw new GamectlError('timeout', `${method}: the bridge did not answer within ${timeoutS}+${graceS} s (the command may still be running)`, { executed: 'unknown', cause: e });
    }
    throw new GamectlError('not_running', `${method}: bridge on port ${info.port} unreachable (${err.code ?? err.message})`, { cause: e });
  }
  const b = res.body ?? {};
  if (res.status === 200 && b.ok) return b.result;
  const msg = `${method}: ${b.error ?? `HTTP ${res.status}`}`;
  if (res.status === 401 || res.status === 403 || res.status === 415) {
    throw new GamectlError('auth', `${msg} (HTTP ${res.status})`, { status: res.status, body: b });
  }
  if (res.status === 504) {
    const executed = b.executed === false ? false : 'unknown';
    const hint = executed === false ? 'it did not run' : 'it may have partly run; check state before retrying';
    throw new GamectlError('timeout', `${msg} [${hint}]`, { status: 504, body: b, executed });
  }
  throw new GamectlError('error', msg, { status: res.status, body: b });
}

/** A connected bridge: raw `call` plus sugar for the common methods. */
export class Bridge {
  /** @param {DiscoveryInfo} info */
  constructor(info) {
    this.info = info;
    this.game = /** @type {import('./config.js').GameId} */ (info.game);
    this.port = info.port;
    this.pid = info.pid;
  }

  /**
   * @param {string} method
   * @param {Record<string, unknown>} [params]
   * @param {{timeout?: number, graceS?: number}} [opts]
   */
  call(method, params, opts) {
    return rpc(this.info, method, params, opts);
  }

  health() { return health(this.port); }
  ping() { return this.call('ping'); }
  state() { return this.call('state'); }
  methods() { return this.call('methods'); }
  /** @param {number} [limit] */
  saves(limit) { return this.call('saves.list', limit !== undefined ? { limit } : {}); }
  /** @param {string} name @param {{timeout?: number}} [opts] */
  load(name, opts = {}) { return this.call('save.load', { name }, { timeout: opts.timeout ?? 900 }); }
  /** cs2: playable maps. @param {number} [limit] */
  maps(limit) { return this.call('maps.list', limit !== undefined ? { limit } : {}); }
  /**
   * cs2: start a new city from a map; replies once it is loaded.
   * @param {string} map  map id or display name (see maps())
   * @param {{name?: string, theme?: string, options?: Record<string, boolean>, timeout?: number}} [opts]
   */
  newGame(map, opts = {}) {
    /** @type {Record<string, unknown>} */
    const p = { map };
    if (opts.name !== undefined) p.name = opts.name;
    if (opts.theme !== undefined) p.theme = opts.theme;
    if (opts.options !== undefined) p.options = opts.options;
    return this.call('game.new', p, { timeout: opts.timeout ?? 900 });
  }
  /** cs2: save the loaded city; the bridge only accepts names starting with "cs1conv-". @param {string} name @param {{timeout?: number}} [opts] */
  saveWrite(name, opts = {}) { return this.call('save.write', { name }, { timeout: opts.timeout ?? 300 }); }
  /** @param {{timeout?: number}} [opts] */
  menu(opts = {}) { return this.call('menu', {}, { timeout: opts.timeout ?? 300 }); }
  quit() { return this.call('quit', {}, { timeout: 15 }); }
  cameraGet() { return this.call('camera.get'); }
  /** @param {{x?: number, y?: number, z?: number, distance?: number, yaw?: number, pitch?: number, instant?: boolean}} cam */
  cameraSet(cam) { return this.call('camera.set', cam); }
  pause() { return this.call('sim.set', { paused: true }); }
  resume() { return this.call('sim.set', { paused: false }); }
  /** @param {number} speed */
  speed(speed) { return this.call('sim.set', { speed }); }
  /** @param {number} [frames] @param {number} [seconds] @param {{timeout?: number}} [opts] */
  wait(frames, seconds, opts = {}) {
    /** @type {Record<string, number>} */
    const p = {};
    if (frames !== undefined) p.frames = frames;
    if (seconds !== undefined) p.seconds = seconds;
    return this.call('wait', p, { timeout: opts.timeout ?? Math.max(60, (seconds ?? 0) + (frames ?? 0) / 10 + 30) });
  }

  /**
   * @param {string} file  absolute .png path (use resolveShotPath for defaults)
   * @param {{supersize?: number, timeout?: number}} [opts]
   */
  screenshot(file, opts = {}) {
    if (!path.isAbsolute(file)) throw new GamectlError('usage', `screenshot path must be absolute: ${file}`);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    /** @type {Record<string, unknown>} */
    const p = { path: file };
    if (opts.supersize !== undefined) p.supersize = opts.supersize;
    return this.call('screenshot', p, { timeout: opts.timeout ?? 60 });
  }

  /**
   * Hot-load and run a DLL entry point.
   * @param {string} dll  path, resolved to absolute
   * @param {{type?: string, method?: string, args?: unknown, phase?: 'main' | 'tool', pump?: 'main' | 'tool', timeout?: number}} [opts]
   */
  exec(dll, opts = {}) {
    const abs = path.resolve(dll);
    if (!fs.existsSync(abs)) throw new GamectlError('usage', `dll not found: ${abs}`);
    if (opts.phase !== undefined && this.game !== 'cs2') throw new GamectlError('usage', '--phase is only supported by the cs2 bridge');
    /** @type {Record<string, unknown>} */
    const p = { dll: abs };
    if (opts.type !== undefined) p.type = opts.type;
    if (opts.method !== undefined) p.method = opts.method;
    if (opts.args !== undefined) p.args = opts.args;
    if (opts.phase !== undefined) p.phase = opts.phase;
    if (opts.pump !== undefined) {
      if (this.game !== 'cs2') throw new GamectlError('usage', '--pump is only supported by the cs2 bridge');
      p.pump = opts.pump;
    }
    return this.call('exec', p, { timeout: opts.timeout });
  }
}

/**
 * Connect to a running bridge: discovery file → pid alive → /health answers with the same pid.
 * @param {string} game
 * @param {{dir?: string, isAlive?: (pid: number) => boolean, healthTimeoutMs?: number}} [opts]
 */
export async function connect(game, opts = {}) {
  const info = readDiscovery(checkGame(game), opts);
  const h = await health(info.port, { timeoutMs: opts.healthTimeoutMs });
  if (h.pid !== undefined && h.pid !== info.pid) {
    throw new GamectlError('not_running', `port ${info.port} answers for pid ${h.pid}, but the discovery file says pid ${info.pid} (stale file?)`);
  }
  if (h.game !== undefined && h.game !== game) {
    throw new GamectlError('not_running', `port ${info.port} answers as ${h.game}, not ${game}`);
  }
  return new Bridge(info);
}
