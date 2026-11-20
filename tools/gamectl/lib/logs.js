// Log helpers: tail the last lines of a file, follow files, and read only content written after a snapshot
// (used by `start` so a ready marker left by a previous run never counts).

import fs from 'node:fs';
import { sleep } from './proc.js';

const CHUNK = 1 << 20; // 1 MiB
const MAX_SCAN = 64 << 20; // never read more than 64 MiB backwards

/** @param {string} s */
const splitLines = (s) => s.split(/\r?\n/);

/**
 * Last `lines` lines of a file (optionally only the ones matching `grep`). Missing file → [].
 * Reads backwards in chunks, so huge logs are cheap.
 * @param {string} file
 * @param {{lines?: number, grep?: RegExp}} [opts]
 * @returns {string[]}
 */
export function tailFile(file, opts = {}) {
  const want = opts.lines ?? 50;
  if (want <= 0) return [];
  let fd;
  try {
    fd = fs.openSync(file, 'r');
  } catch {
    return [];
  }
  try {
    const size = fs.fstatSync(fd).size;
    let start = size;
    /** @type {Buffer[]} */
    let bufs = [];
    /** @type {string[]} */
    let lines = [];
    while (start > 0 && size - start < MAX_SCAN) {
      const n = Math.min(CHUNK, start);
      start -= n;
      const b = Buffer.alloc(n);
      fs.readSync(fd, b, 0, n, start);
      bufs.unshift(b);
      lines = splitLines(Buffer.concat(bufs).toString('utf8'));
      if (start > 0) lines.shift(); // first line may be cut in the middle
      if (lines.length && lines[lines.length - 1] === '') lines.pop();
      const count = opts.grep ? lines.filter((l) => opts.grep?.test(l)).length : lines.length;
      if (count >= want) break;
    }
    if (opts.grep) lines = lines.filter((l) => opts.grep?.test(l));
    return lines.slice(-want);
  } finally {
    fs.closeSync(fd);
  }
}

/**
 * @typedef {object} FileSnapshot
 * @property {boolean} exists
 * @property {number} size
 * @property {number} mtimeMs
 * @property {number} birthtimeMs
 */

/** @param {string} file @returns {FileSnapshot} */
export function snapshot(file) {
  try {
    const st = fs.statSync(file);
    return { exists: true, size: st.size, mtimeMs: st.mtimeMs, birthtimeMs: st.birthtimeMs };
  } catch {
    return { exists: false, size: 0, mtimeMs: 0, birthtimeMs: 0 };
  }
}

/**
 * Reads what gets appended to a file after a snapshot, line by line. Handles the game recreating or truncating the
 * file (Unity rewrites its logs on start): if the file shrank or its creation time changed, it restarts at offset 0,
 * because all content of a recreated file is new.
 */
export class LogCursor {
  /**
   * @param {string} file
   * @param {FileSnapshot} [since]  defaults to the file's current state (only future content counts)
   */
  constructor(file, since = snapshot(file)) {
    this.file = file;
    this.offset = since.exists ? since.size : 0;
    this.birthtimeMs = since.birthtimeMs;
    this.partial = '';
  }

  /** New complete lines since the last call. */
  read() {
    let fd;
    try {
      fd = fs.openSync(this.file, 'r');
    } catch {
      return [];
    }
    try {
      const st = fs.fstatSync(fd);
      const recreated = this.birthtimeMs !== 0 && st.birthtimeMs !== 0 && Math.abs(st.birthtimeMs - this.birthtimeMs) > 1;
      if (st.size < this.offset || recreated) {
        this.offset = 0;
        this.partial = '';
      }
      this.birthtimeMs = st.birthtimeMs;
      if (st.size === this.offset) return [];
      const n = st.size - this.offset;
      const b = Buffer.alloc(n);
      const got = fs.readSync(fd, b, 0, n, this.offset);
      this.offset += got;
      const lines = splitLines(this.partial + b.subarray(0, got).toString('utf8'));
      this.partial = lines.pop() ?? '';
      return lines;
    } finally {
      fs.closeSync(fd);
    }
  }

  /** The unterminated last line, for markers written without a trailing newline. */
  pending() {
    return this.partial;
  }
}

/**
 * Follow files like `tail -f`, calling `onLine(file, line)` for each new line until `signal` aborts.
 * @param {string[]} files
 * @param {(file: string, line: string) => void} onLine
 * @param {{signal?: AbortSignal, intervalMs?: number}} [opts]
 */
export async function followFiles(files, onLine, opts = {}) {
  const cursors = files.map((f) => new LogCursor(f));
  while (!opts.signal?.aborted) {
    for (const c of cursors) for (const l of c.read()) onLine(c.file, l);
    await sleep(opts.intervalMs ?? 250);
  }
}
