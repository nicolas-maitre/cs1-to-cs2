// A fake game bridge implementing tools/PROTOCOL.md, for gamectl's tests.
//
// As a module: `startFakeBridge({game, dir})` serves /health and /rpc on an ephemeral 127.0.0.1 port and writes
// `<dir>/<game>.json`. As a script it plays a whole fake game process:
//
//   node fake-bridge.js --game cs2 --dir <bridgeDir> --log <markerLog> [--marker "MainMenu reached"]
//                       [--marker-delay ms] [--bridge-delay ms] [--truncate] [--no-marker] [--no-bridge]
//
// It optionally truncates the log (like Unity recreating it), appends the marker after --marker-delay, starts the
// bridge after --bridge-delay with its own pid, and exits on rpc `quit`. It also writes `<log>.env.json` with the
// SteamAppId/SteamGameId it saw and its argv.

import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

/** @typedef {(params: any, ctx: {timeout_s: number, bridge: FakeBridge}) => any} Handler */

/**
 * @typedef {object} FakeBridge
 * @property {string} game
 * @property {number} port
 * @property {string} token
 * @property {number} pid
 * @property {string} file          discovery file
 * @property {any} state            mutable state object
 * @property {{method: string, url: string, headers: http.IncomingHttpHeaders, body: any}[]} requests
 * @property {Record<string, Handler>} handlers
 * @property {() => Promise<void>} close
 */

const sleep = (/** @type {number} */ ms) => new Promise((r) => setTimeout(r, ms));
class RpcError extends Error {}

/** 1×1 transparent PNG */
const PNG = Buffer.from('89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000d4944415478da63fcffff3f0300050001fe0a2d8a740000000049454e44ae426082', 'hex');

/**
 * @param {{game?: string, dir: string, pid?: number, token?: string, writeDiscovery?: boolean, handlers?: Record<string, Handler>, onQuit?: () => void}} opts
 * @returns {Promise<FakeBridge>}
 */
