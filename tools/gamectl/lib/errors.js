// Error type shared by the module and the CLI. `code` picks the CLI exit code (see EXIT_CODES / README).

/** Exit codes of the CLI, by error code. */
export const EXIT_CODES = Object.freeze({
  ok: 0,
  error: 1, // anything else, including a bridge answering 400 (unknown method, handler threw, loading)
  usage: 2, // bad command line
  not_running: 3, // no game process / no discovery file / stale discovery file / bridge unreachable
  auth: 4, // 401 / 403 / 415
  timeout: 5, // 504 from the bridge, or no answer from it in time
  lifecycle: 6, // start / stop failed: Steam not running, game exited, ready marker never seen, process won't exit
});

export class GamectlError extends Error {
  /**
   * @param {keyof typeof EXIT_CODES} code
   * @param {string} message
   * @param {{status?: number, body?: any, executed?: boolean | 'unknown', cause?: unknown}} [extra]
   */
  constructor(code, message, extra = {}) {
    super(message, extra.cause !== undefined ? { cause: extra.cause } : undefined);
    this.name = 'GamectlError';
    this.code = code;
    /** HTTP status when the error came from the bridge. */
    this.status = extra.status;
    /** Parsed response body when the error came from the bridge. */
    this.body = extra.body;
    /** For timeouts: false = never ran, 'unknown' = timed out while running, undefined = n/a. */
    this.executed = extra.executed;
  }

  get exitCode() {
    return EXIT_CODES[this.code] ?? 1;
  }

  toJSON() {
    /** @type {Record<string, unknown>} */
    const o = { ok: false, code: this.code, error: this.message };
    if (this.status !== undefined) o.status = this.status;
    if (this.executed !== undefined) o.executed = this.executed;
    return o;
  }
}
