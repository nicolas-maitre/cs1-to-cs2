// Game constants and path resolution: install folders (local.props / env), log files, discovery dir, screenshots.

import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { GamectlError } from './errors.js';

/** @typedef {'cs1' | 'cs2'} GameId */

/**
 * Everything gamectl needs to know about one game. `gameConfig()` builds it; tests override fields.
 * @typedef {object} GameConfig
 * @property {GameId} game
 * @property {string} appId           Steam app id, passed as SteamAppId / SteamGameId
 * @property {string} image           process image name (tasklist), e.g. Cities2.exe
 * @property {string | null} installDir
 * @property {string | null} exe      absolute exe path (null when the install folder is unknown)
 * @property {string[]} args          fixed launch args (CS2: -developerMode)
 * @property {number} port            the bridge's default port (informational: clients use the discovery file's port)
 * @property {string | null} markerFile  log file that receives the ready marker
 * @property {RegExp} markerRe        ready marker
 * @property {string} bridgeDir       folder holding <game>.json discovery files
 * @property {() => string[]} logFiles default files for `gamectl log`
 * @property {string | null} logDir   folder that relative `log --file` names resolve against
 */

const HERE = path.dirname(fileURLToPath(import.meta.url));

/** Repository root (tools/gamectl/lib → ../../..), overridable with GAMECTL_REPO_ROOT. */
export function repoRoot() {
  return process.env.GAMECTL_REPO_ROOT ? path.resolve(process.env.GAMECTL_REPO_ROOT) : path.resolve(HERE, '..', '..', '..');
}

/** @param {string} game @returns {GameId} */
export function checkGame(game) {
  if (game === 'cs1' || game === 'cs2') return game;
  throw new GamectlError('usage', `unknown game "${game}" (expected cs1 or cs2)`);
}

/** Folder of the discovery files: GAMECTL_BRIDGE_DIR, else %LOCALAPPDATA%\cs1-to-cs2\bridge. */
export function bridgeDir() {
  if (process.env.GAMECTL_BRIDGE_DIR) return path.resolve(process.env.GAMECTL_BRIDGE_DIR);
  const local = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  return path.join(local, 'cs1-to-cs2', 'bridge');
}

/**
 * Parse `local.props` (MSBuild XML) into a {name: value} map of its simple properties.
 * @param {string} [file]
 * @returns {Record<string, string>}
 */
export function readLocalProps(file = path.join(repoRoot(), 'local.props')) {
  /** @type {Record<string, string>} */
  const props = {};
  let text;
  try {
    text = fs.readFileSync(file, 'utf8');
  } catch {
    return props;
  }
  text = text.replace(/<!--[\s\S]*?-->/g, '');
  for (const m of text.matchAll(/<([A-Za-z_][\w.-]*)(?:\s[^>]*)?>([^<]*)<\/\1>/g)) props[m[1]] = m[2].trim();
  return props;
}

/**
 * Install folder of a game: env CS1_DIR / CS2_DIR, else local.props `CS?Managed` two levels up.
 * @param {GameId} game
 * @param {Record<string, string>} [props]
 * @returns {string | null}
 */
export function installDir(game, props) {
  const envName = game === 'cs1' ? 'CS1_DIR' : 'CS2_DIR';
  if (process.env[envName]) return path.resolve(process.env[envName]);
  const managed = (props ?? readLocalProps())[game === 'cs1' ? 'CS1Managed' : 'CS2Managed'];
  return managed ? path.resolve(managed, '..', '..') : null;
}

/** CS2's per-user data folder (Player.log, Logs\). Overridable with GAMECTL_CS2_DATA_DIR. */
export function cs2DataDir() {
  if (process.env.GAMECTL_CS2_DATA_DIR) return path.resolve(process.env.GAMECTL_CS2_DATA_DIR);
  const home = process.env.USERPROFILE || os.homedir();
  return path.join(home, 'AppData', 'LocalLow', 'Colossal Order', 'Cities Skylines II');
}

/** CS2 files under Logs\ that matter for the bridge: scene flow, the bridge's own log, mod loading. */
const CS2_BRIDGE_LOG_RE = /^(SceneFlow|.*Bridge.*|Modding)\.log$/i;

/**
 * @param {GameId} game
 * @param {Partial<GameConfig>} [overrides]
 * @returns {GameConfig}
 */
export function gameConfig(game, overrides = {}) {
  checkGame(game);
  const dir = overrides.installDir !== undefined ? overrides.installDir : installDir(game);
  /** @type {GameConfig} */
  let cfg;
  if (game === 'cs2') {
    const data = cs2DataDir();
    const logs = path.join(data, 'Logs');
    cfg = {
      game,
      appId: '949230',
      image: 'Cities2.exe',
      installDir: dir,
      exe: dir ? path.join(dir, 'Cities2.exe') : null,
      args: ['-developerMode'],
      port: 17760,
      markerFile: path.join(logs, 'SceneFlow.log'),
      markerRe: /MainMenu reached/,
      bridgeDir: bridgeDir(),
      logDir: logs,
      logFiles: () => {
        const files = [path.join(data, 'Player.log')];
        try {
          for (const f of fs.readdirSync(logs).sort()) if (CS2_BRIDGE_LOG_RE.test(f)) files.push(path.join(logs, f));
        } catch { /* no Logs folder yet */ }
        return files;
      },
    };
  } else {
    const log = dir ? path.join(dir, 'Cities_Data', 'output_log.txt') : null;
    cfg = {
      game,
      appId: '255710',
      image: 'Cities.exe',
      installDir: dir,
      exe: dir ? path.join(dir, 'Cities.exe') : null,
      args: [],
      port: 17761,
      markerFile: log,
      markerRe: /Scene \[MainMenu\] loaded/,
      bridgeDir: bridgeDir(),
      logDir: dir ? path.join(dir, 'Cities_Data') : null,
      logFiles: () => (log ? [log] : []),
    };
  }
  return { ...cfg, ...overrides };
}

/** @param {Date} d */
export function timestamp(d = new Date()) {
  const p = (/** @type {number} */ n, w = 2) => String(n).padStart(w, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}-${p(d.getMilliseconds(), 3)}`;
}

/**
 * Absolute .png path for a screenshot. No file → `<repo>/.gamectl/shots/<game>-<timestamp>.png`;
 * a relative file resolves against `cwd`; a missing .png extension is added; a trailing slash / existing
 * folder gets the default file name inside it.
 * @param {GameId} game
 * @param {string | undefined} file
 * @param {{cwd?: string, root?: string, now?: Date}} [opts]
 */
export function resolveShotPath(game, file, opts = {}) {
  const name = `${game}-${timestamp(opts.now)}.png`;
  if (!file) return path.join(opts.root ?? repoRoot(), '.gamectl', 'shots', name);
  let p = path.resolve(opts.cwd ?? process.cwd(), file);
  const isDir = /[\\/]$/.test(file) || (fs.existsSync(p) && fs.statSync(p).isDirectory());
  if (isDir) return path.join(p, name);
  if (path.extname(p).toLowerCase() !== '.png') p += '.png';
  return p;
}