export async function startFakeBridge(opts) {
  const game = opts.game ?? 'cs2';
  const pid = opts.pid ?? process.pid;
  const token = opts.token ?? crypto.randomBytes(24).toString('hex');
  let frame = 0;
  const state = {
    game, mode: 'menu', loading: false, city_loaded: false, city_name: null, save_name: null, paused: false, speed: 1, frame: 0, bridge_version: '0.0.0-fake',
  };
  const ticker = setInterval(() => { state.frame = ++frame; }, 5);
  let camera = { x: 0, y: 0, z: 0, distance: 1000, yaw: 0, pitch: 45 };

  /** @type {Record<string, Handler>} */
  const handlers = {
    ping: () => ({ ...state }),
    state: () => ({ ...state }),
    methods: () => Object.keys(handlers).map((name) => ({ name, help: `fake ${name}`, while_loading: true })),
    wait: async (p) => {
      const frames = p.frames ?? 0;
      const f0 = frame;
      const t0 = Date.now();
      while (frame - f0 < frames || (Date.now() - t0) / 1000 < (p.seconds ?? 0)) await sleep(5);
      return { frames: frame - f0, seconds: (Date.now() - t0) / 1000 };
    },
    exec: (p) => {
      if (!p.dll || !path.isAbsolute(p.dll)) throw new RpcError('dll must be an absolute path');
      return { echo: p };
    },
    screenshot: (p) => {
      if (!p.path || !path.isAbsolute(p.path) || !p.path.endsWith('.png')) throw new RpcError('path must be an absolute .png');
      fs.writeFileSync(p.path, PNG);
      return { path: p.path, bytes: PNG.length, frames: 2 };
    },
    'saves.list': (p) => ({ saves: [{ name: 'Alpha', city: 'Alpha City', modified: '2026-11-20T10:00:00Z' }, { name: 'Beta', city: 'Beta', modified: '2026-11-19T10:00:00Z', autosave: true }].slice(0, p.limit ?? 50) }),
    'save.load': async (p) => {
      if (!['Alpha', 'Beta'].includes(p.name)) throw new RpcError(`no save named ${p.name}`);
      Object.assign(state, { loading: true, mode: 'loading' });
      await sleep(50);
      Object.assign(state, { loading: false, mode: 'game', city_loaded: true, city_name: `${p.name} City`, save_name: p.name });
      return { loaded: p.name, city: state.city_name };
    },
    menu: () => {
      Object.assign(state, { mode: 'menu', city_loaded: false, city_name: null, save_name: null });
      return { menu: true };
    },
    quit: () => {
      setTimeout(() => opts.onQuit?.(), 50);
      return { quitting: true };
    },
    'camera.get': () => ({ ...camera }),
    'camera.set': (p) => {
      for (const k of ['x', 'y', 'z', 'distance', 'yaw', 'pitch']) if (typeof p[k] === 'number') /** @type {any} */ (camera)[k] = p[k];
      return { ...camera };
    },
    'sim.set': (p) => {
      if (typeof p.paused === 'boolean') state.paused = p.paused;
      if (typeof p.speed === 'number') state.speed = p.speed;
      return { paused: state.paused, speed: state.speed };
    },
    // test helpers: sleep `ms` while "running" (→ 504 executed:"unknown" if timeout_s is shorter)
    'test.sleep': async (p) => { await sleep(p.ms ?? 1000); return { slept: p.ms }; },
    ...(opts.handlers ?? {}),
  };
  /** @type {FakeBridge['requests']} */
  const requests = [];
  /** methods queued but never started (simulates a blocked main thread → 504 executed:false) */
  const blocked = new Set(['test.block']);

  const server = http.createServer(async (req, res) => {
    const chunks = [];
    for await (const c of req) chunks.push(c);
    const text = Buffer.concat(chunks).toString('utf8');
    /** @param {number} status @param {any} body */
    const send = (status, body) => {
      res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', Connection: 'close' });
      res.end(JSON.stringify(body));
    };
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { /* keep null */ }
    requests.push({ method: req.method ?? '', url: req.url ?? '', headers: req.headers, body });
    if (req.headers.origin || req.headers['sec-fetch-site'] || req.headers['sec-fetch-mode']) return send(403, { ok: false, error: 'browser requests are refused' });
    if (req.method === 'GET' && req.url === '/health') {
      return send(200, { ok: true, game, bridge_version: state.bridge_version, protocol: 1, pid, status_age_ms: 10, status: { ...state } });
    }
    if (req.method !== 'POST' || req.url !== '/rpc') return send(404, { ok: false, error: `unknown endpoint ${req.method} ${req.url}` });
    if (req.headers.authorization !== `Bearer ${token}`) return send(401, { ok: false, error: 'missing or wrong token' });
    if (!String(req.headers['content-type'] ?? '').startsWith('application/json')) return send(415, { ok: false, error: 'Content-Type must be application/json' });
    if (!body || typeof body.method !== 'string') return send(400, { ok: false, error: 'missing method' });
    let timeout = Number(body.timeout_s ?? 60);
    if (!(timeout > 0)) timeout = 60;
    timeout = Math.min(timeout, 3600);
    if (body.method === 'test.silent') return; // never answers, ignores timeout_s (a hung bridge)
    if (blocked.has(body.method)) {
      await sleep(timeout * 1000);
      return send(504, { ok: false, executed: false, error: `timed out after ${timeout} s waiting for the game's main thread; the command was NOT run` });
    }
    const h = handlers[body.method];
    if (!h) return send(400, { ok: false, error: `unknown method ${body.method}` });
    const work = Promise.resolve().then(() => h(body.params ?? {}, { timeout_s: timeout, bridge: fake }));
    /** @type {NodeJS.Timeout | undefined} */
    let t;
    const timer = new Promise((resolve) => { t = setTimeout(() => resolve('__timeout__'), timeout * 1000); });
    try {
      const r = await Promise.race([work, timer]);
      if (r === '__timeout__') return send(504, { ok: false, executed: 'unknown', error: `timed out after ${timeout} s while the command was running` });
      return send(200, { ok: true, result: r });
    } catch (e) {
      return send(400, { ok: false, error: /** @type {Error} */ (e).message });
    } finally {
      clearTimeout(t);
      work.catch(() => {});
    }
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(undefined)));
  const port = /** @type {import('node:net').AddressInfo} */ (server.address()).port;
  const file = path.join(opts.dir, `${game}.json`);
  if (opts.writeDiscovery !== false) {
    fs.mkdirSync(opts.dir, { recursive: true });
    const tmp = file + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify({ game, port, token, pid, bridge_version: state.bridge_version, protocol: 1, started: new Date().toISOString() }));
    fs.renameSync(tmp, file);
  }

  /** @type {FakeBridge} */
  const fake = {
    game, port, token, pid, file, state, requests, handlers,
    close: () => new Promise((resolve) => {
      clearInterval(ticker);
      server.closeAllConnections?.();
      server.close(() => resolve());
    }),
  };
  return fake;
}

/** Script mode: a fake game process. */
async function fakeGame() {
  const args = process.argv.slice(2);
  /** @param {string} name */
  const get = (name) => { const i = args.indexOf(`--${name}`); return i >= 0 ? args[i + 1] : undefined; };
  const has = (/** @type {string} */ name) => args.includes(`--${name}`);
  const game = get('game') ?? 'cs2';
  const dir = /** @type {string} */ (get('dir'));
  const log = /** @type {string} */ (get('log'));
  const marker = get('marker') ?? (game === 'cs2' ? 'MainMenu reached' : 'Scene [MainMenu] loaded');
  fs.writeFileSync(`${log}.env.json`, JSON.stringify({ SteamAppId: process.env.SteamAppId, SteamGameId: process.env.SteamGameId, CS1TOCS2_GAMECTL: process.env.CS1TOCS2_GAMECTL, argv: args, pid: process.pid }));
  if (has('truncate')) fs.writeFileSync(log, 'new run\n');
  else fs.appendFileSync(log, `[${process.pid}] starting\n`);
  const keepAlive = setInterval(() => {}, 1 << 30);
  const quit = () => { clearInterval(keepAlive); process.exit(0); };
  const markerDelay = Number(get('marker-delay') ?? 0);
  const bridgeDelay = Number(get('bridge-delay') ?? 0);
  if (!has('no-marker')) {
    setTimeout(() => {
      fs.appendFileSync(log, `[${process.pid}] ${marker}\n`);
      fs.writeFileSync(`${log}.marker-at`, String(Date.now()));
    }, markerDelay);
  }
  if (!has('no-bridge')) {
    await sleep(bridgeDelay);
    await startFakeBridge({ game, dir, onQuit: has('ignore-quit') ? undefined : quit });
  }
  if (has('exit-now')) quit();
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url) && process.argv.includes('--game')) {
  fakeGame().catch((e) => { console.error(e); process.exit(1); });
}
