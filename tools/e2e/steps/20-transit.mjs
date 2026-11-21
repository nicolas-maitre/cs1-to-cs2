// e2e step plug-in: the terrain worker's transit lines (transit/, verified by them on 2026-11-21: 145/145 stops, 9/9
// lines). On by default, `--no-transit` skips it. Runs after the outside connections, i.e. after the buildings plug-in
// (stations and depots must exist first). `--transit-theme EU|NA` is passed on as --theme.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export default {
  name: 'transit',
  after: 'connections',
  enabled: (ctx) => fs.existsSync(path.join(ctx.repo, 'transit', 'transit.mjs')),
  async run(ctx) {
    execFileSync('dotnet', ['build', '-c', 'Release', '--nologo', '-v', 'q', path.join(ctx.repo, 'transit', 'cs2-exec')], { stdio: ['ignore', 'ignore', 'inherit'] });
    const { transit } = await import(pathToFileURL(path.join(ctx.repo, 'transit', 'transit.mjs')).href);
    const theme = ctx.opt('--transit-theme');
    return transit(['apply', '--plan', ctx.planFile, ...(theme ? ['--theme', theme] : [])]);
  },
};
