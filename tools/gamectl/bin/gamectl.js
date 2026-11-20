#!/usr/bin/env node
// gamectl CLI entry point. See ../README.md.
import { main } from '../lib/cli.js';

const ac = new AbortController();
process.on('SIGINT', () => {
  if (ac.signal.aborted) process.exit(130);
  ac.abort(); // ends `log --follow`; a second Ctrl+C exits immediately
});

const code = await main(process.argv.slice(2), {
  out: (s) => process.stdout.write(s + '\n'),
  err: (s) => process.stderr.write(s + '\n'),
  cwd: process.cwd(),
  signal: ac.signal,
});
process.exitCode = code;
